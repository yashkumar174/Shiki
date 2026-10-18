using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConvertedBro.UI;

/// <summary>Look of one liquid glass surface. Lengths are in physical pixels.</summary>
public sealed record LiquidGlassStyle
{
    /// <summary>How far in from the edge the curved rim reaches.</summary>
    public double Bezel { get; init; } = 18;
    /// <summary>Largest sideways shift of the background, at the steepest part of the rim.</summary>
    public double Refraction { get; init; } = 20;
    /// <summary>Background blur radius; 0 is perfectly clear glass.</summary>
    public int Frost { get; init; } = 5;
    public double Saturation { get; init; } = 1.55;
    /// <summary>Red and blue split apart by this fraction of the shift (colour fringing at the rim).</summary>
    public double Aberration { get; init; } = 0.08;
    public Color Tint { get; init; } = Color.FromArgb(0x30, 0x10, 0x10, 0x18);
    public double Specular { get; init; } = 0.75;
    /// <summary>Where the light comes from, in degrees (screen coordinates, -125 = upper left).</summary>
    public double LightAngle { get; init; } = -125;
    /// <summary>Soft shadow under each piece, so glass still reads on a flat background.</summary>
    public double ShadowOpacity { get; init; } = 0.22;
    public double ShadowOffset { get; init; } = 5;
    public double ShadowBlur { get; init; } = 14;
}

/// <summary>
/// Apple-style "liquid glass", computed on the CPU from a snapshot of what's behind a popup.
/// Same optics as the open-source web versions (kube.io's write-up, liquidGL, liquid-glass-react):
/// <list type="number">
/// <item>The surface has a convex squircle bevel, height f(x) = (1 − (1 − x)⁴)^¼ across the rim.</item>
/// <item>A ray hitting the bevel bends by Snell's law (air 1.0 → glass 1.5); the sideways offset
/// tells us where to sample the background, so the edges bend the desktop and the middle stays clear.</item>
/// <item>The background is frosted slightly and its saturation lifted.</item>
/// <item>A specular rim light falls on the edges facing the light, a softer one on the far side.</item>
/// </list>
/// Shapes are signed distance fields: positive inside, in pixels to the nearest edge.
/// </summary>
public static class LiquidGlass
{
    const double IndexOfRefraction = 1.5;
    static readonly float[] BevelShift = BuildBevelLut(256);

