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
using ConvertedBro.Core;

namespace ConvertedBro.UI.Dashboard;

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
    public required Action MakeQr { get; init; }
    public required Action Paste { get; init; }
    public required Action OpenSettings { get; init; }
    public required Func<bool> ExplorerMenuOn { get; init; }
    public required Action AddExplorerMenu { get; init; }
    public required Func<bool> StartsWithWindows { get; init; }
    public required Action StartWithWindows { get; init; }
    public required Action<string> SetCat { get; init; }
}

public enum DashPage { Home, Tools, History }

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

    public DashboardWindow(DashboardHost host)
    {
        _host = host;
        Title = "Converted Bro";
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
        Closed += (_, _) => { History.Changed -= OnHistoryChanged; Names.Found -= OnHistoryChanged; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _historySearch ??= ""; Show(DashPage.History); e.Handled = true; }
        };
        ApplyTheme();
    }

    void OnHistoryChanged() => Dispatcher.BeginInvoke(() => { if (_page != DashPage.Tools) Rebuild(); });

    bool DarkWanted => _host.Settings.DashboardDark ?? DashPalette.WindowsIsDark();

    void ApplyTheme()
    {
        _p = DarkWanted ? DashPalette.Night : DashPalette.Light;
        _k = new DashKit(_p);
        Background = DashKit.B(_p.Canvas);
        Rebuild();
    }

    public void Show(DashPage page)
    {
        _page = page;
        Rebuild();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) base.Show();
        Activate();
    }

    void Rebuild()
    {
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        _root.RowDefinitions.Clear();
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
        panel.Child = scroll;
        Grid.SetRow(panel, 1);
        Grid.SetColumn(panel, 1);
        _root.Children.Add(panel);
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
        top.Children.Add(Nav("\uE80F", "Home", DashPage.Home));
        top.Children.Add(Nav("\uE90F", "Tools", DashPage.Tools));
        top.Children.Add(Nav("\uE81C", "History", DashPage.History));
        top.Children.Add(NavAction("\uE713", "Settings", _host.OpenSettings));
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
            var name = _k.Text("Converted Bro", 22, weight: FontWeights.SemiBold, font: DashKit.Display);
            name.Margin = new Thickness(10, -1, 0, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
        }
        return row;
    }

    Border Nav(string glyph, string label, DashPage page) =>
        NavItem(glyph, label, _page == page, () => Show(page), null, label);

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
    /// "Set up Converted Bro": the few things that make it feel built in. Each line is checked
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
        int done = steps.Count(x => x.Done);
        if (done == steps.Count) return null;

        var stack = new StackPanel();
        stack.Children.Add(_k.Text("Set up Converted Bro", 14, weight: FontWeights.SemiBold));
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
