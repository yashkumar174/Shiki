using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Shiki.Core.Voice;

/// <summary>
/// Turns a short recording into text. Swappable: Whisper runs on this PC (the default), Deepgram
/// in the cloud with the person's key, and Wispr Flow once its API is open to us. When Deepgram
/// can't be reached, Whisper takes over.
/// </summary>
public interface ISpeechToText
{
    string Name { get; }

    /// <summary>Downloads or loads what it needs (reports 0 to 1). Cheap when already done.</summary>
    Task Prepare(IProgress<double>? progress, CancellationToken ct);

    /// <param name="samples">Mono, 16 kHz, -1 to 1.</param>
    /// <param name="hint">Words likely to come up (the cat's name, file words), to help spelling.</param>
    Task<string> Transcribe(float[] samples, string hint, CancellationToken ct);
}

/// <summary>Which engine handles commands, from settings.</summary>
public static class Speech
{
    /// <summary>English Whisper: the wake word, and commands when only English is spoken.</summary>
    public static readonly WhisperLocal Whisper = new("ggml-base.en.bin", 147_964_211, "148 MB", "en");

    /// <summary>Multilingual Whisper, for Hindi and Hinglish.</summary>
    public static readonly WhisperLocal WhisperHindi = new("ggml-small-q5_1.bin", 190_085_487, "190 MB", "auto");

    /// <summary>"en", or "hi-en" for Hindi and Hinglish too; set from settings.</summary>
    public static string Language { get; set; } = "en";

    public static bool Hindi => Language == "hi-en";

    /// <summary>Whisper for what the person says (not the wake word): multilingual when they speak Hindi.</summary>
    public static WhisperLocal Local => Hindi ? WhisperHindi : Whisper;

    /// <summary>The wake word goes to Deepgram (speech bursts only) rather than Whisper; set from settings.</summary>
    public static bool WakeOnDeepgram { get; set; }

    public static bool CommandsOnDeepgram(Settings settings) => settings.VoiceEngine == "deepgram" && Deepgram.HasKey;

    /// <summary>ElevenLabs Scribe writes down what's said (set from settings, for the "Hey Sky, …" check too).</summary>
    public static bool OnScribe { get; set; }

    public static bool CommandsOnScribe(Settings settings) => settings.VoiceEngine == "elevenlabs" && ElevenLabs.HasKey;

    /// <summary>An engine that streams live (words as they're said), not a whole-recording one.</summary>
    public static bool IsLive(ISpeechToText engine) => engine is Deepgram or Scribe;

    /// <summary>
    /// Opens the live stream for this engine at the microphone's rate: Scribe (falling back to
    /// Deepgram if it can't), or Deepgram. Null when neither can: the recording is written down whole.
    /// </summary>
    public static async Task<ILiveStream?> OpenLive(ISpeechToText engine, int rate, IEnumerable<string> terms, CancellationToken ct)
    {
        var words = terms.ToList();
        if (engine is Scribe && ElevenLabs.Key.Read() is { } elevenKey && Scribe.TakesRate(rate))
        {
            try
            {
                return await ScribeLive.Open(elevenKey, rate, words, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log.Error("Scribe stream couldn't open; trying Deepgram", ex);
            }
        }
        if (engine is Deepgram or Scribe && Deepgram.Key.Read() is { } deepgramKey)
        {
            try
            {
                return await DeepgramLive.Open(deepgramKey, rate, words, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log.Error("Deepgram stream couldn't open; using Whisper", ex);
            }
        }
        return null;
    }

    /// <summary>Words Deepgram should expect: the cat's name, the person's people, and the app's vocabulary.</summary>
    public static IEnumerable<string> KeyTerms(string catName) =>
        [catName, $"Hey {catName}", "Shiki", .. People.ForSpeech(30), .. Extra, "PDF", "JPG", "PNG", "WEBP", "MP4", "MP3", "GIF", "Downloads", "zip",
            "Spotify", "WhatsApp", "Chrome", "Gmail", "YouTube", "Instagram", "Excel", "PowerPoint", "Word", "Teams", "Zoom", "Notepad",
            "File Explorer", "Desktop", "Documents", "C drive", "D drive", "disk space", "dark mode", "do not disturb", "captions", "screenshot"];

    /// <summary>The person's own words worth expecting: routine phrases and the people they email (set by the app).</summary>
    public static IReadOnlyList<string> Extra { get; set; } = [];

    public static ISpeechToText ForCommands(Settings settings, Func<string> catName) =>
        settings.VoiceEngine == "wispr" && WisprFlow.Available && WisprFlow.HasKey ? new WisprFlow()
        : CommandsOnScribe(settings) ? new Scribe(() => KeyTerms(catName()))
        : CommandsOnDeepgram(settings) ? new Deepgram(() => KeyTerms(catName()))
        : Local;
}
