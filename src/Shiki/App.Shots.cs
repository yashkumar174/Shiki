using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Shiki.Core;
using Shiki.Core.Ai;
using Shiki.Shell;
using Shiki.UI;

namespace Shiki;

/// <summary>
/// The screenshot pile: after a screenshot the cat offers to keep collecting; each one after joins
/// the pile; Paste all puts them into the app you're in, one after another; on a web page,
/// "Capture whole page" scrolls it and adds it as screen-sized parts.
/// </summary>
public partial class App
{
    ClipboardWatch? _clip;
    ShotTray? _shotTray;
    /// <summary>The screenshot offered but not collected yet.</summary>
    string? _offeredShot;
    /// <summary>The window the last screenshot was taken of (for "whole page").</summary>
    IntPtr _shotWindow;
    CancellationTokenSource? _pageCapture;
    bool _pasting;
    bool _shotsHooked;

    /// <summary>Starts (or stops) watching for screenshots, as the setting says.</summary>
    void ApplyShotPile()
    {
        if (_settings.CollectScreenshots && _clip == null)
        {
            _clip = new ClipboardWatch();
            _clip.PictureCopied += picture => Dispatcher.BeginInvoke(() => OnPictureCopied(picture));
            if (!_shotsHooked)
            {
                _shotsHooked = true;
                ShotStack.Changed += () => Dispatcher.BeginInvoke(RefreshShotTray);
                ShotStack.Tidy();
            }
        }
        else if (!_settings.CollectScreenshots && _clip != null)
        {
            _clip.Dispose();
            _clip = null;
            ShotStack.Clear();
            _shotTray?.Vanish();
        }
    }

    ShotTray Tray() => _shotTray ??= new ShotTray
    {
        Collect = CollectShots,
        WholePage = () => _ = CaptureWholePage(),
        PasteAll = () => _ = PasteAllShots(),
        PasteAsOne = () => _ = PasteShotsAsOne(),
        Done = () => { _offeredShot = null; ShotStack.Clear(); _shotTray?.Vanish(); },
        Remove = ShotStack.Remove,
        StopCapture = () => _pageCapture?.Cancel(),
        SkinId = () => _settings.Cat,
        CatArea = () => _buddy is { IsVisible: true } b ? b.BodyDip() : null,
        CatAway = () => _buddy?.Away ?? new Vector(0, -1),
    };

    string? PageApp => _shotWindow != IntPtr.Zero && PageCapture.IsPage(_shotWindow) ? PageCapture.AppName(_shotWindow) : null;

    void OnPictureCopied(CopiedPicture picture)
    {
        if (_pasting || _pageCapture != null) return;
        if (ShotStack.Collecting)
        {
            _shotWindow = picture.Window;
            ShotStack.Add([ShotStack.Save(picture.Png)]);
            AiLog.Write("shots", $"added a screenshot ({ShotStack.Shots.Count} in the pile)");
            return;
        }
        // Not collecting: only real screenshots are offered (not every picture copied from an app).
        if (!picture.FromScreenshotTool) return;
        _shotWindow = picture.Window;
        _offeredShot = ShotStack.Save(picture.Png);
        Tray().ShowOffer(_offeredShot, PageApp);
    }

    void CollectShots()
    {
        if (_offeredShot == null) return;
        ShotStack.Start([_offeredShot]);
        _offeredShot = null;
        AiLog.Write("shots", "started collecting screenshots");
    }

    void RefreshShotTray()
    {
        if (_pageCapture != null || _shotTray == null) return;
        if (ShotStack.Collecting) _shotTray.ShowPile(ShotStack.Shots, PageApp);
        else if (_offeredShot == null) _shotTray.Vanish();
    }

    /// <summary>Each shot into the app in front, one after another (as if pasted by hand), then the pile back on the clipboard.</summary>
    async Task PasteAllShots()
    {
        if (ShotStack.Shots.Count == 0 || _pasting) return;
        _pasting = true;
        try
        {
            await TypeText.WaitForModifiersUp();
            foreach (var shot in ShotStack.Shots.ToList())
            {
                ShotStack.PutOneOnClipboard(shot);
                await Task.Delay(140);
                TypeText.Paste();
                // Chat apps need a moment to take each picture in before the next.
                await Task.Delay(650);
            }
            ShotStack.PutPileOnClipboard();
            AiLog.Write("shots", $"pasted {ShotStack.Shots.Count} screenshots one by one");
            _shotTray?.ShowPile(ShotStack.Shots, PageApp, $"Pasted {ShotStack.Shots.Count}. Done clears the pile.");
        }
        finally { _pasting = false; }
    }

    async Task PasteShotsAsOne()
    {
        if (ShotStack.Shots.Count == 0 || _pasting) return;
        _pasting = true;
        try
        {
            await TypeText.WaitForModifiersUp();
            ShotStack.PutStackOnClipboard();
            await Task.Delay(140);
            TypeText.Paste();
            await Task.Delay(700);
            ShotStack.PutPileOnClipboard();
            AiLog.Write("shots", $"pasted {ShotStack.Shots.Count} screenshots as one picture");
            _shotTray?.ShowPile(ShotStack.Shots, PageApp, "Pasted as one picture.");
        }
        finally { _pasting = false; }
    }

