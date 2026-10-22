using System;
using System.IO;
using PdfSharp.Fonts;

namespace ConvertedBro.Core;

/// <summary>Gives PDFsharp the Segoe UI files that ship with Windows, for page numbers and similar text.</summary>
public sealed class PdfFonts : IFontResolver
{
    static readonly string FontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public static void Install()
    {
        if (GlobalFontSettings.FontResolver == null) GlobalFontSettings.FontResolver = new PdfFonts();
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // Every family maps to Segoe UI: it's on every Windows machine and reads well small.
        string face = (bold, italic) switch
        {
            (true, true) => "segoeuiz",
            (true, false) => "segoeuib",
            (false, true) => "segoeuii",
            _ => "segoeui",
        };
        return File.Exists(Path.Combine(FontDir, face + ".ttf")) ? new FontResolverInfo(face) : new FontResolverInfo("segoeui");
    }

    public byte[]? GetFont(string faceName)
    {
        var path = Path.Combine(FontDir, faceName + ".ttf");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