    /// <summary>Renders glass over <paramref name="snapshot"/> for the shape described by <paramref name="sdf"/>.</summary>
    public static BitmapSource Render(BitmapSource snapshot, float[] sdf, LiquidGlassStyle style)
    {
        int w = snapshot.PixelWidth, h = snapshot.PixelHeight;
        var source = new byte[w * h * 4];
        new FormatConvertedBitmap(snapshot, PixelFormats.Bgra32, null, 0).CopyPixels(source, w * 4, 0);
        var frosted = style.Frost > 0 ? BoxBlur(source, w, h, style.Frost) : source;

        var output = new byte[w * h * 4];
        double lightAngle = style.LightAngle * Math.PI / 180;
        double lx = Math.Cos(lightAngle), ly = Math.Sin(lightAngle);
        float tintA = style.Tint.A / 255f, tintR = style.Tint.R / 255f, tintG = style.Tint.G / 255f, tintB = style.Tint.B / 255f;

        Parallel.For(1, h - 1, y =>
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                float d = sdf[i];
                // Drop shadow: how close the shape is if we look a little upward.
                float shadow = 0;
                if (d < 0.5f && style.ShadowOpacity > 0)
                {
                    int sy = Math.Clamp(y - (int)style.ShadowOffset, 0, h - 1);
                    double reach = Math.Clamp(1 + sdf[sy * w + x] / style.ShadowBlur, 0, 1);
                    shadow = (float)(style.ShadowOpacity * reach * reach);
                }
                if (d <= -1)
                {
                    if (shadow > 0.003f) output[i * 4 + 3] = (byte)(shadow * 255);
                    continue;
                }
                float coverage = Math.Clamp(d + 0.5f, 0, 1);

                // The distance field rises toward the inside, so its gradient points inward.
                float gx = sdf[i + 1] - sdf[i - 1], gy = sdf[i + w] - sdf[i - w];
                float len = MathF.Sqrt(gx * gx + gy * gy);
                if (len > 1e-4f) { gx /= len; gy /= len; } else { gx = gy = 0; }

                double t = Math.Clamp(d / style.Bezel, 0, 1);
                double shift = style.Refraction * BevelShift[(int)(t * (BevelShift.Length - 1))];

                // Sample further inside: the rim pulls in and bends what's behind it.
                double sr = shift * (1 - style.Aberration), sb = shift * (1 + style.Aberration);
                float r = Sample(frosted, w, h, x + gx * sr, y + gy * sr, 2);
                var (_, g, _) = SampleRgb(frosted, w, h, x + gx * shift, y + gy * shift);
                float b = Sample(frosted, w, h, x + gx * sb, y + gy * sb, 0);

                // Lift saturation, like Apple's material, then lay the tint over it.
                float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                float sat = (float)style.Saturation;
                r = Math.Clamp(luma + (r - luma) * sat, 0, 1);
                g = Math.Clamp(luma + (g - luma) * sat, 0, 1);
                b = Math.Clamp(luma + (b - luma) * sat, 0, 1);
                r += (tintR - r) * tintA;
                g += (tintG - g) * tintA;
                b += (tintB - b) * tintA;

                // Rim light: bright where the outward edge faces the light, softer on the opposite side.
                double facing = -(gx * lx + gy * ly);
                double rim = Math.Exp(-Math.Max(d, 0) / 1.9);
                double sheen = Math.Max(0, facing) * (1 - t) * 0.10;
                double light = (Math.Pow(Math.Max(0, facing), 1.6) * 0.9 + Math.Pow(Math.Max(0, -facing), 1.6) * 0.38) * rim * style.Specular + sheen;
                // A faint even rim keeps edges visible on any background.
                light += rim * 0.10;
                float lift = (float)Math.Min(1, light);
                r += (1 - r) * lift;
                g += (1 - g) * lift;
                b += (1 - b) * lift;

                // Premultiplied: glass where covered, its shadow showing through the anti-aliased edge.
                int o = i * 4;
                float alpha = coverage + shadow * (1 - coverage);
                output[o] = (byte)(b * coverage * 255);
                output[o + 1] = (byte)(g * coverage * 255);
                output[o + 2] = (byte)(r * coverage * 255);
                output[o + 3] = (byte)(alpha * 255);
            }
        });

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, output, w * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Sideways shift across the bevel (0 = outer edge, 1 = where the flat top starts), normalised to 1.
    /// Height follows a convex squircle; the ray bends by Snell's law at the local slope, and the shift
    /// is the glass thickness there times tan(θ₁ − θ₂).
    /// </summary>
    static float[] BuildBevelLut(int size)
    {
        static double Height(double x) => Math.Pow(1 - Math.Pow(1 - Math.Clamp(x, 0, 1), 4), 0.25);
        var lut = new float[size];
        double max = 0;
        for (int i = 0; i < size; i++)
        {
            double x = Math.Max(0.004, i / (double)(size - 1));
            const double dx = 0.002;
            double slope = (Height(x + dx) - Height(x - dx)) / (2 * dx);
            double theta1 = Math.Atan(Math.Abs(slope));
            double theta2 = Math.Asin(Math.Sin(theta1) / IndexOfRefraction);
            double thickness = 0.35 + 0.65 * Height(x);
            lut[i] = (float)(thickness * Math.Tan(theta1 - theta2));
            max = Math.Max(max, lut[i]);
        }
        for (int i = 0; i < size; i++) lut[i] = (float)(lut[i] / max);
        lut[^1] = 0;
        return lut;
    }

    static float Sample(byte[] px, int w, int h, double x, double y, int channel)
    {
        x = Math.Clamp(x, 0, w - 1.001);
        y = Math.Clamp(y, 0, h - 1.001);
        int x0 = (int)x, y0 = (int)y;
        double fx = x - x0, fy = y - y0;
        int i00 = (y0 * w + x0) * 4 + channel, i10 = i00 + 4, i01 = i00 + w * 4, i11 = i01 + 4;
        double top = px[i00] + (px[i10] - px[i00]) * fx;
        double bottom = px[i01] + (px[i11] - px[i01]) * fx;
        return (float)((top + (bottom - top) * fy) / 255);
    }

    static (float r, float g, float b) SampleRgb(byte[] px, int w, int h, double x, double y) =>
        (Sample(px, w, h, x, y, 2), Sample(px, w, h, x, y, 1), Sample(px, w, h, x, y, 0));

    /// <summary>Three box blurs in a row: close to a Gaussian, and cheap.</summary>
    static byte[] BoxBlur(byte[] src, int w, int h, int radius)
    {
        var a = (byte[])src.Clone();
        var b = new byte[src.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BlurLine(a, b, w, h, radius, horizontal: true);
            BlurLine(b, a, w, h, radius, horizontal: false);
        }
        return a;
    }

    static void BlurLine(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        Parallel.For(0, lines, line =>
        {
            int Index(int k) => horizontal ? (line * w + k) * 4 : (k * w + line) * 4;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0, span = 2 * r + 1;
                for (int k = -r; k <= r; k++) sum += src[Index(Math.Clamp(k, 0, length - 1)) + c];
                for (int k = 0; k < length; k++)
                {
                    dst[Index(k) + c] = (byte)(sum / span);
                    sum += src[Index(Math.Min(k + r + 1, length - 1)) + c] - src[Index(Math.Max(k - r, 0)) + c];
                }
            }
        });
    }
}

