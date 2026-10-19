using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ConvertedBro.Core;

/// <summary>Copies the text out of images and PDFs with the OCR engine built into Windows.</summary>
public static class TextTools
{
    public static async Task<IReadOnlyList<string>> CopyText(IReadOnlyList<string> inputs, IProgress<double> progress, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Windows text recognition isn't set up. Add a language under Settings > Time & language > Language.");

        var text = new StringBuilder();
        for (int i = 0; i < inputs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var input = inputs[i];
            if (FileKinds.Of(input) == FileKind.Pdf)
            {
                var pages = PdfTools.RenderPages(input, 2.5, new Progress<double>());
                try
                {
                    foreach (var page in pages) Append(text, await Recognize(engine, page.Image));
                }
                finally
                {
                    foreach (var page in pages) page.Image.Dispose();
                }
            }
            else
            {
                using var image = ImageTools.Load(input);
                Append(text, await Recognize(engine, image));
            }
            progress.Report((i + 1) / (double)inputs.Count);
        }

        var result = text.ToString().Trim();
        if (result.Length == 0)
        {
            JobNotes.Say("No text found");
            return [];
        }
        SetClipboard(result);
        int words = result.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        JobNotes.Say($"Copied {words} {(words == 1 ? "word" : "words")} to the clipboard");
        return [];
    }

    static void Append(StringBuilder text, string page)
    {
        if (page.Length == 0) return;
        if (text.Length > 0) text.AppendLine().AppendLine();
        text.Append(page);
    }

    static async Task<string> Recognize(OcrEngine engine, MagickImage source)
    {
        using var image = (MagickImage)source.Clone();
        ImageTools.FlattenOnWhite(image);
        uint max = OcrEngine.MaxImageDimension;
        if (image.Width > max || image.Height > max) image.Resize(new MagickGeometry(max, max));
        // Small screenshots read much better a little larger.
        if (image.Height < 700 && image.Width < 1400) image.Resize(new Percentage(200));

        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        using var random = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(random);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return Layout(result.Lines
            .Where(line => line.Words.Count > 0)
            .Select(line =>
            {
                var boxes = line.Words.Select(w => w.BoundingRect).ToList();
                double left = boxes.Min(b => b.X), top = boxes.Min(b => b.Y);
                return new TextLine(line.Text, left, top, boxes.Max(b => b.X + b.Width) - left, boxes.Max(b => b.Y + b.Height) - top);
            })
            .ToList());
    }

    record TextLine(string Text, double X, double Y, double Width, double Height)
    {
        public double Bottom => Y + Height;
        public double Right => X + Width;
    }

    /// <summary>
    /// Puts recognised lines back in reading order. Windows OCR returns blocks separately, so a
    /// list's numbers ("1.", "2.") come out as their own column before the text. Lines that sit on
    /// the same row are joined left to right; a clearly bigger gap between rows becomes a blank line.
    /// </summary>
    static string Layout(List<TextLine> lines)
    {
        if (lines.Count == 0) return "";
        var rows = new List<List<TextLine>>();
        foreach (var line in lines.OrderBy(l => l.Y + l.Height / 2))
        {
            // Same row when most of the shorter line overlaps the row vertically.
            var row = rows.FirstOrDefault(r =>
            {
                double top = r.Min(l => l.Y), bottom = r.Max(l => l.Bottom);
                double overlap = Math.Min(bottom, line.Bottom) - Math.Max(top, line.Y);
                return overlap > 0.5 * Math.Min(bottom - top, line.Height);
            });
            if (row == null) rows.Add([line]);
            else row.Add(line);
        }
        rows.Sort((a, b) => a.Min(l => l.Y).CompareTo(b.Min(l => l.Y)));

        double typical = Median(rows.Select(r => r.Max(l => l.Height)));
        var text = new StringBuilder();
        double? previousBottom = null;
        foreach (var row in rows)
        {
            var ordered = row.OrderBy(l => l.X).ToList();
            double top = ordered.Min(l => l.Y);
            // Lists and chat messages space their lines a little apart; only a real gap is a new paragraph.
            if (previousBottom is { } prev && top - prev > typical * 1.6) text.AppendLine();

            var rowText = new StringBuilder(ordered[0].Text);
            for (int i = 1; i < ordered.Count; i++)
            {
                // A wide gap means a separate column (a table, say): keep it apart with a tab.
                double gap = ordered[i].X - ordered[i - 1].Right;
                rowText.Append(gap > typical * 2.5 ? "\t" : " ").Append(ordered[i].Text);
            }
            text.AppendLine(rowText.ToString());
            previousBottom = row.Max(l => l.Bottom);
        }
        return text.ToString().TrimEnd();
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    static void SetClipboard(string text) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // Another app may be holding the clipboard for a moment; try a few times.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    return;
                }
                catch (COMException)
                {
                    Thread.Sleep(80);
                }
            }
            throw new InvalidOperationException("Another app is holding the clipboard. Try again.");
        });
}
