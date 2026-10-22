using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConvertedBro.Core.Voice;

/// <summary>
/// Turns a short recording into text. Swappable: Whisper runs on this PC (the default); Wispr
/// Flow is the cloud option once its API is open to us. The wake word always stays on Whisper.
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

    public static ISpeechToText ForCommands(Settings settings) =>
        settings.VoiceEngine == "wispr" && WisprFlow.Available && WisprFlow.HasKey ? new WisprFlow() : Whisper;
}
