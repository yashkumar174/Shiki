using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Shiki.Shell;

/// <summary>The colour of the pixel under the mouse pointer, with a plain name ("a deep blue").</summary>
public static class ScreenColour
{
    public static (string Hex, byte R, byte G, byte B) UnderPointer()
    {
        GetCursorPos(out var p);
        var dc = GetDC(IntPtr.Zero);
        try
        {
            uint c = GetPixel(dc, p.X, p.Y);
            byte r = (byte)(c & 0xFF), g = (byte)((c >> 8) & 0xFF), b = (byte)((c >> 16) & 0xFF);
            return ($"#{r:X2}{g:X2}{b:X2}", r, g, b);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    static readonly (string Name, byte R, byte G, byte B)[] Names =
    [
        ("black", 0, 0, 0), ("near black", 24, 24, 24), ("charcoal", 48, 48, 52), ("white", 255, 255, 255), ("grey", 128, 128, 128), ("light grey", 200, 200, 200), ("dark grey", 64, 64, 64),
        ("red", 220, 30, 30), ("dark red", 128, 0, 0), ("pink", 255, 160, 190), ("hot pink", 255, 60, 150), ("orange", 255, 140, 0),
        ("peach", 255, 200, 160), ("brown", 140, 80, 30), ("beige", 235, 220, 190), ("yellow", 250, 220, 30), ("gold", 212, 175, 55),
        ("olive", 128, 128, 0), ("lime green", 150, 230, 50), ("green", 40, 160, 60), ("dark green", 0, 90, 30), ("mint", 170, 240, 200),
        ("teal", 0, 128, 128), ("turquoise", 60, 210, 200), ("sky blue", 120, 190, 240), ("blue", 30, 100, 230), ("navy", 0, 30, 100),
        ("indigo", 75, 0, 130), ("purple", 130, 50, 180), ("lavender", 200, 180, 240), ("magenta", 220, 0, 200), ("maroon", 110, 20, 50),
    ];

    /// <summary>The nearest everyday colour name.</summary>
    public static string Name(byte r, byte g, byte b) =>
        Names.OrderBy(n =>
        {
            // "Redmean": a cheap distance that follows what eyes see better than plain RGB.
            double mean = (n.R + r) / 2.0, dr = n.R - r, dg = n.G - g, db = n.B - b;
            return (2 + mean / 256) * dr * dr + 4 * dg * dg + (2 + (255 - mean) / 256) * db * db;
        }).First().Name;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
}
