using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shiki.Core.Ai;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// While the agent works: what it's doing, a Stop button, and its questions with Yes and No. The
/// agent can't see this pill (it's left out of screenshots), so it can't answer for the person.
/// Touching the real mouse or keyboard stops the agent: the person can take over at any moment.
/// </summary>
public sealed class AgentPill : Window
{
    readonly DashKit _k = new(DashPalette.Night);
    readonly ComputerAgent _agent;
    readonly TextBlock _status, _task;
    readonly StackPanel _question = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBlock _questionText;
    TaskCompletionSource<bool>? _answer;
    IntPtr _mouseHook, _keyHook;
    readonly HookProc _mouseProc, _keyProc;
    POINT? _firstMove;

    /// <summary>The person used the real mouse or keyboard (or pressed Stop).</summary>
    public event Action<string>? TookOver;

    public AgentPill(ComputerAgent agent, string task)
    {
        _agent = agent;
        var p = _k.P;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 380;

        _task = _k.Text(task, 12, p.Muted, wrap: true);
        _task.MaxHeight = 34;
        _task.TextTrimming = TextTrimming.CharacterEllipsis;
        _status = _k.Text(agent.Status, 14, p.Text, FontWeights.SemiBold, wrap: true);
        var stop = _k.Button("Stop", () => TookOver?.Invoke("You pressed Stop."), DashKit.Pill.Quiet, "\uE71A", 12.5);
        stop.VerticalAlignment = VerticalAlignment.Top;
        var top = new DockPanel();
        DockPanel.SetDock(stop, Dock.Right);
        top.Children.Add(stop);
        var words = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        words.Children.Add(_status);
        words.Children.Add(_task);
        top.Children.Add(words);

        _questionText = _k.Text("", 14, p.Text, wrap: true);
        _question.Children.Add(_questionText);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var yes = _k.Button("Yes, go ahead", () => Answer(true), DashKit.Pill.Accent, size: 13);
        yes.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(yes);
        buttons.Children.Add(_k.Button("No", () => Answer(false), DashKit.Pill.Quiet, size: 13));
        _question.Children.Add(buttons);

        var hint = _k.Text("Touch the mouse or keyboard to take over.", 11.5, p.Faint);
        hint.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_question);
        stack.Children.Add(hint);
        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(16, 12, 12, 12),
            Background = DashKit.B(p.Card),
            BorderBrush = DashKit.B(p.AccentEdge),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.5 },
        };

        _mouseProc = MouseHook;
        _keyProc = KeyHook;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
            // Left out of every screenshot: the agent never sees (or clicks) this pill.
            SetWindowDisplayAffinity(hwnd, 0x11);
            using var module = Process.GetCurrentProcess().MainModule!;
            var handle = GetModuleHandle(module.ModuleName);
            _mouseHook = SetWindowsHookEx(14, _mouseProc, handle, 0);
            _keyHook = SetWindowsHookEx(13, _keyProc, handle, 0);
        };
        SizeChanged += (_, _) => Place();
        Closed += (_, _) =>
        {
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
            _answer?.TrySetResult(false);
        };
        agent.Changed += () => _status.Text = agent.Status;
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Top + 8;
    }

    /// <summary>Shows the agent's question; the answer comes from Yes or No.</summary>
    public Task<bool> Ask(AgentQuestion question)
    {
        _answer?.TrySetResult(false);
        _answer = new TaskCompletionSource<bool>();
        _questionText.Text = question.Question;
        _question.Visibility = Visibility.Visible;
        System.Media.SystemSounds.Asterisk.Play();
        return _answer.Task;
    }

    void Answer(bool yes)
    {
        _question.Visibility = Visibility.Collapsed;
        _firstMove = null;
        _answer?.TrySetResult(yes);
        _answer = null;
    }

    bool Asking => _answer != null;

    bool OverPill(POINT p)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        return GetWindowRect(hwnd, out var r) && p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;
    }

    // Real input (not the agent's own, which Windows marks as injected) means the person is taking over.
    IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !Asking)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
            bool injected = (info.flags & 0x01) != 0;
            if (!injected && !OverPill(info.pt))
            {
                int msg = message.ToInt32();
                if (msg is 0x0201 or 0x0204 or 0x0207 or 0x020A) Dispatcher.BeginInvoke(() => TookOver?.Invoke("You used the mouse, so I stopped."));
                else if (msg == 0x0200)
                {
                    // A real move of more than a little: a nudge of the desk doesn't count.
                    _firstMove ??= info.pt;
                    if (Math.Abs(info.pt.X - _firstMove.Value.X) + Math.Abs(info.pt.Y - _firstMove.Value.Y) > 60)
                        Dispatcher.BeginInvoke(() => TookOver?.Invoke("You moved the mouse, so I stopped."));
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    IntPtr KeyHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !Asking && message.ToInt32() is 0x0100 or 0x0104)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
            if ((info.flags & 0x10) == 0) Dispatcher.BeginInvoke(() => TookOver?.Invoke("You used the keyboard, so I stopped."));
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
