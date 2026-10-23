using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// Live captions above the taskbar: the last line or two of what the PC is playing, and the
/// translation when there is one. It never takes the keyboard; drag it anywhere, × closes it.
/// </summary>
public sealed class CaptionsWindow : Window
{
    readonly DashKit _k = new(DashPalette.Night);
    readonly StackPanel _lines = new();
    readonly TextBlock _partial;
    readonly List<(string Original, string? Translated)> _shown = [];
    readonly bool _translating;
    bool _moved;

    public event Action? CloseRequested;

    public CaptionsWindow(string engine, string? translateTo)
    {
        _translating = translateTo != null;
        var p = _k.P;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = Math.Min(920, SystemParameters.WorkArea.Width * 0.7);
        UseLayoutRounding = true;

        _partial = _k.Text("", 19, p.Muted, wrap: true);
        _partial.Margin = new Thickness(0, 2, 0, 0);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var close = _k.Icon("\uE711", 11, p.Muted);
        var closeButton = new Border { Child = close, Padding = new Thickness(6), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Turn captions off" };
        closeButton.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseRequested?.Invoke(); };
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);
        header.Children.Add(_k.Text(translateTo != null ? $"Live captions, in {translateTo}" : "Live captions", 12, p.Faint));
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_lines);
        stack.Children.Add(_partial);
        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(20, 10, 14, 14),
            Background = DashKit.B(Color.FromArgb(0xE8, 0x10, 0x10, 0x12)),
            BorderBrush = DashKit.B(p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.45 },
        };
        _partial.Text = engine == "Whisper" ? "Listening to what's playing (a few seconds behind)…" : "Listening to what's playing…";

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Never takes the keyboard, and stays off Alt+Tab.
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
        };
        SizeChanged += (_, _) => { if (!_moved) Place(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            _moved = true;
            DragMove();
        };
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 70;
    }

    public void SetPartial(string text)
    {
        _partial.Text = text;
    }

    public void AddLine(string original, string? translated)
    {
        _shown.Add((original, translated));
        // The last two lines (one when translating: each comes with its original).
        while (_shown.Count > (_translating ? 1 : 2)) _shown.RemoveAt(0);
        _lines.Children.Clear();
        foreach (var (o, t) in _shown)
        {
            if (t != null)
            {
                var main = _k.Text(t, 22, _k.P.Text, FontWeights.SemiBold, wrap: true);
                _lines.Children.Add(main);
                var small = _k.Text(o, 13.5, _k.P.Muted, wrap: true);
                small.Margin = new Thickness(0, 3, 0, 4);
                _lines.Children.Add(small);
            }
            else
            {
                var line = _k.Text(o, 22, _k.P.Text, FontWeights.SemiBold, wrap: true);
                line.Margin = new Thickness(0, 0, 0, 4);
                _lines.Children.Add(line);
            }
        }
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
