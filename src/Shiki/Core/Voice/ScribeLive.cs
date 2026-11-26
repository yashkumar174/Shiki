using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Shiki.Core.Voice;

/// <summary>A live speech stream for one spoken request (Deepgram's or ElevenLabs'): the same shape for the listener.</summary>
public interface ILiveStream : IAsyncDisposable
{
    /// <summary>Everything heard so far, as it firms up. Raised on a background thread.</summary>
    event Action<string>? Partial;

    /// <summary>The engine thinks the person has finished. Raised on a background thread.</summary>
    event Action? Ended;

    /// <summary>Mono samples at the stream's rate, straight from the microphone.</summary>
    void Send(float[] mono);

    /// <summary>No more audio: returns everything heard.</summary>
    Task<string> Finish();
}

/// <summary>
/// ElevenLabs Scribe v2 as the command engine: careful with Hindi and English mixed and with names
/// (it's given the person's words to expect, up to a thousand), with the background filtered out
/// and the "um"s left out. Recordings are written down whole, live is streamed.
/// </summary>
public sealed class Scribe(Func<IEnumerable<string>> keyterms) : ISpeechToText
{
    public string Name => "ElevenLabs Scribe v2";

    public Task Prepare(IProgress<double>? progress, CancellationToken ct) => Task.CompletedTask;

    public Task<string> Transcribe(float[] samples, string hint, CancellationToken ct) => ElevenLabs.Transcribe(Deepgram.Wav(samples), keyterms(), ct, "en");

    /// <summary>The rates the live stream takes as they are.</summary>
    public static bool TakesRate(int rate) => rate is 8000 or 16000 or 22050 or 24000 or 44100 or 48000;
}

/// <summary>
/// A live ElevenLabs Scribe v2 stream for one spoken request: audio goes up as it's captured, words
/// come back as they're said, and Scribe commits each phrase after 0.6 s of quiet; Shiki then waits
/// a few seconds more after a word a sentence doesn't end on ("send it to…").
/// </summary>
public sealed class ScribeLive : ILiveStream
{
    readonly ClientWebSocket _socket = new();
    readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>();
    readonly List<string> _finals = [];
    readonly int _rate;
    readonly TaskCompletionSource _lastCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task? _sending, _receiving;
    long _bytes;
    string _interim = "";
    bool _ended, _finishing;
    CancellationTokenSource? _waiting;

    public event Action<string>? Partial;
    public event Action? Ended;

    ScribeLive(int rate) => _rate = rate;

    public string Text => string.Join(" ", _finals).Trim();

    public static async Task<ScribeLive> Open(string key, int sampleRate, IEnumerable<string> terms, CancellationToken ct, string? language = null, string? secondary = null)
    {
        var live = new ScribeLive(sampleRate);
        live._socket.Options.SetRequestHeader("xi-api-key", key);
        var query = new StringBuilder($"model_id=scribe_v2_realtime&audio_format=pcm_{sampleRate}&commit_strategy=vad&vad_silence_threshold_secs=0.6&no_verbatim=true&filter_background_audio=true");
        if (!string.IsNullOrEmpty(language)) query.Append("&language_code=").Append(language);
        if (!string.IsNullOrEmpty(secondary)) query.Append("&secondary_languages=").Append(secondary);
        foreach (var term in terms.Select(t => t.Trim()).Where(t => t.Length is > 1 and <= 50 && t.Split(' ').Length <= 5 && !t.Any(c => "<>{}[]\\".Contains(c)))
                     .Distinct(StringComparer.OrdinalIgnoreCase).Take(50)) // the live model takes at most 50 (names come first)
            query.Append("&keyterms=").Append(Uri.EscapeDataString(term));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        await live._socket.ConnectAsync(new Uri("wss://api.elevenlabs.io/v1/speech-to-text/realtime?" + query), timeout.Token);
        live._sending = live.SendLoop();
        live._receiving = live.ReceiveLoop();
        return live;
    }

