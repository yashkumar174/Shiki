using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Shiki.Core.Ai;

namespace Shiki.Core.Voice;

/// <summary>
/// The cat's own voice: replies read aloud with a Windows voice (free, on this PC) or a Deepgram
/// Aura voice (natural, with the person's key). While it talks the microphones stand aside, so it
/// never hears itself; pressing the mic or the shortcut cuts it off.
/// Raises Changed on the thread it was made on (the UI thread).
/// </summary>
public sealed class Speaker
{
    readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    CancellationTokenSource? _cancel;
    TaskCompletionSource? _done;

    // One output kept open while a conversation is going (silence between replies), so the sound
    // device is awake and a reply starts the moment its first sound arrives. Closed when idle.
    static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(24000, 1);
    readonly object _lineLock = new();
    WasapiOut? _line;
    MixingSampleProvider? _mixer;
    Timer? _idle;

    public bool Speaking { get; private set; }
    /// <summary>Reading a whole document (not just a reply): replies shouldn't cut in.</summary>
    public bool Reading { get; private set; }
    public event Action? Changed;

    /// <summary>Characters sent to Deepgram so far (for the running cost).</summary>
    public static event Action<int>? Sent;

    public sealed record Voice(string Id, string Label);

    // Deepgram Aura-2 English voices: a few that read well for short replies.
    static readonly (string Model, string Label)[] AuraVoices =
    [
        ("aura-2-thalia-en", "Thalia"), ("aura-2-andromeda-en", "Andromeda"), ("aura-2-helena-en", "Helena"),
        ("aura-2-apollo-en", "Apollo"), ("aura-2-arcas-en", "Arcas"), ("aura-2-luna-en", "Luna"),
    ];

