using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// "Capture whole page": scrolls a page from top to bottom, takes each screen, works out how far
/// the page really moved between them (so nothing is repeated, and toolbars or sticky headers
/// that stay put appear once), and cuts the result into screen-sized parts at quiet rows, so
/// each part reads like a normal screenshot instead of one very long one.
/// </summary>
public static class PageCapture
{
    static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "thorium", "yandex", "zen"];
    static readonly string[] Documents = ["winword", "acrord32", "acrobat", "sumatrapdf", "notion", "obsidian", "code", "onenote"];
    static readonly string[] SnipTools = ["snippingtool", "screenclippinghost", "screensketch"];

    const int MaxParts = 30, MaxFrames = 60, Columns = 240;

    // ---- Which window ----

    /// <summary>The app window a screenshot was most likely taken of: the one in front, or the top one that isn't Shiki or the snipping tool.</summary>
    public static IntPtr PageWindow(IntPtr front)
    {
        var root = front == IntPtr.Zero ? IntPtr.Zero : GetAncestor(front, 2);
        if (Usable(root)) return root;
        for (var hwnd = GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, 2))
            if (Usable(hwnd)) return hwnd;
        return IntPtr.Zero;
    }

    static bool Usable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return false;
        if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var cls = ClassOf(hwnd);
        if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "NotifyIconOverflowWindow" or "Windows.UI.Core.CoreWindow") return false;
        if ((GetWindowLong(hwnd, -20) & 0x80) != 0) return false;
        if (!GetWindowRect(hwnd, out var r) || r.Right - r.Left < 300 || r.Bottom - r.Top < 200) return false;
        var name = ProcessOf(hwnd);
        return name == null || !SnipTools.Contains(name);
    }

    /// <summary>A browser or a document app: somewhere "the whole page" means something.</summary>
    public static bool IsPage(IntPtr hwnd)
    {
        var name = ProcessOf(hwnd);
        return name != null && (Browsers.Contains(name) || Documents.Contains(name));
    }

    /// <summary>The app's name for showing ("Chrome", "Edge").</summary>
    public static string AppName(IntPtr hwnd) => ProcessOf(hwnd) switch
    {
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "firefox" => "Firefox",
        "brave" => "Brave",
        "winword" => "Word",
        "code" => "VS Code",
        { } other => char.ToUpperInvariant(other[0]) + other[1..],
        null => "this window",
    };

    static string? ProcessOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName.ToLowerInvariant(); }
        catch { return null; }
    }

    // ---- The capture ----

    /// <summary>
    /// Scrolls the window's page to the top, then down screen by screen, and returns the page as
    /// parts. The mouse is moved over the page to scroll it, then put back; Esc stops early.
    /// </summary>
    public static async Task<List<BitmapSource>> CaptureAsync(IntPtr hwnd, Action<string> progress, CancellationToken stop)
    {
        var view = Viewport(hwnd);
        if (view.Right - view.Left < 100 || view.Bottom - view.Top < 100) throw new InvalidOperationException("That window has no page to scroll.");
        NativeMethods.ForceForeground(hwnd);
        NativeMethods.GetCursorPos(out var mouseWas);
        var frames = new List<Frame>();
        try
        {
            NativeMethods.SetCursorPos((view.Left + view.Right) / 2, (view.Top + view.Bottom) / 2);
            await Task.Delay(120, stop);

            // To the top first.
            progress("Going to the top of the page…");
            var before = await Settled(hwnd, view, null, stop);
            for (int i = 0; i < 30; i++)
            {
                Wheel(+40);
                var after = await Settled(hwnd, view, before, stop);
                if (Same(before.Gray, after.Gray, before.Width)) { before = after; break; }
                before = after;
                if (Escape()) break;
            }
            frames.Add(before);

            int notches = 3;
            double pixelsPerNotch = 0;
            int stripHeight = before.Height;
            for (int i = 1; i < MaxFrames && !Escape() && !stop.IsCancellationRequested; i++)
            {
                progress($"Capturing… {Math.Max(1, (int)Math.Ceiling(stripHeight / (double)before.Height))} screen(s) so far. Esc stops.");
                Wheel(-notches);
                var next = await Settled(hwnd, view, frames[^1], stop);
                var move = Measure(frames[^1], next);
                if (move.Shift <= 0) break; // the bottom
                next.Move = move;
                frames.Add(next);
                stripHeight += move.Shift;
                if (stripHeight > MaxParts * before.Height) break;
                pixelsPerNotch = move.Shift / (double)notches;
                double usable = next.Height - move.Header - move.Footer;
                notches = Math.Clamp((int)Math.Round(usable * 0.82 / Math.Max(1, pixelsPerNotch)), 1, 40);
            }
        }
        finally
        {
            NativeMethods.SetCursorPos(mouseWas.X, mouseWas.Y);
        }
        progress("Cutting it into parts…");
        return Parts(frames);
    }

    static bool Escape() => (GetAsyncKeyState(0x1B) & 0x8000) != 0;

    /// <summary>A screen of the page, and a grey copy of it (narrow) for comparing.</summary>
    sealed class Frame(BitmapSource picture, byte[] gray, int width, int height)
    {
        public BitmapSource Picture { get; } = picture;
        public byte[] Gray { get; } = gray;
        public int Width { get; } = width;
        public int Height { get; } = height;
        /// <summary>How it moved from the frame before it (none for the first).</summary>
        public Movement Move { get; set; }
    }

    public readonly record struct Movement(int Shift, int Header, int Footer);

    /// <summary>Waits for the page to stop moving (smooth scrolling, pictures loading) and takes it.</summary>
    static async Task<Frame> Settled(IntPtr hwnd, RECT view, Frame? previous, CancellationToken stop)
    {
        var last = Grab(hwnd, view);
        int calm = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 1800 && calm < 2)
        {
            await Task.Delay(90, stop);
            var now = Grab(hwnd, view);
            calm = Same(last.Gray, now.Gray, last.Width) ? calm + 1 : 0;
            last = now;
        }
        return last;
    }

    static Frame Grab(IntPtr hwnd, RECT view)
    {
        GetWindowRect(hwnd, out var win);
        BitmapSource? whole = Drawn(hwnd, win.Right - win.Left, win.Bottom - win.Top);
        BitmapSource picture;
        var crop = new Int32Rect(view.Left - win.Left, view.Top - win.Top, view.Right - view.Left, view.Bottom - view.Top);
        if (whole != null && crop.X >= 0 && crop.Y >= 0 && crop.X + crop.Width <= whole.PixelWidth && crop.Y + crop.Height <= whole.PixelHeight && !Blank(whole))
            picture = new CroppedBitmap(whole, crop);
        else
            picture = UI.Glass.Capture(new Int32Rect(view.Left, view.Top, view.Right - view.Left, view.Bottom - view.Top)) ?? throw new InvalidOperationException("Couldn't take a picture of the page.");
        picture.Freeze();
        return ToFrame(picture);
    }

    static Frame ToFrame(BitmapSource picture)
    {
        var narrow = new TransformedBitmap(picture, new ScaleTransform(Columns / (double)picture.PixelWidth, 1));
        var gray = new FormatConvertedBitmap(narrow, PixelFormats.Gray8, null, 0);
        int w = gray.PixelWidth, h = gray.PixelHeight;
        var pixels = new byte[w * h];
        gray.CopyPixels(pixels, w, 0);
        return new Frame(picture, pixels, w, h);
    }

    static bool Same(byte[] a, byte[] b, int width)
    {
        if (a.Length != b.Length) return false;
        long total = 0;
        for (int i = 0; i < a.Length; i += 3) total += Math.Abs(a[i] - b[i]);
        return total / (double)(a.Length / 3) < 0.6;
    }

    // ---- How far the page moved ----

    /// <summary>The columns compared: the page's middle, not the scroll bar on the right edge.</summary>
    static (int From, int To) Band(int width) => ((int)(width * 0.01), (int)(width * 0.95));

    static double RowDiff(Frame a, int ya, Frame b, int yb)
    {
        var (from, to) = Band(a.Width);
        int oa = ya * a.Width, ob = yb * b.Width, total = 0;
        for (int x = from; x < to; x += 2) total += Math.Abs(a.Gray[oa + x] - b.Gray[ob + x]);
        return total / (double)((to - from + 1) / 2);
    }

    /// <summary>
    /// How many rows the page moved from a to b, and how tall the parts that stayed put are at the
    /// top (toolbars, a sticky header) and the bottom (a cookie bar). 0 means it didn't move.
    /// </summary>
    public static Movement Measure(byte[] grayA, byte[] grayB, int width, int height) =>
        Measure(new Frame(null!, grayA, width, height), new Frame(null!, grayB, width, height));

    static Movement Measure(Frame a, Frame b)
    {
        int h = Math.Min(a.Height, b.Height);
        int header = 0;
        while (header < h && RowDiff(a, header, b, header) < 1.5) header++;
        if (header >= h - 8) return new Movement(0, header, 0);
        int footer = 0;
        while (footer < h - header && RowDiff(a, h - 1 - footer, b, h - 1 - footer) < 1.5) footer++;
        // A still band only counts as a footer if it's short (a long one is just a blank page end).
        if (footer > h / 4) footer = 0;
        int body = h - header - footer;
        if (body < 40) return new Movement(0, header, footer);

        int best = 0;
        double bestErr = double.MaxValue;
        for (int dy = 1; dy <= body - 24; dy++)
        {
            int rows = body - dy;
            int step = Math.Max(1, rows / 160);
            double err = 0;
            int n = 0;
            for (int y = header; y < header + rows; y += step, n++)
            {
                err += RowDiff(a, y + dy, b, y);
                if (n > 8 && err / n > bestErr * 1.6) break;
            }
            err /= Math.Max(1, n);
            if (err < bestErr) { bestErr = err; best = dy; }
        }
        // Nothing lines up (it jumped a whole screen or more): take the whole new screen.
        if (bestErr > 14) best = body;
        return new Movement(best, header, footer);
    }

    // ---- Into parts ----

    /// <summary>Where each row of the long page comes from: a frame and a row in it.</summary>
    readonly record struct Piece(Frame Frame, int From, int Rows);

    static List<BitmapSource> Parts(List<Frame> frames)
    {
        // The long page as pieces of frames: the whole first screen (less a footer), then the new rows of each.
        var pieces = new List<Piece>();
        var first = frames[0];
        int footer = frames.Count > 1 ? frames[1].Move.Footer : 0;
        pieces.Add(new Piece(first, 0, first.Height - footer));
        for (int i = 1; i < frames.Count; i++)
        {
            var f = frames[i];
            var m = f.Move;
            pieces.Add(new Piece(f, f.Height - m.Footer - m.Shift, m.Shift));
        }
        if (footer > 0) pieces.Add(new Piece(frames[^1], frames[^1].Height - footer, footer));

        int total = pieces.Sum(p => p.Rows);
        int pageHeight = first.Height;
        // Busy-ness of every row of the long page, to cut between lines rather than through them.
        var busy = new double[total];
        int at = 0;
        foreach (var piece in pieces)
            for (int r = 0; r < piece.Rows; r++) busy[at++] = Busyness(piece.Frame, piece.From + r);

        var cuts = new List<int> { 0 };
        while (total - cuts[^1] > pageHeight)
        {
            int target = cuts[^1] + pageHeight;
            int bestRow = target;
            double bestBusy = double.MaxValue;
            for (int y = target; y >= target - pageHeight * 0.18; y--)
            {
                if (busy[y] < bestBusy - 0.01) { bestBusy = busy[y]; bestRow = y; }
                if (bestBusy < 0.5) break;
            }
            cuts.Add(bestRow);
            if (cuts.Count > MaxParts) break;
        }
        cuts.Add(total);

        var parts = new List<BitmapSource>();
        for (int i = 0; i + 1 < cuts.Count; i++)
            if (cuts[i + 1] - cuts[i] > 8) parts.Add(Render(pieces, cuts[i], cuts[i + 1]));
        return parts;
    }

    /// <summary>How much is going on along a row (0 for a plain background).</summary>
    static double Busyness(Frame f, int y)
    {
        var (from, to) = Band(f.Width);
        int o = y * f.Width, total = 0;
        for (int x = from + 1; x < to; x++) total += Math.Abs(f.Gray[o + x] - f.Gray[o + x - 1]);
        return total / (double)(to - from);
    }

    static BitmapSource Render(List<Piece> pieces, int from, int to)
    {
        int width = pieces[0].Frame.Picture.PixelWidth;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            int at = 0;
            foreach (var piece in pieces)
            {
                int start = Math.Max(from, at), end = Math.Min(to, at + piece.Rows);
                if (end > start)
                {
                    var rows = new CroppedBitmap(piece.Frame.Picture, new Int32Rect(0, piece.From + (start - at), piece.Frame.Picture.PixelWidth, end - start));
                    dc.DrawImage(rows, new Rect(0, start - from, rows.PixelWidth, end - start));
                }
                at += piece.Rows;
            }
        }
        var bitmap = new RenderTargetBitmap(width, to - from, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Dev aid: screens of a page given as pictures (as if scrolled), into parts.</summary>
    public static List<BitmapSource> PartsOf(IReadOnlyList<BitmapSource> screens, Action<string>? note = null)
    {
        var frames = new List<Frame> { ToFrame(screens[0]) };
        for (int i = 1; i < screens.Count; i++)
        {
            var next = ToFrame(screens[i]);
            var move = Measure(frames[^1], next);
            note?.Invoke($"screen {i}: moved {move.Shift}, header {move.Header}, footer {move.Footer}");
            if (move.Shift <= 0) break;
            next.Move = move;
            frames.Add(next);
        }
        return Parts(frames);
    }

    // ---- Windows ----

    /// <summary>The page area: a Chromium browser's page view, else the window's client area (screen pixels).</summary>
    static RECT Viewport(IntPtr hwnd)
    {
        RECT best = default;
        long bestArea = 0;
        EnumChildWindows(hwnd, (child, _) =>
        {
            if (ClassOf(child) == "Chrome_RenderWidgetHostHWND" && IsWindowVisible(child) && GetWindowRect(child, out var r))
            {
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = r; }
            }
            return true;
        }, IntPtr.Zero);
        if (bestArea > 0) return best;
        GetClientRect(hwnd, out var client);
        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        ClientToScreen(hwnd, ref origin);
        return new RECT { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
    }

    static void Wheel(int notches)
    {
        var input = new INPUT { type = 0, mi = new MOUSEINPUT { mouseData = notches * 120, dwFlags = 0x0800 } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    static BitmapSource? Drawn(IntPtr hwnd, int w, int h)
    {
        if (w <= 0 || h <= 0) return null;
        var screenDc = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, w, h);
        var old = SelectObject(dc, bitmap);
        try
        {
            if (!PrintWindow(hwnd, dc, 2)) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

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

    static string ClassOf(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy, mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref NativeMethods.POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
