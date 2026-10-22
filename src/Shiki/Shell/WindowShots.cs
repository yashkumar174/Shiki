using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// Pictures of other apps' windows, for "what's this error?": the top window that isn't Shiki's
/// own, drawn by the window itself (so the cat's bubble on top of it isn't in the picture).
/// </summary>
public static class WindowShots
{
    const int MaxSide = 1568;

    /// <summary>The top visible window of another app, skipping the desktop and the taskbar.</summary>
    public static IntPtr TopOtherWindow()
    {
        uint mine = (uint)Environment.ProcessId;
        for (var hwnd = GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, 2))
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) continue;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == mine) continue;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "NotifyIconOverflowWindow" or "Windows.UI.Core.CoreWindow") continue;
            if (!Bounds(hwnd, out var r) || r.Right - r.Left < 120 || r.Bottom - r.Top < 80) continue;
            // Owned tool windows (tooltips, popups) aren't what people mean by "this window".
            if ((GetWindowLong(hwnd, -20) & 0x80) != 0) continue;
            return hwnd;
        }
        return IntPtr.Zero;
    }

    /// <summary>Dev aid: when set, "the window in front" is this picture instead (for testing without a real screen).</summary>
    public static (byte[] Png, string Title)? TestPicture { get; set; }

    /// <summary>A PNG of the window (sized for Claude) and its title, or null.</summary>
    public static (byte[] Png, string Title)? Capture(IntPtr hwnd)
    {
        if (TestPicture is { } test) return test;
        if (hwnd == IntPtr.Zero || !Bounds(hwnd, out var r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w < 40 || h < 40) return null;
        var title = new StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        BitmapSource? picture = Drawn(hwnd, w, h);
        // Some windows can't draw themselves off screen (or draw black): take the screen's pixels instead.
        if (picture == null || Blank(picture)) picture = UI.Glass.Capture(new Int32Rect(r.Left, r.Top, w, h));
        if (picture == null) return null;
        return (Encode(picture), title.ToString());
    }

    static BitmapSource? Drawn(IntPtr hwnd, int w, int h)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, w, h);
        var old = SelectObject(dc, bitmap);
        try
        {
            // PW_RENDERFULLCONTENT: also browsers and other GPU-drawn windows.
            if (!PrintWindow(hwnd, dc, 2)) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Log.Error("Could not draw a window for its picture", ex);
            return null;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>All one colour (a window that drew nothing).</summary>
    static bool Blank(BitmapSource picture)
    {
        var converted = new FormatConvertedBitmap(picture, PixelFormats.Bgr32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        uint first = BitConverter.ToUInt32(pixels, 0) & 0xFFFFFF;
        for (int i = 0; i < pixels.Length; i += 4 * 97)
            if ((BitConverter.ToUInt32(pixels, i) & 0xFFFFFF) != first) return false;
        return true;
    }

    static byte[] Encode(BitmapSource picture)
    {
        int longest = Math.Max(picture.PixelWidth, picture.PixelHeight);
        if (longest > MaxSide)
        {
            double s = (double)MaxSide / longest;
            picture = new TransformedBitmap(picture, new ScaleTransform(s, s));
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    static bool Bounds(IntPtr hwnd, out RECT r) =>
        DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf<RECT>()) == 0 || GetWindowRect(hwnd, out r);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
