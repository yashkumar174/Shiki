using System;
using System.Collections.Generic;
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
    public static readonly WhisperLocal Whisper = new();

    /// <summary>The wake word goes to Deepgram (speech bursts only) rather than Whisper; set from settings.</summary>
    public static bool WakeOnDeepgram { get; set; }

    public static bool CommandsOnDeepgram(Settings settings) => settings.VoiceEngine == "deepgram" && Deepgram.HasKey;

    /// <summary>Words Deepgram should expect: the cat's name and the app's vocabulary.</summary>
    public static IEnumerable<string> KeyTerms(string catName) =>
        [catName, $"Hey {catName}", "PDF", "JPG", "PNG", "WEBP", "MP4", "MP3", "GIF", "Downloads", "zip"];

    public static ISpeechToText ForCommands(Settings settings, Func<string> catName) =>
        settings.VoiceEngine == "wispr" && WisprFlow.Available && WisprFlow.HasKey ? new WisprFlow()
        : CommandsOnDeepgram(settings) ? new Deepgram(() => KeyTerms(catName()))
        : Whisper;
}
