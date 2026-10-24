using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shiki.Core;
using Shiki.Core.Ai;

namespace Shiki.UI.Dashboard;

/// <summary>
/// Draws the cat chat's items: what you said, what the cat said, the quiet lines about what it
/// looked at, and plan cards with Do it and Not now. Used by the Ask page and the bubble.
/// </summary>
public sealed class ChatCards(DashKit k, CatChat chat, string catName, ImageSource catHead, Action<string> open, Action openSettings, bool compact = false)
{
    DashPalette P => k.P;
    double Size => compact ? 14 : 15.5;
    const string MicGlyph = "\uE720";

    public UIElement Render(ChatItem item) => item switch
    {
        YouSaid you => You(you.Text, you.Spoken, you.Picture),
        TableItem table => Table(table),
        CatSaid cat => Cat(cat.Text),
        CatDid did => Did(did.Glyph, did.Text),
        ChatProblem problem => Problem(problem),
        PlanItem plan => Plan(plan),
        ChoiceItem choice => Choice(choice),
        _ => new Border(),
    };

    UIElement You(string text, bool spoken, byte[]? picture)
    {
        UIElement content = Selectable(text, Size, P.Text);
        if (picture != null)
        {
            // What the paw picked, above the words that went with it.
            var stack = new StackPanel();
            var thumb = Thumbnail(picture, compact ? 260 : 420, compact ? 150 : 240);
            thumb.Margin = new Thickness(0, 2, 0, 8);
            thumb.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(thumb);
            stack.Children.Add(content);
            content = stack;
        }
        if (spoken)
        {
            // Said out loud: a small mic in front, so misheard words make sense.
            var mic = k.Icon(MicGlyph, Size - 3, P.Muted);
            mic.VerticalAlignment = VerticalAlignment.Top;
            mic.Margin = new Thickness(0, 3, 8, 0);
            var row = new DockPanel();
            DockPanel.SetDock(mic, Dock.Left);
            row.Children.Add(mic);
            row.Children.Add(content);
            content = row;
        }
        var bubble = new Border
        {
            Background = DashKit.B(P.Selected),
            CornerRadius = new CornerRadius(16, 16, 4, 16),
            Padding = new Thickness(14, 9, 14, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = compact ? 420 : 560,
            Margin = new Thickness(compact ? 40 : 120, compact ? 8 : 14, 0, 4),
            Child = content,
        };
        return bubble;
    }

    UIElement Cat(string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, compact ? 8 : 14, compact ? 0 : 80, 2) };
        var head = Head(compact ? 20 : 24);
        head.VerticalAlignment = VerticalAlignment.Top;
        head.Margin = new Thickness(0, 1, compact ? 10 : 12, 0);
        DockPanel.SetDock(head, Dock.Left);
        row.Children.Add(head);
        // Paragraphs, lists and bold, the way the answer was written.
        row.Children.Add(ChatText.View(text, Size, P.Text, P.Muted, P.Accent, P.Hover));
        return row;
    }

    UIElement Did(string glyph, string text)
    {
        var row = new DockPanel { Margin = new Thickness(compact ? 30 : 36, 5, 0, 1) };
        var icon = k.Icon(glyph, 12, P.Faint);
        icon.Margin = new Thickness(0, 2, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        // Wraps rather than running off the edge ("Added to your calendar: Mon 26 Oct, 07:45–08:45: Dentist…").
        row.Children.Add(k.Text(text, compact ? 12.5 : 13, P.Faint, wrap: true));
        return row;
    }

    UIElement Problem(ChatProblem problem)
    {
        var stack = new StackPanel { Margin = new Thickness(compact ? 30 : 36, 10, 0, 2) };
        stack.Children.Add(k.Text(problem.Text, compact ? 13.5 : 14.5, P.Danger, wrap: true));
        if (problem.NeedsKey)
        {
            var settings = k.Button("Open settings", openSettings, DashKit.Pill.Quiet, size: 13);
            settings.Padding = new Thickness(14, 6, 14, 7);
            settings.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(settings);
        }
        return stack;
    }

    /// <summary>"Which Yash?": a button per option; once answered, the pick is shown.</summary>
    UIElement Choice(ChoiceItem choice)
    {
        var stack = new StackPanel();
        stack.Children.Add(k.Text(choice.Question, compact ? 14.5 : 16, weight: FontWeights.SemiBold, wrap: true));
        var options = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var option in choice.Options)
        {
            bool picked = choice.Picked == option;
            if (choice.Picked == null)
            {
                var button = k.Button(option, () => chat.Choose(choice, option), DashKit.Pill.Quiet, size: compact ? 13 : 13.5);
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 0, 0, 6);
                options.Children.Add(button);
            }
            else
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var mark = k.Icon(picked ? "\uE73E" : "\uE738", 11, picked ? P.Text : P.Faint);
                mark.Margin = new Thickness(0, 2, 8, 0);
                DockPanel.SetDock(mark, Dock.Left);
                row.Children.Add(mark);
                row.Children.Add(k.Text(option, compact ? 13 : 13.5, picked ? P.Text : P.Faint, wrap: true));
                options.Children.Add(row);
            }
        }
        stack.Children.Add(options);
        if (choice.Picked == null)
        {
            var hint = k.Text("Or say it: \"the first one\", or the name.", compact ? 11.5 : 12, P.Faint);
            hint.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(hint);
        }
        var card = k.Card(stack, compact ? new Thickness(16, 14, 16, 14) : new Thickness(20, 18, 20, 18), 16);
        card.Margin = new Thickness(compact ? 30 : 36, 10, 0, 4);
        return card;
    }

    UIElement Plan(PlanItem plan)
    {
        var stack = new StackPanel();
        stack.Children.Add(k.Text(plan.Summary, compact ? 14.5 : 16, weight: FontWeights.SemiBold, wrap: true));

        var steps = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            bool running = plan.State == PlanState.Running && plan.Current == i;
            bool past = plan.State == PlanState.Done || (plan.State is PlanState.Running or PlanState.Failed && i < plan.Current);
            var number = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = DashKit.B(running ? P.Accent : past ? P.Selected : P.Hover),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0),
                Child = past ? k.Icon("\uE73E", 11, P.Text) : new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontFamily = DashKit.Sans,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = DashKit.B(running ? P.AccentText : P.Muted),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);
            var what = new StackPanel();
            what.Children.Add(k.Text(step.Action.Label.TrimEnd('…'), compact ? 13.5 : 14.5, weight: FontWeights.SemiBold));
            var files = step.UsePrevious
                ? $"What step {i} made"
                : step.Files.Count <= 3
                    ? string.Join(", ", step.Files.Select(f => Path.GetFileName(f.TrimEnd('\\'))))
                    : $"{string.Join(", ", step.Files.Take(2).Select(f => Path.GetFileName(f.TrimEnd('\\'))))} and {step.Files.Count - 2} more";
            var detail = k.Text(files, compact ? 12.5 : 13, P.Muted, wrap: true);
            detail.Margin = new Thickness(0, 2, 0, 0);
            if (!step.UsePrevious) detail.ToolTip = string.Join("\n", step.Files.Take(40));
            what.Children.Add(detail);
            row.Children.Add(what);
            steps.Children.Add(row);
        }
        if (plan.Detail is { } note)
        {
            var line = k.Text(note, compact ? 12.5 : 13, P.Muted, wrap: true);
            line.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(line);
        }
        else if (plan.Steps.Count > 0) stack.Children.Add(steps);
        // Attached files, one per line, by name.
        if (plan.Attached.Count > 0)
        {
            var files = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var file in plan.Attached)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4), ToolTip = file };
                var clip = k.Icon("\uE723", 12, P.Muted);
                clip.Margin = new Thickness(0, 1, 8, 0);
                DockPanel.SetDock(clip, Dock.Left);
                row.Children.Add(clip);
                var name = k.Text(Path.GetFileName(file), compact ? 12.5 : 13, P.Text);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(name);
                files.Children.Add(row);
            }
            stack.Children.Add(files);
        }
        // The long version, folded away.
        if (plan.More is { } more)
        {
            var full = k.Text(more, compact ? 12 : 12.5, P.Muted, wrap: true);
            full.Margin = new Thickness(0, 6, 0, 0);
            full.Visibility = Visibility.Collapsed;
            var toggle = k.Text("Show the steps", compact ? 12 : 12.5, P.Text, weight: FontWeights.SemiBold);
            toggle.Margin = new Thickness(0, 8, 0, 0);
            toggle.Cursor = System.Windows.Input.Cursors.Hand;
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                bool show = full.Visibility != Visibility.Visible;
                full.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                toggle.Text = show ? "Hide the steps" : "Show the steps";
            };
            stack.Children.Add(toggle);
            stack.Children.Add(full);
        }
        if (plan.Fine is { } fine)
        {
            var small = k.Text(fine, compact ? 11.5 : 12, P.Faint, wrap: true);
            small.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(small);
        }
        // Someone new: their number or email, typed (or said) right here.
        Action<bool>? addressReady = null;
        if (plan is { Ask: { } ask, State: PlanState.Waiting }) stack.Children.Add(AddressBox(plan, ask, ready => addressReady?.Invoke(ready)));
        var footer = PlanFooter(plan, ready => addressReady = ready);
        if (footer is FrameworkElement spaced && plan.Steps.Count == 0) spaced.Margin = new Thickness(spaced.Margin.Left, Math.Max(spaced.Margin.Top, 14), spaced.Margin.Right, spaced.Margin.Bottom);
        stack.Children.Add(footer);

        var card = k.Card(stack, compact ? new Thickness(16, 14, 16, 14) : new Thickness(20, 18, 20, 18), 16);
        card.Margin = new Thickness(compact ? 30 : 36, 10, 0, 4);
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (plan.State == PlanState.Waiting) card.BorderBrush = DashKit.B(P.AccentEdge);
        return card;
    }

    /// <summary>"Tausif's WhatsApp number" with a box (+91 already in it), and what's still missing under it.</summary>
    UIElement AddressBox(PlanItem plan, AddressAsk ask, Action<bool> ready)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var label = k.Text(ask.Kind == AddressKind.Phone ? $"{ask.Name}'s WhatsApp number" : $"{ask.Name}'s email", compact ? 12.5 : 13, P.Muted, FontWeights.SemiBold);
        label.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(label);
        var box = new TextBox
        {
            Text = plan.Address,
            FontFamily = DashKit.Sans,
            FontSize = compact ? 14.5 : 15.5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            VerticalContentAlignment = VerticalAlignment.Center,
            InputScope = new InputScope { Names = { new InputScopeName(ask.Kind == AddressKind.Phone ? InputScopeNameValue.TelephoneNumber : InputScopeNameValue.EmailSmtpAddress) } },
        };
        var field = new Border
        {
            Background = DashKit.B(P.Hover),
            BorderBrush = DashKit.B(P.AccentEdge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 7, 12, 8),
            Child = box,
        };
        stack.Children.Add(field);
        var hint = k.Text("", compact ? 11.5 : 12, P.Faint, wrap: true);
        hint.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(hint);
        void Check()
        {
            // Kept on the card, not the chat: the chat redraws itself, and this survives it.
            plan.Address = box.Text;
            var problem = Addresses.Problem(ask.Kind, box.Text);
            hint.Text = problem ?? "I'll remember it, so next time just say the name.";
            hint.Foreground = DashKit.B(problem == null ? P.Muted : P.Faint);
            ready(problem == null);
        }
        Check();
        box.TextChanged += (_, _) => Check();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (plan.AddressReady) _ = chat.Confirm(plan);
        };
        // Ready to type straight away, after what's already there.
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        };
        return stack;
    }

    UIElement PlanFooter(PlanItem plan, Action<Action<bool>>? addressReady = null)
    {
        var footer = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        switch (plan.State)
        {
            case PlanState.Waiting when plan.SendsAt is { } sendsAt:
            {
                // Someone already written to: it goes by itself, unless stopped.
                var left = k.Text("", 13.5, P.Text, FontWeights.SemiBold);
                left.VerticalAlignment = VerticalAlignment.Center;
                left.Margin = new Thickness(0, 0, 12, 0);
                void Tick() => left.Text = $"Sending in {Math.Max(1, (int)Math.Ceiling((sendsAt - DateTime.Now).TotalSeconds))}… say \"stop\" to cancel";
                Tick();
                var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                clock.Tick += (_, _) => { if (plan.SendsAt is { } at) { sendsAt = at; Tick(); } };
                left.Loaded += (_, _) => clock.Start();
                left.Unloaded += (_, _) => clock.Stop();
                var now = k.Button("Send now", () => _ = chat.Confirm(plan), DashKit.Pill.Accent, size: 13.5);
                now.Margin = new Thickness(0, 0, 8, 0);
                footer.Children.Add(left);
                footer.Children.Add(now);
                footer.Children.Add(k.Button("Stop", () => chat.Decline(plan), DashKit.Pill.Quiet, size: 13.5));
                break;
            }
            case PlanState.Waiting:
            {
                var go = k.Button(plan.Ask != null ? "Save and send" : "Do it", () => _ = chat.Confirm(plan), DashKit.Pill.Accent, size: 13.5);
                go.Margin = new Thickness(0, 0, 8, 0);
                if (plan.Ask != null)
                {
                    // Greyed out until the box holds a number (or email) that will do.
                    void Ready(bool ready)
                    {
                        go.IsEnabled = ready;
                        go.Opacity = ready ? 1 : 0.45;
                    }
                    Ready(plan.AddressReady);
                    addressReady?.Invoke(Ready);
                }
                footer.Children.Add(go);
                footer.Children.Add(k.Button("Not now", () => chat.Decline(plan), DashKit.Pill.Quiet, size: 13.5));
                break;
            }
            case PlanState.Running:
                footer.Children.Add(Pulse(k.Text(plan.Steps.Count == 1 ? "Working on it…" : $"Working on step {plan.Current + 1} of {plan.Steps.Count}…", 13.5, P.Muted)));
                break;
            case PlanState.Done:
            {
                var done = k.Text(plan.Outcome ?? "Done.", 13.5, P.Text, FontWeights.SemiBold, wrap: true);
                done.Margin = new Thickness(0, 0, 0, plan.Outputs.Count > 0 ? 10 : 0);
                var stack = new StackPanel();
                stack.Children.Add(done);
                if (plan.Outputs.Count > 0)
                {
                    var buttons = new WrapPanel();
                    if (plan.Outputs.Count == 1 && File.Exists(plan.Outputs[0]))
                    {
                        var openButton = k.Button("Open", () => open(plan.Outputs[0]), DashKit.Pill.Quiet, "\uE8A7", 13);
                        openButton.Margin = new Thickness(0, 0, 8, 0);
                        buttons.Children.Add(openButton);
                    }
                    var reveal = k.Button("Show in folder", () => Shell.ExplorerReveal.Show(plan.Outputs), DashKit.Pill.Quiet, "\uE838", 13);
                    buttons.Children.Add(reveal);
                    if (plan.Undo != null && !plan.Undone)
                    {
                        reveal.Margin = new Thickness(0, 0, 8, 0);
                        buttons.Children.Add(k.Button("Undo", () => _ = chat.UndoPlan(plan), DashKit.Pill.Quiet, "\uE7A7", 13));
                    }
                    stack.Children.Add(buttons);
                }
                footer.Children.Add(stack);
                break;
            }
            case PlanState.Failed:
                footer.Children.Add(k.Text(plan.Outcome ?? "That didn't work.", 13.5, P.Danger, wrap: true));
                break;
            case PlanState.Declined:
                footer.Children.Add(k.Text("Not run.", 13.5, P.Faint));
                break;
            case PlanState.Skipped:
                footer.Children.Add(k.Text("Not run: you moved on.", 13.5, P.Faint));
                break;
        }
        return footer;
    }

    /// <summary>A picture, fitted inside a box, with rounded corners.</summary>
    public static FrameworkElement Thumbnail(byte[] png, double maxWidth, double maxHeight)
    {
        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.EndInit();
        bitmap.Freeze();
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = maxWidth, MaxHeight = maxHeight };
        return new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = image, Background = Brushes.Transparent };
    }

    /// <summary>
    /// The paw's picture waiting to be asked about, with the questions people ask most. Clicking
    /// one asks it; the box asks anything else; the x lets it go.
    /// </summary>
    public UIElement PictureStrip(byte[] png, Action<string> ask, Action forget)
    {
        var stack = new StackPanel { Margin = new Thickness(compact ? 0 : 36, compact ? 6 : 14, 0, 6) };
        var top = new DockPanel();
        var close = k.IconButton("\uE711", forget, "Don't ask about it", 11, P.Muted, 28);
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        var thumb = Thumbnail(png, compact ? 300 : 420, compact ? 140 : 220);
        thumb.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(thumb);
        stack.Children.Add(top);
        var label = k.Text("Ask about this:", compact ? 12.5 : 13, P.Muted);
        label.Margin = new Thickness(0, 8, 0, 6);
        stack.Children.Add(label);
        var chips = new WrapPanel();
        foreach (var question in new[] { "What is this?", "Copy the text", "Make it a table", "Explain it simply" })
        {
            var chip = k.Button(question, () => ask(question), DashKit.Pill.Quiet, size: compact ? 12.5 : 13);
            chip.Padding = new Thickness(12, 6, 12, 7);
            chip.Margin = new Thickness(0, 0, 6, 6);
            chips.Children.Add(chip);
        }
        stack.Children.Add(chips);
        return stack;
    }

    /// <summary>A table read off the screen: a preview, Copy (cells for Excel or Sheets) and Save as CSV.</summary>
    UIElement Table(TableItem table)
    {
        var stack = new StackPanel();
        stack.Children.Add(k.Text(table.Title, compact ? 14 : 15.5, weight: FontWeights.SemiBold));
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        int columns = Math.Min(6, table.Rows.Max(r => r.Length));
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int shown = Math.Min(table.Rows.Count, 8);
        for (int r = 0; r < shown; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < columns && c < table.Rows[r].Length; c++)
            {
                var cell = k.Text(table.Rows[r][c], compact ? 12 : 12.5, r == 0 ? P.Text : P.Muted, r == 0 ? FontWeights.SemiBold : FontWeights.Normal);
                cell.Margin = new Thickness(0, 2, 16, 2);
                cell.MaxWidth = 180;
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        stack.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (table.Rows.Count > shown)
            stack.Children.Add(k.Text($"and {table.Rows.Count - shown} more rows", 12, P.Faint));

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        TextBlock? note = null;
        var copy = k.Button("Copy", () =>
        {
            bool ok = TryCopy(CatChat.TableText(table));
            note!.Text = ok ? "Copied: paste into Excel or Sheets and it lands in cells." : "The clipboard is busy. Try again.";
        }, DashKit.Pill.Accent, "\uE8C8", 13);
        copy.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(copy);
        if (table.SavedAs is { } saved)
            buttons.Children.Add(k.Button("Open", () => open(saved), DashKit.Pill.Quiet, "\uE8A7", 13));
        else
            buttons.Children.Add(k.Button("Save as CSV", () => chat.SaveTable(table), DashKit.Pill.Quiet, "\uE74E", 13));
        stack.Children.Add(buttons);
        note = k.Text(table.SavedAs is { } file ? $"Saved as {Path.GetFileName(file)}" : "", 12.5, P.Muted, wrap: true);
        note.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(note);

        var card = k.Card(stack, compact ? new Thickness(16, 14, 16, 14) : new Thickness(20, 18, 20, 18), 16);
        card.Margin = new Thickness(compact ? 30 : 36, 10, 0, 4);
        return card;
    }

    static bool TryCopy(string text)
    {
        try
        {
            Core.ClipboardTools.Retry(() => Clipboard.SetText(text));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>"Mochi is thinking…", with a Stop link.</summary>
    public UIElement Thinking()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, compact ? 10 : 16, 0, 2) };
        var head = Head(compact ? 20 : 24);
        head.Margin = new Thickness(0, 0, compact ? 10 : 12, 0);
        row.Children.Add(head);
        var text = k.Text($"{catName} is thinking…", Size, P.Muted);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(Pulse(text));
        var stop = k.Text("Stop", 13, P.Faint);
        stop.Cursor = Cursors.Hand;
        stop.Margin = new Thickness(14, 2, 0, 0);
        stop.VerticalAlignment = VerticalAlignment.Center;
        stop.TextDecorations = TextDecorations.Underline;
        stop.MouseLeftButtonUp += (_, e) => { e.Handled = true; chat.Stop(); };
        row.Children.Add(stop);
        return row;
    }

    public Image Head(double size)
    {
        var image = new Image { Source = catHead, Width = size, Height = size };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    /// <summary>A gentle fade in and out while something's in progress (still for reduced motion).</summary>
    static UIElement Pulse(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return element;
        var fade = new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        return element;
    }

    /// <summary>Text you can select and copy, styled like the rest.</summary>
    TextBox Selectable(string text, double size, Color colour) => new()
    {
        Text = text,
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = DashKit.Sans,
        FontSize = size,
        Foreground = DashKit.B(colour),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        Cursor = Cursors.IBeam,
        SelectionBrush = DashKit.B(P.Accent),
    };
}

