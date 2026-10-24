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
/// "Hey Sky": a small pill above the cat instead of the whole chat. It springs open from a dot,
/// a waveform moves with your voice and your words appear as you say them; then the cat's short
/// answer (or what it did). Anything that needs more room (a card to confirm, a table, a long
/// answer) grows the pill into the full chat; so does a click. It never takes the keyboard, and
/// shrinks back to a dot once the conversation ends.
/// </summary>
/// <remarks>
/// The window is as wide as the chat bubble and tall enough for it, and see-through, so the card
/// can change shape inside it; the bubble sits at the same bottom centre, so the pill grows
/// straight into it.
/// </remarks>
public sealed class VoicePill : Window
{
    const double CardWidth = 420, Shadow = 18, Dot = 64, Round = Dot / 2;
    // The row inside the card: its width less the padding and the border.
    const double Inner = CardWidth - 26 - 2;
    readonly CatChat _chat;
    readonly VoiceInput _voice;
    readonly Action _expand;
    readonly DashKit _k;
    readonly Canvas _canvas = new();
    readonly Border _card;
    readonly Grid _clip;
    readonly FrameworkElement _text;
    readonly Waveform _wave;
    readonly Border _waveDisc;
    readonly TextBlock _words, _answer;
    readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(14) };
    Spring? _spring;
    double _bottom;
    bool _closing, _expanded, _morphing;

    /// <summary>Where this conversation starts in the chat, for the bubble that takes over.</summary>
    public int Start { get; }

    public VoicePill(CatChat chat, VoiceInput voice, DashPalette palette, Action expand)
    {
        _chat = chat;
        _voice = voice;
        _expand = expand;
        _k = new DashKit(palette);
        var p = palette;
        Start = chat.Items.Count;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = AskBubble.CardWidth + Shadow * 2;
        Height = AskBubble.TallestCard + Shadow * 2 + 24;
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
        _words = _k.Text("Listening…", 15, p.Muted, wrap: true);
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

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinHeight = 38 };
        words.Children.Add(_words);
        words.Children.Add(_answer);
        var text = new DockPanel();
        DockPanel.SetDock(open, Dock.Right);
        text.Children.Add(open);
        text.Children.Add(words);
        _text = text;
        var row = new DockPanel { Width = Inner, HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(_waveDisc, Dock.Left);
        row.Children.Add(_waveDisc);
        row.Children.Add(text);
        // The row keeps its full width while the card is narrower, so nothing rewraps as it grows.
        _clip = new Grid { ClipToBounds = true, Children = { row } };
        _card = new Border
        {
            Width = Dot,
            Padding = new Thickness(12, 12, 14, 12),
            Background = DashKit.B(p.Card),
            BorderBrush = DashKit.B(p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Round),
            Child = _clip,
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = p.Dark ? 0.5 : 0.18 },
        };
        _card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Expand(); };
        _card.SizeChanged += (_, _) => PlaceCard();
        _canvas.Children.Add(_card);
        Content = _canvas;
        _text.Opacity = 0;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Never takes the keyboard: whatever you were typing in keeps it.
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
        };
        Loaded += (_, _) => Open();
        _voice.Changed += Refresh;
        _chat.Changed += Refresh;
        _voice.ConversationEnded += Ended;
        _idle.Tick += (_, _) => { if (!_voice.Active && !_chat.Busy) FadeOut(); };
        _idle.Start();
        Closed += (_, _) =>
        {
            _spring?.Stop();
            _voice.Changed -= Refresh;
            _chat.Changed -= Refresh;
            _voice.ConversationEnded -= Ended;
            _idle.Stop();
        };
        Refresh();
    }

    /// <summary>Sits above the cat, with the same bottom centre the chat bubble would have.</summary>
    public void Place(Point bottomCentre)
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(bottomCentre.X - Width / 2, area.Left - Shadow + 8, area.Right - Width + Shadow - 8);
        _bottom = Math.Min(bottomCentre.Y + Shadow, area.Bottom + Shadow - 8);
        Top = _bottom - Height;
    }

    /// <summary>Bottom centre of the window, while it's a pill.</summary>
    void PlaceCard()
    {
        if (_morphing) return;
        Canvas.SetLeft(_card, (Width - _card.Width) / 2);
        Canvas.SetTop(_card, Height - Shadow - _card.ActualHeight);
    }

    /// <summary>From a dot to the full pill, then the words fade in.</summary>
    void Open()
    {
        _spring = new Spring(x =>
        {
            _card.Width = Math.Max(Dot, Dot + (CardWidth - Dot) * x);
            PlaceCard();
        }, response: 0.5, damping: 0.72).Start();
        _text.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 200 : 0))
        {
            BeginTime = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 140 : 0),
        });
    }

    void Refresh()
    {
        if (_closing) return;
        // Something needs the whole chat: a card to answer, a table, a problem, or a long answer.
        var since = _chat.Items.Skip(Start).ToList();
        if (since.Any(i => i is PlanItem { State: PlanState.Waiting } or ChoiceItem { Picked: null } or TableItem or ChatProblem)
            || since.OfType<CatSaid>().LastOrDefault() is { } long_ && !_chat.Busy && Long(long_.Text))
        {
            Expand();
            return;
        }
        _idle.Stop();
        _idle.Start();

        var state = _voice.State;
        bool hearing = state is VoiceState.Waiting or VoiceState.Hearing;
        bool lit = hearing || _chat.Busy || state == VoiceState.Understanding;
        _wave.Mode = hearing ? Waveform.Modes.Listening : lit ? Waveform.Modes.Thinking : Waveform.Modes.Still;
        _wave.Level = _voice.Level;
        _waveDisc.Background = DashKit.B(lit ? _k.P.Accent : _k.P.Hover);
        _wave.Colour = DashKit.B(lit ? _k.P.AccentText : _k.P.Muted);

        // The newest words: what's being said now, else the last thing said this time.
        var lastYou = since.OfType<YouSaid>().LastOrDefault();
        var lastYouIndex = lastYou == null ? -1 : since.LastIndexOf(lastYou);
        if (hearing || state == VoiceState.Understanding)
        {
            _words.Text = _voice.Partial.Length > 0 ? _voice.Partial
                : state == VoiceState.Understanding && lastYou == null ? "…"
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

    /// <summary>
    /// Grows the card into the chat bubble's card (screen DIPs): the words fade, the shape springs
    /// to the new size and corners, then the bubble fades in on top and this goes.
    /// </summary>
    public void MorphInto(Rect target, double radius, Action reveal)
    {
        _closing = true;
        _idle.Stop();
        _spring?.Stop();
        _morphing = true;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        var from = new Rect(Canvas.GetLeft(_card), Canvas.GetTop(_card), _card.ActualWidth, _card.ActualHeight);
        var to = new Rect(target.X - Left, target.Y - Top, target.Width, target.Height);
        _card.Height = from.Height;
        _clip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 110 : 0)));
        static double Mix(double a, double b, double t) => a + (b - a) * t;
        bool revealed = false;
        _spring = new Spring(t =>
        {
            // The chat starts fading in as the shape arrives, not after it has fully settled.
            if (!revealed && t > 0.9)
            {
                revealed = true;
                reveal();
            }
            Canvas.SetLeft(_card, Mix(from.X, to.X, t));
            Canvas.SetTop(_card, Mix(from.Y, to.Y, t));
            _card.Width = Math.Max(Dot, Mix(from.Width, to.Width, t));
            _card.Height = Math.Max(Dot, Mix(from.Height, to.Height, t));
            _card.CornerRadius = new CornerRadius(Math.Max(radius, Mix(Round, radius, t)));
        }, () =>
        {
            if (!revealed) reveal();
            var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
            later.Tick += (_, _) => { later.Stop(); Close(); };
            later.Start();
        }, response: 0.42, damping: 0.8).Start();
    }

    /// <summary>Dev aid: fixed words and an answer, for a still picture of the open pill.</summary>
    public FrameworkElement Preview(string words, string? answer, double level)
    {
        _closing = true;
        _spring?.Stop();
        _words.Text = words;
        _words.Foreground = DashKit.B(_k.P.Text);
        _answer.Text = answer ?? "";
        _answer.Visibility = answer == null ? Visibility.Collapsed : Visibility.Visible;
        _waveDisc.Background = DashKit.B(_k.P.Accent);
        _wave.Colour = DashKit.B(_k.P.AccentText);
        _wave.Level = level;
        _wave.Snap();
        _text.Opacity = 1;
        _card.Width = CardWidth;
        _canvas.Children.Remove(_card);
        return new Border { Padding = new Thickness(Shadow), Child = _card };
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
        // The app opens the bubble and calls MorphInto (or closes this if there's no bubble).
        _expand();
    }

    /// <summary>The words go, the pill shrinks back to a dot and fades.</summary>
    void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        _spring?.Stop();
        double from = _card.ActualWidth;
        _text.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 120 : 0)));
        _spring = new Spring(x =>
        {
            _card.Width = Math.Max(Dot, from + (Dot - from) * x);
            PlaceCard();
            Opacity = Math.Clamp(1 - (x - 0.55) / 0.45, 0, 1);
        }, Close, response: 0.4, damping: 1).Start();
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
