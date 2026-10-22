using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ImageMagick;

namespace Shiki.Core;

public enum ScanFilter { Color, BlackWhite, Original }

/// <summary>
/// Turns phone photos of paper into clean scans: find the page, straighten it, whiten the paper.
/// Corners are in the photo's pixel coordinates (after camera rotation), in the order
/// top-left, top-right, bottom-right, bottom-left.
/// </summary>
public static class ScanTools
{
    const int DetectSize = 480;

    /// <summary>
    /// Guesses where the page is: paper is bright and nearly colourless, so score every pixel on that,
    /// split with Otsu's threshold, keep the biggest blob, and take its four extreme corners.
    /// Falls back to the whole photo when nothing page-like stands out.
    /// </summary>
    public static Point[] DetectPage(MagickImage photo)
    {
        using var small = (MagickImage)photo.Clone();
        small.Resize(new MagickGeometry(DetectSize, DetectSize));
        int w = (int)small.Width, h = (int)small.Height;
        var rgb = small.GetPixels().ToByteArray(PixelMapping.RGB)!;

        var score = new byte[w * h];
        var histogram = new int[256];
        for (int i = 0; i < w * h; i++)
        {
            int r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            int s = Math.Clamp(max - (int)((max - min) * 0.9), 0, 255);
            score[i] = (byte)s;
            histogram[s]++;
        }

        int threshold = Otsu(histogram, w * h);
        var mask = new bool[w * h];
        for (int i = 0; i < mask.Length; i++) mask[i] = score[i] > threshold;
        // Close small holes (text, shadows) so the page is one solid blob.
        mask = Erode(Dilate(mask, w, h, 2), w, h, 2);

        var blob = LargestBlob(mask, w, h);
        double factor = photo.Width / (double)w;
        if (blob.Count < w * h * 0.12) return FullImage(photo);

        Point tl = default, tr = default, br = default, bl = default;
        double minSum = double.MaxValue, maxSum = double.MinValue, minDiff = double.MaxValue, maxDiff = double.MinValue;
        foreach (int i in blob)
        {
            int x = i % w, y = i / w;
            if (x + y < minSum) { minSum = x + y; tl = new Point(x, y); }
            if (x + y > maxSum) { maxSum = x + y; br = new Point(x, y); }
            if (x - y > maxDiff) { maxDiff = x - y; tr = new Point(x, y); }
            if (x - y < minDiff) { minDiff = x - y; bl = new Point(x, y); }
        }
        var quad = new[] { tl, tr, br, bl }.Select(p => new Point((p.X + 0.5) * factor, (p.Y + 0.5) * factor)).ToArray();
        // Pull the corners in a little: the blob's rim often includes a sliver of table, which the
        // whitening would turn into a dark smudge along the edge.
        var centre = new Point(quad.Average(p => p.X), quad.Average(p => p.Y));
        double inset = Math.Min(photo.Width, photo.Height) * 0.008;
        quad = quad.Select(p =>
        {
            var v = centre - p;
            return p + v / v.Length * inset;
        }).ToArray();
        // A page that covers almost nothing is probably a wrong guess.
        return Area(quad) < photo.Width * photo.Height * 0.12 ? FullImage(photo) : quad;
    }

    public static Point[] FullImage(MagickImage photo) =>
        [new(0, 0), new(photo.Width, 0), new(photo.Width, photo.Height), new(0, photo.Height)];

