using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Core.Voice;

namespace Shiki.UI.Dashboard;

/// <summary>
/// The box you type to the cat in, with a microphone and a send button. While listening, the box
/// gives way to what the microphone is doing, and the mic button swells with your voice.
/// Shared by the Ask page and the bubble.
/// </summary>
public sealed class ChatComposer
{
    readonly DashKit _k;
    readonly VoiceInput? _voice;
    readonly Action<string> _send;
    readonly TextBlock _hint, _status;
    readonly Border _mic, _sendButton;
    readonly TextBlock _micGlyph;
    readonly ScaleTransform _swell = new(1, 1);
    readonly string _placeholder;

    DashPalette P => _k.P;

    public TextBox Box { get; }
    public FrameworkElement Root { get; }

    public ChatComposer(DashKit k, string catName, VoiceInput? voice, Action<string> send, bool compact)
    {
        _k = k;
        _voice = voice;
        _send = send;
        double size = compact ? 14.5 : 15.5, button = compact ? 30 : 36;

        Box = new TextBox
        {
            FontFamily = DashKit.Sans,
            FontSize = size,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            SelectionBrush = DashKit.B(P.Accent),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = compact ? 96 : 140,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _placeholder = voice != null ? $"Tell {catName} what to do, or press the mic…" : $"Tell {catName} what to do…";
        _hint = k.Text(_placeholder, size, P.Faint);
        _hint.IsHitTestVisible = false;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _status = k.Text("", size, P.Muted, wrap: true);
        _status.VerticalAlignment = VerticalAlignment.Center;
        // What's being heard wraps above the buttons instead of running under them.
        _status.MaxHeight = compact ? 96 : 140;
        _status.Visibility = Visibility.Collapsed;
        Box.TextChanged += (_, _) => ShowHint();
        Box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                if (_voice is { Active: true }) _voice.Finish();
                else Send();
            }
        };
        var field = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { _hint, _status, Box } };

        _sendButton = Round(button, P.Accent, k.Icon("\uE724", compact ? 13 : 15, P.AccentText), "Send (Enter)", Send);
        _micGlyph = k.Icon("\uE720", compact ? 14 : 16, P.Text);
        _mic = Round(button, P.Hover, _micGlyph, "Talk (the cat listens until you pause)", () =>
        {
            if (_voice == null) return;
            if (_voice.Active) _voice.Finish();
            else _ = _voice.Listen();
        });
        _mic.RenderTransformOrigin = new Point(0.5, 0.5);
        _mic.RenderTransform = _swell;
        _mic.Margin = new Thickness(10, 0, 0, 0);
        _sendButton.Margin = new Thickness(8, 0, 0, 0);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        if (voice != null) buttons.Children.Add(_mic);
        buttons.Children.Add(_sendButton);
        var row = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(field);

        Root = compact
            ? new Border
            {
                Background = DashKit.B(P.Panel),
                BorderBrush = DashKit.B(P.Line),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 6, 6, 6),
                Child = row,
            }
            : k.Card(row, new Thickness(18, 8, 8, 8), 22);

        if (voice != null)
        {
            voice.Changed += Refresh;
            Refresh();
        }
    }

    public void Detach()
    {
        if (_voice != null) _voice.Changed -= Refresh;
    }

    void Send()
    {
        var text = Box.Text.Trim();
        if (text.Length == 0) return;
        Box.Text = "";
        _send(text);
    }

    void ShowHint() => _hint.Visibility = Box.Text.Length == 0 && _status.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

    void Refresh()
    {
        var voice = _voice!;
        bool listening = voice.Active;
        _status.Text = voice.State switch
        {
            VoiceState.GettingReady => $"Getting my ears ready (one time, {Speech.Local.Size})… {voice.ReadyProgress:0%}",
            VoiceState.Waiting => voice.Partial.Length > 0 ? voice.Partial
                : voice.Answering ? "Say yes or no…"
                : voice.FollowingUp ? "Still listening… say what's next, or \"thanks\" to stop"
                : "Listening…",
            VoiceState.Hearing => voice.Partial.Length > 0 ? voice.Partial : voice.Answering ? "Listening for yes or no…" : "Listening… pause when you're done",
            VoiceState.Understanding => "Writing down what you said…",
            _ => "",
        };
        _status.Visibility = listening ? Visibility.Visible : Visibility.Collapsed;
        Box.Visibility = listening ? Visibility.Hidden : Visibility.Visible;
        ShowHint();
        _sendButton.Visibility = listening ? Visibility.Collapsed : Visibility.Visible;

        bool hot = voice.State is VoiceState.Waiting or VoiceState.Hearing;
        ((SolidColorBrush)_mic.Background).Color = hot ? P.Accent : P.Hover;
        _micGlyph.Foreground = DashKit.B(hot ? P.AccentText : P.Text);
        _micGlyph.Text = voice.State == VoiceState.Understanding ? "\uE916" : "\uE720";
        _mic.ToolTip = hot ? "Done talking (Enter)" : "Talk (the cat listens until you pause)";
        // The button swells with your voice; reduced-motion users get a steady colour instead.
        double grow = hot && SystemParameters.ClientAreaAnimation ? 1 + Math.Min(voice.Level, 1) * 0.35 : 1;
        _swell.ScaleX = _swell.ScaleY = grow;
    }

    Border Round(double size, Color fill, UIElement child, string tip, Action click)
    {
        var round = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(fill),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = child,
        };
        round.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return round;
    }
}
