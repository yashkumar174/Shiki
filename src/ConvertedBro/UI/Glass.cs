using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConvertedBro.Core;

namespace ConvertedBro.UI;

/// <summary>
/// Frosted glass for short-lived popups: a blurred snapshot of what's on screen behind the
/// popup, taken just before it appears, with the theme tint on top.
/// </summary>
public static class Glass
{
    /// <summary>Snapshot of a screen area in physical pixels. Null if the capture fails.</summary>
    public static BitmapSource? Capture(Int32Rect px)
    {
        if (px.Width <= 0 || px.Height <= 0) return null;
        try
        {
            using var bitmap = new System.Drawing.Bitmap(px.Width, px.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
                g.CopyFromScreen(px.X, px.Y, 0, 0, bitmap.Size);
            var hbitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Screen capture for glass failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Builds the glass layer. The snapshot covers the whole popup; <paramref name="clip"/> cuts out
    /// the visible shape, which keeps the blur's soft edges outside it.
    /// </summary>
    public static UIElement Build(BitmapSource? snapshot, Geometry clip, Palette palette, double blurRadius = 34)
    {
        var layer = new Grid { Clip = clip, IsHitTestVisible = false };
        if (snapshot != null)
        {
            var image = new Image
            {
                Source = snapshot,
                Stretch = Stretch.Fill,
                Effect = new BlurEffect { Radius = blurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            layer.Children.Add(image);
            layer.Children.Add(new Rectangle { Fill = Theme.Brush(palette.GlassTint) });
        }
        else
        {
            // No snapshot (e.g. a protected screen): fall back to a near-solid surface.
            layer.Children.Add(new Rectangle { Fill = Theme.Brush(palette.Surface) });
        }

        // A soft light falling from the top-left gives the glass some depth.
        layer.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                Color.FromArgb(palette.Dark ? (byte)0x1E : (byte)0x50, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255), new Point(0.15, 0), new Point(0.6, 0.8)),
        });
        return layer;
    }

    /// <summary>Converts a window-relative DIP rectangle to physical screen pixels.</summary>
    public static Int32Rect ToPixels(Visual reference, Rect dip)
    {
        var toDevice = PresentationSource.FromVisual(reference)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var topLeft = toDevice.Transform(dip.TopLeft);
        var bottomRight = toDevice.Transform(dip.BottomRight);
        return new Int32Rect((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X - topLeft.X), (int)Math.Round(bottomRight.Y - topLeft.Y));
    }

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
}
