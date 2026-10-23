using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shiki.Core.Voice;
using Shiki.Shell;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// The little pill above the taskbar while dictating: a mic that swells with your voice and the
/// words as they come. It never takes the keyboard, so the words land where you were typing.
/// </summary>
public sealed class DictationPill : Window
{
    readonly Dictation _dictation;
    readonly DashKit _k = new(DashPalette.Night);
    readonly TextBlock _text;
    readonly Waveform _wave;

    public DictationPill(Dictation dictation)
    {
        _dictation = dictation;
        var p = _k.P;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;

        _wave = new Waveform(DashKit.B(p.AccentText), 14, 2.5, 2.5) { Mode = Waveform.Modes.Listening, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mic = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = DashKit.B(p.Accent),
            Child = _wave,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _text = _k.Text("", 14, p.Text);
        _text.Margin = new Thickness(12, 0, 4, 1);
        _text.MaxWidth = 520;
        _text.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 16, 8) };
        row.Children.Add(mic);
        row.Children.Add(_text);
        Content = new Border
        {
            Margin = new Thickness(12),
            Background = DashKit.B(Color.FromArgb(0xF4, p.Card.R, p.Card.G, p.Card.B)),
            BorderBrush = DashKit.B(p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.45 },
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
        };
        SizeChanged += (_, _) => Place();
        _dictation.Changed += Refresh;
        Closed += (_, _) => _dictation.Changed -= Refresh;
        Refresh();
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 6;
    }

    void Refresh()
    {
        var partial = _dictation.Partial;
        // The newest words, if it's getting long.
        if (partial.Length > 90) partial = "…" + partial[^89..];
        _text.Text = partial.Length > 0 ? partial : _dictation.Status;
        _text.Foreground = DashKit.B(partial.Length > 0 ? _k.P.Text : _k.P.Muted);
        _wave.Level = _dictation.Level;
        _wave.Mode = _dictation.Status.StartsWith("Writing") ? Waveform.Modes.Thinking : Waveform.Modes.Listening;
    }

    /// <summary>Shows a last word ("Typed", "Didn't catch that") and goes.</summary>
    public void Done(string message)
    {
        _dictation.Changed -= Refresh;
        _text.Text = message;
        _text.Foreground = DashKit.B(_k.P.Muted);
        var close = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
        close.Tick += (_, _) => { close.Stop(); Close(); };
        close.Start();
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
