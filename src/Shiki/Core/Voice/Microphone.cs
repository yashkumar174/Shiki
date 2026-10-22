using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Shiki.Core.Voice;

/// <summary>How a listen ended.</summary>
public enum HeardKind { Speech, Nothing, Stopped }

public sealed record Heard(HeardKind Kind, float[] Samples);

/// <summary>
/// Listens once: opens the default microphone, waits for speech, and stops by itself after a
/// short pause (or when told to). The microphone is only open while listening. Gives back mono
/// 16 kHz audio, the way Whisper wants it.
/// </summary>
public sealed class Microphone : IDisposable
{
    const double FrameSeconds = 0.03;
    const double MaxLength = 30;
    // How long to wait for anyone to start talking before giving up.
    readonly double _waitForSpeech;
    // How long a pause ends the listen: long enough to think between words, short enough not to wait around.
    readonly double _pauseEnds;

    readonly WasapiCapture _capture;
    readonly List<float> _mono = [];
    readonly TaskCompletionSource<Heard> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly int _rate, _channels, _frame;
    readonly object _gate = new();
    float _frameSum;
    int _frameCount;
    double _floor = -1, _heardSeconds, _quietSeconds, _elapsed;
    bool _speaking, _finished;
    int _speechFrames;

    /// <summary>Loudness of the last frame, 0 to 1, for a meter. Raised on the audio thread.</summary>
    public event Action<double>? Level;

    /// <summary>Raised once speech is detected (the meter can switch from "waiting" to "hearing").</summary>
    public event Action? SpeechStarted;

    Action<float[]>? _sink;

    public int SampleRate => _rate;

    /// <param name="pauseEnds">Seconds of quiet after speech that end the listen.</param>
    /// <param name="waitForSpeech">Seconds to wait for speech to start before giving up.</param>
    public Microphone(double pauseEnds = 1.9, double waitForSpeech = 7)
    {
        _pauseEnds = pauseEnds;
        _waitForSpeech = waitForSpeech;
        var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        _capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };
        _rate = _capture.WaveFormat.SampleRate;
        _channels = _capture.WaveFormat.Channels;
        _frame = (int)(_rate * FrameSeconds);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null) _done.TrySetException(e.Exception);
            else Finish(_speechFrames > 3 ? HeardKind.Speech : HeardKind.Nothing);
        };
    }

    /// <summary>Is there a microphone at all?</summary>
    public static bool Present()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            return false;
        }
    }

    public Task<Heard> Listen(CancellationToken ct)
    {
        ct.Register(Stop);
        _capture.StartRecording();
        return _done.Task;
    }

    /// <summary>Stop now and keep what was said so far.</summary>
    public void Stop()
    {
        try { _capture.StopRecording(); } catch (Exception ex) { Log.Error("Stopping the microphone", ex); }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture.WaveFormat;
        int bytesPerSample = format.BitsPerSample / 8;
        int count = e.BytesRecorded / bytesPerSample / _channels;
        lock (_gate)
        {
            if (_finished) return;
            var chunk = _sink != null ? new float[count] : null;
            for (int i = 0; i < count; i++)
            {
                float sample = MonoSample(format, e.Buffer, i);
                _mono.Add(sample);
                if (chunk != null) chunk[i] = sample;
                _frameSum += sample * sample;
                if (++_frameCount == _frame) EndFrame();
            }
            // Under the lock, so nothing is sent twice or skipped when streaming starts late.
            if (chunk != null) _sink!(chunk);
        }
    }

    /// <summary>
    /// Streams what's heard (mono, at <see cref="SampleRate"/>) to the sink: everything so far at
    /// once, then each piece as it comes. Lets the cloud connect while you're already talking.
    /// </summary>
    public void StreamTo(Action<float[]> sink)
    {
        lock (_gate)
        {
            if (_mono.Count > 0) sink(_mono.ToArray());
            _sink = sink;
        }
    }

    /// <summary>
    /// A small voice detector: the first few frames set the room's noise level, speech is clearly
    /// louder than that, and a pause after speech ends the listen.
    /// </summary>
    void EndFrame()
    {
        double rms = Math.Sqrt(_frameSum / _frameCount);
        _frameSum = 0;
        _frameCount = 0;
        _elapsed += FrameSeconds;
        Level?.Invoke(Math.Clamp(rms * 12, 0, 1));

        if (_elapsed <= 0.25)
        {
            // The room's level is its quietest moment here, so talking straight away isn't taken for noise.
            _floor = _floor < 0 ? rms : Math.Min(_floor, rms);
            return;
        }
        // A noisy start can still set it too high; no room is louder than this.
        _floor = Math.Min(_floor, 0.015);
        // Once you're talking, softer sounds (the end of a word, a quiet syllable) still count as talking.
        double threshold = _speaking ? Math.Max(_floor * 1.8, 0.008) : Math.Max(_floor * 2.8, 0.012);
        bool loud = rms > threshold;
        if (loud)
        {
            _speechFrames++;
            _quietSeconds = 0;
            if (!_speaking && _speechFrames >= 4)
            {
                _speaking = true;
                SpeechStarted?.Invoke();
            }
        }
        else
        {
            _quietSeconds += FrameSeconds;
            // The room's noise drifts; let the floor follow it while nobody's talking.
            if (!_speaking) _floor = _floor * 0.95 + rms * 0.05;
        }
        if (_speaking) _heardSeconds += FrameSeconds;

        if (!_speaking && _elapsed > _waitForSpeech) StopSoon();
        else if (_speaking && _quietSeconds > _pauseEnds) StopSoon();
        else if (_elapsed > MaxLength) StopSoon();
    }

    // Stopping from the audio thread's own callback can deadlock; hand it off.
    void StopSoon() => ThreadPool.QueueUserWorkItem(_ => Stop());

    void Finish(HeardKind kind)
    {
        float[] samples;
        lock (_gate)
        {
            if (_finished) return;
            _finished = true;
            samples = Resample(_mono.ToArray(), _rate);
        }
        _done.TrySetResult(new Heard(kind, samples));
    }

    /// <summary>Sample i of a captured buffer, its channels averaged, as -1 to 1.</summary>
    internal static float MonoSample(WaveFormat format, byte[] buffer, int i)
    {
        int bytesPerSample = format.BitsPerSample / 8, channels = format.Channels;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible x && x.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        float sum = 0;
        for (int c = 0; c < channels; c++)
        {
            int at = (i * channels + c) * bytesPerSample;
            sum += isFloat ? BitConverter.ToSingle(buffer, at)
                : bytesPerSample == 2 ? BitConverter.ToInt16(buffer, at) / 32768f
                : BitConverter.ToInt32(buffer, at) / 2147483648f;
        }
        return sum / channels;
    }

    /// <summary>Mono at any rate to 16 kHz (WDL's resampler, which filters properly).</summary>
    internal static float[] Resample(float[] mono, int rate)
    {
        if (rate == 16000 || mono.Length == 0) return mono;
        var source = new RawSamples(mono, rate);
        var resampler = new WdlResamplingSampleProvider(source, 16000);
        var output = new List<float>((int)((long)mono.Length * 16000 / rate) + 1024);
        var buffer = new float[16000];
        int read;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) output.Add(buffer[i]);
        return output.ToArray();
    }

    sealed class RawSamples(float[] samples, int rate) : ISampleProvider
    {
        int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }

    public void Dispose() => _capture.Dispose();
}
