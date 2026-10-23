using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Shiki.Core.Ai;

namespace Shiki.Shell;

/// <summary>
/// The main screen, for the agent: screenshots made small enough for Claude (1366 px at most),
/// and the mouse and keyboard through Windows' own input, with every coordinate scaled back.
/// </summary>
public sealed class RealScreen : IAgentScreen
{
    readonly Rectangle _bounds;
    readonly double _scale;

    public RealScreen()
    {
        _bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        _scale = Math.Min(1.0, Math.Min(1366.0 / _bounds.Width, 1366.0 / _bounds.Height));
        Size = ((int)Math.Round(_bounds.Width * _scale), (int)Math.Round(_bounds.Height * _scale));
    }

    public (int Width, int Height) Size { get; }

    (int X, int Y) ToScreen(int x, int y) =>
        (_bounds.Left + (int)Math.Round(Math.Clamp(x, 0, Size.Width - 1) / _scale), _bounds.Top + (int)Math.Round(Math.Clamp(y, 0, Size.Height - 1) / _scale));

    public byte[] Screenshot() => Capture(_bounds, Size.Width, Size.Height, cursor: true);

    public byte[] Zoom(int x0, int y0, int x1, int y1)
    {
        var (ax, ay) = ToScreen(Math.Min(x0, x1), Math.Min(y0, y1));
        var (bx, by) = ToScreen(Math.Max(x0, x1), Math.Max(y0, y1));
        var area = Rectangle.FromLTRB(ax, ay, Math.Max(bx, ax + 8), Math.Max(by, ay + 8));
        // Scaled to fit the usual screenshot size, keeping its shape.
        double fit = Math.Min(Size.Width / (double)area.Width, Size.Height / (double)area.Height);
        return Capture(area, Math.Max(1, (int)(area.Width * fit)), Math.Max(1, (int)(area.Height * fit)), cursor: false);
    }

