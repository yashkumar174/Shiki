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
using Shiki.Core.Voice;

namespace Shiki.Core.Meetings;

/// <summary>Turns a meeting's two channels (you, them) into lines of who said what.</summary>
public interface IMeetingTranscriber : IAsyncDisposable
{
    string Name { get; }
    /// <summary>A finished line. Raised on a background thread.</summary>
    event Action<MeetingLine>? Line;
    /// <summary>What's being said right now on a channel (0 you, 1 them), before it's final.</summary>
    event Action<int, string>? Partial;
    void Send(byte[] stereo);
    /// <summary>No more sound: wait for the last words.</summary>
    Task FinishAsync();
}

/// <summary>
/// Deepgram Nova-3, live, with both channels kept apart (multichannel) and the other side's
/// voices told apart (diarize). If the connection drops it reconnects and carries on, keeping
/// the times right. Billed per channel, so a meeting costs about twice a command per minute.
/// </summary>
public sealed class DeepgramMeeting : IMeetingTranscriber
{
    readonly string _key;
    readonly IEnumerable<string> _terms;
    readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>();
    readonly CancellationTokenSource _stop = new();
    Task? _run;
    long _bytesSent;
    bool _finishing;

    public string Name => "Deepgram";
    public event Action<MeetingLine>? Line;
    public event Action<int, string>? Partial;

    public DeepgramMeeting(string key, IEnumerable<string> terms)
    {
        _key = key;
        _terms = terms;
        _run = Task.Run(Run);
    }

    public void Send(byte[] stereo) => _audio.Writer.TryWrite(stereo);

    public async Task FinishAsync()
    {
        _finishing = true;
        _audio.Writer.TryComplete();
        if (_run != null) await Task.WhenAny(_run, Task.Delay(8000));
    }

    async Task Run()
    {
        int failures = 0;
        while (!_stop.IsCancellationRequested)
        {
            // Times in a reconnected stream start from zero again: offset them by what was sent before.
            double offset = _bytesSent / 4.0 / MeetingAudio.Rate;
            using var socket = new ClientWebSocket();
            try
            {
                socket.Options.SetRequestHeader("Authorization", "Token " + _key);
                var url = "wss://api.deepgram.com/v1/listen?" + Deepgram.Query(_terms,
                    $"&encoding=linear16&sample_rate={MeetingAudio.Rate}&channels=2&multichannel=true&diarize=true&interim_results=true&endpointing=600");
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    await socket.ConnectAsync(new Uri(url), timeout.Token);
                failures = 0;
                var receiving = Receive(socket, offset);
                await foreach (var chunk in _audio.Reader.ReadAllAsync(_stop.Token))
                {
                    if (socket.State != WebSocketState.Open) throw new WebSocketException("closed");
                    await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, _stop.Token);
                    _bytesSent += chunk.Length;
                }
                // All sent: ask for the last words, then wait for Deepgram to close.
                await socket.SendAsync("{\"type\":\"CloseStream\"}"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
                await Task.WhenAny(receiving, Task.Delay(6000));
                Deepgram.Report(_bytesSent / 4.0 / MeetingAudio.Rate * 2);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (_finishing && _audio.Reader.Completion.IsCompleted) return;
                failures++;
                Log.Error($"Meeting stream dropped (try {failures})", ex);
                // Back off a little; the sound keeps queueing meanwhile, so nothing is lost.
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * failures)), _stop.Token); } catch (OperationCanceledException) { return; }
            }
        }
    }

    async Task Receive(ClientWebSocket socket, double offset)
    {
        var buffer = new byte[128 * 1024];
        var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                try { Handle(message.ToArray(), offset); } catch (Exception ex) { Log.Error("Reading a meeting result", ex); }
                message.SetLength(0);
            }
        }
        catch (WebSocketException)
        {
            // The send side notices and reconnects.
        }
    }

    void Handle(byte[] json, double offset)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "Results") return;
        int channel = root.TryGetProperty("channel_index", out var ci) && ci.ValueKind == JsonValueKind.Array && ci.GetArrayLength() > 0 ? ci[0].GetInt32() : 0;
        var alternative = root.GetProperty("channel").GetProperty("alternatives")[0];
        var transcript = alternative.GetProperty("transcript").GetString() ?? "";
        bool final = root.TryGetProperty("is_final", out var f) && f.GetBoolean();
        if (!final)
        {
            Partial?.Invoke(channel, transcript);
            return;
        }
        Partial?.Invoke(channel, "");
        if (transcript.Trim().Length == 0) return;
        double start = offset + (root.TryGetProperty("start", out var s) ? s.GetDouble() : 0);
        if (channel == 0)
        {
            Line?.Invoke(new MeetingLine { At = start, Speaker = "You", Text = transcript.Trim() });
            return;
        }
        // The other side: split by voice.
        if (!alternative.TryGetProperty("words", out var words) || words.GetArrayLength() == 0)
        {
            Line?.Invoke(new MeetingLine { At = start, Speaker = "Speaker 1", Text = transcript.Trim() });
            return;
        }
        int? speaker = null;
        double at = start;
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0) Line?.Invoke(new MeetingLine { At = at, Speaker = $"Speaker {(speaker ?? 0) + 1}", Text = text.ToString().Trim() });
            text.Clear();
        }
        foreach (var word in words.EnumerateArray())
        {
            int who = word.TryGetProperty("speaker", out var sp) ? sp.GetInt32() : 0;
            if (speaker != who)
            {
                Flush();
                speaker = who;
                at = offset + (word.TryGetProperty("start", out var ws) ? ws.GetDouble() : start - offset);
            }
            text.Append(word.TryGetProperty("punctuated_word", out var pw) ? pw.GetString() : word.GetProperty("word").GetString()).Append(' ');
        }
        Flush();
    }

    public async ValueTask DisposeAsync()
    {
        _audio.Writer.TryComplete();
        _stop.Cancel();
        if (_run != null) await Task.WhenAny(_run, Task.Delay(2000));
    }
}

