using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shiki.Core;
using Shiki.Core.Ai;
using Shiki.Core.Meetings;
using Shiki.Core.Voice;
using Shiki.Shell;
using Shiki.UI;
using Shiki.UI.Dashboard;
using Velopack;

namespace Shiki;

/// <summary>
/// Command line:
///   Shiki                      desktop buddy + tray icon
///   Shiki run &lt;action&gt; files   run an action (what the Explorer menu calls)
///   Shiki menu files           show the radial menu at the cursor
///   Shiki files...             same as menu (drag files onto the exe, "Open with")
///   Shiki --register / --unregister   add or remove the Explorer menu
/// </summary>
public partial class App : Application
{
    // Explorer starts one process per selected file; collect them for a moment so a
    // 10-file selection becomes one job (and one merged PDF), not ten.
    static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(700);

    SingleInstance? _instance;
    Settings _settings = new();
    CatWindow? _buddy;
    TrayIcon? _tray;
    GlobalMouseHook? _hook;
    ConvertWheel? _dragWheel;
    BroMenu? _menu;
    Hotkeys? _hotkeys;
    int _snipHotkeyId = -1, _colourHotkeyId = -1, _convertHotkeyId = -1, _recordHotkeyId = -1, _askHotkeyId = -1, _talkHotkeyId = -1, _pawHotkeyId = -1, _errorHotkeyId = -1, _notesHotkeyId = -1, _dictateHotkeyId = -1;
    RecordingSession? _recording;
    SnipWindow? _snip;
    PawWindow? _paw;
    ColourPickerWindow? _picker;
    Point _menuAnchor;
    bool _headless;
    readonly Dictionary<string, List<string>> _pending = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _batchTimer = new() { Interval = BatchWindow };
    readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    static string ExePath => Environment.ProcessPath!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Formerly Converted Bro: its data folder moves here before anything opens it.
        FormerName.MoveData();
        // The installer (Velopack) runs the app with special arguments around install, update and
        // uninstall; these hooks do their bit and exit. A normal start goes straight through.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ =>
            {
                StartupRegistration.Set(true, ExePath);
                ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(CatSprites.Skin(Settings.Load().Cat)));
            })
            .OnAfterUpdateFastCallback(_ =>
            {
                if (ContextMenuRegistrar.IsRegistered()) ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(CatSprites.Skin(Settings.Load().Cat)));
                if (StartupRegistration.IsEnabled()) StartupRegistration.Set(true, ExePath);
            })
            .OnBeforeUninstallFastCallback(_ =>
            {
                // Leave nothing pointing at the removed exe. Settings, history and models stay.
                ContextMenuRegistrar.Unregister();
                StartupRegistration.Set(false, ExePath);
            })
            .Run();
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled UI error", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Unhandled error", ex.ExceptionObject as Exception);

        var args = e.Args;
        if (args.Length == 2 && args[0] == "--export-sprites")
        {
            CatSprites.Export(args[1]);
            Shutdown();
            return;
        }
        if (args.Length == 2 && args[0] == "--cutout-test")
        {
            // Dev aid: remove the background of every photo in a folder; timings go to the log.
            Directory.CreateDirectory(Path.Combine(args[1], "out"));
            foreach (var path in Directory.GetFiles(args[1], "*.jpg"))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var photo = ImageTools.Load(path);
                using var cut = Cutout.Remove(photo);
                Cutout.WritePng(cut, Path.Combine(args[1], "out", Path.GetFileNameWithoutExtension(path) + ".png"));
                Log.Info($"cutout-test {Path.GetFileName(path)} {photo.Width}x{photo.Height}: {sw.ElapsedMilliseconds} ms on {Cutout.Device}");
            }
            Shutdown();
            return;
        }
        if (args.Length is 3 or 4 && args[0] == "--form-test")
        {
            // Dev aid: auto-crop and produce a form photo; crop and result go to the log.
            var preset = FormPhotos.Presets.First(p => p.Id == args[1]);
            using var photo = ImageTools.Load(args[2]);
            var crop = System.Threading.Tasks.Task.Run(() => FormPhotos.SuggestCrop(photo, preset)).GetAwaiter().GetResult();
            var made = FormPhotos.Produce(args[2], crop, preset, whiteBackground: args.Length == 4 && args[3] == "white");
            using var check = new ImageMagick.MagickImage(made);
            Log.Info($"form-test {preset.Id}: crop {crop}, out {check.Width}x{check.Height} {check.Density} {new FileInfo(made).Length / 1024} KB -> {made}");
            Shutdown();
            return;
        }
        if (args.Length == 3 && args[0] == "--sign-test")
        {
            // Dev aid: import a signature photo and stamp it bottom-right on page 1.
            SignatureStore.ImportPhoto(args[2]);
            using (var doc = PdfSharp.Pdf.IO.PdfReader.Open(args[1], PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify))
            using (var image = PdfSharp.Drawing.XImage.FromFile(SignatureStore.Path))
            using (var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(doc.Pages[0], PdfSharp.Drawing.XGraphicsPdfPageOptions.Append))
            {
                var size = gfx.PageSize;
                double w = size.Width * 0.3, h = w * image.PixelHeight / image.PixelWidth;
                gfx.DrawImage(image, size.Width * 0.62, size.Height * 0.78, w, h);
                gfx.Dispose();
                doc.Save(OutputPaths.Next(args[1], ".pdf", "signed"));
            }
            Shutdown();
            return;
        }
        if (args.Length == 2 && args[0] == "--pdf-test")
        {
            // Dev aid: lock, check passwords, unlock and number a PDF; results go to the log.
            PdfFonts.Install();
            var none = new Progress<double>();
            var locked = PdfSecurity.Lock(args[1], "test123");
            Log.Info($"pdf-test: locked {locked}; plain={PdfSecurity.TryPassword(args[1], "x")} wrong={PdfSecurity.TryPassword(locked, "nope")} right={PdfSecurity.TryPassword(locked, "test123")}");
            Log.Info($"pdf-test: unlocked {PdfSecurity.Unlock(locked, "test123", none)}");
            Log.Info($"pdf-test: numbered {PdfSecurity.AddPageNumbers(args[1], none)}");
            Shutdown();
            return;
        }
        if (args.Length == 2 && args[0] == "--export-icon")
        {
            // Dev aid: the app icon (Mochi) for the exe and the installer.
            BroArt.WriteIcon(CatSprites.Skin("mochi"), args[1]);
            Shutdown();
            return;
        }
        if (args.Length == 3 && args[0] == "--record-test")
        {
            // Dev aid: record a 640x360 corner of the main screen for N seconds, with system sound.
            var seconds = int.Parse(args[1]);
            var output = args[2];
            System.Threading.Tasks.Task.Run(async () =>
            {
                using var recorder = new ScreenRecorder((0, 0, 640, 360), mic: false, sound: true);
                await recorder.StartAsync();
                await System.Threading.Tasks.Task.Delay(seconds * 1000);
                await recorder.StopAsync(output);
            }).GetAwaiter().GetResult();
            Log.Info($"record-test saved {output}");
            Shutdown();
            return;
        }
        if (args.Length == 1 && args[0] == "--desktop-selection-test")
        {
            // Dev aid: what's selected on the desktop, read the way the convert shortcut reads it.
            Log.Info($"desktop selection: {string.Join(" | ", ExplorerSelection.FromDesktop())}");
            Shutdown();
            return;
        }
        if (args.Length == 3 && args[0] == "--qr-test")
        {
            // Dev aid: make a QR code, then read it back as is, small and inverted on a busy picture.
            File.WriteAllBytes(args[2], QrTools.MakePng(args[1]));
            using var plain = new ImageMagick.MagickImage(args[2]);
            Log.Info($"qr-test plain: {string.Join(" | ", QrTools.Read(plain))}");
            using var scene = new ImageMagick.MagickImage("plasma:", 1600, 1000);
            using var small = (ImageMagick.MagickImage)plain.Clone();
            small.Negate();
            small.Resize(new ImageMagick.Percentage(35));
            small.Rotate(8);
            scene.Composite(small, 700, 400, ImageMagick.CompositeOperator.Over);
            scene.Blur(0, 0.8);
            scene.Write(Path.ChangeExtension(args[2], ".scene.png"));
            Log.Info($"qr-test scene: {string.Join(" | ", QrTools.Read(scene))}");
            Shutdown();
            return;
        }
        if (args.Length == 2 && args[0] == "--scan-test")
        {
            // Dev aid: auto-detect the page and save it with every filter, plus the detected corners.
            using var photo = ImageTools.Load(args[1]);
            var corners = ScanTools.DetectPage(photo);
            File.WriteAllText(args[1] + ".corners.txt", string.Join(" ", corners.Select(c => $"{c.X:0},{c.Y:0}")));
            foreach (var filter in Enum.GetValues<ScanFilter>())
            {
                using var page = ScanTools.Process(photo, corners, 0, filter);
                page.Quality = 88;
                page.Write(OutputPaths.Next(args[1], ".jpg", $"scan {filter}"));
            }
            Shutdown();
            return;
        }
        if (args.Length > 0 && args[0] is "--register" or "--unregister")
        {
            if (args[0] == "--register") ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(CatSprites.Skin(Settings.Load().Cat)));
            else ContextMenuRegistrar.Unregister();
            Shutdown();
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsPrimary)
        {
            if (!SingleInstance.SendToPrimary(args.Length == 0 ? ["show"] : args))
                Log.Error($"Could not reach the running instance; dropped: {string.Join(" ", args)}");
            Shutdown();
            return;
        }

        _instance.MessageReceived += a => Dispatcher.BeginInvoke(() => Handle(a));
        _instance.StartServer();
        _settings = Settings.Load();
        // There used to be one name for every cat; now each has its own, and the old one goes to the cat on the desktop.
        if (_settings.CatNames.Count == 0 && !string.IsNullOrWhiteSpace(_settings.CatName))
        {
            _settings.CatNames[_settings.Cat] = _settings.CatName.Trim();
            _settings.Save();
        }
        Theme.EditorDark = _settings.DashboardDark ?? DashPalette.WindowsIsDark();
        SmoothScroll.Install();
        OutputPaths.Folder = _settings.OutputFolder;
        OutputPaths.SortByType = _settings.SortOutputs;
        FormerName.MoveTheRest(_settings, ExePath, BroArt.EnsureIconFile(Skin));
        Theme.Style = _settings.Style;
        // Page numbers and other PDF text use the fonts installed in Windows.
        PdfFonts.Install();
        LiveGlass.Enabled = _settings.LiveGlass;
        _batchTimer.Tick += (_, _) => FlushBatches();

        JobRunner.JobStarted += job => Dispatcher.BeginInvoke(() =>
        {
            ToastHost.Show(new ToastWindow(job));
            _buddy?.SetBusy(JobRunner.ActiveCount);
            ReactToSize(job.Inputs);
        });
        JobRunner.JobFinished += job => Dispatcher.BeginInvoke(() =>
        {
            _buddy?.SetBusy(JobRunner.ActiveCount);
            if (job.State == JobState.Failed) History.Add(job);
            if (job.State != JobState.Done) return;
            bool milestone = RecordStats(job);
            History.Add(job);
            if (milestone) _buddy?.Party();
            else _buddy?.Celebrate();
            CheckLevel();
            OfferHabit();
            SavedQuip(job);
        });

        if (args.Length == 0 || args[0] == "--background")
        {
            EnterBuddyMode();
            Names.Load();
            // Older "Start with Windows" entries had no flag, and would open the dashboard at sign-in.
            if (StartupRegistration.IsEnabled()) StartupRegistration.Set(true, ExePath);
        }
        else
        {
            // Launched from Explorer: do the work quietly and exit when idle.
            _headless = true;
            _idleTimer.Tick += (_, _) => ExitIfIdle();
            _idleTimer.Start();
        }
        Handle(args);
    }

    void Handle(string[] args)
    {
        if (args.Length > 0 && args[0] == "--background")
        {
            // Signing in to Windows: just the cat, no window.
            EnterBuddyMode();
            return;
        }
        if (args.Length == 0 || args[0] == "show")
        {
            EnterBuddyMode();
            SetBuddyVisible(true);
            ShowDashboard();
            return;
        }

        if (HandleShotVerb(args)) return;
        switch (args[0])
        {
            case "run" when args.Length >= 3:
                Queue(args[1], args[2..]);
                break;
            case "recent-test":
                // Dev aid: what a new conversation is told about lately.
                Log.Info("recent-test: " + Core.Ai.Recent.Summary().Replace("\n", " | "));
                break;
            case "say-test" when args.Length >= 2:
                // Dev aid: the cat says this, the normal way (first-sound time goes to the log).
                _ = Mouth.Say(string.Join(" ", args[1..]), _settings.TalkVoice);
                break;
            case "lookup-test" when args.Length >= 2:
                // Dev aid: finds a song's Spotify link the quick way (nothing plays); the answer goes to the log.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var watch = Stopwatch.StartNew();
                    var found = await Spotify.Lookup(string.Join(" ", args[1..]), default);
                    Log.Info($"lookup-test \"{string.Join(" ", args[1..])}\": {(found == null ? "nothing" : $"{found.Label} {found.Uri}")} in {watch.ElapsedMilliseconds} ms");
                });
                break;
            case "names-test":
                // Dev aid: contact names fetched, the words Deepgram is given, sounds-like and corrections (read-only, except "learn").
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await People.RefreshContacts(force: true);
                    Log.Info("names-test: for speech: " + string.Join(", ", People.ForSpeech(30)));
                    foreach (var heardName in new[] { "torsive", "Tosef", "Tauzif", "yesh", "mommy", "kumar" })
                        Log.Info($"names-test: \"{heardName}\" sounds like: {string.Join(" | ", People.SoundsLike(heardName))}");
                    if (args.Length > 2 && args[1] == "learn") People.Learn(args[2], args[3]);
                    Log.Info($"names-test: fix \"send the photos to torsive\" -> \"{People.Fix("send the photos to torsive")}\"");
                    Log.Info($"names-test: known tausif@example.com={People.Known("tausif@example.com")} 919000000001={People.Known("919000000001")} someone@new.com={People.Known("someone@new.com")}");
                });
                break;
            case "whatsapp-read-test":
                // Dev aid: opens WhatsApp Web on a chat (yours, if given a number) and only READS the page:
                // the tab, "Use here", the message box. Nothing is typed, pasted or sent.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    Log.Info($"whatsapp-read-test: app installed = {WhatsAppWeb.AppInstalled()}");
                    if (args.Length > 1) Dispatcher.Invoke(() => OpenWhatsAppWeb($"https://web.whatsapp.com/send?phone={new string(args[1].Where(char.IsDigit).ToArray())}"));
                    var watch = Stopwatch.StartNew();
                    var chat = await WhatsAppWeb.WaitForChat(TimeSpan.FromSeconds(40), step => Log.Info($"whatsapp-read-test: {step} ({watch.ElapsedMilliseconds} ms)"));
                    if (chat is { } c)
                    {
                        Log.Info($"whatsapp-read-test: message box \"{c.Box.Current.Name}\" found after {watch.ElapsedMilliseconds} ms; window \"{c.Window.Current.Name}\"");
                        Log.Info($"whatsapp-read-test: Send button now = {await WhatsAppWeb.SendButton(c.Window) != null}, preview up = {await WhatsAppWeb.PreviewUp(c.Window)}");
                    }
                });
                break;
            case "compare-clips":
                // Dev aid: the kept voice clips (newest first, or the newest N) through Deepgram's careful mode,
                // ElevenLabs Scribe v2 and Whisper on this PC, next to what was heard live. Writes voice-clips\compare.md.
                _ = CompareClips(args.Length > 1 && int.TryParse(args[1], out var howMany) ? howMany : 30);
                break;
            case "scribe-file" when args.Length == 2:
                // Dev aid: stream a WAV to ElevenLabs Scribe live, as the microphone would; partials and the end go to the log.
                EnterBuddyMode();
                _ = ScribeFile(args[1]);
                break;
            case "scribe-lang-test" when args.Length == 2:
                // Dev aid: one recording through Scribe with each language setting (whole and live); answers go to the log.
                _ = ScribeLanguageTest(args[1]);
                break;
            case "deepgram-terms-test":
                // Dev aid: opens Deepgram's live stream with more and more of the words to expect, to find one it refuses.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var terms = Speech.KeyTerms(CatName).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(90).ToList();
                    Log.Info($"deepgram-terms-test: {terms.Count} terms, query {Deepgram.Query(terms).Length} characters");
                    async System.Threading.Tasks.Task<bool> Opens(List<string> some)
                    {
                        try { await using var live = await DeepgramLive.Open(Deepgram.Key.Read()!, 16000, some, default); return true; }
                        catch (Exception) { return false; }
                    }
                    foreach (var n in new[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90 })
                    {
                        if (n > terms.Count) break;
                        bool ok = await Opens(terms.Take(n).ToList());
                        Log.Info($"deepgram-terms-test: first {n}: {(ok ? "opens" : "refused")}");
                        if (!ok)
                        {
                            foreach (var term in terms.Take(n).Skip(Math.Max(0, n - 10)))
                                Log.Info($"deepgram-terms-test:   alone \"{term}\": {(await Opens([term]) ? "fine" : "REFUSED")}");
                            break;
                        }
                    }
                });
                break;
            case "script-test":
                // Dev aid: what different scripts become in English letters (answers go to the log).
                foreach (var heardText in new[] { "కేక్ నంబర్ వై", "نمسطو ته বাই seedhe maut",
                    "भाई, जाना ही ले रहा है?", "Can you open Spotify and play Kesariya?", "نمسطو ته" })
                    Log.Info($"script-test \"{heardText}\" -> \"{Hinglish.LatinOnly(heardText)}\"");
                break;
            case "deepgram-warm":
                // Dev aid: opens the Deepgram connection, as speech starting does for "Hey Sky".
                Deepgram.Warm();
                break;
            case "eleven-warm":
                // Dev aid: opens the ElevenLabs connection, as starting to talk does.
                ElevenLabs.Warm();
                break;
            case "say-with" when args.Length >= 3:
                // Dev aid: the cat says this with a given voice id (e.g. a broken one, to see the fallback).
                _ = Mouth.Say(string.Join(" ", args[2..]), args[1]);
                break;
            case "say-timing":
                // Dev aid: how long the cat's voice takes, step by step (it plays out loud).
                _ = SayTiming();
                break;
            case "cat-menu" when args.Length == 2:
                // Dev aid: the cat's right-click menu, shown for a moment and saved as a picture.
                EnterBuddyMode();
                ShowMenu(new Point(900, 300));
                Dispatcher.BeginInvoke(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(900);
                    if (_menu?.Content is FrameworkElement shown)
                    {
                        var dpi = VisualTreeHelper.GetDpi(shown);
                        var bitmap = new RenderTargetBitmap((int)(shown.ActualWidth * dpi.DpiScaleX), (int)(shown.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        bitmap.Render(shown);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var file = File.Create(args[1])) encoder.Save(file);
                    }
                    _menu?.Close();
                });
                break;
            case "menu" when args.Length >= 2:
                Queue("menu", args[1..]);
                break;
            case "pick":
                EnterBuddyMode();
                StartColourPicker();
                break;
            case "qr":
                EnterBuddyMode();
                ShowQr(string.Join(" ", args[1..]));
                break;
            case "paw-test" when args.Length >= 3:
                // Dev aid: ask about a picture file as if the paw had picked it.
                EnterBuddyMode();
                Chat.Show(File.ReadAllBytes(args[1]), "Test picture");
                _ = Chat.Send(string.Join(" ", args[2..]));
                break;
            case "look-test" when args.Length >= 3:
                // Dev aid: "the window in front" is this picture, then ask (e.g. "what's this error?").
                EnterBuddyMode();
                WindowShots.TestPicture = (File.ReadAllBytes(args[1]), Path.GetFileNameWithoutExtension(args[1]));
                _ = Chat.Send(string.Join(" ", args[2..])).ContinueWith(_ => WindowShots.TestPicture = null);
                break;
            case "window-shot" when args.Length == 2:
                // Dev aid: save the picture "what's this error?" would take (nothing is sent anywhere).
                if (WindowShots.Capture(WindowShots.TopOtherWindow()) is { } front)
                {
                    File.WriteAllBytes(args[1], front.Png);
                    Log.Info($"window-shot: \"{front.Title}\", {front.Png.Length / 1024} KB");
                }
                else Log.Info("window-shot: no window");
                break;
            case "meeting-test" when args.Length == 2:
                // Dev aid: a stereo WAV (left you, right them) goes through the meeting notes as if it were a call.
                EnterBuddyMode();
                _ = MeetingTest(args[1]);
                break;
            case "voice-test" when args.Length >= 3:
                // Dev aid: learn a voice from the first WAV(s) after "learn", then say how alike each other WAV is.
                _ = VoiceTest(args[1..]);
                break;
            case "speak-file" when args.Length >= 3:
                // Dev aid: the cat's voice into a WAV file instead of the speakers ("speak-file out.wav voice-id text…").
                _ = SpeakFile(args[1], args[2], string.Join(" ", args[3..]));
                break;
            case "routine-test":
                // Dev aid: which routine each phrase starts (nothing is run).
                foreach (var said in new[] { "start work mode", "please start work mode", "done for the day", "I'm done for today", "work", "merge my pdfs", "meeting" })
                    Log.Info($"routine-test \"{said}\": {Routines.Match(said)?.Name ?? "-"}");
                foreach (var r in Routines.All) Log.Info($"routine-test saved: {r.Name} [{string.Join(" / ", r.Phrases)}] {string.Join(", ", r.Steps.Select(Routines.Describe))}");
                break;
            case "captions-test" when args.Length >= 2:
                // Dev aid: captions on (translated into args[2] if given), a WAV played through the speakers, then off.
                EnterBuddyMode();
                _ = CaptionsTest(args[1], args.Length > 2 ? args[2] : null);
                break;
            case "quip" when args.Length >= 2:
                // Dev aid: a quip now (skips the 20-minute wait) and its picture.
                EnterBuddyMode();
                _lastQuip = DateTime.MinValue;
                Quip(string.Join(" ", args[1..]));
                break;
            case "quip-shot" when args.Length >= 3:
                // Dev aid: a quip bubble drawn off screen.
                var bubbleShot = new QuipBubble(string.Join(" ", args[2..]), new Rect(0, 400, 100, 100), new Vector(0, -1));
                DashboardWindow.SaveElement((FrameworkElement)bubbleShot.Content, 320, args[1]);
                bubbleShot.Close();
                break;
            case "guard-test":
                // Dev aid: the privacy guard on made-up text (nothing is sent).
                foreach (var sample in new[]
                {
                    "Card 4111 1111 1111 1111 exp 12/29, and order 1234 5678 9012 3456 (not a card).",
                    "Aadhaar 2341 2341 2346 and phone 9876543210; PAN ABCPE1234F.",
                    "Wifi password: hunter2!  my pin is 4455 and the PIN: 9911",
                    "key sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123 and invoice INV-2231 for Rs 28,990 dated 2026-09-03",
                })
                {
                    var (clean, hidden) = Core.Ai.PrivacyGuard.Clean(sample);
                    Log.Info($"guard-test: {clean}  [{Core.Ai.PrivacyGuard.Describe(hidden)}]");
                }
                break;
            case "suggest-test":
                // Dev aid: what would be suggested for some made-up downloads (nothing runs).
                foreach (var f in Directory.GetFiles(args.Length > 1 ? args[1] : ".")) Log.Info($"suggest-test {Path.GetFileName(f)}: {(Suggestions.ForDownload(f) is { } sg ? $"{sg.Action.Id} ({sg.Why})" : "-")}");
                break;
            case "agent-selftest":
                // Dev aid: the agent's hands without Claude: screen size and capture, key names, the sandbox's clicks and typing, the pill's look.
                EnterBuddyMode();
                AgentSelfTest();
                break;
            case "instant-test" when args.Length >= 2:
                // Dev aid: an instant answer, silently (what it would say goes to the log).
                {
                    var said = string.Join(" ", args[1..]);
                    var talk = _settings.TalkBack;
                    _settings.TalkBack = "off";
                    var watch = Stopwatch.StartNew();
                    var act = Instant(WakeMatcher.Normalize(Speech.Hindi ? Hinglish.ToCommand(said) : said));
                    act?.Invoke();
                    Dispatcher.BeginInvoke(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(2500);
                        _settings.TalkBack = talk;
                        var last = Chat.Items.OfType<CatSaid>().LastOrDefault()?.Text;
                        Log.Info($"instant-test \"{said}\": {(act == null ? "not instant (goes to Claude)" : last)} ({watch.ElapsedMilliseconds} ms incl. 2.5 s wait)");
                    });
                }
                break;
            case "index-test" when args.Length >= 2:
                // Dev aid: the index's state and how many documents match (counts and the test file only).
                if (_index is { } idx)
                {
                    var hits = idx.Search(string.Join(" ", args[1..]));
                    Log.Info($"index-test: {idx.Count} indexed, busy {idx.Indexing}; \"{string.Join(" ", args[1..])}\": {hits.Count} hits" + string.Concat(hits.Where(h => h.Path.Contains("shiki-index-test")).Select(h => $" | {Path.GetFileName(h.Path)}: {h.Snippet}")));
                }
                else Log.Info("index-test: no index");
                break;
            case "calendar-day" when args.Length == 2 && DateTime.TryParse(args[1], out var calendarDay):
                // Dev aid: one day's events with their ids and places (read-only).
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    foreach (var e in await Core.Mail.Calendar.Between(calendarDay, calendarDay.AddDays(1), default))
                        Log.Info($"calendar-day: {e.Id} | {Core.Mail.Calendar.Describe(e)} | at {e.Location}");
                });
                break;
            case "calendar-remove" when args.Length == 2:
                // Dev aid: removes one event by id (for cleaning up a test).
                _ = Dispatcher.InvokeAsync(async () => { await Core.Mail.Calendar.Delete(args[1], default); Log.Info($"calendar-remove: {args[1]}"); });
                break;
            case "google-test":
                // Dev aid: today's calendar and a contacts search (read-only; nothing goes to Claude).
                _ = GoogleTest(args.Length > 1 ? args[1] : "a");
                break;
            case "gmail-test":
                // Dev aid: the newest email's sender and subject (read-only; nothing goes to Claude).
                _ = GmailTest();
                break;
            case "agent-sandbox" when args.Length >= 2:
                // Dev aid: the agent on a pretend mail app drawn off screen (no real clicks or typing); questions are answered yes.
                EnterBuddyMode();
                _ = AgentSandboxTest(string.Join(" ", args[1..]));
                break;
            case "habit-test":
                // Dev aid: what habit would be offered now, and the rules (nothing changes).
                Log.Info($"habit-test spot: {(Habits.Spot() is { } hs ? $"{hs.Extension} {hs.Action.Id} x{hs.Times}" : "-")}");
                foreach (var rule in Habits.Rules) Log.Info($"habit-test rule: {Habits.Describe(rule)}");
                {
                    // Made-up history: HEIC to JPG three times from Downloads, PNG to WEBP twice, compress (not a format change) three times.
                    var dl = @"C:\Users\Test\Downloads";
                    HistoryEntry Fake(int hoursAgo, string id, params string[] names) =>
                        new() { Time = DateTime.Now.AddHours(-hoursAgo), ActionId = id, Inputs = names.Select(n => Path.Combine(dl, n)).ToList() };
                    var fake = new[]
                    {
                        Fake(1, "image-jpg", "a.heic", "b.heic"), Fake(30, "image-jpg", "c.heic"), Fake(80, "image-jpg", "d.HEIC"),
                        Fake(2, "image-webp", "x.png"), Fake(5, "image-webp", "y.png"), Fake(3, "image-compress", "p.jpg"), Fake(4, "image-compress", "q.jpg"), Fake(6, "image-compress", "r.jpg"),
                    };
                    Log.Info($"habit-test fake spot: {(Habits.Spot(fake, dl) is { } fs ? $"{fs.Extension} {fs.Action.Id} x{fs.Times}" : "-")}");
                }
                break;
            case "speech-language" when args.Length == 2:
                // Dev aid: the speech language for this run only (the setting isn't changed).
                Speech.Language = args[1];
                Log.Info($"speech-language: {Speech.Language}");
                break;
            case "hear-file" when args.Length == 2:
                // Dev aid: what the command engine writes down from a WAV, and the command it would be (nothing runs).
                _ = HearFile(args[1]);
                break;
            case "hindi-test":
                // Dev aid: Devanagari to Roman, and Hinglish commands (nothing runs).
                foreach (var said in new[] { "आवाज़ कम करो", "क्रोम खोलो", "स्पॉटिफाई चलाओ", "आवाज़ बंद करो", "chrome kholo", "क्रोम खोलो", "volume chalis pe kar do", "roshni badhao", "dark mode on karo",
                    "नोट्स लेना शुरू करो", "captions chalu karo", "ruko", "spotify chala do", "मुझे कल बैंक जाना है", "क्या आप मेरी मदद कर सकते हैं", "समझ नहीं आया", "करना", "संबंध", "हाँ", "नहीं", "शुक्रिया" })
                    Log.Info($"hindi-test \"{said}\": roman \"{Hinglish.ToRoman(said)}\" -> command \"{WakeMatcher.Normalize(Hinglish.ToCommand(said))}\" pc={PcCommands.Match(WakeMatcher.Normalize(Hinglish.ToCommand(said))) != null} yes/no={Hinglish.Answer(said)} bye={Hinglish.IsGoodbye(said)}");
                break;
            case "pc-test":
                // Dev aid: reads the PC's controls, sets each to what it already is, and checks what
                // phrases and app names would match (nothing is left changed).
                PcTest();
                break;
            case "type-test":
                // Dev aid: types into a small window of its own and logs what arrived (nothing else is touched).
                _ = TypeTest();
                break;
            case "paw":
                // Paw to ask, as the shortcut opens it.
                EnterBuddyMode();
                StartPaw();
                break;
            case "paste":
                EnterBuddyMode();
                PasteToCat();
                break;
            case "settings":
                EnterBuddyMode();
                ShowSettings();
                break;
            case "dashboard":
                // "dashboard tools" or "dashboard history" opens that page.
                EnterBuddyMode();
                ShowDashboard(args.Length > 1 && Enum.TryParse<DashPage>(args[1], true, out var page) ? page : DashPage.Home);
                break;
            case "demo-pills":
                // Dev aid for the ad: the dashboard's pills swell and open one by one.
                EnterBuddyMode();
                ShowDashboard(DashPage.Home);
                _dashboard?.DemoPills();
                break;
            case "demo-scroll" when args.Length >= 2 && Enum.TryParse<DashPage>(args[1], true, out var glidePage):
                // Dev aid for the ad: a page glides from top to bottom ("demo-scroll levels 6").
                EnterBuddyMode();
                ShowDashboard(glidePage);
                _dashboard?.DemoScroll(args.Length > 2 && double.TryParse(args[2], System.Globalization.CultureInfo.InvariantCulture, out var secs) ? secs : 5);
                break;
            case "demo-rename" when args.Length >= 3:
                // Dev aid for the ad: on the Cats page, a cat's pencil opens and a name is typed (then let go).
                EnterBuddyMode();
                ShowDashboard(DashPage.Cats);
                var who = args[1];
                var newName = string.Join(" ", args[2..]);
                Dispatcher.BeginInvoke(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(900);
                    if (UI.Dashboard.CatsPage.DemoRename.TryGetValue(who, out var demo)) await demo(newName);
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                break;
            case "snapshot" when args.Length == 2:
                // Dev aid: save what the dashboard shows to a PNG.
                _dashboard?.Snapshot(args[1]);
                break;
            case "snapshot" when args.Length == 3 && Enum.TryParse<DashPage>(args[1], true, out var shot):
                // Dev aid: a page drawn off screen (the dashboard isn't shown, nothing takes focus).
                EnterBuddyMode();
                if (_dashboard == null) ShowDashboard(shot, show: false);
                _dashboard!.Snapshot(args[2], shot);
                break;
            case "view-media" when args.Length == 2 && File.Exists(args[1]):
                // Dev aid: the phone gallery's viewer on this file (see it with "snapshot phone <png>").
                EnterBuddyMode();
                if (_dashboard == null) ShowDashboard(DashPage.Phone, show: false);
                _dashboard!.PeekViewer(args[1]);
                break;
            case "select-test" when args.Length == 2 && int.TryParse(args[1], out var pickCount):
                // Dev aid: the Phone page with the first few chosen (see it with "snapshot phone <png>").
                EnterBuddyMode();
                if (_dashboard == null) ShowDashboard(DashPage.Phone, show: false);
                _dashboard!.PeekSelection(pickCount);
                break;
            case "media-info" when args.Length >= 2:
                // Dev aid: what the viewer reads from each file.
                foreach (var media in args[1..]) _ = System.Threading.Tasks.Task.Run(async () => Log.Info($"media-info: {await Core.Phone.MediaInfo.ReadAsync(media)}"));
                break;
            case "try-saying" when args.Length >= 2:
                // Dev aid: what Try on the skills page does with these words.
                TrySaying(string.Join(" ", args[1..]));
                break;
            case "snapshot-whole" when args.Length == 3 && Enum.TryParse<DashPage>(args[1], true, out var wholePage):
                // Dev aid: a dashboard page from top to bottom.
                EnterBuddyMode();
                if (_dashboard == null) ShowDashboard(wholePage, show: false);
                _dashboard!.SnapshotWhole(args[2], wholePage);
                break;
            case "snapshot-settings" when args.Length is 2 or 3:
                // Dev aid: the settings drawn off screen, whole (one tab, if named).
                EnterBuddyMode();
                if (args.Length == 3) SettingsWindow.OpenTab = args[2];
                var settingsShot = NewSettings();
                DashboardWindow.SaveElement((FrameworkElement)settingsShot.Content, 640, args[1]);
                settingsShot.Close();
                break;
            case "snapshot-teach" when args.Length == 2:
                // Dev aid: the teach-your-cat page drawn off screen.
                EnterBuddyMode();
                var teach = new WakeSetupWindow(CatName, _settings.WakeVariants, _settings.WakeWord, (_, _, _) => { });
                DashboardWindow.SaveElement((FrameworkElement)teach.Content, teach.Width, args[1]);
                teach.Close();
                break;
            case "snapshot-meeting" when args.Length == 3:
                // Dev aid: one meeting's page, drawn off screen.
                EnterBuddyMode();
                if (_dashboard == null) ShowDashboard(DashPage.Meetings, show: false);
                _dashboard!.SelectMeeting(args[1]);
                _dashboard.Snapshot(args[2], DashPage.Meetings);
                break;
            case "task-remove" when args.Length >= 2:
                // Dev aid: takes a test task back off the list (by its exact words).
                foreach (var test in Todos.All.Where(t => t.Text == string.Join(" ", args.Skip(1))).ToList()) Todos.Remove(test);
                break;
            case "bundle-demo":
            {
                // Dev aid: a pretend two-things card in the chat (it can't run: it isn't offered), for snapshot-bubble.
                var parts = new List<PlanItem>
                {
                    new("Send on WhatsApp to Mummy", []) { Detail = "I'm on my way, reaching in 20 minutes." },
                    new("Send an email to you", []) { Detail = "Subject: Logo photos", Attached = [@"D:\Designs\shiki-logo-1.png", @"D:\Designs\shiki-logo-2.png", @"D:\Designs\shiki-logo-3.png"] },
                };
                Chat.Say(new YouSaid("WhatsApp Mummy that I'm on my way and email these to me", spoken: true));
                Chat.Say(new CatSaid("Okay to send?"));
                Chat.Say(Chat.Bundle(parts));
                break;
            }
            case "briefing":
                // Dev aid: the day's briefing in the chat (see it with snapshot-bubble), as if asked.
                EnterBuddyMode();
                _ = Brief(spoken: false);
                break;
            case "quick-test" when args.Length >= 2:
            {
                // Dev aid: how the quick path reads a request, and who it would go to (nothing is sent or set).
                var request = string.Join(" ", args.Skip(1));
                var plan = QuickPath.Plan(request, DateTime.Now);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var who = new List<string>();
                    foreach (var item in plan ?? [])
                    {
                        if (item is QuickPath.Message m) who.Add($"{m.Name} -> {await FindPhone(m.Name)}");
                        if (item is QuickPath.Files { Email: true } f) who.Add($"{f.Who} -> {(f.Who == "me" ? QuickPath.RememberedEmail("me")?.ToString() : (await FindEmail(f.Who)).ToString())}");
                        if (item is QuickPath.Files { Email: false } w) who.Add($"{w.Who} -> {await FindPhone(w.Who)}");
                    }
                    Log.Info($"quick-test \"{request}\": {(plan == null ? "to Claude" : string.Join(" + ", plan))}{(who.Count > 0 ? " | " + string.Join(" | ", who) : "")}");
                });
                break;
            }
            case "address-test" when args.Length >= 3 && args[1] is "phone" or "email":
            {
                // Dev aid: how a typed or spoken number / email is read (nothing is sent).
                var kind = args[1] == "phone" ? AddressKind.Phone : AddressKind.Email;
                var typed = string.Join(" ", args.Skip(2));
                Log.Info($"address-test {args[1]} \"{typed}\": clean={Addresses.Clean(kind, typed) ?? "-"} | problem={Addresses.Problem(kind, typed) ?? "-"} | spoken={Addresses.FromSpeech(kind, typed) ?? "-"}");
                break;
            }
            case "address-card-demo" when args.Length >= 3 && args[1] is "phone" or "email":
            {
                // Dev aid: a pretend new-person card in the chat (it can't send: Claude didn't make it), for snapshot-bubble.
                var kind = args[1] == "phone" ? AddressKind.Phone : AddressKind.Email;
                var demo = new PlanItem(kind == AddressKind.Phone ? $"Send on WhatsApp to {args[2]}" : $"Send an email to {args[2]}", [])
                {
                    Detail = kind == AddressKind.Phone ? "Reached home, will call you in 10 minutes." : "Subject: Logo photos\nHere are the logo photos we talked about.",
                    Ask = new AddressAsk(kind, args[2]),
                };
                demo.Address = args.Length > 3 ? string.Join(" ", args.Skip(3)) : Addresses.Start(kind);
                Chat.Say(new YouSaid(kind == AddressKind.Phone ? $"WhatsApp {args[2]} that I reached home" : $"Email the logo photos to {args[2]}", spoken: true));
                Chat.Say(new CatSaid($"I don't have {args[2]}'s {(kind == AddressKind.Phone ? "number" : "email")} yet. Type it in, or say it."));
                Chat.Say(demo);
                break;
            }
            case "snapshot-bubble" when args.Length == 2:
                // Dev aid: the ask bubble drawn off screen.
                EnterBuddyMode();
                var palette = (_settings.DashboardDark ?? DashPalette.WindowsIsDark()) ? DashPalette.Night : DashPalette.Light;
                var lastSaid = Chat.Items.ToList().FindLastIndex(i => i is YouSaid);
                var bubble = new AskBubble(Chat, Voice, palette, CatName, CatSprites.Head(Skin), OpenFile, () => { }, lastSaid >= 0 ? lastSaid : null);
                DashboardWindow.SaveElement((FrameworkElement)bubble.Content, bubble.Width, args[1]);
                bubble.Close();
                break;
            case "snapshot-pill" when args.Length is 2 or 3 or 4:
                // Dev aid: the "Hey Sky" voice pill drawn off screen, with words (and an answer).
                EnterBuddyMode();
                var pillPalette = (_settings.DashboardDark ?? DashPalette.WindowsIsDark()) ? DashPalette.Night : DashPalette.Light;
                var voicePill = new VoicePill(Chat, Voice, pillPalette, () => { });
                var pillShot = voicePill.Preview(args.Length > 2 ? args[2] : "Listening…", args.Length > 3 ? args[3] : null, 0.7);
                DashboardWindow.SaveElement(pillShot, 420 + 36, args[1]);
                voicePill.Close();
                break;
            case "gmail-draft-test" when args.Length >= 3:
                // Dev aid: saves a real Gmail draft (to args[1], with the files) and reads it back; nothing is sent.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var draft = new Shiki.Core.Mail.Gmail.Draft(args[1], "Shiki logo photos", "", null, args.Skip(2).ToList());
                        await Shiki.Core.Mail.Gmail.SaveDraft(draft, System.Threading.CancellationToken.None);
                        var found = await Shiki.Core.Mail.Gmail.Search("in:drafts subject:\"Shiki logo photos\"", 1, System.Threading.CancellationToken.None);
                        var mail = await Shiki.Core.Mail.Gmail.Read(found[0].Id, System.Threading.CancellationToken.None);
                        Log.Info($"gmail-draft-test: draft to {mail.To}, \"{mail.Subject}\", {mail.Attachments.Count} attachment(s): {string.Join(" | ", mail.Attachments)}");
                    }
                    catch (Exception e) { Log.Error("gmail-draft-test failed", e); }
                });
                break;
            case "mime-test" when args.Length >= 3:
                // Dev aid: writes the email Shiki would send (to yourself, with these files) to a file; nothing is sent.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var mime = await Shiki.Core.Mail.Gmail.Preview(new Shiki.Core.Mail.Gmail.Draft("me@example.com", "Shiki logo photos", "", null, args.Skip(2).ToList()));
                    await File.WriteAllBytesAsync(args[1], mime);
                    Log.Info($"mime-test: {mime.Length:N0} bytes, {args.Length - 2} file(s)");
                });
                break;
            case "reminder-say-test":
                // Dev aid: a pretend reminder (not saved), said aloud and listened for, whatever the talk setting.
                {
                    EnterBuddyMode();
                    var pretend = new Todo { Text = "Stretch your back" };
                    ToastWindow? card = null;
                    card = ToastWindow.Message($"Reminder: {pretend.Text}", "A test reminder.", null, ("Done", () => card!.CloseAnimated()));
                    card.KeepFor(TimeSpan.FromMinutes(3));
                    ToastHost.Show(card);
                    _buddy?.Act("startle");
                    SayReminder(pretend, card, evenIfQuiet: true);
                }
                break;
            case "reminder-answer-test":
                // Dev aid: how answers to a reminder are read (nothing is changed).
                foreach (var said in new[] { "done", "It's done.", "I did it", "ho gaya", "later", "in 20 minutes", "10 minutes please", "remind me in an hour",
                    "tomorrow", "remind me tomorrow", "okay", "thanks", "haan haan, theek hai", "okay thank you Sky", "call him on WhatsApp", "send him an email saying I'll call tomorrow", "snooze", "baad mein" })
                {
                    var (kind, until) = ReadReminderAnswer(said);
                    Log.Info($"reminder-answer \"{said}\" -> {kind}{(until is { } u ? $" until {u:ddd HH:mm}" : "")}");
                }
                break;
            case "pill-demo":
                // Dev aid: the "Hey Sky" pill opens (and, with words, they're asked), then grows into the chat.
                EnterBuddyMode();
                ShowVoicePill();
                if (args.Length > 1) _ = Chat.Send(args[1]);
                var grow = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                grow.Tick += (_, _) => { grow.Stop(); if (_voicePill is { } demo) GrowIntoBubble(demo); };
                grow.Start();
                break;
            case "catmenu":
                // Dev aid: the cat's menu at the mouse, as a right-click would open it.
                EnterBuddyMode();
                NativeMethods.GetCursorPos(out var at);
                ShowMenu(new Point(at.X, at.Y));
                break;
            case "cat" when args.Length == 2:
                EnterBuddyMode();
                _buddy?.Act(args[1]);
                break;
            case "record-demo" when args.Length == 2:
                // Dev aid: record a fixed area for N seconds with the real bar and frame.
                EnterBuddyMode();
                _recording = new RecordingSession(_settings);
                _recording.Saved += (file, centre) => ShowWheel([file], centre);
                _recording.Ended += () => _recording = null;
                _recording.Demo(new Int32Rect(320, 200, 960, 540), int.Parse(args[1]));
                break;
            case "play" when args.Length == 2:
                // Plays a video or audio file in the dashboard.
                EnterBuddyMode();
                ShowDashboard();
                _dashboard!.Play(System.IO.Path.GetFullPath(args[1]));
                break;
            case "record":
                EnterBuddyMode();
                StartRecording();
                break;
            case "ask":
                // The ask bubble by the cat, as the shortcut opens it; "ask some words" sends them too.
                EnterBuddyMode();
                ShowAskBubble(args.Length > 1 ? string.Join(" ", args[1..]) : null);
                break;
            case "teach":
                // Meet your cat: name it and teach it "Hey <name>".
                EnterBuddyMode();
                TeachName();
                break;
            case "wake-file" when args.Length == 2:
                // (With a voiceprint learned, the log also says how alike the voice is.)
                // Dev aid: does this recording (WAV) wake the cat? The answer goes to the log.
                EnterBuddyMode();
                _ = WakeFile(args[1]);
                break;
            case "voice-phrases":
                // Dev aid: how a few phrases are taken (goodbye, yes, no, or a request); answers go to the log.
                foreach (var phrase in new[] { "Thanks.", "Thank you, Sky.", "That's all.", "No, that's it.", "Stop listening.", "Thanks, now compress it.",
                    "Yes, do it.", "No thanks.", "Never mind.", "Now make it smaller.", "Okay, great.", "Done.", "I'm done with the PDF, now zip the photos." })
                    Log.Info($"voice-phrases: \"{phrase}\" -> {(VoiceInput.IsGoodbye(phrase) ? "goodbye" : VoiceInput.Answer(phrase) switch { true => "yes", false => "no", _ => "request" })}");
                break;
            case "stream-file" when args.Length == 2:
                // Dev aid: stream a WAV to Deepgram in real time, as the microphone would; partials and the end go to the log.
                EnterBuddyMode();
                _ = StreamFile(args[1]);
                break;
            case "say-file" when args.Length == 2:
                // Dev aid: a recording (WAV) goes through speech-to-text and to the cat, as if spoken.
                EnterBuddyMode();
                _ = SayFile(args[1]);
                break;
            case "ask-quiet" when args.Length > 1:
                // Dev aid: say something to the cat without opening anything (see it with "snapshot ask").
                EnterBuddyMode();
                _ = Chat.Send(string.Join(" ", args[1..]));
                break;
            case "ask-choose" when args.Length == 2 && int.TryParse(args[1], out var pickNumber):
                // Dev aid: press option N (from 1) on the waiting choice card.
                if (Chat.WaitingChoice is { } waitingChoice && pickNumber >= 1 && pickNumber <= waitingChoice.Options.Count)
                    Chat.Choose(waitingChoice, waitingChoice.Options[pickNumber - 1]);
                break;
            case "ask-decline":
                // Dev aid: press Not now on the waiting plan.
                if (Chat.Items.OfType<PlanItem>().LastOrDefault(x => x.State == PlanState.Waiting) is { } declined) Chat.Decline(declined);
                break;
            case "ask-confirm" when args.Length == 2:
                // Dev aid: press Do it on the waiting plan, with results going to this folder for once.
                if (Chat.Items.OfType<PlanItem>().LastOrDefault(x => x.State == PlanState.Waiting) is { } waiting)
                {
                    var keep = OutputPaths.Folder;
                    OutputPaths.Folder = args[1];
                    _ = Chat.Confirm(waiting).ContinueWith(_ => Dispatcher.BeginInvoke(() => OutputPaths.Folder = keep));
                }
                break;
            case "selected":
                // Same as the convert shortcut: the files selected in the Explorer window in front.
                EnterBuddyMode();
                ConvertSelection();
                break;
            default:
                var files = args.Where(a => File.Exists(a) || Directory.Exists(a)).ToArray();
                if (files.Length > 0) Queue("menu", files);
                else Log.Error($"Unknown command: {string.Join(" ", args)}");
                break;
        }
    }

    void Queue(string key, IEnumerable<string> files)
    {
        if (!_pending.TryGetValue(key, out var list)) _pending[key] = list = [];
        list.AddRange(files);
        _batchTimer.Stop();
        _batchTimer.Start();
    }

    void FlushBatches()
    {
        _batchTimer.Stop();
        var batches = _pending.ToList();
        _pending.Clear();
        foreach (var (key, raw) in batches)
        {
            var files = FileList.Expand(raw);
            if (key == "menu")
            {
                NativeMethods.GetCursorPos(out var cursor);
                ShowWheel(files, new Point(cursor.X, cursor.Y));
                continue;
            }
            var action = ActionCatalog.Find(key);
            if (action == null)
            {
                Log.Error($"Unknown action {key}");
                continue;
            }
            StartJob(action, files);
        }
    }

    void StartJob(BroAction action, List<string> files)
    {
        // Merges follow Explorer's name order, since launch order is random.
        if (action.Combine) files.Sort(NaturalComparer.Instance);
        if (action.Editor != null)
        {
            var accepted = files.Where(action.Accepts).ToList();
            if (accepted.Count < action.MinFiles) return;
            var editor = action.Editor(accepted);
            if (editor != null)
            {
                // Editors live in the dashboard now, wherever they were started from.
                InDashboard(editor);
                return;
            }
        }
        if (JobRunner.Start(action, files) == null)
            ToastHost.Show(ToastWindow.Message("Nothing to do", $"None of those files can be used for \"{action.Label}\"."));
    }

    public void ShowWheel(IReadOnlyList<string> files, Point centerPx)
    {
        if (files.Count == 0) return;
        ReactToSize(files);
        var wheel = new ConvertWheel(ConvertWheel.WheelMode.Click, centerPx, files);
        wheel.ActionChosen += (action, chosen) => StartJob(action, chosen.ToList());
        wheel.Show();
    }

    /// <summary>Shift + drag: open the wheel right under the cursor so the file can be dropped on a wedge.</summary>
    void OpenDragWheel(int xPx, int yPx)
    {
        _dragWheel?.CloseAnimated();
        var wheel = new ConvertWheel(ConvertWheel.WheelMode.Drag, new Point(xPx, yPx));
        wheel.ActionChosen += (action, files) => StartJob(action, files.ToList());
        wheel.FilesArrived += ReactToSize;
        wheel.Closed += (_, _) => { if (_dragWheel == wheel) _dragWheel = null; };
        _dragWheel = wheel;
        _buddy?.Notice(true);
        wheel.Show();
    }

    CatSkin Skin => CatSprites.Skin(_settings.Cat);

    void ShowMenu(Point anchorPx)
    {
        _menu?.CloseAnimated();
        _menuAnchor = anchorPx;
        bool visible = _buddy?.IsVisible ?? false;
        // Just what you'd reach for from the cat itself; the tools are on the dashboard and their keys,
        // and how the cat looks and moves is in Settings.
        _menu = new BroMenu(anchorPx, $"Drop files on me, or say \"Hey {CatName}\" and ask.",
        [
            new MenuEntry("\uE8BD", $"Ask {CatName}…", () => ShowAskBubble()),
            new MenuEntry("\uE80F", "Open Shiki", () => ShowDashboard()),
            new MenuEntry("\uE8E5", "Convert files…", PickFiles),
            null,
            _settings.WakeVariants.Count > 0
                ? new MenuEntry("\uE720", $"Listen for \"Hey {CatName}\"", ToggleWake, () => _settings.WakeWord)
                : new MenuEntry("\uE720", $"Teach me my name…", TeachName),
            .. (_meeting != null ? [new MenuEntry("\uE8F2", "Stop meeting notes", ToggleNotes)] : Array.Empty<object?>()),
            new MenuEntry(visible ? "\uE7B3" : "\uE890", visible ? "Hide the cat" : "Show the cat", () => SetBuddyVisible(!visible)),
            null,
            new MenuEntry("\uE713", "Settings…", ShowSettings),
            new MenuEntry("\uE711", "Quit", () => Shutdown()),
        ], CatSprites.Head(Skin), Skin.Name, StatsLine());
        _menu.Closed += (_, _) => _menu = null;
        _menu.Show();
    }

    // ---- The cat's tally ----

    const long BigFile = 1L << 30; // 1 GB: big enough to make the cat jump
    static readonly int[] Milestones = [10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000];

    readonly HashSet<string> _startledFor = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Big files make the cat jump, once: when they reach the wheel or, failing that, when the
    /// job starts (Explorer's menu, say). The same files don't startle it twice.
    /// </summary>
    void ReactToSize(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0 || files.All(_startledFor.Contains) || TotalBytes(files) <= BigFile) return;
        if (_startledFor.Count > 500) _startledFor.Clear();
        _startledFor.UnionWith(files);
        _buddy?.Startle();
    }

    /// <summary>Above the cat when it's showing (so the wheel doesn't cover it), else the fallback.</summary>
    Point CatSpot(Point fallback) => _buddy is { IsVisible: true } cat ? cat.WheelSpotPx(ConvertWheel.DiscRadiusDip) : fallback;

    static long TotalBytes(IEnumerable<string> paths) =>
        paths.Sum(p => { try { return File.Exists(p) ? new FileInfo(p).Length : 0; } catch (IOException) { return 0; } });

    /// <summary>Counts a finished job; returns true when it crossed a milestone (and says so).</summary>
    bool RecordStats(Job job)
    {
        var inputs = job.Inputs.Where(File.Exists).ToList();
        if (inputs.Count == 0) return false;
        int before = _settings.FilesDone;
        _settings.FilesDone += inputs.Count;
        // Saved space: only where the results are files and came out smaller than what went in.
        var outputs = job.Outputs.Where(File.Exists).ToList();
        if (outputs.Count > 0)
        {
            long saved = TotalBytes(inputs) - TotalBytes(outputs);
            if (saved > 0) _settings.BytesSaved += saved;
        }
        _settings.Save();

        int? reached = null;
        foreach (var milestone in Milestones)
            if (before < milestone && _settings.FilesDone >= milestone) reached = milestone;
        // Past the list: every 10,000.
        if (reached == null && _settings.FilesDone >= 20000 && _settings.FilesDone / 10000 > before / 10000) reached = _settings.FilesDone / 10000 * 10000;
        if (reached is not { } count) return false;
        var soFar = _settings.BytesSaved >= 1 << 20 ? $" {FormatBytes(_settings.BytesSaved)} saved so far." : "";
        ToastHost.Show(ToastWindow.Message($"{count:N0} files converted!", $"{Skin.Name} is very proud.{soFar} Thanks for keeping me busy."));
        return true;
    }

    string? StatsLine()
    {
        if (_settings.FilesDone == 0) return null;
        var files = $"{_settings.FilesDone:N0} {(_settings.FilesDone == 1 ? "file" : "files")} converted";
        return _settings.BytesSaved >= 1 << 20 ? $"{files}, {FormatBytes(_settings.BytesSaved)} saved" : files;
    }

    static string FormatBytes(long bytes) => bytes switch
    {
        < 1L << 30 => $"{bytes / 1024.0 / 1024:0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
    };

    void SetCat(string id)
    {
        if (!CatLevel.CatUnlocked(id, Level())) return;
        _settings.Cat = id;
        // The new cat brings its own name, and that's the one used everywhere now.
        _settings.CatName = CatName;
        RefreshKeyTerms();
        _settings.Save();
        DressCat();
        _tray?.SetCat(Skin);
        // The Explorer menu shows the cat too.
        if (ContextMenuRegistrar.IsRegistered()) ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(Skin));
    }

    // ---- The cat grows: levels, cats and things to wear ----

    PcWatch? _pc;
    readonly DispatcherTimer _levelCheck = new() { Interval = TimeSpan.FromMinutes(2) };

    /// <summary>The cat on screen: its skin, wearing its accessory (headphones while taking notes).</summary>
    void DressCat() => _buddy?.SetSkin(CatSprites.Dressed(Skin, _meeting != null ? "headphones" : _settings.Accessory));

    void SetAccessory(string? id)
    {
        if (id != null && !CatLevel.AccessoryUnlocked(id, Level())) return;
        _settings.Accessory = id;
        _settings.Save();
        DressCat();
    }

    int Points() => CatLevel.Points(_settings, MeetingStore.All().Count, Todos.All.Count(t => t.Done), History.Streak());

    /// <summary>What's been done with Shiki, for the level tasks.</summary>
    CatStats Stats() => new(_settings.FilesDone, _settings.AiRequests, MeetingStore.All().Count, Todos.All.Count(t => t.Done), History.Streak(),
        Routines.All.Count, Preferences.All.Count, (Core.Phone.PhoneLink.Current?.Phones.Count ?? 0) > 0, _settings.WakeVariants.Count > 0);

    /// <summary>The level reached (kept), plus any next levels whose tasks are now all done.</summary>
    int Level() => CatLevel.LevelNow(Stats(), _settings.CatLevel > 0 ? _settings.CatLevel : CatLevel.LevelFor(Points()));

    /// <summary>A new level: a party, and a card saying what it unlocked. The first check only remembers where it is.</summary>
    void CheckLevel()
    {
        int level = Level();
        if (_settings.CatLevel == 0)
        {
            _settings.CatLevel = level;
            _settings.Save();
            return;
        }
        if (level <= _settings.CatLevel) return;
        var unlocked = Enumerable.Range(_settings.CatLevel + 1, level - _settings.CatLevel).SelectMany(CatLevel.NewAt).ToList();
        _settings.CatLevel = level;
        _settings.Save();
        _buddy?.Party();
        var what = unlocked.Count == 0 ? "Keep going: there's more to unlock."
            : "Unlocked: " + string.Join(", ", unlocked.Select(u => u.IsCat ? $"{u.Name} (a new cat)" : u.Name)) + ". Pick it on the Cats page.";
        ToastHost.Show(ToastWindow.Message($"{CatName} reached level {level}!", what));
    }

    /// <summary>The PC's news reaches the cat: battery, a busy processor, downloads, coming back.</summary>
    void WatchPc()
    {
        _pc = new PcWatch();
        Speech.Language = _settings.SpeechLanguage;
        RefreshKeyTerms();
        Core.Ai.PrivacyGuard.On = _settings.PrivacyGuard;
        if (_settings.SearchInsideFiles) StartIndex();
        // Read the Start menu now, so "open Spotify" is instant later.
        System.Threading.Tasks.Task.Run(Apps.All);
        _pc.StateChanged += () => _buddy?.SetPcState(_pc.BatteryLow, _pc.Busy);
        _pc.PluggedIn += () => _buddy?.Cheer(Floater.Bolt);
        _pc.Downloaded += _ => _buddy?.Cheer(Floater.Download);
        _pc.FileArrived += AutoConvert;
        _pc.CameBack += () => { _buddy?.WelcomeBack(); ComeBackQuip(); };
        _pc.Downloaded += SuggestFor;
        _buddy?.SetPcState(_pc.BatteryLow, _pc.Busy);
        _levelCheck.Tick += (_, _) => CheckLevel();
        _levelCheck.Start();
        CheckLevel();
        Todos.Changed += () => Dispatcher.BeginInvoke(CheckLevel);
    }

    void SetStyle(string id)
    {
        if (_settings.Style == id) return;
        _settings.Style = id;
        _settings.Save();
        Theme.Style = id;
        // Reopen the menu so the change shows right away.
        var anchor = _menuAnchor;
        if (_menu is { } menu)
        {
            menu.Closed += (_, _) => Dispatcher.BeginInvoke(() => ShowMenu(anchor));
            menu.CloseAnimated();
        }
    }

    void ToggleExplorerMenu()
    {
        bool on = !ContextMenuRegistrar.IsRegistered();
        if (on) ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(Skin));
        else ContextMenuRegistrar.Unregister();
        ToastHost.Show(ToastWindow.Message(on ? "Added to Explorer" : "Removed from Explorer",
            on ? "Right-click a file, choose Show more options, then Convert with Shiki." : "Convert with Shiki is no longer in the right-click menu."));
    }

    SettingsWindow? _settingsWindow;

    void ShowSettings()
    {
        if (_settingsWindow != null) { InDashboard(_settingsWindow, isSettings: true); return; }
        _settingsWindow = NewSettings();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        InDashboard(_settingsWindow, isSettings: true);
    }

    SettingsWindow NewSettings() => new SettingsWindow(
            new OutputSetting(
                () => _settings.OutputFolder,
                folder =>
                {
                    _settings.OutputFolder = folder;
                    _settings.ChoseOutputFolder = true;
                    OutputPaths.Folder = folder;
                    _settings.Save();
                },
                () => _settings.SortOutputs,
                sort =>
                {
                    _settings.SortOutputs = sort;
                    OutputPaths.SortByType = sort;
                    _settings.Save();
                },
                OpenFolder),
            [
                new ShortcutSetting("Convert selected files", () => _settings.ConvertHotkey, text => ChangeHotkey(text, () => _settings.ConvertHotkey, v => _settings.ConvertHotkey = v)),
                new ShortcutSetting("Snip and convert", () => _settings.SnipHotkey, text => ChangeHotkey(text, () => _settings.SnipHotkey, v => _settings.SnipHotkey = v)),
                new ShortcutSetting("Record the screen", () => _settings.RecordHotkey, text => ChangeHotkey(text, () => _settings.RecordHotkey, v => _settings.RecordHotkey = v)),
                new ShortcutSetting("Pick a colour", () => _settings.ColourHotkey, text => ChangeHotkey(text, () => _settings.ColourHotkey, v => _settings.ColourHotkey = v)),
                new ShortcutSetting($"Ask {CatName}", () => _settings.AskHotkey, text => ChangeHotkey(text, () => _settings.AskHotkey, v => _settings.AskHotkey = v)),
                new ShortcutSetting($"Talk to {CatName}", () => _settings.TalkHotkey, text => ChangeHotkey(text, () => _settings.TalkHotkey, v => _settings.TalkHotkey = v)),
                new ShortcutSetting("Paw to ask", () => _settings.PawHotkey, text => ChangeHotkey(text, () => _settings.PawHotkey, v => _settings.PawHotkey = v)),
                new ShortcutSetting("What's this error?", () => _settings.ErrorHotkey, text => ChangeHotkey(text, () => _settings.ErrorHotkey, v => _settings.ErrorHotkey = v)),
                new ShortcutSetting("Meeting notes", () => _settings.NotesHotkey, text => ChangeHotkey(text, () => _settings.NotesHotkey, v => _settings.NotesHotkey = v)),
                new ShortcutSetting("Dictation (hold, talk, let go)", () => _settings.DictateHotkey, text => ChangeHotkey(text, () => _settings.DictateHotkey, v => _settings.DictateHotkey = v)),
            ],
            [
                new SettingSwitch("Right-click menu in Explorer", "Convert with Shiki, under Show more options.", ContextMenuRegistrar.IsRegistered, ToggleExplorerMenu, "files"),
                new SettingSwitch("Shift + drag opens the wheel", "Hold Shift while dragging any file.", () => _settings.ShiftDragSummon, ToggleShiftDrag, "files"),
                new SettingSwitch("Collect screenshots", "After a screenshot (Win+Shift+S or Print Screen) the cat offers to keep collecting: take a few more, then paste them all at once. On a web page it can capture the whole page, in screen-sized parts.", () => _settings.CollectScreenshots, () => { _settings.CollectScreenshots = !_settings.CollectScreenshots; _settings.Save(); ApplyShotPile(); }, "files"),
                new SettingSwitch("Glass follows what's behind", "Glass look only. Off shows popups in screen recordings.", () => _settings.LiveGlass, ToggleLiveGlass, "cat"),
                new SettingSwitch("Start with Windows", "The cat is there when you sign in.", StartupRegistration.IsEnabled, () => StartupRegistration.Set(!StartupRegistration.IsEnabled(), ExePath)),
                new SettingSwitch("Offer to take notes when a call starts", "Zoom, Teams, Meet in a browser and other calling apps. Asks once per call.", () => _settings.OfferNotes, () => { _settings.OfferNotes = !_settings.OfferNotes; _settings.Save(); }, "meetings"),
                new SettingSwitch("Send to people I know after a countdown", "A WhatsApp or email to someone you've written to before goes after 5 seconds (say \"stop\" or press Stop) instead of asking first. New people still get a card.", () => _settings.CountdownSends, () => { _settings.CountdownSends = !_settings.CountdownSends; _settings.Save(); }, "sending"),
                new SettingSwitch("Quick sends and reminders", "\"WhatsApp Mummy that I'm on my way\", \"email this to me\", \"remind me at 5 to call the bank\": done in a second on this PC, without waiting for Claude. Anything unclear still goes to Claude.", () => _settings.QuickPath, () => { _settings.QuickPath = !_settings.QuickPath; _settings.Save(); }, "sending"),
                new SettingSwitch("Morning briefing", $"The first \"Hey {CatName}\" of the day (after 4 am), said on its own, tells you today's meetings, the emails that matter and tasks due. Say \"brief me\" or \"good morning\" any time for it.", () => _settings.BriefingOnWake, () => { _settings.BriefingOnWake = !_settings.BriefingOnWake; _settings.Save(); }, "day"),
                new SettingSwitch("Keep my last 30 voice requests", "As sound files on this PC only (Shiki's data folder, voice-clips), so speech engines can be tested on your own voice. Nothing is uploaded by itself.", () => _settings.KeepVoiceClips, () => { _settings.KeepVoiceClips = !_settings.KeepVoiceClips; VoiceClips.On = _settings.KeepVoiceClips; _settings.Save(); }, "voiceadvanced"),
                new SettingSwitch("Privacy guard", "Card numbers, Aadhaar and PAN numbers, passwords and keys in documents or copied text are hidden on this PC before anything goes to Claude.", () => _settings.PrivacyGuard, () => { _settings.PrivacyGuard = !_settings.PrivacyGuard; Core.Ai.PrivacyGuard.On = _settings.PrivacyGuard; _settings.Save(); }, "privacy"),
                new SettingSwitch("Suggest what to do with downloads", "A HEIC photo, a huge video, an archive: one tap to fix it. Worked out on this PC.", () => _settings.SuggestDownloads, () => { _settings.SuggestDownloads = !_settings.SuggestDownloads; _settings.Save(); }, "files"),
                new SettingSwitch($"{CatName} says a word now and then", "Welcome back, what a job saved, your tasks for today. Never over full-screen apps.", () => _settings.Quips, () => { _settings.Quips = !_settings.Quips; _settings.Save(); }, "cat"),
                new SettingSwitch("Keep meeting recordings", "Also save a meeting's sound (M4A, with your recordings). Off: only the notes and the transcript stay.", () => _settings.KeepMeetingAudio, () => { _settings.KeepMeetingAudio = !_settings.KeepMeetingAudio; _settings.Save(); }, "meetings"),
            ],
            new AiSetting(
                CatName,
                () => _settings.AiEnabled,
                () => { _settings.AiEnabled = !_settings.AiEnabled; _settings.Save(); ApplyWake(); },
                () => _settings.VoiceEngine,
                engine => { _settings.VoiceEngine = engine; _settings.Save(); Speech.OnScribe = engine == "elevenlabs"; },
                () => _settings.WakeEngine,
                engine => { _settings.WakeEngine = engine; _settings.Save(); Speech.WakeOnDeepgram = engine == "deepgram"; },
                () => _settings.DeepgramSeconds < 1 ? "" : $"Deepgram so far: {_settings.DeepgramSeconds / 60:0.0} minutes of speech (about ${_settings.DeepgramSeconds / 60 * 0.0077:0.00} at Nova-3's streaming price).",
                () => _settings.VoiceAnswers,
                () => { _settings.VoiceAnswers = !_settings.VoiceAnswers; _settings.Save(); },
                () => _settings.FollowUpSeconds,
                seconds => { _settings.FollowUpSeconds = seconds; _settings.Save(); },
                () => _settings.WakeWord && _settings.WakeVariants.Count > 0,
                ToggleWake,
                TeachName,
                () => _settings.VoiceLock,
                () => { _settings.VoiceLock = !_settings.VoiceLock; _settings.Save(); },
                () => _settings.VoiceLockLevel,
                level => { _settings.VoiceLockLevel = level; _settings.Save(); },
                () => _settings.SpeechLanguage,
                language => { _settings.SpeechLanguage = language; Speech.Language = language; _settings.Save(); },
                () => _settings.HindiScript,
                script => { _settings.HindiScript = script; _settings.Save(); },
                () => _settings.TalkBack,
                mode => { _settings.TalkBack = mode; _settings.Save(); },
                () => _settings.TalkVoice,
                voice =>
                {
                    _settings.TalkVoice = voice;
                    _settings.Save();
                    _ = Mouth.Say($"Hi, I'm {CatName}. This is how I'll sound.", voice);
                },
                () => _settings.DeepgramSpokenChars < 1 ? "" : $"Read aloud so far: {_settings.DeepgramSpokenChars:N0} characters (about ${_settings.DeepgramSpokenChars / 1000.0 * 0.03:0.00}).",
                () => _settings.AiRequests == 0 ? "Nothing spent yet." : $"Spent so far: {AskView.Money(_settings.AiSpentUsd)} over {_settings.AiRequests:N0} requests.",
                () => Process.Start(new ProcessStartInfo(AiLog.FilePath) { UseShellExecute = true }),
                () => _settings.TalkHotkey),
            new RoutineSetting(CatName, () => Routines.All, routine => RunRoutine(routine, spoken: false), name => Routines.Remove(name),
                () => Preferences.All, fact => Preferences.Remove(fact), () => Habits.Rules, Habits.Stop),
            new GmailSetting(
                () => Core.Mail.Gmail.Connected ? _settings.GmailAccount ?? "your Gmail" : null,
                () => Core.Mail.Gmail.HasClient,
                Core.Mail.Gmail.SaveClient,
                async () =>
                {
                    var address = await Core.Mail.Gmail.Connect(System.Threading.CancellationToken.None);
                    _settings.GmailAccount = address;
                    _settings.Save();
                    return address;
                },
                async () =>
                {
                    await Core.Mail.Gmail.Disconnect();
                    _settings.GmailAccount = null;
                    _settings.Save();
                },
                url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(),
                () => Core.Mail.Gmail.HasCalendar,
                () => Core.Mail.Gmail.HasContacts),
            new CatSetting(
                () => CatSprites.Skins.Where(c => CatLevel.CatUnlocked(c.Id, Level())).Select(c => (c.Id, CatsPage.NameOf(c, _settings), (ImageSource)CatSprites.Head(c))).ToList(),
                () => _settings.Cat, SetCat,
                () => _settings.Style, SetStyle,
                () => _settings.CatLife, life => { _settings.CatLife = life; _settings.Save(); _buddy?.SetLife(life); }),
            new IndexSetting(() => _settings.SearchInsideFiles, ToggleIndex, IndexStatus, () => { if (_settings.SearchInsideFiles) { _index ??= new ContentIndex(); _index.Rebuild(); } }),
            _phone == null ? null : new PhoneSetting(
                () => _phone.Phones.Select(p => (p.Id, p.Name, _phone.IsConnected(p) ? "Connected now"
                    : p.LastSeen is { } seen ? $"Last seen {seen:d MMM, HH:mm}" : "Not seen yet")).ToList(),
                () => ShowPhoneConnect(),
                id => _phone.Remove(id),
                () => _phone.PhotoFolder,
                () => OpenFolder(_phone.PhotoFolder)));

    /// <summary>Tries a new shortcut from the settings window; puts the old one back if it's taken.</summary>
    bool ChangeHotkey(string text, Func<string> get, Action<string> set)
    {
        var previous = get();
        // Shortcuts can't share keys.
        if (text != previous && (text == _settings.SnipHotkey || text == _settings.ColourHotkey || text == _settings.ConvertHotkey || text == _settings.RecordHotkey || text == _settings.AskHotkey || text == _settings.TalkHotkey || text == _settings.PawHotkey || text == _settings.ErrorHotkey || text == _settings.NotesHotkey || text == _settings.DictateHotkey)) return false;
        set(text);
        if (BindHotkeys(quiet: true))
        {
            _settings.Save();
            return true;
        }
        set(previous);
        BindHotkeys(quiet: true);
        return false;
    }

    void ToggleLiveGlass()
    {
        _settings.LiveGlass = !_settings.LiveGlass;
        _settings.Save();
        LiveGlass.Enabled = _settings.LiveGlass;
    }

    void ToggleShiftDrag()
    {
        _settings.ShiftDragSummon = !_settings.ShiftDragSummon;
        _settings.Save();
        if (_settings.ShiftDragSummon) _hook?.Start(); else _hook?.Stop();
    }

    // ---- Buddy mode ----

    void EnterBuddyMode()
    {
        if (_tray != null) return;
        Log.Info($"Cat started (process {Environment.ProcessId})");
        _headless = false;
        _idleTimer.Stop();

        _tray = new TrayIcon(Skin);
        _tray.Clicked += () => ShowDashboard();
        _tray.MenuRequested += ShowMenu;

        _buddy = new CatWindow(_settings);
        _buddy.FilesDropped += (paths, center) => ShowWheel(FileList.Expand(paths), CatSpot(center));
        _buddy.MenuRequested += ShowMenu;
        _buddy.PickFilesRequested += PickFiles;
        ToastHost.Avoid = () => _buddy is { IsVisible: true } b ? new Rect(b.Left, b.Top, b.Width, b.Height) : null;
        if (_settings.BuddyVisible) _buddy.Show();

        _hook = new GlobalMouseHook();
        _hook.ShiftDragStarted += (x, y) => Dispatcher.BeginInvoke(() => OpenDragWheel(x, y));
        _hook.DragEnded += () => Dispatcher.BeginInvoke(() =>
        {
            _dragWheel?.OnDragEnded();
            _buddy?.Notice(false);
        });
        if (_settings.ShiftDragSummon) _hook.Start();

        _hotkeys = new Hotkeys();
        BindHotkeys(quiet: false);
        ApplyWake();
        StartReminders();
        StartPhoneLink();
        ApplyShotPile();
        WatchPc();
        _calls = new CallWatcher();
        _calls.Started += OfferNotes;
        // Deepgram's running total, for Settings.
        Deepgram.Sent += seconds => Dispatcher.BeginInvoke(() => { _settings.DeepgramSeconds += seconds; _settings.Save(); });
        Speaker.Sent += characters => Dispatcher.BeginInvoke(() => { _settings.DeepgramSpokenChars += characters; _settings.Save(); });
    }

    /// <summary>(Re)binds every shortcut from settings. Unless quiet, says which one another app owns.</summary>
    bool BindHotkeys(bool quiet)
    {
        bool snip = Bind(ref _snipHotkeyId, _settings.SnipHotkey, StartSnip, quiet);
        bool colour = Bind(ref _colourHotkeyId, _settings.ColourHotkey, StartColourPicker, quiet);
        bool convert = Bind(ref _convertHotkeyId, _settings.ConvertHotkey, ConvertSelection, quiet);
        bool record = Bind(ref _recordHotkeyId, _settings.RecordHotkey, StartRecording, quiet);
        bool ask = Bind(ref _askHotkeyId, _settings.AskHotkey, () => ShowAskBubble(), quiet);
        bool talk = Bind(ref _talkHotkeyId, _settings.TalkHotkey, Talk, quiet);
        bool paw = Bind(ref _pawHotkeyId, _settings.PawHotkey, () => StartPaw(), quiet);
        bool error = Bind(ref _errorHotkeyId, _settings.ErrorHotkey, ExplainError, quiet);
        bool notes = Bind(ref _notesHotkeyId, _settings.NotesHotkey, ToggleNotes, quiet);
        bool dictate = Bind(ref _dictateHotkeyId, _settings.DictateHotkey, Dictate, quiet);
        return snip && colour && convert && record && ask && talk && paw && error && notes && dictate;
    }

    bool Bind(ref int id, string text, Action handler, bool quiet)
    {
        if (_hotkeys == null) return false;
        if (id > 0) _hotkeys.Unregister(id);
        id = -1;
        if (Hotkey.Parse(text) is not { } hotkey) return false;
        if (_hotkeys.Register(hotkey, handler, out int newId))
        {
            id = newId;
            return true;
        }
        if (!quiet)
            ToastHost.Show(ToastWindow.Message($"{hotkey} is taken",
                "Another app already uses that shortcut. Pick a different one in Settings."));
        return false;
    }

    static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // ---- The dashboard ----

    DashboardWindow? _dashboard;

    public void ShowDashboard(DashPage page = DashPage.Home, bool show = true)
    {
        _menu?.CloseAnimated();
        if (_dashboard == null)
        {
            _dashboard = new DashboardWindow(new DashboardHost
            {
                Settings = _settings,
                SaveSettings = _settings.Save,
                PickFiles = PickFiles,
                Convert = files =>
                {
                    NativeMethods.GetCursorPos(out var cursor);
                    ShowWheel(files, new Point(cursor.X, cursor.Y));
                },
                Run = (action, files) => StartJob(action, files),
                Snip = StartSnip,
                PickColour = StartColourPicker,
                Record = StartRecording,
                Paw = () => StartPaw(),
                TrySaying = TrySaying,
                TryTool = tool =>
                {
                    switch (tool)
                    {
                        case "paw": StartPaw(); break;
                        case "error": ExplainError(); break;
                        case "snip": StartSnip(); break;
                        case "record": StartRecording(); break;
                        case "colour": StartColourPicker(); break;
                        case "notes": ToggleNotes(); break;
                    }
                },
                ExplainError = ExplainError,
                NotesRunning = () => _meeting != null,
                ToggleNotes = ToggleNotes,
                AskAboutMeeting = AskAboutMeeting,
                RewriteNotes = meeting => _ = RewriteNotes(meeting),
                MakeQr = () => ShowQr(""),
                Paste = PasteToCat,
                OpenSettings = ShowSettings,
                ExplorerMenuOn = ContextMenuRegistrar.IsRegistered,
                AddExplorerMenu = () => { if (!ContextMenuRegistrar.IsRegistered()) ToggleExplorerMenu(); },
                StartsWithWindows = StartupRegistration.IsEnabled,
                StartWithWindows = () => StartupRegistration.Set(true, ExePath),
                SetCat = SetCat,
                Level = Level,
                Points = Points,
                Stats = Stats,
                ConnectPhone = () => ShowPhoneConnect(),
                RenameCat = RenameCat,
                SetAccessory = SetAccessory,
                Chat = Chat,
                Voice = Voice,
                TeachName = TeachName,
                NameTaught = () => _settings.WakeVariants.Count > 0,
                CatName = () => CatName,
                OpenFile = OpenFile,
            });
            _dashboard.Closed += (_, _) => _dashboard = null;
        }
        if (show) _dashboard.Show(page);
    }

    /// <summary>Opens the dashboard with this editor (or settings) inside it.</summary>
    void InDashboard(Window window, bool isSettings = false)
    {
        _menu?.CloseAnimated();
        if (_dashboard == null) ShowDashboard();
        _dashboard!.ShowWindow(window, isSettings);
    }

    // ---- Ask the cat ----

    CatChat? _chat;
    VoiceInput? _voice;
    AskBubble? _bubble;

    VoiceInput Voice
    {
        get
        {
            if (_voice != null) return _voice;
            _voice = new VoiceInput(Chat, _settings, () => CatName) { Commands = VoiceCommand, Quiet = () => Mouth.Quiet(), Interrupt = () => _mouth?.Stop() };
            _voice.AnswerCardsAloud();
            // The conversation is over: put the bubble away if you're not using it.
            // Attentive eyes while you're talking to it.
            _voice.Changed += () =>
            {
                _buddy?.Listening(_voice.State is VoiceState.Waiting or VoiceState.Hearing);
                // A reply is coming: wake the speakers now, so its first words aren't lost.
                if (_voice.State is VoiceState.Hearing or VoiceState.Understanding && _settings.TalkBack != "off")
                {
                    Mouth.Warm();
                    if (_settings.TalkVoice.StartsWith("el:")) ElevenLabs.Warm();
                }
            };
            _voice.ConversationEnded += () => Dispatcher.BeginInvoke(() => { if (_bubble is { IsActive: false } bubble) bubble.Close(); },
                DispatcherPriority.ApplicationIdle);
            return _voice;
        }
    }

    // ---- "Hey <cat name>" ----

    WakeListener? _wake;
    WakeSetupWindow? _teach;

    /// <summary>Starts or stops the always-on listener to match settings (it needs Ask the cat, a key and a taught name).</summary>
    void ApplyWake()
    {
        Speech.WakeOnDeepgram = _settings.WakeEngine == "deepgram";
        Speech.OnScribe = _settings.VoiceEngine == "elevenlabs";
        bool want = _settings.WakeWord && _settings.AiEnabled && _settings.WakeVariants.Count > 0 && ClaudeKey.Exists;
        if (want && _wake == null)
        {
            _wake = new WakeListener(() => CatName, () => _settings.WakeVariants,
                // Hear nothing while the cat is already listening or thinking, while recording, or while being taught.
                () => _recording != null || (_voice?.Active ?? false) || (_chat?.Busy ?? false) || (_teach?.Listening ?? false) || _dictation != null
                    // While the cat talks it would hear itself, unless only the owner's voice can wake it.
                    || ((_mouth?.Speaking ?? false) && !(_settings.VoiceLock && VoicePrint.HasPrint)),
                () => _settings.VoiceLock && VoicePrint.HasPrint ? VoicePrint.Enough(_settings.VoiceLockLevel) : null);
            _wake.Woke += OnWoke;
            // "Hey Sky, …": show the bubble straight away, before the rest is written down.
            _wake.Waking += () => { if (_voice is not { Active: true } && !Chat.Busy) { ShowVoicePill(); _buddy?.Act("purr"); } };
            _wake.Start();
            Speech.Whisper.KeepLoaded = true;
            // Load the model now, so the first "Hey Sky" is as quick as the rest.
            // With Deepgram, this also opens the connection, so the first check is as quick as the rest.
            _ = System.Threading.Tasks.Task.Run(() => WakeListener.Recognize(new float[16000], CatName, default));
        }
        else if (!want && _wake != null)
        {
            _wake.Dispose();
            _wake = null;
            Speech.Whisper.KeepLoaded = false;
        }
    }

    void ToggleWake()
    {
        _settings.WakeWord = !_settings.WakeWord;
        _settings.Save();
        if (_settings.WakeWord && _settings.WakeVariants.Count == 0) TeachName();
        ApplyWake();
    }

    /// <summary>"Hey Latte": the bubble, listening. "Hey Latte, merge my PDFs": straight to the request.</summary>
    void OnWoke(string after)
    {
        if (_voice is { Active: true } || Chat.Busy) return;
        if (!ShowVoicePill()) return;
        _buddy?.Act("purr");
        // The first "Hey Sky" of the day, on its own: the day's briefing (then it listens as usual).
        bool first = Briefing.FirstToday(_settings.LastWake, DateTime.Now);
        _settings.LastWake = DateTime.Now;
        _settings.Save();
        if (first && _settings.BriefingOnWake && after.Length == 0) _ = Brief(spoken: true);
        else if (after.Length == 0) _ = Voice.Listen();
        else _ = Voice.Use(after);
    }

    // How much of a reply is read aloud before "the rest is on screen" (more for the briefing).
    int _speakLimit = 320;

    /// <summary>
    /// The day's briefing: meetings, the emails that matter and tasks due, gathered here, written
    /// up by Claude in a few spoken sentences, then it listens for what's next ("reply to Rahul").
    /// </summary>
    async System.Threading.Tasks.Task Brief(bool spoken)
    {
        AiLog.Write("briefing", spoken ? "spoken" : "typed");
        Chat.Say(new CatDid("\uE706", "Your day at a glance"));
        string note;
        try
        {
            var (facts, found) = await Briefing.Gather(CatName, default);
            note = facts;
            AiLog.Write("briefing", $"{found.Events} events, {found.Mail} unread emails, {found.Tasks} tasks due");
        }
        catch (Exception ex)
        {
            Log.Error("Briefing failed", ex);
            note = $"Briefing: say good morning, and that you couldn't get today's details ({ex.Message}).";
        }
        Chat.NextNote = note;
        _speakLimit = 900;
        try
        {
            await Chat.Send("Brief me on my day.", spoken, shown: false);
        }
        finally
        {
            _speakLimit = 320;
        }
        if (!spoken || _settings.FollowUpSeconds <= 0) return;
        while (_mouth?.Speaking == true) await System.Threading.Tasks.Task.Delay(150);
        if (!Voice.Active && !Chat.Busy) _ = Voice.Listen(followUp: true);
    }

    /// <summary>Meet your cat: name it and say "Hey &lt;name&gt;" three times.</summary>
    void TeachName()
    {
        _teach?.Close();
        _teach = new WakeSetupWindow(CatName, _settings.WakeVariants, _settings.WakeWord, (name, learned, listen) =>
        {
            _settings.CatName = name;
            _settings.CatNames[_settings.Cat] = name.Trim();
            _settings.WakeVariants = learned;
            _settings.WakeWord = listen;
            _settings.Save();
            AiLog.Write("named", $"{name}, wakes to: {string.Join(", ", learned)}");
            ApplyWake();
        });
        _teach.Closed += (_, _) => _teach = null;
        InDashboard(_teach);
    }

    /// <summary>The talk shortcut: the bubble, already listening. Pressed again, it stops and sends.</summary>
    void Talk()
    {
        Log.Info($"Talk shortcut ({(_voice is { Active: true } ? "sends what was heard" : "starts listening")})");
        if (_voice is { Active: true })
        {
            _voice.Finish();
            return;
        }
        ShowAskBubble();
        if (_bubble != null && !Chat.Busy) _ = Voice.Listen();
    }

    /// <summary>The name of the cat on the desktop: used everywhere (Ask, the voice, the pages).</summary>
    string CatName => CatsPage.NameOf(Skin, _settings);

    /// <summary>
    /// A cat gets a name of its own (empty, or its own name: back to that). If it's the cat on the
    /// desktop, the new name is used everywhere at once.
    /// </summary>
    void RenameCat(string id, string name)
    {
        name = string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > 24) name = name[..24];
        var skin = CatSprites.Skin(id);
        bool onDesktop = id == _settings.Cat;
        var before = CatName;
        if (name.Length == 0 || name == skin.Name) _settings.CatNames.Remove(id);
        else _settings.CatNames[id] = name;
        if (onDesktop) _settings.CatName = CatName;
        _settings.Save();
        AiLog.Write("named", $"{skin.Name} is now called {CatsPage.NameOf(skin, _settings)}");
        if (!onDesktop || before == CatName) return;
        RefreshKeyTerms();
        // "Hey <old name>" was what it learned: offer to teach the new one.
        if (_settings.WakeWord && _settings.WakeVariants.Count > 0)
            ToastHost.Show(ToastWindow.Message($"Now called {CatName}", $"Teach it \"Hey {CatName}\" so it wakes to its new name.", null, ("Teach it", TeachName)));
    }

    CatChat Chat => _chat ??= new CatChat(new CatHands
    {
        Start = StartForCat,
        Reveal = RevealAndPoint,
        Open = OpenFile,
        UseTool = UseToolForCat,
        ReadScreen = () => ExplorerSelection.Frontmost() is { } front ? new ScreenContext(front.Folder, front.Selected) : null,
        CatName = () => CatName,
        Spent = cost =>
        {
            _settings.AiSpentUsd += cost;
            _settings.AiRequests++;
            _settings.Save();
        },
        Copy = TrySetClipboard,
        LookAtScreen = () => WindowShots.Capture(WindowShots.TopOtherWindow()),
        ReadAloud = ReadAloud,
        ReadClipboard = ReadClipboardForCat,
        StartAgent = StartAgent,
        SendWhatsApp = SendWhatsApp,
        Quick = VoiceCommand,
        CountdownSends = () => _settings.CountdownSends,
        PersonTalking = () => _voice is { State: VoiceState.Hearing or VoiceState.Understanding } || _mouth?.Speaking == true,
        SearchContents = (words, max) => _index is { } index ? (index.Search(words, max), index.Count, index.Indexing) : null,
        RunRoutine = name => Routines.Find(name) is { } routine ? RunRoutine(routine, spoken: false) : $"There's no routine called \"{name}\".",
        Replied = (sentence, spoken, first) =>
        {
            // Not over a document being read aloud.
            if (_mouth?.Reading == true) return;
            if (!(_settings.TalkBack == "always" || (_settings.TalkBack == "spoken" && spoken))) return;
            // A long answer: the first few sentences aloud, the rest on screen.
            if (first) _spokenChars = 0;
            if (_spokenChars >= _speakLimit) return;
            _spokenChars += sentence.Length;
            var say = _spokenChars >= _speakLimit ? sentence + " The rest is on screen." : sentence;
            if (first) _ = Mouth.Say(say, _settings.TalkVoice);
            else Mouth.Append(say, _settings.TalkVoice);
        },
    });

    ToastWindow? _readingToast;

    /// <summary>Reads a document (or a summary) aloud, with a Stop button while it does.</summary>
    void ReadAloud(string title, string text)
    {
        _readingToast?.CloseAnimated();
        ToastWindow? toast = null;
        toast = _readingToast = ToastWindow.Message($"Reading {title} aloud", $"Say \"Hey {CatName}, stop\" or press Stop.", null,
            ("Stop", () => { _mouth?.Stop(); toast!.CloseAnimated(); }));
        ToastHost.Show(toast);
        Dispatcher.BeginInvoke(async () =>
        {
            await Mouth.Say(text, _settings.TalkVoice, full: true);
            if (_readingToast == toast) { toast.CloseAnimated(); _readingToast = null; }
        });
    }

    // ---- Search inside files ----

    ContentIndex? _index;

    void StartIndex()
    {
        try
        {
            _index ??= new ContentIndex();
            _index.Start();
        }
        catch (Exception ex)
        {
            Log.Error("The content index couldn't start", ex);
            _index = null;
        }
    }

    void ToggleIndex()
    {
        _settings.SearchInsideFiles = !_settings.SearchInsideFiles;
        _settings.Save();
        if (_settings.SearchInsideFiles) StartIndex();
        else _index?.Stop();
    }

    string IndexStatus() => !_settings.SearchInsideFiles ? "Off: documents are found by name only."
        : _index == null ? "Starting…"
        : _index.Indexing ? $"Reading your documents for the first time, quietly in the background: {_index.Count:N0} so far."
        : $"{_index.Count:N0} documents indexed, kept up to date as they change.";

    // ---- WhatsApp ----

    /// <summary>
    /// WhatsApp opens on the chat with the message typed (its own link, no searching), and once
    /// its window is in front, Enter sends it. If it never comes up, the message waits there.
    /// With no WhatsApp app on this PC, WhatsApp Web does it instead.
    /// </summary>
    async System.Threading.Tasks.Task<string> SendWhatsApp(string phone, string text, bool web, IReadOnlyList<string> files)
    {
        if (!web && !WhatsAppWeb.AppInstalled())
        {
            web = true;
            AiLog.Write("whatsapp", "no WhatsApp app on this PC: using WhatsApp Web");
        }
        if (files.Count > 0) return await SendWhatsAppFiles(phone, text, web, files);
        if (web) return await SendWhatsAppWeb(phone, text);
        var link = $"whatsapp://send?phone={phone}&text={Uri.EscapeDataString(text)}";
        Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
        AiLog.Write("whatsapp", $"to {phone}: {text.Length} characters");
        for (int i = 0; i < 40; i++)
        {
            await System.Threading.Tasks.Task.Delay(250);
            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var pid);
            string? name = null;
            try { using var process = Process.GetProcessById((int)pid); name = process.ProcessName; } catch (ArgumentException) { }
            if (name != null && name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
            {
                // Give the chat a moment to load the message into the box.
                await System.Threading.Tasks.Task.Delay(1200);
                TypeText.PressEnter();
                return "Sent on WhatsApp.";
            }
        }
        return "WhatsApp is open with the message ready: press Enter to send it.";
    }

    /// <summary>Opens WhatsApp Web on a chat in Chrome (or the usual browser); says which.</summary>
    static string OpenWhatsAppWeb(string link)
    {
        try
        {
            Process.Start(new ProcessStartInfo("chrome", link) { UseShellExecute = true })?.Dispose();
            return "Chrome";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
            return "your browser";
        }
    }

    /// <summary>
    /// WhatsApp Web, on the chat with the message typed. Shiki waits until the chat's message box
    /// is really there (reading the page the way a screen reader does), then presses its Send
    /// button. If the page can't be read, it falls back to Enter once the tab has had time to load.
    /// </summary>
    async System.Threading.Tasks.Task<string> SendWhatsAppWeb(string phone, string text)
    {
        var link = $"https://web.whatsapp.com/send?phone={phone}&text={Uri.EscapeDataString(text)}";
        var browser = OpenWhatsAppWeb(link);
        AiLog.Write("whatsapp", $"web, to {phone}: {text.Length} characters");
        void Note(string step) => AiLog.Write("whatsapp", step);
        var opened = await WhatsAppWeb.WaitForChat(TimeSpan.FromSeconds(35), Note, () => Dispatcher.Invoke(() => OpenWhatsAppWeb(link)));
        if (WhatsAppWeb.NotOnWhatsApp) return $"WhatsApp says +{phone} isn't on WhatsApp, so nothing was sent. Check the number.";
        if (opened is { } chat)
        {
            WhatsAppWeb.FocusBox(chat.Window, chat.Box);
            if (await WhatsAppWeb.PressSend(chat.Window, TimeSpan.FromSeconds(6)))
            {
                Note("pressed Send");
                return "Sent on WhatsApp Web.";
            }
            Note("no Send button (the message didn't fill in)");
            return $"WhatsApp Web is open in {browser} on that chat, but the message didn't fill in: type it there.";
        }
        // The page couldn't be read: the old way, Enter once it has had time to load.
        if (WhatsAppWebInFront())
        {
            await System.Threading.Tasks.Task.Delay(2000);
            if (WhatsAppWebInFront())
            {
                TypeText.PressEnter();
                return $"WhatsApp Web is open in {browser} on that chat, and Enter was pressed to send it. If the message is still in the box, press Enter.";
            }
        }
        return $"WhatsApp Web is open in {browser} with the message typed: press Enter to send it.";
    }

    /// <summary>
    /// Photos and files on WhatsApp, with no mouse: the files go on the clipboard (as if copied in
    /// File Explorer), the chat opens, Ctrl+V puts them in WhatsApp's send preview, the text goes in
    /// as the caption and Send sends. On WhatsApp Web each step is checked on the page itself (the
    /// message box is there, the preview opened, the Send button is pressed). If a step doesn't
    /// come, the files stay on the clipboard to paste by hand; otherwise the clipboard's text is
    /// put back.
    /// </summary>
    async System.Threading.Tasks.Task<string> SendWhatsAppFiles(string phone, string text, bool web, IReadOnlyList<string> files)
    {
        string? kept = null;
        try { if (Clipboard.ContainsText()) kept = Clipboard.GetText(); } catch (System.Runtime.InteropServices.ExternalException) { }
        var list = new System.Collections.Specialized.StringCollection();
        list.AddRange(files.ToArray());
        Clipboard.SetFileDropList(list);
        string what = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files";
        AiLog.Write("whatsapp", $"{(web ? "web, " : "")}to {phone}: {what}{(text.Length > 0 ? $" + {text.Length} characters" : "")}");
        string left = $"The chat is open and the {(files.Count == 1 ? "file is" : "files are")} on your clipboard: press Ctrl+V, then Enter.";
        bool sent = false;
        try
        {
            string result = web ? await FilesOnWhatsAppWeb(phone, text, what, left, files) : await FilesOnWhatsAppApp(phone, text, what, left);
            sent = result.StartsWith("Sent");
            return result;
        }
        finally
        {
            // Sent: put back what was copied before (a little later, so the paste has happened).
            // Not sent: the files stay on the clipboard, ready to paste.
            if (sent)
            {
                await System.Threading.Tasks.Task.Delay(1500);
                try { if (kept != null) Clipboard.SetText(kept); } catch (System.Runtime.InteropServices.ExternalException) { }
            }
        }
    }

    async System.Threading.Tasks.Task<string> FilesOnWhatsAppWeb(string phone, string text, string what, string left, IReadOnlyList<string> files)
    {
        var link = $"https://web.whatsapp.com/send?phone={phone}";
        var browser = OpenWhatsAppWeb(link);
        void Note(string step) => AiLog.Write("whatsapp", step);
        var opened = await WhatsAppWeb.WaitForChat(TimeSpan.FromSeconds(40), Note, () => Dispatcher.Invoke(() => OpenWhatsAppWeb(link)));
        if (WhatsAppWeb.NotOnWhatsApp) return $"WhatsApp says +{phone} isn't on WhatsApp, so nothing was sent. Check the number.";
        if (opened is not { } chat)
            return $"WhatsApp Web is open in {browser}, but the chat didn't finish loading. The {(what.EndsWith("files") ? "files are" : "file is")} on your clipboard: open the chat, press Ctrl+V, then Enter.";
        WhatsAppWeb.FocusBox(chat.Window, chat.Box);
        await System.Threading.Tasks.Task.Delay(300);
        // Attach the files the way a person does (pasting files isn't passed on to WhatsApp Web).
        if (!await WhatsAppWeb.AttachFiles(chat.Window, files, Note)) return left;
        // The preview opens with a caption box and a Send button (bigger files take a moment).
        bool up = false;
        for (int i = 0; i < 20 && !up; i++)
        {
            await System.Threading.Tasks.Task.Delay(500);
            up = await WhatsAppWeb.PreviewUp(chat.Window);
        }
        if (!up) { Note("WhatsApp's preview didn't open"); return left; }
        if (text.Length > 0)
        {
            await System.Threading.Tasks.Task.Delay(300);
            NativeMethods.ForceForeground(new IntPtr(chat.Window.Current.NativeWindowHandle));
            await System.Threading.Tasks.Task.Delay(150);
            if (!WhatsAppWebInFront() || !TypeText.Type(text)) { Note("couldn't type the caption"); return "The files are in WhatsApp's preview: add the caption and press Enter."; }
        }
        if (await WhatsAppWeb.PressSend(chat.Window, TimeSpan.FromSeconds(5)))
        {
            Note("pressed Send");
            return $"Sent {what} on WhatsApp Web.";
        }
        // No Send button to press: Enter does the same in the preview.
        if (WhatsAppWebInFront())
        {
            TypeText.PressEnter();
            Note("pressed Enter (no Send button found)");
            return $"Sent {what} on WhatsApp Web.";
        }
        return "The files are in WhatsApp's preview: press Enter to send.";
    }

    async System.Threading.Tasks.Task<string> FilesOnWhatsAppApp(string phone, string text, string what, string left)
    {
        Process.Start(new ProcessStartInfo($"whatsapp://send?phone={phone}") { UseShellExecute = true })?.Dispose();
        bool up = false;
        for (int i = 0; i < 60 && !up; i++)
        {
            await System.Threading.Tasks.Task.Delay(250);
            up = WhatsAppAppInFront();
        }
        if (!up) { AiLog.Write("whatsapp", "the WhatsApp app didn't come to the front"); return left; }
        // The chat loads, then the files go in.
        await System.Threading.Tasks.Task.Delay(1800);
        await TypeText.WaitForModifiersUp();
        if (!WhatsAppAppInFront()) return left;
        TypeText.Paste();
        // The send preview opens with the caption box ready.
        await System.Threading.Tasks.Task.Delay(1500);
        if (!WhatsAppAppInFront()) return left;
        if (text.Length > 0 && !TypeText.Type(text)) return left;
        await System.Threading.Tasks.Task.Delay(300);
        if (!WhatsAppAppInFront()) return "The files and caption are ready in WhatsApp: press Enter to send.";
        TypeText.PressEnter();
        return $"Sent {what} on WhatsApp.";
    }

    /// <summary>The WhatsApp app is the window in front.</summary>
    static bool WhatsAppAppInFront()
    {
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>A browser window in front whose tab is WhatsApp.</summary>
    static bool WhatsAppWebInFront()
    {
        var front = NativeMethods.GetForegroundWindow();
        var title = new System.Text.StringBuilder(256);
        GetWindowText(front, title, title.Capacity);
        if (!title.ToString().Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) return false;
        NativeMethods.GetWindowThreadProcessId(front, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi";
        }
        catch (ArgumentException) { return false; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

    // ---- The agent: mouse and keyboard ----

    ComputerAgent? _agent;

    /// <summary>Starts the agent on a task; it reports back to the chat when it's done.</summary>
    string StartAgent(string task)
    {
        if (_agent != null) return "I'm already working on something on screen. Stop it first (the pill at the top right).";
        if (ClaudeKey.Read() is not { } key) return "I need a Claude API key first.";
        _bubble?.Close();
        // Out of the way, so the screen is the person's apps and not the dashboard.
        if (_dashboard is { IsVisible: true } dashboard) dashboard.WindowState = WindowState.Minimized;
        AgentPill? pill = null;
        var agent = _agent = new ComputerAgent(new RealScreen(), question => pill!.Ask(question), url => OpenWebsite(new Uri(url)), Apps.Open);
        pill = new AgentPill(agent, task);
        pill.StopPressed += () =>
        {
            if (agent.Stopped) return;
            agent.Stop();
            AiLog.Write("agent stopped", "Stop pressed");
            Chat.Say(new CatDid("\uE71A", "Stopped."));
        };
        pill.Paused += agent.Pause;
        pill.Resumed += agent.Resume;
        pill.Show();
        _buddy?.Listening(true);
        Dispatcher.BeginInvoke(async () =>
        {
            string summary;
            try
            {
                summary = await agent.Run(task, key);
            }
            catch (Exception ex)
            {
                Log.Error("The agent failed", ex);
                summary = CatChat.Explain(ex).Message ?? "Stopped.";
            }
            _agent = null;
            pill.Close();
            _buddy?.Listening(false);
            _settings.AiSpentUsd += agent.Cost;
            _settings.AiRequests++;
            _settings.Save();
            Chat.Report("\uE8A7", summary);
            AiLog.Write("agent cost", $"${agent.Cost:0.000} over {agent.Steps} steps");
            ToastHost.Show(ToastWindow.Message(agent.Stopped ? "Stopped" : "Done", summary));
        });
        return "Started: watch the pill at the top right. Click or type yourself and it pauses.";
    }

    // ---- Live captions ----

    LiveCaptions? _captions;
    CaptionsWindow? _captionsWindow;

    /// <summary>Captions for whatever the PC plays, translated when a language is given.</summary>
    void StartCaptions(string? translateTo)
    {
        StopCaptions();
        try
        {
            var captions = _captions = new LiveCaptions(translateTo, ClaudeKey.Read, cost =>
                Dispatcher.BeginInvoke(() => { _settings.AiSpentUsd += cost; _settings.AiRequests++; _settings.Save(); }));
            var window = _captionsWindow = new CaptionsWindow(captions.Engine, captions.TranslateTo);
            captions.Partial += text => Dispatcher.BeginInvoke(() => { if (_captions == captions) window.SetPartial(text); });
            captions.Line += (text, translated) => Dispatcher.BeginInvoke(() => { if (_captions == captions) window.AddLine(text, translated); });
            window.CloseRequested += StopCaptions;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Error("Captions couldn't start", ex);
            ToastHost.Show(ToastWindow.Message("Captions couldn't start", "Windows didn't let me listen to the speakers. Is a sound device plugged in?"));
        }
    }

    void StopCaptions()
    {
        if (_captions is { } captions) _ = captions.DisposeAsync().AsTask();
        _captions = null;
        _captionsWindow?.Close();
        _captionsWindow = null;
    }

    // ---- Suggestions and quips ----

    /// <summary>A new download that's probably wanted another way: one tap to do it.</summary>
    void SuggestFor(string file)
    {
        if (!_settings.SuggestDownloads || _buddy?.FullScreenNow == true || Suggestions.ForDownload(file) is not { } suggestion) return;
        var (action, why) = suggestion;
        ToastWindow? toast = null;
        var label = action.Label.TrimEnd('…');
        toast = ToastWindow.Message($"New download: {Path.GetFileName(file)}", why, null,
            (char.ToUpperInvariant(label[0]) + label[1..], () => { toast!.CloseAnimated(); JobRunner.Start(action, [file]); }),
            ("Not now", () => toast!.CloseAnimated()));
        ToastHost.Show(toast);
        AiLog.Write("suggested", $"{action.Id} for {file}");
    }

    DateTime _lastQuip = DateTime.MinValue;
    DateTime _lateNightQuip = DateTime.MinValue;
    QuipBubble? _quip;
    readonly Random _quipRandom = new();

    /// <summary>A short line in a bubble above the cat; rare (one every 20 minutes at most).</summary>
    void Quip(string text)
    {
        if (!_settings.Quips || _buddy is not { IsVisible: true } cat || cat.FullScreenNow) return;
        if (DateTime.Now - _lastQuip < TimeSpan.FromMinutes(20)) return;
        _lastQuip = DateTime.Now;
        _quip?.Close();
        _quip = new QuipBubble(text, cat.BodyDip(), cat.Away);
        _quip.Show();
        AiLog.Write("quip", text);
    }

    static readonly string[] Welcomes =
    [
        "Welcome back. I kept your files warm.",
        "Oh, you're back. I was definitely not asleep.",
        "Back! Nothing got lost while you were away.",
        "Hi again. Need anything? Just say my name.",
    ];

    /// <summary>Coming back to the PC: tasks for today, a word at night, or (now and then) a hello.</summary>
    void ComeBackQuip()
    {
        var now = DateTime.Now;
        var today = Todos.All.Where(t => !t.Done && t.Due is { } d && d.Date <= now.Date).ToList();
        if (now.Hour < 5 && _lateNightQuip.Date != now.Date)
        {
            _lateNightQuip = now;
            Quip($"It's {now:h:mm} in the morning. I'll keep watch; get some sleep soon.");
        }
        else if (today.Count > 0)
            Quip(today.Count == 1 ? $"Welcome back. One thing for today: {today[0].Text}." : $"Welcome back. {today.Count} things for today, first: {today[0].Text}.");
        else if (now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 16)
            Quip("Friday afternoon. Nearly there.");
        else if (_quipRandom.Next(3) == 0)
            Quip(Welcomes[_quipRandom.Next(Welcomes.Length)]);
    }

    /// <summary>A job that made things much smaller: say how much it saved.</summary>
    void SavedQuip(Job job)
    {
        try
        {
            long before = job.Inputs.Where(File.Exists).Sum(f => new FileInfo(f).Length);
            long after = job.Outputs.Where(File.Exists).Sum(f => new FileInfo(f).Length);
            if (job.Outputs.Count > 0 && before - after > 5L * 1024 * 1024)
                Quip($"That saved {FileChores.Size(before - after)}. Light as a feather.");
        }
        catch (IOException)
        {
        }
    }

    // ---- Habits ----

    readonly HashSet<string> _habitsOffered = [];

    /// <summary>After a job: if the same conversion keeps being done to downloads, offer to do it by itself.</summary>
    void OfferHabit()
    {
        if (Habits.Spot() is not { } spot) return;
        var (ext, action, times) = spot;
        // Once per run of the app; a "not now" (closing the card) asks again another day.
        if (!_habitsOffered.Add($"{ext}|{action.Id}")) return;
        ToastWindow? toast = null;
        toast = ToastWindow.Message($"Turn every new {ext} download {action.Label.TrimEnd('…')}?",
            $"You've done it {times} times lately. {CatName} can do it by itself as each one lands in Downloads (the original stays). Stop it any time in Settings.", null,
            ("Yes, always", () => { Habits.Accept(ext, action); toast!.CloseAnimated(); }),
            ("No thanks", () => { Habits.Decline(ext, action); toast!.CloseAnimated(); }));
        ToastHost.Show(toast);
    }

    /// <summary>A new download a rule covers: convert it by itself (results never set a rule off again: other kind).</summary>
    void AutoConvert(string file)
    {
        if (Habits.For(file) is not { } action) return;
        // A result of ours that landed in Downloads isn't a download.
        if (History.Entries.Take(50).Any(e => e.Outputs.Contains(file, StringComparer.OrdinalIgnoreCase))) return;
        AiLog.Write("auto", $"{action.Id} on {file}");
        JobRunner.Start(action, [file]);
    }

    (string? Text, byte[]? Png) ReadClipboardForCat()
    {
        try
        {
            if (Clipboard.ContainsText()) return (Clipboard.GetText(), null);
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var memory = new MemoryStream();
                encoder.Save(memory);
                return (null, memory.ToArray());
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the clipboard", ex);
        }
        return (null, null);
    }

    /// <summary>
    /// "Try" on the skills page: the plain commands run straight away (as if said); anything else
    /// goes to the cat on the Ask page.
    /// </summary>
    void TrySaying(string words)
    {
        ShowDashboard(DashPage.Ask);
        if (VoiceCommand(words) is { } command)
        {
            Chat.Say(new YouSaid(words));
            command();
            return;
        }
        _ = Chat.Send(words);
    }

    /// <summary>
    /// Words Deepgram should expect from this person: their routine phrases, and the names of
    /// people they've emailed lately (so "partners at aurea gifts" comes out right).
    /// </summary>
    void RefreshKeyTerms()
    {
        VoiceClips.On = _settings.KeepVoiceClips;
        // The contacts' names, for the speech engine (once a day, in the background).
        _ = People.RefreshContacts();
        var terms = Routines.All.SelectMany(r => r.Phrases.Take(2)).ToList();
        try
        {
            // Senders and recipients the cat has seen in Gmail, from the log ("gmail search from:…").
            var log = File.Exists(AiLog.FilePath) ? File.ReadLines(AiLog.FilePath).TakeLast(2000) : [];
            foreach (var line in log)
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"(?:from|to):(\S+)");
                if (match.Success) terms.Add(match.Groups[1].Value.Split('@')[0].Replace('.', ' '));
            }
        }
        catch (IOException)
        {
        }
        Speech.Extra = terms.Where(t => t.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    /// <summary>Dev aid: how each engine writes down the person's own kept clips (see "compare-clips").</summary>
    async System.Threading.Tasks.Task CompareClips(int most)
    {
        var clips = VoiceClips.All().Take(most).ToList();
        var terms = Speech.KeyTerms(CatName).ToList();
        var table = new System.Text.StringBuilder("| Clip | Live (Deepgram, as heard) | Deepgram careful | ElevenLabs Scribe v2 | Whisper (this PC) |\n|---|---|---|---|---|\n");
        static string Cell(string t) => t.Replace("|", "/").Replace("\n", " ");
        foreach (var (wav, live) in clips)
        {
            var samples = VoiceClips.Read(wav);
            string deepgram, scribe, whisper;
            var watch = Stopwatch.StartNew();
            try { deepgram = await Deepgram.Transcribe(samples, terms, default) + $" ({watch.ElapsedMilliseconds} ms)"; } catch (Exception ex) { deepgram = "error: " + ex.Message; }
            watch.Restart();
            try { scribe = ElevenLabs.HasKey ? await ElevenLabs.Transcribe(File.ReadAllBytes(wav), terms, default) + $" ({watch.ElapsedMilliseconds} ms)" : "no key"; } catch (Exception ex) { scribe = "error: " + ex.Message; }
            watch.Restart();
            try { whisper = Speech.WhisperHindi.IsDownloaded ? await Speech.WhisperHindi.Transcribe(samples, $"{CatName}.", default, threads: Math.Clamp(Environment.ProcessorCount - 1, 2, 8)) + $" ({watch.ElapsedMilliseconds} ms)" : "not downloaded"; } catch (Exception ex) { whisper = "error: " + ex.Message; }
            Log.Info($"compare-clips {Path.GetFileNameWithoutExtension(wav)}: live \"{live}\" | deepgram \"{deepgram}\" | scribe \"{scribe}\" | whisper \"{whisper}\"");
            table.Append($"| {Path.GetFileNameWithoutExtension(wav)} | {Cell(live)} | {Cell(deepgram)} | {Cell(scribe)} | {Cell(whisper)} |\n");
        }
        File.WriteAllText(Path.Combine(VoiceClips.Folder, "compare.md"), table.ToString());
        Log.Info($"compare-clips: {clips.Count} clips, table in {Path.Combine(VoiceClips.Folder, "compare.md")}");
    }

    // ---- Instant answers ----

    static readonly Dictionary<string, string> Sites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://www.youtube.com", ["gmail"] = "https://mail.google.com", ["google"] = "https://www.google.com",
        ["instagram"] = "https://www.instagram.com", ["facebook"] = "https://www.facebook.com", ["twitter"] = "https://x.com", ["x"] = "https://x.com",
        ["linkedin"] = "https://www.linkedin.com", ["netflix"] = "https://www.netflix.com", ["amazon"] = "https://www.amazon.in", ["flipkart"] = "https://www.flipkart.com",
        ["chatgpt"] = "https://chatgpt.com", ["claude"] = "https://claude.ai", ["whatsapp web"] = "https://web.whatsapp.com", ["google drive"] = "https://drive.google.com",
        ["drive"] = "https://drive.google.com", ["google maps"] = "https://www.google.com/maps", ["maps"] = "https://www.google.com/maps", ["github"] = "https://github.com",
        ["reddit"] = "https://www.reddit.com", ["google calendar"] = "https://calendar.google.com", ["calendar"] = "https://calendar.google.com",
    };

    /// <summary>
    /// Plain questions and openings answered here in a fraction of a second: the time, the date,
    /// the battery, today's calendar, new email, a website, a YouTube or Google search.
    /// Anything else (or anything unclear) goes to Claude as usual.
    /// </summary>
    Action? Instant(string words)
    {
        static System.Text.RegularExpressions.Match rx(string input, string pattern) => System.Text.RegularExpressions.Regex.Match(input, pattern);
        words = System.Text.RegularExpressions.Regex.Replace(words, @"^(hey |ok |okay |so |please |can you |could you |tell me )+|( please| now| for me)+$", "");
        if (words is "what time is it" or "whats the time" or "what is the time" or "time" or "the time" or "kitne baje hain" or "kya time hua" or "time kya hua" or "kya time hai")
            return () => Tell("\uE823", $"It's {DateTime.Now:h:mm tt}.");
        if (words is "whats the date" or "what is the date" or "what date is it" or "todays date" or "the date" or "what day is it" or "whats the day today" or "aaj kya date hai" or "aaj kaun sa din hai" or "what is today")
            return () => Tell("\uE787", $"It's {DateTime.Now:dddd, d MMMM}.");
        // Connecting the phone works without Claude: the window that pairs it (and shows if it's connected).
        if (rx(words, @"^(connect|pair|link|set up|setup|add|reconnect|re connect)( up)?( my| the| a| an| new)? (phone|iphone|mobile|android)( again| to (this |the |my )?(pc|computer|laptop)| with (this |the |my )?(pc|computer|laptop))?$|^(phone|iphone|mobile) (connect|pairing|link)$|^(mera )?(phone|mobile) (connect|jodo|connect karo)$").Success)
            return () => { ShowPhoneConnect(); Tell("\uE8EA", "Here's Connect a phone. Scan the code with Shiki on your phone: once it's connected, it reconnects by itself."); };
        if (rx(words, @"^(battery|how much battery|whats my battery|what is my battery|my battery|battery level|how much charge|battery kitni hai|charge kitna hai)( is left| left| level)?$").Success)
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) return () => Tell("\uE83F", "This PC runs on mains power: no battery.");
            var charging = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            var left = !charging && power.BatteryLifeRemaining > 0 ? $", about {TimeSpan.FromSeconds(power.BatteryLifeRemaining):h\\:mm} left" : "";
            return () => Tell("\uE83F", $"Battery is at {power.BatteryLifePercent * 100:0}%{(charging ? " and charging" : left)}.");
        }
        if (Core.Mail.Gmail.HasCalendar && rx(words, @"^(whats on|what is on|whats my schedule|what is my schedule|my schedule|my calendar|whats on my calendar|what do i have)( for| on)? ?(today|tomorrow)?$|^(aaj|kal) (kya hai|ka schedule)$").Success)
        {
            bool tomorrow = words.Contains("tomorrow") || words.StartsWith("kal");
            return () => _ = TellCalendar(tomorrow);
        }
        if (Core.Mail.Gmail.Connected && rx(words, @"^(any new emails?|any new mail|do i have (any )?new (emails?|mail)|new emails?|check my (email|mail|inbox)|any emails today|koi naya (email|mail))$").Success)
            return () => _ = TellNewMail();
        // "Open YouTube": a website, when it isn't an app on this PC.
        var open = rx(words, @"^(open|launch|go to) (?<site>.+?)$");
        if (open.Success && Sites.TryGetValue(open.Groups["site"].Value, out var url) && Apps.Find(open.Groups["site"].Value, strict: true) == null)
            return () => { OpenWebsite(new Uri(url)); Tell("\uE774", $"Opening {open.Groups["site"].Value}."); };
        var youtube = rx(words, @"^(search youtube for|youtube search|search on youtube for|search on youtube|play on youtube) (?<q>.+)$|^(?<q>.+?) on youtube$");
        if (youtube.Success && !youtube.Groups["q"].Value.StartsWith("open"))
        {
            var query = youtube.Groups["q"].Value.Replace("play ", "");
            return () => { OpenWebsite(new Uri($"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}")); Tell("\uE774", $"Searching YouTube for {query}."); };
        }
        // "Play AC/DC" again: the song found last time, at once.
        var music = rx(words, @"^(open spotify and |on spotify |spotify pe |spotify par )?(play|bajao|chalao|laga do|lagao|put on) (?<q>.+?)( on spotify| spotify pe| spotify par)?$|^(?<q>.+?) (bajao|chalao|laga do|lagao)$");
        if (music.Success && Spotify.Find(music.Groups["q"].Value) is { } pick)
            return () => _ = PlayRemembered(pick);
        // The first time: found on this PC's side in a couple of seconds, without the whole chat.
        if (music.Success && ClaudeKey.Exists)
        {
            var asked = music.Groups["q"].Value;
            var said = words;
            return () => _ = FindAndPlay(asked, said);
        }
        var google = rx(words, @"^(google|search google for|google search) (?<q>.+)$");
        if (google.Success)
        {
            var query = google.Groups["q"].Value;
            return () => { OpenWebsite(new Uri($"https://www.google.com/search?q={Uri.EscapeDataString(query)}")); Tell("\uE774", $"Searching Google for {query}."); };
        }
        return null;
    }

    async System.Threading.Tasks.Task FindAndPlay(string asked, string said)
    {
        Chat.Say(new CatDid("\uE768", $"Finding {asked} on Spotify…"));
        var pick = await Spotify.Lookup(asked, default);
        if (pick == null)
        {
            // Not clearly a song or artist (a mood, a playlist): the chat works it out.
            _ = Chat.Send(said, spoken: _voice is { } v && v.Active, shown: false);
            return;
        }
        var playing = await Spotify.Play(pick.Uri, pick.Label);
        if (playing.StartsWith("Playing")) Spotify.Remember(asked, pick.Uri, pick.Label);
        Tell("\uE768", playing);
    }

    async System.Threading.Tasks.Task PlayRemembered(Spotify.Pick pick)
    {
        var playing = await Spotify.Play(pick.Uri, pick.Label);
        Tell("\uE768", playing);
    }

    // ---- The quick path ----

    /// <summary>
    /// Sends and reminders worked out on this PC ("WhatsApp Mummy that\u2026", "email this to me",
    /// "remind me at 5 to\u2026"): who's who from what the cat remembers, then Google contacts. Sends
    /// still go on a card (or a countdown for people already written to); reminders are set at once.
    /// Anything unclear (two contacts with that name, nothing selected) goes to Claude after all.
    /// </summary>
    async System.Threading.Tasks.Task RunQuick(List<QuickPath.Item> items, string said)
    {
        bool spoken = _voice is { } v && v.Active;
        // What was selected when the bubble was opened, if it was (else what's in front now).
        var screen = Chat.NextScreen;
        Chat.NextScreen = null;
        async System.Threading.Tasks.Task ToClaude(string why)
        {
            AiLog.Write("quick", $"left to Claude: {why}");
            Chat.NextScreen = screen;
            await Chat.Send(said, spoken, shown: false);
        }
        var cards = new List<PlanItem>();
        try
        {
            foreach (var item in items)
            {
                PlanItem? card = null;
                switch (item)
                {
                    case QuickPath.Message message:
                    {
                        var (name, phone, unsure) = await FindPhone(message.Name);
                        if (unsure) { await ToClaude($"more than one number for {message.Name}"); return; }
                        var input = new Dictionary<string, object> { ["name"] = name, ["text"] = message.Text };
                        if (phone != null) input["phone"] = phone;
                        if (message.Web) input["web"] = true;
                        card = await Chat.Card("whatsapp_send", input);
                        break;
                    }
                    case QuickPath.Files send:
                    {
                        var files = (QuickPath.IsSelection(send.What)
                            ? screen?.Selected ?? ExplorerSelection.Frontmost()?.Selected ?? []
                            : Learned.Means(send.What) ?? []).ToList();
                        if (files.Count == 0 || !files.All(File.Exists)) { await ToClaude(files.Count == 0 ? "nothing selected" : "a folder is selected"); return; }
                        if (send.Email)
                        {
                            var (name, to, unsure) = send.Who == "me"
                                ? (QuickPath.RememberedEmail("me") is { } own ? ("me", own.Email, false) : ("me", null, true))
                                : await FindEmail(send.Who);
                            if (unsure) { await ToClaude($"which email for {send.Who}"); return; }
                            var input = new Dictionary<string, object>
                            {
                                ["to"] = to ?? "",
                                ["subject"] = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0]) : $"{files.Count} files",
                                // No words unless they've asked for a line with files.
                                ["body"] = Learned.TextWithFiles ? (files.Count == 1 ? "Here's the file." : "Here are the files.") : "",
                                ["files"] = files,
                            };
                            if (name != "me") input["name"] = name;
                            card = await Chat.Card("gmail_send", input);
                        }
                        else
                        {
                            // To yourself: the number you gave once ("my own WhatsApp"), else Claude asks.
                            var (name, phone, unsure) = send.Who == "me"
                                ? (QuickPath.RememberedPhone("me") is { } own ? ("me", own.Phone, false) : ("me", null, true))
                                : await FindPhone(send.Who);
                            if (unsure) { await ToClaude($"which number for {send.Who}"); return; }
                            var input = new Dictionary<string, object> { ["name"] = name, ["text"] = "", ["files"] = files };
                            if (phone != null) input["phone"] = phone;
                            card = await Chat.Card("whatsapp_send", input);
                        }
                        break;
                    }
                }
                if (item is QuickPath.Reminder) continue;
                if (card == null) { await ToClaude("no card"); return; }
                cards.Add(card);
            }
            // A new person's address goes on a card of its own: with other things too, Claude sorts out the order.
            if (cards.Count > 1 && cards.Any(c => c.Ask != null)) { await ToClaude("a new person among several sends"); return; }
        }
        catch (Exception ex)
        {
            Log.Error("Quick path failed", ex);
            await ToClaude(ex.Message);
            return;
        }

        // Worked out: reminders are set now (nothing to confirm), sends go on one card.
        var reminded = new List<string>();
        foreach (var reminder in items.OfType<QuickPath.Reminder>())
        {
            var todo = Todos.Add(reminder.Text, reminder.Due, hasTime: true);
            AiLog.Write("task", $"{reminder.Text} {reminder.Due:yyyy-MM-dd HH:mm} (quick)");
            Chat.Note($"the person said \"{said}\", so it set a reminder \"{reminder.Text}\" for {reminder.Due:dddd d MMM, HH:mm}");
            reminded.Add($"{char.ToLowerInvariant(reminder.Text[0])}{reminder.Text[1..]} {todo.DueText().Replace(", ", " at ")}");
        }
        string? remind = reminded.Count == 0 ? null : $"I'll remind you to {string.Join(", and to ", reminded)}.";
        if (cards.Count == 0)
        {
            Tell("\uE762", remind!);
            return;
        }
        var offer = cards.Count == 1 ? cards[0] : Chat.Bundle(cards);
        var ask = offer.Ask is { } a
            ? $"I don't have {a.Name}'s {(a.Kind == AddressKind.Phone ? "number" : "email")} yet. Type it in, or say it."
            : offer.Countdown ? "Sending in 5 seconds. Say stop to cancel." : "Okay to send?";
        Tell(offer.Ask != null ? "\uE77B" : "\uE724", remind == null ? ask : $"{remind} {ask}");
        Chat.Offer(offer, said);
    }

    /// <summary>
    /// Whether a contact is the one named: the whole name, the first name, or a first name that
    /// sounds the same ("Raj" isn't "Rajesh").
    /// </summary>
    static bool Named(Core.Mail.Contact contact, string name) =>
        contact.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        || contact.Name.Split(' ')[0].Equals(name, StringComparison.OrdinalIgnoreCase)
        || People.Sound(contact.Name.Split(' ')[0]) == People.Sound(name);

    /// <summary>A WhatsApp number for a name: remembered, else the one in Google contacts; Unsure when there's a choice to make.</summary>
    static async System.Threading.Tasks.Task<(string Name, string? Phone, bool Unsure)> FindPhone(string name)
    {
        if (QuickPath.RememberedPhone(name) is { } known) return (known.Name, known.Phone, false);
        if (!Core.Mail.Gmail.HasContacts) return (name, null, false);
        var phones = (await Core.Mail.Contacts.Find(name, default))
            .Where(c => Named(c, name))
            .SelectMany(c => c.Phones.Select(p => (c.Name, Phone: Addresses.Phone(p))))
            .Where(x => x.Phone != null).DistinctBy(x => x.Phone).ToList();
        return phones.Count switch
        {
            1 => (phones[0].Name, phones[0].Phone, false),
            0 => (name, null, false),
            _ => (name, null, true),
        };
    }

    /// <summary>An email address for a name: remembered, else the one in Google contacts; Unsure when there's a choice to make.</summary>
    static async System.Threading.Tasks.Task<(string Name, string? Email, bool Unsure)> FindEmail(string name)
    {
        if (QuickPath.RememberedEmail(name) is { } known) return (known.Name, known.Email, false);
        if (!Core.Mail.Gmail.HasContacts) return (name, null, false);
        var emails = (await Core.Mail.Contacts.Find(name, default))
            .Where(c => Named(c, name))
            .SelectMany(c => c.Emails.Select(e => (c.Name, Email: e.ToLowerInvariant())))
            .DistinctBy(x => x.Email).ToList();
        return emails.Count switch
        {
            1 => (emails[0].Name, emails[0].Email, false),
            0 => (name, null, false),
            _ => (name, null, true),
        };
    }

    /// <summary>An instant answer: shown in the chat and, when the cat talks back, said aloud.</summary>
    void Tell(string glyph, string text)
    {
        Chat.Say(new CatSaid(text));
        AiLog.Write("instant", text);
        if (_settings.TalkBack != "off" && _mouth?.Reading != true) _ = Mouth.Say(text, _settings.TalkVoice);
    }

    async System.Threading.Tasks.Task TellCalendar(bool tomorrow)
    {
        try
        {
            var day = tomorrow ? DateTime.Today.AddDays(1) : DateTime.Today;
            var events = await Core.Mail.Calendar.Between(tomorrow ? day : DateTime.Now.AddMinutes(-30), day.AddDays(1), default);
            var when = tomorrow ? "tomorrow" : "today";
            Tell("\uE787", events.Count switch
            {
                0 => $"Nothing on your calendar {(tomorrow ? "tomorrow" : "for the rest of today")}.",
                1 => $"One thing {when}: {Short(events[0])}.",
                _ => $"{events.Count} things {when}: " + string.Join("; ", events.Take(4).Select(Short)) + (events.Count > 4 ? ", and more." : "."),
            });
        }
        catch (Exception ex)
        {
            Tell("\uE787", ex.Message);
        }
        static string Short(Core.Mail.CalendarEvent e) => e.AllDay ? e.Title : $"{e.Title} at {e.Start:h:mm tt}";
    }

    async System.Threading.Tasks.Task TellNewMail()
    {
        try
        {
            var unread = await Core.Mail.Gmail.Search("in:inbox is:unread newer_than:1d", 10, default);
            if (unread.Count == 0)
            {
                Tell("\uE715", "No new emails today.");
                return;
            }
            string Who(Core.Mail.MailSummary m) => m.From.Split('<')[0].Trim().Trim('"') is { Length: > 0 } name ? name : m.From;
            Tell("\uE715", unread.Count == 1 ? $"One new email, from {Who(unread[0])}: {unread[0].Subject}."
                : $"{(unread.Count == 10 ? "10 or more" : unread.Count.ToString())} new emails today. The latest is from {Who(unread[0])}: {unread[0].Subject}.");
        }
        catch (Exception ex)
        {
            Tell("\uE715", ex.Message);
        }
    }

    // ---- Routines ----

    /// <summary>Runs each step of a routine, showing what happened in the chat; returns a summary for Claude.</summary>
    string RunRoutine(Routine routine, bool spoken)
    {
        AiLog.Write("routine", routine.Name);
        var done = new List<string>();
        string? say = null;
        foreach (var step in routine.Steps)
        {
            string result;
            try
            {
                result = step.Do switch
                {
                    "do_not_disturb_on" => PcControl.SetDoNotDisturb(true),
                    "do_not_disturb_off" => PcControl.SetDoNotDisturb(false),
                    "set_volume" => PcControl.SetVolume(int.TryParse(step.Value, out var v) ? v : 50),
                    "mute" => PcControl.Mute(true),
                    "unmute" => PcControl.Mute(false),
                    "set_brightness" => PcControl.SetBrightness(int.TryParse(step.Value, out var b) ? b : 70),
                    "dark_mode_on" => PcControl.SetDarkMode(true),
                    "dark_mode_off" => PcControl.SetDarkMode(false),
                    "switch_sound_output" => PcControl.SetOutput(step.Value ?? ""),
                    "open_app" => Apps.Open(step.Value ?? ""),
                    "close_app" => Apps.Close(step.Value ?? ""),
                    "open_website" when Uri.TryCreate(step.Value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" => OpenWebsite(uri),
                    "app_tool" => UseToolForCat(step.Value ?? "", null) ? Routines.Describe(step) : $"No tool called {step.Value}",
                    "say" => "",
                    _ => $"Skipped {step.Do}",
                };
            }
            catch (Exception ex)
            {
                Log.Error($"Routine step {step.Do} failed", ex);
                result = $"{Routines.Describe(step)} didn't work";
            }
            if (step.Do == "say") say = step.Value;
            else done.Add(result.TrimEnd('.'));
        }
        Chat.Say(new CatDid("\uE8F1", $"{routine.Name}: {string.Join("; ", done)}"));
        if (say != null)
        {
            Chat.Say(new CatSaid(say));
            if (_settings.TalkBack == "always" || (_settings.TalkBack == "spoken" && spoken)) _ = Mouth.Say(say, _settings.TalkVoice);
        }
        return $"Ran \"{routine.Name}\": {string.Join("; ", done)}.{(say != null ? $" It also said: {say}" : "")}";
    }

    static string OpenWebsite(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        return $"Opened {uri.Host.Replace("www.", "")}";
    }

    // ---- The cat's voice ----

    Speaker? _mouth;
    int _spokenChars;

    /// <summary>Reads replies aloud; the cat's mouth moves while it does, and the wake word waits.</summary>
    Speaker Mouth
    {
        get
        {
            if (_mouth != null) return _mouth;
            _mouth = new Speaker();
            _mouth.Changed += () => _buddy?.Talking(_mouth.Speaking);
            return _mouth;
        }
    }

    // ---- Dictation ----

    Dictation? _dictation;

    /// <summary>
    /// Hold the shortcut and talk: when it's let go, the words are typed where the cursor is.
    /// A quick tap listens until a pause instead (or until it's pressed again).
    /// </summary>
    async void Dictate()
    {
        if (_dictation != null)
        {
            _dictation.Finish();
            return;
        }
        if (!Microphone.Present())
        {
            ToastHost.Show(ToastWindow.Message("No microphone", "Plug one in, or check Windows' sound settings, then try again."));
            return;
        }
        var target = NativeMethods.GetForegroundWindow();
        var dictation = _dictation = new Dictation(_settings, () => CatName);
        var pill = new DictationPill(dictation);
        pill.Show();

        // Held or tapped: watch the shortcut's key; let go after a moment means "type it now".
        var key = Hotkey.Parse(_settings.DictateHotkey) is { } hotkey ? System.Windows.Input.KeyInterop.VirtualKeyFromKey(hotkey.Key) : 0;
        var pressed = Stopwatch.StartNew();
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        watch.Tick += (_, _) =>
        {
            if (key != 0 && TypeText.Down(key)) return;
            watch.Stop();
            if (pressed.ElapsedMilliseconds < 450) dictation.Held = false;
            else dictation.Finish();
        };
        watch.Start();

        string? text;
        try
        {
            text = await dictation.Run();
        }
        catch (Exception ex)
        {
            Log.Error("Dictation failed", ex);
            text = null;
        }
        watch.Stop();
        _dictation = null;
        if (text == null)
        {
            pill.Done("Didn't catch that");
            return;
        }
        await TypeText.WaitForModifiersUp();
        // The pill never takes the keyboard; if something else did meanwhile, go back to where you were.
        if (NativeMethods.GetForegroundWindow() != target && target != IntPtr.Zero) NativeMethods.SetForegroundWindow(target);
        int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (TypeText.Type(text + " "))
        {
            pill.Done(words == 1 ? "Typed 1 word" : $"Typed {words} words");
            AiLog.Write("dictated", $"{words} words");
        }
        else
        {
            // An app running as administrator won't take typing from a normal app: the clipboard will do.
            TrySetClipboard(text);
            pill.Done("Copied: paste it with Ctrl+V");
        }
    }

    static async System.Threading.Tasks.Task SpeakFile(string path, string voice, string text)
    {
        try
        {
            var wav = await Speaker.Render(text, voice == "-" ? "" : voice);
            File.WriteAllBytes(path, wav);
            Log.Info($"speak-file: {Speaker.Pick(voice == "-" ? "" : voice)}, {wav.Length / 1024} KB, read as \"{Speaker.Readable(text)}\"");
            foreach (var v in Speaker.Voices()) Log.Info($"speak-file voice: {v.Id} = {v.Label}");
        }
        catch (Exception ex)
        {
            Log.Error("speak-file failed", ex);
        }
    }

    async System.Threading.Tasks.Task CaptionsTest(string wav, string? language)
    {
        StartCaptions(language);
        if (_captions is not { } captions) return;
        captions.Line += (text, translated) => Log.Info($"captions-test line: {text}{(translated != null ? $" => {translated}" : "")}");
        await System.Threading.Tasks.Task.Delay(1500);
        using (var reader = new NAudio.Wave.WaveFileReader(wav))
        using (var output = new NAudio.Wave.WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100))
        {
            output.Init(reader);
            output.Play();
            while (output.PlaybackState == NAudio.Wave.PlaybackState.Playing) await System.Threading.Tasks.Task.Delay(200);
        }
        await System.Threading.Tasks.Task.Delay(language != null ? 8000 : 4000);
        _captionsWindow?.Dispatcher.Invoke(() => { });
        if (_captionsWindow != null) DashboardWindow.SaveElement((FrameworkElement)_captionsWindow.Content, _captionsWindow.ActualWidth, Path.ChangeExtension(wav, ".png"));
        StopCaptions();
        Log.Info("captions-test done");
    }

    void AgentSelfTest()
    {
        var real = new RealScreen();
        var png = real.Screenshot();
        using (var image = System.Drawing.Image.FromStream(new MemoryStream(png)))
            Log.Info($"agent-selftest: screen {System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Size} -> shots {real.Size}, png {image.Width}x{image.Height}, {png.Length / 1024} KB; cursor {real.Cursor()}");
        var zoom = real.Zoom(0, 0, 300, 200);
        using (var image = System.Drawing.Image.FromStream(new MemoryStream(zoom))) Log.Info($"agent-selftest: zoom {image.Width}x{image.Height}");
        foreach (var combo in new[] { "Return", "ctrl+s", "alt+Tab", "ctrl+shift+t", "Page_Down", "F5", "super", "a", "A", "@", "ctrl+a", "BackSpace" })
            Log.Info($"agent-selftest: key {combo} = {string.Join(",", RealScreen.Keys(combo).Select(k => $"0x{k:X2}"))}");
        var sandbox = new AgentSandbox();
        sandbox.Click("left", 1, sandbox.Where("Reply"), null);
        sandbox.Key("ctrl+a", 1);
        sandbox.Type("Yes, Friday works.");
        sandbox.Click("left", 1, sandbox.Where("Send"), null);
        Log.Info($"agent-selftest: sandbox sent={sandbox.Sent} to={sandbox.To} body={sandbox.Body}");
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "agent-sandbox.png"), sandbox.Screenshot());
        var agent = new ComputerAgent(sandbox, _ => System.Threading.Tasks.Task.FromResult(false), u => u, a => a);
        var pill = new AgentPill(agent, "Reply to Rahul's email about lunch saying: Yes, Friday at 1 works for me.");
        _ = pill.Ask(new AgentQuestion("Send this reply to Rahul: \"Yes, Friday at 1 works for me. See you there!\"?", true));
        DashboardWindow.SaveElement((FrameworkElement)pill.Content, 380, Path.Combine(Path.GetTempPath(), "agent-pill.png"));
        pill.Close();
    }

    async System.Threading.Tasks.Task SayTiming()
    {
        var watch = Stopwatch.StartNew();
        var wav = await Speaker.Render("Cats can't taste sweetness.", _settings.TalkVoice);
        var rendered = watch.ElapsedMilliseconds;
        double seconds;
        using (var reader = new NAudio.Wave.WaveFileReader(new MemoryStream(wav))) seconds = reader.TotalTime.TotalSeconds;
        watch.Restart();
        using (var reader = new NAudio.Wave.WaveFileReader(new MemoryStream(wav)))
        using (var output = new NAudio.Wave.WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 120))
        {
            output.Init(reader);
            var init = watch.ElapsedMilliseconds;
            output.Play();
            while (output.PlaybackState == NAudio.Wave.PlaybackState.Playing) await System.Threading.Tasks.Task.Delay(20);
            Log.Info($"say-timing: render {rendered} ms, init {init} ms, played {watch.ElapsedMilliseconds} ms for {seconds:0.00} s of sound, {wav.Length / 1024} KB");
        }
    }

    static async System.Threading.Tasks.Task GoogleTest(string name)
    {
        Log.Info($"google-test: calendar allowed {Core.Mail.Gmail.HasCalendar}, contacts allowed {Core.Mail.Gmail.HasContacts}");
        try
        {
            var events = await Core.Mail.Calendar.Between(DateTime.Today, DateTime.Today.AddDays(2), default);
            Log.Info($"google-test: {events.Count} events today and tomorrow" + string.Concat(events.Take(5).Select(e => " | " + Core.Mail.Calendar.Describe(e))));
        }
        catch (Exception ex) { Log.Info($"google-test calendar: {ex.Message}"); }
        try
        {
            var people = await Core.Mail.Contacts.Find(name, default);
            Log.Info($"google-test: {people.Count} contacts for \"{name}\"" + string.Concat(people.Take(3).Select(p => $" | {p.Name} ({p.Emails.Count} emails, {p.Phones.Count} phones)")));
        }
        catch (Exception ex) { Log.Info($"google-test contacts: {ex.Message}"); }
    }

    static async System.Threading.Tasks.Task GmailTest()
    {
        try
        {
            var watch = Stopwatch.StartNew();
            var newest = await Core.Mail.Gmail.Search("in:inbox", 1, default);
            Log.Info(newest.Count == 0 ? "gmail-test: inbox is empty" : $"gmail-test: {newest[0].Date:ddd d MMM HH:mm} | {newest[0].From} | \"{newest[0].Subject}\" ({watch.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {
            Log.Error("gmail-test failed", ex);
        }
    }

    async System.Threading.Tasks.Task AgentSandboxTest(string task)
    {
        if (ClaudeKey.Read() is not { } key) { Log.Info("agent-sandbox: no key"); return; }
        var sandbox = new AgentSandbox();
        var agent = new ComputerAgent(sandbox,
            question => { Log.Info($"agent-sandbox: ASKED \"{question.Question}\" -> yes"); return System.Threading.Tasks.Task.FromResult(true); },
            url => { Log.Info($"agent-sandbox: open website {url}"); return "Opened."; },
            app => { Log.Info($"agent-sandbox: open app {app}"); return "Opened."; });
        string summary;
        try
        {
            summary = await agent.Run(task, key);
        }
        catch (Exception ex)
        {
            Log.Error("agent-sandbox failed", ex);
            return;
        }
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "agent-sandbox.png"), sandbox.Screenshot());
        Log.Info($"agent-sandbox: done in {agent.Steps} steps, ${agent.Cost:0.000}; sent={sandbox.Sent}; to={sandbox.To}; body={sandbox.Body.Replace("\n", " / ")}; summary: {summary}");
    }

    static void PcTest()
    {
        Log.Info($"pc-test status: {PcControl.Status()}");
        Log.Info("pc-test outputs: " + string.Join(" | ", PcControl.Outputs().Select(o => o.Name + (o.Current ? " *" : ""))));
        var (percent, muted, _) = PcControl.Volume();
        Log.Info($"pc-test set volume same: {PcControl.SetVolume(percent)} -> {PcControl.Volume().Percent}");
        PcControl.Mute(muted);
        if (PcControl.Brightness() is { } bright) Log.Info($"pc-test set brightness same: {PcControl.SetBrightness(bright)} -> {PcControl.Brightness()}");
        var dark = PcControl.DarkMode();
        Log.Info($"pc-test dark mode same: {PcControl.SetDarkMode(dark)}");
        var quiet = PcControl.DoNotDisturb();
        Log.Info($"pc-test dnd now: {quiet}");
        if (quiet == false)
        {
            Log.Info($"pc-test dnd on: {PcControl.SetDoNotDisturb(true)} -> {PcControl.DoNotDisturb()}");
            Log.Info($"pc-test dnd off: {PcControl.SetDoNotDisturb(false)} -> {PcControl.DoNotDisturb()}");
        }
        var apps = Apps.All();
        Log.Info($"pc-test apps: {apps.Count}");
        foreach (var name in new[] { "chrome", "word", "excel", "notepad", "calculator", "spotify", "whatsapp", "vs code", "settings", "paint", "camera", "teams", "edge", "the invoice", "work mode", "recording" })
            Log.Info($"pc-test find \"{name}\": {Apps.Find(name)?.Name ?? "-"} / strict {Apps.Find(name, strict: true)?.Name ?? "-"}");
        foreach (var said in new[] { "mute", "unmute the sound", "volume up", "turn it down", "set the volume to 40", "volume fifty percent", "brightness 70", "make it brighter",
                     "dark mode", "turn off dark mode", "do not disturb", "turn off do not disturb", "please mute the computer", "open chrome", "merge my pdfs", "volume 400" })
            Log.Info($"pc-test phrase \"{said}\": {(PcCommands.Match(WakeMatcher.Normalize(said)) != null ? "PC command" : "-")}");
        var running = Apps.Windows();
        Log.Info("pc-test windows: " + string.Join(" | ", running.Select(r => $"{r.Name} [{r.Process.ProcessName}]: {r.Title}")));
        foreach (var r in running) r.Process.Dispose();
    }

    async System.Threading.Tasks.Task TypeTest()
    {
        var box = new System.Windows.Controls.TextBox { FontSize = 16, AcceptsReturn = true };
        var window = new Window { Title = "Shiki typing test", Width = 520, Height = 160, Content = box, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Show();
        NativeMethods.ForceForeground(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        window.Activate();
        box.Focus();
        await System.Threading.Tasks.Task.Delay(400);
        var sent = "Hello from Shiki: ünïcødé, ₹500, 日本語, done.";
        await TypeText.WaitForModifiersUp();
        bool ok = TypeText.Type(sent);
        await System.Threading.Tasks.Task.Delay(500);
        Log.Info($"type-test: sent ok={ok}, arrived \"{box.Text}\", same={box.Text == sent}");
        window.Close();
    }

    // ---- Meeting notes ----

    MeetingSession? _meeting;
    NotesPill? _pill;
    CallWatcher? _calls;
    readonly DispatcherTimer _reminders = new() { Interval = TimeSpan.FromSeconds(30) };

    void ToggleNotes()
    {
        if (_meeting == null) StartNotes();
        else StopNotes();
    }

    /// <summary>Starts taking notes: the pill appears at the top, and the call is written down as it goes.</summary>
    void StartNotes(string? app = null)
    {
        if (_meeting != null) return;
        _menu?.CloseAnimated();
        if (!Microphone.Present())
        {
            ToastHost.Show(ToastWindow.Message("No microphone", "Plug one in, or check Windows' sound settings, then try again."));
            return;
        }
        try
        {
            _meeting = MeetingSession.Start(_settings, CatName, app);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start meeting notes", ex);
            ToastHost.Show(ToastWindow.Message("Couldn't start notes", ex.Message));
            return;
        }
        _pill = new NotesPill(_meeting);
        DressCat();
        _pill.MarkRequested += () => _meeting?.Mark();
        _pill.StopRequested += StopNotes;
        _pill.Show();
        _buddy?.Act("purr");
    }

    /// <summary>Stops, writes the notes, adds your tasks, and says where to find them.</summary>
    async void StopNotes()
    {
        if (_meeting is not { Stopping: false } session) return;
        try
        {
            var meeting = await session.StopAsync(Names.First ?? "", cost => { _settings.AiSpentUsd += cost; _settings.AiRequests++; _settings.Save(); });
            int mine = meeting.Tasks.Count(t => t.Owner.Equals("me", StringComparison.OrdinalIgnoreCase));
            var message = meeting.NotesProblem ?? (meeting.Summary is { Length: > 0 }
                ? $"{meeting.Length}. {(mine == 0 ? "No tasks for you." : mine == 1 ? "1 task added to your list." : $"{mine} tasks added to your list.")}"
                : "The transcript is saved.");
            ToastWindow? toast = null;
            toast = ToastWindow.Message(meeting.NotesProblem == null ? $"Notes ready: {meeting.Title}" : "Meeting saved", message, null,
                ("Open", () => { toast!.CloseAnimated(); ShowDashboard(); _dashboard!.OpenMeetingPage(meeting.Id); }));
            ToastHost.Show(toast);
        }
        catch (Exception ex)
        {
            Log.Error("Stopping meeting notes failed", ex);
            ToastHost.Show(ToastWindow.Message("Notes stopped", "Something went wrong while saving. The log has the details."));
        }
        finally
        {
            _pill?.Close();
            _pill = null;
            _meeting = null;
            DressCat();
            CheckLevel();
        }
    }

    async System.Threading.Tasks.Task RewriteNotes(Meeting meeting)
    {
        try
        {
            using var cancel = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
            await MeetingNotes.Write(meeting, Names.First ?? "", cost => { _settings.AiSpentUsd += cost; _settings.AiRequests++; _settings.Save(); }, cancel.Token);
            foreach (var task in meeting.Tasks.Where(t => t.Owner.Equals("me", StringComparison.OrdinalIgnoreCase)))
                if (!Todos.All.Any(t => t.MeetingId == meeting.Id && t.Text == task.Text))
                    Todos.Add(task.Text, task.Due, task.Due is { } due && due.TimeOfDay != TimeSpan.Zero, meeting.Title, meeting.Id);
        }
        catch (Exception ex)
        {
            meeting.NotesProblem = ex is InvalidOperationException ? ex.Message : $"The notes couldn't be written ({ex.Message}).";
        }
        MeetingStore.Save(meeting);
    }

    /// <summary>Opens the Ask page with the meeting's notes and transcript attached.</summary>
    void AskAboutMeeting(Meeting meeting)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Meeting: {meeting.Title}, {meeting.Started:dddd d MMMM yyyy, HH:mm}, {meeting.Length}.");
        if (meeting.Summary is { Length: > 0 } summary) text.AppendLine($"Summary: {summary}");
        if (meeting.Decisions.Count > 0) text.AppendLine("Decisions: " + string.Join("; ", meeting.Decisions));
        if (meeting.Tasks.Count > 0) text.AppendLine("Tasks: " + string.Join("; ", meeting.Tasks.Select(t => $"{t.Text} ({t.Owner}{(t.Due is { } d ? $", by {d:yyyy-MM-dd}" : "")})")));
        text.AppendLine().AppendLine("Transcript:").Append(meeting.Transcript());
        Chat.Attach($"the meeting \"{meeting.Title}\" ({meeting.Started:d MMM})", text.ToString());
        ShowDashboard(DashPage.Ask);
    }

    /// <summary>A calling app started using the microphone: offer once to take notes.</summary>
    void OfferNotes(string app)
    {
        if (!_settings.OfferNotes || _meeting != null) return;
        ToastWindow? toast = null;
        toast = ToastWindow.Message($"A call started in {app}", $"Want {CatName} to take notes? You'll get a summary and your tasks at the end.", null,
            ("Take notes", () => { toast!.CloseAnimated(); StartNotes(app); }),
            ("Not this time", () => toast!.CloseAnimated()));
        ToastHost.Show(toast);
    }

    /// <summary>
    /// "Take notes", "mute", "volume 40", "open Chrome": done straight away when said, no need to
    /// ask Claude. Returns what to do, or null to let Claude handle it.
    /// </summary>
    Action? VoiceCommand(string said)
    {
        // Hindi and Hinglish ("awaaz kam karo", "chrome kholo") in the English the commands below know.
        var words = WakeMatcher.Normalize(Speech.Hindi ? Hinglish.ToCommand(said) : said);
        // "Good morning", "brief me", "what's my day like": the day's briefing.
        var withoutName = System.Text.RegularExpressions.Regex.Replace(WakeMatcher.Normalize(said), $@" {System.Text.RegularExpressions.Regex.Escape(WakeMatcher.Normalize(CatName))}$", "");
        if (Briefing.Asked(words) || Briefing.Asked(withoutName))
            return () => _ = Brief(spoken: _voice is { Active: true });
        // Everyday sends and reminders, alone or two or three joined by "and": worked out here, no waiting for Claude.
        if (_settings.QuickPath && QuickPath.Plan(said, DateTime.Now) is { } quick)
        {
            AiLog.Write("quick", string.Join("; ", quick.Select(i => i.ToString())));
            return () => _ = RunQuick(quick, said);
        }
        // Two things in one go ("play Kesariya and turn the volume down"): the chat does both, rather
        // than an instant command doing one part and dropping the rest. "Open Spotify and play…" is one thing.
        var asSaid = WakeMatcher.Normalize(said);
        if (System.Text.RegularExpressions.Regex.IsMatch(asSaid, @"\b(and|aur|then|phir|also|plus)\b")
            && !System.Text.RegularExpressions.Regex.IsMatch(asSaid, @"^(can you |could you |please )?(open spotify and |spotify (pe|par) )")
            && asSaid.Split(' ').Length > 3)
            return null;
        if (words is "take notes" or "take meeting notes" or "start taking notes" or "start notes" or "start the notes" or "take notes please" or "start meeting notes" or "take some notes")
        {
            return () =>
            {
                if (_meeting != null) Chat.Say(new CatDid("\uE8F2", "I'm already taking notes."));
                else
                {
                    StartNotes();
                    Chat.Say(new CatDid("\uE8F2", $"Taking notes. Say \"Hey {CatName}, stop notes\" when the meeting's over."));
                }
            };
        }
        if (words is "stop notes" or "stop taking notes" or "stop the notes" or "end notes" or "finish notes" or "stop meeting notes" or "end the meeting notes" or "stop note taking")
        {
            return () =>
            {
                if (_meeting == null) Chat.Say(new CatDid("\uE8F2", "I wasn't taking notes."));
                else
                {
                    StopNotes();
                    Chat.Say(new CatDid("\uE8F2", "Stopped. Writing the notes now; they'll pop up when ready."));
                }
            };
        }
        if (_meeting != null && words is "mark that" or "mark this" or "mark it" or "bookmark that" or "bookmark this" or "remember that" or "note that")
        {
            return () =>
            {
                _meeting.Mark();
                Chat.Say(new CatDid("\uE734", "Marked."));
            };
        }
        // Hearing the wake word already cut the reading off; these just say so.
        if (words is "stop" or "stop reading" or "stop talking" or "be quiet" or "quiet" or "shush" or "stop it" or "thats enough" or "enough")
        {
            _mouth?.Stop();
            return () => Chat.Say(new CatDid("\uE71A", "Stopped."));
        }
        if (words is "stop recording" or "stop the recording" or "stop screen recording" or "end recording" or "finish recording" or "stop recording the screen"
            && _recording is { State: RecordingSession.Stage.Recording } recording)
        {
            return () =>
            {
                recording.Toggle();
                Chat.Say(new CatDid("\uE71A", "Stopped recording. Saving it now."));
            };
        }
        if (words is "what colour is this" or "what color is this" or "what colour is that" or "what color is that" or "whats this colour" or "whats this color" or "colour under the pointer" or "color under the pointer")
        {
            var (hex, r, g, b) = ScreenColour.UnderPointer();
            TrySetClipboard(hex);
            return () => Chat.Say(new CatDid("\uE790", $"That's {hex}, {ScreenColour.Name(r, g, b)} (rgb {r}, {g}, {b}). Copied."));
        }
        if (words is "captions" or "captions on" or "turn on captions" or "live captions" or "show captions" or "subtitles" or "subtitles on" or "turn on subtitles" or "start captions")
            return () => { StartCaptions(null); Chat.Say(new CatDid("\uE7F0", "Captions on for whatever's playing. Say \"captions off\" to stop.")); };
        if (words is "captions off" or "turn off captions" or "stop captions" or "hide captions" or "subtitles off" or "turn off subtitles" or "stop subtitles")
            return () => { StopCaptions(); Chat.Say(new CatDid("\uE7F0", "Captions off.")); };
        // Questions and openings answered on this PC in a moment, without Claude.
        if (Instant(words) is { } instant) return instant;
        // A routine's phrase ("start work mode").
        if (Routines.Match(said) is { } routine)
            return () => RunRoutine(routine, spoken: true);
        // The PC's own controls, said plainly.
        if (PcCommands.Try(words) is { } done)
        {
            AiLog.Write("pc", $"{words}: {done}");
            return () => Chat.Say(new CatDid("\uE7F4", done));
        }
        // "Open Spotify": only when the name clearly is an app (else "open the invoice" goes to Claude).
        if (System.Text.RegularExpressions.Regex.Match(words, @"^(open|launch|start)( up)? (the )?(?<app>.+?)( app)?$") is { Success: true } open
            && Apps.Find(open.Groups["app"].Value, strict: true) is { } app)
        {
            var opened = Apps.Open(app.Name);
            return () => Chat.Say(new CatDid("\uE8A7", opened));
        }
        return null;
    }

    // ---- Meetings from the calendar ----

    List<Core.Mail.CalendarEvent> _upcoming = [];
    DateTime _upcomingRead = DateTime.MinValue;
    readonly HashSet<string> _meetingOffered = [];

    /// <summary>A calendar meeting with a video call is starting: offer to join it, and to take notes.</summary>
    async void WatchMeetings()
    {
        if (!Core.Mail.Gmail.HasCalendar || !_settings.OfferNotes) return;
        try
        {
            var now = DateTime.Now;
            if (now - _upcomingRead > TimeSpan.FromMinutes(10))
            {
                _upcomingRead = now;
                _upcoming = await Core.Mail.Calendar.Between(now.AddMinutes(-5), now.AddHours(2), default);
            }
            foreach (var meeting in _upcoming.Where(e => !e.AllDay && e.JoinLink != null && e.Start <= now.AddMinutes(1) && e.Start >= now.AddMinutes(-3)))
            {
                if (!_meetingOffered.Add(meeting.Id)) continue;
                var link = meeting.JoinLink!;
                void Join() => Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
                ToastWindow? toast = null;
                toast = ToastWindow.Message($"{meeting.Title} is starting", meeting.Attendees.Count > 0 ? $"With {string.Join(", ", meeting.Attendees.Take(4))}." : "Your video call.", null,
                    ("Join and take notes", () => { toast!.CloseAnimated(); Join(); if (_meeting == null) StartNotes(meeting.Title); }),
                    ("Join", () => { toast!.CloseAnimated(); Join(); }),
                    ("Not now", () => toast!.CloseAnimated()));
                ToastHost.Show(toast);
                AiLog.Write("meeting starting", meeting.Title);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't check the calendar", ex);
        }
    }

    // ---- Task reminders ----

    /// <summary>
    /// Puts right a reason the cat was told to stay up that nothing will ever switch off (a card
    /// that closed without saying so, a voice that stopped mid-word): noticing with nothing open,
    /// listening with nothing listening, talking with nothing being said. Then it tucks back down.
    /// </summary>
    void UnstickCat()
    {
        if (_buddy is not { } cat) return;
        bool somethingOpen = _voicePill != null || _bubble != null || _dragWheel != null || _paw != null || _menu != null;
        if (cat.IsNoticing && !somethingOpen)
        {
            Log.Info($"Cat was left noticing with nothing open ({cat.WhyOut()}): let it rest");
            cat.Notice(false);
        }
        if (cat.IsListening && _voice is not { State: VoiceState.Waiting or VoiceState.Hearing } && _agent == null)
        {
            Log.Info($"Cat was left listening with nothing listening ({cat.WhyOut()}): let it rest");
            cat.Listening(false);
        }
        if (cat.IsTalking && _mouth?.Speaking != true)
        {
            Log.Info($"Cat was left talking with nothing being said ({cat.WhyOut()}): let it rest");
            cat.Talking(false);
        }
    }

    void StartReminders()
    {
        _reminders.Tick += (_, _) => { RemindDue(); WatchMeetings(); UnstickCat(); };
        _reminders.Start();
        RemindDue();
    }

    /// <summary>A card for each task whose time has come: Done, an hour later, or tomorrow morning.</summary>
    void RemindDue()
    {
        foreach (var todo in Todos.DueNow())
        {
            todo.Reminded = DateTime.Now;
            todo.SnoozedUntil = null;
            Todos.Update(todo);
            ToastWindow? toast = null;
            var from = todo.From is { Length: > 0 } f ? $"From {f}." : "";
            toast = ToastWindow.Message($"Reminder: {todo.Text}", $"{(todo.Due is { } d && d < DateTime.Today ? $"It was due {todo.DueText()}. " : "")}{from}".Trim(), null,
                ("Done", () => { todo.Done = true; Todos.Update(todo); toast!.CloseAnimated(); }),
                ("10 min", () => Snooze(todo, toast!, DateTime.Now.AddMinutes(10))),
                ("An hour", () => Snooze(todo, toast!, DateTime.Now.AddHours(1))),
                ("Tomorrow", () => Snooze(todo, toast!, DateTime.Today.AddDays(1).AddHours(9))));
            toast.KeepFor(TimeSpan.FromMinutes(3));
            ToastHost.Show(toast);
            _buddy?.Act("startle");
            SayReminder(todo, toast);
        }
    }

    /// <summary>
    /// What an answer to a reminder means: "done", "snooze" (with until when), "ok" (heard, nothing
    /// to do) or "other" (something to ask the cat about it). Short answers only: a long sentence
    /// that mentions "tomorrow" is a request, not a snooze.
    /// </summary>
    static (string Kind, DateTime? Until) ReadReminderAnswer(string said)
    {
        var words = System.Text.RegularExpressions.Regex.Replace(said.ToLowerInvariant(), @"[^a-z0-9' ]", " ").Trim();
        words = System.Text.RegularExpressions.Regex.Replace(words, @"\s+", " ");
        bool Is(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(words, pattern);
        if (Is(@"^(yes |yeah |ok |okay )?(it'?s |i |i've |already )?(done|did it|finished|completed|ho gaya|kar liya|ho gya|ho gaya hai)( it)?( already)?$")) return ("done", null);
        bool shortAnswer = words.Split(' ').Length <= 6;
        var minutes = System.Text.RegularExpressions.Regex.Match(words, @"\b(\d+|five|ten|fifteen|twenty|thirty) (min|mins|minute|minutes)\b");
        if (shortAnswer && minutes.Success)
            return ("snooze", DateTime.Now.AddMinutes(minutes.Groups[1].Value switch { "five" => 5, "ten" => 10, "fifteen" => 15, "twenty" => 20, "thirty" => 30, var n => int.Parse(n) }));
        if (shortAnswer && Is(@"\b(an|one|1) hour\b")) return ("snooze", DateTime.Now.AddHours(1));
        if (shortAnswer && Is(@"^(remind me )?(tomorrow|kal)( morning)?( please)?$")) return ("snooze", DateTime.Today.AddDays(1).AddHours(9));
        if (Is(@"^(later|snooze|snooze it|remind me later|not now|baad mein|thodi der mein|thodi der baad)( please)?$")) return ("snooze", DateTime.Now.AddMinutes(10));
        // Only "heard you" words, in any mix ("haan haan, theek hai", "okay thanks").
        var heardYou = new HashSet<string> { "ok", "okay", "got", "it", "thanks", "thank", "you", "alright", "sure", "yes", "yeah", "yep", "haan", "han", "ha", "theek", "thik", "hai", "accha", "achha", "acha", "cool", "fine", "noted", "right", "sky" };
        if (words.Length > 0 && words.Split(' ').All(heardYou.Contains)) return ("ok", null);
        return ("other", null);
    }

    static void Snooze(Todo todo, ToastWindow toast, DateTime until)
    {
        todo.SnoozedUntil = until;
        Todos.Update(todo);
        toast.CloseAnimated();
        AiLog.Write("snoozed", $"{todo.Text} until {until:ddd HH:mm}");
    }

    /// <summary>
    /// The cat says the reminder, then listens once: "done", "later", "in 20 minutes",
    /// "tomorrow", or something to do about it ("call him on WhatsApp"), which goes to the chat.
    /// </summary>
    async void SayReminder(Todo todo, ToastWindow toast, bool evenIfQuiet = false)
    {
        try
        {
            if (_settings.TalkBack == "off" && !evenIfQuiet || _voice is { Active: true } || Chat.Busy) return;
            await Mouth.Say($"Reminder: {todo.Text}.", _settings.TalkVoice);
            await Mouth.Quiet();
            if (_voice is { Active: true } || Chat.Busy || !Microphone.Present()) return;
            Voice.OneAnswer = said => ReminderAnswer(todo, toast, said);
            _ = Voice.Listen(answering: true);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't say the reminder", ex);
        }
    }

    /// <summary>What was said back to a reminder: done or a snooze is dealt with here; anything else goes to the chat.</summary>
    string? ReminderAnswer(Todo todo, ToastWindow toast, string said)
    {
        var (kind, until) = ReadReminderAnswer(said);
        if (kind == "done")
        {
            todo.Done = true;
            Todos.Update(todo);
            toast.CloseAnimated();
            AiLog.Write("reminder", $"done: {todo.Text}");
            _ = Mouth.Say("Ticked off.", _settings.TalkVoice);
            return null;
        }
        if (kind == "snooze" && until is { } at)
        {
            Snooze(todo, toast, at);
            _ = Mouth.Say(at.Date > DateTime.Today ? "I'll remind you tomorrow morning." : $"I'll remind you at {at:h:mm}.", _settings.TalkVoice);
            return null;
        }
        if (kind == "ok") return null;
        // Something to do about it: the chat takes it, knowing what the reminder was.
        toast.CloseAnimated();
        ShowVoicePill();
        Chat.NextNote = $"This is what the person said back when the reminder \"{todo.Text}\" came up";
        return said;
    }

    /// <summary>
    /// "Show me": File Explorer opens with the files selected, and the cat raises a paw toward it
    /// instead of running across the screen.
    /// </summary>
    void RevealAndPoint(IReadOnlyList<string> paths)
    {
        ExplorerReveal.Show(paths);
        var look = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        look.Tick += (_, _) =>
        {
            look.Stop();
            var explorer = WindowShots.TopOtherWindow();
            if (explorer != IntPtr.Zero && GetWindowRect(explorer, out var r)) _buddy?.PointToward((r.Left + r.Right) / 2.0);
        };
        look.Start();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    /// <summary>
    /// "What's this error?" in one press: a picture of the window in front is taken right away
    /// (before the bubble opens over it), and the cat explains what went wrong and how to fix it.
    /// </summary>
    void ExplainError()
    {
        _menu?.CloseAnimated();
        if (!_settings.AiEnabled || !ClaudeKey.Exists)
        {
            ShowDashboard(DashPage.Ask);
            return;
        }
        if (Chat.Busy) { ShowAskBubble(); return; }
        if (WindowShots.Capture(WindowShots.TopOtherWindow()) is not { } shot)
        {
            ToastHost.Show(ToastWindow.Message("Nothing to look at", "Bring the window with the error to the front, then press " + _settings.ErrorHotkey + " again."));
            return;
        }
        Chat.Show(shot.Png, shot.Title);
        ShowAskBubble("What's this error? Explain it simply and tell me how to fix it.");
    }

    /// <summary>
    /// Paw to ask: the screen holds still, the paw picks something, and the bubble opens beside it
    /// with that picture ready to ask about. From voice, it listens straight away.
    /// </summary>
    void StartPaw(bool listen = false)
    {
        if (_paw != null || _snip != null || _picker != null) return;
        _menu?.CloseAnimated();
        if (!_settings.AiEnabled || !ClaudeKey.Exists)
        {
            ShowDashboard(DashPage.Ask);
            return;
        }
        _bubble?.Close();
        _paw = new PawWindow(Skin);
        _paw.Closed += (_, _) => _paw = null;
        _paw.Picked += pick =>
        {
            Chat.Show(pick.Png, pick.WindowTitle);
            double scale = NativeMethods.SystemScale;
            var area = SystemParameters.WorkArea;
            double centre = (pick.ScreenPx.X + pick.ScreenPx.Width / 2.0) / scale;
            double top = pick.ScreenPx.Y / scale, bottom = (pick.ScreenPx.Y + pick.ScreenPx.Height) / scale;
            // Above what was picked when there's room, otherwise below it.
            var spot = top - area.Top > 360 ? new Point(centre, top - 6) : new Point(centre, Math.Min(area.Bottom - 8, bottom + 380));
            ShowAskBubble(at: spot);
            if (listen) _ = Voice.Listen();
        };
        _paw.Show();
    }

    VoicePill? _voicePill;

    /// <summary>
    /// "Hey Sky": the small voice pill above the cat (or the chat, if it's already open, or if the
    /// cat still needs setting up). False when there's nowhere to talk to it.
    /// </summary>
    bool ShowVoicePill()
    {
        if (_bubble != null) return true;
        if (!_settings.AiEnabled || !ClaudeKey.Exists)
        {
            ShowAskBubble();
            return _bubble != null;
        }
        if (_voicePill != null) return true;
        Chat.NextScreen = ExplorerSelection.DesktopInFront()
            ? new ScreenContext(null, ExplorerSelection.FromDesktop(), FromDesktop: true)
            : Chat.NextScreen ?? (ExplorerSelection.Frontmost() is { } front ? new ScreenContext(front.Folder, front.Selected) : null);
        var palette = (_settings.DashboardDark ?? DashPalette.WindowsIsDark()) ? DashPalette.Night : DashPalette.Light;
        VoicePill? pill = null;
        pill = _voicePill = new VoicePill(Chat, Voice, palette, () => GrowIntoBubble(pill!));
        pill.Closed += (_, _) =>
        {
            if (_voicePill == pill) _voicePill = null;
            if (_bubble == null) _buddy?.Notice(false);
        };
        pill.Place(BubbleSpot(null));
        pill.Show();
        _buddy?.Notice(true);
        return true;
    }

    /// <summary>
    /// The pill needs more room: the chat opens see-through with the whole voice conversation, the
    /// pill springs into its shape, then the chat fades in over it.
    /// </summary>
    void GrowIntoBubble(VoicePill pill)
    {
        if (_voicePill == pill) _voicePill = null;
        ShowAskBubble(from: pill.Start, hidden: _bubble == null);
        if (_bubble is { } bubble)
        {
            bubble.UpdateLayout();
            pill.MorphInto(bubble.CardRect, AskBubble.Radius, bubble.Reveal);
        }
        else pill.Close();
    }

    /// <summary>Just above the cat (in screen units), or the bottom right when it's hidden.</summary>
    Point BubbleSpot(Point? at)
    {
        double scale = NativeMethods.SystemScale;
        var area = SystemParameters.WorkArea;
        if (at is { } given) return given;
        if (_buddy is not { IsVisible: true } cat) return new Point(area.Right - 260, area.Bottom - 8);
        // The bubble grows up from its bottom centre: above the cat on the bottom edge; beside or
        // below it on the other edges, with room for a full-size bubble.
        if (cat.Edge == CatEdge.Bottom) return new Point(cat.WheelSpotPx(0).X / scale, cat.WheelSpotPx(0).Y / scale);
        var spot = CatWindow.Beside(cat.BodyDip(), cat.Away, new Size(440, 640), 12);
        return new Point(spot.X + 220, Math.Min(area.Bottom - 8, spot.Y + 640));
    }

    /// <summary>
    /// The ask shortcut: a bubble above the cat. What's selected is read first, while File Explorer
    /// (or the desktop) is still in front, so "these" means what you were looking at.
    /// </summary>
    void ShowAskBubble(string? say = null, Point? at = null, int? from = null, bool hidden = false)
    {
        _menu?.CloseAnimated();
        // The full chat takes over from the small voice pill.
        if (_voicePill is { } open)
        {
            _voicePill = null;
            open.Close();
        }
        if (!_settings.AiEnabled || !ClaudeKey.Exists)
        {
            // Setting up happens on the Ask page.
            ShowDashboard(DashPage.Ask);
            return;
        }
        if (_bubble == null)
        {
            Chat.NextScreen = ExplorerSelection.DesktopInFront()
                ? new ScreenContext(null, ExplorerSelection.FromDesktop(), FromDesktop: true)
                : Chat.NextScreen ?? (ExplorerSelection.Frontmost() is { } front ? new ScreenContext(front.Folder, front.Selected) : null);
            var palette = (_settings.DashboardDark ?? DashPalette.WindowsIsDark()) ? DashPalette.Night : DashPalette.Light;
            _bubble = new AskBubble(Chat, Voice, palette, CatName, CatSprites.Head(Skin), OpenFile, () => ShowDashboard(DashPage.Ask), from);
            _bubble.Closed += (_, _) => _bubble = null;
            if (hidden) _bubble.Opacity = 0;
            _bubble.Place(BubbleSpot(at));
            _bubble.Show();
            _buddy?.Notice(true);
            _bubble.Closed += (_, _) => _buddy?.Notice(false);
        }
        else _bubble.FocusBox();
        if (!string.IsNullOrWhiteSpace(say)) _ = Chat.Send(say);
    }

    async System.Threading.Tasks.Task MeetingTest(string wav)
    {
        try
        {
            using var reader = new NAudio.Wave.WaveFileReader(wav);
            var bytes = new byte[reader.Length];
            reader.ReadExactly(bytes);
            var meeting = new Meeting { App = "Test" };
            IMeetingTranscriber words = Deepgram.HasKey ? new DeepgramMeeting(Deepgram.Key.Read()!, Speech.KeyTerms(CatName)) : new WhisperMeeting();
            words.Line += line => { lock (meeting) meeting.Add(line); };
            var watch = Stopwatch.StartNew();
            // A tenth of a second at a time, at real speed, like the live call.
            for (int at = 0; at < bytes.Length; at += 6400)
            {
                words.Send(bytes[at..Math.Min(bytes.Length, at + 6400)]);
                var due = (at + 6400) / 64.0 - watch.Elapsed.TotalMilliseconds;
                if (due > 1) await System.Threading.Tasks.Task.Delay((int)due);
            }
            await words.FinishAsync();
            await words.DisposeAsync();
            meeting.Seconds = bytes.Length / 64000.0;
            meeting.Lines.Sort((a, b) => a.At.CompareTo(b.At));
            Log.Info($"meeting-test: {meeting.Lines.Count} lines with {words.Name}:\n{meeting.Transcript()}");
            using var cancel = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
            await MeetingNotes.Write(meeting, Names.First ?? "", _ => { }, cancel.Token);
            MeetingStore.Save(meeting);
            Log.Info($"meeting-test notes: {meeting.Title} | {meeting.Summary} | tasks: {string.Join("; ", meeting.Tasks.Select(t => $"{t.Text} [{t.Owner}, {t.Due}]"))} | decisions: {string.Join("; ", meeting.Decisions)} | questions: {string.Join("; ", meeting.Questions)}");
        }
        catch (Exception ex)
        {
            Log.Error("meeting-test failed", ex);
        }
    }

    async System.Threading.Tasks.Task VoiceTest(string[] files)
    {
        try
        {
            await VoicePrint.Prepare(null, default);
            int split = Array.IndexOf(files, "then");
            var learn = files[..split].Select(ReadWav).ToArray();
            var watch = Stopwatch.StartNew();
            VoicePrint.Enroll(learn);
            Log.Info($"voice-test: learned from {learn.Length} in {watch.ElapsedMilliseconds} ms");
            foreach (var file in files[(split + 1)..])
            {
                watch.Restart();
                var likeness = VoicePrint.Likeness(ReadWav(file));
                Log.Info($"voice-test: {Path.GetFileName(file)} likeness {likeness:0.000} ({watch.ElapsedMilliseconds} ms)");
            }
            VoicePrint.Forget();
        }
        catch (Exception ex)
        {
            Log.Error("voice-test failed", ex);
        }
    }

    static float[] ReadWav(string wav)
    {
        using var reader = new NAudio.Wave.WaveFileReader(wav);
        var all16 = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(reader);
        NAudio.Wave.ISampleProvider source = all16.WaveFormat.Channels == 2 ? new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(all16) : all16;
        if (source.WaveFormat.SampleRate != 16000) source = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(source, 16000);
        var all = new List<float>();
        var buffer = new float[16000];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) all.AddRange(buffer.Take(read));
        return all.ToArray();
    }

    async System.Threading.Tasks.Task StreamFile(string wav)
    {
        try
        {
            var samples = ReadWav(wav);
            var key = Deepgram.Key.Read() ?? throw new InvalidOperationException("no Deepgram key");
            var watch = Stopwatch.StartNew();
            await using var live = await DeepgramLive.Open(key, 16000, Speech.KeyTerms(CatName), default);
            Log.Info($"stream-file: connected in {watch.ElapsedMilliseconds} ms");
            bool ended = false;
            live.Partial += text => Log.Info($"stream-file: {watch.ElapsedMilliseconds} ms partial \"{text}\"");
            live.Ended += () => { ended = true; Log.Info($"stream-file: {watch.ElapsedMilliseconds} ms Deepgram says finished"); };
            // 20 ms pieces at real speed, then two seconds of silence, as a person pausing.
            var all = samples.Concat(new float[32000]).ToArray();
            // Paced by the clock (Task.Delay alone runs slow on Windows' 15 ms timer), like a real microphone.
            var pace = Stopwatch.StartNew();
            for (int i = 0; i < all.Length && !ended; i += 320)
            {
                live.Send(all[i..Math.Min(all.Length, i + 320)]);
                var due = (i + 320) / 16.0 - pace.Elapsed.TotalMilliseconds;
                if (due > 1) await System.Threading.Tasks.Task.Delay((int)due);
            }
            Log.Info($"stream-file: audio sent by {watch.ElapsedMilliseconds} ms");
            Log.Info($"stream-file: final \"{await live.Finish()}\" after {watch.ElapsedMilliseconds} ms ({samples.Length / 16000.0:0.0} s of speech)");
        }
        catch (Exception ex)
        {
            Log.Error("stream-file failed", ex);
        }
    }

    async System.Threading.Tasks.Task ScribeFile(string wav)
    {
        try
        {
            var samples = ReadWav(wav);
            var key = ElevenLabs.Key.Read() ?? throw new InvalidOperationException("no ElevenLabs key");
            var watch = Stopwatch.StartNew();
            await using var live = await ScribeLive.Open(key, 16000, Speech.KeyTerms(CatName), default);
            Log.Info($"scribe-file: connected in {watch.ElapsedMilliseconds} ms");
            bool ended = false;
            live.Partial += text => Log.Info($"scribe-file: {watch.ElapsedMilliseconds} ms partial \"{text}\"");
            live.Ended += () => { ended = true; Log.Info($"scribe-file: {watch.ElapsedMilliseconds} ms Scribe says finished"); };
            var all = samples.Concat(new float[32000]).ToArray();
            var pace = Stopwatch.StartNew();
            for (int i = 0; i < all.Length && !ended; i += 320)
            {
                live.Send(all[i..Math.Min(all.Length, i + 320)]);
                var due = (i + 320) / 16.0 - pace.Elapsed.TotalMilliseconds;
                if (due > 1) await System.Threading.Tasks.Task.Delay((int)due);
            }
            Log.Info($"scribe-file: audio sent by {watch.ElapsedMilliseconds} ms");
            Log.Info($"scribe-file: final \"{await live.Finish()}\" after {watch.ElapsedMilliseconds} ms ({samples.Length / 16000.0:0.0} s of speech)");
        }
        catch (Exception ex)
        {
            Log.Error("scribe-file failed", ex);
        }
    }

    async System.Threading.Tasks.Task ScribeLanguageTest(string wav)
    {
        var terms = Speech.KeyTerms(CatName).ToList();
        foreach (var language in new string?[] { null, "en", "hi" })
        {
            try { Log.Info($"scribe-lang-test whole {language ?? "auto"}: \"{await ElevenLabs.Transcribe(File.ReadAllBytes(wav), terms, default, language)}\""); }
            catch (Exception ex) { Log.Info($"scribe-lang-test whole {language ?? "auto"}: error {ex.Message}"); }
        }
        var samples = ReadWav(wav);
        foreach (var (language, secondary) in new (string?, string?)[] { (null, null), ("en", null), ("en", "hi"), ("hi", null) })
        {
            try
            {
                await using var live = await ScribeLive.Open(ElevenLabs.Key.Read()!, 16000, terms, default, language, secondary);
                var all = samples.Concat(new float[16000]).ToArray();
                var pace = Stopwatch.StartNew();
                for (int i = 0; i < all.Length; i += 320)
                {
                    live.Send(all[i..Math.Min(all.Length, i + 320)]);
                    var due = (i + 320) / 16.0 - pace.Elapsed.TotalMilliseconds;
                    if (due > 1) await System.Threading.Tasks.Task.Delay((int)due);
                }
                Log.Info($"scribe-lang-test live {language ?? "auto"}{(secondary != null ? "+" + secondary : "")}: \"{await live.Finish()}\"");
            }
            catch (Exception ex) { Log.Info($"scribe-lang-test live {language ?? "auto"}: error {ex.Message}"); }
        }
    }

    async System.Threading.Tasks.Task WakeFile(string wav)
    {
        string text;
        var watch = Stopwatch.StartNew();
        try
        {
            var samples = ReadWav(wav);
            text = await WakeListener.Recognize(samples, CatName, default);
            var (quickHit, quickRest) = WakeMatcher.Match(text, CatName, _settings.WakeVariants);
            if (quickHit && quickRest.Length > 0)
                Log.Info($"wake-file: request after the name, in full: {await WakeListener.RequestAfterName(samples, CatName, _settings.WakeVariants, quickRest, default)} ({watch.ElapsedMilliseconds} ms in all)");
        }
        catch (Exception ex)
        {
            Log.Error("wake-file failed", ex);
            return;
        }
        var (hit, after) = WakeMatcher.Match(text, CatName, _settings.WakeVariants);
        if (VoicePrint.Likeness(ReadWav(wav)) is { } likeness) Log.Info($"wake-file: voice likeness {likeness:0.00} (needs {VoicePrint.Enough(_settings.VoiceLockLevel):0.00})");
        Log.Info($"wake-file: heard \"{text}\" -> {(hit ? $"wakes, then \"{after}\"" : "no wake")} in {watch.ElapsedMilliseconds} ms");
    }

    async System.Threading.Tasks.Task HearFile(string wav)
    {
        try
        {
            var engine = Speech.ForCommands(_settings, () => CatName);
            var text = await engine.Transcribe(ReadWav(wav), "", default);
            var command = WakeMatcher.Normalize(Speech.Hindi ? Hinglish.ToCommand(text) : text);
            Log.Info($"hear-file ({engine.Name}, {Speech.Language}): \"{text}\" -> \"{command}\" pc={PcCommands.Match(command) != null}");
        }
        catch (Exception ex)
        {
            Log.Error("hear-file failed", ex);
        }
    }

    /// <summary>Dev aid for voice without a microphone: transcribe a WAV and use it like speech.</summary>
    async System.Threading.Tasks.Task SayFile(string wav)
    {
        try
        {
            var samples = ReadWav(wav);
            var watch = Stopwatch.StartNew();
            // The engine commands use (Deepgram when chosen), in the language set.
            var text = await Speech.ForCommands(_settings, () => CatName).Transcribe(samples, $"{CatName}. Convert, merge, compress, PDF, JPG, Downloads.", default);
            Log.Info($"say-file: heard \"{text}\" in {watch.ElapsedMilliseconds} ms ({samples.Length / 16000.0:0.0} s of audio)");
            await Voice.Use(text, listenAfter: false);
        }
        catch (Exception ex)
        {
            Log.Error("say-file failed", ex);
        }
    }

    /// <summary>Runs one step of a plan the person confirmed: a job, or an editor in the dashboard.</summary>
    Job? StartForCat(BroAction action, List<string> files, out bool openedWindow)
    {
        openedWindow = false;
        if (action.Combine) files.Sort(NaturalComparer.Instance);
        if (action.Editor != null)
        {
            var accepted = files.Where(action.Accepts).ToList();
            if (accepted.Count >= action.MinFiles && action.Editor(accepted) is { } editor)
            {
                InDashboard(editor);
                openedWindow = true;
                return null;
            }
        }
        return JobRunner.Start(action, files);
    }

    /// <summary>Videos and music play in the dashboard; everything else opens in its usual app.</summary>
    void OpenFile(string path)
    {
        if (PlayerView.CanPlay(path) && !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            ShowDashboard();
            _dashboard!.Play(path);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {path}", ex);
        }
    }

    bool UseToolForCat(string tool, string? text)
    {
        // Let the bubble get out of the way of whatever comes next.
        _bubble?.Close();
        switch (tool)
        {
            case "snip": StartSnip(); return true;
            case "record_screen": StartRecording(); return true;
            case "stop_recording":
                if (_recording is not { State: RecordingSession.Stage.Recording } running) return false;
                running.Toggle();
                return true;
            case "pick_colour": StartColourPicker(); return true;
            case "paw_to_ask": Dispatcher.BeginInvoke(() => StartPaw(listen: _voice?.FollowingUp == true || _voice?.Active == true)); return true;
            case "make_qr": ShowQr(text ?? ""); return true;
            case "connect_phone": Dispatcher.BeginInvoke(() => ShowPhoneConnect()); return true;
            case "paste_clipboard": PasteToCat(); return true;
            case "open_history": ShowDashboard(DashPage.History); return true;
            case "open_settings": ShowSettings(); return true;
            case "take_notes": Dispatcher.BeginInvoke(() => StartNotes()); return true;
            case "stop_notes": Dispatcher.BeginInvoke(StopNotes); return true;
            case "open_tasks": Dispatcher.BeginInvoke(() => ShowDashboard(DashPage.Tasks)); return true;
            case "open_meetings": Dispatcher.BeginInvoke(() => ShowDashboard(DashPage.Meetings)); return true;
            case "captions_on": Dispatcher.BeginInvoke(() => StartCaptions(text)); return true;
            case "captions_off": Dispatcher.BeginInvoke(StopCaptions); return true;
            default: return false;
        }
    }

    // ---- Convert what's selected ----

    /// <summary>The shortcut's job: the wheel, at the mouse, for whatever is selected in Explorer or on the desktop.</summary>
    void ConvertSelection()
    {
        var selected = ExplorerSelection.FromForeground();
        if (selected is { Count: > 0 })
        {
            NativeMethods.GetCursorPos(out var cursor);
            ShowWheel(FileList.Expand(selected), new Point(cursor.X, cursor.Y));
            if (!_settings.UsedConvertHotkey) { _settings.UsedConvertHotkey = true; _settings.Save(); }
            return;
        }
        ToastHost.Show(ToastWindow.Message(selected == null ? "Select files in File Explorer" : "Nothing selected",
            $"Click a file in File Explorer or on the desktop, then press {_settings.ConvertHotkey}."));
    }

    // ---- Screen recording ----

    /// <summary>The record shortcut: start a recording, or stop the one that's running.</summary>
    public void StartRecording()
    {
        if (_recording != null)
        {
            _recording.Toggle();
            return;
        }
        if (_snip != null || _picker != null) return;
        _menu?.CloseAnimated();
        var session = new RecordingSession(_settings);
        session.Saved += (file, centre) => ShowWheel([file], centre);
        session.Ended += () => { if (_recording == session) _recording = null; };
        _recording = session;
        session.Begin();
    }

    // ---- Colour picker ----

    public void StartColourPicker()
    {
        if (_picker != null || _snip != null) return;
        _menu?.CloseAnimated();
        _picker = new ColourPickerWindow();
        _picker.Closed += (_, _) => _picker = null;
        _picker.Picked += OnColourPicked;
        _picker.Show();
    }

    void OnColourPicked(Color colour)
    {
        var hex = ColourPickerWindow.Hex(colour);
        var rgb = ColourPickerWindow.Rgb(colour);
        var hsl = ColourPickerWindow.Hsl(colour);
        bool copied = TrySetClipboard(hex);
        ToastWindow? toast = null;
        toast = ToastWindow.Message(copied ? $"{hex} copied" : hex, $"{rgb}   {hsl}", colour,
            ("Copy RGB", () => { if (TrySetClipboard(rgb)) toast!.SetMessage($"Copied {rgb}"); }),
            ("Copy HSL", () => { if (TrySetClipboard(hsl)) toast!.SetMessage($"Copied {hsl}"); }),
            ("Pick again", () => { toast!.CloseAnimated(); StartColourPicker(); }));
        ToastHost.Show(toast);
    }

    static bool TrySetClipboard(string text)
    {
        try
        {
            ClipboardTools.Retry(() => Clipboard.SetText(text));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not set the clipboard", ex);
            return false;
        }
    }

    // ---- QR codes ----

    QrWindow? _qr;

    // ---- The phone (Shiki for Android, over the Wi-Fi) ----

    Core.Phone.PhoneLink? _phone;
    PhoneLinkWindow? _phoneWindow;

    void StartPhoneLink()
    {
        try
        {
            _phone = Core.Phone.PhoneLink.Start(_settings);
            _phone.PairRequested += request => Dispatcher.BeginInvoke(() => ShowPhoneConnect(request));
            _phone.JobFinished += job => Dispatcher.BeginInvoke(() => PhoneFilesArrived(job));
            _phone.PhonePaired += phone => Dispatcher.BeginInvoke(() =>
            {
                _phoneWindow?.ShowPaired(phone);
                ToastHost.Show(ToastWindow.Message($"{phone.Name} is connected", $"Photos and files from it land in {_phone.PhotoFolder}."));
            });
        }
        catch (Exception ex)
        {
            Log.Error("Phone link didn't start", ex);
        }
    }

    /// <summary>The connect window (a phone asking to pair opens it, with the code to check).</summary>
    void ShowPhoneConnect(Core.Phone.PairRequest? request = null)
    {
        if (_phone == null) return;
        _menu?.CloseAnimated();
        if (_phoneWindow == null)
        {
            _phoneWindow = new PhoneLinkWindow(_phone);
            _phoneWindow.Closed += (_, _) => _phoneWindow = null;
            InDashboard(_phoneWindow);
        }
        else _phoneWindow.Activate();
        if (request != null) _phoneWindow.ShowRequest(request);
    }

    /// <summary>Files came from the phone by themselves (shared, or new photos): say so, with a way to see them.</summary>
    void PhoneFilesArrived(Core.Phone.PhoneJob job)
    {
        if (job.Quiet || job.Saved.Count == 0 && job.Failed == 0) return;
        var phone = _phone?.Phones.FirstOrDefault(p => p.Id == job.Phone)?.Name ?? "your phone";
        int photos = job.Saved.Count(f => Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".heic" or ".heif" or ".webp" or ".dng" or ".gif");
        int videos = job.Saved.Count(f => Path.GetExtension(f).ToLowerInvariant() is ".mp4" or ".mov" or ".3gp" or ".mkv" or ".webm" or ".m4v");
        int others = job.Saved.Count - photos - videos;
        var parts = new List<string>();
        if (photos > 0) parts.Add(photos == 1 ? "1 photo" : $"{photos} photos");
        if (videos > 0) parts.Add(videos == 1 ? "1 video" : $"{videos} videos");
        if (others > 0) parts.Add(others == 1 ? "1 file" : $"{others} files");
        var what = parts.Count == 0 ? "Nothing" : string.Join(" and ", parts);
        // Checked byte for byte on the way; photos also by size against the phone's own.
        var line = job.Saved.Count == 0 ? ""
            : job.Smaller.Count > 0 ? $"{(job.Smaller.Count == 1 ? "1 photo" : $"{job.Smaller.Count} photos")} came out smaller than on the phone: {job.Smaller[0]}."
            : job.SameSize > 0 ? $"Full quality, each checked: {job.LastPixels}, the same as on the phone. {Path.GetFileName(job.Saved[^1])}"
            : $"Full quality, each checked. {Path.GetFileName(job.Saved[^1])}";
        if (job.Smaller.Count > 0) Log.Info("Phone photos smaller than on the phone: " + string.Join("; ", job.Smaller));
        if (job.Failed > 0) line += (line.Length > 0 ? " " : "") + $"{job.Failed} didn't come through.";
        AiLog.Write("phone", $"{what} from {phone}");
        var folder = job.Saved.Count > 0 ? Path.GetDirectoryName(job.Saved[^1]) : null;
        ToastHost.Show(ToastWindow.Message($"{what} from {phone}", line, null,
            ("Open folder", () => { if (folder != null) OpenFolder(folder); }),
            ("See them", () => ShowDashboard(DashPage.Phone))));
        _dashboard?.PhoneFilesChanged();
    }

    void ShowQr(string text)
    {
        _menu?.CloseAnimated();
        _qr?.Close();
        _qr = new QrWindow(text);
        _qr.Closed += (_, _) => _qr = null;
        InDashboard(_qr);
    }

    // ---- Paste ----

    /// <summary>
    /// Whatever is on the clipboard goes to the cat: copied files open the wheel, a copied picture
    /// (a screenshot, an image from a browser) is saved and opens the wheel, text becomes a QR code.
    /// </summary>
    void PasteToCat()
    {
        _menu?.CloseAnimated();
        NativeMethods.GetCursorPos(out var cursor);
        var centre = CatSpot(new Point(cursor.X, cursor.Y));
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                ShowWheel(FileList.Expand(files), centre);
                return;
            }
            if (Clipboard.ContainsImage() || Clipboard.ContainsData("PNG"))
            {
                ShowWheel([SavePastedImage()], centre);
                return;
            }
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                var path = text.Trim('"');
                if ((File.Exists(path) || Directory.Exists(path)) && !path.Contains('\n'))
                {
                    ShowWheel(FileList.Expand([path]), centre);
                    return;
                }
                if (text.Length > 0)
                {
                    ShowQr(text);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Paste failed", ex);
            ToastHost.Show(ToastWindow.Message("Couldn't read the clipboard", "Another app may be holding it. Try again."));
            return;
        }
        ToastHost.Show(ToastWindow.Message("Nothing to paste", "Copy a file, a picture or some text first."));
    }

    /// <summary>Saves the clipboard picture where snips go.</summary>
    static string SavePastedImage()
    {
        var folder = OutputPaths.CreatedFolder;
        var file = Path.Combine(folder, $"Pasted {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        // Browsers and design apps put a real PNG (with transparency) on the clipboard; prefer it.
        if (Clipboard.GetData("PNG") is MemoryStream png)
        {
            File.WriteAllBytes(file, png.ToArray());
            return file;
        }
        // A plain bitmap (screenshots): its alpha channel is often garbage, so drop it.
        var image = Clipboard.GetImage() ?? throw new InvalidOperationException("The clipboard picture couldn't be read.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0)));
        using var stream = File.Create(file);
        encoder.Save(stream);
        return file;
    }

    public void StartSnip()
    {
        if (_snip != null || _picker != null) return;
        _menu?.CloseAnimated();
        _snip = new SnipWindow();
        _snip.Closed += (_, _) => _snip = null;
        _snip.Snipped += (file, centre) => ShowWheel([file], centre);
        _snip.Show();
    }

    void SetBuddyVisible(bool visible)
    {
        if (_buddy == null) return;
        if (visible) _buddy.Show(); else _buddy.Hide();
        _settings.BuddyVisible = visible;
        _settings.Save();
    }

    void PickFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick files for Shiki",
            Multiselect = true,
            Filter = "Everything I can convert|" + string.Join(";", FileKinds.AllExtensions.Select(x => "*" + x)) + "|All files|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        NativeMethods.GetCursorPos(out var cursor);
        ShowWheel(dialog.FileNames, CatSpot(new Point(cursor.X, cursor.Y)));
    }

    void ExitIfIdle()
    {
        bool idle = _headless && JobRunner.ActiveCount == 0 && _pending.Count == 0
            && !Windows.OfType<Window>().Any(w => w.IsVisible);
        if (idle) Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
