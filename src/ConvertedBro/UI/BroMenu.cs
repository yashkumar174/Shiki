using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>A row in <see cref="BroMenu"/>. Give <see cref="IsOn"/> to make it a switch.</summary>
public sealed record MenuEntry(string Glyph, string Text, Action OnClick, Func<bool>? IsOn = null);

/// <summary>A row of picture chips, e.g. which cat to show.</summary>
public sealed record MenuChoice(string Text, IReadOnlyList<ChoiceOption> Options, Func<string> Current, Action<string> Select);

public sealed record ChoiceOption(string Id, string Label, ImageSource? Image = null, FontFamily? Font = null);

/// <summary>
/// The menu for the tray icon and a right-click on the cat. Arcade style: solid, square, hard
/// shadow, pixel font. Glass style: frosted, rounded, soft shadow.
/// </summary>
public sealed class BroMenu : Window
{
    const double MenuWidth = 300, Pad = 18;

    readonly Palette _p = Theme.Current;
    readonly bool _pixel = Theme.Current.Pixel;
    readonly double _px = 1 / NativeMethods.SystemScale;
    readonly double _radius;
    readonly Point _anchorPx;
    readonly Grid _host = new();
    readonly Grid _outer = new();
    readonly Image _glassImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    LiveGlass? _live;
    readonly Border _panel;
    readonly TranslateTransform _slide = new();
    readonly DispatcherTimer _outsideClick = new() { Interval = TimeSpan.FromMilliseconds(40) };
    bool _closing, _opensUp;

    public BroMenu(Point anchorPx, string subtitle, IReadOnlyList<object?> entries, ImageSource? avatar)
    {
        _anchorPx = anchorPx;
        _radius = _pixel ? 0 : 18;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Left = -10000;
        Opacity = 0;
        UseLayoutRounding = true;
        if (_pixel) TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);

        var stack = new StackPanel { Margin = new Thickness(_pixel ? 6 : 8) };
        // On clear glass a soft shadow keeps text readable over anything behind it.
        if (!_pixel) stack.Effect = new DropShadowEffect { BlurRadius = 7, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black };
        stack.Children.Add(Header(subtitle, avatar));
        foreach (var entry in entries)
            stack.Children.Add(entry switch
            {
                MenuEntry row => Row(row),
                MenuChoice choice => Choice(choice),
                _ => Separator(),
            });

        _panel = new Border { Width = MenuWidth, CornerRadius = new CornerRadius(_radius), Child = stack };
        _host.Margin = new Thickness(Pad);
        _outer.Children.Add(_glassImage);
        _outer.Children.Add(_host);
        _outer.RenderTransform = _slide;
        Content = _outer;

        // Size is known up front so the glass snapshot can match it exactly.
        _panel.Measure(new Size(MenuWidth, double.PositiveInfinity));
        Width = MenuWidth + Pad * 2;
        Height = Math.Ceiling(_panel.DesiredSize.Height) + Pad * 2;

