using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Shiki.Core;

/// <summary>A touch-up brush stroke, in the photo's own pixels. Keep paints the subject back in.</summary>
public sealed record BrushStroke(IReadOnlyList<(double X, double Y)> Points, double Radius, bool Keep);

/// <summary>
/// Background removal with BiRefNet (MIT, github.com/ZhengPeng7/BiRefNet), run locally with ONNX
/// Runtime on the graphics card (DirectML) or the CPU. The model's mask is then refined at full
/// resolution: a guided filter snaps it to the photo's real edges (hair survives), and foreground
/// colour estimation removes the old background's colour from the edges (no halos).
/// </summary>
public static class Cutout
{
    const int ModelSize = 1024;
    static readonly float[] Mean = [0.485f, 0.456f, 0.406f], Std = [0.229f, 0.224f, 0.225f];

    // BiRefNet lite in half precision: a 115 MB download, fits a 4 GB graphics card, and gives the
    // same mask as full precision. (The 970 MB matting model was tried: barely finer hair, 20x slower.)
    const string ModelFile = "birefnet-lite-fp16.onnx";
    const string ModelUrl = "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/main/onnx/model_fp16.onnx";
    const long ModelBytes = 114_538_221;

    static readonly SemaphoreSlim Gate = new(1, 1);
    static InferenceSession? _session;
    static Timer? _idle;
    static bool _gpuFailed;
    public static string Device { get; private set; } = "";

    static string ModelPath => Path.Combine(Log.DataDir, "models", ModelFile);
    public static bool IsDownloaded => File.Exists(ModelPath);
    public static string DownloadSize => $"{ModelBytes / 1_000_000} MB";

