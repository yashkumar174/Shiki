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
    readonly IAgentStatus _agent;
    readonly TextBlock _status, _task;
    readonly StackPanel _question = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBlock _questionText;
    readonly StackPanel _pausedPanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    TaskCompletionSource<bool>? _answer;
    IntPtr _mouseHook, _keyHook;
    readonly HookProc _mouseProc, _keyProc;
    bool _paused;

    /// <summary>Stop was pressed.</summary>
    public event Action? StopPressed;
    /// <summary>The person clicked or typed themselves: the agent waits (Continue picks up again).</summary>
    public event Action? Paused;
    public event Action? Resumed;

    public AgentPill(IAgentStatus agent, string task)
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
        var stop = _k.Button("Stop", () => StopPressed?.Invoke(), DashKit.Pill.Quiet, "\uE71A", 12.5);
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

        _pausedPanel.Children.Add(_k.Text("Paused while you use the PC. Continue when you're done.", 14, p.Text, wrap: true));
        var pausedButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var resume = _k.Button("Continue", Resume, DashKit.Pill.Accent, size: 13);
        resume.Margin = new Thickness(0, 0, 8, 0);
        pausedButtons.Children.Add(resume);
        pausedButtons.Children.Add(_k.Button("Stop", () => StopPressed?.Invoke(), DashKit.Pill.Quiet, size: 13));
        _pausedPanel.Children.Add(pausedButtons);

        var hint = _k.Text("Click or type yourself and it pauses. Moving the mouse is fine.", 11.5, p.Faint);
        hint.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_question);
        stack.Children.Add(_pausedPanel);
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
        _answer?.TrySetResult(yes);
        _answer = null;
    }

    bool Asking => _answer != null;

    void Pause()
    {
        if (_paused || Asking) return;
        _paused = true;
        _pausedPanel.Visibility = Visibility.Visible;
        Paused?.Invoke();
    }

    void Resume()
    {
        if (!_paused) return;
        _paused = false;
        _pausedPanel.Visibility = Visibility.Collapsed;
        Resumed?.Invoke();
    }

    bool OverPill(POINT p)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        return GetWindowRect(hwnd, out var r) && p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;
    }

    // A real click, scroll or key (not the agent's own, which Windows marks as injected) means the
    // person is doing something themselves: the agent waits. Just moving the mouse doesn't count.
    IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !Asking && !_paused && message.ToInt32() is 0x0201 or 0x0204 or 0x0207 or 0x020A)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
            if ((info.flags & 0x01) == 0 && !OverPill(info.pt)) Dispatcher.BeginInvoke(Pause);
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    IntPtr KeyHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !Asking && !_paused && message.ToInt32() is 0x0100 or 0x0104)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
            if ((info.flags & 0x10) == 0) Dispatcher.BeginInvoke(Pause);
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