    public void Send(float[] mono)
    {
        var bytes = new byte[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), Deepgram.Pcm(mono[i]));
        _outgoing.Writer.TryWrite(bytes);
    }

    byte[] Chunk(byte[] audio, bool commit) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
    {
        ["message_type"] = "input_audio_chunk",
        ["audio_base_64"] = Convert.ToBase64String(audio),
        ["commit"] = commit,
        ["sample_rate"] = _rate,
    });

    public async Task<string> Finish()
    {
        _finishing = true;
        _outgoing.Writer.TryComplete();
        try
        {
            // Sending can hang on a connection that dropped: a few seconds, then on with what's here.
            if (_sending != null) await Task.WhenAny(_sending, Task.Delay(3000));
            // Everything already written down (it ended on a commit): nothing more to wait for.
            // Otherwise the words still being written down come with the last commit.
            if (_ended && _interim.Length == 0 && _finals.Count > 0) return Text;
            await Task.WhenAny(_lastCommit.Task, Task.Delay(2500));
        }
        catch (Exception ex)
        {
            Log.Error("Finishing the Scribe stream", ex);
        }
        return Text.Length > 0 ? (Text + (_interim.Length > 0 && !_lastCommit.Task.IsCompleted ? " " + _interim : "")).Trim() : _interim.Trim();
    }

    async Task SendLoop()
    {
        try
        {
            await foreach (var audio in _outgoing.Reader.ReadAllAsync())
            {
                if (_socket.State != WebSocketState.Open) break;
                await _socket.SendAsync(Chunk(audio, false), WebSocketMessageType.Text, true, CancellationToken.None);
                _bytes += audio.Length;
            }
            // A tenth of a second of silence, committed: Scribe hands over its last words.
            if (_socket.State == WebSocketState.Open)
                await _socket.SendAsync(Chunk(new byte[_rate / 10 * 2], true), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            Log.Error("Scribe stream dropped while sending", ex);
        }
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                Handle(message.ToArray());
                message.SetLength(0);
            }
        }
        catch (WebSocketException ex)
        {
            Log.Error("Scribe stream dropped", ex);
        }
        _lastCommit.TrySetResult();
    }

    void Handle(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("message_type", out var t) ? t.GetString() : null;
        var text = root.TryGetProperty("text", out var x) ? (x.GetString() ?? "").Trim() : "";
        switch (type)
        {
            case "partial_transcript":
                _interim = text;
                Partial?.Invoke((Text + " " + _interim).Trim());
                // New words: a pending "it didn't sound finished" wait starts over.
                if (text.Length > 0) _waiting?.Cancel();
                break;
            case "committed_transcript":
            case "committed_transcript_with_timestamps":
                if (text.Length > 0) _finals.Add(text);
                _interim = "";
                Partial?.Invoke(Text);
                if (_finishing) _lastCommit.TrySetResult();
                else if (_finals.Count > 0) EndUnlessUnfinished();
                break;
            case "error":
            case "auth_error":
            case "quota_exceeded":
            case "rate_limited":
            case "input_error":
            case "invalid_request":
            case "transcriber_error":
                Log.Error($"Scribe said {type}: {(root.TryGetProperty("error", out var e) ? e.GetString() : json.Length.ToString())}");
                break;
        }
    }

    void EndUnlessUnfinished()
    {
        if (!DeepgramLive.LooksUnfinished(Text))
        {
            End();
            return;
        }
        // Give them a moment more; new words cancel this and the next commit decides again.
        _waiting?.Cancel();
        var waiting = _waiting = new CancellationTokenSource();
        _ = Task.Delay(4000, waiting.Token).ContinueWith(t => { if (!t.IsCanceled) End(); }, TaskScheduler.Default);
    }

    void End()
    {
        if (_ended) return;
        _ended = true;
        Ended?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _outgoing.Writer.TryComplete();
        try
        {
            // A polite goodbye, but not forever: a connection that dropped never answers it.
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", wait.Token);
        }
        catch (OperationCanceledException)
        {
            _socket.Abort();
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
        }
        _socket.Dispose();
    }
}
