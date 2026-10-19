using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ConvertedBro.Shell;

/// <summary>A shortcut like Ctrl+Shift+Q, as stored in settings and shown in the UI.</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public override string ToString() =>
        string.Join("+", new[]
        {
            Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
            Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
            Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : null,
            Modifiers.HasFlag(ModifierKeys.Windows) ? "Win" : null,
            KeyName(Key),
        }.Where(p => p != null));

    static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.Space => "Space",
        _ => key.ToString(),
    };

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default:
                    if (part.Length == 1 && char.IsDigit(part[0])) key = Key.D0 + (part[0] - '0');
                    else if (Enum.TryParse<Key>(part, true, out var k)) key = k;
                    break;
            }
        }
        return key is { } found && mods != ModifierKeys.None ? new Hotkey(mods, found) : null;
    }
}

/// <summary>System-wide shortcuts (RegisterHotKey), delivered on the UI thread.</summary>
public sealed class Hotkeys : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    readonly HwndSource _window;
    readonly Dictionary<int, Action> _handlers = [];
    int _nextId = 1;

    public Hotkeys()
    {
        // A message-only window just to receive WM_HOTKEY.
        _window = new HwndSource(new HwndSourceParameters("ConvertedBro hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    /// <summary>Registers a shortcut. Returns false when another app already owns it.</summary>
    public bool Register(Hotkey hotkey, Action handler, out int id)
    {
        id = _nextId++;
        uint mods = MOD_NOREPEAT
            | (hotkey.Modifiers.HasFlag(ModifierKeys.Alt) ? MOD_ALT : 0)
            | (hotkey.Modifiers.HasFlag(ModifierKeys.Control) ? MOD_CONTROL : 0)
            | (hotkey.Modifiers.HasFlag(ModifierKeys.Shift) ? MOD_SHIFT : 0)
            | (hotkey.Modifiers.HasFlag(ModifierKeys.Windows) ? MOD_WIN : 0);
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        if (!RegisterHotKey(_window.Handle, id, mods, vk)) return false;
        _handlers[id] = handler;
        return true;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id)) UnregisterHotKey(_window.Handle, id);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList()) Unregister(id);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