/// <summary>
/// The Ask page: talk to the cat about your files. A chat down the middle, the box to type in
/// pinned at the bottom. Kept alive between visits so a half-typed message stays put.
/// </summary>
public sealed class AskView
{
    readonly DashKit _k;
    readonly DashboardHost _host;
    readonly CatChat _chat;
    readonly StackPanel _transcript = new();
    readonly ScrollViewer _scroll;
    readonly ChatComposer _input;
    readonly TextBlock _cost;
    readonly Border _wakeSetup;
    readonly ContentControl _bottom = new();
    readonly FrameworkElement _composer;

    DashPalette P => _k.P;
    string CatName => _host.CatName();

    public FrameworkElement Root { get; }

    public string Draft { get => _input.Box.Text; set => _input.Box.Text = value; }

    static readonly string[] Ideas =
    [
        "Merge the PDFs I downloaded today",
        "Turn my newest screenshot into a JPG",
        "Compress the videos on my desktop",
        "Show me the photos from this week",
    ];

    public AskView(DashKit k, DashboardHost host, CatChat chat, Style scrollBar)
    {
        _k = k;
        _host = host;
        _chat = chat;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- Header ----
        var head = new DockPanel { Margin = new Thickness(52, 40, 46, 4) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        _cost = k.Text("", 13, P.Faint);
        _cost.VerticalAlignment = VerticalAlignment.Center;
        _cost.Margin = new Thickness(0, 0, 10, 0);
        tools.Children.Add(_cost);
        _wakeSetup = k.Button($"Set up \"Hey {CatName}\"", () => host.TeachName?.Invoke(), DashKit.Pill.Quiet, "\uE720", 13);
        _wakeSetup.Padding = new Thickness(14, 7, 14, 8);
        _wakeSetup.Margin = new Thickness(0, 0, 10, 0);
        _wakeSetup.ToolTip = "Wake the cat by saying its name, no keys needed";
        tools.Children.Add(_wakeSetup);
        tools.Children.Add(k.IconButton("\uE710", () => { _chat.Reset(); FocusBox(); }, "New chat", 14, P.Muted));
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        var titles = new StackPanel();
        titles.Children.Add(k.Text($"Ask {CatName}", 30, weight: FontWeights.SemiBold, font: DashKit.Display));
        var sub = k.Text($"Say what you want done with your files or your PC, or ask about anything on screen ({host.Settings.PawHotkey} to point at it). {CatName} shows you the plan and waits for your OK.", 15, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 0);
        titles.Children.Add(sub);
        head.Children.Add(titles);
        grid.Children.Add(head);

        // ---- Transcript ----
        _transcript.Margin = new Thickness(52, 10, 52, 24);
        _transcript.MaxWidth = 860;
        _transcript.HorizontalAlignment = HorizontalAlignment.Stretch;
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _transcript };
        _scroll.Resources.Add(typeof(ScrollBar), scrollBar);
        Grid.SetRow(_scroll, 1);
        grid.Children.Add(_scroll);

        // ---- Composer ----
        _input = new ChatComposer(k, CatName, host.Voice, text => { if (!_chat.Busy) _ = _chat.Send(text); }, compact: false);
        _composer = Composer();
        _bottom.Margin = new Thickness(52, 0, 52, 26);
        _bottom.MaxWidth = 860;
        Grid.SetRow(_bottom, 2);
        grid.Children.Add(_bottom);

        Root = grid;
        _chat.Changed += Refresh;
        Refresh();
    }

