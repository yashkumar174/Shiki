using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ConvertedBro.Core;

/// <summary>
/// Records part of the screen to MP4 with FFmpeg: Windows' GPU desktop capture (ddagrab) when the
/// area is on the main screen, the older GDI capture otherwise or if that fails; the graphics
/// card's encoder when there is one. System sound and the microphone are captured separately
/// (WASAPI) and mixed in when recording stops.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    const int Fps = 30;

    readonly (int X, int Y, int W, int H) _area;
    readonly bool _mic, _sound;
    readonly string _work;
    readonly string _video;
    readonly Stopwatch _clock = new();
    Process? _ffmpeg;
    AudioTrack? _systemTrack, _micTrack;

    public TimeSpan Elapsed => _clock.Elapsed;
    public bool Recording => _ffmpeg is { HasExited: false };

    /// <param name="area">Physical screen pixels (virtual-desktop coordinates).</param>
    public ScreenRecorder((int X, int Y, int W, int H) area, bool mic, bool sound)
    {
        // H.264 in yuv420p needs even sizes.
        _area = (area.X, area.Y, Math.Max(2, area.W & ~1), Math.Max(2, area.H & ~1));
        _mic = mic;
        _sound = sound;
        _work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ConvertedBro", "rec-" + DateTime.Now.ToString("HHmmss"))).FullName;
        _video = Path.Combine(_work, "video.mkv");
    }

    /// <summary>Starts capturing; returns once the first frames are on their way.</summary>
    public async Task StartAsync()
    {
        var ffmpeg = MediaTools.FindFfmpeg() ?? throw new InvalidOperationException("FFmpeg is missing, so recording isn't available.");
        var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        bool onPrimary = _area.X >= primary.Left && _area.Y >= primary.Top && _area.X + _area.W <= primary.Right && _area.Y + _area.H <= primary.Bottom;

        // Try the fast path first, then fall back step by step.
        var attempts = new List<string[]>();
        foreach (var encoder in new[] { Nvenc, X264 })
        {
            if (onPrimary) attempts.Add([.. DdaInput(primary), .. encoder]);
            attempts.Add([.. GdiInput(), .. encoder]);
        }
        foreach (var args in attempts)
        {
            if (await TryStart(ffmpeg, args)) return;
        }
        throw new InvalidOperationException("Recording couldn't start. The log has FFmpeg's reasons.");
    }

    string[] DdaInput(System.Drawing.Rectangle primary) =>
    [
        "-f", "lavfi", "-i",
        $"ddagrab=output_idx=0:framerate={Fps}:draw_mouse=1:offset_x={_area.X - primary.Left}:offset_y={_area.Y - primary.Top}:video_size={_area.W}x{_area.H}",
        "-vf", "hwdownload,format=bgra,format=yuv420p",
    ];

    string[] GdiInput() =>
    [
        "-f", "gdigrab", "-framerate", Fps.ToString(), "-draw_mouse", "1",
        "-offset_x", _area.X.ToString(), "-offset_y", _area.Y.ToString(), "-video_size", $"{_area.W}x{_area.H}", "-i", "desktop",
        "-vf", "format=yuv420p",
    ];

    static readonly string[] Nvenc = ["-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", "23", "-b:v", "0"];
    static readonly string[] X264 = ["-c:v", "libx264", "-preset", "veryfast", "-crf", "23"];

    async Task<bool> TryStart(string ffmpeg, string[] args)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-hide_banner", "-y" }.Concat(args).Append(_video)) info.ArgumentList.Add(a);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (errors) if (errors.Count < 60) errors.Add(e.Data);
            // FFmpeg prints this once the input is open and encoding has begun.
            if (e.Data.Contains("Press [q] to stop") || e.Data.StartsWith("frame=")) started.TrySetResult(true);
        };
        process.Exited += (_, _) => started.TrySetResult(false);
        process.Start();
        process.BeginErrorReadLine();

        var ok = await Task.WhenAny(started.Task, Task.Delay(8000)) == started.Task && started.Task.Result;
        if (ok)
        {
            // Sound starts with the picture, so the two line up.
            _clock.Restart();
            if (_sound) _systemTrack = AudioTrack.System(Path.Combine(_work, "system.wav"));
            if (_mic) _micTrack = AudioTrack.Microphone(Path.Combine(_work, "mic.wav"));
            // A quick crash right after starting (no GPU encoder, say) shows up within a moment.
            ok = !await Task.Run(() => process.WaitForExit(700));
        }
        if (!ok)
        {
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            lock (errors) Log.Info($"Recorder attempt failed ({string.Join(" ", args.Take(6))}…): {string.Join(" | ", errors.TakeLast(4))}");
            process.Dispose();
            _systemTrack?.Dispose();
            _micTrack?.Dispose();
            _systemTrack = _micTrack = null;
            return false;
        }
        _ffmpeg = process;
        Log.Info($"Recording {_area.W}x{_area.H} at {_area.X},{_area.Y}: {string.Join(" ", args.Take(2))} {(args.Contains("h264_nvenc") ? "nvenc" : "x264")}");
        return true;
    }

    /// <summary>Stops, mixes in the sound, and saves the MP4. Returns its path.</summary>
    public async Task<string> StopAsync(string output, IProgress<double>? progress = null)
    {
        _clock.Stop();
        var duration = _clock.Elapsed;
        _systemTrack?.Stop(duration);
        _micTrack?.Stop(duration);
        if (_ffmpeg is { } ffmpeg)
        {
            try
            {
                // "q" lets FFmpeg finish the file properly.
                await ffmpeg.StandardInput.WriteAsync('q');
                await ffmpeg.StandardInput.FlushAsync();
            }
            catch (IOException) { }
            if (!await Task.Run(() => ffmpeg.WaitForExit(15000))) ffmpeg.Kill();
        }
        if (!File.Exists(_video)) throw new InvalidOperationException("The recording didn't save.");

        var tracks = new[] { _systemTrack, _micTrack }.Where(t => t is { HasSound: true }).Select(t => t!.Path).ToList();
        // RunAsync opens the video itself; the sound files follow as more inputs.
        var args = new List<string>();
        foreach (var track in tracks) args.AddRange(["-i", track]);
        if (tracks.Count == 2)
            args.AddRange(["-filter_complex", "[1:a][2:a]amix=inputs=2:duration=longest:normalize=0[a]", "-map", "0:v", "-map", "[a]"]);
        else if (tracks.Count == 1)
            args.AddRange(["-map", "0:v", "-map", "1:a"]);
        args.AddRange(["-c:v", "copy"]);
        if (tracks.Count > 0) args.AddRange(["-c:a", "aac", "-b:a", "160k", "-shortest"]);
        args.AddRange(["-movflags", "+faststart"]);
        await MediaTools.RunAsync(_video, output, args, progress ?? new Progress<double>(), CancellationToken.None);
        return output;
    }

    public void Dispose()
    {
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(); } catch (InvalidOperationException) { }
        _ffmpeg?.Dispose();
        _systemTrack?.Dispose();
        _micTrack?.Dispose();
        try { Directory.Delete(_work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// One sound source written to a WAV. Windows sends nothing while it's silent, so the gaps
    /// are filled with silence to keep the sound in step with the picture.
    /// </summary>
    sealed class AudioTrack : IDisposable
    {
        readonly IWaveIn _capture;
        readonly WaveFileWriter _writer;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly object _gate = new();
        long _written;
        bool _stopped;
        public string Path { get; }
        public bool HasSound { get; private set; }

        AudioTrack(IWaveIn capture, string path)
        {
            _capture = capture;
            Path = path;
            _writer = new WaveFileWriter(path, capture.WaveFormat);
            capture.DataAvailable += (_, e) =>
            {
                lock (_gate)
                {
                    if (_stopped) return;
                    PadTo(_clock.Elapsed - TimeSpan.FromSeconds((double)e.BytesRecorded / _writer.WaveFormat.AverageBytesPerSecond));
                    _writer.Write(e.Buffer, 0, e.BytesRecorded);
                    _written += e.BytesRecorded;
                    HasSound = true;
                }
            };
            capture.StartRecording();
        }

        public static AudioTrack? System(string path)
        {
            try { return new AudioTrack(new WasapiLoopbackCapture(), path); }
            catch (Exception ex) { Log.Error("System sound capture failed", ex); return null; }
        }

        public static AudioTrack? Microphone(string path)
        {
            try { return new AudioTrack(new WasapiCapture(new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)), path); }
            catch (Exception ex) { Log.Error("Microphone capture failed", ex); return null; }
        }

        /// <summary>Writes silence up to this point in time (block-aligned).</summary>
        void PadTo(TimeSpan at)
        {
            var format = _writer.WaveFormat;
            long target = (long)(at.TotalSeconds * format.AverageBytesPerSecond);
            target -= target % format.BlockAlign;
            if (target <= _written) return;
            var silence = new byte[Math.Min(target - _written, 1 << 20)];
            while (_written < target)
            {
                int n = (int)Math.Min(silence.Length, target - _written);
                _writer.Write(silence, 0, n);
                _written += n;
            }
        }

        public void Stop(TimeSpan duration)
        {
            try { _capture.StopRecording(); } catch (Exception ex) { Log.Error("Stopping audio capture", ex); }
            lock (_gate)
            {
                if (_stopped) return;
                if (HasSound) PadTo(duration);
                _stopped = true;
                _writer.Flush();
                _writer.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (!_stopped) { _stopped = true; _writer.Dispose(); }
            }
            _capture.Dispose();
        }
    }
}
