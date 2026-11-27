using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Shiki.Shell;

/// <summary>
/// Types text into whatever has the keyboard, as if typed: each character goes in as a Unicode
/// key press, so it works in any app and any language without touching the clipboard. It waits
/// for Ctrl, Alt, Shift and Win to be let go first, so nothing turns into a shortcut.
/// </summary>
public static class TypeText
{
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_UNICODE = 0x0004, KEYEVENTF_KEYUP = 0x0002;

    /// <summary>True once the modifier keys are up (or after two seconds, whichever comes first).</summary>
    public static async Task<bool> WaitForModifiersUp()
    {
        for (int i = 0; i < 40; i++)
        {
            if (!Down(VK_CONTROL) && !Down(VK_MENU) && !Down(VK_SHIFT) && !Down(VK_LWIN) && !Down(VK_RWIN)) return true;
            await Task.Delay(50);
        }
        return false;
    }

    public static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>Types the text; returns false if Windows refused (an app running as administrator, say).</summary>
    public static bool Type(string text)
    {
        // Line breaks as Enter; everything else as Unicode characters, a few dozen per batch.
        text = text.Replace("\r\n", "\n");
        const int Batch = 32;
        for (int start = 0; start < text.Length; start += Batch)
        {
            var piece = text.Substring(start, Math.Min(Batch, text.Length - start));
            var inputs = new INPUT[piece.Length * 2];
            for (int i = 0; i < piece.Length; i++)
            {
                char c = piece[i];
                bool enter = c == '\n';
                inputs[i * 2] = Key(enter ? (ushort)0x0D : (ushort)0, enter ? (ushort)0 : c, enter ? 0 : KEYEVENTF_UNICODE);
                inputs[i * 2 + 1] = Key(enter ? (ushort)0x0D : (ushort)0, enter ? (ushort)0 : c, (enter ? 0 : KEYEVENTF_UNICODE) | KEYEVENTF_KEYUP);
            }
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length) return false;
            Thread.Sleep(5);
        }
        return true;
    }

    /// <summary>Ctrl+V: pastes what's on the clipboard (files, into a chat).</summary>
    public static void Paste()
    {
        var inputs = new[] { Key(VK_CONTROL, 0, 0), Key(0x56, 0, 0), Key(0x56, 0, KEYEVENTF_KEYUP), Key(VK_CONTROL, 0, KEYEVENTF_KEYUP) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Ctrl+C, Ctrl+A or Ctrl+Z: copy the selection, select all in the box, undo.</summary>
    public static void Copy() => Ctrl(0x43);
    public static void SelectAll() => Ctrl(0x41);
    public static void Undo() => Ctrl(0x5A);

    static void Ctrl(ushort vk)
    {
        var inputs = new[] { Key(VK_CONTROL, 0, 0), Key(vk, 0, 0), Key(vk, 0, KEYEVENTF_KEYUP), Key(VK_CONTROL, 0, KEYEVENTF_KEYUP) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>One press of Enter (sending a message that's ready in its box).</summary>
    public static void PressEnter()
    {
        var inputs = new[] { Key(0x0D, 0, 0), Key(0x0D, 0, KEYEVENTF_KEYUP) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // The union must be as big as its biggest member (MOUSEINPUT) for SendInput to accept the size.
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

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}
