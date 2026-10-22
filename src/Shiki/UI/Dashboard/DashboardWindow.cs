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
    public required Action MakeQr { get; init; }
    public required Action Paste { get; init; }
    public required Action OpenSettings { get; init; }
    public required Func<bool> ExplorerMenuOn { get; init; }
    public required Action AddExplorerMenu { get; init; }
    public required Func<bool> StartsWithWindows { get; init; }
    public required Action StartWithWindows { get; init; }
    public required Action<string> SetCat { get; init; }
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
    /// <summary>Plays a video or audio file in the dashboard (set by the dashboard itself).</summary>
    public Action<string>? Play { get; set; }
}

public enum DashPage { Home, Ask, Tools, History }

/// <summary>
/// The main window: a sidebar on the left, one big rounded panel on the right, and a title bar
/// drawn into the window itself, the way Wispr Flow does it. The cat and the wheel stay the
/// quick way in; this is where everything lives.
/// </summary>
public sealed class DashboardWindow : Window
{
    const double SidebarWidth = 252, SidebarCollapsed = 72, TitleHeight = 52;

    readonly DashboardHost _host;
    DashPalette _p = DashPalette.Light;
    DashKit _k = new(DashPalette.Light);
    DashPage _page = DashPage.Home;
    bool _collapsed;
    readonly Grid _root = new();
    string? _historySearch;
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
    string _askDraft = "";
    WindowState _stateBeforeFullscreen;