    async Task CaptureWholePage()
    {
        if (_pageCapture != null) return;
        var window = _shotWindow;
        if (window == IntPtr.Zero) return;
        var tray = Tray();
        _pageCapture = new CancellationTokenSource();
        tray.ShowBusy($"Scrolling {PageCapture.AppName(window)} from the top. Esc stops.");
        try
        {
            var parts = await PageCapture.CaptureAsync(window, text => tray.ShowBusy(text), _pageCapture.Token);
            var paths = new List<string>();
            for (int i = 0; i < parts.Count; i++)
                paths.Add(ShotStack.Save(ShotStack.Png(parts[i]), $"page {i + 1} of {parts.Count}"));
            AiLog.Write("shots", $"captured a whole page in {parts.Count} parts from {PageCapture.AppName(window)}");
            _pageCapture.Dispose();
            _pageCapture = null;
            if (ShotStack.Collecting)
            {
                // The snip of that page is replaced by the whole page.
                if (ShotStack.Shots.Count > 0) ShotStack.Remove(ShotStack.Shots[^1]);
                if (ShotStack.Collecting) ShotStack.Add(paths);
                else ShotStack.Start(paths);
            }
            else
            {
                _offeredShot = null;
                ShotStack.Start(paths);
            }
        }
        catch (OperationCanceledException)
        {
            _pageCapture?.Dispose();
            _pageCapture = null;
            tray.ShowNote("Stopped", "The page wasn't captured.");
        }
        catch (Exception ex)
        {
            Log.Error("Capturing a whole page", ex);
            _pageCapture?.Dispose();
            _pageCapture = null;
            tray.ShowNote("Couldn't capture that page", ex is InvalidOperationException ? ex.Message : "Something went wrong while scrolling it. Try once more with the page in front.");
        }
    }

    /// <summary>Dev verbs for the pile. True when handled.</summary>
    bool HandleShotVerb(string[] args)
    {
        switch (args[0])
        {
            case "shot-test" when args.Length >= 2 && File.Exists(args[1]):
                // Dev aid: a picture arrives as if just snipped (offers to collect, or joins the pile).
                EnterBuddyMode();
                OnPictureCopied(new CopiedPicture(File.ReadAllBytes(args[1]), true, PageCapture.PageWindow(NativeMethods.GetForegroundWindow())));
                return true;
            case "cat-drop" when args.Length == 3 && double.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out var dropX) && double.TryParse(args[2], System.Globalization.CultureInfo.InvariantCulture, out var dropY):
                // Dev aid: the cat let go at this point on screen (DIPs), as if dragged there: it jumps to the nearest edge.
                EnterBuddyMode();
                _buddy?.DropAt(new Point(dropX, dropY));
                return true;
            case "pixels-test" when args.Length >= 2:
                // Dev aid: the pixel size the phone check reads from each file (null for anything that isn't a picture).
                foreach (var file in args[1..]) Log.Info($"pixels-test {Path.GetFileName(file)}: {(Core.Phone.PhoneLink.Pixels(file) is { } px ? $"{px.W}x{px.H}" : "not a picture")}");
                return true;
            case "firewall-test":
                // Dev aid: whether Windows Firewall lets the phone in (reads the rules, changes nothing).
                _ = Task.Run(() => Log.Info($"firewall-test: {Firewall.Look()}"));
                return true;
            case "shot-collect":
                // Dev aid: presses "Collect more" on the offer.
                CollectShots();
                return true;
            case "shot-done":
                ShotStack.Clear();
                _offeredShot = null;
                _shotTray?.Vanish();
                return true;
            case "snapshot-shots" when args.Length == 2:
                // Dev aid: the panel as it is now, to a PNG.
                if (_shotTray != null)
                {
                    var card = _shotTray.Card;
                    UI.Dashboard.DashboardWindow.SaveElement(card, ShotTray.CardWidth + 40, args[1]);
                    _shotTray.Content = card; // drawing it took it out of the panel; put it back
                }
                return true;
            case "pages-test" when args.Length >= 3:
                // Dev aid: a tall picture "scrolled" screen by screen (with a sticky bar on top), put back together and cut into parts.
                PagesTest(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 900, args.Length > 4 ? int.Parse(args[4]) : 700);
                return true;
        }
        return false;
    }

    static void PagesTest(string tallPath, string outDir, int screen, int step)
    {
        try
        {
            var tall = ShotStack.Load(tallPath)!;
            const int Bar = 70; // a sticky header: the first rows of the picture stay on every screen
            var bar = new CroppedBitmap(tall, new Int32Rect(0, 0, tall.PixelWidth, Bar));
            var screens = new List<BitmapSource>();
            for (int top = Bar; ; top += step)
            {
                int y = Math.Min(top, tall.PixelHeight - (screen - Bar));
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(new CroppedBitmap(tall, new Int32Rect(0, y, tall.PixelWidth, screen - Bar)), new Rect(0, Bar, tall.PixelWidth, screen - Bar));
                    dc.DrawImage(bar, new Rect(0, 0, tall.PixelWidth, Bar));
                }
                var frame = new RenderTargetBitmap(tall.PixelWidth, screen, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                frame.Render(visual);
                frame.Freeze();
                screens.Add(frame);
                if (y + screen - Bar >= tall.PixelHeight) { screens.Add(frame); break; }
            }
            var notes = new List<string>();
            var parts = PageCapture.PartsOf(screens, notes.Add);
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < parts.Count; i++) File.WriteAllBytes(Path.Combine(outDir, $"part-{i + 1}.png"), ShotStack.Png(parts[i]));
            Log.Info($"pages-test: {screens.Count} screens of {tall.PixelWidth}x{screen} from a {tall.PixelHeight}-tall picture -> {parts.Count} parts ({string.Join(", ", parts.Select(p => p.PixelHeight))}); {string.Join("; ", notes)}");
        }
        catch (Exception ex) { Log.Error("pages-test", ex); }
    }
}
