using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;
using Path = System.Windows.Shapes.Path;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using IOPath = System.IO.Path;

namespace ConvertedBro.UI;

/// <summary>
/// The frosted wheel of formats. Two ways in:
/// <list type="bullet">
/// <item>Click mode: files are known (dropped on the bro, Explorer "More options…"); click a wedge.</item>
/// <item>Drag mode: opened under the cursor by Shift + drag; drop the file straight onto a wedge.</item>
/// </list>
/// Formats and tools live on two pages; the hub switches between them.
/// </summary>
public sealed class ConvertWheel : Window
{
    public enum WheelMode { Click, Drag }

    const double Size = 440, C = Size / 2;
    const double DiscRadius = 162, OuterRadius = 151, InnerRadius = 80, HubRadius = 63;
    const double Gap = 6, Corner = 12;
    // Liquid glass pieces float apart, so they get wider gaps and softer corners.
    const double LiquidGap = 11, LiquidCorner = 18;
    static readonly TimeSpan HoverTime = TimeSpan.FromMilliseconds(130);

    readonly WheelMode _mode;
    readonly Point _centerPx;
    readonly Palette _p = Theme.Current;
    readonly bool _pixel = Theme.Current.Pixel;
    /// <summary>Arcade art pixel: 4 physical screen pixels, so edges land on real pixels.</summary>
    readonly double _cell = 4 / NativeMethods.SystemScale;
    readonly Grid _root = new() { Width = Size, Height = Size };
    readonly ScaleTransform _rootScale = new(0.86, 0.86, C, C);
    readonly Grid _glassHost = new() { IsHitTestVisible = false };
    readonly Canvas _wedgeLayer = new() { Width = Size, Height = Size };
    readonly Ellipse _hub = new() { Width = HubRadius * 2, Height = HubRadius * 2 };
    readonly SolidColorBrush _hubFill;
    readonly TextBlock _hubTitle = new(), _hubSub = new();
    readonly List<Rectangle> _pageDots = [];
    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly DispatcherTimer _dwell = new() { Interval = TimeSpan.FromMilliseconds(520) };
    readonly DispatcherTimer _noFiles = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    readonly DispatcherTimer _outsideClick = new() { Interval = TimeSpan.FromMilliseconds(40) };

    IReadOnlyList<string> _files;
    BitmapSource? _snapshot;
    LiveGlass? _live;
    Int32Rect _glassPixels;
    readonly Image _glass = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    int _glassVersion;
    // Pages in order: formats, tools, size limits (only the ones that have something to offer).
    List<(ActionGroup Group, List<BroAction> Actions)> _pages = [];
    int _pageIndex;
    readonly List<Wedge> _wedges = [];
    Color _accent = Theme.Brand;
    int _hover = -1;
    bool _hubHot, _leftHubOnce, _chosen, _closing, _switching;

    public event Action<BroAction, IReadOnlyList<string>>? ActionChosen;

    public ConvertWheel(WheelMode mode, Point centerPx, IReadOnlyList<string>? files = null)
    {
        _mode = mode;
        _centerPx = centerPx;
        _files = files ?? [];
        _hubFill = new SolidColorBrush(_pixel ? _p.WedgeRest : Colors.Transparent);

        Title = "Converted Bro";
        Width = Height = Size;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Almost invisible, so the whole square receives mouse and drag events.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = mode == WheelMode.Click;
        AllowDrop = mode == WheelMode.Drag;
        Opacity = 0;
        Left = -10000;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        if (_pixel) TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);

        BuildChrome();
        Content = _root;
        SetFiles(_files);

        SourceInitialized += (_, _) => { PlaceAndCaptureGlass(); HideFromAltTab(); };
        Loaded += (_, _) => Open();

        MouseMove += (_, e) => Track(e.GetPosition(_root), dragging: false);
        MouseLeave += (_, _) => Track(new Point(-1, -1), dragging: false);
        MouseLeftButtonUp += (_, e) => OnClick(e.GetPosition(_root));
        MouseWheel += (_, _) => SwitchPage();
        KeyDown += OnKeyDown;
        Deactivated += (_, _) => { if (_mode == WheelMode.Click) CloseAnimated(); };

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => { if (!IsInside(CursorInRoot())) Track(new Point(-1, -1), dragging: true); };
        Drop += OnDrop;

