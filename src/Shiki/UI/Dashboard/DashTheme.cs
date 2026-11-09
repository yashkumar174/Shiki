using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Shiki.UI.Dashboard;

/// <summary>
/// The dashboard's look, "night shift": Shiki is a cat that lives on your screen after dark. Ink
/// blue-black (or a pale day sky), moonlight blue for everything you can press, and the cat's own
/// amber eyes only on the cat's things. Pixel type for the big moments, Manrope for reading.
/// </summary>
public sealed record DashPalette(
    bool Dark,
    Color Canvas, Color Panel, Color Card, Color Line,
    Color Text, Color Muted, Color Faint,
    Color Hover, Color Selected,
    Color Hero, Color HeroText,
    Color Accent, Color AccentEdge, Color AccentText,
    Color Danger)
{
    /// <summary>Day: a pale sky, white cards, deep ink, a deeper blue to press.</summary>
    public static readonly DashPalette Light = new(
        Dark: false,
        Canvas: Rgb(0xE8EDF4), Panel: Rgb(0xF4F7FB), Card: Rgb(0xFFFFFF), Line: Rgb(0xD8E0EB),
        Text: Rgb(0x0E1726), Muted: Rgb(0x526077), Faint: Rgb(0x75829A),
        Hover: Rgb(0xE6ECF4), Selected: Rgb(0xDCE7F5),
        Hero: Rgb(0x0B1322), HeroText: Rgb(0xF3F6FA),
        Accent: Rgb(0x1971C2), AccentEdge: Rgb(0x135A9C), AccentText: Rgb(0xFFFFFF),
        Danger: Rgb(0xC8410C));

    /// <summary>Night: ink blue-black, panels a step lighter, moonlight blue to press.</summary>
    public static readonly DashPalette Night = new(
        Dark: true,
        Canvas: Rgb(0x0A0D14), Panel: Rgb(0x0E121B), Card: Rgb(0x141A27), Line: Rgb(0x222B3C),
        Text: Rgb(0xE9EDF3), Muted: Rgb(0x9AA5B8), Faint: Rgb(0x6F7B90),
        Hover: Rgb(0x1A2132), Selected: Rgb(0x1D2840),
        Hero: Rgb(0x0B1322), HeroText: Rgb(0xF3F6FA),
        Accent: Rgb(0x5AB9FC), AccentEdge: Rgb(0x3A9EE6), AccentText: Rgb(0x06111F),
        Danger: Rgb(0xFF7A6B));

    /// <summary>The cat's eyes: its level, its streak, the cat itself. Never a button.</summary>
    public static readonly Color Eye = Rgb(0xF2C14E);

    static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Windows' own app setting (Settings, Personalisation, Colours).</summary>
    public static bool WindowsIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}

/// <summary>Small building blocks for the dashboard pages, in the current palette.</summary>
public sealed class DashKit(DashPalette p)
{
    public DashPalette P { get; } = p;