/// <summary>Signed distance fields (positive inside, in pixels) for the shapes our popups use.</summary>
public static class GlassShapes
{
    public static float[] Field(int w, int h, Func<double, double, double> distance)
    {
        var field = new float[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++) field[y * w + x] = (float)distance(x + 0.5, y + 0.5);
        });
        return field;
    }

    public static Func<double, double, double> Circle(Point c, double radius) =>
        (x, y) => radius - Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y));

    public static Func<double, double, double> RoundedRect(Rect r, double radius) => (x, y) =>
    {
        double qx = Math.Abs(x - (r.X + r.Width / 2)) - (r.Width / 2 - radius);
        double qy = Math.Abs(y - (r.Y + r.Height / 2)) - (r.Height / 2 - radius);
        double outside = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2)) + Math.Min(Math.Max(qx, qy), 0) - radius;
        return -outside;
    };

    /// <summary>A ring of rounded slices with even gaps (angles clockwise from 3 o'clock).</summary>
    public static Func<double, double, double> Wedges(Point c, double inner, double outer, int count, double firstStart, double gap, double corner)
    {
        double step = Math.PI * 2 / count;
        return (x, y) =>
        {
            double dx = x - c.X, dy = y - c.Y, r = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx);
            if (count == 1) return Math.Min(r - inner, outer - r);
            int nearest = (int)Math.Floor(Wrap(angle - firstStart) / step);
            double best = double.NegativeInfinity;
            // The point can only be near its own slice or a neighbour.
            for (int k = -1; k <= 1; k++)
            {
                int index = ((nearest + k) % count + count) % count;
                double start = firstStart + index * step;
                best = Math.Max(best, Slice(r, angle, start, start + step, inner, outer, gap, corner));
            }
            return best;
        };
    }

    public static Func<double, double, double> Union(params Func<double, double, double>[] shapes) =>
        (x, y) =>
        {
            double best = double.NegativeInfinity;
            foreach (var shape in shapes) best = Math.Max(best, shape(x, y));
            return best;
        };

    static double Slice(double r, double angle, double start, double end, double inner, double outer, double gap, double corner)
    {
        double fromStart = Signed(angle - start), toEnd = Signed(end - angle);
        double d0 = r - inner, d1 = outer - r;
        double d2 = r * Math.Sin(Math.Clamp(fromStart, -Math.PI / 2, Math.PI / 2)) - gap / 2;
        double d3 = r * Math.Sin(Math.Clamp(toEnd, -Math.PI / 2, Math.PI / 2)) - gap / 2;
        // The two nearest edges (smallest distances), without allocating per pixel.
        double a = double.MaxValue, b = double.MaxValue;
        foreach (var v in (ReadOnlySpan<double>)[d0, d1, d2, d3])
        {
            if (v < a) { b = a; a = v; }
            else if (v < b) b = v;
        }
        // Round the corner where the two nearest edges meet.
        if (a >= 0 && b < corner) return corner - Math.Sqrt((corner - a) * (corner - a) + (corner - b) * (corner - b));
        return a;
    }

    static double Wrap(double a) => (a % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
    static double Signed(double a) { a = Wrap(a); return a > Math.PI ? a - Math.PI * 2 : a; }
}
