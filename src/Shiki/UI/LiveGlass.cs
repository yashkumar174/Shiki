using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConvertedBro.Core;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>
/// Keeps a window's liquid glass in step with whatever is behind it. The window is excluded from
/// screen capture while it's open (it stays visible on screen), so we can keep grabbing the area
/// behind it and re-rendering the glass, only when that area actually changed.
/// </summary>
public sealed class LiveGlass : IDisposable
{
    /// <summary>Off: glass is a snapshot taken when a popup opens, and popups show up in screen recordings.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Dev aid: when set, the latest frame is written here as PNG every second.</summary>
    static readonly string? DumpPath = Environment.GetEnvironmentVariable("CONVERTEDBRO_GLASS_DUMP");

    readonly Window _window;
    readonly Action<ImageSource> _show;
    readonly int _frameMs;
    readonly object _gate = new();
    GlassPlan? _plan;
    WriteableBitmap? _bitmap;
    Thread? _thread;
    volatile bool _running;
    volatile int _x, _y; // window's top-left on screen, physical pixels
    IntPtr _hwnd;
    long _frames, _renderTicks, _captureTicks, _applyTicks;

    public LiveGlass(Window window, Action<ImageSource> show, int fps = 30)
    {
        _window = window;
        _show = show;
        _frameMs = 1000 / fps;
        window.LocationChanged += (_, _) => TrackPosition();
    }

    /// <summary>The shapes changed (e.g. the wheel switched page). Safe to call any time.</summary>
    public void SetPlan(GlassPlan plan)
    {
        lock (_gate) _plan = plan;
        if (_bitmap == null || _bitmap.PixelWidth != plan.Width || _bitmap.PixelHeight != plan.Height)
        {
            _bitmap = new WriteableBitmap(plan.Width, plan.Height, 96, 96, PixelFormats.Pbgra32, null);
            _show(_bitmap);
        }
        _lastChecksum = 0; // force a fresh frame
    }

    /// <summary>Call once the window has a handle (SourceInitialized), before it becomes visible.</summary>
    public void Start()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (!NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE))
        {
            Log.Error("Live glass: could not exclude the window from capture");
            return;
        }
        TrackPosition();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Live glass", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    void TrackPosition()
    {
        double scale = NativeMethods.SystemScale;
        _x = (int)Math.Round(_window.Left * scale);
        _y = (int)Math.Round(_window.Top * scale);
    }

    long _lastChecksum;

    void Loop()
    {
        System.Drawing.Bitmap? capture = null;
        byte[]? snapshot = null, output = null;
        var lastDump = Stopwatch.StartNew();
        try
        {
            while (_running)
            {
                var started = Stopwatch.GetTimestamp();
                GlassPlan? plan;
                lock (_gate) plan = _plan;
                if (plan != null)
                {
                    int w = plan.Width, h = plan.Height;
                    if (capture == null || capture.Width != w || capture.Height != h)
                    {
                        capture?.Dispose();
                        capture = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        snapshot = new byte[w * h * 4];
                        output = new byte[w * h * 4];
                    }
                    using (var g = System.Drawing.Graphics.FromImage(capture))
                        g.CopyFromScreen(_x, _y, 0, 0, capture.Size);
                    long captured = Stopwatch.GetTimestamp();
                    _captureTicks += captured - started;
                    var data = capture.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, capture.PixelFormat);
                    Marshal.Copy(data.Scan0, snapshot!, 0, snapshot!.Length);
                    capture.UnlockBits(data);

                    // Nothing moved behind us: skip the work.
                    long checksum = Checksum(snapshot);
                    if (checksum != _lastChecksum)
                    {
                        _lastChecksum = checksum;
                        long applyStart = Stopwatch.GetTimestamp();
                        plan.Apply(snapshot, output!);
                        _applyTicks += Stopwatch.GetTimestamp() - applyStart;
                        var frame = output!;
                        _window.Dispatcher.Invoke(() =>
                        {
                            if (_bitmap != null && _bitmap.PixelWidth == w && _bitmap.PixelHeight == h)
                                _bitmap.WritePixels(new Int32Rect(0, 0, w, h), frame, w * 4, 0);
                        });
                        _frames++;
                        _renderTicks += Stopwatch.GetTimestamp() - started;
                        if (DumpPath != null && lastDump.ElapsedMilliseconds > 1000) { Dump(frame, w, h); lastDump.Restart(); }
                    }
                }
                int spent = (int)((Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency);
                Thread.Sleep(Math.Max(4, _frameMs - spent));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Live glass stopped", ex);
        }
        finally
        {
            capture?.Dispose();
        }
    }

    /// <summary>Cheap change detection: a sparse sum over the snapshot.</summary>
    static long Checksum(byte[] px)
    {
        long sum = 17;
        for (int i = 0; i < px.Length; i += 61) sum = sum * 31 + px[i];
        return sum;
    }

    void Dump(byte[] frame, int w, int h)
    {
        try
        {
            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, frame, w * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var file = System.IO.Path.Combine(DumpPath!, $"glass-{_window.GetType().Name}-{DateTime.Now:HHmmss}.png");
            using var stream = System.IO.File.Create(file);
            encoder.Save(stream);
            double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency / Math.Max(1, _frames);
            Log.Info($"Live glass {_window.GetType().Name} {w}x{h}: {_frames} frames, {Ms(_renderTicks):0.0} ms each (capture {Ms(_captureTicks):0.0}, glass {Ms(_applyTicks):0.0})");
        }
        catch (Exception ex)
        {
            Log.Error("Glass dump failed", ex);
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_hwnd != IntPtr.Zero) NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_NONE);
    }
}
