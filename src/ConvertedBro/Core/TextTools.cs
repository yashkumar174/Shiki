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
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
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