    /// <summary>The cat's own pixel type: titles, the greeting, big numbers.</summary>
    public static readonly FontFamily Pixel = Theme.PixelFont;
    /// <summary>Everything read: Manrope (Segoe UI for what it doesn't cover, such as Devanagari).</summary>
    public static readonly FontFamily Sans = Theme.TextFont;
    public static readonly FontFamily Display = Theme.TextFont;
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static SolidColorBrush B(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    public static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public TextBlock Text(string text, double size = 14, Color? color = null, FontWeight? weight = null, FontFamily? font = null, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = font ?? Sans,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = B(color ?? P.Text),
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    public TextBlock Icon(string glyph, double size = 16, Color? color = null) => new()
    {
        Text = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = B(color ?? P.Text),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>A small label over a section ("1 November", "Cats"), in sentence case.</summary>
    public TextBlock Label(string text) => new()
    {
        Text = text,
        FontFamily = Sans,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = B(P.Muted),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public Border Card(UIElement child, Thickness? padding = null, double radius = 18, Color? fill = null) => new()
    {
        Background = B(fill ?? P.Card),
        BorderBrush = B(P.Line),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Padding = padding ?? new Thickness(24),
        Child = child,
    };

    public enum Pill { Accent, Light, Quiet, Outline }

    /// <summary>A rounded button: Accent is moonlight blue; Light sits on the night card.</summary>
    public Border Button(string label, Action onClick, Pill kind = Pill.Quiet, string? glyph = null, double size = 14)
    {
        var (fill, hot, edge, ink) = kind switch
        {
            Pill.Accent => (P.Accent, Theme.Lighten(P.Accent, 0.12), P.AccentEdge, P.AccentText),
            Pill.Light => (Color.FromRgb(0xEE, 0xF3, 0xF9), Colors.White, Colors.Transparent, Color.FromRgb(0x0B, 0x13, 0x22)),
            Pill.Outline => (Colors.Transparent, A(Colors.White, 0x1A), A(Colors.White, 0x55), Colors.White),
            _ => (P.Hover, P.Selected, P.Line, P.Text),
        };
        var background = new SolidColorBrush(fill);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph != null)
        {
            var icon = Icon(glyph, size, ink);
            icon.Margin = new Thickness(0, 1, 8, 0);
            row.Children.Add(icon);
        }
        var text = Text(label, size, ink, FontWeights.SemiBold);
        row.Children.Add(text);
        var button = new Border
        {
            Background = background,
            BorderBrush = B(edge),
            BorderThickness = new Thickness(kind == Pill.Light ? 0 : 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 9, 18, 10),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row,
            Focusable = true,
        };
        var fast = TimeSpan.FromMilliseconds(110);
        button.MouseEnter += (_, _) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot, fast));
        button.MouseLeave += (_, _) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fill, fast));
        Press(button);
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        button.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; onClick(); } };
        return button;
    }

    /// <summary>A square icon button (title bar, list rows).</summary>
    public Border IconButton(string glyph, Action onClick, string tooltip, double size = 15, Color? color = null, double box = 34, Color? hover = null)
    {
        var background = new SolidColorBrush(Colors.Transparent);
        var button = new Border
        {
            Width = box,
            Height = box,
            CornerRadius = new CornerRadius(9),
            Background = background,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = Icon(glyph, size, color ?? P.Text),
        };
        var hot = hover ?? P.Hover;
        button.MouseEnter += (_, _) => background.Color = hot;
        button.MouseLeave += (_, _) => background.Color = Colors.Transparent;
        Press(button);
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }

    /// <summary>
    /// How every button answers the pointer: it rises a pixel under the mouse and sinks a little
    /// while pressed, so a click is felt before anything happens.
    /// </summary>
    public static void Press(FrameworkElement button)
    {
        var lift = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = new TransformGroup { Children = { scale, lift } };
        var quick = new Duration(TimeSpan.FromMilliseconds(120));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        void To(DependencyProperty property, Animatable target, double value) =>
            target.BeginAnimation(property, new DoubleAnimation(value, quick) { EasingFunction = ease });
        button.MouseEnter += (_, _) => To(TranslateTransform.YProperty, lift, -1);
        button.MouseLeave += (_, _) => { To(TranslateTransform.YProperty, lift, 0); To(ScaleTransform.ScaleXProperty, scale, 1); To(ScaleTransform.ScaleYProperty, scale, 1); };
        button.PreviewMouseLeftButtonDown += (_, _) => { To(ScaleTransform.ScaleXProperty, scale, 0.97); To(ScaleTransform.ScaleYProperty, scale, 0.97); };
        button.PreviewMouseLeftButtonUp += (_, _) => { To(ScaleTransform.ScaleXProperty, scale, 1); To(ScaleTransform.ScaleYProperty, scale, 1); };
    }

    /// <summary>A thin rule between rows.</summary>
    public Border Rule() => new() { Height = 1, Background = B(P.Line) };
}
