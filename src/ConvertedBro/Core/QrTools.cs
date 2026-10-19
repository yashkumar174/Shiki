using System;
using System.Collections.Generic;
using System.Linq;
using ImageMagick;
using QRCoder;
using ZXing;
using ZXing.Common;

namespace ConvertedBro.Core;

/// <summary>Makes QR codes (QRCoder) and reads QR codes and barcodes in pictures (ZXing).</summary>
public static class QrTools
{
    /// <summary>A crisp black-on-white QR code PNG with a quiet zone.</summary>
    public static byte[] MakePng(string text, int pixelsPerModule = 16)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    /// <summary>Every code found in the picture, in reading order (top to bottom, left to right).</summary>
    public static IReadOnlyList<string> Read(MagickImage source)
    {
        using var image = (MagickImage)source.Clone();
        ImageTools.FlattenOnWhite(image);
        // Huge photos: the code is still large in pixels at 2000 px, and decoding is much faster.
        if (Math.Max(image.Width, image.Height) > 2000) image.Resize(new MagickGeometry(2000, 2000));

        var found = Decode(image);
        if (found.Count == 0)
        {
            // Light-on-dark codes (dark mode apps) read once inverted.
            image.Negate();
            found = Decode(image);
        }
        if (found.Count == 0 && Math.Max(image.Width, image.Height) < 600)
        {
            // Tiny snips: blow them up so each module is several pixels.
            image.Negate();
            image.FilterType = FilterType.Point;
            image.Resize(new Percentage(300));
            found = Decode(image);
        }
        return found
            .OrderBy(r => r.ResultPoints.Length > 0 ? r.ResultPoints.Min(p => p.Y) : 0)
            .ThenBy(r => r.ResultPoints.Length > 0 ? r.ResultPoints.Min(p => p.X) : 0)
            .Select(r => r.Text)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();
    }

    static List<Result> Decode(MagickImage image)
    {
        var rgb = image.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var source = new RGBLuminanceSource(rgb, (int)image.Width, (int)image.Height, RGBLuminanceSource.BitmapFormat.RGB24);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = false },
        };
        return reader.DecodeMultiple(source)?.ToList() ?? [];
    }

    public static bool IsLink(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