    /// <summary>The voices to choose from: Windows' own (English first), and Deepgram's when there's a key.</summary>
    public static List<Voice> Voices()
    {
        var list = new List<Voice>();
        try
        {
            foreach (var v in Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices
                         .OrderBy(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(v => v.DisplayName))
                list.Add(new Voice("win:" + v.Id, $"{v.DisplayName.Replace("Microsoft ", "")} ({v.Language})"));
        }
        catch (Exception ex)
        {
            Log.Error("Could not list the Windows voices", ex);
        }
        if (Deepgram.HasKey) list.InsertRange(0, AuraVoices.Select(a => new Voice("dg:" + a.Model, $"{a.Label} (natural)")));
        return list;
    }

    /// <summary>The voice to use: the one chosen if it's still there, else Deepgram's Thalia with a key, else Windows' default.</summary>
    public static string Pick(string chosen)
    {
        if (chosen.StartsWith("dg:") && Deepgram.HasKey) return chosen;
        if (chosen.StartsWith("win:") && Voices().Any(v => v.Id == chosen)) return chosen;
        return Deepgram.HasKey ? "dg:" + AuraVoices[0].Model : "win:";
    }

    /// <summary>Waits until the cat has finished talking (and a moment more, so the room is quiet).</summary>
    public async Task Quiet()
    {
        if (_done is { } done)
        {
            await done.Task;
            await Task.Delay(250);
        }
    }

    /// <summary>Stops talking now.</summary>
    public void Stop()
    {
        _cancel?.Cancel();
        lock (_lineLock) _mixer?.RemoveAllMixerInputs();
    }

    /// <summary>
    /// Wakes the speakers before a reply is due (the person is talking to the cat), so the first
    /// words aren't lost to a sleeping sound card. Stays awake a little while, then lets go.
    /// </summary>
    public void Warm()
    {
        try
        {
            Line();
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't open the speakers", ex);
        }
    }

    MixingSampleProvider Line()
    {
        lock (_lineLock)
        {
            if (_line == null || _mixer == null)
            {
                _mixer = new MixingSampleProvider(Format) { ReadFully = true };
                _line = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 80);
                _line.Init(_mixer);
                _line.Play();
            }
            _idle ??= new Timer(_ => CloseIfIdle());
            _idle.Change(TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
            return _mixer;
        }
    }

    void CloseIfIdle()
    {
        lock (_lineLock)
        {
            if (Speaking)
            {
                _idle?.Change(TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
                return;
            }
            _line?.Dispose();
            _line = null;
            _mixer = null;
        }
    }

    /// <summary>Reads the text aloud with the voice; returns when it's done (or stopped).</summary>
    /// <param name="full">All of it (reading a document), in pieces, rather than a reply's first sentences.</param>
    public async Task Say(string text, string voice, bool full = false)
    {
        Stop();
        text = Readable(text, full ? int.MaxValue : 320);
        if (text.Length == 0) return;
        var cancel = _cancel = new CancellationTokenSource(full ? TimeSpan.FromHours(3) : TimeSpan.FromSeconds(60));
        var done = _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Speaking = true;
        Reading = full;
        Changed?.Invoke();
        try
        {
            voice = Pick(voice);
            var mixer = Line();
            // Each piece starts streaming at once: Deepgram's sound plays as it arrives, and the
            // next piece downloads while this one plays. A Windows voice is made whole (it's quick).
            async Task<Sound> Start(string piece)
            {
                if (voice.StartsWith("dg:"))
                {
                    try
                    {
                        return await AuraStream(piece, voice[3..], cancel.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Offline or out of credit: a Windows voice carries on.
                        Log.Error("Deepgram's voice failed; using a Windows voice", ex);
                        voice = "win:";
                    }
                }
                return Sound.FromWav(await WindowsVoice(piece, voice[4..]));
            }
            // Deepgram takes up to 2,000 characters at a time.
            var pieces = Pieces(text, 1500);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var next = Start(pieces[0]);
            for (int i = 0; i < pieces.Count; i++)
            {
                var sound = await next;
                cancel.Token.ThrowIfCancellationRequested();
                if (i == 0) Log.Info($"Speaking {text.Length} characters in {pieces.Count} piece{(pieces.Count == 1 ? "" : "s")} ({voice}), first sound after {clock.ElapsedMilliseconds} ms");
                lock (_lineLock) mixer.AddMixerInput(sound);
                if (i + 1 < pieces.Count) next = Start(pieces[i + 1]);
                using (cancel.Token.Register(() => sound.Done.TrySetResult()))
                    await sound.Done.Task;
                cancel.Token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (Exception ex)
        {
            Log.Error("The cat couldn't speak", ex);
        }
        finally
        {
            if (_cancel == cancel)
            {
                Speaking = false;
                Reading = false;
                _cancel = null;
                _done = null;
                _ui.Post(_ => Changed?.Invoke(), null);
            }
            done.TrySetResult();
        }
    }

    /// <summary>The text in pieces of about <paramref name="size"/> characters, split between sentences.</summary>
    static List<string> Pieces(string text, int size)
    {
        var pieces = new List<string>();
        var piece = new StringBuilder();
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?])\s+"))
        {
            if (piece.Length + sentence.Length > size && piece.Length > 0)
            {
                pieces.Add(piece.ToString().Trim());
                piece.Clear();
            }
            // A single very long "sentence" (a table, a list) is cut at a space.
            var rest = sentence;
            while (rest.Length > size)
            {
                int cut = rest.LastIndexOf(' ', size);
                if (cut <= 0) cut = size;
                pieces.Add(rest[..cut].Trim());
                rest = rest[cut..];
            }
            piece.Append(rest).Append(' ');
        }
        if (piece.ToString().Trim().Length > 0) pieces.Add(piece.ToString().Trim());
        return pieces;
    }

    /// <summary>
    /// What reads well aloud: no file paths or long numbered steps. A long reply is cut to its
    /// first sentences; the rest is on screen.
    /// </summary>
    public static string Readable(string text, int most = 320)
    {
        text = Regex.Replace(text, @"[A-Za-z]:\\[^\s,;]+", m => Path.GetFileName(m.Value.TrimEnd('.', '\\')));
        text = Regex.Replace(text, @"https?://\S+", "the link");
        text = Regex.Replace(text, @"\s*\n+\s*", " ").Trim();
        text = Regex.Replace(text, @"\s{2,}", " ");
        if (text.Length <= most) return text;
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        var said = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (said.Length + sentence.Length > most && said.Length > 0) break;
            said.Append(sentence).Append(' ');
        }
        return said.ToString().Trim() + " The rest is on screen.";
    }

    /// <summary>The spoken reply as a WAV, without playing it (for tests).</summary>
    public static Task<byte[]> Render(string text, string voice)
    {
        voice = Pick(voice);
        text = Readable(text);
        return voice.StartsWith("dg:") ? Aura(text, voice[3..], CancellationToken.None) : WindowsVoice(text, voice[4..]);
    }

    static async Task<byte[]> WindowsVoice(string text, string id)
    {
        using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        if (id.Length > 0 && Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == id) is { } chosen)
            synth.Voice = chosen;
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        using var read = stream.AsStreamForRead();
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);
        return copy.ToArray();
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static async Task<byte[]> Aura(string text, string model, CancellationToken ct)
    {
        var key = Deepgram.Key.Read() ?? throw new InvalidOperationException("No Deepgram key.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.deepgram.com/v1/speak?model={model}&encoding=linear16&sample_rate=24000&container=wav")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new DeepgramException(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        Sent?.Invoke(text.Length);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Deepgram's voice as raw sound, handed to the speakers bit by bit as it arrives.</summary>
    static async Task<Sound> AuraStream(string text, string model, CancellationToken ct)
    {
        var key = Deepgram.Key.Read() ?? throw new InvalidOperationException("No Deepgram key.");
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.deepgram.com/v1/speak?model={model}&encoding=linear16&sample_rate=24000&container=none")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
        var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            request.Dispose();
            throw new DeepgramException(response.StatusCode, body);
        }
        Sent?.Invoke(text.Length);
        var sound = new Sound(new WaveFormat(24000, 16, 1));
        _ = Task.Run(async () =>
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[8192];
                int read, odd = 0;
                var carry = new byte[1];
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    // Keep whole 16-bit samples together.
                    int start = 0;
                    if (odd == 1) { sound.Add([carry[0], buffer[0]], 2); start = 1; odd = 0; }
                    int whole = (read - start) & ~1;
                    if (whole > 0) sound.Add(buffer[start..(start + whole)], whole);
                    if ((read - start) % 2 == 1) { carry[0] = buffer[read - 1]; odd = 1; }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException)
            {
                // Stopped, or the connection dropped: what arrived still plays.
            }
            finally
            {
                sound.Complete();
                response.Dispose();
                request.Dispose();
            }
        }, CancellationToken.None);
        return sound;
    }

