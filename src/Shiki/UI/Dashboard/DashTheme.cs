using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Shiki.UI.Dashboard;

/// <summary>
/// The dashboard's look, after Wispr Flow: a warm paper canvas (or deep charcoal), white cards,
/// a serif for the big moments, and one warm orange accent. No blue anywhere.
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
    public static readonly DashPalette Light = new(
        Dark: false,
        Canvas: Rgb(0xF2F0EC), Panel: Rgb(0xFBFAF8), Card: Rgb(0xFFFFFF), Line: Rgb(0xE7E3DD),
        Text: Rgb(0x1C1B19), Muted: Rgb(0x6D6862), Faint: Rgb(0x8C877F),
        Hover: Rgb(0xECE9E4), Selected: Rgb(0xE6E2DC),
        Hero: Rgb(0x0E0D0C), HeroText: Rgb(0xFFFFFF),
        Accent: Rgb(0xF6A945), AccentEdge: Rgb(0xD98A2B), AccentText: Rgb(0x1C1B19),
        Danger: Rgb(0xC2410C));

    public static readonly DashPalette Night = new(
        Dark: true,
        Canvas: Rgb(0x121110), Panel: Rgb(0x1A1918), Card: Rgb(0x211F1D), Line: Rgb(0x2E2B28),
        Text: Rgb(0xF2EFEA), Muted: Rgb(0xA6A098), Faint: Rgb(0x837E77),
        Hover: Rgb(0x252320), Selected: Rgb(0x2C2A27),
        Hero: Rgb(0x070707), HeroText: Rgb(0xFFFFFF),
        Accent: Rgb(0xF6A945), AccentEdge: Rgb(0xB9741F), AccentText: Rgb(0x1C1B19),
        Danger: Rgb(0xF08A5D));

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

    public static readonly FontFamily Serif = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Instrument Serif");
    public static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
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

    /// <summary>A small all-caps label over a section, like Flow's "TODAY".</summary>
    public TextBlock Label(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = Sans,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = B(P.Faint),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public Border Card(UIElement child, Thickness? padding = null, double radius = 20, Color? fill = null) => new()
    {
        Background = B(fill ?? P.Card),
        BorderBrush = B(P.Line),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Padding = padding ?? new Thickness(24),
        Child = child,
    };

    public enum Pill { Accent, Light, Quiet, Outline }

    /// <summary>A rounded button. Accent is Flow's orange; Light sits on the black hero card.</summary>
    public Border Button(string label, Action onClick, Pill kind = Pill.Quiet, string? glyph = null, double size = 14)
    {
        var (fill, hot, edge, ink) = kind switch
        {
            Pill.Accent => (P.Accent, Theme.Lighten(P.Accent, 0.1), P.AccentEdge, P.AccentText),
            Pill.Light => (Color.FromRgb(0xF4, 0xF0, 0xE9), Colors.White, Colors.Transparent, Color.FromRgb(0x1C, 0x1B, 0x19)),
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
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 9, 18, 10),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row,
            Focusable = true,
        };
        var fast = TimeSpan.FromMilliseconds(110);
        button.MouseEnter += (_, _) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot, fast));
        button.MouseLeave += (_, _) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fill, fast));
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
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }

    /// <summary>A thin rule between rows.</summary>
    public Border Rule() => new() { Height = 1, Background = B(P.Line) };
}
