using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace Shiki.Core.Voice;

/// <summary>
/// OpenAI's Whisper (MIT) through whisper.cpp, on this PC: nothing leaves it. Two models: English
/// "base" (148 MB, the wake word and English commands) and multilingual "small" (190 MB, for Hindi
/// and Hinglish). Each downloads the first time it's needed and is let go after a few idle minutes.
/// </summary>
public sealed class WhisperLocal(string file, long bytes, string size, string language) : ISpeechToText
{
    readonly string _url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + file;

    /// <summary>The English model, as everything used it before Hindi came along.</summary>
    public const string DownloadSize = "148 MB";

    /// <summary>This model's download, e.g. "190 MB".</summary>
    public string Size => size;

    readonly SemaphoreSlim _gate = new(1, 1);
    WhisperFactory? _factory;
    Timer? _idle;

    /// <summary>Keep the model in memory (the wake word is on, and loading it costs time on every check).</summary>
    public bool KeepLoaded { get; set; }

    public string Name => "Whisper (on this PC)";

    /// <summary>Running on the graphics card (Vulkan or CUDA) rather than the processor; known once the model has loaded.</summary>
    public static bool OnGpu { get; private set; }

    public string ModelPath => Path.Combine(Log.DataDir, "models", file);

    /// <summary>The English model is on this PC (the wake word's).</summary>
    public static bool Downloaded => Speech.Whisper.IsDownloaded;

    public bool IsDownloaded => File.Exists(ModelPath);

    public async Task Prepare(IProgress<double>? progress, CancellationToken ct)
    {
        var path = ModelPath;
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = path + ".part";
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        using var response = await http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? bytes;
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
    /// <param name="shortClip">
    /// A second or two of speech: Whisper normally works on a 30-second window whatever the length;
    /// shrinking it to the clip makes short checks several times faster.
    /// </param>
    /// <param name="maxTokens">Stop after this many tokens (0: no limit); a quick check only needs the first few words.</param>
    public async Task<string> Transcribe(float[] samples, string hint, CancellationToken ct, int threads, bool shortClip = false, int maxTokens = 0)
    {
        await Prepare(null, ct);
        await _gate.WaitAsync(ct);
        try
        {
            if (_factory == null)
            {
                // The graphics card (Vulkan works on NVIDIA, AMD and Intel) when there is one; the processor otherwise.
                _factory = WhisperFactory.FromPath(ModelPath, new WhisperFactoryOptions { UseGpu = true });
                var library = Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary;
                OnGpu = library is Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan or Whisper.net.LibraryLoader.RuntimeLibrary.Cuda;
                Log.Info($"Whisper loaded on {library}");
            }
            var builder = _factory.CreateBuilder()
                // "auto" for the multilingual model: Hindi, English, or both in one sentence.
                .WithLanguage(language)
                .WithNoContext()
                .WithThreads(threads);
            // No hint for the wake word: a hint with the name in it makes Whisper hear the name everywhere.
            if (hint.Length > 0) builder = builder.WithPrompt(hint);
            // 50 audio frames a second (1500 for 30 s), with room to spare.
            // Half the window (15 s): about twice as fast, and still reads real voices well. (Much smaller garbles them.)
            if (shortClip && samples.Length < 16000 * 12) builder = builder.WithAudioContextSize(768);
            // A small window can make Whisper repeat itself ("the sky the sky…"); a cap keeps that short.
            if (maxTokens > 0) builder = builder.WithMaxTokensPerSegment(maxTokens).WithSingleSegment();
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
        if (KeepLoaded) return;
        _idle ??= new Timer(_ =>
        {
            if (KeepLoaded || !_gate.Wait(0)) return;
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
