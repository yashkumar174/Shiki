using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shiki.Core.Voice;

/// <summary>
/// One go of dictation: listen (Deepgram live when chosen, Whisper otherwise), and give back the
/// words. Held: it listens until the key is let go. Tapped: until a pause. Raises Changed on the
/// thread it was made on (the UI thread).
/// </summary>
public sealed class Dictation(Settings settings, Func<string> catName)
{
    readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    readonly CancellationTokenSource _cancel = new();
    Microphone? _mic;

    /// <summary>Listening while the key is held (true) or until a pause (false, after a quick tap).</summary>
    public bool Held { get; set; } = true;
    public string Partial { get; private set; } = "";
    public string Status { get; private set; } = "Listening…";
    public double Level { get; private set; }

    public event Action? Changed;

    void Post(Action action) => _ui.Post(_ => { action(); Changed?.Invoke(); }, null);

    /// <summary>Stop listening and write down what was said.</summary>
    public void Finish() => _mic?.Stop();

    /// <summary>Stop and throw it away.</summary>
    public void Cancel()
    {
        _cancel.Cancel();
        _mic?.Stop();
    }

    /// <returns>What was said, or null when nothing was (or it was cancelled).</returns>
    public async Task<string?> Run()
    {
        var ct = _cancel.Token;
        var engine = Speech.ForCommands(settings, catName);
        if (engine is WhisperLocal && !WhisperLocal.Downloaded)
        {
            await engine.Prepare(new Progress<double>(p => Post(() => Status = $"Getting my ears ready (one time)… {p:0%}")), ct);
        }
        DeepgramLive? live = null;
        try
        {
            Heard heard;
            // Dictation has longer pauses than commands: thinking mid-sentence mustn't end it.
            using (var mic = _mic = new Microphone(pauseEnds: engine is Deepgram ? 8 : 2.5, waitForSpeech: 10))
            {
                int tick = 0;
                mic.Level += level => { Level = level; if (++tick % 4 == 0) Post(() => { }); };
                Post(() => Status = Held ? "Listening… let go to type" : "Listening… pause to finish");
                var listening = mic.Listen(ct);
                if (engine is Deepgram && Deepgram.Key.Read() is { } key)
                {
                    try
                    {
                        live = await DeepgramLive.Open(key, mic.SampleRate, [catName(), "Shiki"], ct);
                        live.Partial += text => Post(() => Partial = text);
                        // Tapped: Deepgram decides when you've finished. Held: the key does.
                        live.Ended += () => { if (!Held) mic.Stop(); };
                        mic.StreamTo(live.Send);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        Log.Error("Dictation: Deepgram couldn't open; using Whisper", ex);
                        live = null;
                        engine = Speech.Whisper;
                    }
                }
                heard = await listening;
            }
            if (ct.IsCancellationRequested) return null;
            var streamed = live != null ? await live.Finish() : null;
            if (heard.Kind != HeardKind.Speech && string.IsNullOrWhiteSpace(streamed)) return null;
            Post(() => Status = "Writing it down…");
            var text = streamed is { Length: > 0 } ? streamed : await (engine is WhisperLocal ? Speech.Whisper : engine).Transcribe(heard.Samples, "", ct);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (live != null) await live.DisposeAsync();
            _mic = null;
        }
    }
}
