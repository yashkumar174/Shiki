using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Shiki.Core;

/// <summary>"Make this under 200 KB": shrinks images and PDFs until they fit a size limit.</summary>
public static class SizeTools
{
    const int MinQuality = 28, MaxQuality = 92;

    public static string Label(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024 / 1024} MB" : $"{bytes / 1024} KB";

    /// <summary>
    /// Finds the best JPEG (or WEBP) quality that fits; if even the lowest quality is too big, the
    /// picture gets smaller and the search runs again.
    /// </summary>
    public static string FitImage(string input, long maxBytes)
    {
        var ext = Path.GetExtension(input).ToLowerInvariant();
        bool webp = ext == ".webp";
        var output = OutputPaths.Next(input, webp ? ".webp" : ".jpg", $"under {Label(maxBytes)}");

        if (new FileInfo(input).Length <= maxBytes && (webp || FileKinds.IsJpeg(ext)))
        {
            File.Copy(input, output);
            JobNotes.Say($"Already under {Label(maxBytes)}, saved a copy");
            return output;
        }

        using var original = ImageTools.Load(input);
        original.Strip();
        ImageTools.FlattenOnWhite(original);
        // Nobody needs a 50 MP photo for a form; this also keeps the search quick.
        if (Math.Max(original.Width, original.Height) > 3200) original.Resize(new MagickGeometry(3200, 3200));

        var best = Search(maxBytes, scale =>
        {
            var copy = (MagickImage)original.Clone();
            if (scale < 0.999) copy.Resize(new Percentage(scale * 100));
            return quality =>
            {
                copy.Quality = (uint)quality;
                return copy.ToByteArray(webp ? MagickFormat.WebP : MagickFormat.Jpeg);
            };
        });

        File.WriteAllBytes(output, best);
        Report(best.Length, maxBytes);
        return output;
    }

    /// <summary>
    /// Re-renders every page as a JPEG at the best quality that fits. Pages become pictures, so text
    /// is no longer selectable, which is what squeezes scanned and photo PDFs well.
    /// </summary>
    public static string FitPdf(string input, long maxBytes, IProgress<double> progress)
    {
        var output = OutputPaths.Next(input, ".pdf", $"under {Label(maxBytes)}");
        if (new FileInfo(input).Length <= maxBytes)
        {
            File.Copy(input, output);
            JobNotes.Say($"Already under {Label(maxBytes)}, saved a copy");
            return output;
        }

        var pages = PdfTools.RenderPages(input, 2.0, progress);
        try
        {
            var best = Search(maxBytes, scale => quality => BuildPdf(pages, scale, quality));
            File.WriteAllBytes(output, best);
            Report(best.Length, maxBytes);
            return output;
        }
        finally
        {
            foreach (var page in pages) page.Image.Dispose();
        }
    }

    /// <summary>
    /// Binary search on quality at the current scale; when nothing fits, scale down by the size
    /// ratio and try again. Returns the largest result that fits, or the smallest one seen.
    /// </summary>
    static byte[] Search(long maxBytes, Func<double, Func<int, byte[]>> encoderAtScale)
    {
        double scale = 1;
        byte[]? smallest = null;
        for (int round = 0; round < 7; round++)
        {
            var encode = encoderAtScale(scale);
            byte[]? fit = null;
            int lo = MinQuality, hi = MaxQuality;
            while (lo <= hi)
            {
                int q = (lo + hi) / 2;
                var data = encode(q);
                if (smallest == null || data.Length < smallest.Length) smallest = data;
                if (data.Length <= maxBytes) { fit = data; lo = q + 1; }
                else hi = q - 1;
            }
            if (fit != null) return fit;
            scale *= Math.Clamp(Math.Sqrt((double)maxBytes / smallest!.Length) * 0.92, 0.3, 0.9);
        }
        return smallest!;
    }

    static void Report(long size, long maxBytes)
    {
        if (size > maxBytes) JobNotes.Say($"Couldn't get under {Label(maxBytes)}; the smallest is {Label(size)}");
    }

    static byte[] BuildPdf(IReadOnlyList<PdfTools.RenderedPage> pages, double scale, int quality)
    {
        using var doc = new PdfDocument();
        foreach (var page in pages)
        {
            using var image = (MagickImage)page.Image.Clone();
            if (scale < 0.999) image.Resize(new Percentage(scale * 100));
            image.Quality = (uint)quality;
            using var jpeg = new MemoryStream(image.ToByteArray(MagickFormat.Jpeg));
            using var ximage = XImage.FromStream(jpeg);
            var pdfPage = doc.AddPage();
            pdfPage.Width = XUnit.FromPoint(page.WidthPoints);
            pdfPage.Height = XUnit.FromPoint(page.HeightPoints);
            using var gfx = XGraphics.FromPdfPage(pdfPage);
            gfx.DrawImage(ximage, 0, 0, page.WidthPoints, page.HeightPoints);
        }
        using var stream = new MemoryStream();
        doc.Save(stream);
        return stream.ToArray();
    }
}
