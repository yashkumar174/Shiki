using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shiki.Core;
using Shiki.Core.Ai;
using Shiki.Shell;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>
/// Writing help over any app: the text you selected, a row of things to do with it (fix, shorter,
/// clearer, a tone, a language, a reply, or your own words), then the new text to check, change
/// if you like, and put back in place of the old with one press of Enter.
/// </summary>
public sealed class WritingPanel : Window
{
    public const double CardWidth = 540;
    const double Shadow = 18;

    readonly DashKit _k;
    readonly IntPtr _target;
    readonly Action<double> _spent;
    readonly ContentControl _body = new() { Focusable = false };
    string? _text;
    WriteAsk _ask;
    string? _how;
    CancellationTokenSource? _work;
    bool _keepOpen;
    Action? _enter;

    DashPalette P => _k.P;

    /// <param name="target">The window the text came from (it goes back there).</param>
    /// <param name="text">The selected text, or null when nothing was selected.</param>
    public WritingPanel(DashPalette palette, ImageSource head, IntPtr target, string? text, Action<double> spent)
    {
        _k = new DashKit(palette);
        _target = target;
        _text = text;
        _spent = spent;

        Title = "Writing help";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = CardWidth + Shadow * 2;
        SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;

        var header = new DockPanel { Margin = new Thickness(18, 14, 10, 2) };
        var close = _k.IconButton("\uE8BB", Close, "Close (Esc)", 11, P.Muted, 30);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var face = new Image { Source = head, Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) };
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        DockPanel.SetDock(face, Dock.Left);
        header.Children.Add(face);
        var name = _k.Text("Writing help", 14.5, weight: FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(name);

        _body.Margin = new Thickness(18, 6, 18, 18);
        Content = new Border
        {
            Margin = new Thickness(Shadow),
            Background = DashKit.B(P.Card),
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Child = new StackPanel { Children = { header, _body } },
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = P.Dark ? 0.5 : 0.18 },
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_work != null) _work.Cancel(); else Close();
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && _enter != null && Keyboard.FocusedElement is not TextBox { AcceptsReturn: true })
            {
                e.Handled = true;
                _enter();
            }
        };
        // Clicking away puts it away while you're still choosing; once it's writing or has written, it stays.
        bool closing = false;
        Closing += (_, _) => { closing = true; _work?.Cancel(); };
        Deactivated += (_, _) => { if (!closing && !_keepOpen) Close(); };

        if (_text == null) ShowNothing();
        else ShowChoices();
    }

    /// <summary>Dev aid: the card as it looks with this result (null: the choices), for a snapshot.</summary>
    public FrameworkElement Preview(WriteAsk ask, string? result)
    {
        _ask = ask;
        if (result != null) ShowResult(result);
        var card = (FrameworkElement)Content;
        Content = null;
        return card;
    }

    /// <summary>Starts on this straight away (from a voice request).</summary>
    public void Start(WriteAsk ask) { if (_text != null && _work == null) Run(ask, null); }

    /// <summary>Near the mouse (where the text usually is), kept on the screen.</summary>
    public void PlaceNearMouse()
    {
        double scale = NativeMethods.SystemScale;
        NativeMethods.GetCursorPos(out var cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        double x = cursor.X / scale - 40, y = cursor.Y / scale + 18;
        double left = screen.Left / scale, top = screen.Top / scale, right = screen.Right / scale, bottom = screen.Bottom / scale;
        const double Tall = 420; // about how tall it gets with a result
        if (y + Tall > bottom) y = Math.Max(top, cursor.Y / scale - Tall - 12);
        Left = Math.Clamp(x, left, right - Width);
        Top = Math.Clamp(y, top, Math.Max(top, bottom - 200));
    }

    // ---- Nothing selected ----

    void ShowNothing()
    {
        _keepOpen = false;
        var stack = new StackPanel();
        stack.Children.Add(_k.Text("Select some text first", 17, P.Text, FontWeights.Bold));
        var how = _k.Text("Select text in any app (a message, an email, a document), then press the shortcut again. Or use everything in the box you're typing in:", 14, P.Muted, wrap: true);
        how.Margin = new Thickness(0, 6, 0, 14);
        stack.Children.Add(how);
        var all = _k.Button("Use the whole box", () => _ = UseWholeBox(), DashKit.Pill.Accent, "\uE8B3", 13);
        all.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(all);
        _enter = () => _ = UseWholeBox();
        _body.Content = stack;
    }

    async Task UseWholeBox()
    {
        _keepOpen = true;
        Hide();
        var text = await Selection.Read(_target, all: true);
        _text = text;
        Show();
        Activate();
        if (text == null) { ShowProblem("There was no text there to use.", ShowNothing); return; }
        ShowChoices();
    }

    // ---- Choosing ----

    void ShowChoices()
    {
        _keepOpen = false;
        _enter = () => Run(WriteAsk.Fix, null);
        var stack = new StackPanel();

        if (_text!.Length > Writer.Most)
        {
            ShowProblem($"That's a lot of text ({_text.Length:N0} characters). Select up to about {Writer.Most:N0} at a time.", null);
            return;
        }
        if (PrivacyGuard.On && PrivacyGuard.Clean(_text).Hidden is { Count: > 0 } hidden)
        {
            ShowProblem($"This has {Article(hidden[0])} in it, so it isn't sent to Claude. Select the part without it.", null);
            return;
        }

        // What was selected, in a quiet box.
        var original = _k.Text(Squash(_text), 13.5, P.Muted, wrap: true);
        original.MaxHeight = 64;
        original.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(new Border
        {
            Background = DashKit.B(P.Hover),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 9, 12, 10),
            Child = original,
        });

        var grid = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        void Add(WriteAsk ask, string glyph, bool main = false)
        {
            var button = _k.Button(Writer.Label(ask), () => Run(ask, null), main ? DashKit.Pill.Accent : DashKit.Pill.Quiet, glyph, 13);
            button.Margin = new Thickness(0, 0, 8, 8);
            grid.Children.Add(button);
        }
        Add(WriteAsk.Fix, "\uE8FB", main: true);
        Add(WriteAsk.Shorter, "\uE8C5");
        Add(WriteAsk.Clearer, "\uE7B3");
        Add(WriteAsk.Polite, "\uE76E");
        Add(WriteAsk.Friendly, "\uED54");
        Add(WriteAsk.Professional, "\uE821");
        Add(WriteAsk.English, "\uE8C1");
        Add(WriteAsk.Hindi, "\uE8C1");
        Add(WriteAsk.Reply, "\uE97A");
        stack.Children.Add(grid);

        // Or in your own words.
        var box = new TextBox
        {
            FontFamily = DashKit.Sans,
            FontSize = 14,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            SelectionBrush = DashKit.B(P.Accent),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = _k.Text("Or say how: \"funnier\", \"as bullet points\", \"for my boss\"…", 14, P.Faint);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(box.Text)) return;
            e.Handled = true;
            Run(WriteAsk.Custom, box.Text.Trim());
        };
        var field = new Border
        {
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new Grid { Children = { hint, box } },
        };
        stack.Children.Add(field);
        var keys = _k.Text("Enter fixes it.  Esc closes.", 12, P.Faint);
        keys.Margin = new Thickness(2, 10, 0, 0);
        stack.Children.Add(keys);
        _body.Content = stack;
    }

    // ---- Writing ----

    void Run(WriteAsk ask, string? how)
    {
        if (_text == null || _work != null) return;
        _ask = ask;
        _how = how;
        _keepOpen = true;
        _enter = null;
        var work = _work = new CancellationTokenSource();
        var stack = new StackPanel();
        stack.Children.Add(_k.Text(ask == WriteAsk.Custom ? $"Working on it: {how}" : $"{Writer.Label(ask)}…", 16, P.Text, FontWeights.Bold));
        var wait = _k.Text("A moment.", 14, P.Muted);
        wait.Margin = new Thickness(0, 6, 0, 14);
        stack.Children.Add(wait);
        var stop = _k.Button("Stop", () => work.Cancel(), DashKit.Pill.Quiet, "\uE711", 13);
        stop.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(stop);
        _body.Content = stack;
        _ = Write(work);
    }

    async Task Write(CancellationTokenSource work)
    {
        try
        {
            var (result, cost) = await Writer.Rewrite(_text!, _ask, _how, work.Token);
            _spent(cost);
            AiLog.Write("writing", $"{Writer.Label(_ask)}{(_how != null ? $" ({_how})" : "")}: {_text!.Length} characters");
            _work = null;
            ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            _work = null;
            if (IsVisible) ShowChoices();
        }
        catch (Exception ex)
        {
            _work = null;
            Log.Error("Writing help", ex);
            var key = ex.Message.Contains("x-api-key", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase);
            ShowProblem(key ? "Claude didn't accept the key. Put a new one in Settings, Ask the cat." : "Claude didn't answer. Check the internet and try again.", ShowChoices);
        }
    }

    // ---- The result ----

    void ShowResult(string result)
    {
        _keepOpen = true;
        bool reply = Writer.IsNew(_ask);
        bool same = !reply && result.Trim() == _text!.Trim();
        // Nowhere to put it back (the Try on the skills page): copying is the way.
        bool copyOnly = reply || _target == IntPtr.Zero;
        var stack = new StackPanel();
        stack.Children.Add(_k.Text(same ? "It's already right" : reply ? "A reply" : _ask == WriteAsk.Custom ? "Done" : Writer.Label(_ask), 16, P.Text, FontWeights.Bold));
        if (same)
        {
            var note = _k.Text("Nothing needed fixing.", 14, P.Muted);
            note.Margin = new Thickness(0, 6, 0, 14);
            stack.Children.Add(note);
            var back = _k.Button("Back", ShowChoices, DashKit.Pill.Quiet, "\uE72B", 13);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(back);
            _enter = Close;
            _body.Content = stack;
            return;
        }

        // The new text, which you can still change before it goes in.
        var box = new TextBox
        {
            Text = result,
            FontFamily = DashKit.Sans,
            FontSize = 14.5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            SelectionBrush = DashKit.B(P.Accent),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        box.Resources.Add(typeof(ScrollBar), DashboardWindow.ThinScrollBar(P, "0,2,0,2"));
        stack.Children.Add(new Border
        {
            BorderBrush = DashKit.B(P.Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 10, 0, 0),
            Child = box,
        });
        var tip = _k.Text(reply ? "Copy it, then paste it into the reply box." : copyOnly ? "Copy it, then paste it anywhere." : "You can change it here first.", 12, P.Faint);
        tip.Margin = new Thickness(2, 8, 0, 12);
        stack.Children.Add(tip);

        var buttons = new WrapPanel();
        void Add(string label, Action go, bool main, string glyph)
        {
            var button = _k.Button(label, go, main ? DashKit.Pill.Accent : DashKit.Pill.Quiet, glyph, 13);
            button.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(button);
        }
        if (copyOnly)
        {
            Add("Copy", () => CopyAndClose(box.Text), true, "\uE8C8");
            _enter = () => CopyAndClose(box.Text);
        }
        else
        {
            Add("Replace", () => _ = Put(box.Text), true, "\uE8FB");
            Add("Copy", () => CopyAndClose(box.Text), false, "\uE8C8");
            _enter = () => _ = Put(box.Text);
        }
        Add("Try again", () => Run(_ask, _how), false, "\uE72C");
        Add("Back", ShowChoices, false, "\uE72B");
        stack.Children.Add(buttons);
        if (!copyOnly)
        {
            var keys = _k.Text("Enter puts it in place of what you selected.", 12, P.Faint);
            keys.Margin = new Thickness(2, 10, 0, 0);
            stack.Children.Add(keys);
        }
        _body.Content = stack;
    }

    async Task Put(string text)
    {
        _keepOpen = true;
        Hide();
        bool done = await Selection.Replace(_target, text);
        var target = _target;
        if (done)
        {
            ToastHost.Show(ToastWindow.Message("Replaced", "The new text is in. Changed your mind?", null, ("Undo", () => _ = Selection.Undo(target))));
            Close();
        }
        else
        {
            Show();
            Activate();
            ShowProblem("That window has closed. Copy the text instead.", () => ShowResult(text));
        }
    }

    void CopyAndClose(string text)
    {
        try { Clipboard.SetText(text); } catch (Exception) { }
        ToastHost.Show(ToastWindow.Message("Copied", "Paste it with Ctrl+V."));
        Close();
    }

    // ---- When it can't ----

    void ShowProblem(string text, Action? back)
    {
        _keepOpen = false;
        var stack = new StackPanel();
        var say = _k.Text(text, 14.5, P.Text, wrap: true);
        stack.Children.Add(say);
        if (back != null)
        {
            var button = _k.Button("Back", back, DashKit.Pill.Quiet, "\uE72B", 13);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(button);
            _enter = back;
        }
        else _enter = Close;
        _body.Content = stack;
    }

    static string Article(string what) => what.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(what[0])) ? $"an {what.ToLowerInvariant()}" : $"a {what.ToLowerInvariant()}";

    /// <summary>The selection on a few lines: runs of blank lines and spaces folded.</summary>
    static string Squash(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s*\n\s*\n\s*", "\n");
}
