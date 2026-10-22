using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ImageMagick;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace Shiki.Core;

/// <summary>A photo or signature size that forms ask for.</summary>
public sealed record FormPreset(string Id, string Name, string Detail, double WidthMm, double HeightMm, int Dpi, int MinKb, int MaxKb, bool Signature = false)
{
    public int WidthPx => (int)Math.Round(WidthMm / 25.4 * Dpi);
    public int HeightPx => (int)Math.Round(HeightMm / 25.4 * Dpi);
    public double Aspect => WidthMm / HeightMm;
    public string SizeText => MinKb > 0 ? $"{MinKb}–{MaxKb} KB" : $"under {MaxKb} KB";
}

/// <summary>Crops, sizes and squeezes photos and signatures to what exam and visa forms require.</summary>
public static class FormPhotos
{
    public static IReadOnlyList<FormPreset> Presets { get; } =
    [
        new("exam", "Exam photo", "3.5 × 4.5 cm", 35, 45, 200, 20, 50),
        new("passport", "Passport", "2 × 2 in", 50.8, 50.8, 300, 0, 240),
        new("visa", "Visa", "35 × 45 mm", 35, 45, 300, 0, 300),
        new("signature", "Signature", "3.5 × 1.5 cm", 35, 15, 200, 10, 20, Signature: true),
    ];

    /// <summary>
    /// Where to crop, in photo pixels. Photos: centre on the face with passport-style headroom (the
    /// face takes about half the height). Signatures: the ink, padded to the preset's shape.
    /// </summary>
    public static async Task<Rect> SuggestCrop(MagickImage photo, FormPreset preset)
    {
        var area = new Rect(0, 0, photo.Width, photo.Height);
        Rect target;
        if (preset.Signature)
        {
            target = InkBounds(photo) ?? area;
            target.Inflate(target.Width * 0.06, target.Height * 0.15);
        }
        else if (await FindFace(photo) is { } face)
        {
            double height = face.Height / 0.5;
            target = new Rect(face.X + face.Width / 2 - height * preset.Aspect / 2, face.Y + face.Height / 2 - height * 0.47, height * preset.Aspect, height);
        }
        else
        {
            target = area;
        }
        return FitAspect(target, preset.Aspect, area);
    }

    /// <summary>Grows the box to the preset's shape around its centre, then keeps it inside the photo.</summary>
    public static Rect FitAspect(Rect box, double aspect, Rect bounds)
    {
        double w = box.Width, h = box.Height;
        if (w / h < aspect) w = h * aspect; else h = w / aspect;
        if (w > bounds.Width) { w = bounds.Width; h = w / aspect; }
        if (h > bounds.Height) { h = bounds.Height; w = h * aspect; }
        double x = Math.Clamp(box.X + box.Width / 2 - w / 2, bounds.X, bounds.Right - w);
        double y = Math.Clamp(box.Y + box.Height / 2 - h / 2, bounds.Y, bounds.Bottom - h);
        return new Rect(x, y, w, h);
    }

    /// <summary>Makes the final JPG: crop, resize, set DPI, and land inside the preset's size range.</summary>
    /// <param name="whiteBackground">Replace whatever is behind the person with plain white.</param>
    public static string Produce(string input, Rect crop, FormPreset preset, bool whiteBackground = false)
    {
        using var image = Cropped(input, crop, preset.Signature ? false : whiteBackground);
        if (preset.Signature) CleanSignature(image);
        image.FilterType = FilterType.Lanczos;
        image.Resize(new MagickGeometry((uint)preset.WidthPx, (uint)preset.HeightPx) { IgnoreAspectRatio = true });
        image.Strip();
        image.Density = new Density(preset.Dpi, preset.Dpi, DensityUnit.PixelsPerInch);

        long max = preset.MaxKb * 1024L, min = preset.MinKb * 1024L;
        byte[] best = Encode(image, 95);
        // Highest quality that fits under the maximum.
        int lo = 30, hi = 95;
        while (lo <= hi)
        {
            int q = (lo + hi) / 2;
            var data = Encode(image, q);
            if (data.Length <= max) { best = data; lo = q + 1; }
            else hi = q - 1;
        }
        if (best.Length < min)
        {
            // A small, simple picture can't reach the minimum on its own; forms only check the
            // file size, so pad it with an empty comment. The picture itself doesn't change.
            image.Quality = 95;
            image.SetAttribute("comment", new string(' ', (int)(min - best.Length + 512)));
            best = image.ToByteArray(MagickFormat.Jpeg);
            JobNotes.Say($"Padded to {best.Length / 1024} KB to meet the {preset.MinKb} KB minimum");
        }
        else if (best.Length > max)
        {
            JobNotes.Say($"Couldn't get under {preset.MaxKb} KB; the smallest is {best.Length / 1024} KB");
        }

        var output = OutputPaths.Next(input, ".jpg", preset.Name.ToLowerInvariant());
        File.WriteAllBytes(output, best);
        return output;
    }

