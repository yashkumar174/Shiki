using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using ConvertedBro.Core;
using ImageMagick;
using LineCap = System.Drawing.Drawing2D.LineCap;

namespace ConvertedBro.UI;

/// <summary>Draws the bro's face as an .ico, used by the tray and the Explorer menu.</summary>
public static class BroArt
{
    const int Version = 1; // bump when the drawing changes so old icons get replaced

    public static string IconPath => Path.Combine(Log.DataDir, $"bro-{Version}.ico");

    public static string EnsureIconFile()
    {
        if (File.Exists(IconPath)) return IconPath;
        try
        {
            using var png = new MemoryStream();
            using (var bitmap = Draw(256)) bitmap.Save(png, ImageFormat.Png);
            png.Position = 0;
            using var image = new MagickImage(png);
            image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,64,48,32,24,16");
            image.Write(IconPath, MagickFormat.Icon);
        }
        catch (Exception ex)
        {
            Log.Error("Could not create icon", ex);
        }
        return IconPath;
    }

    public static Icon LoadIcon(int size)
    {
        try
        {
            return new Icon(EnsureIconFile(), size, size);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float s = size / 100f;

        var body = new RectangleF(4 * s, 4 * s, 92 * s, 92 * s);
        using (var fill = new LinearGradientBrush(body, Color.FromArgb(0xA0, 0x8B, 0xFF), Color.FromArgb(0x5B, 0x48, 0xF2), 60f))
            g.FillEllipse(fill, body);
        using (var shine = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
            g.FillEllipse(shine, 20 * s, 12 * s, 40 * s, 20 * s);

        using var white = new SolidBrush(Color.White);
        using var pupil = new SolidBrush(Color.FromArgb(0x1E, 0x1B, 0x3A));
        foreach (var x in new[] { 28f, 56f })
        {
            g.FillEllipse(white, x * s, 32 * s, 18 * s, 21 * s);
            g.FillEllipse(pupil, (x + 5) * s, 39 * s, 10 * s, 11 * s);
        }

        using var mouth = new Pen(Color.FromArgb(0x1E, 0x1B, 0x3A), 5 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(mouth, 36 * s, 52 * s, 28 * s, 18 * s, 20, 140);
        return bmp;
    }
}
