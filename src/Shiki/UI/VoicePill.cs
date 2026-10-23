using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Shiki.Core.Ai;
using Shiki.Core.Voice;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// "Hey Sky": a small pill above the cat instead of the whole chat. A waveform moves with your
/// voice and your words appear as you say them; then the cat's short answer (or what it did).
/// Anything that needs more room (a card to confirm, a table, a long answer) opens the full
/// chat; so does a click. It never takes the keyboard, and fades once the conversation ends.
/// </summary>
public sealed class VoicePill : Window
{
    const double CardWidth = 420, Shadow = 16;
    readonly CatChat _chat;
    readonly VoiceInput _voice;
    readonly Action _expand;
    readonly DashKit _k;
    readonly Waveform _wave;
    readonly Border _waveDisc;
    readonly TextBlock _words, _answer;
    readonly int _start;
    readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(14) };
    double _bottom;
    bool _closing, _expanded;

    public VoicePill(CatChat chat, VoiceInput voice, DashPalette palette, Action expand)
    {
        _chat = chat;
        _voice = voice;
        _expand = expand;
        _k = new DashKit(palette);
        var p = palette;
        _start = chat.Items.Count;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = CardWidth + Shadow * 2;
        SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;

        _wave = new Waveform(DashKit.B(p.AccentText), 16, 3, 3) { Mode = Waveform.Modes.Listening, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _waveDisc = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = DashKit.B(p.Accent),
            Child = _wave,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
        };
        _words = _k.Text("Listening…", 15, p.Text, wrap: true);
        _words.MaxHeight = 66;
        _words.TextTrimming = TextTrimming.CharacterEllipsis;
        _answer = _k.Text("", 14, p.Muted, wrap: true);
        _answer.MaxHeight = 80;
        _answer.TextTrimming = TextTrimming.CharacterEllipsis;
        _answer.Margin = new Thickness(0, 6, 0, 0);
        _answer.Visibility = Visibility.Collapsed;
        var open = _k.IconButton("\uE8A7", Expand, "Open the chat", 12, p.Faint, 26);
        open.VerticalAlignment = VerticalAlignment.Top;
        open.Margin = new Thickness(8, -2, -4, 0);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinHeight = 38 };
        text.Children.Add(_words);
        text.Children.Add(_answer);
        var row = new DockPanel();
        DockPanel.SetDock(_waveDisc, Dock.Left);
        DockPanel.SetDock(open, Dock.Right);
        row.Children.Add(_waveDisc);
        row.Children.Add(open);
        row.Children.Add(text);
        var card = new Border
        {
            Margin = new Thickness(Shadow),
            Padding = new Thickness(12, 12, 14, 12),
            Background = DashKit.B(p.Card),
            BorderBrush = DashKit.B(p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(26),
            Child = row,
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = p.Dark ? 0.5 : 0.18 },
        };
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Expand(); };
        Content = card;
        Opacity = 0;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Never takes the keyboard: whatever you were typing in keeps it.
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
        };
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 160 : 0)));
        SizeChanged += (_, _) => Top = _bottom - ActualHeight;
        _voice.Changed += Refresh;
        _chat.Changed += Refresh;
        _voice.ConversationEnded += Ended;
        _idle.Tick += (_, _) => { if (!_voice.Active && !_chat.Busy) FadeOut(); };
        _idle.Start();
        Closed += (_, _) =>
        {
            _voice.Changed -= Refresh;
            _chat.Changed -= Refresh;
            _voice.ConversationEnded -= Ended;
            _idle.Stop();
        };
        Refresh();
    }

    /// <summary>Sits above the cat, like the chat bubble.</summary>
    public void Place(Point bottomCentre)
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(bottomCentre.X - Width / 2, area.Left - Shadow + 8, area.Right - Width + Shadow - 8);
        _bottom = Math.Min(bottomCentre.Y + Shadow, area.Bottom + Shadow - 8);
        Top = _bottom - (ActualHeight > 0 ? ActualHeight : 100);
    }

    void Refresh()
    {
        if (_closing) return;
        // Something needs the whole chat: a card to answer, a table, a problem, or a long answer.
        var since = _chat.Items.Skip(_start).ToList();
        if (since.Any(i => i is PlanItem { State: PlanState.Waiting } or TableItem or ChatProblem)
            || since.OfType<CatSaid>().LastOrDefault() is { } long_ && !_chat.Busy && Long(long_.Text))
        {
            Expand();
            return;
        }
        _idle.Stop();
        _idle.Start();

        var state = _voice.State;
        bool hearing = state is VoiceState.Waiting or VoiceState.Hearing;
        _wave.Mode = hearing ? Waveform.Modes.Listening : _chat.Busy || state == VoiceState.Understanding ? Waveform.Modes.Thinking : Waveform.Modes.Still;
        _wave.Level = _voice.Level;
        _waveDisc.Background = DashKit.B(hearing || _chat.Busy || state == VoiceState.Understanding ? _k.P.Accent : _k.P.Hover);
        _wave.Colour = DashKit.B(hearing || _chat.Busy || state == VoiceState.Understanding ? _k.P.AccentText : _k.P.Muted);

        // The newest words: what's being said now, else the last thing said this time.
        var lastYou = since.OfType<YouSaid>().LastOrDefault();
        var lastYouIndex = lastYou == null ? -1 : since.LastIndexOf(lastYou);
        if (hearing || state == VoiceState.Understanding)
        {
            _words.Text = _voice.Partial.Length > 0 ? _voice.Partial
                : state == VoiceState.Understanding ? "…"
                : _voice.FollowingUp ? "Say what's next, or \"thanks\" to stop"
                : "Listening…";
            _words.Foreground = DashKit.B(_voice.Partial.Length > 0 ? _k.P.Text : _k.P.Muted);
        }
        else if (lastYou != null)
        {
            _words.Text = lastYou.Text;
            _words.Foreground = DashKit.B(_k.P.Text);
        }

        // The answer to it: what the cat said, or what it did.
        var after = lastYouIndex >= 0 ? since.Skip(lastYouIndex + 1).ToList() : since;
        string? answer = after.OfType<CatSaid>().LastOrDefault()?.Text is { Length: > 0 } said ? Plain(said)
            : after.OfType<CatDid>().LastOrDefault()?.Text;
        if (_chat.Busy && answer == null) answer = "Thinking…";
        _answer.Text = answer ?? "";
        _answer.Visibility = answer != null && !(hearing && _voice.Partial.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Dev aid: fixed words and an answer, for a still picture.</summary>
    public void Preview(string words, string? answer, double level)
    {
        _closing = true;
        _words.Text = words;
        _words.Foreground = DashKit.B(_k.P.Text);
        _answer.Text = answer ?? "";
        _answer.Visibility = answer == null ? Visibility.Collapsed : Visibility.Visible;
        _waveDisc.Background = DashKit.B(_k.P.Accent);
        _wave.Colour = DashKit.B(_k.P.AccentText);
        _wave.Level = level;
        _wave.Snap();
    }

    static bool Long(string text) => text.Length > 260 || Regex.IsMatch(text, @"\n\s*([-*•]|\d+[.)])\s|\n\s*\n");

    static string Plain(string markdown) =>
        Regex.Replace(Regex.Replace(markdown, @"\*\*(.+?)\*\*|`([^`]+)`", "$1$2"), @"\s+", " ").Trim();

    void Ended()
    {
        // The conversation is over: let the last answer show for a moment, then go.
        var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        later.Tick += (_, _) => { later.Stop(); if (!_voice.Active && !_chat.Busy) FadeOut(); };
        later.Start();
    }

    void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        _closing = true;
        _expand();
        Close();
    }

    void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 260 : 0));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
