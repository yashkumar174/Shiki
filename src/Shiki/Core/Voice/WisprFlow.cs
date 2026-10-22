using System;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core.Voice;

/// <summary>
/// Wispr Flow's speech-to-text, for people who'd rather use it than Whisper. Its API is only
/// open to enterprise customers so far (model "flow-v1" at api.wisprflow.ai); access has been
/// asked for. Until its documentation arrives this stays switched off and Whisper is used, so
/// nothing is guessed about the request format. The key is already stored like the Claude key.
/// </summary>
public sealed class WisprFlow : ISpeechToText
{
    /// <summary>False until the adapter is written against Wispr's real documentation.</summary>
    public const bool Available = false;

    public static readonly SecretStore Key = new("Shiki/WisprApiKey");

    public static bool HasKey => Key.Exists;

    public string Name => "Wispr Flow";

    public Task Prepare(IProgress<double>? progress, CancellationToken ct) => Task.CompletedTask;

    public Task<string> Transcribe(float[] samples, string hint, CancellationToken ct) =>
        throw new NotSupportedException("Wispr Flow's API isn't open to Shiki yet, so Whisper on this PC is used instead.");
}