    public DashboardWindow(DashboardHost host)
    {
        _host = host;
        _host.Play = Play;
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
        History.Changed += OnHistoryChanged;
        Names.Found += OnHistoryChanged;
        Closed += (_, _) =>
        {
            History.Changed -= OnHistoryChanged;
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
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _historySearch ??= ""; Show(DashPage.History); e.Handled = true; }
        };
        KeyDown += (_, e) => { if (_showing != null && !e.Handled) Forward(e, Keyboard.KeyDownEvent); };
        ApplyTheme();
    }

    // Tools and Ask don't show history; rebuilding Ask while a plan runs would only flicker.
    void OnHistoryChanged() => Dispatcher.BeginInvoke(() => { if (_page is not (DashPage.Tools or DashPage.Ask) || _showing != null || _onPlayer) Rebuild(); });

    bool DarkWanted => _host.Settings.DashboardDark ?? DashPalette.WindowsIsDark();

    void ApplyTheme()
    {
        _p = DarkWanted ? DashPalette.Night : DashPalette.Light;
        Theme.EditorDark = _p.Dark;
        _k = new DashKit(_p);
        if (_ask != null) { _askDraft = _ask.Draft; _ask.Detach(); _ask = null; }
        Background = DashKit.B(_p.Canvas);
        Rebuild();
    }

    public void Show(DashPage page)
    {
        _page = page;
        // Settings has nothing to lose: leaving it closes it. Editors are parked instead.
        _settingsPage?.Window.Close();
        _showing = null;
        ParkPlayer();
        Rebuild();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) base.Show();
        Activate();
        if (page == DashPage.Ask) _ask?.FocusBox();
    }

    AskView Ask() => _ask ??= new AskView(_k, _host, _host.Chat!, ThinScrollBar()) { Draft = _askDraft };

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
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_collapsed ? SidebarCollapsed : SidebarWidth) });
        _root.ColumnDefinitions.Add(new ColumnDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleHeight) });
        _root.RowDefinitions.Add(new RowDefinition());

        _root.Children.Add(TitleLeft());
        var right = TitleRight();
        Grid.SetColumn(right, 1);
        _root.Children.Add(right);

        var sidebar = Sidebar();
        Grid.SetRow(sidebar, 1);
        _root.Children.Add(sidebar);

        var panel = new Border
        {
            Background = DashKit.B(_p.Panel),
            BorderBrush = DashKit.B(_p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Margin = new Thickness(0, 0, 14, 14),
            ClipToBounds = true,
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = Page(),
        };
        scroll.Resources.Add(typeof(ScrollBar), ThinScrollBar());
        // Pages open at the top, even if something inside takes focus and asks to be shown.
        scroll.Loaded += (_, _) => scroll.ScrollToTop();
        scroll.RequestBringIntoView += (_, e) => { if (!scroll.IsMouseOver) e.Handled = true; };
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
        // Round the content's corners to match the panel (ClipToBounds alone clips square).
        panel.SizeChanged += (_, _) =>
        {
            if (panel.Child is FrameworkElement inner)
                inner.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, panel.ActualWidth - 2), Math.Max(0, panel.ActualHeight - 2)), 23, 23);
        };
        Grid.SetRow(panel, 1);
        Grid.SetColumn(panel, 1);
        _root.Children.Add(panel);
    }

    // ---- The media player ----

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
        _ => new HomePage(_k, _host, () => Show(DashPage.History), () => { _historySearch = ""; Show(DashPage.History); }).Build(),
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

    /// <summary>A slim rounded scrollbar in the palette's colours, instead of the grey system one.</summary>
    Style ThinScrollBar()
    {
        var c = DashKit.A(_p.Text, _p.Dark ? (byte)0x40 : (byte)0x30);
        var colour = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        var xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Setter Property="Width" Value="12"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="Transparent" Margin="0,18,3,18">
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
            """.Replace("COLOUR", colour);
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    // ---- Title bar ----

    UIElement TitleLeft()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var toggle = _k.IconButton("\uE700", () => { _collapsed = !_collapsed; Rebuild(); }, _collapsed ? "Show the sidebar" : "Hide the sidebar", 15, _p.Muted);
        WindowChrome.SetIsHitTestVisibleInChrome(toggle, true);
        row.Children.Add(toggle);
        return row;
    }

    UIElement TitleRight()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var theme = _k.IconButton(_p.Dark ? "\uE706" : "\uE708", ToggleTheme, _p.Dark ? "Light mode" : "Dark mode", 15, _p.Muted);
        theme.Margin = new Thickness(0, 0, 18, 0);
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

    // ---- Sidebar ----

    UIElement Sidebar()
    {
        var dock = new DockPanel { Margin = new Thickness(_collapsed ? 12 : 18, 6, _collapsed ? 12 : 18, 18), LastChildFill = false };

        var top = new StackPanel();
        top.Children.Add(Logo());
        if (_player is { } player)
        {
            var playing = System.IO.Path.GetFileNameWithoutExtension(player.Path);
            if (playing.Length > 22) playing = playing[..21] + "\u2026";
            top.Children.Add(NavItem("\uE768", playing, _onPlayer, () => { _showing = null; Play(player.Path); }, null, player.Path));
        }
        if (_editor is { } editor)
        {
            // The open editor, so it's one click away after looking at another page.
            var name = editor.Window.Title.Split(':')[0].Trim();
            if (name.Length > 24) name = name[..23] + "\u2026";
            top.Children.Add(NavItem("\uE70F", name, _showing == editor && !_onPlayer, () => { ParkPlayer(); _showing = editor; Rebuild(); }, null, editor.Window.Title));
        }
        top.Children.Add(Nav("\uE80F", "Home", DashPage.Home));
        if (_host.Chat != null) top.Children.Add(Nav("\uE8BD", $"Ask {_host.CatName()}", DashPage.Ask));
        top.Children.Add(Nav("\uE90F", "Tools", DashPage.Tools));
        top.Children.Add(Nav("\uE81C", "History", DashPage.History));
        top.Children.Add(NavItem("\uE713", "Settings", !_onPlayer && _showing != null && _showing == _settingsPage, _host.OpenSettings, null, "Settings"));
        if (!_collapsed && SetupCard() is { } setup) top.Children.Add(setup);
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        var bottom = new StackPanel();
        bottom.Children.Add(new Border { Height = 1, Background = DashKit.B(_p.Line), Margin = new Thickness(4, 0, 4, 10) });
        var skin = CatSprites.Skin(_host.Settings.Cat);
        bottom.Children.Add(NavItem(null, skin.Name, false, () =>
        {
            // Click the cat to meet the next one.
            var skins = CatSprites.Skins;
            var next = skins[(skins.ToList().FindIndex(s => s.Id == skin.Id) + 1) % skins.Count];
            _host.SetCat(next.Id);
            Rebuild();
        }, CatSprites.Head(skin), "Switch cat"));
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        return dock;
    }

    UIElement Logo()
    {
        var head = new Image { Source = CatSprites.Head(CatSprites.Skin(_host.Settings.Cat)), Width = 30, Height = 30 };
        RenderOptions.SetBitmapScalingMode(head, BitmapScalingMode.NearestNeighbor);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(_collapsed ? 9 : 8, 10, 0, 22) };
        row.Children.Add(head);
        if (!_collapsed)
        {
            var name = _k.Text("Shiki", 22, weight: FontWeights.SemiBold, font: DashKit.Display);
            name.Margin = new Thickness(10, -1, 0, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
        }
        return row;
    }

    Border Nav(string glyph, string label, DashPage page) =>
        NavItem(glyph, label, _page == page && _showing == null && !_onPlayer, () => Show(page), null, label);

    Border NavAction(string glyph, string label, Action action) => NavItem(glyph, label, false, action, null, label);

    Border NavItem(string? glyph, string label, bool selected, Action onClick, ImageSource? picture, string tooltip)
    {
        var rest = selected ? _p.Selected : Colors.Transparent;
        var background = new SolidColorBrush(rest);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (picture != null)
        {
            var image = new Image { Source = picture, Width = 20, Height = 20 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            row.Children.Add(image);
        }
        else row.Children.Add(new Border { Width = 20, Child = _k.Icon(glyph!, 17, _p.Text) });
        if (!_collapsed)
        {
            var text = _k.Text(label, 15.5, weight: selected ? FontWeights.SemiBold : FontWeights.Normal);
            text.Margin = new Thickness(13, 0, 0, 1);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
        }
        var item = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(_collapsed ? 14 : 12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = Cursors.Hand,
            Child = row,
            ToolTip = _collapsed ? tooltip : null,
        };
        item.MouseEnter += (_, _) => { if (!selected) background.Color = _p.Hover; };
        item.MouseLeave += (_, _) => background.Color = rest;
        item.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return item;
    }

    /// <summary>
    /// "Set up Shiki": the few things that make it feel built in. Each line is checked
    /// against the real state and does the thing when clicked; the card goes away when all are done.
    /// </summary>
    UIElement? SetupCard()
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
        track.Children.Add(new Border
        {
            Background = DashKit.B(_p.Accent),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = (SidebarWidth - 36 - 34) * done / steps.Count,
        });
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
        var card = _k.Card(stack, new Thickness(16, 16, 16, 14), 16);
        card.Margin = new Thickness(0, 18, 0, 0);
        return card;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