    static MagickImage Cropped(string input, Rect crop, bool whiteBackground)
    {
        var image = ImageTools.Load(input);
        image.Crop(new MagickGeometry((int)crop.X, (int)crop.Y, (uint)Math.Max(1, crop.Width), (uint)Math.Max(1, crop.Height)));
        image.Page = new MagickGeometry(0, 0, image.Width, image.Height);
        if (whiteBackground)
        {
            Cutout.EnsureModel(new Progress<double>(), System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            var cut = Cutout.Remove(image);
            image.Dispose();
            image = cut;
            JobNotes.Say("Background made white");
        }
        ImageTools.FlattenOnWhite(image);
        return image;
    }

    static byte[] Encode(MagickImage image, int quality)
    {
        image.Quality = (uint)quality;
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>White paper, crisp dark ink: the way forms want a signature to look.</summary>
    static void CleanSignature(MagickImage image)
    {
        image.Grayscale();
        using var background = (MagickImage)image.Clone();
        background.Resize(new Percentage(12.5));
        background.Blur(0, 6);
        background.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
        image.Composite(background, CompositeOperator.DivideSrc);
        image.Level(new Percentage(30), new Percentage(85));
    }

    /// <summary>The ink's bounding box in a photo of a signature, or null if nothing dark is there.</summary>
    static Rect? InkBounds(MagickImage photo)
    {
        using var gray = (MagickImage)photo.Clone();
        double scale = Math.Min(1, 800.0 / Math.Max(gray.Width, gray.Height));
        if (scale < 1) gray.Resize(new Percentage(scale * 100));
        CleanSignature(gray);
        int w = (int)gray.Width, h = (int)gray.Height;
        var px = gray.GetPixels().ToByteArray("R")!;
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x] < 140)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        if (right < 0) return null;
        return new Rect(left / scale, top / scale, (right - left + 1) / scale, (bottom - top + 1) / scale);
    }

    /// <summary>The largest face in the photo, using the face detector built into Windows (offline).</summary>
    static async Task<Rect?> FindFace(MagickImage photo)
    {
        try
        {
            using var small = (MagickImage)photo.Clone();
            double scale = Math.Min(1, 1200.0 / Math.Max(small.Width, small.Height));
            if (scale < 1) small.Resize(new Percentage(scale * 100));
            ImageTools.FlattenOnWhite(small);

            using var stream = new MemoryStream(small.ToByteArray(MagickFormat.Png));
            using var random = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(random);
            using var color = await decoder.GetSoftwareBitmapAsync();
            using var gray = SoftwareBitmap.Convert(color, BitmapPixelFormat.Gray8);
            var detector = await FaceDetector.CreateAsync();
            var faces = await detector.DetectFacesAsync(gray);
            var face = faces.OrderByDescending(f => f.FaceBox.Width * f.FaceBox.Height).FirstOrDefault();
            if (face == null) return null;
            var box = face.FaceBox;
            return new Rect(box.X / scale, box.Y / scale, box.Width / scale, box.Height / scale);
        }
        catch (Exception ex)
        {
            Log.Error("Face detection failed", ex);
            return null;
        }
    }
}
