using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ConvertedBro.Core;

/// <summary>Runs the bundled FFmpeg for audio and video work.</summary>
public static partial class MediaTools
{
    public static readonly string[] ToMp4 =
        ["-c:v", "libx264", "-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p",
         "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"];

    public static readonly string[] ToWebm =
        ["-c:v", "libvpx-vp9", "-crf", "33", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4",
         "-c:a", "libopus", "-b:a", "128k"];

    public static readonly string[] ToGif =
        ["-an", "-vf", "fps=12,scale='min(480,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4",
         "-loop", "0"];

    public static readonly string[] CompressVideo =
        ["-c:v", "libx264", "-preset", "medium", "-crf", "28", "-pix_fmt", "yuv420p",
         "-vf", "scale='min(1920,iw)':-2", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"];

    public static readonly string[] Mute = ["-c:v", "copy", "-an"];

    public static readonly string[] ToMp3 = ["-vn", "-c:a", "libmp3lame", "-q:a", "2"];
    public static readonly string[] ToWav = ["-vn", "-c:a", "pcm_s16le"];
    public static readonly string[] ToM4a = ["-vn", "-c:a", "aac", "-b:a", "192k"];
    public static readonly string[] ToFlac = ["-vn", "-c:a", "flac"];
    public static readonly string[] ToOgg = ["-vn", "-c:a", "libvorbis", "-q:a", "5"];

    public static string? FindFfmpeg()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir.Trim('"'), "ffmpeg.exe"))
            .FirstOrDefault(File.Exists);
    }

    public static async Task RunAsync(string input, string output, IReadOnlyList<string> args,
        IProgress<double> progress, CancellationToken ct)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException(
            "FFmpeg not found. Put ffmpeg.exe in the tools\\ffmpeg folder next to ConvertedBro.exe, or on PATH.");

        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-y", "-i", input }) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var a in new[] { "-progress", "pipe:1", "-nostats", output }) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        double durationSeconds = 0;
        var errorTail = new Queue<string>();

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                var m = DurationRegex().Match(line);
                if (m.Success && durationSeconds == 0)
                    durationSeconds = TimeSpan.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).TotalSeconds;
                errorTail.Enqueue(line);
                if (errorTail.Count > 12) errorTail.Dequeue();
            }
        });

        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                // -progress reports out_time_us (microseconds) for each update.
                if (line.StartsWith("out_time_us=") && durationSeconds > 0 &&
                    long.TryParse(line.AsSpan("out_time_us=".Length), out var us) && us > 0)
                    progress.Report(Math.Min(1, us / 1_000_000.0 / durationSeconds));
            }
        });

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            TryDelete(output);
            throw;
        }

        await Task.WhenAll(stderrTask, stdoutTask);
        if (process.ExitCode != 0)
        {
            TryDelete(output);
            var reason = errorTail.LastOrDefault(l => l.Length > 0) ?? $"exit code {process.ExitCode}";
            Log.Error($"FFmpeg failed for {input}:{Environment.NewLine}{string.Join(Environment.NewLine, errorTail)}");
            throw new InvalidOperationException($"FFmpeg: {reason}");
        }
        progress.Report(1);
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [GeneratedRegex(@"Duration:\s*(\d+:\d+:\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();
}
