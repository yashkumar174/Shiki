using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core.Voice;

/// <summary>
/// Deepgram's Nova-3 speech-to-text, in the cloud, with the person's own API key. Short clips
/// (the wake word) go up whole; commands stream live (see <see cref="DeepgramLive"/>). The cat's
/// name goes in as a key term, so "Sky" comes back as "Sky".
/// </summary>
public sealed class Deepgram(Func<IEnumerable<string>> keyterms) : ISpeechToText
{
    public static readonly SecretStore Key = new("Shiki/DeepgramApiKey");
    public static bool HasKey => Key.Exists;

    /// <summary>Seconds of audio sent so far (for the running cost shown in Settings).</summary>
    public static event Action<double>? Sent;

    // Connections stay open between checks (a new one costs most of a second), for ten idle minutes.
    static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10) }) { Timeout = TimeSpan.FromSeconds(15) };

    public string Name => "Deepgram Nova-3";

    public Task Prepare(IProgress<double>? progress, CancellationToken ct) => Task.CompletedTask;

    public Task<string> Transcribe(float[] samples, string hint, CancellationToken ct) => Transcribe(samples, keyterms(), ct);

    public static async Task<string> Transcribe(float[] samples, IEnumerable<string> terms, CancellationToken ct, string? key = null)
    {
        key ??= Key.Read() ?? throw new InvalidOperationException("No Deepgram API key saved.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepgram.com/v1/listen?" + Query(terms))
        {
            Content = new ByteArrayContent(Wav(samples)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new DeepgramException(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        Sent?.Invoke(samples.Length / 16000.0);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("results").GetProperty("channels")[0].GetProperty("alternatives")[0].GetProperty("transcript").GetString()?.Trim() ?? "";
    }

    static DateTime _warmed;

    /// <summary>
    /// Opens the connection while someone is still talking, so the short clip that follows doesn't
    /// wait for it (a cold start is about a second slower). A free request; nothing is listened to.
    /// </summary>
    public static void Warm()
    {
        if (DateTime.UtcNow - _warmed < TimeSpan.FromSeconds(30) || Key.Read() is not { } key) return;
        _warmed = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepgram.com/v1/projects");
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Offline: the check itself will say so, and Whisper takes over.
            }
        });
    }

    /// <summary>Checks a key with half a second of silence. Null when it works.</summary>
    public static async Task<string?> TestKey(string key)
    {
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await Transcribe(new float[8000], [], cancel.Token, key);
            return null;
        }
        catch (DeepgramException ex)
        {
            return ex.Message;
        }
        catch (HttpRequestException)
        {
            return "I can't reach Deepgram. Check the internet connection.";
        }
        catch (TaskCanceledException)
        {
            return "Deepgram took too long to answer.";
        }
    }

    internal static string Query(IEnumerable<string> terms, string extra = "")
    {
        // "multi": Nova-3 follows Hindi and English as they mix in a sentence (Hinglish).
        var query = new StringBuilder($"model=nova-3&language={(Speech.Hindi ? "multi" : "en")}&smart_format=true");
        foreach (var term in terms.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(90))
            query.Append("&keyterm=").Append(Uri.EscapeDataString(term.Trim()));
        return query.Append(extra).ToString();
    }

    /// <summary>16 kHz mono floats as a little WAV file (16-bit PCM).</summary>
    static byte[] Wav(float[] samples)
    {
        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(16000);
        w.Write(32000);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write(Pcm(s));
        w.Flush();
        return stream.ToArray();
    }

    internal static short Pcm(float sample) => (short)Math.Clamp(sample * 32767f, short.MinValue, short.MaxValue);

    internal static void Report(double seconds) => Sent?.Invoke(seconds);
}

public sealed class DeepgramException(System.Net.HttpStatusCode status, string body) : Exception(Explain(status, body))
{
    static string Explain(System.Net.HttpStatusCode status, string body) => (int)status switch
    {
        401 or 403 => "Deepgram didn't accept the API key.",
        402 => "The Deepgram account is out of credit.",
        429 => "Deepgram says that's too many requests for now.",
        >= 500 => "Deepgram is having trouble right now.",
        _ => $"Deepgram couldn't do it ({(int)status}): {(body.Length > 160 ? body[..160] : body)}",
    };
}

/// <summary>
/// A live Deepgram stream for one spoken request: audio goes up as it's captured, the words come
/// back as you say them, and Deepgram says when you've finished (a pause it judges from the
/// words, not just the loudness, so stopping to think doesn't end it too early).
/// </summary>
public sealed class DeepgramLive : IAsyncDisposable
{
    readonly ClientWebSocket _socket = new();
    readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>();
    readonly List<string> _finals = [];
    readonly int _rate;
    Task? _sending, _receiving;
    long _bytes;
    string _interim = "";
    bool _ended;

    /// <summary>Everything heard so far, as it firms up. Raised on a background thread.</summary>
    public event Action<string>? Partial;

    /// <summary>Deepgram thinks the person has finished. Raised on a background thread.</summary>
    public event Action? Ended;

    DeepgramLive(int rate) => _rate = rate;

    public string Text => string.Join(" ", _finals).Trim();

