using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>What the dashboard asks the app to do. The app owns jobs, windows and settings.</summary>
public sealed class DashboardHost
{
    public required Settings Settings { get; init; }
    public required Action SaveSettings { get; init; }
    public required Action PickFiles { get; init; }
    /// <summary>Files dropped on the dashboard: open the wheel for them at the mouse.</summary>
    public required Action<IReadOnlyList<string>> Convert { get; init; }
    public required Action<BroAction, List<string>> Run { get; init; }
    public required Action Snip { get; init; }
    public required Action PickColour { get; init; }
    public required Action Record { get; init; }
    /// <summary>Paw to ask: pick something on screen and ask the cat about it.</summary>
    public Action? Paw { get; init; }
    /// <summary>The cat looks at the window in front and explains its error.</summary>
    public Action? ExplainError { get; init; }
    /// <summary>Meeting notes: running now, and start or stop them.</summary>
    public Func<bool>? NotesRunning { get; init; }
    public Action? ToggleNotes { get; init; }
    /// <summary>Opens the Ask page with this meeting attached, to ask about it.</summary>
    public Action<Core.Meetings.Meeting>? AskAboutMeeting { get; init; }
    /// <summary>Tries writing a meeting's notes again (they failed the first time).</summary>
    public Action<Core.Meetings.Meeting>? RewriteNotes { get; init; }
    /// <summary>Shows one meeting (set by the dashboard itself).</summary>
    public Action<string>? OpenMeeting { get; set; }
    public required Action MakeQr { get; init; }
    public required Action Paste { get; init; }
    public required Action OpenSettings { get; init; }
    public required Func<bool> ExplorerMenuOn { get; init; }
    public required Action AddExplorerMenu { get; init; }
    public required Func<bool> StartsWithWindows { get; init; }
    public required Action StartWithWindows { get; init; }
    public required Action<string> SetCat { get; init; }
    /// <summary>The cat's level and points (it grows with use), and what it wears.</summary>
    public Func<int>? Level { get; init; }
    public Func<int>? Points { get; init; }
    /// <summary>What's been done with Shiki, for the level tasks on the Cats page.</summary>
    public Func<CatStats>? Stats { get; init; }
    /// <summary>Gives a cat its own name (an empty name gives it back its own).</summary>
    public Action<string, string>? RenameCat { get; init; }
    /// <summary>Opens "Connect your phone".</summary>
    public Action? ConnectPhone { get; init; }
    public Action<string?>? SetAccessory { get; init; }
    /// <summary>Talking to the cat (Claude); null hides the Ask page.</summary>
    public Core.Ai.CatChat? Chat { get; init; }
    /// <summary>Talking to the cat; null when there's no chat.</summary>
    public Core.Voice.VoiceInput? Voice { get; init; }
    /// <summary>Opens "Teach your cat its name" (the wake word).</summary>
    public Action? TeachName { get; init; }
    public Func<bool>? NameTaught { get; init; }
    public required Func<string> CatName { get; init; }
    /// <summary>Opens a file the way the app does: videos and music in the player, the rest in their usual app.</summary>
    public required Action<string> OpenFile { get; init; }
    /// <summary>"Try" on the skills page: does what the words say, as if they'd been said.</summary>
    public Action<string>? TrySaying { get; init; }
    /// <summary>"Try" for a shortcut: paw, error, snip, record, colour, notes.</summary>
    public Action<string>? TryTool { get; init; }
    /// <summary>Plays a video or audio file in the dashboard (set by the dashboard itself).</summary>
    public Action<string>? Play { get; set; }
    /// <summary>Shows phone photos and videos one at a time with their details, starting at this one (set by the dashboard itself).</summary>
    public Action<IReadOnlyList<string>, int>? ShowViewer { get; set; }
}

public enum DashPage { Home, Ask, Tools, History, Meetings, Tasks, Skills, Cats, Levels, Phone }

/// <summary>
/// The main window: a folder. Its tab (the cat and Shiki) sits top left and flows down into one
/// big panel; the pages are pills beside the tab, the current one filled in. The cat and the
/// wheel stay the quick way in; this is where everything lives.
/// </summary>
public sealed class DashboardWindow : Window
{
    const double TitleHeight = 72;

    readonly DashboardHost _host;
    DashPalette _p = DashPalette.Light;
    DashKit _k = new(DashPalette.Light);
    DashPage _page = DashPage.Home;
    readonly Grid _root = new();
    string? _historySearch;
    string? _meetingId;
    Border? _maxButton;

    // An editor or settings window living inside the dashboard. It keeps its own logic; only its
    // content moves here. Stepping to another page parks it (the sidebar offers a way back).
    // Two slots: an editor and settings, so peeking at settings never throws away an edit.
    sealed class Hosted(Window window, FrameworkElement frame)
    {
        public Window Window { get; } = window;
        public FrameworkElement Frame { get; } = frame;
    }
    Hosted? _editor, _settingsPage, _showing;