/// <summary>
/// Whisper on this PC, when there's no Deepgram (no key, or offline). Each side is written down
/// in pieces of up to 15 seconds, cut at a pause where possible; quiet pieces are skipped.
/// Everyone on the other side is "Speaker 1": Whisper can't tell voices apart.
/// </summary>
public sealed class WhisperMeeting : IMeetingTranscriber
{
    const int MaxPiece = MeetingAudio.Rate * 15, MinPiece = MeetingAudio.Rate * 4;
    readonly List<float>[] _sides = [[], []];
    readonly double[] _sideStart = [0, 0];
    readonly Channel<(int Side, float[] Samples, double At)> _work = Channel.CreateUnbounded<(int, float[], double)>();
    readonly Task _worker;
    long _samples;

    public string Name => "Whisper";
    public event Action<MeetingLine>? Line;
    public event Action<int, string>? Partial;

    public WhisperMeeting() => _worker = Task.Run(Work);

    public void Send(byte[] stereo)
    {
        int n = stereo.Length / 4;
        for (int i = 0; i < n; i++)
        {
            _sides[0].Add(BitConverter.ToInt16(stereo, i * 4) / 32768f);
            _sides[1].Add(BitConverter.ToInt16(stereo, i * 4 + 2) / 32768f);
        }
        _samples += n;
        for (int side = 0; side < 2; side++) Cut(side, force: false);
    }

    /// <summary>A piece is ready when it's long enough and the last half second is quiet, or at 15 seconds.</summary>
    void Cut(int side, bool force)
    {
        var buffer = _sides[side];
        if (buffer.Count == 0) return;
        bool ready = force || buffer.Count >= MaxPiece || (buffer.Count >= MinPiece && Rms(buffer, buffer.Count - MeetingAudio.Rate / 2, buffer.Count) < 0.01);
        if (!ready) return;
        var piece = buffer.ToArray();
        double at = _sideStart[side];
        buffer.Clear();
        _sideStart[side] = _samples / (double)MeetingAudio.Rate;
        // Nobody spoke: nothing to write down.
        if (Rms(piece, 0, piece.Length) < 0.006) return;
        _work.Writer.TryWrite((side, piece, at));
    }

    static double Rms(IList<float> samples, int from, int to)
    {
        double sum = 0;
        from = Math.Max(0, from);
        for (int i = from; i < to; i++) sum += samples[i] * samples[i];
        return to > from ? Math.Sqrt(sum / (to - from)) : 0;
    }

    async Task Work()
    {
        await foreach (var (side, samples, at) in _work.Reader.ReadAllAsync())
        {
            try
            {
                Partial?.Invoke(side, "…");
                var text = await Speech.Local.Transcribe(samples, "", CancellationToken.None);
                Partial?.Invoke(side, "");
                if (text.Length > 0) Line?.Invoke(new MeetingLine { At = at, Speaker = side == 0 ? "You" : "Speaker 1", Text = text });
            }
            catch (Exception ex)
            {
                Log.Error("Whisper meeting piece failed", ex);
            }
        }
    }

    public async Task FinishAsync()
    {
        Cut(0, force: true);
        Cut(1, force: true);
        _work.Writer.TryComplete();
        await _worker;
    }

    public ValueTask DisposeAsync()
    {
        _work.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
