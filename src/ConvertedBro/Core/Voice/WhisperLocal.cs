using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace ConvertedBro.Core.Voice;

/// <summary>
/// OpenAI's Whisper (English "base" model, MIT) through whisper.cpp, on this PC: nothing leaves it.
/// The model (148 MB) downloads the first time voice is used and is let go after a few idle minutes.
/// </summary>
public sealed class WhisperLocal : ISpeechToText
{
    const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";
    const long ModelBytes = 147_964_211;
    public const string DownloadSize = "148 MB";

    readonly SemaphoreSlim _gate = new(1, 1);
    WhisperFactory? _factory;
    Timer? _idle;

    public string Name => "Whisper (on this PC)";

    public static string ModelPath => Path.Combine(Log.DataDir, "models", "ggml-base.en.bin");

    public static bool Downloaded => File.Exists(ModelPath);

    public async Task Prepare(IProgress<double>? progress, CancellationToken ct)
    {
        var path = ModelPath;
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = path + ".part";
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
                progress?.Report((double)done / total);
            }
        }
        File.Move(part, path, overwrite: true);
    }

    public Task<string> Transcribe(float[] samples, string hint, CancellationToken ct) => Transcribe(samples, hint, ct, Math.Clamp(Environment.ProcessorCount - 1, 1, 8));

    /// <param name="threads">Fewer for the wake word, which runs in the background.</param>
    public async Task<string> Transcribe(float[] samples, string hint, CancellationToken ct, int threads)
    {
        await Prepare(null, ct);
        await _gate.WaitAsync(ct);
        try
        {
            _factory ??= WhisperFactory.FromPath(ModelPath);
            var builder = _factory.CreateBuilder()
                .WithLanguage("en")
                .WithNoContext()
                .WithThreads(threads);
            // No hint for the wake word: a hint with the name in it makes Whisper hear the name everywhere.
            if (hint.Length > 0) builder = builder.WithPrompt(hint);
            using var processor = builder.Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct))
                text.Append(segment.Text);
            return Clean(text.ToString());
        }
        finally
        {
            _gate.Release();
            ReleaseLater();
        }
    }

    /// <summary>Whisper marks non-speech as [BLANK_AUDIO], (wind blowing) and the like; drop those.</summary>
    static string Clean(string text)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", " ");
        cleaned = string.Join(" ", cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        // A lone "you" or "Thank you." is what Whisper hears in near-silence.
        return cleaned.Trim().TrimEnd('.').ToLowerInvariant() is "you" or "thank you" or "thanks for watching" ? "" : cleaned.Trim();
    }

    void ReleaseLater()
    {
        _idle ??= new Timer(_ =>
        {
            if (!_gate.Wait(0)) return;
            try
            {
                _factory?.Dispose();
                _factory = null;
            }
            finally
            {
                _gate.Release();
            }
        });
        _idle.Change(TimeSpan.FromMinutes(3), Timeout.InfiniteTimeSpan);
    }
}
