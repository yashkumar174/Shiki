using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Shiki.Core.Meetings;
using Shiki.Shell;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// The small pill at the top of the screen while the cat takes meeting notes: a red dot, the
/// time, who's talking, Mark and Stop, and (opened) the last few lines as they're written down.
/// It never takes focus from the call and is left out of screen shares and recordings.
/// </summary>
public sealed class NotesPill : Window
{
    const double PillWidth = 360;

    readonly MeetingSession _session;
    readonly DashKit _k = new(DashPalette.Night);
    readonly TextBlock _time, _status;
    readonly Border _meMeter, _themMeter;
    readonly StackPanel _lines = new() { Margin = new Thickness(14, 2, 14, 12) };
    readonly Border _panel;
    readonly TextBlock _openGlyph;
    readonly System.Windows.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _open;

    public event Action? MarkRequested, StopRequested;

    public NotesPill(MeetingSession session)
    {
        _session = session;
        var p = _k.P;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = PillWidth + 24;
        UseLayoutRounding = true;

        // ---- The pill ----
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = DashKit.B(Color.FromRgb(0xE5, 0x48, 0x4D)), VerticalAlignment = VerticalAlignment.Center };
        if (SystemParameters.ClientAreaAnimation)
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        var title = _k.Text("Notes", 13.5, weight: FontWeights.SemiBold);
        title.Margin = new Thickness(9, 0, 8, 1);
        title.VerticalAlignment = VerticalAlignment.Center;
        _time = _k.Text("00:00", 13, p.Muted);
        _time.VerticalAlignment = VerticalAlignment.Center;
        _meMeter = Meter(p.Accent);
        _themMeter = Meter(Color.FromRgb(0x8F, 0xB8, 0xFF));
        var meters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Left: you. Right: everyone else." };
        meters.Children.Add(_meMeter);
        meters.Children.Add(_themMeter);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(dot);
        left.Children.Add(title);
        left.Children.Add(_time);
        left.Children.Add(meters);

        var mark = _k.IconButton("\uE734", () => MarkRequested?.Invoke(), "Mark this moment (or say \"Hey Sky, mark that\")", 13, p.Text, 30);
        var stop = _k.Button("Stop", () => StopRequested?.Invoke(), DashKit.Pill.Quiet, "\uE71A", 12.5);
        stop.Padding = new Thickness(10, 4, 12, 5);
        stop.Margin = new Thickness(4, 0, 2, 0);
        stop.ToolTip = "Stop and write the notes";
        _openGlyph = _k.Icon("\uE70D", 11, p.Muted);
        var open = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = _openGlyph, ToolTip = "Show the live notes" };
        open.MouseLeftButtonUp += (_, e) => { e.Handled = true; _open = !_open; Refresh(); };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(mark);
        right.Children.Add(stop);
        right.Children.Add(open);

        var row = new DockPanel { Margin = new Thickness(14, 8, 8, 8) };
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(left);

        _status = _k.Text("Let everyone know you're taking notes.", 12, p.Faint, wrap: true);
        _status.Margin = new Thickness(14, 0, 14, 8);

        _panel = new Border { BorderBrush = DashKit.B(p.Line), BorderThickness = new Thickness(0, 1, 0, 0), Child = _lines, Visibility = Visibility.Collapsed };

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_status);
        stack.Children.Add(_panel);
        var card = new Border
        {
            Margin = new Thickness(12),
            Background = DashKit.B(Color.FromArgb(0xF4, p.Card.R, p.Card.G, p.Card.B)),
            BorderBrush = DashKit.B(p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.45 },
        };
        // Drag it out of the way by its empty parts.
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = card;

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 4;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Not in screen shares or recordings, and clicking it doesn't pull focus from the call.
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
        };
        _session.Changed += Refresh;
        _clock.Tick += (_, _) => Refresh();
        _clock.Start();
        var hint = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        hint.Tick += (_, _) => { hint.Stop(); if (!_session.Stopping) _status.Visibility = Visibility.Collapsed; };
        hint.Start();
        Closed += (_, _) => { _session.Changed -= Refresh; _clock.Stop(); };
        Refresh();
    }

    Border Meter(Color colour) => new()
    {
        Width = 4,
        Height = 14,
        Margin = new Thickness(2, 0, 0, 0),
        CornerRadius = new CornerRadius(2),
        Background = DashKit.B(colour),
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(0.5, 1),
        RenderTransform = new ScaleTransform(1, 0.2),
    };

    void Refresh()
    {
        _time.Text = Meeting.Clock(_session.Seconds);
        ((ScaleTransform)_meMeter.RenderTransform).ScaleY = 0.2 + 0.8 * _session.LevelMe;
        ((ScaleTransform)_themMeter.RenderTransform).ScaleY = 0.2 + 0.8 * _session.LevelThem;
        if (_session.Stopping)
        {
            _status.Text = "Writing your notes…";
            _status.Visibility = Visibility.Visible;
        }
        _openGlyph.Text = _open ? "\uE70E" : "\uE70D";
        _panel.Visibility = _open ? Visibility.Visible : Visibility.Collapsed;
        if (!_open) return;

        _lines.Children.Clear();
        var meeting = _session.Meeting;
        foreach (var line in meeting.Lines.TakeLast(7))
            _lines.Children.Add(Line(meeting.Who(line.Speaker), line.Text, line.Marked, live: false));
        if (_session.Partial[1] is { Length: > 0 } them) _lines.Children.Add(Line("…", them, false, live: true));
        if (_session.Partial[0] is { Length: > 0 } me) _lines.Children.Add(Line("You", me, false, live: true));
        if (_lines.Children.Count == 0)
            _lines.Children.Add(_k.Text($"Listening ({_session.Engine}). Words appear here as people talk.", 12.5, _k.P.Faint, wrap: true));
    }

    UIElement Line(string who, string text, bool marked, bool live)
    {
        var block = new TextBlock { FontFamily = DashKit.Sans, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        if (marked) block.Inlines.Add(new System.Windows.Documents.Run("★ ") { Foreground = DashKit.B(_k.P.Accent) });
        block.Inlines.Add(new System.Windows.Documents.Run(who + "  ") { FontWeight = FontWeights.SemiBold, Foreground = DashKit.B(who == "You" ? _k.P.Accent : Color.FromRgb(0x8F, 0xB8, 0xFF)) });
        block.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = DashKit.B(live ? _k.P.Faint : _k.P.Text), FontStyle = live ? FontStyles.Italic : FontStyles.Normal });
        return block;
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
