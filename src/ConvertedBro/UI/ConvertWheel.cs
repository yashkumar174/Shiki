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
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;
using Path = System.Windows.Shapes.Path;
using Ellipse = System.Windows.Shapes.Ellipse;
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
    static readonly TimeSpan HoverTime = TimeSpan.FromMilliseconds(130);

    readonly WheelMode _mode;
    readonly Point _centerPx;
    readonly Palette _p = Theme.Current;
    readonly Grid _root = new() { Width = Size, Height = Size };
    readonly ScaleTransform _rootScale = new(0.86, 0.86, C, C);
    readonly Grid _glassHost = new() { IsHitTestVisible = false };
    readonly Canvas _wedgeLayer = new() { Width = Size, Height = Size };
    readonly Ellipse _hub = new() { Width = HubRadius * 2, Height = HubRadius * 2 };
    readonly SolidColorBrush _hubFill;
    readonly TextBlock _hubTitle = new(), _hubSub = new();
    readonly Ellipse _dotFormats = new(), _dotTools = new();
    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly DispatcherTimer _dwell = new() { Interval = TimeSpan.FromMilliseconds(520) };
    readonly DispatcherTimer _noFiles = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    readonly DispatcherTimer _outsideClick = new() { Interval = TimeSpan.FromMilliseconds(40) };

    IReadOnlyList<string> _files;
    List<BroAction> _formats = [], _tools = [];
    ActionGroup _page = ActionGroup.Format;
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
        _hubFill = new SolidColorBrush(_p.WedgeRest);

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
        Closed += (_, _) => { _dwell.Stop(); _noFiles.Stop(); _outsideClick.Stop(); };
    }

    // ---- Construction ----

    void BuildChrome()
    {
        _root.RenderTransform = _rootScale;

        // Soft, wide shadow so the glass floats above the desktop.
        _root.Children.Add(new Ellipse
        {
            Width = DiscRadius * 2 + 56,
            Height = DiscRadius * 2 + 56,
            Margin = new Thickness(0, 14, 0, 0),
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush(
            [
                new GradientStop(Color.FromArgb(_p.Dark ? (byte)0x70 : (byte)0x38, 0, 0, 0), 0.78),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
            ]),
        });

        _root.Children.Add(_glassHost);

        _root.Children.Add(new Ellipse
        {
            Width = DiscRadius * 2,
            Height = DiscRadius * 2,
            Stroke = Theme.Brush(_p.GlassRim),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });

        _root.Children.Add(_wedgeLayer);

        _hub.Fill = _hubFill;
        _hub.Stroke = Theme.Brush(_p.WedgeEdge);
        _hub.StrokeThickness = 1;
        _hub.IsHitTestVisible = false;
        _root.Children.Add(_hub);

        foreach (var (tb, size, font, weight, brush) in new[]
        {
            (_hubTitle, 14.5, Theme.DisplayFont, FontWeights.SemiBold, _p.Label),
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

        foreach (var dot in new[] { _dotFormats, _dotTools })
        {
            dot.Width = dot.Height = 5;
            dot.Margin = new Thickness(2.5, 7, 2.5, 0);
            _dots.Children.Add(dot);
        }

        _root.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Children = { _hubTitle, _hubSub, _dots },
        });
    }

    void SetFiles(IReadOnlyList<string> files)
    {
        _files = files;
        var actions = ActionCatalog.For(files);
        _formats = actions.Where(a => a.Group == ActionGroup.Format).ToList();
        _tools = actions.Where(a => a.Group == ActionGroup.Tool).ToList();
        _accent = Theme.Accent(ActionCatalog.DominantKind(files));
        _page = _formats.Count > 0 ? ActionGroup.Format : ActionGroup.Tool;
        BuildWedges(animate: IsLoaded);
    }

    List<BroAction> PageActions => _page == ActionGroup.Format ? _formats : _tools;
    bool HasTwoPages => _formats.Count > 0 && _tools.Count > 0;

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
                Data = WedgeGeometry.Create(new Point(C, C), InnerRadius, OuterRadius, 0, Math.PI * 2, Gap, Corner),
                Fill = Theme.Brush(_p.WedgeRest),
                Stroke = Theme.Brush(_p.WedgeEdge),
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
                var wedge = new Wedge(actions[i], i, start, start + step, _p, _accent);
                _wedges.Add(wedge);
                _wedgeLayer.Children.Add(wedge.Container);
                if (animate) wedge.AnimateIn(i * 14, fromAngle);
            }
        }
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
        Left = Math.Clamp(center.X - C, area.Left - margin, area.Right - Size + margin);
        Top = Math.Clamp(center.Y - C, area.Top - margin, area.Bottom - Size + margin);

        // Snapshot what's behind us before we become visible.
        var snapshot = Glass.Capture(Glass.ToPixels(this, new Rect(Left, Top, Size, Size)));
        _glassHost.Children.Add(Glass.Build(snapshot, new EllipseGeometry(new Point(C, C), DiscRadius, DiscRadius), _p));
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

        var pop = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut },
        };
        _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
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
        var shrink = new DoubleAnimation(_chosen ? 1.04 : 0.92, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
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
            var target = hub && HasTwoPages ? Theme.Lighten(_p.WedgeRest, _p.Dark ? 0.08 : 0.3) : _p.WedgeRest;
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
            _hubSub.Foreground = Theme.Brush(Theme.Lighten(_accent, _p.Dark ? 0.3 : 0));
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
            _hubTitle.Text = _page == ActionGroup.Format ? "Tools" : "Formats";
            _hubSub.Text = _mode == WheelMode.Drag ? "Hold here to switch" : "Click to switch";
        }
        else
        {
            SetHubTitleToFiles();
            _hubSub.Text = _files.Count == 1 ? FileSummary(_files[0]) : (_page == ActionGroup.Format ? "Convert to…" : "Tools");
        }

        _dots.Visibility = HasTwoPages ? Visibility.Visible : Visibility.Collapsed;
        _dotFormats.Fill = Theme.Brush(_page == ActionGroup.Format ? _accent : Theme.WithAlpha(_p.SubLabel, 0x70));
        _dotTools.Fill = Theme.Brush(_page == ActionGroup.Tool ? _accent : Theme.WithAlpha(_p.SubLabel, 0x70));
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
            _page = _page == ActionGroup.Format ? ActionGroup.Tool : ActionGroup.Format;
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

        readonly Path _shape;
        readonly SolidColorBrush _fill, _label, _glyph;
        readonly TranslateTransform _push = new();
        readonly RotateTransform _spin = new(0, C, C);
        readonly ScaleTransform _pulse = new(1, 1, C, C);
        readonly double _mid;
        readonly Color _accent, _rest, _labelColor;
        readonly DropShadowEffect _glow;

        public Wedge(BroAction action, int index, double start, double end, Palette p, Color accent)
        {
            Action = action;
            _accent = accent;
            _rest = p.WedgeRest;
            _labelColor = p.Label;
            _mid = (start + end) / 2;
            _fill = new SolidColorBrush(p.WedgeRest);
            _label = new SolidColorBrush(p.Label);
            _glyph = new SolidColorBrush(p.SubLabel);
            _glow = new DropShadowEffect { Color = accent, BlurRadius = 22, ShadowDepth = 0, Opacity = 0 };

            var geometry = WedgeGeometry.Create(new Point(C, C), InnerRadius, OuterRadius, start, end, Gap, Corner);
            _shape = new Path { Data = geometry, Fill = _fill, Stroke = Theme.Brush(p.WedgeEdge), StrokeThickness = 1, Effect = _glow };
            Container.Children.Add(_shape);

            // A faint sheen from the top keeps each slice reading as glass, also when lit up.
            Container.Children.Add(new Path
            {
                Data = geometry,
                Fill = new LinearGradientBrush(Color.FromArgb(p.Dark ? (byte)0x14 : (byte)0x40, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), 90),
            });

            var content = BuildLabel(action);
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelRadius = (InnerRadius + OuterRadius) / 2;
            Canvas.SetLeft(content, C + labelRadius * Math.Cos(_mid) - content.DesiredSize.Width / 2);
            Canvas.SetTop(content, C + labelRadius * Math.Sin(_mid) - content.DesiredSize.Height / 2);
            Container.Children.Add(content);

            Container.RenderTransform = new TransformGroup { Children = { _pulse, _spin, _push } };
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
                    FontFamily = Theme.TextFont,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = _label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = action.Short,
                    FontFamily = Theme.DisplayFont,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }
            return stack;
        }

        public void SetHot(bool hot)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot ? _accent : _rest, HoverTime) { EasingFunction = ease });
            _label.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot ? Colors.White : _labelColor, HoverTime));
            _glyph.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot ? Colors.White : Theme.Current.SubLabel, HoverTime));
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(hot ? 0.6 : 0, HoverTime));
            double push = hot ? 5 : 0;
            _push.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(push * Math.Cos(_mid), HoverTime) { EasingFunction = ease });
            _push.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(push * Math.Sin(_mid), HoverTime) { EasingFunction = ease });
        }

        public void AnimateIn(int delayMs, double fromAngle)
        {
            var delay = TimeSpan.FromMilliseconds(delayMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Container.Opacity = 0;
            Container.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = delay, EasingFunction = ease });
            _spin.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(fromAngle, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = delay, EasingFunction = ease });
        }

        public void AnimateOut(double toAngle)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            Container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
            _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(toAngle, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
        }

        public void Pulse()
        {
            var bump = new DoubleAnimation(1, 1.035, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
            _pulse.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
            _pulse.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
        }
    }
}
