using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Shiki.Core.Voice;

/// <summary>
/// Is this "Hey Latte"? Compares the start of what Whisper wrote down with the ways it wrote the
/// name while the person taught it (and a few plain greetings), allowing small spelling slips.
/// </summary>
public static class WakeMatcher
{
    static readonly string[] Greetings = ["hey", "hi", "hello", "okay", "ok"];

    /// <summary>Lower case, letters and digits only, single spaces: "Hey, Latte!" becomes "hey latte".</summary>
    public static string Normalize(string text) =>
        string.Join(" ", Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9 ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The phrases that count as calling the cat.</summary>
    public static IEnumerable<string> Phrases(string name, IEnumerable<string> learned)
    {
        var n = Normalize(name);
        return learned.Select(Normalize).Concat(Greetings.Select(g => $"{g} {n}")).Where(p => p.Length > 0).Distinct();
    }

    /// <returns>Hit when it starts with the cat's name; After is whatever was said after it ("merge my PDFs").</returns>
    public static (bool Hit, string After) Match(string heard, string name, IEnumerable<string> learned)
    {
        var words = heard.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var plain = words.Select(Normalize).ToList();
        var phrases = Phrases(name, learned).Select(p => p.Replace(" ", "")).ToList();
        int used = 0;
        // Whisper may split or join words ("hey lot a", "heylatte"): try the first one to four words glued together.
        for (int k = 1; k <= Math.Min(4, words.Length) && used == 0; k++)
        {
            var start = string.Concat(plain.Take(k));
            if (start.Length == 0) continue;
            if (phrases.Any(phrase => Distance(start, phrase) <= Slack(phrase))) used = k;
        }
        if (used == 0) return (false, "");
        var rest = string.Join(" ", words.Skip(used)).TrimStart(',', '.', '!', '?', ' ', '-');
        return (true, rest.Length > 1 ? char.ToUpperInvariant(rest[0]) + rest[1..] : rest);
    }

    /// <summary>
    /// Spelling slips allowed: none for short phrases ("heymax" mustn't match "heyman"), one for
    /// most ("heylatte" / "heylattee"), two for long ones.
    /// </summary>
    static int Slack(string phrase) => phrase.Length >= 12 ? 2 : phrase.Length >= 7 ? 1 : 0;

    static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int previous = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int keep = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = keep;
            }
        }
        return row[b.Length];
    }
}

/// <summary>
/// Listens all the time for "Hey &lt;cat name&gt;", entirely on this PC. A small voice detector cuts
/// the microphone into short bursts of speech; only bursts short enough to be a call (under six
/// seconds) are written down by Whisper and checked. What it hears is never saved or sent; only
/// a match wakes the cat. Long talk (a call, a video) is skipped without being written down.
/// </summary>
public sealed class WakeListener(Func<string> name, Func<IReadOnlyList<string>> learned, Func<bool> paused, Func<double?>? ownerOnly = null) : IDisposable
{
    // A short pause ends a burst: "Hey Sky" is checked about a third of a second after you say it.
    const double FrameSeconds = 0.03, PreRoll = 0.3, EndPause = 0.33, MinSpeech = 0.3, MaxSpeech = 6;

    readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    readonly object _gate = new();
    WasapiCapture? _capture;
    Timer? _retry;
    bool _running;

    // The detector's state (audio thread).
    readonly Queue<float> _before = new();
    readonly List<float> _speech = [];
    int _rate, _frame, _frameCount, _loudFrames;
    float _frameSum;
    double _floor = -1, _quiet, _talking, _elapsed;
    bool _inSpeech, _tooLong;
    int _checking;

    /// <summary>The name was heard; the text is what came after it (empty for just "Hey Latte").</summary>
    public event Action<string>? Woke;

    /// <summary>The name was heard with more after it, which is still being written down.</summary>
    public event Action? Waking;

