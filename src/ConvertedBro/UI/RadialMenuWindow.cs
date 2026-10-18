using System;
using System.Collections.Generic;
using System.IO;
using Path = System.Windows.Shapes.Path;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>The ring of format bubbles that pops up around the bro (or the cursor).</summary>
public sealed class RadialMenuWindow : Window
{
    const double Size = 420;

    readonly IReadOnlyList<string> _files;
    readonly IReadOnlyList<BroAction> _actions;
    readonly Point _centerPx;
    readonly ScaleTransform _rootScale = new(0.75, 0.75);
    readonly TextBlock _title = new();
    readonly TextBlock _subtitle = new();
    // Backup for when Windows refuses us focus: close on any click outside the ring.
    readonly DispatcherTimer _outsideClickWatch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    bool _closing;

    public event Action<BroAction>? ActionChosen;

    public RadialMenuWindow(IReadOnlyList<string> files, IReadOnlyList<BroAction> actions, Point centerPx)
    {
        _files = files;
        _actions = actions;
        _centerPx = centerPx;

        Title = "Converted Bro";
        Width = Height = Size;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Almost transparent, so clicks around the ring close it instead of falling through.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000; // positioned properly once we know our DPI

        Content = BuildContent();

        SourceInitialized += (_, _) => PlaceAroundCenter();
        Loaded += (_, _) => AnimateIn();
        Deactivated += (_, _) => CloseAnimated();
        KeyDown += OnKeyDown;
        MouseLeftButtonUp += (_, _) => CloseAnimated();
        _outsideClickWatch.Tick += (_, _) => CloseOnOutsideClick();
        Closed += (_, _) => _outsideClickWatch.Stop();
    }

    void CloseOnOutsideClick()
    {
        bool pressed = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
        if (pressed && !IsMouseOver) CloseAnimated();
    }

