using System;
using System.Windows.Media;
using Shiki.Core;
using Microsoft.Win32;

namespace Shiki.UI;

/// <summary>Design tokens. Follows the Windows light/dark app setting.</summary>
public sealed class Palette
{
    public required bool Dark { get; init; }
    /// <summary>Arcade style: pixel font, square corners, stepped shapes, hard shadows.</summary>
    public bool Pixel { get; init; }
    /// <summary>Laid over the blurred desktop to make the glass.</summary>
    public required Color GlassTint { get; init; }
    public required Color GlassRim { get; init; }
    public required Color WedgeRest { get; init; }
    public required Color WedgeEdge { get; init; }
    public required Color Label { get; init; }
    public required Color SubLabel { get; init; }
    public required Color HubPill { get; init; }
    /// <summary>Menus and toasts.</summary>
    public required Color Surface { get; init; }
    public required Color SurfaceBorder { get; init; }
    public required Color RowHover { get; init; }
    public required Color Text { get; init; }
    public required Color SubtleText { get; init; }
    public required Color Track { get; init; }
}

public static class Theme
{
    public static readonly Color Brand = Color.FromRgb(0x7B, 0x6B, 0xFF);
    public static readonly Color BrandDeep = Color.FromRgb(0x4B, 0x3F, 0xD9);

    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily PixelFont = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Pixelify Sans");

    /// <summary>"arcade" or "glass"; set from settings.</summary>
    public static string Style { get; set; } = "arcade";
    public static bool Arcade => Style == "arcade";

    /// <summary>Headings and wheel labels.</summary>
    public static FontFamily TitleFont => Arcade ? PixelFont : DisplayFont;
    /// <summary>Short UI labels such as menu rows.</summary>
    public static FontFamily LabelFont => Arcade ? PixelFont : TextFont;

    static readonly Palette ArcadePalette = new()
    {
        Dark = true,
        Pixel = true,
        // Neutral near-black (not pure black), grey edges; colour comes only from the accents.
        GlassTint = Color.FromArgb(0xF5, 0x17, 0x17, 0x17),
        GlassRim = Color.FromRgb(0x3A, 0x3A, 0x3A),
        WedgeRest = Color.FromRgb(0x26, 0x26, 0x26),
        WedgeEdge = Color.FromRgb(0x3A, 0x3A, 0x3A),
        Label = Color.FromRgb(0xF2, 0xF2, 0xF2),
        SubLabel = Color.FromRgb(0xA3, 0xA3, 0xA3),
        HubPill = Color.FromRgb(0x1F, 0x1F, 0x1F),
        Surface = Color.FromArgb(0xFA, 0x17, 0x17, 0x17),
        SurfaceBorder = Color.FromRgb(0x3A, 0x3A, 0x3A),
        RowHover = Color.FromRgb(0x26, 0x26, 0x26),
        Text = Color.FromRgb(0xF2, 0xF2, 0xF2),
        SubtleText = Color.FromRgb(0xA3, 0xA3, 0xA3),
        Track = Color.FromRgb(0x2A, 0x2A, 0x2A),
    };

