using System;
using System.IO;
using ImageMagick;

namespace Shiki.Core;

public static class ImageTools
{
    /// <summary>Converts to the format implied by <paramref name="output"/>'s extension.</summary>
    public static void Convert(string input, string output)
    {
        var targetExt = Path.GetExtension(output).ToLowerInvariant();

        // Keep animation when going between animated formats (gif <-> webp).
        if (targetExt is ".gif" or ".webp")
        {
            using var frames = new MagickImageCollection(input);
            if (frames.Count > 1)
            {
                frames.Coalesce();
                if (targetExt == ".webp")
                    foreach (var frame in frames) frame.Quality = 85;
                frames.Write(output);
                return;
            }
        }

        using var image = Load(input);
        switch (targetExt)
        {
            case ".jpg" or ".jpeg":
                FlattenOnWhite(image);
                image.Quality = 90;
                break;
            case ".webp":
                image.Quality = 85;
                break;
            case ".ico":
                MakeSquare(image, 256);
                image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,128,64,48,32,16");
                break;
        }
        image.Write(output);
    }

    /// <summary>Makes the file smaller while keeping its format (HEIC/BMP/TIFF become JPG).</summary>
    public static string Compress(string input)
    {
        var ext = Path.GetExtension(input).ToLowerInvariant();
        var outExt = ext is ".png" or ".webp" or ".gif" || FileKinds.IsJpeg(ext) ? ext : ".jpg";
        var output = OutputPaths.Next(input, outExt, "small");

        using var image = Load(input);
        image.Strip();
        if (Math.Max(image.Width, image.Height) > 2560)
            image.Resize(new MagickGeometry(2560, 2560));

        switch (outExt)
        {
            case ".png":
            case ".gif":
                image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                break;
            case ".webp":
                image.Quality = 70;
                break;
            default:
                FlattenOnWhite(image);
                image.Quality = 72;
                break;
        }
        image.Write(output);
        return output;
    }

    public static string Resize(string input, int percent)
    {
        var output = OutputPaths.Next(input, Path.GetExtension(input), $"{percent}%");
        using var image = Load(input);
        image.Resize(new Percentage(percent));
        image.Write(output);
        return output;
    }

    public static string RotateRight(string input)
    {
        var output = OutputPaths.Next(input, Path.GetExtension(input), "rotated");
        using var image = Load(input);
        image.Rotate(90);
        if (FileKinds.IsJpeg(Path.GetExtension(input))) image.Quality = 92;
        image.Write(output);
        return output;
    }

    /// <summary>Removes EXIF (camera, date, GPS location) and other embedded profiles.</summary>
    public static string StripMetadata(string input)
    {
        var output = OutputPaths.Next(input, Path.GetExtension(input), "clean");
        // Load applies the camera rotation first, so the photo still looks the same once the tag is gone.
        using var image = Load(input);
        image.Strip();
        if (FileKinds.IsJpeg(Path.GetExtension(input))) image.Quality = 92;
        image.Write(output);
        return output;
    }

    /// <summary>Loads a single frame with phone-camera rotation applied.</summary>
    public static MagickImage Load(string input)
    {
        var image = new MagickImage(input);
        image.AutoOrient();
        return image;
    }

    public static void FlattenOnWhite(MagickImage image)
    {
        if (!image.HasAlpha) return;
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
    }

    static void MakeSquare(MagickImage image, uint size)
    {
        image.Resize(new MagickGeometry(size, size));
        image.BackgroundColor = MagickColors.Transparent;
        image.Extent(size, size, Gravity.Center);
    }
}