        _dwell.Tick += (_, _) => { _dwell.Stop(); SwitchPage(); };
        _noFiles.Tick += (_, _) => { _noFiles.Stop(); if (_files.Count == 0) CloseAnimated(); };
        _outsideClick.Tick += (_, _) => CloseOnOutsideClick();
        Closed += (_, _) => { _dwell.Stop(); _noFiles.Stop(); _outsideClick.Stop(); _live?.Dispose(); };
    }

    // ---- Construction ----

    void BuildChrome()
    {
        _root.RenderTransform = _rootScale;
        if (_pixel)
        {
            BuildPixelChrome();
            return;
        }

        // Soft, wide shadow so the glass floats above the desktop.
        _root.Children.Add(new Ellipse
        {
            Width = DiscRadius * 2 + 56,
            Height = DiscRadius * 2 + 56,
            Margin = new Thickness(0, 14, 0, 0),
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush(
            [
                new GradientStop(Color.FromArgb(_p.Dark ? (byte)0x1C : (byte)0x10, 0, 0, 0), 0.72),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
            ]),
        });

        _glassHost.Children.Add(_glass);
        _root.Children.Add(_glassHost);
        _root.Children.Add(_wedgeLayer);

        // The hub is a glass piece of its own; this only carries its hover tint.
        _hub.Fill = _hubFill;
        _hub.IsHitTestVisible = false;
        _root.Children.Add(_hub);
        BuildHubText();
    }

    /// <summary>Arcade: a solid pixel disc with a hard drop shadow, bevelled like an old game button.</summary>
    void BuildPixelChrome()
    {
        var center = new Point(C, C);
        var disc = PixelShape.Create(Size, _cell, PixelShape.Disc(center, DiscRadius));
        _root.Children.Add(new Path { Data = disc.Body, Fill = Theme.Brush(Color.FromArgb(0x80, 0, 0, 0)), RenderTransform = new TranslateTransform(_cell * 2, _cell * 2), IsHitTestVisible = false });
        _root.Children.Add(new Path { Data = disc.Body, Fill = Theme.Brush(_p.GlassTint), IsHitTestVisible = false });
        _root.Children.Add(new Path { Data = disc.Outline, Fill = Theme.Brush(_p.GlassRim), IsHitTestVisible = false });
        _root.Children.Add(_wedgeLayer);

        var hub = PixelShape.Create(Size, _cell, PixelShape.Disc(center, HubRadius));
        _root.Children.Add(new Path { Data = hub.Body, Fill = _hubFill, IsHitTestVisible = false });
        _root.Children.Add(new Path { Data = hub.Light, Fill = Theme.Brush(Color.FromArgb(0x30, 255, 255, 255)), IsHitTestVisible = false });
        _root.Children.Add(new Path { Data = hub.Dark, Fill = Theme.Brush(Color.FromArgb(0x55, 0, 0, 0)), IsHitTestVisible = false });
        BuildHubText();
    }

    void BuildHubText()
    {
        foreach (var (tb, size, font, weight, brush) in new[]
        {
            (_hubTitle, _pixel ? 15.0 : 14.5, Theme.TitleFont, _pixel ? FontWeights.Normal : FontWeights.SemiBold, _p.Label),
            (_hubSub, 11.5, Theme.TextFont, FontWeights.Normal, _p.SubLabel),
        })
        {
            tb.FontSize = size;
            tb.FontFamily = font;
            tb.FontWeight = weight;
            tb.Foreground = Theme.Brush(brush);
            tb.TextAlignment = TextAlignment.Center;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
        }
        _hubTitle.MaxWidth = HubRadius * 2 - 22;
        _hubSub.MaxWidth = HubRadius * 2 - 18;
        _hubSub.TextWrapping = TextWrapping.Wrap;
        _hubSub.MaxHeight = 32;
        _hubSub.Margin = new Thickness(0, 2, 0, 0);

        _root.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Children = { _hubTitle, _hubSub, _dots },
            Effect = _pixel ? null : new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
        });
    }

    void SetFiles(IReadOnlyList<string> files)
    {
        _files = files;
        var actions = ActionCatalog.For(files);
        _pages = Enum.GetValues<ActionGroup>()
            .Select(g => (g, actions.Where(a => a.Group == g).ToList()))
            .Where(page => page.Item2.Count > 0)
            .ToList();
        _pageIndex = 0;
        _accent = Theme.Accent(ActionCatalog.DominantKind(files));

        _dots.Children.Clear();
        _pageDots.Clear();
        foreach (var _ in _pages)
        {
            var dot = new Rectangle
            {
                Width = _pixel ? _cell * 2 : 5,
                Height = _pixel ? _cell * 2 : 5,
                RadiusX = _pixel ? 0 : 2.5,
                RadiusY = _pixel ? 0 : 2.5,
                Margin = new Thickness(2.5, 7, 2.5, 0),
            };
            _pageDots.Add(dot);
            _dots.Children.Add(dot);
        }
        BuildWedges(animate: IsLoaded);
    }

    List<BroAction> PageActions => _pages.Count == 0 ? [] : _pages[_pageIndex].Actions;
    bool HasTwoPages => _pages.Count > 1;

    static string PageName(ActionGroup group) => group switch
    {
        ActionGroup.Format => "Formats",
        ActionGroup.Tool => "Tools",
        _ => "Size limit",
    };

    void BuildWedges(bool animate, double fromAngle = -12)
    {
        _wedgeLayer.Children.Clear();
        _wedges.Clear();
        _hover = -1;

        var actions = PageActions;
        if (actions.Count == 0)
        {
            // Empty glass ring: waiting for a drag to tell us what's coming, or nothing to offer.
            var ring = new Path
            {
                Data = _pixel
                    ? PixelShape.Create(Size, _cell, PixelShape.Wedge(new Point(C, C), InnerRadius, OuterRadius, 0, Math.PI * 2, Gap, _cell)).Body
                    : WedgeGeometry.Create(new Point(C, C), InnerRadius, OuterRadius, 0, Math.PI * 2, Gap, Corner),
                Fill = _pixel ? Theme.Brush(_p.WedgeRest) : Brushes.Transparent,
                Stroke = null,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            _wedgeLayer.Children.Add(ring);
        }
        else
        {
            double step = Math.PI * 2 / actions.Count;
            for (int i = 0; i < actions.Count; i++)
            {
                double start = -Math.PI / 2 - step / 2 + i * step;
                var wedge = new Wedge(actions[i], start, start + step, _p, _accent, _pixel ? _cell : 0);
                _wedges.Add(wedge);
                _wedgeLayer.Children.Add(wedge.Container);
                if (animate) wedge.AnimateIn(i * 14, fromAngle);
            }
        }
        if (!_pixel && (_snapshot != null || _live != null)) RenderGlass();
        UpdateHub();
    }

    // ---- Opening, placing, closing ----

    void PlaceAndCaptureGlass()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var center = fromDevice.Transform(_centerPx);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_centerPx.X, (int)_centerPx.Y)).WorkingArea;
        var area = new Rect(fromDevice.Transform(new Point(screen.Left, screen.Top)), fromDevice.Transform(new Point(screen.Right, screen.Bottom)));

        // Keep the disc itself on screen; the transparent margin may hang over the edge.
        double margin = C - DiscRadius - 4;
        Left = Snap(Math.Clamp(center.X - C, area.Left - margin, area.Right - Size + margin));
        Top = Snap(Math.Clamp(center.Y - C, area.Top - margin, area.Bottom - Size + margin));
        if (_pixel) return;

        _glassPixels = Glass.ToPixels(this, new Rect(Left, Top, Size, Size));
        if (LiveGlass.Enabled)
        {
            // Live: keep re-capturing what's behind us while we're open.
            _live = new LiveGlass(this, source => _glass.Source = source);
            _live.Start();
            RenderGlass();
            return;
        }
        // Snapshot what's behind us before we become visible; the glass bends this picture.
        _snapshot = Glass.Capture(_glassPixels);
        if (_snapshot == null)
            _glassHost.Children.Add(Glass.Build(null, new EllipseGeometry(new Point(C, C), DiscRadius, DiscRadius), _p));
        else
            RenderGlass();
    }

    /// <summary>
    /// Renders the wedges and hub as liquid glass. Runs off the UI thread; the page's wedge count
    /// decides the shapes, so a page switch renders again.
    /// </summary>
    void RenderGlass()
    {
        var snapshot = _snapshot;
        var live = _live;
        int version = ++_glassVersion;
        int count = Math.Max(1, PageActions.Count);
        int w = _glassPixels.Width, h = _glassPixels.Height;
        double scale = w / Size;
        var style = GlassStyle(_p, bezel: 20 * scale, refraction: 28 * scale, frost: 4);
        var center = new Point(C * scale, C * scale);
        double step = Math.PI * 2 / count;
        var shape = GlassShapes.Union(
            GlassShapes.Wedges(center, InnerRadius * scale, OuterRadius * scale, count, -Math.PI / 2 - step / 2, LiquidGap * scale, LiquidCorner * scale),
            GlassShapes.Circle(center, HubRadius * scale));
        System.Threading.Tasks.Task.Run(() => GlassPlan.Create(GlassShapes.Field(w, h, shape), w, h, style))
            .ContinueWith(t =>
            {
                if (t.IsFaulted) { Log.Error("Liquid glass failed", t.Exception); return; }
                var plan = t.Result;
                // Live glass renders frames itself; a snapshot is rendered once, here.
                var still = live == null && snapshot != null ? plan.Render(snapshot) : null;
                Dispatcher.BeginInvoke(() =>
                {
                    if (version != _glassVersion) return;
                    if (live != null) live.SetPlan(plan);
                    else if (still != null) _glass.Source = still;
                });
            });
    }

    public static LiquidGlassStyle GlassStyle(Palette p, double bezel, double refraction, int frost = 5) => new()
    {
        Bezel = bezel,
        Refraction = refraction,
        Frost = frost,
        // Dark mode darkens the glass a touch so white labels stay readable; light mode brightens it.
        Tint = p.Dark ? Color.FromArgb(0x2C, 0x12, 0x10, 0x1C) : Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
        Specular = 1.0,
        ShadowOffset = bezel * 0.3,
        ShadowBlur = bezel * 0.8,
    };

    /// <summary>Lands on a whole physical pixel, so pixel art and hairlines stay sharp.</summary>
    static double Snap(double dip)
    {
        double scale = NativeMethods.SystemScale;
        return Math.Round(dip * scale) / scale;
    }

    void Open()
    {
        if (_mode == WheelMode.Click)
        {
            NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
            Activate();
            Focus();
            _outsideClick.Start();
            if (PageActions.Count == 0) CloseAfter(TimeSpan.FromSeconds(1.8));
        }
        else if (_files.Count == 0)
        {
            _noFiles.Start();
        }

        if (_pixel)
        {
            // Scaling pixel art blurs it; the arcade wheel snaps open and lets the wedges drop in.
            _rootScale.ScaleX = _rootScale.ScaleY = 1;
        }
        else
        {
            var pop = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut },
            };
            _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
        for (int i = 0; i < _wedges.Count; i++) _wedges[i].AnimateIn(40 + i * 16, -14);
    }

    void CloseAfter(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); CloseAnimated(); };
        timer.Start();
    }

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        if (!_pixel)
        {
            var shrink = new DoubleAnimation(_chosen ? 1.04 : 0.92, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Drag mode: the mouse button was released somewhere.</summary>
    public void OnDragEnded()
    {
        if (_mode != WheelMode.Drag) return;
        // The drop (if it was on us) arrives just after the button-up; give it a moment.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); if (!_chosen) CloseAnimated(); };
        timer.Start();
    }

    // ---- Pointer tracking ----

    bool IsInside(Point p) => Distance(p) <= DiscRadius + 6;

    /// <summary>The real cursor position; drag events don't always report a reliable one on leave.</summary>
    Point CursorInRoot()
    {
        NativeMethods.GetCursorPos(out var px);
        return _root.PointFromScreen(new Point(px.X, px.Y));
    }

    static double Distance(Point p) => Math.Sqrt((p.X - C) * (p.X - C) + (p.Y - C) * (p.Y - C));

    /// <summary>Which wedge is under the point, -1 for none; hub reports true.</summary>
    (int wedge, bool hub) HitTest(Point p)
    {
        double r = Distance(p);
        if (r <= HubRadius) return (-1, true);
        if (_wedges.Count == 0 || r < InnerRadius - 6 || r > OuterRadius + 14) return (-1, false);
        double step = Math.PI * 2 / _wedges.Count;
        double angle = Math.Atan2(p.Y - C, p.X - C) - (-Math.PI / 2 - step / 2);
        angle = (angle % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
        return ((int)(angle / step) % _wedges.Count, false);
    }

    void Track(Point p, bool dragging)
    {
        if (_closing || _switching) return;
        var (wedge, hub) = p.X < 0 ? (-1, false) : HitTest(p);
        SetHover(wedge);

        // Only a real visit to a wedge counts as having left the hub; drag-leave events can be spurious.
        if (wedge >= 0) _leftHubOnce = true;

        if (hub != _hubHot)
        {
            _hubHot = hub;
            var rest = _pixel ? _p.WedgeRest : Colors.Transparent;
            var lit = _pixel ? Theme.Lighten(_p.WedgeRest, 0.08) : Color.FromArgb(0x26, 255, 255, 255);
            var target = hub && HasTwoPages ? lit : rest;
            _hubFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, HoverTime));
            Cursor = hub && HasTwoPages && !dragging ? Cursors.Hand : null;
            UpdateHub();
        }

        // While dragging, resting on the hub flips the page (there's no click mid-drag).
        if (dragging && hub && HasTwoPages && _leftHubOnce)
        {
            if (!_dwell.IsEnabled) _dwell.Start();
        }
        else
        {
            _dwell.Stop();
        }
    }

    void SetHover(int index)
    {
        if (index == _hover) return;
        if (_hover >= 0 && _hover < _wedges.Count) _wedges[_hover].SetHot(false);
        _hover = index;
        if (_hover >= 0) _wedges[_hover].SetHot(true);
        if (_mode == WheelMode.Click) Cursor = _hover >= 0 ? Cursors.Hand : null;
        UpdateHub();
    }

    void UpdateHub()
    {
        // The title stays put (it's the file); the line below says what the hovered wedge will do.
        _hubSub.Foreground = Theme.Brush(_p.SubLabel);
        if (_hover >= 0 && _hover < _wedges.Count)
        {
            SetHubTitleToFiles();
            _hubSub.Text = _wedges[_hover].Action.Hint;
            _hubSub.Foreground = Theme.Brush(_pixel ? Theme.Lighten(_accent, 0.3) : Color.FromArgb(0xEE, 255, 255, 255));
        }
        else if (_files.Count == 0)
        {
            _hubTitle.Text = "Drop here";
            _hubSub.Text = "then pick a format";
        }
        else if (PageActions.Count == 0)
        {
            _hubTitle.Text = "Not supported";
            _hubSub.Text = "Try images, video, audio or PDF";
        }
        else if (_hubHot && HasTwoPages)
        {
            _hubTitle.Text = PageName(_pages[(_pageIndex + 1) % _pages.Count].Group);
            _hubSub.Text = _mode == WheelMode.Drag ? "Hold here to switch" : "Click to switch";
        }
        else
        {
            SetHubTitleToFiles();
            _hubSub.Text = _files.Count == 1 ? FileSummary(_files[0]) : PageName(_pages[_pageIndex].Group);
        }

        _dots.Visibility = HasTwoPages ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _pageDots.Count; i++)
            _pageDots[i].Fill = Theme.Brush(i == _pageIndex ? _accent : Theme.WithAlpha(_p.SubLabel, 0x70));
    }

    void SetHubTitleToFiles() =>
        _hubTitle.Text = _files.Count == 1 ? IOPath.GetFileNameWithoutExtension(_files[0]) : $"{_files.Count} files";

    static string FileSummary(string path)
    {
        var ext = IOPath.GetExtension(path).TrimStart('.').ToUpperInvariant();
        try
        {
            long bytes = new FileInfo(path).Length;
            string size = bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / 1024.0 / 1024:0.#} MB";
            return $"{ext}, {size}";
        }
        catch
        {
            return ext;
        }
    }

    // ---- Paging ----

    void SwitchPage()
    {
        if (!HasTwoPages || _switching || _closing) return;
        _switching = true;
        _dwell.Stop();
        var outgoing = _wedges.ToList();
        foreach (var w in outgoing) w.AnimateOut(14);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _pageIndex = (_pageIndex + 1) % _pages.Count;
            BuildWedges(animate: true, fromAngle: -14);
            _switching = false;
            _leftHubOnce = false; // don't flip straight back while still resting on the hub
            if (Mouse.DirectlyOver != null) Track(Mouse.GetPosition(_root), dragging: false);
        };
        timer.Start();
    }

    // ---- Input ----

    void OnClick(Point p)
    {
        if (_mode != WheelMode.Click || _closing) return;
        var (wedge, hub) = HitTest(p);
        if (wedge >= 0) Choose(_wedges[wedge]);
        else if (hub) SwitchPage();
        else if (!IsInside(p)) CloseAnimated();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseAnimated();
                return;
            case Key.Tab or Key.Left or Key.Right:
                SwitchPage();
                e.Handled = true;
                return;
        }
        int digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1
            : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
        if (digit >= 0 && digit < _wedges.Count) Choose(_wedges[digit]);
    }

    void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        if (_files.Count == 0)
        {
            _noFiles.Stop();
            SetFiles(FileList.Expand(paths));
        }
        OnDragOver(sender, e);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        Track(e.GetPosition(_root), dragging: true);
        // Always "copy": with Shift held Explorer asks to move, which would delete the original.
        e.Effects = _hover >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var (wedge, _) = HitTest(e.GetPosition(_root));
        e.Effects = wedge >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
        if (wedge >= 0)
        {
            var chosen = _wedges[wedge];
            // Return from the drop call first so Explorer isn't kept waiting.
            Dispatcher.BeginInvoke(() => Choose(chosen));
        }
        else
        {
            Dispatcher.BeginInvoke(CloseAnimated);
        }
    }

    void Choose(Wedge wedge)
    {
        if (_chosen || _closing) return;
        _chosen = true;
        wedge.Pulse();
        ActionChosen?.Invoke(wedge.Action, _files);
        CloseAfter(TimeSpan.FromMilliseconds(140));
    }

    void CloseOnOutsideClick()
    {
        bool pressed = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
        if (pressed && !IsInside(CursorInRoot())) CloseAnimated();
    }

    void HideFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64() | NativeMethods.WS_EX_TOOLWINDOW;
        if (_mode == WheelMode.Drag) style |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>One slice of the wheel: its shape, label and hover state.</summary>
    sealed class Wedge
    {
        public BroAction Action { get; }
        public Canvas Container { get; } = new() { Width = Size, Height = Size, IsHitTestVisible = false };

        readonly SolidColorBrush _fill, _label, _glyph;
        readonly TranslateTransform _push = new(), _drop = new();
        readonly RotateTransform _spin = new(0, C, C);
        readonly ScaleTransform _pulse = new(1, 1, C, C);
        readonly double _mid, _cell;
        readonly Color _accent, _labelColor, _glyphColor;
        Color _rest;
        readonly DropShadowEffect? _glow;
        readonly Path? _hardShadow;
        bool Pixel => _cell > 0;

        /// <param name="cell">Arcade pixel size in DIPs; 0 for the smooth glass wedge.</param>
        public Wedge(BroAction action, double start, double end, Palette p, Color accent, double cell)
        {
            Action = action;
            _accent = accent;
            _rest = p.WedgeRest;
            _labelColor = p.Label;
            _glyphColor = p.SubLabel;
            _cell = cell;
            _mid = (start + end) / 2;
            // Liquid glass wedges are clear until lit; the glass itself is rendered underneath.
            if (!Pixel) _rest = Theme.WithAlpha(accent, 0);
            _fill = new SolidColorBrush(_rest);
            _label = new SolidColorBrush(p.Label);
            _glyph = new SolidColorBrush(p.SubLabel);

            if (Pixel)
            {
                var shape = PixelShape.Create(Size, cell, PixelShape.Wedge(new Point(C, C), InnerRadius, OuterRadius, start, end, Gap, cell));
                // A hard, offset shadow in a darker accent sits under the lit wedge, like a raised key.
                var deep = Color.FromRgb((byte)(accent.R * 0.45), (byte)(accent.G * 0.45), (byte)(accent.B * 0.45));
                _hardShadow = new Path { Data = shape.Body, Fill = Theme.Brush(deep), Opacity = 0, RenderTransform = new TranslateTransform(cell, cell) };
                Container.Children.Add(_hardShadow);
                Container.Children.Add(new Path { Data = shape.Body, Fill = _fill });
                Container.Children.Add(new Path { Data = shape.Light, Fill = Theme.Brush(Color.FromArgb(0x30, 255, 255, 255)) });
                Container.Children.Add(new Path { Data = shape.Dark, Fill = Theme.Brush(Color.FromArgb(0x55, 0, 0, 0)) });
            }
            else
            {
                _glow = new DropShadowEffect { Color = accent, BlurRadius = 26, ShadowDepth = 0, Opacity = 0 };
                var geometry = WedgeGeometry.Create(new Point(C, C), InnerRadius, OuterRadius, start, end, LiquidGap, LiquidCorner);
                // Lit, the glass takes on the accent colour (tinted glass), with a soft glow around it.
                Container.Children.Add(new Path { Data = geometry, Fill = _fill, Effect = _glow });
            }

            var content = BuildLabel(action);
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelRadius = (InnerRadius + OuterRadius) / 2;
            double left = C + labelRadius * Math.Cos(_mid) - content.DesiredSize.Width / 2;
            double top = C + labelRadius * Math.Sin(_mid) - content.DesiredSize.Height / 2;
            Canvas.SetLeft(content, Snap(left));
            Canvas.SetTop(content, Snap(top));
            Container.Children.Add(content);

            Container.RenderTransform = new TransformGroup { Children = { _pulse, _spin, _push, _drop } };
        }

        FrameworkElement BuildLabel(BroAction action)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            if (action.Glyph != null)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = action.Glyph,
                    FontFamily = Theme.IconFont,
                    FontSize = 19,
                    Foreground = _glyph,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                });
                stack.Children.Add(new TextBlock
                {
                    Text = action.Short,
                    FontFamily = Pixel ? Theme.PixelFont : Theme.TextFont,
                    FontSize = Pixel ? 12.5 : 11.5,
                    FontWeight = Pixel ? FontWeights.Normal : FontWeights.Medium,
                    Foreground = _label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = action.Short,
                    FontFamily = Pixel ? Theme.PixelFont : Theme.DisplayFont,
                    FontSize = Pixel ? 17 : 15,
                    FontWeight = Pixel ? FontWeights.Normal : FontWeights.SemiBold,
                    Foreground = _label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }
            // Clear glass shows whatever is behind it; a soft shadow keeps labels readable on anything.
            if (!Pixel) stack.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black };
            return stack;
        }

        public void SetHot(bool hot)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var time = Pixel ? TimeSpan.FromMilliseconds(60) : HoverTime;
            var lit = Pixel ? _accent : Theme.WithAlpha(_accent, 0xB0);
            _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot ? lit : _rest, time) { EasingFunction = ease });
            // Arcade labels go dark on the bright key; glass labels go white.
            var labelHot = Pixel ? Color.FromRgb((byte)(_accent.R * 0.18), (byte)(_accent.G * 0.18), (byte)(_accent.B * 0.18)) : Colors.White;
            _label.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot ? labelHot : _labelColor, time));
            _glyph.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot ? labelHot : _glyphColor, time));

            if (Pixel)
            {
                _hardShadow!.Opacity = hot ? 1 : 0;
                // Step outward two whole pixels, never landing in between.
                double push = hot ? 2 : 0;
                _push.BeginAnimation(TranslateTransform.XProperty, Steps(_push.X, Math.Round(push * Math.Cos(_mid)) * _cell));
                _push.BeginAnimation(TranslateTransform.YProperty, Steps(_push.Y, Math.Round(push * Math.Sin(_mid)) * _cell));
                return;
            }

            // The glass under the tint stays put, so the lit wedge glows rather than moves.
            _glow!.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(hot ? 0.75 : 0, HoverTime));
        }

        DoubleAnimationUsingKeyFrames Steps(double from, double to)
        {
            var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(70) };
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(Math.Round((from + to) / 2 / _cell) * _cell, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(30))));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
            return frames;
        }

        public void AnimateIn(int delayMs, double fromAngle)
        {
            var delay = TimeSpan.FromMilliseconds(delayMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Container.Opacity = 0;
            Container.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Pixel ? 120 : 200)) { BeginTime = delay, EasingFunction = ease });
            if (Pixel)
            {
                // Drop in from three pixels up, one pixel per frame.
                var frames = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = TimeSpan.FromMilliseconds(150) };
                for (int i = 0; i <= 3; i++)
                    frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(-(3 - i) * _cell, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 50))));
                _drop.BeginAnimation(TranslateTransform.YProperty, frames);
                return;
            }
            _spin.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(fromAngle, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = delay, EasingFunction = ease });
        }

        public void AnimateOut(double toAngle)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            Container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
            if (!Pixel)
                _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(toAngle, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
        }

        public void Pulse()
        {
            if (Pixel)
            {
                // Arcade press: the key sinks onto its shadow.
                _push.BeginAnimation(TranslateTransform.XProperty, Steps(_push.X, _cell));
                _push.BeginAnimation(TranslateTransform.YProperty, Steps(_push.Y, _cell));
                _hardShadow!.Opacity = 0;
                return;
            }
            var bump = new DoubleAnimation(1, 1.035, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
            _pulse.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
            _pulse.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
        }
    }
}
