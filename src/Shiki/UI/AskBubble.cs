using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shiki.Core.Ai;
using Shiki.Core.Voice;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// The quick way to talk to the cat: a small card right above it (from the shortcut), with a box
/// to type in and this turn of the conversation. The whole chat lives on the dashboard's Ask page.
/// </summary>
public sealed class AskBubble : Window
{
    const double CardWidth = 560, Shadow = 18;

    readonly CatChat _chat;
    readonly DashKit _k;
    readonly string _catName;
    readonly ImageSource _head;
    readonly Action<string> _open;
    readonly Action _openDashboard;
    readonly StackPanel _items = new();
    readonly ScrollViewer _scroll;
    readonly ChatComposer _input;
    readonly VoiceInput? _voice;
    double _bottom;
    // Where this visit starts in the chat: older turns stay on the Ask page, unless one is unfinished.
    int _first;

    public AskBubble(CatChat chat, VoiceInput? voice, DashPalette palette, string catName, ImageSource head, Action<string> open, Action openDashboard)
    {
        _chat = chat;
        _voice = voice;
        _k = new DashKit(palette);
        _catName = catName;
        _head = head;
        _open = open;
        _openDashboard = openDashboard;
        var p = palette;

        Title = $"Ask {catName}";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = CardWidth + Shadow * 2;
        SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;

        // ---- Header ----
        var header = new DockPanel { Margin = new Thickness(16, 12, 10, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_k.IconButton("\uE8A7", () => { _openDashboard(); Close(); }, "Open the whole chat", 13, p.Muted, 30));
        buttons.Children.Add(_k.IconButton("\uE8BB", Close, "Close (Esc)", 11, p.Muted, 30));
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        var face = new Image { Source = head, Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) };
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        DockPanel.SetDock(face, Dock.Left);
        header.Children.Add(face);
        var name = _k.Text($"Ask {catName}", 14.5, weight: FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(name);

        // ---- This turn ----
        _items.Margin = new Thickness(16, 0, 16, 6);
        // Taller on a taller screen, so a long answer reads without much scrolling.
        _scroll = new ScrollViewer { Content = _items, MaxHeight = Math.Clamp(SystemParameters.WorkArea.Height * 0.55, 380, 640), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        // ---- Box ----
        _input = new ChatComposer(_k, catName, voice, text => { if (!_chat.Busy) _ = _chat.Send(text); }, compact: true);
        _input.Root.Margin = new Thickness(12, 4, 12, 12);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_scroll);
        stack.Children.Add(_input.Root);
        Content = new Border
        {
            Margin = new Thickness(Shadow),
            Background = DashKit.B(p.Card),
            BorderBrush = DashKit.B(p.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = p.Dark ? 0.5 : 0.18 },
        };

        _first = chat.Busy || chat.Items.OfType<PlanItem>().Any(x => x.State is PlanState.Waiting or PlanState.Running)
            ? LastSaid() : chat.Items.Count;
        _chat.Changed += Refresh;
        Closed += (_, _) =>
        {
            _chat.Changed -= Refresh;
            _input.Detach();
            if (_voice is { Active: true }) _voice.Cancel();
        };
        // Esc stops listening first, then closes.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_voice is { Active: true }) _voice.Cancel();
            else Close();
        };
        // Clicking elsewhere puts it away, unless the cat is listening, busy or waiting for an answer.
        bool closing = false;
        Closing += (_, _) => closing = true;
        Deactivated += (_, _) =>
        {
            // Closing itself (Esc, the X) deactivates it too; it's already on its way out.
            if (closing) return;
            if (_voice is { Active: true } || _chat.Busy || _chat.Items.OfType<PlanItem>().Any(x => x.State is PlanState.Waiting or PlanState.Running)) return;
            Close();
        };
        SizeChanged += (_, _) => Top = _bottom - ActualHeight;
        Loaded += (_, _) =>
        {
            Shell.NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
            Activate();
            _input.Box.Focus();
        };
        Refresh();
    }

    /// <summary>Sits with its bottom centre just above this point (DIPs), kept on screen.</summary>
    public void Place(Point bottomCentre)
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(bottomCentre.X - Width / 2, area.Left - Shadow + 8, area.Right - Width + Shadow - 8);
        _bottom = Math.Min(bottomCentre.Y + Shadow, area.Bottom + Shadow - 8);
        Top = _bottom - (ActualHeight > 0 ? ActualHeight : 160);
    }

    public void FocusBox()
    {
        Activate();
        _input.Box.Focus();
    }

    int LastSaid()
    {
        for (int i = _chat.Items.Count - 1; i >= 0; i--)
            if (_chat.Items[i] is YouSaid) return i;
        return 0;
    }

    void Refresh()
    {
        _items.Children.Clear();
        var cards = new ChatCards(_k, _chat, _catName, _head, _open, () => { _openDashboard(); Close(); }, compact: true);
        // Just this turn: from the last thing the person said.
        var items = _chat.Items;
        if (_first > items.Count) _first = 0;
        int start = Math.Max(_first, LastSaid());
        for (int i = start; i < items.Count; i++) _items.Children.Add(cards.Render(items[i]));
        if (_chat.Busy) _items.Children.Add(cards.Thinking());
        else if (_chat.Picture is { } picture)
            _items.Children.Add(cards.PictureStrip(picture, question => _ = _chat.Send(question), _chat.ForgetPicture));
        _scroll.Visibility = _items.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _scroll.Dispatcher.BeginInvoke(() => _scroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
