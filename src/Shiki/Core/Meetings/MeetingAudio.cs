using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Shiki.Core.Meetings;

/// <summary>
/// A call's sound, as two channels: the left is your microphone ("You"), the right is what the
/// PC plays (everyone else). Both are brought to 16 kHz and kept in step by the clock, with
/// silence filled in when a side has nothing (Windows sends nothing at all while the speakers
/// are quiet). Each tenth of a second goes out as 16-bit stereo, ready to stream or save.
/// </summary>
public sealed class MeetingAudio : IDisposable
{
    public const int Rate = 16000;

    readonly Source? _mic;
    readonly Source _speakers;
    readonly CancellationTokenSource _stop = new();
    Task? _pump;
    WaveFileWriter? _keep;

    /// <summary>16-bit stereo at 16 kHz, interleaved (left you, right them). Raised on a background thread.</summary>
    public event Action<byte[]>? Chunk;

    /// <summary>Loudness of each side (0 to 1), about ten times a second, for the pill's meters.</summary>
    public event Action<double, double>? Levels;

    /// <summary>Seconds of sound so far.</summary>
    public double Seconds { get; private set; }

    /// <param name="keepAs">Also write everything to this WAV (when the person keeps recordings).</param>
    /// <param name="mic">False for only what the PC plays (captions): the left side stays silent and the mic is never opened.</param>
    public MeetingAudio(string? keepAs = null, bool mic = true)
    {
        if (mic) _mic = new Source(new WasapiCapture(new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)));
        _speakers = new Source(new WasapiLoopbackCapture());
        if (keepAs != null) _keep = new WaveFileWriter(keepAs, new WaveFormat(Rate, 16, 2));
    }

    public void Start()
    {
        _mic?.Capture.StartRecording();
        _speakers.Capture.StartRecording();
        _pump = Task.Run(Pump);
    }

    async Task Pump()
    {
        var clock = Stopwatch.StartNew();
        long sent = 0;
        var left = new float[Rate];
        var right = new float[Rate];
        while (!_stop.IsCancellationRequested)
        {
            try { await Task.Delay(100, _stop.Token); } catch (OperationCanceledException) { break; }
            long due = (long)(clock.Elapsed.TotalSeconds * Rate) - sent;
            if (due <= 0) continue;
            int n = (int)Math.Min(due, Rate);
            int a = _mic?.Read(left, n) ?? 0, b = _speakers.Read(right, n);
            var bytes = new byte[n * 4];
            double sumL = 0, sumR = 0;
            for (int i = 0; i < n; i++)
            {
                float l = i < a ? left[i] : 0, r = i < b ? right[i] : 0;
                sumL += l * l;
                sumR += r * r;
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), Pcm(l));
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4 + 2), Pcm(r));
            }
            sent += n;
            Seconds = sent / (double)Rate;
            _keep?.Write(bytes, 0, bytes.Length);
            Chunk?.Invoke(bytes);
            Levels?.Invoke(Math.Clamp(Math.Sqrt(sumL / n) * 10, 0, 1), Math.Clamp(Math.Sqrt(sumR / n) * 10, 0, 1));
        }
    }

    static short Pcm(float sample) => (short)Math.Clamp(sample * 32767f, short.MinValue, short.MaxValue);

    public async Task StopAsync()
    {
        _stop.Cancel();
        try { _mic?.Capture.StopRecording(); } catch (Exception ex) { Log.Error("Stopping the meeting microphone", ex); }
        try { _speakers.Capture.StopRecording(); } catch (Exception ex) { Log.Error("Stopping the meeting sound", ex); }
        if (_pump != null) await _pump;
        _keep?.Dispose();
        _keep = null;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _mic?.Dispose();
        _speakers.Dispose();
        _keep?.Dispose();
    }

    /// <summary>One capture device, turned into mono 16 kHz samples on demand.</summary>
    sealed class Source : IDisposable
    {
        readonly BufferedWaveProvider _buffer;
        readonly ISampleProvider _out;

        public WasapiCapture Capture { get; }

        public Source(WasapiCapture capture)
        {
            Capture = capture;
            // ReadFully: when the device sent nothing (quiet speakers), reading gives silence.
            _buffer = new BufferedWaveProvider(capture.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = true, BufferDuration = TimeSpan.FromSeconds(4) };
            capture.DataAvailable += (_, e) => _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            ISampleProvider samples = _buffer.ToSampleProvider();
            samples = samples.WaveFormat.Channels switch
            {
                1 => samples,
                2 => new StereoToMonoSampleProvider(samples) { LeftVolume = 0.5f, RightVolume = 0.5f },
                _ => new DownMix(samples),
            };
            _out = samples.WaveFormat.SampleRate == Rate ? samples : new WdlResamplingSampleProvider(samples, Rate);
        }

        public int Read(float[] into, int count) => _out.Read(into, 0, count);

        public void Dispose() => Capture.Dispose();
    }

    /// <summary>Any number of channels averaged into one.</summary>
    sealed class DownMix(ISampleProvider source) : ISampleProvider
    {
        readonly int _channels = source.WaveFormat.Channels;
        float[] _wide = [];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            if (_wide.Length < count * _channels) _wide = new float[count * _channels];
            int read = source.Read(_wide, 0, count * _channels) / _channels;
            for (int i = 0; i < read; i++)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++) sum += _wide[i * _channels + c];
                buffer[offset + i] = sum / _channels;
            }
            return read;
        }
    }
}
