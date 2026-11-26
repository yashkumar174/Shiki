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
using Shiki.Shell;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// The screenshot pile's little panel, above the cat. After a screenshot it offers to keep
/// collecting (and, on a web page, to capture the whole page); while collecting it shows the
/// pile with Paste all. It never takes the keyboard from the app you're in (so Paste all lands
/// there) and it never shows up in screenshots.
/// </summary>
public sealed class ShotTray : Window
{
    readonly DashKit k = new(DashPalette.Night);
    DashPalette P => k.P;
    readonly Border _card;
    readonly StackPanel _body = new();
    readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(9) };
    readonly TranslateTransform _slide = new();

    public required Action Collect { get; init; }
    public required Action WholePage { get; init; }
    public required Action PasteAll { get; init; }
    public required Action PasteAsOne { get; init; }
    public required Action Done { get; init; }
    public required Action<string> Remove { get; init; }
    public required Action StopCapture { get; init; }
    /// <summary>Which cat is out, for its head on the panel.</summary>
    public required Func<string?> SkinId { get; init; }
    /// <summary>Where the cat is (DIPs), to sit just above it.</summary>
    public Func<Rect?>? CatArea { get; init; }
    /// <summary>The cat's open side (away from its edge), to sit on that side.</summary>
    public Func<Vector>? CatAway { get; init; }

    public const double CardWidth = 372;

    public ShotTray()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = CardWidth + 40;
        UseLayoutRounding = true;

        _card = new Border
        {
            Margin = new Thickness(20),
            Padding = new Thickness(18, 16, 18, 18),
            CornerRadius = new CornerRadius(20),
            Background = DashKit.B(P.Card),
            BorderBrush = DashKit.B(Color.FromRgb(0x2A, 0x35, 0x50)),
            BorderThickness = new Thickness(1.5),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.5, Color = Colors.Black },
            Child = _body,
            RenderTransform = _slide,
        };
        Content = _card;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
            // Never in a screenshot: the next snip shouldn't catch the panel itself.
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        };
        SizeChanged += (_, _) => Place();
        _autoHide.Tick += (_, _) => { _autoHide.Stop(); Done(); };
        MouseEnter += (_, _) => _autoHide.Stop();
    }

    // ---- What it shows ----

    /// <summary>Just after a screenshot (not collecting yet): keep it and add more?</summary>
    public void ShowOffer(string shot, string? pageApp)
    {
        _body.Children.Clear();
        _body.Children.Add(Head("Screenshot copied", CloseButton()));
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var thumb = Thumb(shot, 84, 56, removable: false);
        DockPanel.SetDock(thumb, Dock.Left);
        row.Children.Add(thumb);
        var line = k.Text("Taking a few? Keep collecting, then paste them all in one go.", 13.5, P.Muted, wrap: true);
        line.Margin = new Thickness(14, 0, 0, 0);
        line.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(line);
        _body.Children.Add(row);
        var buttons = Buttons();
        buttons.Children.Add(Button("Collect more", Collect, DashKit.Pill.Accent, "\uE710"));
        if (pageApp != null) buttons.Children.Add(Button("Capture whole page", WholePage, DashKit.Pill.Quiet, "\uE8A9"));
        _body.Children.Add(buttons);
        Appear();
        _autoHide.Start();
    }

    /// <summary>Collecting: the pile, and what to do with it.</summary>
    public void ShowPile(IReadOnlyList<string> shots, string? pageApp, string? note = null)
    {
        _autoHide.Stop();
        _body.Children.Clear();
        var count = shots.Count == 1 ? "1 screenshot" : $"{shots.Count} screenshots";
        _body.Children.Add(Head(count, CloseButton()));
        var strip = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var shot in shots.TakeLast(8)) strip.Children.Add(Thumb(shot, 72, 48, removable: true));
        if (shots.Count > 8)
        {
            var more = k.Text($"+{shots.Count - 8}", 13, P.Muted, FontWeights.SemiBold);
            more.VerticalAlignment = VerticalAlignment.Center;
            more.Margin = new Thickness(4, 0, 0, 8);
            strip.Children.Add(more);
        }
        _body.Children.Add(strip);
        var hint = k.Text(note ?? "Take more with Win+Shift+S. Ctrl+V pastes the whole pile; Paste all puts them in one by one.", 12.5, note != null ? P.Accent : P.Muted, wrap: true);
        hint.Margin = new Thickness(0, 4, 0, 0);
        _body.Children.Add(hint);
        var buttons = Buttons();
        buttons.Children.Add(Button("Paste all", PasteAll, DashKit.Pill.Accent, "\uE77F"));
        buttons.Children.Add(Button("As one picture", PasteAsOne, DashKit.Pill.Quiet, null));
        if (pageApp != null) buttons.Children.Add(Button("Whole page", WholePage, DashKit.Pill.Quiet, "\uE8A9"));
        buttons.Children.Add(Button("Done", Done, DashKit.Pill.Quiet, null));
        _body.Children.Add(buttons);
        Appear();
    }

    /// <summary>A whole page being captured.</summary>
    public void ShowBusy(string text)
    {
        _autoHide.Stop();
        _body.Children.Clear();
        _body.Children.Add(Head("Capturing the whole page", null));
        var line = k.Text(text, 13.5, P.Muted, wrap: true);
        line.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(line);
        var hands = k.Text("Hands off the mouse for a moment.", 12.5, P.Faint, wrap: true);
        hands.Margin = new Thickness(0, 4, 0, 0);
        _body.Children.Add(hands);
        var buttons = Buttons();
        buttons.Children.Add(Button("Stop", StopCapture, DashKit.Pill.Quiet, "\uE71A"));
        _body.Children.Add(buttons);
        Appear();
    }

    /// <summary>A short message (a capture that didn't work).</summary>
    public void ShowNote(string title, string text)
    {
        _body.Children.Clear();
        _body.Children.Add(Head(title, CloseButton()));
        var line = k.Text(text, 13.5, P.Muted, wrap: true);
        line.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(line);
        Appear();
        _autoHide.Start();
    }

    // ---- Pieces ----

    UIElement Head(string title, UIElement? close)
    {
        var grid = new DockPanel();
        var cat = new Image
        {
            Source = CatSprites.Head(CatSprites.Skin(SkinId())),
            Width = 26,
            Height = 26,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.NearestNeighbor);
        DockPanel.SetDock(cat, Dock.Left);
        grid.Children.Add(cat);
        if (close != null)
        {
            DockPanel.SetDock(close, Dock.Right);
            grid.Children.Add(close);
        }
        var text = k.Text(title, 20, P.Text, font: DashKit.Pixel);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        return grid;
    }

    UIElement CloseButton() => k.IconButton("\uE711", Done, "Close", 12, P.Muted, 28);

    static WrapPanel Buttons() => new() { Margin = new Thickness(0, 14, 0, 0) };

    Border Button(string label, Action click, DashKit.Pill kind, string? glyph)
    {
        var button = k.Button(label, click, kind, glyph, 13.5);
        button.Margin = new Thickness(0, 0, 8, 8);
        button.Padding = new Thickness(14, 7, 14, 8);
        return button;
    }

    FrameworkElement Thumb(string path, double w, double h, bool removable)
    {
        var picture = Small(path, (int)(h * 2));
        var tile = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(8),
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1),
            Background = picture != null ? new ImageBrush(picture) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top } : DashKit.B(P.Panel),
            ToolTip = Path.GetFileName(path),
        };
        // A new one pops in.
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        var pop = new ScaleTransform(0.6, 0.6);
        tile.RenderTransform = pop;
        var spring = new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
        if (!removable) return tile;

        var holder = new Grid { Margin = new Thickness(0, 0, 8, 8), Children = { tile } };
        var x = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = DashKit.B(Color.FromArgb(0xE0, 0x0A, 0x0D, 0x14)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -6, 0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Hidden,
            Child = k.Icon("\uE711", 9, Colors.White),
            ToolTip = "Take this one out",
        };
        ((TextBlock)x.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)x.Child).VerticalAlignment = VerticalAlignment.Center;
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; Remove(path); };
        holder.Children.Add(x);
        holder.MouseEnter += (_, _) => x.Visibility = Visibility.Visible;
        holder.MouseLeave += (_, _) => x.Visibility = Visibility.Hidden;
        return holder;
    }

    static BitmapSource? Small(string path, int height)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = height;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    // ---- Showing and placing ----

    void Appear()
    {
        if (IsVisible) { Place(); return; }
        Opacity = 0;
        Show();
        Place();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public void Vanish()
    {
        _autoHide.Stop();
        if (!IsVisible) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => { if (Opacity < 0.05) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Just above the cat, or bottom-left of the screen.</summary>
    void Place()
    {
        var area = SystemParameters.WorkArea;
        double height = ActualHeight > 0 ? ActualHeight : 200;
        if (CatArea?.Invoke() is { } cat)
        {
            // The card has a 20 px see-through margin for its shadow; let that overlap the gap.
            var spot = CatWindow.Beside(cat, CatAway?.Invoke() ?? new Vector(0, -1), new Size(Width, height), -14);
            Left = spot.X;
            Top = spot.Y;
        }
        else
        {
            Left = area.Left + 8;
            Top = area.Bottom - height;
        }
    }

    /// <summary>Dev aid: the panel's card, for drawing it to a PNG.</summary>
    public FrameworkElement Card => _card;
}
