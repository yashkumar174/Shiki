using System;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using ConvertedBro.Core;
using ImageMagick;

namespace ConvertedBro.UI;

/// <summary>Icons (tray, Explorer menu) made from the chosen cat's pixel head.</summary>
public static class BroArt
{
    const int Version = 2; // bump when the art changes so old icons get replaced
    static readonly uint[] Sizes = [256, 64, 48, 40, 32, 24, 20, 16];

    public static string IconPath(CatSkin skin) => Path.Combine(Log.DataDir, $"cat-{skin.Id}-{Version}.ico");

    public static string EnsureIconFile(CatSkin skin)
    {
        var path = IconPath(skin);
        if (File.Exists(path)) return path;
        try
        {
            using var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CatSprites.Head(skin)));
            encoder.Save(png);

            using var frames = new MagickImageCollection();
            foreach (var size in Sizes)
            {
                png.Position = 0;
                var frame = new MagickImage(png);
                // Point sampling keeps hard pixel edges; anything smoother turns the cat to mush.
                frame.FilterType = FilterType.Point;
                frame.Resize(size, size);
                frames.Add(frame);
            }
            frames.Write(path, MagickFormat.Icon);
        }
        catch (Exception ex)
        {
            Log.Error("Could not create icon", ex);
        }
        return path;
    }

    public static Icon LoadIcon(CatSkin skin, int size)
    {
        try
        {
            return new Icon(EnsureIconFile(skin), size, size);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