    UIElement BuildContent()
    {
        int count = Math.Max(1, _actions.Count);
        double itemSize = count > 8 ? 58 : 66;
        double radius = count <= 6 ? 108 : count <= 8 ? 122 : 136;
        double outer = radius + itemSize / 2 + 14, inner = radius - itemSize / 2 - 14;
        var c = new Point(Size / 2, Size / 2);

        var root = new Canvas
        {
            Width = Size,
            Height = Size,
            RenderTransform = _rootScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        var ring = new Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(c, outer, outer), new EllipseGeometry(c, inner, inner)),
            Fill = (Brush)FindResource("PanelBrush"),
            Stroke = (Brush)FindResource("PanelBorderBrush"),
            StrokeThickness = 1,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 },
        };
        root.Children.Add(ring);

        var hub = new Ellipse
        {
            Width = (inner - 10) * 2,
            Height = (inner - 10) * 2,
            Fill = (Brush)FindResource("PanelBrush"),
            Stroke = (Brush)FindResource("PanelBorderBrush"),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(hub, c.X - inner + 10);
        Canvas.SetTop(hub, c.Y - inner + 10);
        root.Children.Add(hub);

        var hubText = new StackPanel { Width = (inner - 18) * 2, IsHitTestVisible = false };
        _title.FontSize = 15;
        _title.FontWeight = FontWeights.SemiBold;
        _subtitle.FontSize = 11.5;
        foreach (var tb in new[] { _title, _subtitle })
        {
            tb.FontFamily = (FontFamily)FindResource("BroFont");
            tb.Foreground = (Brush)FindResource(tb == _title ? "TextBrush" : "SubtleTextBrush");
            tb.TextAlignment = TextAlignment.Center;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            tb.MaxHeight = tb == _title ? 42 : 48;
            hubText.Children.Add(tb);
        }
        _subtitle.Margin = new Thickness(0, 3, 0, 0);
        hubText.Loaded += (_, _) =>
        {
            Canvas.SetLeft(hubText, c.X - hubText.ActualWidth / 2);
            Canvas.SetTop(hubText, c.Y - hubText.ActualHeight / 2);
        };
        hubText.SizeChanged += (_, _) => Canvas.SetTop(hubText, c.Y - hubText.ActualHeight / 2);
        root.Children.Add(hubText);
        ShowDefaultHubText();

        for (int i = 0; i < _actions.Count; i++)
        {
            double angle = -Math.PI / 2 + i * 2 * Math.PI / _actions.Count;
            var bubble = BuildBubble(_actions[i], i, itemSize);
            Canvas.SetLeft(bubble, c.X + radius * Math.Cos(angle) - itemSize / 2);
            Canvas.SetTop(bubble, c.Y + radius * Math.Sin(angle) - itemSize / 2);
            root.Children.Add(bubble);
        }
        return root;
    }

    FrameworkElement BuildBubble(BroAction action, int index, double size)
    {
        var color = KindColor(action.Kind);
        var light = Color.FromRgb((byte)Math.Min(255, color.R + 40), (byte)Math.Min(255, color.G + 40), (byte)Math.Min(255, color.B + 40));
        var scale = new ScaleTransform(1, 1);

        var label = new TextBlock
        {
            Text = action.Short,
            FontFamily = (FontFamily)FindResource("BroFont"),
            FontWeight = FontWeights.Bold,
            FontSize = action.Short.Length > 5 ? 12 : 14,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var number = new TextBlock
        {
            Text = index < 9 ? (index + 1).ToString() : "",
            FontFamily = (FontFamily)FindResource("BroFont"),
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var bubble = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new LinearGradientBrush(light, color, 70),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label, number } },
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35, Color = color },
            ToolTip = $"{action.Label} — {action.Hint}",
        };

        bubble.MouseEnter += (_, _) =>
        {
            Grow(scale, 1.16);
            _title.Text = action.Label;
            _subtitle.Text = action.Hint;
        };
        bubble.MouseLeave += (_, _) =>
        {
            Grow(scale, 1);
            ShowDefaultHubText();
        };
        bubble.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Choose(action);
        };
        return bubble;
    }

    void ShowDefaultHubText()
    {
        if (_actions.Count == 0)
        {
            _title.Text = "Can't convert that yet";
            _subtitle.Text = "I do images, video, audio and PDFs";
            return;
        }
        _title.Text = _files.Count == 1 ? IOPath.GetFileName(_files[0]) : $"{_files.Count} files";
        _subtitle.Text = "Pick what to make";
    }

    static void Grow(ScaleTransform scale, double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    static Color KindColor(FileKind kind) => (Color)Application.Current.FindResource(kind switch
    {
        FileKind.Video => "VideoColor",
        FileKind.Audio => "AudioColor",
        FileKind.Pdf => "PdfColor",
        _ => "ImageColor",
    });

    void Choose(BroAction action)
    {
        if (_closing) return;
        ActionChosen?.Invoke(action);
        CloseAnimated();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseAnimated();
            return;
        }
        int digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1
            : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
        if (digit >= 0 && digit < _actions.Count) Choose(_actions[digit]);
    }

    void PlaceAroundCenter()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var center = fromDevice.Transform(_centerPx);
        var px = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_centerPx.X, (int)_centerPx.Y)).WorkingArea;
        var area = new Rect(fromDevice.Transform(new Point(px.Left, px.Top)), fromDevice.Transform(new Point(px.Right, px.Bottom)));

        Left = Math.Clamp(center.X - Size / 2, area.Left, Math.Max(area.Left, area.Right - Size));
        Top = Math.Clamp(center.Y - Size / 2, area.Top, Math.Max(area.Top, area.Bottom - Size));
    }

    void AnimateIn()
    {
        // When Explorer launched us we were given the right to take focus; use it so Esc and digits work.
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        Focus();
        _outsideClickWatch.Start();

        var duration = TimeSpan.FromMilliseconds(200);
        var pop = new DoubleAnimation(1, duration) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
        _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));

        if (_actions.Count == 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            timer.Tick += (_, _) => { timer.Stop(); CloseAnimated(); };
            timer.Start();
        }
    }

    void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        var shrink = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(120));
        _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