    // The media player: one at a time, parked (paused) when you look at another page.
    PlayerView? _player;
    bool _onPlayer, _fullscreen;

    // The cat chat page, kept between visits (and across rebuilds) so a half-typed message stays.
    AskView? _ask;

    // One phone photo or video, big, with its details (over the Phone page; Esc goes back to it).
    MediaViewer? _viewer;
    double? _galleryOffset;
    string _askDraft = "";
    WindowState _stateBeforeFullscreen;

    public DashboardWindow(DashboardHost host)
    {
        _host = host;
        _host.Play = Play;
        _host.ShowViewer = (paths, index) =>
        {
            _galleryOffset = _pageScroll?.VerticalOffset;
            _viewer = new MediaViewer(_k, _host, paths, index, CloseViewer);
            Rebuild();
        };
        _host.OpenMeeting = id => { _meetingId = id; Show(DashPage.Meetings); };
        Title = "Shiki";
        Width = 1240;
        Height = 820;
        MinWidth = 960;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        Icon = CrispHead(CatSprites.Skin(host.Settings.Cat), 64);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = TitleHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        Content = _root;

        SourceInitialized += (_, _) =>
        {
            // Windows 11: rounded corners like any other app window.
            int round = 2;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int));
        };
        StateChanged += (_, _) =>
        {
            // A maximised custom-chrome window hangs over the screen edge by its border; pull in.
            _root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            if (_maxButton?.Child is TextBlock glyph) glyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
        SizeChanged += (_, _) => FitPills();
        History.Changed += OnHistoryChanged;
        Todos.Changed += OnListsChanged;
        Core.Meetings.MeetingStore.Changed += OnListsChanged;
        Names.Found += OnHistoryChanged;
        Closed += (_, _) =>
        {
            History.Changed -= OnHistoryChanged;
            Todos.Changed -= OnListsChanged;
            Core.Meetings.MeetingStore.Changed -= OnListsChanged;
            Names.Found -= OnHistoryChanged;
            _settingsPage?.Window.Close();
            _editor?.Window.Close();
            _player?.Close();
            _ask?.Detach();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (_onPlayer && _player != null && Keyboard.FocusedElement is not TextBox)
            {
                if (_player.HandleKey(e.Key)) e.Handled = true;
                return;
            }
            if (_showing != null) { Forward(e, Keyboard.PreviewKeyDownEvent); return; }
            if (_viewer != null && !_onPlayer && Keyboard.FocusedElement is not TextBox)
            {
                if (_viewer.HandleKey(e.Key)) e.Handled = true;
                return;
            }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _historySearch ??= ""; Show(DashPage.History); e.Handled = true; }
        };
        KeyDown += (_, e) => { if (_showing != null && !e.Handled) Forward(e, Keyboard.KeyDownEvent); };
        ApplyTheme();
    }

    /// <summary>The phone sent something: the gallery (and Home's phone tile) show it at once.</summary>
    public void PhoneFilesChanged() => Dispatcher.BeginInvoke(() => { if (_page is DashPage.Phone or DashPage.Home && _showing == null && _viewer == null && !_onPlayer && !IsTyping()) RebuildInPlace(); });

    /// <summary>Back from one photo to the gallery, where it was scrolled to.</summary>
    void CloseViewer()
    {
        if (_viewer == null) return;
        _viewer = null;
        _keepOffset = _galleryOffset;
        Rebuild();
    }

    // Tasks and meetings change from elsewhere (a reminder, notes being written): show it.
    void OnListsChanged() => Dispatcher.BeginInvoke(() => { if (_page is DashPage.Tasks or DashPage.Meetings && _showing == null && !_onPlayer && !IsTyping()) Rebuild(); });

    /// <summary>Someone's typing in a box on the page (a task, a speaker's name): don't rebuild under them.</summary>
    static bool IsTyping() => Keyboard.FocusedElement is TextBox;

    // Tools and Ask don't show history; rebuilding Ask while a plan runs would only flicker.
    void OnHistoryChanged() => Dispatcher.BeginInvoke(() => { if (_page is not (DashPage.Tools or DashPage.Ask) || _showing != null || _onPlayer) Rebuild(); });

    bool DarkWanted => _host.Settings.DashboardDark ?? DashPalette.WindowsIsDark();

    void ApplyTheme()
    {
        _p = DarkWanted ? DashPalette.Night : DashPalette.Light;
        Theme.EditorDark = _p.Dark;
        _k = new DashKit(_p);
        if (_ask != null) { _askDraft = _ask.Draft; _ask.Detach(); _ask = null; }
        _viewer = null;
        Background = DashKit.B(_p.Canvas);
        Rebuild();
    }

    public void Show(DashPage page)
    {
        _page = page;
        // Settings has nothing to lose: leaving it closes it. Editors are parked instead.
        _settingsPage?.Window.Close();
        _showing = null;
        _viewer = null;
        ParkPlayer();
        Rebuild();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) base.Show();
        Activate();
        if (page == DashPage.Ask) _ask?.FocusBox();
    }

    AskView Ask() => _ask ??= new AskView(_k, _host, _host.Chat!, ThinScrollBar()) { Draft = _askDraft };

    // Where the page was scrolled to, kept across one redraw (picking a cat shouldn't jump to the top).
    ScrollViewer? _pageScroll;
    double? _keepOffset;

    /// <summary>Redraws the page (and the sidebar) where it is, after something on it changed.</summary>
    void RebuildInPlace()
    {
        _keepOffset = _pageScroll?.VerticalOffset;
        Rebuild();
    }

    void Rebuild()
    {
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        _root.RowDefinitions.Clear();
        if (_fullscreen && _onPlayer && _player != null)
        {
            // Full screen: just the player, edge to edge.
            if (_player.Root.Parent is Decorator holder) holder.Child = null;
            _root.Children.Add(new Border { Background = DashKit.B(_p.Panel), Child = _player.Root });
            return;
        }
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleHeight) });
        _root.RowDefinitions.Add(new RowDefinition());

        // The folder: one shape, a tab on the top left that flows down into the panel.
        var tab = TabContent();
        tab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tabWidth = Math.Ceiling(tab.DesiredSize.Width) + 2 * TabPadding;
        var folder = new System.Windows.Shapes.Path { Fill = DashKit.B(_p.Panel), Stroke = DashKit.B(_p.Line), StrokeThickness = 1, IsHitTestVisible = false };
        var frame = new Grid { Margin = new Thickness(Edge, FrameTop, Edge, Edge) };
        frame.SizeChanged += (_, e) => folder.Data = Folder(e.NewSize.Width, e.NewSize.Height, tabWidth, TitleHeight - FrameTop);
        frame.Children.Add(folder);
        Grid.SetRowSpan(frame, 2);
        _root.Children.Add(frame);

        tab.Margin = new Thickness(Edge + TabPadding, FrameTop, 0, 0);
        tab.HorizontalAlignment = HorizontalAlignment.Left;
        tab.VerticalAlignment = VerticalAlignment.Center;
        _root.Children.Add(tab);

        // The pages, as pills beside the tab; the window's own buttons on the right.
        var right = TitleRight();
        right.Margin = new Thickness(0, FrameTop, 10, 0);
        _root.Children.Add(right);
        right.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = Edge + tabWidth + Shoulder + 14;
        double room = (ActualWidth > 0 ? ActualWidth : Width) - left - right.DesiredSize.Width - 24;
        var pills = Pills(compact: false);
        pills.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _navFull = pills.DesiredSize.Width;
        _navRoom = room;
        _navFixed = left + right.DesiredSize.Width + 24;
        if (_navFull > room) pills = Pills(compact: true);
        pills.Margin = new Thickness(left, FrameTop, 0, 0);
        _root.Children.Add(pills);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = Page(),
        };
        scroll.Resources.Add(typeof(ScrollBar), ThinScrollBar());
        // Pages open at the top (or where they were, after a redraw), even if something inside asks to be shown.
        _pageScroll = scroll;
        scroll.Loaded += (_, _) =>
        {
            if (_keepOffset is { } offset) scroll.ScrollToVerticalOffset(offset);
            else scroll.ScrollToTop();
            _keepOffset = null;
        };
        scroll.RequestBringIntoView += (_, e) => { if (!scroll.IsMouseOver) e.Handled = true; };
        var panel = new Border { Margin = new Thickness(Edge + 1, 0, Edge + 1, Edge + 1) };
        if (_onPlayer && _player != null)
        {
            if (_player.Root.Parent is Decorator old) old.Child = null;
            panel.Child = _player.Root;
        }
        else if (_showing != null)
        {
            if (_showing.Frame.Parent is Decorator old) old.Child = null;
            panel.Child = _showing.Frame;
        }
        else if (_viewer != null)
        {
            if (_viewer.Root.Parent is Decorator old) old.Child = null;
            panel.Child = _viewer.Root;
        }
        else if (_page == DashPage.Ask && _host.Chat != null)
        {
            var view = Ask();
            // Settings may have changed meanwhile (the name taught, the wake word switched on).
            view.Refresh();
            var ask = view.Root;
            if (ask.Parent is Decorator old) old.Child = null;
            panel.Child = ask;
        }
        else panel.Child = scroll;
        // The content's corners follow the panel's: square under the tab, round elsewhere.
        panel.SizeChanged += (_, _) =>
        {
            if (panel.Child is FrameworkElement inner)
                inner.Clip = Corners(Math.Max(0, panel.ActualWidth), Math.Max(0, panel.ActualHeight), 0, Radius - 1, Radius - 1, Radius - 1);
        };
        Grid.SetRow(panel, 1);
        _root.Children.Add(panel);
    }

    // The folder's measurements: its distance from the window edge, where the tab starts, the
    // tab's padding, the curve from the tab down to the panel, and the corners.
    const double Edge = 14, FrameTop = 12, TabPadding = 24, Shoulder = 30, Radius = 22;
    double _navFull, _navRoom, _navFixed;

    /// <summary>The window got wider or narrower: the pills keep or drop their labels to fit.</summary>
    void FitPills()
    {
        if (_navFixed == 0 || _root.RowDefinitions.Count == 0) return;
        double room = ActualWidth - _navFixed;
        if ((_navFull > room) != (_navFull > _navRoom)) RebuildInPlace();
    }

    /// <summary>The folder's outline: a tab top left, an S-curve down to the panel's top edge, round corners.</summary>
    static Geometry Folder(double w, double h, double tab, double top)
    {
        const double r = Radius, s = Shoulder;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, r), true, true);
            c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(tab - s, 0), true, true);
            c.BezierTo(new Point(tab, 0), new Point(tab, top), new Point(tab + s, top), true, true);
            c.LineTo(new Point(w - r, top), true, true);
            c.ArcTo(new Point(w, top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(w, h - r), true, true);
            c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(r, h), true, true);
            c.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A rectangle with its own radius at each corner (top left, top right, bottom right, bottom left).</summary>
    static Geometry Corners(double w, double h, double tl, double tr, double br, double bl)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(tl, 0), true, true);
            c.LineTo(new Point(w - tr, 0), true, true);
            if (tr > 0) c.ArcTo(new Point(w, tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(w, h - br), true, true);
            if (br > 0) c.ArcTo(new Point(w - br, h), new Size(br, br), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(bl, h), true, true);
            if (bl > 0) c.ArcTo(new Point(0, h - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(0, tl), true, true);
            if (tl > 0) c.ArcTo(new Point(tl, 0), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    // ---- The media player ----

    /// <summary>Dev aid: which meeting the Meetings page shows, without showing the window.</summary>
    public void SelectMeeting(string? id) => _meetingId = id;

    /// <summary>Shows one meeting's notes.</summary>
    public void OpenMeetingPage(string id)
    {
        _meetingId = id;
        Show(DashPage.Meetings);
    }

    /// <summary>Opens a video or audio file in the dashboard's player.</summary>
    public void Play(string path)
    {
        if (_player?.Path != path)
        {
            _player?.Close();
            _player = new PlayerView(_k, _host, path, ClosePlayer, ToggleFullscreen);
        }
        else _player.Play();
        _onPlayer = true;
        Rebuild();
        Bring();
    }

    void ClosePlayer()
    {
        if (_fullscreen) ToggleFullscreen();
        _player?.Close();
        _player = null;
        _onPlayer = false;
        Rebuild();
    }

    /// <summary>Looking at another page pauses the player; it waits in the sidebar.</summary>
    void ParkPlayer()
    {
        if (!_onPlayer) return;
        _player?.Pause();
        _onPlayer = false;
        if (_fullscreen) ToggleFullscreen();
    }

    void ToggleFullscreen()
    {
        if (_player == null) return;
        _fullscreen = !_fullscreen;
        _player.Fullscreen = _fullscreen;
        var chrome = WindowChrome.GetWindowChrome(this);
        if (_fullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            chrome.CaptionHeight = 0;
            WindowState = WindowState.Maximized;
        }
        else
        {
            chrome.CaptionHeight = TitleHeight;
            WindowState = _stateBeforeFullscreen;
        }
        Rebuild();
    }

    // ---- Editors and settings inside the dashboard ----

    /// <summary>
    /// Moves a window's content into the dashboard. The window itself is never shown: it keeps
    /// running its own code, gets its keys forwarded, and when it closes itself (Save, Cancel,
    /// Done) the dashboard goes back to where it was.
    /// </summary>
    public void ShowWindow(Window window, bool isSettings = false)
    {
        ParkPlayer();
        var existing = isSettings ? _settingsPage : _editor;
        if (existing?.Window == window)
        {
            _showing = existing;
            Rebuild();
            Bring();
            return;
        }
        // A new editor replaces the old one (settings only replaces settings).
        existing?.Window.Close();
        if (window.Content is not UIElement content) return;
        window.Content = null;
        var hosted = new Hosted(window, Frame(window, content, isSettings));
        if (isSettings) _settingsPage = hosted; else _editor = hosted;
        _showing = hosted;
        window.Closed += (_, _) =>
        {
            bool wasShowing = _showing == hosted;
            if (_editor == hosted) _editor = null;
            if (_settingsPage == hosted) _settingsPage = null;
            // Closing settings goes back to the editor, if one is open; anything else to the page.
            if (wasShowing) _showing = isSettings ? _editor : null;
            Rebuild();
        };
        // The window's own Loaded handlers (focus a box, measure the stage) run once its content shows.
        if (content is FrameworkElement element)
        {
            void Once(object? sender, RoutedEventArgs e)
            {
                element.Loaded -= Once;
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, window));
            }
            element.Loaded += Once;
        }
        Rebuild();
        Bring();
    }

    /// <summary>Dev aid: a page drawn off screen at the window's size, without showing or focusing anything.</summary>
    public void Snapshot(string path, DashPage page)
    {
        if (IsVisible) { Snapshot(path); return; }
        _page = page;
        Rebuild();
        Content = null;
        var holder = new Border { Background = Background, Child = _root };
        SaveElement(holder, Width, path, Height);
        holder.Child = null;
        Content = _root;
    }

    /// <summary>Dev aid: the photo viewer on this file (with the rest of its folder to step through), for "snapshot phone".</summary>
    public void PeekViewer(string path, int steps = 0)
    {
        var folder = System.IO.Path.GetDirectoryName(path)!;
        var paths = new System.IO.DirectoryInfo(folder).EnumerateFiles()
            .Where(f => PhonePage.IsVideo(f.FullName) || f.Extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".heic" or ".heif")
            .OrderByDescending(f => f.CreationTime).Select(f => f.FullName).ToList();
        _page = DashPage.Phone;
        _viewer = new MediaViewer(_k, _host, paths, Math.Max(0, paths.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))), CloseViewer);
        for (int i = 0; i < steps; i++) _viewer.HandleKey(Key.Right);
        if (IsVisible) Rebuild();
    }

    /// <summary>Dev aid: a page's whole length (not just what fits the window), as a PNG.</summary>
    public void SnapshotWhole(string path, DashPage page)
    {
        var shown = _page;
        _page = page;
        var holder = new Border { Background = DashKit.B(_k.P.Card), Child = Page() };
        SaveElement(holder, 1000, path);
        _page = shown;
    }

    /// <summary>Dev aid: lays out an element that isn't on screen and saves it as a PNG.</summary>
    public static void SaveElement(FrameworkElement element, double width, string path, double? height = null)
    {
        if (element.Parent is Window owner) owner.Content = null;
        element.Measure(new Size(width, height ?? double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, width, height ?? element.DesiredSize.Height));
        element.UpdateLayout();
        // Lists scroll to the end once laid out; let that happen before drawing.
        element.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        element.Measure(new Size(width, height ?? double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, width, height ?? element.DesiredSize.Height));
        element.UpdateLayout();
        double scale = Shell.NativeMethods.SystemScale;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(width * scale), (int)((height ?? element.DesiredSize.Height) * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Dev aid: the window as it looks right now, even when it's behind others.</summary>
    public void Snapshot(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)(ActualWidth * dpi.DpiScaleX), h = (int)(ActualHeight * dpi.DpiScaleY);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var fill = new DrawingVisual();
        using (var dc = fill.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        bitmap.Render(fill);
        bitmap.Render(_root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    void Bring()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) base.Show();
        // Opened from the wheel or Explorer: come to the front even though another app has focus.
        Shell.NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
    }

    FrameworkElement Frame(Window window, UIElement content, bool isSettings)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        if (!isSettings)
        {
            var head = new DockPanel { Margin = new Thickness(18, 14, 22, 4) };
            var back = _k.IconButton("\uE72B", () => window.Close(), "Back (Esc)", 14, _p.Muted);
            DockPanel.SetDock(back, Dock.Left);
            head.Children.Add(back);
            var title = _k.Text(window.Title, 15, weight: FontWeights.SemiBold);
            title.Margin = new Thickness(8, 0, 0, 1);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            grid.Children.Add(head);
        }
        // Dialog-sized windows (settings, QR, passwords) sit at a readable width; editors fill.
        bool dialog = window.SizeToContent != SizeToContent.Manual || window.ResizeMode == ResizeMode.NoResize;
        UIElement body = content;
        if (dialog)
        {
            var holder = new Border { Child = content, MaxWidth = Math.Max(window.Width, 480), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(isSettings ? 26 : 14, isSettings ? 22 : 0, 26, 26) };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = holder };
            scroll.Resources.Add(typeof(ScrollBar), ThinScrollBar());
            body = scroll;
        }
        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>Hands a key to the hosted window's own handlers, as if it had focus itself.</summary>
    void Forward(KeyEventArgs e, RoutedEvent routed)
    {
        var source = e.InputSource ?? PresentationSource.FromVisual(this);
        if (source == null) return;
        var copy = new KeyEventArgs(e.KeyboardDevice, source, e.Timestamp, e.Key == Key.System ? e.SystemKey : e.Key) { RoutedEvent = routed };
        _showing!.Window.RaiseEvent(copy);
        if (copy.Handled) e.Handled = true;
    }

    UIElement Page() => _page switch
    {
        DashPage.Tools => new ToolsPage(_k, _host).Build(),
        DashPage.History => new HistoryPage(_k, _host, _historySearch, s => _historySearch = s, Rebuild).Build(),
        DashPage.Meetings => new MeetingsPage(_k, _host, _meetingId, id => { _meetingId = id; Rebuild(); }, Rebuild).Build(),
        DashPage.Tasks => new TasksPage(_k, _host, Rebuild).Build(),
        DashPage.Skills => new SkillsPage(_k, _host).Build(),
        DashPage.Cats => new CatsPage(_k, _host, RebuildInPlace, () => Show(DashPage.Levels)).Build(),
        DashPage.Levels => new LevelsPage(_k, _host).Build(),
        DashPage.Phone => new PhonePage(_k, _host).Build(),
        _ => new HomePage(_k, _host, () => Show(DashPage.History), () => { _historySearch = ""; Show(DashPage.History); }, RebuildInPlace,
            SetupCard, () => Show(DashPage.Cats), Show).Build(),
    };

    /// <summary>The cat's head scaled up with hard pixel edges, for the window and taskbar icon.</summary>
    static ImageSource CrispHead(CatSkin skin, int size)
    {
        var image = new Image { Source = CatSprites.Head(skin), Width = size, Height = size };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        image.Measure(new Size(size, size));
        image.Arrange(new Rect(0, 0, size, size));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(image);
        bitmap.Freeze();
        return bitmap;
    }

    Style ThinScrollBar() => ThinScrollBar(_p);

    /// <summary>A slim rounded scrollbar in the palette's colours, instead of the grey system one.</summary>
    public static Style ThinScrollBar(DashPalette p, string margin = "0,18,3,18")
    {
        var c = DashKit.A(p.Text, p.Dark ? (byte)0x40 : (byte)0x30);
        var colour = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        var xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Setter Property="Width" Value="12"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="Transparent" Margin="MARGIN">
                      <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.Thumb>
                          <Thumb>
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border Background="COLOUR" CornerRadius="3" Width="6" HorizontalAlignment="Right"/>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                      </Track>
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """.Replace("COLOUR", colour).Replace("MARGIN", margin);
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    // ---- The tab and the pills ----

    /// <summary>The tab's label: the cat's face and Shiki in pixel type. Clicking it opens the cats.</summary>
    FrameworkElement TabContent()
    {
        var head = new Image { Source = CatSprites.Head(CatSprites.Dressed(CatSprites.Skin(_host.Settings.Cat), _host.Settings.Accessory)), Width = 32, Height = 32 };
        RenderOptions.SetBitmapScalingMode(head, BitmapScalingMode.NearestNeighbor);
        var name = _k.Text("Shiki", 27, font: DashKit.Pixel);
        name.Margin = new Thickness(11, -3, 0, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Your cats", Children = { head, name } };
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Show(DashPage.Cats); };
        WindowChrome.SetIsHitTestVisibleInChrome(row, true);
        return row;
    }

    /// <summary>
    /// Every page as a pill, the one you're on filled in. Short of room, only the current one
    /// keeps its label (the rest show their icon, and their name on hover).
    /// </summary>
    readonly List<Border> _pills = [];

    StackPanel Pills(bool compact)
    {
        _pills.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (_player is { } player)
        {
            var playing = System.IO.Path.GetFileNameWithoutExtension(player.Path);
            if (playing.Length > 18) playing = playing[..17] + "\u2026";
            row.Children.Add(Pill("\uE768", playing, _onPlayer, () => { _showing = null; Play(player.Path); }, compact, player.Path));
        }
        if (_editor is { } editor)
        {
            // The open editor, one click away after looking at another page.
            var name = editor.Window.Title.Split(':')[0].Trim();
            if (name.Length > 18) name = name[..17] + "\u2026";
            row.Children.Add(Pill("\uE70F", name, _showing == editor && !_onPlayer, () => { ParkPlayer(); _showing = editor; Rebuild(); }, compact, editor.Window.Title));
        }
        row.Children.Add(PagePill("\uE80F", "Home", DashPage.Home, compact));
        if (_host.Chat != null) row.Children.Add(PagePill("\uE8BD", $"Ask {_host.CatName()}", DashPage.Ask, compact));
        row.Children.Add(PagePill("\uE90F", "Tools", DashPage.Tools, compact));
        row.Children.Add(PagePill("\uE81C", "History", DashPage.History, compact));
        row.Children.Add(PagePill("\uE8EA", "Phone", DashPage.Phone, compact));
        if (_host.ToggleNotes != null) row.Children.Add(PagePill("\uE8F2", "Meetings", DashPage.Meetings, compact));
        if (_host.Chat != null) row.Children.Add(PagePill("\uE897", $"What {_host.CatName()} can do", DashPage.Skills, compact));
        int due = Todos.All.Count(t => !t.Done && t.Due is { } d && d.Date <= DateTime.Today);
        row.Children.Add(PagePill("\uE762", due > 0 ? $"Tasks ({due})" : "Tasks", DashPage.Tasks, compact));
        row.Children.Add(Pill(null, "Cats", OnPage(DashPage.Cats), () => Show(DashPage.Cats), compact, "Cats",
            CatSprites.Head(CatSprites.Skin(_host.Settings.Cat))));
        row.Children.Add(PagePill("\uE7C1", "Levels", DashPage.Levels, compact));
        row.Children.Add(Pill("\uE713", "Settings", !_onPlayer && _showing != null && _showing == _settingsPage, _host.OpenSettings, compact, "Settings"));
        return row;
    }

    bool OnPage(DashPage page) => _page == page && _showing == null && !_onPlayer;

    Border PagePill(string glyph, string label, DashPage page, bool compact) => Pill(glyph, label, OnPage(page), () => Show(page), compact, label);

    /// <summary>
    /// A page's pill. Under the mouse it swells a little with a soft spring and, if it's only an
    /// icon, slides open to show its name (the pills beside it glide aside); pressed, it dips.
    /// With Windows' animations off, it changes at once.
    /// </summary>
    Border Pill(string? glyph, string label, bool selected, Action onClick, bool compact, string tooltip, ImageSource? picture = null)
    {
        var fill = selected ? _p.Accent : _p.Card;
        var ink = selected ? _p.AccentText : _p.Text;
        var background = new SolidColorBrush(fill);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (picture != null)
        {
            var image = new Image { Source = picture, Width = 20, Height = 20 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            row.Children.Add(image);
        }
        else row.Children.Add(_k.Icon(glyph!, 15, selected ? ink : _p.Muted));

        // The name sits in a window that opens and closes: full width when shown, none when not.
        var text = _k.Text(label, 14, ink, selected ? FontWeights.Bold : FontWeights.Medium);
        text.Margin = new Thickness(8, 0, 5, 1);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double open = Math.Ceiling(text.DesiredSize.Width);
        bool labelled = !compact || selected;
        var reveal = new Border { Child = text, ClipToBounds = true, Width = labelled ? open : 0, Opacity = labelled ? 1 : 0, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(reveal);

        var scale = new ScaleTransform(1, 1);
        var pill = new Border
        {
            Background = background,
            BorderBrush = DashKit.B(selected ? _p.Accent : _p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Height = 40,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 7, 0),
            Cursor = Cursors.Hand,
            Child = row,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };

        bool motion = SystemParameters.ClientAreaAnimation;
        var smooth = new CubicEase { EasingMode = EasingMode.EaseOut };
        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        Duration For(int ms) => new(TimeSpan.FromMilliseconds(motion ? ms : 0));
        void Grow(double to, int ms, IEasingFunction ease)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, For(ms)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, For(ms)) { EasingFunction = ease });
        }
        void Reveal(bool shown)
        {
            if (labelled) return;
            reveal.BeginAnimation(WidthProperty, new DoubleAnimation(shown ? open : 0, For(shown ? 260 : 200)) { EasingFunction = smooth });
            reveal.BeginAnimation(OpacityProperty, new DoubleAnimation(shown ? 1 : 0, For(shown ? 220 : 120)) { EasingFunction = smooth });
        }
        pill.MouseEnter += (_, _) =>
        {
            // Drawn over its neighbours while it's bigger.
            Panel.SetZIndex(pill, 1);
            if (!selected) background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(_p.Hover, For(140)));
            Grow(1.08, 240, spring);
            Reveal(true);
        };
        pill.MouseLeave += (_, _) =>
        {
            Panel.SetZIndex(pill, 0);
            if (!selected) background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fill, For(180)));
            Grow(1, 220, smooth);
            Reveal(false);
        };
        pill.PreviewMouseLeftButtonDown += (_, _) => Grow(0.95, 90, smooth);
        pill.PreviewMouseLeftButtonUp += (_, _) => Grow(1.08, 180, spring);
        pill.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        WindowChrome.SetIsHitTestVisibleInChrome(pill, true);
        _pills.Add(pill);
        return pill;
    }

    // ---- Dev aids for recording the ad: they play the motion without the mouse or keyboard ----

    /// <summary>Each pill swells and opens in turn, as if the mouse passed along the bar.</summary>
    public async void DemoPills()
    {
        await System.Threading.Tasks.Task.Delay(800);
        foreach (var pill in _pills.ToList())
        {
            pill.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            await System.Threading.Tasks.Task.Delay(520);
            pill.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            await System.Threading.Tasks.Task.Delay(60);
        }
    }

    /// <summary>The page glides from top to bottom over the given seconds (eased), then back up.</summary>
    public async void DemoScroll(double seconds)
    {
        await System.Threading.Tasks.Task.Delay(700);
        if (_pageScroll is not { } scroll) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double end = scroll.ScrollableHeight;
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            double t = watch.Elapsed.TotalSeconds / seconds;
            double eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            scroll.ScrollToVerticalOffset(end * eased);
            await System.Threading.Tasks.Task.Delay(8);
        }
        scroll.ScrollToVerticalOffset(end);
    }

    StackPanel TitleRight()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var theme = _k.IconButton(_p.Dark ? "\uE706" : "\uE708", ToggleTheme, _p.Dark ? "Light mode" : "Dark mode", 15, _p.Muted);
        theme.Margin = new Thickness(0, 0, 14, 0);
        var min = _k.IconButton("\uE921", () => WindowState = WindowState.Minimized, "Minimise", 10, _p.Muted, 40);
        var max = _k.IconButton(WindowState == WindowState.Maximized ? "\uE923" : "\uE922",
            () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, "Maximise", 10, _p.Muted, 40);
        var close = _k.IconButton("\uE8BB", Close, "Close", 10, _p.Muted, 40, Color.FromRgb(0xE8, 0x5A, 0x3C));
        foreach (var b in new[] { theme, min, max, close })
        {
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            row.Children.Add(b);
        }
        _maxButton = max;
        return row;
    }

    void ToggleTheme()
    {
        _host.Settings.DashboardDark = !_p.Dark;
        _host.SaveSettings();
        ApplyTheme();
    }

    /// <summary>
    /// "Set up Shiki": the few things that make it feel built in. Each line is checked
    /// against the real state and does the thing when clicked; the card goes away when all are done.
    /// </summary>
    internal UIElement? SetupCard()
    {
        var s = _host.Settings;
        var steps = new List<(string Label, bool Done, Action Do)>
        {
            ("Add to Explorer's right-click menu", _host.ExplorerMenuOn(), () => { _host.AddExplorerMenu(); Rebuild(); }),
            ($"Try {s.ConvertHotkey} on a file", s.UsedConvertHotkey, () =>
                ToastHost.Show(ToastWindow.Message($"Try {s.ConvertHotkey}", $"Click a file in File Explorer or on the desktop, then press {s.ConvertHotkey}. The wheel opens right at your mouse."))),
            ("Choose where results go", s.ChoseOutputFolder, () => { s.ChoseOutputFolder = true; _host.SaveSettings(); _host.OpenSettings(); Rebuild(); }),
            ("Start with Windows", _host.StartsWithWindows(), () => { _host.StartWithWindows(); Rebuild(); }),
        };
        // Once the cat can talk (a Claude key is saved): teach it its name for "Hey <name>".
        if (_host.TeachName != null && _host.NameTaught != null && Core.Ai.ClaudeKey.Exists)
            steps.Add(($"Teach {_host.CatName()} its name", _host.NameTaught(), _host.TeachName));
        int done = steps.Count(x => x.Done);
        if (done == steps.Count) return null;

        var stack = new StackPanel();
        stack.Children.Add(_k.Text("Set up Shiki", 14, weight: FontWeights.SemiBold));
        var track = new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 14) };
        track.Children.Add(new Border { Background = DashKit.B(_p.Hover), CornerRadius = new CornerRadius(2) });
        var bar = new Border
        {
            Background = DashKit.B(_p.Accent),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        track.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * done / steps.Count;
        track.Children.Add(bar);
        stack.Children.Add(track);
        foreach (var (label, isDone, action) in steps)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), Cursor = isDone ? Cursors.Arrow : Cursors.Hand, Background = Brushes.Transparent };
            var mark = _k.Icon(isDone ? "\uEC61" : "\uEA3A", 17, isDone ? _p.AccentEdge : _p.Faint);
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 1, 10, 0);
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            var text = _k.Text(label, 13, isDone ? _p.Faint : _p.Text, wrap: true);
            if (isDone) text.TextDecorations = TextDecorations.Strikethrough;
            row.Children.Add(text);
            if (!isDone) row.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
            stack.Children.Add(row);
        }
        var card = _k.Card(stack, new Thickness(24, 20, 24, 18), 18);
        card.Margin = new Thickness(0, 0, 0, 18);
        return card;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
