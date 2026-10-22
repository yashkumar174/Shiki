using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiki.UI;

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
    internal static readonly float[] BevelShift = BuildBevelLut(256);

    /// <summary>One-off render of glass over <paramref name="snapshot"/> for the shape in <paramref name="sdf"/>.</summary>
    public static BitmapSource Render(BitmapSource snapshot, float[] sdf, LiquidGlassStyle style) =>
        GlassPlan.Create(sdf, snapshot.PixelWidth, snapshot.PixelHeight, style).Render(snapshot);

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

    /// <summary>Bilinear sample of one channel at a pixel position that's already clamped.</summary>
    internal static float Sample(byte[] px, int w, float x, float y, int channel)
    {
        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        int i00 = (y0 * w + x0) * 4 + channel, i10 = i00 + 4, i01 = i00 + w * 4, i11 = i01 + 4;
        float top = px[i00] + (px[i10] - px[i00]) * fx;
        float bottom = px[i01] + (px[i11] - px[i01]) * fx;
        return (top + (bottom - top) * fy) * (1f / 255);
    }

    /// <summary>Three box blurs in a row: close to a Gaussian, and cheap. Uses the given buffers.</summary>
    internal static byte[] BoxBlur(byte[] src, int w, int h, int radius, byte[] a, byte[] b)
    {
        Buffer.BlockCopy(src, 0, a, 0, src.Length);
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
        int step = horizontal ? 4 : w * 4;
        int span = 2 * r + 1;
        Parallel.For(0, lines, line =>
        {
            int start = horizontal ? line * w * 4 : line * 4;
            int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (int k = -r; k <= r; k++)
            {
                int i = start + Math.Clamp(k, 0, length - 1) * step;
                s0 += src[i]; s1 += src[i + 1]; s2 += src[i + 2]; s3 += src[i + 3];
            }
            for (int k = 0; k < length; k++)
            {
                int o = start + k * step;
                dst[o] = (byte)(s0 / span); dst[o + 1] = (byte)(s1 / span); dst[o + 2] = (byte)(s2 / span); dst[o + 3] = (byte)(s3 / span);
                int add = start + Math.Min(k + r + 1, length - 1) * step, sub = start + Math.Max(k - r, 0) * step;
                s0 += src[add] - src[sub]; s1 += src[add + 1] - src[sub + 1]; s2 += src[add + 2] - src[sub + 2]; s3 += src[add + 3] - src[sub + 3];
            }
        });
    }

    /// <summary>Halves an image by averaging 2×2 blocks.</summary>
    internal static void Half(byte[] src, int w, int h, byte[] dst, int w2, int h2)
    {
        Parallel.For(0, h2, y =>
        {
            int y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
            for (int x = 0; x < w2; x++)
            {
                int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1);
                int a = (y0 * w + x0) * 4, b = (y0 * w + x1) * 4, c = (y1 * w + x0) * 4, d = (y1 * w + x1) * 4, o = (y * w2 + x) * 4;
                for (int ch = 0; ch < 4; ch++) dst[o + ch] = (byte)((src[a + ch] + src[b + ch] + src[c + ch] + src[d + ch] + 2) >> 2);
            }
        });
    }
}

/// <summary>
/// Everything about a glass surface that depends only on its shape, worked out once: where each
/// pixel samples the background (refraction, colour fringing), its coverage, rim light and shadow.
/// Applying it to a fresh snapshot is then cheap enough to do many times a second.
/// </summary>
public sealed class GlassPlan
{
    public int Width { get; }
    public int Height { get; }

    readonly LiquidGlassStyle _style;
    readonly int[] _pixel;              // glass pixels (index into the image)
    readonly float[] _coverage, _lift;  // per glass pixel
    readonly float[] _sample;           // per glass pixel: rx, ry, gx, gy, bx, by
    readonly int[] _shadowPixel;        // pixels outside the glass that only carry shadow
    readonly byte[] _shadowAlpha;
    byte[]? _half, _blurA, _blurB;

    GlassPlan(int w, int h, LiquidGlassStyle style, List<int> pixel, List<float> coverage, List<float> lift, List<float> sample,
        List<int> shadowPixel, List<byte> shadowAlpha)
    {
        Width = w;
        Height = h;
        _style = style;
        _pixel = pixel.ToArray();
        _coverage = coverage.ToArray();
        _lift = lift.ToArray();
        _sample = sample.ToArray();
        _shadowPixel = shadowPixel.ToArray();
        _shadowAlpha = shadowAlpha.ToArray();
    }

    public static GlassPlan Create(float[] sdf, int w, int h, LiquidGlassStyle style)
    {
        var pixel = new List<int>();
        var coverage = new List<float>();
        var lift = new List<float>();
        var sample = new List<float>();
        var shadowPixel = new List<int>();
        var shadowAlpha = new List<byte>();

        double lightAngle = style.LightAngle * Math.PI / 180;
        double lx = Math.Cos(lightAngle), ly = Math.Sin(lightAngle);
        var lut = LiquidGlass.BevelShift;
        float maxX = w - 1.001f, maxY = h - 1.001f;

        for (int y = 1; y < h - 1; y++)
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
                    if (shadow > 0.003f) { shadowPixel.Add(i); shadowAlpha.Add((byte)(shadow * 255)); }
                    continue;
                }

                // The distance field rises toward the inside, so its gradient points inward.
                float gx = sdf[i + 1] - sdf[i - 1], gy = sdf[i + w] - sdf[i - w];
                float len = MathF.Sqrt(gx * gx + gy * gy);
                if (len > 1e-4f) { gx /= len; gy /= len; } else { gx = gy = 0; }

