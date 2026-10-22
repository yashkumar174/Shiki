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
    int _snipHotkeyId = -1, _colourHotkeyId = -1, _convertHotkeyId = -1, _recordHotkeyId = -1, _askHotkeyId = -1, _talkHotkeyId = -1;
    RecordingSession? _recording;
    SnipWindow? _snip;
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
        Theme.EditorDark = _settings.DashboardDark ?? DashPalette.WindowsIsDark();
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

        switch (args[0])
        {
            case "run" when args.Length >= 3:
                Queue(args[1], args[2..]);
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
            case "snapshot-teach" when args.Length == 2:
                // Dev aid: the teach-your-cat page drawn off screen.
                EnterBuddyMode();
                var teach = new WakeSetupWindow(CatName, _settings.WakeVariants, _settings.WakeWord, (_, _, _) => { });
                DashboardWindow.SaveElement((FrameworkElement)teach.Content, teach.Width, args[1]);
                teach.Close();
                break;
            case "snapshot-bubble" when args.Length == 2:
                // Dev aid: the ask bubble drawn off screen.
                EnterBuddyMode();
                var palette = (_settings.DashboardDark ?? DashPalette.WindowsIsDark()) ? DashPalette.Night : DashPalette.Light;
                var bubble = new AskBubble(Chat, Voice, palette, CatName, CatSprites.Head(Skin), OpenFile, () => { });
                DashboardWindow.SaveElement((FrameworkElement)bubble.Content, bubble.Width, args[1]);
                bubble.Close();
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
        _menu = new BroMenu(anchorPx, $"Drop files on me, Shift + drag one, or select files and press {_settings.ConvertHotkey}.",
        [
            new MenuEntry("\uE80F", "Open Shiki", () => ShowDashboard()),
            new MenuEntry("\uE8E5", "Convert files…", PickFiles),
            .. (_settings.OutputFolder is { } results ? [new MenuEntry("\uE838", "Open results folder", () => OpenFolder(results))] : Array.Empty<object?>()),
            new MenuEntry("\uE77F", "Paste from clipboard", PasteToCat),
            _settings.WakeVariants.Count > 0
                ? new MenuEntry("\uE720", $"Listen for \"Hey {CatName}\"", ToggleWake, () => _settings.WakeWord)
                : new MenuEntry("\uE720", $"Teach me to answer to \"Hey {CatName}\"…", TeachName),
            new MenuEntry("\uE924", $"Snip and convert ({_settings.SnipHotkey})", StartSnip),
            new MenuEntry("\uE714", $"Record the screen ({_settings.RecordHotkey})", StartRecording),
            new MenuEntry("\uE790", $"Pick a colour ({_settings.ColourHotkey})", StartColourPicker),
            new MenuEntry("\uED14", "Make a QR code…", () => ShowQr("")),
            new MenuEntry(visible ? "\uE7B3" : "\uE890", visible ? "Hide the cat" : "Show the cat", () => SetBuddyVisible(!visible)),
            null,
            new MenuChoice("Cat", CatSprites.Skins.Select(k => new ChoiceOption(k.Id, $"{k.Name}: {k.Description}", CatSprites.Head(k))).ToList(),
                () => _settings.Cat, SetCat),
            new MenuChoice("Look", [new ChoiceOption("arcade", "Arcade", Font: Theme.PixelFont), new ChoiceOption("glass", "Glass", Font: Theme.DisplayFont)],
                () => _settings.Style, SetStyle),
            null,
            new MenuEntry("\uE713", "Settings…", ShowSettings),
            new MenuEntry("\uE81C", "Open log folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.DataDir}\"") { UseShellExecute = true })),
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
        _settings.Cat = id;
        _settings.Save();
        _buddy?.SetSkin(Skin);
        _tray?.SetCat(Skin);
        // The Explorer menu shows the cat too.
        if (ContextMenuRegistrar.IsRegistered()) ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(Skin));
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
        _settingsWindow = new SettingsWindow(
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
            ],
            [
                new SettingSwitch("Right-click menu in Explorer", "Convert with Shiki, under Show more options.", ContextMenuRegistrar.IsRegistered, ToggleExplorerMenu),
                new SettingSwitch("Shift + drag opens the wheel", "Hold Shift while dragging any file.", () => _settings.ShiftDragSummon, ToggleShiftDrag),
                new SettingSwitch("Glass follows what's behind", "Glass look only. Off shows popups in screen recordings.", () => _settings.LiveGlass, ToggleLiveGlass),
                new SettingSwitch("Start with Windows", "The cat is there when you sign in.", StartupRegistration.IsEnabled, () => StartupRegistration.Set(!StartupRegistration.IsEnabled(), ExePath)),
            ],
            new AiSetting(
                CatName,
                () => _settings.AiEnabled,
                () => { _settings.AiEnabled = !_settings.AiEnabled; _settings.Save(); ApplyWake(); },
                () => _settings.VoiceEngine,
                engine => { _settings.VoiceEngine = engine; _settings.Save(); },
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
                () => _settings.AiRequests == 0 ? "Nothing spent yet." : $"Spent so far: {AskView.Money(_settings.AiSpentUsd)} over {_settings.AiRequests:N0} requests.",
                () => Process.Start(new ProcessStartInfo(AiLog.FilePath) { UseShellExecute = true })));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        InDashboard(_settingsWindow, isSettings: true);
    }

    /// <summary>Tries a new shortcut from the settings window; puts the old one back if it's taken.</summary>
    bool ChangeHotkey(string text, Func<string> get, Action<string> set)
    {
        var previous = get();
        // Shortcuts can't share keys.
        if (text != previous && (text == _settings.SnipHotkey || text == _settings.ColourHotkey || text == _settings.ConvertHotkey || text == _settings.RecordHotkey || text == _settings.AskHotkey || text == _settings.TalkHotkey)) return false;
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
        // Deepgram's running total, for Settings.
        Deepgram.Sent += seconds => Dispatcher.BeginInvoke(() => { _settings.DeepgramSeconds += seconds; _settings.Save(); });
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
        return snip && colour && convert && record && ask && talk;
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
                MakeQr = () => ShowQr(""),
                Paste = PasteToCat,
                OpenSettings = ShowSettings,
                ExplorerMenuOn = ContextMenuRegistrar.IsRegistered,
                AddExplorerMenu = () => { if (!ContextMenuRegistrar.IsRegistered()) ToggleExplorerMenu(); },
                StartsWithWindows = StartupRegistration.IsEnabled,
                StartWithWindows = () => StartupRegistration.Set(true, ExePath),
                SetCat = SetCat,
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
            _voice = new VoiceInput(Chat, _settings, () => CatName);
            // The conversation is over: put the bubble away if you're not using it.
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
        bool want = _settings.WakeWord && _settings.AiEnabled && _settings.WakeVariants.Count > 0 && ClaudeKey.Exists;
        if (want && _wake == null)
        {
            _wake = new WakeListener(() => CatName, () => _settings.WakeVariants,
                // Hear nothing while the cat is already listening or thinking, while recording, or while being taught.
                () => _recording != null || (_voice?.Active ?? false) || (_chat?.Busy ?? false) || (_teach?.Listening ?? false));
            _wake.Woke += OnWoke;
            // "Hey Sky, …": show the bubble straight away, before the rest is written down.
            _wake.Waking += () => { if (_voice is not { Active: true } && !Chat.Busy) { ShowAskBubble(); _buddy?.Act("purr"); } };
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
        if (_bubble == null) ShowAskBubble();
        if (_bubble == null) return;
        _buddy?.Act("purr");
        if (after.Length == 0) _ = Voice.Listen();
        else _ = Voice.Use(after);
    }

    /// <summary>Meet your cat: name it and say "Hey &lt;name&gt;" three times.</summary>
    void TeachName()
    {
        _teach?.Close();
        _teach = new WakeSetupWindow(CatName, _settings.WakeVariants, _settings.WakeWord, (name, learned, listen) =>
        {
            _settings.CatName = name;
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
        if (_voice is { Active: true })
        {
            _voice.Finish();
            return;
        }
        ShowAskBubble();
        if (_bubble != null && !Chat.Busy) _ = Voice.Listen();
    }

    string CatName => string.IsNullOrWhiteSpace(_settings.CatName) ? Skin.Name : _settings.CatName.Trim();

    CatChat Chat => _chat ??= new CatChat(new CatHands
    {
        Start = StartForCat,
        Reveal = ExplorerReveal.Show,
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
    });

    /// <summary>
    /// The ask shortcut: a bubble above the cat. What's selected is read first, while File Explorer
    /// (or the desktop) is still in front, so "these" means what you were looking at.
    /// </summary>
    void ShowAskBubble(string? say = null)
    {
        _menu?.CloseAnimated();
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
            _bubble = new AskBubble(Chat, Voice, palette, CatName, CatSprites.Head(Skin), OpenFile, () => ShowDashboard(DashPage.Ask));
            _bubble.Closed += (_, _) => _bubble = null;
            double scale = NativeMethods.SystemScale;
            var area = SystemParameters.WorkArea;
            var spot = _buddy is { IsVisible: true } cat
                ? new Point(cat.WheelSpotPx(0).X / scale, cat.WheelSpotPx(0).Y / scale)
                : new Point(area.Right - 260, area.Bottom - 8);
            _bubble.Place(spot);
            _bubble.Show();
            _buddy?.Notice(true);
            _bubble.Closed += (_, _) => _buddy?.Notice(false);
        }
        else _bubble.FocusBox();
        if (!string.IsNullOrWhiteSpace(say)) _ = Chat.Send(say);
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
        Log.Info($"wake-file: heard \"{text}\" -> {(hit ? $"wakes, then \"{after}\"" : "no wake")} in {watch.ElapsedMilliseconds} ms");
    }

    /// <summary>Dev aid for voice without a microphone: transcribe a WAV and use it like speech.</summary>
    async System.Threading.Tasks.Task SayFile(string wav)
    {
        try
        {
            var samples = ReadWav(wav);
            var watch = Stopwatch.StartNew();
            var text = await Speech.Whisper.Transcribe(samples, $"{CatName}. Convert, merge, compress, PDF, JPG, Downloads.", default);
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
            case "pick_colour": StartColourPicker(); return true;
            case "make_qr": ShowQr(text ?? ""); return true;
            case "paste_clipboard": PasteToCat(); return true;
            case "open_history": ShowDashboard(DashPage.History); return true;
            case "open_settings": ShowSettings(); return true;
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