    /// <summary>Straightens the page inside <paramref name="corners"/>, turns it, and applies the filter.</summary>
    public static MagickImage Process(MagickImage photo, Point[] corners, int rotation, ScanFilter filter)
    {
        var image = (MagickImage)photo.Clone();
        var (tl, tr, br, bl) = (corners[0], corners[1], corners[2], corners[3]);
        double width = (Distance(tl, tr) + Distance(bl, br)) / 2;
        double height = (Distance(tl, bl) + Distance(tr, br)) / 2;
        uint w = (uint)Math.Max(1, Math.Round(width)), h = (uint)Math.Max(1, Math.Round(height));

        bool whole = corners.SequenceEqual(FullImage(photo));
        if (!whole)
        {
            image.VirtualPixelMethod = VirtualPixelMethod.White;
            image.Distort(new DistortSettings(DistortMethod.Perspective) { Viewport = new MagickGeometry(0, 0, w, h) },
                tl.X, tl.Y, 0, 0,
                tr.X, tr.Y, w, 0,
                br.X, br.Y, w, h,
                bl.X, bl.Y, 0, h);
            image.Page = new MagickGeometry(0, 0, w, h);
        }
        if (rotation % 360 != 0) image.Rotate(rotation);

        if (filter != ScanFilter.Original) Whiten(image);
        if (filter == ScanFilter.Color)
        {
            image.Level(new Percentage(6), new Percentage(96));
            image.Modulate(new Percentage(100), new Percentage(125), new Percentage(100));
            image.Sharpen(0, 1);
        }
        else if (filter == ScanFilter.BlackWhite)
        {
            image.Grayscale();
            image.Level(new Percentage(28), new Percentage(86));
            image.Sharpen(0, 1.2);
        }
        return image;
    }

    /// <summary>
    /// Evens out lighting and shadows: divide the page by a very blurred copy of itself, so the paper
    /// becomes white everywhere and ink stays dark.
    /// </summary>
    static void Whiten(MagickImage image)
    {
        using var background = (MagickImage)image.Clone();
        background.Resize(new Percentage(12.5));
        background.Blur(0, 6);
        background.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
        image.Composite(background, CompositeOperator.DivideSrc);
    }

    public static IReadOnlyList<string> SaveImages(IReadOnlyList<(string Source, MagickImage Page)> pages)
    {
        var outputs = new List<string>();
        foreach (var (source, page) in pages)
        {
            var output = OutputPaths.Next(source, ".jpg", "scan");
            page.Quality = 90;
            page.Write(output, MagickFormat.Jpeg);
            outputs.Add(output);
        }
        return outputs;
    }

    public static string SavePdf(IReadOnlyList<(string Source, MagickImage Page)> pages) =>
        PdfTools.ImagesToPdf(pages.Select(p => p.Page).ToList(), OutputPaths.Next(pages[0].Source, ".pdf", "scan"));

    // ---- Helpers ----

    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    static double Area(Point[] q)
    {
        double sum = 0;
        for (int i = 0; i < q.Length; i++)
        {
            var a = q[i];
            var b = q[(i + 1) % q.Length];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2;
    }

    static int Otsu(int[] histogram, int total)
    {
        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * histogram[i];
        double sumB = 0, best = 0;
        int weightB = 0, threshold = 128;
        for (int t = 0; t < 256; t++)
        {
            weightB += histogram[t];
            if (weightB == 0) continue;
            int weightF = total - weightB;
            if (weightF == 0) break;
            sumB += t * histogram[t];
            double meanB = sumB / weightB, meanF = (sum - sumB) / weightF;
            double between = (double)weightB * weightF * (meanB - meanF) * (meanB - meanF);
            if (between > best) { best = between; threshold = t; }
        }
        return threshold;
    }

    static bool[] Dilate(bool[] m, int w, int h, int times) => Morph(m, w, h, times, grow: true);
    static bool[] Erode(bool[] m, int w, int h, int times) => Morph(m, w, h, times, grow: false);

    static bool[] Morph(bool[] mask, int w, int h, int times, bool grow)
    {
        for (int t = 0; t < times; t++)
        {
            var next = new bool[mask.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = !grow;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = Math.Clamp(x + dx, 0, w - 1), ny = Math.Clamp(y + dy, 0, h - 1);
                            bool n = mask[ny * w + nx];
                            v = grow ? v || n : v && n;
                        }
                    next[y * w + x] = v;
                }
            mask = next;
        }
        return mask;
    }

    static List<int> LargestBlob(bool[] mask, int w, int h)
    {
        var seen = new bool[mask.Length];
        var best = new List<int>();
        var queue = new Queue<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start]) continue;
            var blob = new List<int>();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                blob.Add(i);
                int x = i % w, y = i / w;
                foreach (int n in (ReadOnlySpan<int>)[x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1])
                {
                    if (n < 0 || seen[n] || !mask[n]) continue;
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }
            if (blob.Count > best.Count) best = blob;
        }
        return best;
    }
}