    /// <summary>Stops listening to the chat (the dashboard made a new view, e.g. after a theme change).</summary>
    public void Detach()
    {
        _chat.Changed -= Refresh;
        _input.Detach();
    }

    public void FocusBox() => Root.Dispatcher.BeginInvoke(() => { if (_input.Box.IsVisible) _input.Box.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);

    FrameworkElement Composer()
    {
        var stack = new StackPanel();
        stack.Children.Add(_input.Root);
        var talk = _host.Voice != null ? $" Press the mic or {_host.Settings.TalkHotkey} to talk; your voice is turned into text on this PC." : "";
        var note = _k.Text($"Enter sends, Shift+Enter adds a line.{talk} Uses {CatChat.ModelName} with your API key: only what a request needs goes to Anthropic: your words, file names, and a document, email or picture when you ask about it (card numbers and passwords hidden first).", 12, P.Faint, wrap: true);
        note.Margin = new Thickness(18, 8, 18, 0);
        stack.Children.Add(note);
        return stack;
    }

    public void Refresh()
    {
        _cost.Text = _chat.Cost > 0 ? $"This chat: {Money(_chat.Cost)}" : "";
        bool wakeOn = _host.Settings.WakeWord && _host.Settings.WakeVariants.Count > 0;
        _wakeSetup.Visibility = wakeOn || _host.TeachName == null || !ClaudeKey.Exists ? Visibility.Collapsed : Visibility.Visible;
        _transcript.Children.Clear();
        var cards = new ChatCards(_k, _chat, CatName, CatSprites.Head(CatSprites.Skin(_host.Settings.Cat)), _host.OpenFile, _host.OpenSettings);

        // Switched off, or no key yet: the card that fixes it sits where the chat would start.
        bool off = !_host.Settings.AiEnabled;
        bool noKey = !ClaudeKey.Exists;
        _bottom.Content = off || noKey ? null : _composer;
        if (off || noKey)
        {
            var setup = off ? SwitchedOff() : KeySetup();
            ((FrameworkElement)setup).Margin = new Thickness(0, 18, 0, 0);
            _transcript.Children.Add(setup);
        }
        else if (_chat.Items.Count == 0) _transcript.Children.Add(Ideas_());
        foreach (var item in _chat.Items) _transcript.Children.Add(cards.Render(item));
        if (_chat.Busy) _transcript.Children.Add(cards.Thinking());
        else if (_chat.Picture is { } picture)
            _transcript.Children.Add(cards.PictureStrip(picture, question => _ = _chat.Send(question), _chat.ForgetPicture));
        if (!_chat.Busy && _chat.ContextLabel is { } about) _transcript.Children.Add(AboutChip(about));
        _scroll.Dispatcher.BeginInvoke(() => _scroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>"Asking about the meeting …": what's attached to the next message, with a way to drop it.</summary>
    UIElement AboutChip(string about)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = _k.Icon("\uE723", 13, P.Muted);
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        var text = _k.Text($"Asking about {about}", 13.5, P.Text);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        var close = _k.IconButton("\uE711", _chat.ForgetContext, "Don't attach it", 10, P.Muted, 26);
        close.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(close);
        var chip = new Border
        {
            Background = DashKit.B(P.Hover),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(36, 14, 0, 4),
            Child = row,
        };
        return chip;
    }

    UIElement Ideas_()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        var cards = new ChatCards(_k, _chat, CatName, CatSprites.Head(CatSprites.Skin(_host.Settings.Cat)), _host.OpenFile, _host.OpenSettings);
        var hello = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var head = cards.Head(24);
        head.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(head, Dock.Left);
        hello.Children.Add(head);
        hello.Children.Add(_k.Text($"Hi! Tell me what you need and I'll find the files. Select files in File Explorer first and I can work on \"these\". You can also press {_host.Settings.AskHotkey} anywhere to ask me, {_host.Settings.TalkHotkey} to talk, or teach me my name and just say \"Hey {CatName}\".", 15.5, wrap: true));
        stack.Children.Add(hello);
        var ideas = new WrapPanel { Margin = new Thickness(36, 0, 0, 0) };
        foreach (var idea in Ideas)
        {
            var chip = _k.Button(idea, () => { _input.Box.Text = idea; _input.Box.CaretIndex = idea.Length; _input.Box.Focus(); }, DashKit.Pill.Quiet, size: 13);
            chip.Padding = new Thickness(14, 7, 14, 8);
            chip.Margin = new Thickness(0, 0, 8, 8);
            ideas.Children.Add(chip);
        }
        stack.Children.Add(ideas);
        return stack;
    }

    UIElement SwitchedOff()
    {
        var stack = new StackPanel();
        stack.Children.Add(_k.Text("Ask the cat is switched off", 16, weight: FontWeights.SemiBold));
        var sub = _k.Text("Switch it on to talk to your cat. Nothing is sent anywhere while it's off.", 14, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 4, 0, 14);
        stack.Children.Add(sub);
        stack.Children.Add(_k.Button("Switch on", () => { _host.Settings.AiEnabled = true; _host.SaveSettings(); Refresh(); }, DashKit.Pill.Accent));
        return _k.Card(stack, new Thickness(24, 20, 24, 22), 20);
    }

    /// <summary>First visit: where to get a key and a box to paste it in. Checked with Claude before it's saved.</summary>
    UIElement KeySetup()
    {
        var stack = new StackPanel();
        stack.Children.Add(_k.Text($"{CatName} needs a Claude API key", 17, weight: FontWeights.SemiBold));
        var sub = new TextBlock { FontFamily = DashKit.Sans, FontSize = 14, Foreground = DashKit.B(P.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16) };
        sub.Inlines.Add("Create one at ");
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("console.anthropic.com")) { Foreground = DashKit.B(P.Text) };
        link.Click += (_, _) => Process.Start(new ProcessStartInfo("https://console.anthropic.com/settings/keys") { UseShellExecute = true });
        sub.Inlines.Add(link);
        sub.Inlines.Add(" and paste it here. It's kept in Windows Credential Manager, not in a file, and only used to talk to Claude. A typical request costs a small fraction of a cent.");
        stack.Children.Add(sub);

        var key = new PasswordBox
        {
            FontFamily = DashKit.Sans,
            FontSize = 15,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = _k.Text("sk-ant-…", 15, P.Faint);
        hint.IsHitTestVisible = false;
        key.PasswordChanged += (_, _) => hint.Visibility = key.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid { Children = { hint, key } };
        var fieldCard = new Border
        {
            Background = DashKit.B(P.Panel),
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 9, 14, 9),
            Child = field,
        };
        var status = _k.Text("", 13.5, P.Muted, wrap: true);
        status.Margin = new Thickness(0, 10, 0, 0);
        status.Visibility = Visibility.Collapsed;
        void Say(string text, bool bad)
        {
            status.Text = text;
            status.Foreground = DashKit.B(bad ? P.Danger : P.Muted);
            status.Visibility = Visibility.Visible;
        }
        Border? save = null;
        async void Save()
        {
            var value = key.Password.Trim();
            if (value.Length < 20) { Say("That doesn't look like a whole key. It starts with sk-ant-.", true); return; }
            Say("Checking the key with Claude…", false);
            save!.IsEnabled = false;
            var problem = await CatChat.TestKey(value);
            save.IsEnabled = true;
            if (problem != null)
            {
                Say(problem, true);
                return;
            }
            try
            {
                ClaudeKey.Save(value);
            }
            catch (Exception ex)
            {
                Say(ex.Message, true);
                return;
            }
            key.Clear();
            Refresh();
            FocusBox();
        }
        key.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Save(); } };
        save = _k.Button("Save key", Save, DashKit.Pill.Accent);
        save.Margin = new Thickness(10, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(save, Dock.Right);
        row.Children.Add(save);
        row.Children.Add(fieldCard);
        stack.Children.Add(row);
        stack.Children.Add(status);
        return _k.Card(stack, new Thickness(24, 20, 24, 22), 20);
    }

    public static string Money(double usd) => usd < 0.01 ? $"${usd:0.0000}" : $"${usd:0.00}";
}
