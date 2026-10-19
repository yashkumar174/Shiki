using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConvertedBro.Core;
using ImageMagick;

namespace ConvertedBro.UI;

/// <summary>Your saved signature: a transparent PNG kept in the app's data folder.</summary>
public static class SignatureStore
{
    public static string Path => System.IO.Path.Combine(Log.DataDir, "signature.png");
    public static bool Exists => File.Exists(Path);

    public static BitmapSource? Load()
    {
        if (!Exists) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(Path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Turns a photo of a signature on paper into ink on a transparent background.</summary>
    public static void ImportPhoto(string file)
    {
        using var image = ImageTools.Load(file);
        image.Grayscale();
        // Whiten the paper (same trick as the scanner), then keep only the dark strokes.
        using (var background = (MagickImage)image.Clone())
        {
            background.Resize(new Percentage(12.5));
            background.Blur(0, 6);
            background.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
            image.Composite(background, CompositeOperator.DivideSrc);
        }
        image.Level(new Percentage(35), new Percentage(85));

        // Ink darkness becomes opacity (smooth edges); crop to the ink with a little room around it.
        int w = (int)image.Width, h = (int)image.Height;
        var gray = image.GetPixels().ToByteArray("R")!;
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (gray[y * w + x] < 200)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        if (right < 0) throw new InvalidOperationException("No ink found in the photo.");
        const int pad = 8;
        left = Math.Max(0, left - pad); top = Math.Max(0, top - pad);
        right = Math.Min(w - 1, right + pad); bottom = Math.Min(h - 1, bottom + pad);

        int cw = right - left + 1, ch = bottom - top + 1;
        var rgba = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int o = (y * cw + x) * 4;
                rgba[o] = 18; rgba[o + 1] = 22; rgba[o + 2] = 40; // near-black blue ink
                rgba[o + 3] = (byte)(255 - gray[(y + top) * w + (x + left)]);
            }
        using var ink = new MagickImage();
        ink.ReadPixels(rgba, new PixelReadSettings((uint)cw, (uint)ch, StorageType.Char, PixelMapping.RGBA));
        ink.Write(Path, MagickFormat.Png);
    }

    public static void Save(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path);
        encoder.Save(stream);
    }
}

/// <summary>A pad to draw your signature with the mouse, pen or finger.</summary>
public sealed class SignaturePadWindow : Window
{
    readonly InkCanvas _ink = new() { Background = Brushes.White, Height = 220, Cursor = Cursors.Pen };
    readonly TextBlock _error = UiKit.Error();
    Color _color = Color.FromRgb(0x14, 0x2C, 0x7A);

    public bool Saved { get; private set; }

    public SignaturePadWindow()
    {
        Title = "Your signature";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);

        SetPen();
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        stack.Children.Add(UiKit.Title("Draw your signature"));
        stack.Children.Add(UiKit.Body("Sign in the box. It's saved on this PC for next time; nothing is uploaded."));
        var pad = new Border { Margin = new Thickness(0, 16, 0, 0), BorderThickness = new Thickness(2), BorderBrush = Theme.Brush(Theme.Current.SurfaceBorder), Child = _ink };
        stack.Children.Add(pad);

        var inks = UiKit.Row(
            Swatch(Color.FromRgb(0x14, 0x2C, 0x7A), "Blue"),
            Swatch(Color.FromRgb(0x10, 0x10, 0x14), "Black"),
            UiKit.Button("Clear", () => _ink.Strokes.Clear()));
        inks.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(inks);
        stack.Children.Add(_error);

        var buttons = UiKit.Row(UiKit.Button("Save signature", Save, primary: true), UiKit.Button("Use a photo instead…", ImportPhoto), UiKit.Button("Cancel", Close));
        buttons.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(buttons);
        Content = stack;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    Border Swatch(Color color, string name)
    {
        var chip = UiKit.Button(name, () =>
        {
            _color = color;
            SetPen();
            foreach (var stroke in _ink.Strokes) stroke.DrawingAttributes.Color = color;
        });
        ((TextBlock)chip.Child).Foreground = Theme.Brush(Theme.Lighten(color, 0.55));
        return chip;
    }

    void SetPen() => _ink.DefaultDrawingAttributes = new DrawingAttributes
    {
        Color = _color,
        Width = 3.2,
        Height = 3.2,
        FitToCurve = true,
        IgnorePressure = false,
    };

    void Save()
    {
        if (_ink.Strokes.Count == 0)
        {
            _error.Text = "Draw your signature first.";
            _error.Visibility = Visibility.Visible;
            return;
        }
        // Render just the ink, three times larger, on a transparent background.
        var bounds = _ink.Strokes.GetBounds();
        bounds.Inflate(6, 6);
        const double scale = 3;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            _ink.Strokes.Draw(dc);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        SignatureStore.Save(bitmap);
        Saved = true;
        Close();
    }

    void ImportPhoto()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Photo of your signature on white paper", Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.heic;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SignatureStore.ImportPhoto(dialog.FileName);
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Signature photo import failed", ex);
            _error.Text = "Couldn't read that photo. Try a clearer one on plain white paper.";
            _error.Visibility = Visibility.Visible;
        }
    }
}