        SourceInitialized += (_, _) => PlaceAndBuild();
        Loaded += (_, _) => Open();
        Deactivated += (_, _) => CloseAnimated();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
        _outsideClick.Tick += (_, _) =>
        {
            bool pressed = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
            if (pressed && !_panel.IsMouseOver) CloseAnimated();
        };
        Closed += (_, _) => { _outsideClick.Stop(); _live?.Dispose(); };
    }

    void PlaceAndBuild()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var anchor = fromDevice.Transform(_anchorPx);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_anchorPx.X, (int)_anchorPx.Y)).WorkingArea;
        var area = new Rect(fromDevice.Transform(new Point(screen.Left, screen.Top)), fromDevice.Transform(new Point(screen.Right, screen.Bottom)));

        // Open toward the free side of the screen, like Windows does for tray menus.
        _opensUp = anchor.Y + Height - Pad > area.Bottom;
        double left = anchor.X - Pad, top = _opensUp ? anchor.Y - Height + Pad : anchor.Y - Pad;
        Left = Snap(Math.Clamp(left, area.Left - Pad, area.Right - Width + Pad));
        Top = Snap(Math.Clamp(top, area.Top - Pad, area.Bottom - Height + Pad));

        var rect = new Rect(0, 0, MenuWidth, Height - Pad * 2);
        if (_pixel)
        {
            // Hard shadow, offset down-right with no blur, like a sprite's drop shadow.
            _host.Children.Add(new Rectangle
            {
                Fill = Theme.Brush(Color.FromArgb(0x90, 0, 0, 0)),
                RenderTransform = new TranslateTransform(Snap(6), Snap(6)),
            });
            _host.Children.Add(new Rectangle { Fill = Theme.Brush(_p.Surface) });
            _host.Children.Add(new Rectangle { Stroke = Theme.Brush(_p.SurfaceBorder), StrokeThickness = 2 * _px * 1.5, IsHitTestVisible = false });
        }
        else
        {
            // Liquid glass over the whole window, so its soft shadow can fall outside the panel.
            var pixels = Glass.ToPixels(this, new Rect(Left, Top, Width, Height));
            double scale = pixels.Width / Width;
            var shape = GlassShapes.RoundedRect(new Rect(Pad * scale, Pad * scale, rect.Width * scale, rect.Height * scale), _radius * scale);
            var glassStyle = ConvertWheel.GlassStyle(_p, bezel: 22 * scale, refraction: 22 * scale, frost: 9);
            var plan = GlassPlan.Create(GlassShapes.Field(pixels.Width, pixels.Height, shape), pixels.Width, pixels.Height, glassStyle);
            var snapshot = LiveGlass.Enabled ? null : Glass.Capture(pixels);
            if (LiveGlass.Enabled)
            {
                _live = new LiveGlass(this, source => _glassImage.Source = source);
                _live.SetPlan(plan);
                _live.Start();
            }
            else if (snapshot != null)
            {
                _glassImage.Source = plan.Render(snapshot);
            }
            else
            {
                _host.Children.Add(new Border { CornerRadius = new CornerRadius(_radius), Background = Theme.Brush(_p.Surface) });
            }
        }
        _host.Children.Add(_panel);

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW));
    }

    static double Snap(double dip)
    {
        double scale = NativeMethods.SystemScale;
        return Math.Round(dip * scale) / scale;
    }

    void Open()
    {
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        _outsideClick.Start();
        if (_pixel)
        {
            // Pixel UI pops in whole steps instead of gliding.
            var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(120) };
            double dir = _opensUp ? 1 : -1;
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(Snap(8 * dir), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(Snap(4 * dir), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            _slide.BeginAnimation(TranslateTransform.YProperty, frames);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80)));
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_opensUp ? 10 : -10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(_pixel ? 70 : 110));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    // ---- Pieces ----

    UIElement Header(string subtitle, ImageSource? avatar)
    {
        var picture = new Image { Width = Snap(36), Height = Snap(36), Source = avatar, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);

        var name = new TextBlock
        {
            Text = "Converted Bro",
            FontFamily = Theme.TitleFont,
            FontSize = _pixel ? 16 : 14,
            FontWeight = _pixel ? FontWeights.Normal : FontWeights.SemiBold,
            Foreground = Theme.Brush(_p.Text),
        };
        var sub = new TextBlock { Text = subtitle, FontFamily = Theme.TextFont, FontSize = 11.5, Foreground = Theme.Brush(_p.SubtleText), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };
        if (!_pixel) { name.Foreground = Brushes.White; sub.Foreground = Theme.Brush(Color.FromArgb(0xCC, 255, 255, 255)); }

        var row = new DockPanel { Margin = new Thickness(10, 10, 10, 8) };
        DockPanel.SetDock(picture, Dock.Left);
        row.Children.Add(picture);
        row.Children.Add(text);
        return row;
    }

    UIElement Separator() => new Rectangle
    {
        Height = _pixel ? 2 * _px * 1.5 : 1,
        Margin = new Thickness(10, 4, 10, 4),
        Fill = Theme.Brush(_p.SurfaceBorder),
    };

    TextBlock Label(string text) => new()
    {
        Text = text,
        FontFamily = Theme.LabelFont,
        FontSize = _pixel ? 14 : 13,
        Foreground = _pixel ? Theme.Brush(_p.Text) : Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };

    Border HoverRow(double height, out SolidColorBrush hover)
    {
        var brush = new SolidColorBrush(Colors.Transparent);
        hover = brush;
        var row = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(_pixel ? 0 : 10),
            Background = brush,
            Padding = new Thickness(10, 0, 10, 0),
            Cursor = Cursors.Hand,
        };
        var fast = TimeSpan.FromMilliseconds(_pixel ? 1 : 90);
        var hot = _pixel ? _p.RowHover : Color.FromArgb(0x1E, 255, 255, 255);
        row.MouseEnter += (_, _) => brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot, fast));
        row.MouseLeave += (_, _) => brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.Transparent, fast));
        return row;
    }

    UIElement Row(MenuEntry entry)
    {
        var row = HoverRow(36, out _);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock { Text = entry.Glyph, FontFamily = Theme.IconFont, FontSize = 15, Foreground = Theme.Brush(_p.SubtleText), VerticalAlignment = VerticalAlignment.Center };
        var label = Label(entry.Text);
        Grid.SetColumn(label, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(label);

        Switch? toggle = null;
        if (entry.IsOn != null)
        {
            toggle = new Switch(entry.IsOn(), _p, _pixel);
            Grid.SetColumn(toggle.View, 2);
            grid.Children.Add(toggle.View);
        }
        row.Child = grid;

        row.MouseLeftButtonUp += (_, _) =>
        {
            if (_closing) return;
            if (toggle != null)
            {
                entry.OnClick();
                toggle.Set(entry.IsOn!());
            }
            else
            {
                CloseAnimated();
                // Run after the menu is gone, e.g. so a file dialog isn't hidden behind it.
                Dispatcher.BeginInvoke(entry.OnClick, DispatcherPriority.Background);
            }
        };
        return row;
    }

    UIElement Choice(MenuChoice choice)
    {
        var grid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Label(choice.Text);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(chips, 2);
        grid.Children.Add(chips);

        var borders = new List<(ChoiceOption option, Border chip)>();
        void Refresh()
        {
            string current = choice.Current();
            foreach (var (option, chip) in borders)
                chip.BorderBrush = Theme.Brush(option.Id == current ? Theme.Brand : Colors.Transparent);
        }

        foreach (var option in choice.Options)
        {
            UIElement content;
            if (option.Image != null)
            {
                var image = new Image { Source = option.Image, Width = Snap(32), Height = Snap(32) };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                content = image;
            }
            else
            {
                content = new TextBlock
                {
                    Text = option.Label,
                    FontFamily = option.Font ?? Theme.LabelFont,
                    FontSize = 12.5,
                    Foreground = Theme.Brush(_p.Text),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                };
            }

            var chip = new Border
            {
                Height = Snap(40),
                MinWidth = Snap(40),
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(_pixel ? 0 : 8),
                BorderThickness = new Thickness(_pixel ? 3 * _px : 1.5),
                Background = Theme.Brush(_p.RowHover),
                Cursor = Cursors.Hand,
                ToolTip = option.Label,
                Child = content,
            };
            if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
            chip.MouseLeftButtonUp += (_, _) =>
            {
                if (_closing) return;
                choice.Select(option.Id);
                Refresh();
            };
            borders.Add((option, chip));
            chips.Children.Add(chip);
        }
        Refresh();
        return grid;
    }

    /// <summary>An on/off switch: rounded in Glass, square in Arcade.</summary>
    sealed class Switch
    {
        public Border View { get; }
        readonly SolidColorBrush _track, _border, _knob;
        readonly TranslateTransform _knobMove = new();
        readonly Palette _p;
        readonly bool _pixel;

        public Switch(bool on, Palette p, bool pixel)
        {
            _p = p;
            _pixel = pixel;
            _track = new SolidColorBrush(on ? Theme.Brand : Colors.Transparent);
            _border = new SolidColorBrush(on ? Theme.Brand : p.SubtleText);
            _knob = new SolidColorBrush(on ? Colors.White : p.SubtleText);
            FrameworkElement knob = pixel
                ? new Rectangle { Width = 10, Height = 10, Fill = _knob }
                : new Ellipse { Width = 12, Height = 12, Fill = _knob };
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.Margin = new Thickness(pixel ? 4 : 3, 0, 0, 0);
            knob.RenderTransform = _knobMove;
            View = new Border
            {
                Width = 38,
                Height = 20,
                CornerRadius = new CornerRadius(pixel ? 0 : 10),
                Background = _track,
                BorderBrush = _border,
                BorderThickness = new Thickness(pixel ? 2 : 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = knob,
            };
            _knobMove.X = on ? 18 : 0;
        }

        public void Set(bool on)
        {
            var time = TimeSpan.FromMilliseconds(_pixel ? 1 : 160);
            if (_pixel)
            {
                // Two-frame jump, like a toggle in an old game menu.
                var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(80) };
                frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
                frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(on ? 18 : 0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
                _knobMove.BeginAnimation(TranslateTransform.XProperty, frames);
            }
            else
            {
                _knobMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 18 : 0, time) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            _track.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Brand : Colors.Transparent, time));
            _border.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Brand : _p.SubtleText, time));
            _knob.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Colors.White : _p.SubtleText, time));
        }
    }
}
