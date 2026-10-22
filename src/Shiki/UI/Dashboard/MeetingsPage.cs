using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Core;
using Shiki.Core.Meetings;

namespace Shiki.UI.Dashboard;

/// <summary>
/// Meetings the cat took notes in: a list (searchable), and each one's summary, decisions, open
/// questions, tasks, follow-up email and transcript, with speakers you can name.
/// </summary>
public sealed class MeetingsPage(DashKit k, DashboardHost host, string? openId, Action<string?> open, Action rebuild)
{
    DashPalette P => k.P;
    static string _search = "";

    public UIElement Build() => openId != null && MeetingStore.Load(openId) is { } meeting ? Detail(meeting) : List();

    // ---- The list ----

    UIElement List()
    {
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 48), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
        var head = new DockPanel();
        bool running = host.NotesRunning?.Invoke() ?? false;
        if (host.ToggleNotes != null)
        {
            var notes = k.Button(running ? "Stop notes" : "Take notes", () => { host.ToggleNotes(); rebuild(); }, running ? DashKit.Pill.Quiet : DashKit.Pill.Accent, running ? "\uE71A" : "\uE720", 13.5);
            notes.VerticalAlignment = VerticalAlignment.Top;
            notes.Margin = new Thickness(0, 6, 0, 0);
            DockPanel.SetDock(notes, Dock.Right);
            head.Children.Add(notes);
        }
        head.Children.Add(k.Text("Meetings", 30, weight: FontWeights.SemiBold, font: DashKit.Display));
        page.Children.Add(head);
        var sub = k.Text($"Say \"Hey {host.CatName()}, take notes\" when a call starts, or press {host.Settings.NotesHotkey}. At the end you get a summary, decisions, your tasks and a follow-up email.", 15, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 22);
        page.Children.Add(sub);

        var meetings = MeetingStore.All();
        if (meetings.Count == 0)
        {
            var empty = k.Text("No meetings yet. The next call you take notes in shows up here.", 15, P.Muted, wrap: true);
            page.Children.Add(k.Card(empty, new Thickness(28, 26, 28, 26), 18));
            return page;
        }

