using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>A picture just landed on the clipboard (a screenshot, or a copied image).</summary>
/// <param name="Png">The picture as a PNG.</param>
/// <param name="FromScreenshotTool">It came from Windows' Snipping Tool, Print Screen or a screenshot app.</param>
/// <param name="Window">The window that was in front when it was taken (not the snipping tool's own).</param>
public sealed record CopiedPicture(byte[] Png, bool FromScreenshotTool, IntPtr Window);

/// <summary>
/// Watches the clipboard (Windows tells us each time it changes) and reports pictures put there
/// by other apps. Changes Shiki makes itself are ignored.
/// </summary>
public sealed class ClipboardWatch : IDisposable
{
    readonly HwndSource _window;
    readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(220) };
    IntPtr _front;
    string? _owner;
    uint _sequence;

    public event Action<CopiedPicture>? PictureCopied;

    static readonly string[] ScreenshotApps =
    [
        "snippingtool", "screenclippinghost", "screensketch", "sharex", "greenshot", "lightshot", "screenshot", "snagit32", "snagiteditor", "flameshot", "explorer",
    ];

    public ClipboardWatch()
    {
        // A message-only window: it receives WM_CLIPBOARDUPDATE and is never shown.
        _window = new HwndSource(new HwndSourceParameters("ShikiClipboardWatch") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _window.AddHook(Hook);
        if (!NativeMethods.AddClipboardFormatListener(_window.Handle))
            Log.Info("Clipboard watch couldn't start");
        _settle.Tick += (_, _) => { _settle.Stop(); Read(); };
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE) return IntPtr.Zero;
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == ShotStack.OwnSequence) return IntPtr.Zero;
        var owner = NativeMethods.GetClipboardOwner();
        uint pid = 0;
        if (owner != IntPtr.Zero) NativeMethods.GetWindowThreadProcessId(owner, out pid);
        if (pid == Environment.ProcessId) return IntPtr.Zero;
        _owner = pid == 0 ? null : ProcessName(pid);
        _sequence = sequence;
        // The app in front now: by the time the snip lands, the snipping overlay has (nearly) gone.
        _front = NativeMethods.GetForegroundWindow();
        // Apps often write a few formats one after another: read once they've finished.
        _settle.Stop();
        _settle.Start();
        return IntPtr.Zero;
    }

    void Read()
    {
        try
        {
            if (NativeMethods.GetClipboardSequenceNumber() == ShotStack.OwnSequence) return;
            var data = System.Windows.Forms.Clipboard.GetDataObject();
            if (data == null) return;
            // Copied files (Explorer) aren't a new picture.
            if (data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop)) return;
            byte[]? png = null;
            if (data.GetData("PNG") is MemoryStream stream) png = stream.ToArray();
            if (png == null && System.Windows.Forms.Clipboard.ContainsImage() && System.Windows.Forms.Clipboard.GetImage() is { } image)
            {
                using (image)
                using (var output = new MemoryStream())
                {
                    image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                    png = output.ToArray();
                }
            }
            if (png == null || png.Length < 200) return;
            bool tool = _owner == null || Array.Exists(ScreenshotApps, a => _owner.Contains(a, StringComparison.OrdinalIgnoreCase));
            var window = PageCapture.PageWindow(_front);
            Log.Info($"Picture on the clipboard from {_owner ?? "Print Screen"} ({png.Length:N0} bytes), seq {_sequence}");
            PictureCopied?.Invoke(new CopiedPicture(png, tool, window));
        }
        catch (Exception ex)
        {
            Log.Error("Reading a picture from the clipboard", ex);
        }
    }

    static string? ProcessName(uint pid)
    {
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
        catch { return null; }
    }

    public void Dispose()
    {
        NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
    }
}