    static byte[] Capture(Rectangle area, int width, int height, bool cursor)
    {
        using var full = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
            if (cursor)
            {
                // A small pointer, so Claude can see where the mouse is.
                GetCursorPos(out var p);
                int cx = p.X - area.Left, cy = p.Y - area.Top;
                using var pen = new Pen(Color.Black, 2);
                g.FillPolygon(Brushes.White, new[] { new Point(cx, cy), new Point(cx, cy + 18), new Point(cx + 5, cy + 13), new Point(cx + 12, cy + 13) });
                g.DrawPolygon(pen, new[] { new Point(cx, cy), new Point(cx, cy + 18), new Point(cx + 5, cy + 13), new Point(cx + 12, cy + 13) });
            }
        }
        using var small = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, 0, 0, width, height);
        }
        using var png = new MemoryStream();
        small.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    public void Move(int x, int y)
    {
        var (sx, sy) = ToScreen(x, y);
        SetCursorPos(sx, sy);
    }

    public (int X, int Y) Cursor()
    {
        GetCursorPos(out var p);
        return ((int)Math.Round((p.X - _bounds.Left) * _scale), (int)Math.Round((p.Y - _bounds.Top) * _scale));
    }

    public void Click(string button, int count, (int X, int Y)? at, string? modifiers)
    {
        if (at is { } point) Move(point.X, point.Y);
        Thread.Sleep(40);
        var (down, up) = button switch
        {
            "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };
        WithModifiers(modifiers, () =>
        {
            for (int i = 0; i < count; i++)
            {
                Mouse(down);
                Mouse(up);
                if (i + 1 < count) Thread.Sleep(60);
            }
        });
    }

    public void Drag((int X, int Y) from, (int X, int Y) to, string? modifiers)
    {
        Move(from.X, from.Y);
        WithModifiers(modifiers, () =>
        {
            Mouse(MOUSEEVENTF_LEFTDOWN);
            // A few steps on the way, so apps see a drag rather than a jump.
            for (int i = 1; i <= 10; i++)
            {
                Thread.Sleep(15);
                Move(from.X + (to.X - from.X) * i / 10, from.Y + (to.Y - from.Y) * i / 10);
            }
            Thread.Sleep(60);
            Mouse(MOUSEEVENTF_LEFTUP);
        });
    }

    public void Button(bool down) => Mouse(down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);

    public void Scroll(string direction, int amount, (int X, int Y)? at, string? modifiers)
    {
        if (at is { } point) Move(point.X, point.Y);
        amount = Math.Clamp(amount, 1, 30);
        bool horizontal = direction is "left" or "right";
        int delta = (direction is "up" or "right" ? 120 : -120) * amount;
        WithModifiers(modifiers, () => Mouse(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, delta));
    }

    public void Type(string text)
    {
        TypeText.Type(text);
    }

    public void Key(string combo, int repeat)
    {
        var keys = Keys(combo);
        for (int r = 0; r < Math.Clamp(repeat, 1, 100); r++)
        {
            foreach (var k in keys) KeyEvent(k, false);
            foreach (var k in Enumerable.Reverse(keys)) KeyEvent(k, true);
            Thread.Sleep(20);
        }
    }

    public void HoldKey(string combo, double seconds)
    {
        var keys = Keys(combo);
        foreach (var k in keys) KeyEvent(k, false);
        Thread.Sleep(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 10)));
        foreach (var k in Enumerable.Reverse(keys)) KeyEvent(k, true);
    }

    void WithModifiers(string? modifiers, Action action)
    {
        var keys = string.IsNullOrWhiteSpace(modifiers) ? [] : Keys(modifiers);
        foreach (var k in keys) KeyEvent(k, false);
        try { action(); }
        finally { foreach (var k in Enumerable.Reverse(keys)) KeyEvent(k, true); }
    }

    static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11, ["alt"] = 0x12, ["shift"] = 0x10, ["super"] = 0x5B, ["win"] = 0x5B, ["windows"] = 0x5B, ["cmd"] = 0x5B, ["meta"] = 0x5B,
        ["return"] = 0x0D, ["enter"] = 0x0D, ["kp_enter"] = 0x0D, ["tab"] = 0x09, ["escape"] = 0x1B, ["esc"] = 0x1B, ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E,
        ["insert"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23, ["page_up"] = 0x21, ["pageup"] = 0x21, ["prior"] = 0x21, ["page_down"] = 0x22, ["pagedown"] = 0x22, ["next"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27, ["space"] = 0x20, ["menu"] = 0x5D, ["caps_lock"] = 0x14, ["print"] = 0x2C,
        ["minus"] = 0xBD, ["plus"] = 0xBB, ["equal"] = 0xBB, ["comma"] = 0xBC, ["period"] = 0xBE, ["slash"] = 0xBF, ["semicolon"] = 0xBA,
    };

    /// <summary>"ctrl+shift+t", "Return", "alt+Tab", "F5" as virtual keys, modifiers first.</summary>
    internal static List<ushort> Keys(string combo)
    {
        var keys = new List<ushort>();
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part.Replace(" ", "_");
            if (Named.TryGetValue(name, out var vk)) keys.Add(vk);
            else if (name.Length >= 2 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24) keys.Add((ushort)(0x6F + f));
            else if (name.Length == 1)
            {
                short scan = VkKeyScan(name[0]);
                if (scan == -1) throw new ArgumentException($"Can't press \"{part}\".");
                // Shift for capitals and symbols, when it isn't already part of the combo.
                if ((scan & 0x100) != 0 && !keys.Contains(0x10) && char.IsLetter(name[0]) == false) keys.Insert(0, 0x10);
                keys.Add((ushort)(scan & 0xFF));
            }
            else throw new ArgumentException($"Can't press \"{part}\".");
        }
        if (keys.Count == 0) throw new ArgumentException("No key given.");
        return keys;
    }

    // ---- Windows input ----

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010,
        MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_EXTENDEDKEY = 0x0001;

    static void Mouse(uint flags, int data = 0)
    {
        var input = new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    static void KeyEvent(ushort vk, bool up)
    {
        // Arrows, Home/End, Insert/Delete and Page keys are "extended": without the flag they act as the number pad.
        bool extended = vk is >= 0x21 and <= 0x2E || vk is 0x5B or 0x5D;
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0) } },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
        Thread.Sleep(8);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
}
