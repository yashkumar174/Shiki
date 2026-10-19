using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using ConvertedBro.Core;
using ImageMagick;

namespace ConvertedBro.UI;

/// <summary>Icons (tray, Explorer menu) made from the chosen cat's pixel head.</summary>
public static class BroArt
{
    const int Version = 3; // bump when the art (or the file) changes so old icons get replaced
    static readonly uint[] Sizes = [256, 64, 48, 40, 32, 24, 20, 16];

    public static string IconPath(CatSkin skin) => Path.Combine(Log.DataDir, $"cat-{skin.Id}-{Version}.ico");

    public static string EnsureIconFile(CatSkin skin)
    {
        var path = IconPath(skin);
        if (File.Exists(path)) return path;
        try
        {
            WriteIcon(skin, path);
        }
        catch (Exception ex)
        {
            Log.Error("Could not create icon", ex);
        }
        return path;
    }

    /// <summary>
    /// One .ico holding every size, each a PNG scaled with hard pixel edges (smoothing turns the
    /// cat to mush). Written by hand: Magick.NET split the sizes into separate files.
    /// </summary>
    public static void WriteIcon(CatSkin skin, string path)
    {
        using var source = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CatSprites.Head(skin)));
        encoder.Save(source);

        var images = new List<(uint Size, byte[] Png)>();
        foreach (var size in Sizes)
        {
            source.Position = 0;
            using var frame = new MagickImage(source);
            frame.FilterType = FilterType.Point;
            frame.Resize(size, size);
            images.Add((size, frame.ToByteArray(MagickFormat.Png32)));
        }

        using var file = File.Create(path + ".tmp");
        using (var w = new BinaryWriter(file))
        {
            w.Write((ushort)0);               // reserved
            w.Write((ushort)1);               // type: icon
            w.Write((ushort)images.Count);
            int offset = 6 + 16 * images.Count;
            foreach (var (size, png) in images)
            {
                w.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);             // no palette
                w.Write((byte)0);
                w.Write((ushort)1);           // planes
                w.Write((ushort)32);          // bits per pixel
                w.Write(png.Length);
                w.Write(offset);
                offset += png.Length;
            }
            foreach (var (_, png) in images) w.Write(png);
        }
        File.Move(path + ".tmp", path, overwrite: true);
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