    static readonly Palette DarkPalette = new()
    {
        Dark = true,
        GlassTint = Color.FromArgb(0x5C, 0x16, 0x13, 0x24),
        GlassRim = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),
        WedgeRest = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
        WedgeEdge = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
        Label = Color.FromRgb(0xF4, 0xF3, 0xF8),
        SubLabel = Color.FromRgb(0xB3, 0xB0, 0xC4),
        HubPill = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
        Surface = Color.FromArgb(0xF7, 0x23, 0x21, 0x2E),
        SurfaceBorder = Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF),
        RowHover = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        Text = Color.FromRgb(0xF4, 0xF3, 0xF8),
        SubtleText = Color.FromRgb(0xA9, 0xA6, 0xBA),
        Track = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF),
    };

    static readonly Palette LightPalette = new()
    {
        Dark = false,
        GlassTint = Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF),
        GlassRim = Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF),
        WedgeRest = Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF),
        WedgeEdge = Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF),
        Label = Color.FromRgb(0x24, 0x20, 0x33),
        SubLabel = Color.FromRgb(0x5E, 0x59, 0x70),
        HubPill = Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF),
        Surface = Color.FromArgb(0xF7, 0xFA, 0xF9, 0xFD),
        SurfaceBorder = Color.FromArgb(0x1A, 0x00, 0x00, 0x00),
        RowHover = Color.FromArgb(0x0E, 0x00, 0x00, 0x00),
        Text = Color.FromRgb(0x1D, 0x1A, 0x29),
        SubtleText = Color.FromRgb(0x62, 0x5E, 0x72),
        Track = Color.FromArgb(0x18, 0x00, 0x00, 0x00),
    };

    static Palette _system = Read();

    public static Palette Current => Arcade ? ArcadePalette : _system;

    // ---- The dashboard's look, shared by the editors and settings that live inside it ----

    /// <summary>Light or dark for the dashboard and its editors; set from settings (or Windows).</summary>
    public static bool EditorDark { get; set; }
    public static Palette Editor => EditorDark ? FlowNight : FlowLight;
    /// <summary>Wispr Flow's warm orange; text on it is near-black.</summary>
    public static readonly Color EditorAccent = Color.FromRgb(0xF6, 0xA9, 0x45);
    public static readonly Color EditorAccentText = Color.FromRgb(0x1C, 0x1B, 0x19);

    static readonly Palette FlowLight = new()
    {
        Dark = false,
        GlassTint = Color.FromRgb(0xFB, 0xFA, 0xF8),
        GlassRim = Color.FromRgb(0xE7, 0xE3, 0xDD),
        WedgeRest = Color.FromRgb(0xFF, 0xFF, 0xFF),
        WedgeEdge = Color.FromRgb(0xE7, 0xE3, 0xDD),
        Label = Color.FromRgb(0x1C, 0x1B, 0x19),
        SubLabel = Color.FromRgb(0x6D, 0x68, 0x62),
        HubPill = Color.FromRgb(0xEC, 0xE9, 0xE4),
        Surface = Color.FromRgb(0xFB, 0xFA, 0xF8),
        SurfaceBorder = Color.FromRgb(0xE7, 0xE3, 0xDD),
        RowHover = Color.FromRgb(0xEC, 0xE9, 0xE4),
        Text = Color.FromRgb(0x1C, 0x1B, 0x19),
        SubtleText = Color.FromRgb(0x6D, 0x68, 0x62),
        Track = Color.FromRgb(0xE6, 0xE2, 0xDC),
    };

    static readonly Palette FlowNight = new()
    {
        Dark = true,
        GlassTint = Color.FromRgb(0x1A, 0x19, 0x18),
        GlassRim = Color.FromRgb(0x2E, 0x2B, 0x28),
        WedgeRest = Color.FromRgb(0x21, 0x1F, 0x1D),
        WedgeEdge = Color.FromRgb(0x2E, 0x2B, 0x28),
        Label = Color.FromRgb(0xF2, 0xEF, 0xEA),
        SubLabel = Color.FromRgb(0xA6, 0xA0, 0x98),
        HubPill = Color.FromRgb(0x25, 0x23, 0x20),
        Surface = Color.FromRgb(0x1A, 0x19, 0x18),
        SurfaceBorder = Color.FromRgb(0x2E, 0x2B, 0x28),
        RowHover = Color.FromRgb(0x25, 0x23, 0x20),
        Text = Color.FromRgb(0xF2, 0xEF, 0xEA),
        SubtleText = Color.FromRgb(0xA6, 0xA0, 0x98),
        Track = Color.FromRgb(0x2C, 0x2A, 0x27),
    };

    static Theme()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) _system = Read();
        };
    }

    static Palette Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 1 ? LightPalette : DarkPalette;
        }
        catch
        {
            return DarkPalette;
        }
    }

    public static Color Accent(FileKind kind) => kind switch
    {
        FileKind.Image => Color.FromRgb(0xFF, 0x8A, 0x3D), // tangerine
        FileKind.Video => Color.FromRgb(0x6F, 0x86, 0xFF), // iris
        FileKind.Audio => Color.FromRgb(0x2F, 0xD3, 0xA0), // mint
        FileKind.Pdf => Color.FromRgb(0xFF, 0x5A, 0x6E),   // coral
        FileKind.Document => Color.FromRgb(0xB0, 0x7C, 0xFF), // lavender
        _ => Brand,
    };

    public static string KindGlyph(FileKind kind) => kind switch
    {
        FileKind.Image => "\uE91B",
        FileKind.Video => "\uE714",
        FileKind.Audio => "\uE8D6",
        FileKind.Pdf => "\uEA90",
        _ => "\uE8A5",
    };

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Color Lighten(Color c, double amount) => Color.FromArgb(c.A,
        (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