    public bool Running => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        Open();
    }

    public void Stop()
    {
        _running = false;
        _retry?.Dispose();
        _retry = null;
        lock (_gate)
        {
            var capture = _capture;
            _capture = null;
            if (capture == null) return;
            try { capture.StopRecording(); } catch (Exception ex) { Log.Error("Stopping the wake word", ex); }
            capture.Dispose();
        }
    }

    void Open()
    {
        try
        {
            var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            var capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };
            _rate = capture.WaveFormat.SampleRate;
            _frame = (int)(_rate * FrameSeconds);
            ResetDetector();
            capture.DataAvailable += OnData;
            // The mic was unplugged or switched: try again shortly with whatever is the default now.
            capture.RecordingStopped += (_, e) =>
            {
                if (!_running) return;
                if (e.Exception != null) Log.Error("Wake word microphone stopped", e.Exception);
                RetrySoon();
            };
            lock (_gate) _capture = capture;
            capture.StartRecording();
            Log.Info($"Wake word listening ({device.FriendlyName}, {_rate} Hz)");
        }
        catch (Exception ex)
        {
            Log.Error("Wake word couldn't open the microphone", ex);
            RetrySoon();
        }
    }

    void RetrySoon()
    {
        lock (_gate)
        {
            _capture?.Dispose();
            _capture = null;
        }
        _retry?.Dispose();
        _retry = new Timer(_ => { if (_running) Open(); }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
    }

    void ResetDetector()
    {
        _before.Clear();
        _speech.Clear();
        _frameCount = 0;
        _frameSum = 0;
        _loudFrames = 0;
        _quiet = _talking = _elapsed = 0;
        _floor = -1;
        _inSpeech = _tooLong = false;
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiCapture capture) return;
        if (paused())
        {
            // While the cat is already listening, recording the screen or thinking: hear nothing.
            if (_inSpeech || _speech.Count > 0) ResetDetector();
            return;
        }
        var format = capture.WaveFormat;
        int count = e.BytesRecorded / (format.BitsPerSample / 8) / format.Channels;
        int keepBefore = (int)(_rate * PreRoll);
        for (int i = 0; i < count; i++)
        {
            float sample = Microphone.MonoSample(format, e.Buffer, i);
            if (_inSpeech)
            {
                if (!_tooLong) _speech.Add(sample);
            }
            else
            {
                _before.Enqueue(sample);
                if (_before.Count > keepBefore) _before.Dequeue();
            }
            _frameSum += sample * sample;
            if (++_frameCount == _frame) EndFrame();
        }
    }

    void EndFrame()
    {
        double rms = Math.Sqrt(_frameSum / _frameCount);
        _frameSum = 0;
        _frameCount = 0;
        _elapsed += FrameSeconds;
        if (_elapsed <= 0.5)
        {
            _floor = _floor < 0 ? rms : Math.Min(_floor, rms);
            return;
        }
        _floor = Math.Min(_floor, 0.015);
        double threshold = Math.Max(_floor * 3, 0.01);
        bool loud = rms > threshold;

        if (!_inSpeech)
        {
            // Rooms get louder and quieter; the floor follows while nobody's talking.
            _floor = _floor * 0.98 + Math.Min(rms, threshold) * 0.02;
            _loudFrames = loud ? _loudFrames + 1 : 0;
            if (_loudFrames >= 3)
            {
                // Someone's talking: get the connection ready, so "Hey Sky" is checked the moment they pause.
                if (Speech.WakeOnDeepgram && Deepgram.HasKey) Deepgram.Warm();
                _inSpeech = true;
                _talking = _loudFrames * FrameSeconds;
                _quiet = 0;
                _speech.Clear();
                _speech.AddRange(_before);
                _before.Clear();
            }
            return;
        }

        _talking += FrameSeconds;
        _quiet = loud ? 0 : _quiet + FrameSeconds;
        if (_talking - _quiet > MaxSpeech) _tooLong = true;
        if (_quiet < EndPause) return;

        // A burst of speech has ended: check it if it's the right length and nothing else is being checked.
        double spoken = _talking - _quiet;
        bool worthChecking = !_tooLong && spoken >= MinSpeech && Interlocked.CompareExchange(ref _checking, 1, 0) == 0;
        var samples = worthChecking ? _speech.ToArray() : null;
        _inSpeech = _tooLong = false;
        _speech.Clear();
        _loudFrames = 0;
        if (samples != null) _ = Check(Microphone.Resample(samples, _rate));
    }

    async Task Check(float[] samples)
    {
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var text = await Recognize(samples, name(), CancellationToken.None);
            if (text.Length == 0) return;
            var (hit, rest) = WakeMatcher.Match(text, name(), learned());
            if (!hit) return;
            // Only the owner: the name said in someone else's voice (a video, a friend) is ignored.
            if (ownerOnly?.Invoke() is { } enough && VoicePrint.Likeness(samples) is { } likeness && likeness < enough)
            {
                Ai.AiLog.Write("ignored", $"heard the name, but not in your voice ({likeness:0.00} < {enough:0.00})");
                return;
            }
            long check = watch.ElapsedMilliseconds;
            if (rest.Length > 0)
            {
                // Wake the cat now; the request follows once it's written down in full.
                _ui.Post(_ => Waking?.Invoke(), null);
                rest = await RequestAfterName(samples, name(), learned(), rest, CancellationToken.None);
            }
            Ai.AiLog.Write("woke", (rest.Length > 0 ? $"heard the name, then: {rest}" : "heard the name") + $" ({check} ms to check)");
            _ui.Post(_ => { if (_running && !paused()) Woke?.Invoke(rest); }, null);
        }
        catch (Exception ex)
        {
            Log.Error("Wake word check failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    /// <summary>
    /// How the wake word hears: short-clip Whisper with no hint. Teaching uses the same, so the
    /// spellings it learns are the ones it will see.
    /// </summary>
    /// <remarks>
    /// The name goes in as a hint so Whisper spells it the same way each time ("Hey Sky", not
    /// "he s kai" one time and "face guy" the next). Half the usual window keeps it quick
    /// (0.1 to 0.3 s on the graphics card) without garbling real voices.
    /// </remarks>
    public static async Task<string> Recognize(float[] samples, string name, CancellationToken ct)
    {
        if (Speech.WakeOnDeepgram && Deepgram.HasKey)
        {
            try
            {
                return await Deepgram.Transcribe(samples, [$"Hey {name}", name], ct);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or DeepgramException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Log.Error("Deepgram wake check failed; using Whisper", ex);
            }
        }
        return await Speech.Whisper.Transcribe(samples, $"Hey {name}.", ct, threads: Math.Clamp(Environment.ProcessorCount - 1, 2, 8),
            shortClip: true, maxTokens: 10);
    }

    /// <summary>
    /// "Hey Sky, merge my PDFs": the quick check only keeps the first few words, so what came after
    /// the name is written down again properly before it goes to the cat.
    /// </summary>
    public static async Task<string> RequestAfterName(float[] samples, string name, IEnumerable<string> learned, string quickRest, CancellationToken ct)
    {
        // Deepgram already wrote the whole burst down; only the capped Whisper check needs a second pass.
        if (Speech.WakeOnDeepgram && Deepgram.HasKey) return quickRest;
        var full = await Speech.Local.Transcribe(samples, $"Hey {name}. Convert, merge, compress, PDF, JPG, Downloads, Desktop.", ct);
        var (hit, rest) = WakeMatcher.Match(full, name, learned);
        return hit && rest.Length >= quickRest.Length ? rest : quickRest;
    }

    public void Dispose() => Stop();
}
