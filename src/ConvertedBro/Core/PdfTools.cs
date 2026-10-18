using System;
using System.Collections.Generic;
using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ConvertedBro.Core;

public static class PdfTools
{
    // PDFium (behind Docnet) is not thread safe.
    static readonly object PdfiumGate = new();

    const double A4Width = 595.28, A4Height = 841.89, Margin = 18;

    /// <summary>Renders every page to an image. One page gives one file; more give a folder.</summary>
    public static IReadOnlyList<string> ToImages(string input, string ext, IProgress<double> progress)
    {
        lock (PdfiumGate)
        {
            // Scaling 2 is about 144 DPI: sharp on screen, reasonable file sizes.
            using var doc = DocLib.Instance.GetDocReader(input, new PageDimensions(2.0));
            int count = doc.GetPageCount();
            var folder = count > 1 ? OutputPaths.NextFolder(input, "pages") : null;
            var outputs = new List<string>();

            for (int i = 0; i < count; i++)
            {
                using var page = doc.GetPageReader(i);
                var raw = page.GetImage();
                using var image = new MagickImage();
                image.ReadPixels(raw, new PixelReadSettings((uint)page.GetPageWidth(), (uint)page.GetPageHeight(), StorageType.Char, PixelMapping.BGRA));
                ImageTools.FlattenOnWhite(image);
                if (ext == ".jpg") image.Quality = 90;

                var output = folder == null
                    ? OutputPaths.Next(input, ext)
                    : Path.Combine(folder, PageName(i, count, ext));
                image.Write(output);
                outputs.Add(output);
                progress.Report((i + 1) / (double)count);
            }
            return outputs;
        }
    }

    public sealed record RenderedPage(MagickImage Image, double WidthPoints, double HeightPoints);

    /// <summary>Renders every page on a white background. Scaling 1 is 72 DPI. Caller disposes the images.</summary>
    public static List<RenderedPage> RenderPages(string input, double scaling, IProgress<double> progress)
    {
        lock (PdfiumGate)
        {
            using var doc = DocLib.Instance.GetDocReader(input, new PageDimensions(scaling));
            int count = doc.GetPageCount();
            var pages = new List<RenderedPage>(count);
            for (int i = 0; i < count; i++)
            {
                using var page = doc.GetPageReader(i);
                int w = page.GetPageWidth(), h = page.GetPageHeight();
                var image = new MagickImage();
                image.ReadPixels(page.GetImage(), new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.BGRA));
                ImageTools.FlattenOnWhite(image);
                pages.Add(new RenderedPage(image, w / scaling, h / scaling));
                progress.Report((i + 1) / (double)count);
            }
            return pages;
        }
    }

    /// <summary>Joins already-processed images (e.g. scanned pages) into one PDF of A4 pages.</summary>
    public static string ImagesToPdf(IReadOnlyList<MagickImage> images, string output)
    {
        using var result = new PdfDocument();
        foreach (var image in images) AddImagePage(result, image, jpeg: true);
        result.Save(output);
        return output;
    }

    /// <summary>Splits a PDF into one file per page, inside a new folder.</summary>
    public static IReadOnlyList<string> Split(string input, IProgress<double> progress)
    {
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        int count = source.PageCount;
        var folder = OutputPaths.NextFolder(input, "pages");
        var outputs = new List<string>();
        for (int i = 0; i < count; i++)
        {
            using var single = new PdfDocument();
            single.AddPage(source.Pages[i]);
            var output = Path.Combine(folder, PageName(i, count, ".pdf"));
            single.Save(output);
            outputs.Add(output);
            progress.Report((i + 1) / (double)count);
        }
        return outputs;
    }

    /// <summary>Joins PDFs and images (in the given order) into one PDF. Images get an A4 page each.</summary>
    public static string Combine(IReadOnlyList<string> inputs, IProgress<double> progress)
    {
        var output = OutputPaths.Next(inputs[0], ".pdf", inputs.Count > 1 ? "merged" : null);
        using var result = new PdfDocument();

        for (int i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (FileKinds.Of(input) == FileKind.Pdf)
            {
                using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
                foreach (var page in source.Pages) result.AddPage(page);
            }
            else
            {
                AddImagePage(result, input);
            }
            progress.Report((i + 1) / (double)inputs.Count);
        }

        result.Save(output);
        return output;
    }

    static string PageName(int index, int count, string ext) =>
        $"page-{(index + 1).ToString().PadLeft(count.ToString().Length, '0')}{ext}";

    static void AddImagePage(PdfDocument doc, string imagePath)
    {
        using var image = ImageTools.Load(imagePath);
        AddImagePage(doc, image, FileKinds.IsJpeg(Path.GetExtension(imagePath)));
    }

    static void AddImagePage(PdfDocument doc, MagickImage image, bool jpeg)
    {
        if (jpeg) image.Quality = 92;
        else ImageTools.FlattenOnWhite(image);

        using var stream = new MemoryStream();
        image.Write(stream, jpeg ? MagickFormat.Jpeg : MagickFormat.Png);
        stream.Position = 0;
        using var ximage = XImage.FromStream(stream);

        bool landscape = image.Width > image.Height;
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(landscape ? A4Height : A4Width);
        page.Height = XUnit.FromPoint(landscape ? A4Width : A4Height);

        double pageW = page.Width.Point, pageH = page.Height.Point;
        double scale = Math.Min((pageW - 2 * Margin) / image.Width, (pageH - 2 * Margin) / image.Height);
        double w = image.Width * scale, h = image.Height * scale;

        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(ximage, (pageW - w) / 2, (pageH - h) / 2, w, h);
    }
}