                double t = Math.Clamp(d / style.Bezel, 0, 1);
                double shift = style.Refraction * lut[(int)(t * (lut.Length - 1))];
                // Sample further inside: the rim pulls in and bends what's behind it. Red and blue
                // bend a little less and more than green, for a hint of colour fringing.
                foreach (double s in (ReadOnlySpan<double>)[shift * (1 - style.Aberration), shift, shift * (1 + style.Aberration)])
                {
                    sample.Add(Math.Clamp((float)(x + gx * s), 0, maxX));
                    sample.Add(Math.Clamp((float)(y + gy * s), 0, maxY));
                }

                // Rim light: bright where the outward edge faces the light, softer on the opposite side.
                double facing = -(gx * lx + gy * ly);
                double rim = Math.Exp(-Math.Max(d, 0) / 1.9);
                double sheen = Math.Max(0, facing) * (1 - t) * 0.10;
                double light = (Math.Pow(Math.Max(0, facing), 1.6) * 0.9 + Math.Pow(Math.Max(0, -facing), 1.6) * 0.38) * rim * style.Specular + sheen;
                // A faint even rim keeps edges visible on any background.
                light += rim * 0.10;

                float cover = Math.Clamp(d + 0.5f, 0, 1);
                pixel.Add(i);
                coverage.Add(cover);
                lift.Add((float)Math.Min(1, light));
                // Where the edge is anti-aliased, the shadow shows through: fold it into coverage's alpha.
                shadowPixel.Add(-1 - (pixel.Count - 1));
                shadowAlpha.Add((byte)(Math.Clamp(cover + shadow * (1 - cover), 0, 1) * 255));
            }
        }
        return new GlassPlan(w, h, style, pixel, coverage, lift, sample, shadowPixel, shadowAlpha);
    }

    /// <summary>Renders glass for <paramref name="snapshot"/> (BGRA, this plan's size) into premultiplied <paramref name="output"/>.</summary>
    public void Apply(byte[] snapshot, byte[] output)
    {
        // Frost is a blur, so it can be done at half size: a quarter of the work, and it looks the same.
        int w = Width;
        byte[] frosted = snapshot;
        float scale = 1, maxX = w - 1.001f, maxY = Height - 1.001f;
        if (_style.Frost > 0)
        {
            int w2 = (w + 1) / 2, h2 = (Height + 1) / 2;
            _half ??= new byte[w2 * h2 * 4];
            _blurA ??= new byte[_half.Length];
            _blurB ??= new byte[_half.Length];
            LiquidGlass.Half(snapshot, w, Height, _half, w2, h2);
            frosted = LiquidGlass.BoxBlur(_half, w2, h2, Math.Max(1, _style.Frost / 2), _blurA, _blurB);
            w = w2;
            scale = 0.5f;
            maxX = w2 - 1.001f;
            maxY = h2 - 1.001f;
        }

        float sat = (float)_style.Saturation;
        var tint = _style.Tint;
        float tintA = tint.A / 255f, tintR = tint.R / 255f, tintG = tint.G / 255f, tintB = tint.B / 255f;

        Parallel.ForEach(Partitioner.Create(0, _pixel.Length, 4096), range =>
        {
            for (int k = range.Item1; k < range.Item2; k++)
            {
                int s = k * 6;
                float r = LiquidGlass.Sample(frosted, w, Math.Min(_sample[s] * scale, maxX), Math.Min(_sample[s + 1] * scale, maxY), 2);
                float g = LiquidGlass.Sample(frosted, w, Math.Min(_sample[s + 2] * scale, maxX), Math.Min(_sample[s + 3] * scale, maxY), 1);
                float b = LiquidGlass.Sample(frosted, w, Math.Min(_sample[s + 4] * scale, maxX), Math.Min(_sample[s + 5] * scale, maxY), 0);

                // Lift saturation, like Apple's material, then lay the tint over it.
                float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                r = Math.Clamp(luma + (r - luma) * sat, 0, 1);
                g = Math.Clamp(luma + (g - luma) * sat, 0, 1);
                b = Math.Clamp(luma + (b - luma) * sat, 0, 1);
                r += (tintR - r) * tintA;
                g += (tintG - g) * tintA;
                b += (tintB - b) * tintA;

                float lift = _lift[k];
                r += (1 - r) * lift;
                g += (1 - g) * lift;
                b += (1 - b) * lift;

                float cover = _coverage[k] * 255;
                int o = _pixel[k] * 4; // output is always full size
                output[o] = (byte)(b * cover);
                output[o + 1] = (byte)(g * cover);
                output[o + 2] = (byte)(r * cover);
            }
        });

        // Alpha: shadow-only pixels outside, and coverage (with shadow at the edge) inside.
        for (int k = 0; k < _shadowPixel.Length; k++)
        {
            int p = _shadowPixel[k];
            int index = p >= 0 ? p : _pixel[-1 - p];
            output[index * 4 + 3] = _shadowAlpha[k];
        }
    }

    public BitmapSource Render(BitmapSource snapshot)
    {
        var source = new byte[Width * Height * 4];
        new FormatConvertedBitmap(snapshot, PixelFormats.Bgra32, null, 0).CopyPixels(source, Width * 4, 0);
        var output = new byte[source.Length];
        Apply(source, output);
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, output, Width * 4);
        bitmap.Freeze();
        return bitmap;
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
