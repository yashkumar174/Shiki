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

/// <summary>The bro's own menu (tray icon and right-click on the bro): glass, rounded, with switches.</summary>
public sealed class BroMenu : Window
{
    const double MenuWidth = 292, Pad = 18, Radius = 10;

    readonly Palette _p = Theme.Current;
    readonly Point _anchorPx;
    readonly Grid _host = new();
    readonly Border _panel;
    readonly TranslateTransform _slide = new();
    readonly DispatcherTimer _outsideClick = new() { Interval = TimeSpan.FromMilliseconds(40) };
    bool _closing, _opensUp;

    public BroMenu(Point anchorPx, string subtitle, IReadOnlyList<MenuEntry?> entries)
    {
        _anchorPx = anchorPx;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Left = -10000;
        Opacity = 0;
        UseLayoutRounding = true;

        var stack = new StackPanel { Margin = new Thickness(5) };
        stack.Children.Add(Header(subtitle));
        foreach (var entry in entries)
            stack.Children.Add(entry == null ? Separator() : Row(entry));

        _panel = new Border { Width = MenuWidth, CornerRadius = new CornerRadius(Radius), Child = stack };
        _host.Margin = new Thickness(Pad);
        _host.RenderTransform = _slide;
        Content = _host;

        // Size is known up front so the glass snapshot can match it exactly.
        _panel.Measure(new Size(MenuWidth, double.PositiveInfinity));
        Width = MenuWidth + Pad * 2;
        Height = _panel.DesiredSize.Height + Pad * 2;

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
        Closed += (_, _) => _outsideClick.Stop();
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
        Left = Math.Clamp(left, area.Left - Pad, area.Right - Width + Pad);
        Top = Math.Clamp(top, area.Top - Pad, area.Bottom - Height + Pad);

        var rect = new Rect(0, 0, MenuWidth, _panel.DesiredSize.Height);
        var snapshot = Glass.Capture(Glass.ToPixels(this, new Rect(Left + Pad, Top + Pad, rect.Width, rect.Height)));

        _host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Radius),
            Background = Theme.Brush(_p.Surface),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = _p.Dark ? 0.5 : 0.22, Direction = 270 },
        });
        _host.Children.Add(Glass.Build(snapshot, new RectangleGeometry(rect, Radius, Radius), _p, blurRadius: 40));
        _host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderBrush = Theme.Brush(_p.SurfaceBorder),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        });
        _host.Children.Add(_panel);

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW));
    }

    void Open()
    {
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        _outsideClick.Start();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_opensUp ? 10 : -10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    // ---- Pieces ----

    UIElement Header(string subtitle)
    {
        var avatar = new Grid { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center };
        avatar.Children.Add(new Ellipse
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0xA0, 0x8B, 0xFF), Theme.BrandDeep, new Point(0.2, 0), new Point(0.8, 1)),
        });
        foreach (var x in new[] { 10.0, 18.5 })
        {
            avatar.Children.Add(new Ellipse { Width = 6, Height = 7, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, 11, 0, 0) });
            avatar.Children.Add(new Ellipse { Width = 3, Height = 3.5, Fill = Theme.Brush(Color.FromRgb(0x1E, 0x1B, 0x3A)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x + 2, 13.5, 0, 0) });
        }

        var name = new TextBlock { Text = "Converted Bro", FontFamily = Theme.DisplayFont, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.Brush(_p.Text) };
        var sub = new TextBlock { Text = subtitle, FontFamily = Theme.TextFont, FontSize = 11.5, Foreground = Theme.Brush(_p.SubtleText), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };

        var row = new DockPanel { Margin = new Thickness(10, 10, 10, 8) };
        DockPanel.SetDock(avatar, Dock.Left);
        row.Children.Add(avatar);
        row.Children.Add(text);
        return row;
    }

    UIElement Separator() => new Rectangle
    {
        Height = 1,
        Margin = new Thickness(10, 4, 10, 4),
        Fill = Theme.Brush(_p.SurfaceBorder),
    };

    UIElement Row(MenuEntry entry)
    {
        var hover = new SolidColorBrush(Colors.Transparent);
        var row = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(6),
            Background = hover,
            Padding = new Thickness(10, 0, 10, 0),
            Cursor = Cursors.Hand,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock { Text = entry.Glyph, FontFamily = Theme.IconFont, FontSize = 15, Foreground = Theme.Brush(_p.SubtleText), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = entry.Text, FontFamily = Theme.TextFont, FontSize = 13, Foreground = Theme.Brush(_p.Text), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(label);

        Switch? toggle = null;
        if (entry.IsOn != null)
        {
            toggle = new Switch(entry.IsOn(), _p);
            Grid.SetColumn(toggle.View, 2);
            grid.Children.Add(toggle.View);
        }
        row.Child = grid;

        var fast = TimeSpan.FromMilliseconds(90);
        row.MouseEnter += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(_p.RowHover, fast));
        row.MouseLeave += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.Transparent, fast));
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

    /// <summary>A Windows 11 style on/off switch.</summary>
    sealed class Switch
    {
        public Border View { get; }
        readonly SolidColorBrush _track, _border, _knob;
        readonly TranslateTransform _knobMove = new();
        readonly Palette _p;

        public Switch(bool on, Palette p)
        {
            _p = p;
            _track = new SolidColorBrush(on ? Theme.Brand : Colors.Transparent);
            _border = new SolidColorBrush(on ? Theme.Brand : p.SubtleText);
            _knob = new SolidColorBrush(on ? Colors.White : p.SubtleText);
            var knob = new Ellipse { Width = 12, Height = 12, Fill = _knob, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = _knobMove };
            View = new Border
            {
                Width = 38,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = _track,
                BorderBrush = _border,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = knob,
            };
            _knobMove.X = on ? 18 : 0;
        }

        public void Set(bool on)
        {
            var time = TimeSpan.FromMilliseconds(160);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _knobMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 18 : 0, time) { EasingFunction = ease });
            _track.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Brand : Colors.Transparent, time));
            _border.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Brand : _p.SubtleText, time));
            _knob.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Colors.White : _p.SubtleText, time));
        }
    }
}