        var listHost = new ContentControl();
        page.Children.Add(SearchBox(text => { _search = text; listHost.Content = Cards(meetings); }));
        listHost.Content = Cards(meetings);
        page.Children.Add(listHost);
        return page;
    }

    UIElement SearchBox(Action<string> changed)
    {
        var box = new TextBox
        {
            Text = _search,
            FontFamily = DashKit.Sans,
            FontSize = 15,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = k.Text("Search what was said, names, tasks", 15, P.Faint);
        hint.IsHitTestVisible = false;
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            changed(box.Text);
        };
        var row = new DockPanel();
        var icon = k.Icon("\uE721", 15, P.Faint);
        icon.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(new Grid { Children = { hint, box } });
        var card = k.Card(row, new Thickness(18, 12, 18, 12), 14);
        card.Margin = new Thickness(0, 0, 0, 18);
        return card;
    }

    UIElement Cards(IReadOnlyList<Meeting> meetings)
    {
        var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = meetings.Where(m => words.All(w =>
            m.Title.Contains(w, StringComparison.OrdinalIgnoreCase)
            || (m.Summary?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)
            || m.Tasks.Any(t => t.Text.Contains(w, StringComparison.OrdinalIgnoreCase))
            || m.Names.Values.Any(n => n.Contains(w, StringComparison.OrdinalIgnoreCase))
            || m.Lines.Any(l => l.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))).ToList();
        var stack = new StackPanel();
        if (shown.Count == 0) stack.Children.Add(k.Text("Nothing matches.", 15, P.Muted));
        foreach (var meeting in shown)
        {
            var text = new StackPanel();
            var title = k.Text(meeting.Title, 16.5, weight: FontWeights.SemiBold);
            text.Children.Add(title);
            var meta = new List<string> { meeting.Started.ToString("ddd d MMM, h:mm tt").Replace("AM", "am").Replace("PM", "pm"), meeting.Length };
            if (meeting.App is { } app) meta.Add(app);
            if (meeting.Tasks.Count > 0) meta.Add(meeting.Tasks.Count == 1 ? "1 task" : $"{meeting.Tasks.Count} tasks");
            var line = k.Text(string.Join(", ", meta), 13, P.Muted);
            line.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(line);
            if (meeting.Summary is { Length: > 0 } summary)
            {
                var gist = k.Text(summary.Length > 220 ? summary[..219] + "…" : summary, 14, P.Text, wrap: true);
                gist.Margin = new Thickness(0, 10, 0, 0);
                text.Children.Add(gist);
            }
            else if (meeting.NotesProblem != null)
            {
                var problem = k.Text("No notes yet: open it to try again.", 13.5, P.Danger);
                problem.Margin = new Thickness(0, 8, 0, 0);
                text.Children.Add(problem);
            }
            var card = k.Card(text, new Thickness(22, 18, 22, 18), 16);
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, e) => { e.Handled = true; open(meeting.Id); };
            stack.Children.Add(card);
        }
        return stack;
    }

    // ---- One meeting ----

    UIElement Detail(Meeting meeting)
    {
        var page = new StackPanel { Margin = new Thickness(40, 30, 52, 48), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var back = k.IconButton("\uE72B", () => open(null), "All meetings", 14, P.Muted);
        back.VerticalAlignment = VerticalAlignment.Top;
        back.Margin = new Thickness(0, 4, 8, 0);
        DockPanel.SetDock(back, Dock.Left);
        top.Children.Add(back);
        top.Children.Add(k.Text(meeting.Title, 28, weight: FontWeights.SemiBold, font: DashKit.Display, wrap: true));
        page.Children.Add(top);
        var meta = new List<string> { meeting.Started.ToString("dddd d MMMM, h:mm tt").Replace("AM", "am").Replace("PM", "pm"), meeting.Length };
        if (meeting.App is { } app) meta.Add($"in {app}");
        var metaText = k.Text(string.Join(", ", meta), 14, P.Muted);
        metaText.Margin = new Thickness(44, 0, 0, 16);
        page.Children.Add(metaText);

        var buttons = new WrapPanel { Margin = new Thickness(44, 0, 0, 18) };
        void Add(Border b) { b.Margin = new Thickness(0, 0, 8, 8); buttons.Children.Add(b); }
        if (host.AskAboutMeeting != null) Add(k.Button($"Ask {host.CatName()} about it", () => host.AskAboutMeeting(meeting), DashKit.Pill.Accent, "\uE8BD", 13));
        Add(k.Button("Copy notes", () => Copy(Notes(meeting)), DashKit.Pill.Quiet, "\uE8C8", 13));
        Add(k.Button("Copy transcript", () => Copy(meeting.Transcript()), DashKit.Pill.Quiet, "\uE8C8", 13));
        if (meeting.Audio is { } audio && File.Exists(audio))
            Add(k.Button("Play the recording", () => host.OpenFile(audio), DashKit.Pill.Quiet, "\uE768", 13));
        Add(k.Button("Forget this meeting", () =>
        {
            MeetingStore.Remove(meeting);
            open(null);
        }, DashKit.Pill.Quiet, "\uE74D", 13));
        page.Children.Add(buttons);

        var body = new StackPanel { Margin = new Thickness(44, 0, 0, 0) };
        page.Children.Add(body);

        if (meeting.NotesProblem is { } problem)
        {
            var stack = new StackPanel();
            stack.Children.Add(k.Text(problem, 14.5, P.Danger, wrap: true));
            if (host.RewriteNotes != null)
            {
                var again = k.Button("Write the notes again", () => host.RewriteNotes(meeting), DashKit.Pill.Accent, size: 13);
                again.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(again);
            }
            body.Children.Add(k.Card(stack, new Thickness(22, 18, 22, 18), 16));
        }

        if (meeting.Summary is { Length: > 0 } summary)
        {
            Head(body, "Summary");
            body.Children.Add(k.Card(k.Text(summary, 15, P.Text, wrap: true), new Thickness(22, 18, 22, 18), 16));
        }
        Bullets(body, "Decisions", meeting.Decisions);
        Bullets(body, "Open questions", meeting.Questions);
        if (meeting.Tasks.Count > 0)
        {
            Head(body, "Tasks");
            var rows = new StackPanel();
            foreach (var task in meeting.Tasks)
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                bool mine = task.Owner.Equals("me", StringComparison.OrdinalIgnoreCase);
                bool listed = Todos.All.Any(t => t.MeetingId == meeting.Id && t.Text == task.Text);
                if (!listed)
                {
                    var add = k.Button("Add to my tasks", () => { Todos.Add(task.Text, task.Due, task.Due?.TimeOfDay != TimeSpan.Zero, meeting.Title, meeting.Id, mine ? null : task.Owner); rebuild(); }, DashKit.Pill.Quiet, size: 12.5);
                    add.Padding = new Thickness(12, 5, 12, 6);
                    add.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(add, Dock.Right);
                    row.Children.Add(add);
                }
                else
                {
                    var inList = k.Text("In your tasks", 12.5, P.Faint);
                    inList.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(inList, Dock.Right);
                    row.Children.Add(inList);
                }
                var text = new StackPanel();
                text.Children.Add(k.Text(task.Text, 15, wrap: true));
                var who = new List<string> { mine ? "you" : task.Owner };
                if (task.Due is { } due) who.Add($"due {due:ddd d MMM}{(due.TimeOfDay != TimeSpan.Zero ? $", {due:h:mm tt}" : "")}");
                if (task.Quote is { Length: > 0 } quote) who.Add($"\"{quote}\"");
                var detail = k.Text(string.Join(", ", who), 13, P.Muted, wrap: true);
                detail.Margin = new Thickness(0, 2, 12, 0);
                text.Children.Add(detail);
                row.Children.Add(text);
                rows.Children.Add(row);
            }
            body.Children.Add(k.Card(rows, new Thickness(22, 12, 18, 12), 16));
        }
        if (meeting.EmailBody is { Length: > 0 } email)
        {
            Head(body, "Follow-up email");
            var stack = new StackPanel();
            if (meeting.EmailSubject is { Length: > 0 } subject)
                stack.Children.Add(k.Text(subject, 15, weight: FontWeights.SemiBold, wrap: true));
            var text = k.Text(email, 14.5, P.Text, wrap: true);
            text.Margin = new Thickness(0, 8, 0, 14);
            stack.Children.Add(text);
            stack.Children.Add(k.Button("Copy email", () => Copy($"{meeting.EmailSubject}\r\n\r\n{email}"), DashKit.Pill.Quiet, "\uE8C8", 13));
            body.Children.Add(k.Card(stack, new Thickness(22, 18, 22, 18), 16));
        }
        Speakers(body, meeting);
        Transcript(body, meeting);
        return page;
    }

    void Head(StackPanel body, string text)
    {
        var label = k.Label(text);
        label.Margin = new Thickness(2, 22, 0, 10);
        body.Children.Add(label);
    }

    void Bullets(StackPanel body, string title, List<string> items)
    {
        if (items.Count == 0) return;
        Head(body, title);
        var stack = new StackPanel();
        foreach (var item in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var dot = k.Text("•", 15, P.AccentEdge);
            dot.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(k.Text(item, 15, wrap: true));
            stack.Children.Add(row);
        }
        body.Children.Add(k.Card(stack, new Thickness(22, 14, 22, 14), 16));
    }

    /// <summary>Name the voices: "Speaker 2" becomes Rahul everywhere in this meeting.</summary>
    void Speakers(StackPanel body, Meeting meeting)
    {
        var speakers = meeting.Lines.Select(l => l.Speaker).Where(s => s != "You").Distinct().OrderBy(s => s).ToList();
        if (speakers.Count == 0) return;
        Head(body, "Who spoke");
        var stack = new StackPanel();
        foreach (var speaker in speakers)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var label = k.Text(speaker, 14, P.Muted);
            label.Width = 110;
            label.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            var box = new TextBox
            {
                Text = meeting.Names.GetValueOrDefault(speaker, ""),
                FontFamily = DashKit.Sans,
                FontSize = 14,
                Background = DashKit.B(P.Panel),
                BorderBrush = DashKit.B(P.Line),
                BorderThickness = new Thickness(1),
                Foreground = DashKit.B(P.Text),
                CaretBrush = DashKit.B(P.Text),
                Padding = new Thickness(8, 5, 8, 5),
                MaxWidth = 260,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var said = meeting.Lines.FirstOrDefault(l => l.Speaker == speaker)?.Text ?? "";
            box.ToolTip = $"First said: \"{(said.Length > 80 ? said[..79] + "…" : said)}\"";
            box.LostKeyboardFocus += (_, _) =>
            {
                var name = box.Text.Trim();
                if (name == meeting.Names.GetValueOrDefault(speaker, "")) return;
                if (name.Length == 0) meeting.Names.Remove(speaker); else meeting.Names[speaker] = name;
                MeetingStore.Save(meeting);
            };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            row.Children.Add(box);
            stack.Children.Add(row);
        }
        var note = k.Text("Type a name and it's used in the transcript and when you ask about the meeting.", 12.5, P.Faint, wrap: true);
        note.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(note);
        body.Children.Add(k.Card(stack, new Thickness(22, 14, 22, 14), 16));
    }

    void Transcript(StackPanel body, Meeting meeting)
    {
        if (meeting.Lines.Count == 0) return;
        Head(body, "Transcript");
        var stack = new StackPanel();
        foreach (var line in meeting.Lines)
        {
            var block = new TextBlock { FontFamily = DashKit.Sans, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
            block.Inlines.Add(new System.Windows.Documents.Run(Meeting.Clock(line.At) + "   ") { Foreground = DashKit.B(P.Faint) });
            if (line.Marked) block.Inlines.Add(new System.Windows.Documents.Run("★ ") { Foreground = DashKit.B(P.AccentEdge) });
            block.Inlines.Add(new System.Windows.Documents.Run(meeting.Who(line.Speaker) + "  ") { FontWeight = FontWeights.SemiBold, Foreground = DashKit.B(line.Speaker == "You" ? P.AccentEdge : P.Text) });
            block.Inlines.Add(new System.Windows.Documents.Run(line.Text) { Foreground = DashKit.B(P.Text) });
            stack.Children.Add(block);
        }
        body.Children.Add(k.Card(stack, new Thickness(22, 14, 22, 14), 16));
    }

    /// <summary>The notes as plain text, for pasting into an email or a doc.</summary>
    static string Notes(Meeting m)
    {
        var text = new StringBuilder();
        text.AppendLine(m.Title).AppendLine($"{m.Started:dddd d MMMM yyyy, HH:mm}, {m.Length}").AppendLine();
        if (m.Summary is { Length: > 0 } s) text.AppendLine(s).AppendLine();
        void List(string title, IEnumerable<string> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            text.AppendLine(title);
            foreach (var item in list) text.AppendLine("- " + item);
            text.AppendLine();
        }
        List("Decisions", m.Decisions);
        List("Open questions", m.Questions);
        List("Tasks", m.Tasks.Select(t => $"{t.Text} ({(t.Owner == "me" ? "me" : t.Owner)}{(t.Due is { } d ? $", by {d:d MMM}" : "")})"));
        return text.ToString().TrimEnd();
    }

    static void Copy(string text)
    {
        try
        {
            ClipboardTools.Retry(() => Clipboard.SetText(text));
            ToastHost.Show(ToastWindow.Message("Copied", "Paste it anywhere."));
        }
        catch (Exception ex)
        {
            Log.Error("Could not copy", ex);
        }
    }
}
