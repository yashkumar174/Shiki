using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Shiki.Core.Voice;

/// <summary>
/// "Only my voice": a speaker model (WeSpeaker ResNet34, trained on VoxCeleb, CC-BY 4.0; 26 MB,
/// downloaded when the feature is first used) turns a few seconds of speech into 256 numbers
/// that describe the voice, not the words. The person's takes while teaching the cat its name
/// are averaged into their voiceprint; a wake word whose voice is too far from it is ignored.
/// Everything runs on this PC.
/// </summary>
public static class VoicePrint
{
    const string ModelUrl = "https://huggingface.co/Wespeaker/wespeaker-voxceleb-resnet34-LM/resolve/main/voxceleb_resnet34_LM.onnx";
    public const string DownloadSize = "27 MB";
    /// <summary>How alike a voice must be (cosine, -1 to 1) to count as the owner's.</summary>
    public static double Enough(string level) => level switch
    {
        "relaxed" => 0.45,
        "strict" => 0.7,
        _ => 0.58,
    };

    static readonly object Gate = new();
    static InferenceSession? _session;

    public static string ModelPath => Path.Combine(Log.DataDir, "models", "voxceleb_resnet34_LM.onnx");
    static string PrintPath => Path.Combine(Log.DataDir, "voiceprint.json");

    public static bool HasPrint => File.Exists(PrintPath);

    public static async Task Prepare(IProgress<double>? progress, CancellationToken ct)
    {
        if (File.Exists(ModelPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var part = ModelPath + ".part";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 26_500_000;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(part))
        {
            var buffer = new byte[1 << 18];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress?.Report((double)done / total);
            }
        }
        File.Move(part, ModelPath, overwrite: true);
    }

    /// <summary>The voice in 16 kHz mono samples as 256 numbers (length 1), or null without the model.</summary>
    public static float[]? Embed(float[] samples)
    {
        if (!File.Exists(ModelPath) || samples.Length < 16000 / 2) return null;
        var feats = Fbank.Compute(samples);
        if (feats.GetLength(0) < 20) return null;
        lock (Gate)
        {
            _session ??= new InferenceSession(ModelPath);
            int frames = feats.GetLength(0);
            var input = new DenseTensor<float>(new[] { 1, frames, Fbank.Bins });
            for (int t = 0; t < frames; t++)
                for (int b = 0; b < Fbank.Bins; b++)
                    input[0, t, b] = feats[t, b];
            var name = _session.InputMetadata.Keys.First();
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(name, input)]);
            var output = results.First().AsEnumerable<float>().ToArray();
            return Normalize(output);
        }
    }

    /// <summary>Learns the owner's voice from a few takes (each a second or more of speech).</summary>
    public static bool Enroll(params float[][] takes)
    {
        var prints = takes.Select(Embed).Where(e => e != null).Cast<float[]>().ToList();
        if (prints.Count == 0) return false;
        var mean = new float[prints[0].Length];
        foreach (var p in prints)
            for (int i = 0; i < mean.Length; i++) mean[i] += p[i] / prints.Count;
        File.WriteAllText(PrintPath, JsonSerializer.Serialize(Normalize(mean)));
        Log.Info($"Voiceprint learned from {prints.Count} takes");
        return true;
    }

    public static void Forget()
    {
        if (File.Exists(PrintPath)) File.Delete(PrintPath);
    }

    /// <summary>How much this sounds like the owner (cosine), or null when there's no voiceprint or model.</summary>
    public static double? Likeness(float[] samples)
    {
        if (!HasPrint) return null;
        var print = JsonSerializer.Deserialize<float[]>(File.ReadAllText(PrintPath));
        if (print == null || Embed(samples) is not { } voice) return null;
        double dot = 0;
        for (int i = 0; i < Math.Min(print.Length, voice.Length); i++) dot += print[i] * voice[i];
        return dot;
    }

    static float[] Normalize(float[] v)
    {
        double norm = Math.Sqrt(v.Sum(x => (double)x * x));
        return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
    }

    /// <summary>
    /// Kaldi-style log mel filterbank, as the model was trained on: 25 ms frames every 10 ms,
    /// DC removed, pre-emphasis 0.97, Hamming window, 512-point power spectrum, 80 mel bins
    /// from 20 Hz to 8 kHz, log, then each bin's mean over time subtracted.
    /// </summary>
    static class Fbank
    {
        public const int Bins = 80;
        const int Rate = 16000, Frame = 400, Shift = 160, Fft = 512;
        static readonly double[] Window = Enumerable.Range(0, Frame).Select(n => 0.54 - 0.46 * Math.Cos(2 * Math.PI * n / (Frame - 1))).ToArray();
        static readonly double[][] Mel = MelBanks();

        public static float[,] Compute(float[] samples)
        {
            int frames = samples.Length < Frame ? 0 : 1 + (samples.Length - Frame) / Shift;
            var feats = new float[frames, Bins];
            var re = new double[Fft];
            var im = new double[Fft];
            for (int t = 0; t < frames; t++)
            {
                Array.Clear(re);
                Array.Clear(im);
                double mean = 0;
                for (int i = 0; i < Frame; i++) mean += samples[t * Shift + i];
                mean /= Frame;
                // Kaldi works on 16-bit sized numbers.
                var frame = new double[Frame];
                for (int i = 0; i < Frame; i++) frame[i] = (samples[t * Shift + i] - mean) * 32768.0;
                for (int i = Frame - 1; i > 0; i--) frame[i] -= 0.97 * frame[i - 1];
                frame[0] -= 0.97 * frame[0];
                for (int i = 0; i < Frame; i++) re[i] = frame[i] * Window[i];
                Transform(re, im);
                for (int b = 0; b < Bins; b++)
                {
                    double energy = 0;
                    var weights = Mel[b];
                    for (int k = 0; k < weights.Length; k++)
                        if (weights[k] > 0) energy += weights[k] * (re[k] * re[k] + im[k] * im[k]);
                    feats[t, b] = (float)Math.Log(Math.Max(energy, 1.1920929e-07));
                }
            }
            // Mean normalisation over time.
            for (int b = 0; b < Bins; b++)
            {
                double mean = 0;
                for (int t = 0; t < frames; t++) mean += feats[t, b];
                mean /= Math.Max(1, frames);
                for (int t = 0; t < frames; t++) feats[t, b] -= (float)mean;
            }
            return feats;
        }

        static double MelOf(double hz) => 1127.0 * Math.Log(1 + hz / 700.0);

        static double[][] MelBanks()
        {
            int fftBins = Fft / 2;
            double low = MelOf(20), high = MelOf(Rate / 2.0), delta = (high - low) / (Bins + 1);
            var banks = new double[Bins][];
            for (int b = 0; b < Bins; b++)
            {
                double left = low + b * delta, centre = left + delta, right = centre + delta;
                banks[b] = new double[fftBins];
                for (int k = 0; k < fftBins; k++)
                {
                    double mel = MelOf(k * (double)Rate / Fft);
                    if (mel > left && mel < right)
                        banks[b][k] = mel <= centre ? (mel - left) / (centre - left) : (right - mel) / (right - centre);
                }
            }
            return banks;
        }

        /// <summary>In-place radix-2 FFT.</summary>
        static void Transform(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                double wr = Math.Cos(angle), wi = Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int j = 0; j < len / 2; j++)
                    {
                        int a = i + j, b = a + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                    }
                }
            }
        }
    }
}
