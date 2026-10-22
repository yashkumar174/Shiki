using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static ConvertedBro.Shell.NativeMethods;

namespace ConvertedBro.Shell;

/// <summary>
/// Watches the mouse system-wide for "Shift held while dragging" so the buddy can pop up next
/// to the cursor, like Tangerine on macOS. Only looks at button state and position; nothing is stored.
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    const int DragThreshold = 24; // physical pixels

    readonly LowLevelMouseProc _proc; // kept alive so the GC doesn't collect the callback
    readonly uint _ownPid = (uint)Environment.ProcessId;
    IntPtr _hook;
    bool _buttonDown, _fired, _pressStartedOnUs;
    POINT _downAt;

    /// <summary>Shift + drag passed the threshold. Point is in physical screen pixels.</summary>
    public event Action<int, int>? ShiftDragStarted;
    /// <summary>The button of a drag that raised <see cref="ShiftDragStarted"/> was released.</summary>
    public event Action? DragEnded;

    public GlobalMouseHook()
    {
        _proc = HookProc;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            Core.Log.Error($"Mouse hook failed: {Marshal.GetLastWin32Error()}");
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN:
                    _buttonDown = true;
                    _fired = false;
                    _downAt = info.pt;
                    _pressStartedOnUs = IsOurWindow(info.pt);
                    break;

                case WM_MOUSEMOVE when _buttonDown && !_fired && !_pressStartedOnUs:
                    int dx = info.pt.X - _downAt.X, dy = info.pt.Y - _downAt.Y;
                    if (dx * dx + dy * dy > DragThreshold * DragThreshold && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
                    {
                        _fired = true;
                        ShiftDragStarted?.Invoke(info.pt.X, info.pt.Y);
                    }
                    break;

                case WM_LBUTTONUP:
                    _buttonDown = false;
                    if (_fired) DragEnded?.Invoke();
                    _fired = false;
                    break;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    bool IsOurWindow(POINT pt)
    {
        GetWindowThreadProcessId(WindowFromPoint(pt), out var pid);
        return pid == _ownPid;
    }

    public void Dispose() => Stop();
}