    /// <summary>Downloads the model the first time it's needed, with progress.</summary>
    public static async Task EnsureModel(IProgress<double> progress, CancellationToken ct)
    {
        var path = ModelPath;
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = path + ".part";
        JobNotes.Say($"Downloading the background model ({DownloadSize}, one time)");
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? ModelBytes;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(part))
        {
            var buffer = new byte[1 << 20];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress.Report((double)done / total);
            }
        }
        File.Move(part, path, overwrite: true);
    }

    /// <summary>Batch use: the photo with its background removed, saved next to it as a PNG.</summary>
    public static async Task<string> RemoveToFile(string input, IProgress<double> progress, CancellationToken ct)
    {
        await EnsureModel(progress, ct);
        return await Task.Run(() =>
        {
            using var photo = ImageTools.Load(input);
            using var cut = Remove(photo);
            var output = OutputPaths.Next(input, ".png", "no background");
            WritePng(cut, output);
            return output;
        }, ct);
    }

    /// <summary>Saves quickly: a lighter zlib level costs a few percent in size and saves seconds.</summary>
    public static void WritePng(MagickImage image, string path)
    {
        image.Quality = 45;
        image.Write(path, MagickFormat.Png32);
    }

    /// <summary>The subject with a transparent background, same size as the photo.</summary>
    public static MagickImage Remove(MagickImage photo)
    {
        var alpha = Matte(photo);
        return Compose(photo, alpha);
    }

    /// <summary>The refined alpha matte (0 = background, 1 = subject), at full resolution.</summary>
    public static float[] Matte(MagickImage photo)
    {
        int w = (int)photo.Width, h = (int)photo.Height;
        var coarse = Predict(photo);
        var mask = Upscale(coarse, ModelSize, ModelSize, w, h);
        var gray = Gray(photo);
        return Refine(gray, mask, w, h);
    }

    // ---- Model ----

    /// <summary>Lets go of the model (and its graphics memory) after a few idle minutes.</summary>
    static void ReleaseLater()
    {
        _idle ??= new Timer(_ =>
        {
            if (!Gate.Wait(0)) return;
            try
            {
                _session?.Dispose();
                _session = null;
                Log.Info("Cutout model released after being idle");
            }
            finally
            {
                Gate.Release();
            }
        });
        _idle.Change(TimeSpan.FromMinutes(3), Timeout.InfiniteTimeSpan);
    }

    static InferenceSession Session()
    {
        if (_session != null) return _session;
        var path = ModelPath;
        try
        {
            if (_gpuFailed) throw new InvalidOperationException("The graphics card failed earlier; using the processor.");
            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            // DirectML needs these two off.
            options.EnableMemoryPattern = false;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            // Fused DirectML graphs ask for one huge block of graphics memory; 4 GB cards run out.
            options.AddSessionConfigEntry("ep.dml.disable_graph_fusion", "1");
            options.AppendExecutionProvider_DML(0);
            _session = new InferenceSession(path, options);
            Device = "graphics card";
        }
        catch (Exception ex)
        {
            Log.Error("DirectML unavailable, using the CPU for background removal", ex);
            _session = new InferenceSession(path, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });
            Device = "processor";
        }
        var input = _session.InputMetadata.First();
        Log.Info($"Cutout model {Path.GetFileName(path)} on {Device}: input {input.Key} [{string.Join(",", input.Value.Dimensions)}], outputs {string.Join(", ", _session.OutputMetadata.Keys)}");
        return _session;
    }

    /// <summary>Runs BiRefNet on a 1024×1024 copy of the photo; returns the mask (0..1).</summary>
    static float[] Predict(MagickImage photo)
    {
        using var small = (MagickImage)photo.Clone();
        ImageTools.FlattenOnWhite(small);
        small.FilterType = FilterType.Triangle;
        small.Resize(new MagickGeometry(ModelSize, ModelSize) { IgnoreAspectRatio = true });
        var rgb = small.GetPixels().ToByteArray(PixelMapping.RGB)!;

        var tensor = new DenseTensor<float>([1, 3, ModelSize, ModelSize]);
        var buffer = tensor.Buffer.Span;
        int plane = ModelSize * ModelSize;
        for (int i = 0; i < plane; i++)
            for (int c = 0; c < 3; c++)
                buffer[c * plane + i] = (rgb[i * 3 + c] / 255f - Mean[c]) / Std[c];

        Gate.Wait();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            float[] output;
            try
            {
                output = Run(Session(), tensor);
            }
            catch (OnnxRuntimeException ex) when (Device == "graphics card")
            {
                // Usually not enough graphics memory: carry on with the processor from now on.
                Log.Error("Graphics card couldn't run the cutout model; switching to the processor", ex);
                _gpuFailed = true;
                _session?.Dispose();
                _session = null;
                output = Run(Session(), tensor);
            }
            Log.Info($"Cutout model ran in {timer.ElapsedMilliseconds} ms on the {Device}");
            ReleaseLater();
            float min = output.Min(), max = output.Max();
            bool logits = min < -0.01f || max > 1.01f;
            for (int i = 0; i < output.Length; i++)
                output[i] = logits ? 1f / (1f + MathF.Exp(-output[i])) : Math.Clamp(output[i], 0, 1);
            return output;
        }
        finally
        {
            Gate.Release();
        }
    }

    static float[] Run(InferenceSession session, DenseTensor<float> tensor)
    {
        var (name, meta) = session.InputMetadata.First();
        var input = meta.ElementDataType == TensorElementType.Float16
            ? NamedOnnxValue.CreateFromTensor(name, new DenseTensor<Float16>(tensor.Buffer.ToArray().Select(v => (Float16)v).ToArray(), tensor.Dimensions))
            : NamedOnnxValue.CreateFromTensor(name, tensor);
        using var results = session.Run([input]);
        // The last output is the final, full-resolution prediction.
        var last = results.Last();
        return last.Value is Tensor<Float16> half
            ? half.Select(v => (float)v).ToArray()
            : last.AsEnumerable<float>().ToArray();
    }

    // ---- Refinement ----

    /// <summary>
    /// Guided filter (He et al.) using the photo as guide: the matte follows the photo's own edges,
    /// so detail lost at 1024 px comes back. Applied only near edges; solid areas stay solid.
    /// </summary>
    static float[] Refine(float[] gray, float[] mask, int w, int h)
    {
        int r = Math.Max(2, Math.Min(w, h) / 360);
        const float eps = 1e-4f;
        var meanI = BoxBlur(gray, w, h, r);
        var meanP = BoxBlur(mask, w, h, r);
        var ii = new float[gray.Length];
        var ip = new float[gray.Length];
        for (int i = 0; i < gray.Length; i++) { ii[i] = gray[i] * gray[i]; ip[i] = gray[i] * mask[i]; }
        var corrI = BoxBlur(ii, w, h, r);
        var corrIp = BoxBlur(ip, w, h, r);
        var a = new float[gray.Length];
        var b = new float[gray.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            float varI = corrI[i] - meanI[i] * meanI[i];
            float covIp = corrIp[i] - meanI[i] * meanP[i];
            a[i] = covIp / (varI + eps);
            b[i] = meanP[i] - a[i] * meanI[i];
        }
        var meanA = BoxBlur(a, w, h, r);
        var meanB = BoxBlur(b, w, h, r);

        // Only the uncertain band (where the mask isn't solid nearby) takes the filtered value.
        var spread = BoxBlur(mask, w, h, r * 2);
        var result = new float[mask.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool edge = spread[i] > 0.01f && spread[i] < 0.99f;
                float guided = Math.Clamp(meanA[i] * gray[i] + meanB[i], 0, 1);
                float value = edge ? guided : mask[i];
                // Tiny residues become fully clear or fully solid.
                result[i] = value < 0.02f ? 0 : value > 0.98f ? 1 : value;
            }
        });
        return result;
    }

    /// <summary>
    /// Foreground colour without the old background mixed into soft edges ("approximate fast
    /// foreground colour estimation", Forte and Pitié 2021): blur-fusion, a wide pass then a narrow one.
    /// </summary>
    public static MagickImage Compose(MagickImage photo, float[] alpha)
    {
        int w = (int)photo.Width, h = (int)photo.Height, n = w * h;
        using var flat = (MagickImage)photo.Clone();
        ImageTools.FlattenOnWhite(flat);
        var rgb = flat.GetPixels().ToByteArray(PixelMapping.RGB)!;

        var output = new byte[n * 4];
        int wide = Math.Max(8, Math.Min(w, h) / 12), narrow = Math.Max(2, Math.Min(w, h) / 160);
        var wideAlpha = BoxBlur(alpha, w, h, wide);
        var narrowAlpha = BoxBlur(alpha, w, h, narrow);
        Parallel.For(0, 3, c =>
        {
            var image = new float[n];
            for (int i = 0; i < n; i++) image[i] = rgb[i * 3 + c] / 255f;
            var (f1, b1) = BlurFusion(image, image, image, alpha, wideAlpha, w, h, wide);
            var (f2, _) = BlurFusion(image, f1, b1, alpha, narrowAlpha, w, h, narrow);
            for (int i = 0; i < n; i++) output[i * 4 + c] = (byte)Math.Clamp(f2[i] * 255 + 0.5f, 0, 255);
        });
        for (int i = 0; i < n; i++) output[i * 4 + 3] = (byte)Math.Clamp(alpha[i] * 255 + 0.5f, 0, 255);

        var result = new MagickImage();
        result.ReadPixels(output, new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.RGBA));
        return result;
    }

    static (float[] F, float[] B) BlurFusion(float[] image, float[] f, float[] b, float[] alpha, float[] blurredAlpha, int w, int h, int r)
    {
        int n = image.Length;
        var fa = new float[n];
        var b1a = new float[n];
        for (int i = 0; i < n; i++) { fa[i] = f[i] * alpha[i]; b1a[i] = b[i] * (1 - alpha[i]); }
        var blurredFa = BoxBlur(fa, w, h, r);
        var blurredB1a = BoxBlur(b1a, w, h, r);
        var outF = new float[n];
        var outB = new float[n];
        Parallel.For(0, n / 4096 + 1, chunk =>
        {
            int end = Math.Min(n, (chunk + 1) * 4096);
            for (int i = chunk * 4096; i < end; i++)
            {
                float blurredF = blurredFa[i] / (blurredAlpha[i] + 1e-5f);
                float blurredB = blurredB1a[i] / (1 - blurredAlpha[i] + 1e-5f);
                float a = alpha[i];
                outF[i] = Math.Clamp(blurredF + a * (image[i] - a * blurredF - (1 - a) * blurredB), 0, 1);
                outB[i] = blurredB;
            }
        });
        return (outF, outB);
    }

    // ---- Touch-up ----

    /// <summary>Paints strokes into a matte. <paramref name="scale"/> maps photo pixels to this matte's pixels.</summary>
    public static void Paint(float[] alpha, int w, int h, IEnumerable<BrushStroke> strokes, double scale)
    {
        foreach (var stroke in strokes)
        {
            double r = Math.Max(0.75, stroke.Radius * scale);
            var points = stroke.Points;
            for (int i = 0; i < points.Count; i++)
            {
                var (x1, y1) = (points[i].X * scale, points[i].Y * scale);
                var (x0, y0) = i == 0 ? (x1, y1) : (points[i - 1].X * scale, points[i - 1].Y * scale);
                double length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                int steps = Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.5, r / 4)));
                for (int k = i == 0 ? 0 : 1; k <= steps; k++)
                {
                    double t = (double)k / steps;
                    Dab(alpha, w, h, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r, stroke.Keep);
                }
            }
        }
    }

    /// <summary>One round dab with a one-pixel soft rim.</summary>
    public static void Dab(float[] alpha, int w, int h, double cx, double cy, double r, bool keep)
    {
        int x0 = Math.Max(0, (int)(cx - r - 1)), x1 = Math.Min(w - 1, (int)(cx + r + 1));
        int y0 = Math.Max(0, (int)(cy - r - 1)), y1 = Math.Min(h - 1, (int)(cy + r + 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                float cover = (float)Math.Clamp(r + 0.5 - d, 0, 1);
                if (cover <= 0) continue;
                int i = y * w + x;
                alpha[i] = keep ? Math.Max(alpha[i], cover) : Math.Min(alpha[i], 1 - cover);
            }
    }

    /// <summary>A matte at another size (area-averaged when shrinking, so hair stays smooth).</summary>
    public static float[] Resize(float[] alpha, int w, int h, int newWidth, int newHeight)
    {
        var bytes = new byte[alpha.Length * 3];
        for (int i = 0; i < alpha.Length; i++) bytes[i * 3] = bytes[i * 3 + 1] = bytes[i * 3 + 2] = (byte)Math.Clamp(alpha[i] * 255 + 0.5f, 0, 255);
        using var image = new MagickImage();
        image.ReadPixels(bytes, new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.RGB));
        image.Resize(new MagickGeometry((uint)newWidth, (uint)newHeight) { IgnoreAspectRatio = true });
        var small = image.GetPixels().ToByteArray("R")!;
        var result = new float[newWidth * newHeight];
        for (int i = 0; i < result.Length; i++) result[i] = small[i] / 255f;
        return result;
    }

    // ---- Helpers ----

    static float[] Gray(MagickImage photo)
    {
        using var flat = (MagickImage)photo.Clone();
        ImageTools.FlattenOnWhite(flat);
        var rgb = flat.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var gray = new float[rgb.Length / 3];
        for (int i = 0; i < gray.Length; i++)
            gray[i] = (0.299f * rgb[i * 3] + 0.587f * rgb[i * 3 + 1] + 0.114f * rgb[i * 3 + 2]) / 255f;
        return gray;
    }

    /// <summary>Bilinear upscale of the model's mask to the photo's size.</summary>
    static float[] Upscale(float[] src, int sw, int sh, int w, int h)
    {
        var dst = new float[w * h];
        float sx = (float)sw / w, sy = (float)sh / h;
        Parallel.For(0, h, y =>
        {
            float fy = Math.Clamp((y + 0.5f) * sy - 0.5f, 0, sh - 1.001f);
            int y0 = (int)fy;
            float ty = fy - y0;
            for (int x = 0; x < w; x++)
            {
                float fx = Math.Clamp((x + 0.5f) * sx - 0.5f, 0, sw - 1.001f);
                int x0 = (int)fx;
                float tx = fx - x0;
                int i = y0 * sw + x0;
                float top = src[i] + (src[i + 1] - src[i]) * tx;
                float bottom = src[i + sw] + (src[i + sw + 1] - src[i + sw]) * tx;
                dst[y * w + x] = top + (bottom - top) * ty;
            }
        });
        return dst;
    }

    /// <summary>Mean filter with a (2r+1)² box, via running sums: cost doesn't depend on r.</summary>
    static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            double sum = 0;
            for (int k = -r; k <= r; k++) sum += src[row + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = (float)(sum / (2 * r + 1));
                sum += src[row + Math.Min(x + r + 1, w - 1)] - src[row + Math.Max(x - r, 0)];
            }
        });
        Parallel.For(0, w, x =>
        {
            double sum = 0;
            for (int k = -r; k <= r; k++) sum += tmp[Math.Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (float)(sum / (2 * r + 1));
                sum += tmp[Math.Min(y + r + 1, h - 1) * w + x] - tmp[Math.Max(y - r, 0) * w + x];
            }
        });
        return dst;
    }
}