    /// <summary>
    /// A sound for the speakers, in the shared format: silence while more is on its way, and the
    /// end (which lets the next piece start) once it has all been played.
    /// </summary>
    sealed class Sound : ISampleProvider
    {
        readonly BufferedWaveProvider? _buffer;
        readonly ISampleProvider _samples;
        volatile bool _complete;

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WaveFormat WaveFormat => Format;

        public Sound(WaveFormat incoming)
        {
            _buffer = new BufferedWaveProvider(incoming) { BufferDuration = TimeSpan.FromMinutes(10), ReadFully = false };
            _samples = Fit(_buffer.ToSampleProvider());
        }

        Sound(ISampleProvider whole)
        {
            _samples = Fit(whole);
            _complete = true;
        }

        /// <summary>A whole WAV (a Windows voice).</summary>
        public static Sound FromWav(byte[] wav)
        {
            var reader = new WaveFileReader(new MemoryStream(wav));
            return new Sound(reader.ToSampleProvider());
        }

        static ISampleProvider Fit(ISampleProvider source)
        {
            if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
            return source.WaveFormat.SampleRate == Format.SampleRate ? source : new WdlResamplingSampleProvider(source, Format.SampleRate);
        }

        public void Add(byte[] bytes, int count) => _buffer!.AddSamples(bytes, 0, count);

        public void Complete() => _complete = true;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _samples.Read(buffer, offset, count);
            if (n == count) return n;
            bool drained = _buffer == null || _buffer.BufferedBytes == 0;
            if (_complete && drained)
            {
                // All played: the mixer drops this sound, and the next piece can start.
                Done.TrySetResult();
                return n;
            }
            // Waiting for more: silence, so the speakers keep going.
            Array.Clear(buffer, offset + n, count - n);
            return count;
        }
    }
}