    public static async Task<DeepgramLive> Open(string key, int sampleRate, IEnumerable<string> terms, CancellationToken ct)
    {
        var live = new DeepgramLive(sampleRate);
        live._socket.Options.SetRequestHeader("Authorization", "Token " + key);
        // Endpointing: 0.8 s of silence ends a phrase; UtteranceEnd: 1.2 s without new words. A sentence
        // that clearly isn't finished ("…and", "…my") gets 2.5 s more (see Handle).
        var url = "wss://api.deepgram.com/v1/listen?" + Deepgram.Query(terms,
            $"&encoding=linear16&sample_rate={sampleRate}&channels=1&interim_results=true&endpointing=500&utterance_end_ms=1200");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        await live._socket.ConnectAsync(new Uri(url), timeout.Token);
        live._sending = live.SendLoop();
        live._receiving = live.ReceiveLoop();
        return live;
    }

    /// <summary>Mono samples at the stream's rate, straight from the microphone.</summary>
    public void Send(float[] mono)
    {
        var bytes = new byte[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), Deepgram.Pcm(mono[i]));
        _outgoing.Writer.TryWrite(bytes);
    }

    /// <summary>No more audio: Deepgram sends its last words and closes. Returns everything it heard.</summary>
    public async Task<string> Finish()
    {
        _outgoing.Writer.TryComplete();
        try
        {
            if (_sending != null) await _sending;
            if (_receiving != null) await Task.WhenAny(_receiving, Task.Delay(3000));
        }
        catch (Exception ex)
        {
            Log.Error("Finishing the Deepgram stream", ex);
        }
        Deepgram.Report(_bytes / 2.0 / _rate);
        return Text.Length > 0 ? Text : _interim.Trim();
    }

    async Task SendLoop()
    {
        await foreach (var chunk in _outgoing.Reader.ReadAllAsync())
        {
            if (_socket.State != WebSocketState.Open) break;
            await _socket.SendAsync(chunk, WebSocketMessageType.Binary, true, CancellationToken.None);
            _bytes += chunk.Length;
        }
        if (_socket.State == WebSocketState.Open)
            await _socket.SendAsync("{\"type\":\"CloseStream\"}"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
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
            Log.Error("Deepgram stream dropped", ex);
        }
    }

    void Handle(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type == "Results")
        {
            var said = root.GetProperty("channel").GetProperty("alternatives")[0].GetProperty("transcript").GetString() ?? "";
            bool final = root.TryGetProperty("is_final", out var f) && f.GetBoolean();
            bool speechFinal = root.TryGetProperty("speech_final", out var sf) && sf.GetBoolean();
            if (final)
            {
                if (said.Length > 0) _finals.Add(said);
                _interim = "";
            }
            else _interim = said;
            Partial?.Invoke((Text + " " + _interim).Trim());
            // New words: a pending "it didn't sound finished" wait starts over.
            if (said.Length > 0) _waiting?.Cancel();
            if (speechFinal && _finals.Count > 0) EndUnlessUnfinished();
        }
        else if (type == "UtteranceEnd" && (_finals.Count > 0 || _interim.Length > 0)) EndUnlessUnfinished();
    }

    CancellationTokenSource? _waiting;

    // Words a sentence never ends on: a pause after them is someone thinking, not done.
    static readonly HashSet<string> Dangling = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "but", "so", "the", "a", "an", "to", "of", "my", "your", "with", "from", "in", "on", "at", "that", "which",
        "like", "uh", "um", "if", "then", "also", "into", "about", "by", "ki", "aur", "ko", "ka", "ke", "se", "mein", "toh", "ya", "par", "bhi",
        // A request that hasn't said what yet: "open Spotify and play…", "send it to…".
        "play", "open", "send", "call", "email", "message", "text", "tell", "show", "find", "search", "set", "remind", "add", "put", "make", "turn", "start",
    };

    // Words a sentence can end on ("…for me.", "…do it please."): unfinished only when Deepgram
    // didn't close the sentence (no full stop or question mark).
    static readonly HashSet<string> Soft = new(StringComparer.OrdinalIgnoreCase)
    {
        "me", "it", "this", "these", "please", "is", "are", "was", "will", "be", "for", "what", "there", "some", "i", "can", "could",
    };

    void EndUnlessUnfinished()
    {
        var text = (Text + " " + _interim).Trim().TrimEnd(',', ' ');
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var last = words.LastOrDefault()?.Trim('.', ',', '?', '!') ?? "";
        var lastTwo = words.Length >= 2 ? $"{words[^2].Trim('.', ',', '?', '!')} {last}".ToLowerInvariant() : "";
        bool closed = text.EndsWith('.') || text.EndsWith('?') || text.EndsWith('!');
        bool unfinished = text.EndsWith(',') || words.Length < 2 || Dangling.Contains(last)
            || lastTwo is "can you" or "could you" or "will you" or "would you" or "do you"
            || Soft.Contains(last) && !closed;
        if (!unfinished)
        {
            End();
            return;
        }
        // Give them a moment more; new words cancel this and the next pause decides again.
        _waiting?.Cancel();
        var waiting = _waiting = new CancellationTokenSource();
        _ = Task.Delay(2500, waiting.Token).ContinueWith(t => { if (!t.IsCanceled) End(); }, TaskScheduler.Default);
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
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch (Exception)
        {
            // Already gone.
        }
        _socket.Dispose();
    }
}
