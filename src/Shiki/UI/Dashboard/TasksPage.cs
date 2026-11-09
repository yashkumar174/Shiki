using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>
/// Tasks: from meetings, added here, or told to the cat ("remind me to…"). Overdue first, then
/// today, upcoming and someday; ticking one moves it to Done. The cat reminds you when one is due.
/// </summary>
public sealed class TasksPage(DashKit k, DashboardHost host, Action rebuild)
{
    DashPalette P => k.P;
    static string _when = "none";

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 48), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(k.Text("Tasks", 34, font: DashKit.Pixel));
        var sub = k.Text($"From your meetings, added here, or just tell {host.CatName()}: \"remind me to call the bank tomorrow at 5\". You get a reminder when one is due.", 15, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 22);
        page.Children.Add(sub);
        page.Children.Add(AddBox());

        var all = Todos.All;
        var open = all.Where(t => !t.Done).ToList();
        var today = DateTime.Today;
        Section(page, "Overdue", open.Where(t => t.Due is { } d && (t.HasTime ? d < DateTime.Now : d.Date < today)).OrderBy(t => t.Due), P.Danger);
        Section(page, "Today", open.Where(t => t.Due is { } d && d.Date == today && !(t.HasTime && d < DateTime.Now)).OrderBy(t => t.Due), null);
        Section(page, "Coming up", open.Where(t => t.Due is { } d && d.Date > today).OrderBy(t => t.Due), null);
        Section(page, "Someday", open.Where(t => t.Due == null).OrderByDescending(t => t.Created), null);
        Section(page, "Done", all.Where(t => t.Done).OrderByDescending(t => t.Reminded ?? t.Created).Take(15), null);
        if (all.Count == 0)
        {
            var empty = k.Text("Nothing to do. Tasks from meetings land here by themselves.", 15, P.Muted, wrap: true);
            page.Children.Add(k.Card(empty, new Thickness(28, 26, 28, 26), 18));
        }
        return page;
    }

    UIElement AddBox()
    {
        var box = new TextBox
        {
            FontFamily = DashKit.Sans,
            FontSize = 15,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = k.Text("Add a task…", 15, P.Faint);
        hint.IsHitTestVisible = false;
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        void Add()
        {
            var text = box.Text.Trim();
            if (text.Length == 0) return;
            DateTime? due = _when switch
            {
                "today" => DateTime.Today,
                "tomorrow" => DateTime.Today.AddDays(1),
                // The coming Saturday (today, if it is Saturday).
                "weekend" => DateTime.Today.AddDays(((int)DayOfWeek.Saturday - (int)DateTime.Today.DayOfWeek + 7) % 7),
                "week" => DateTime.Today.AddDays(7),
                _ => null,
            };
            Todos.Add(text, due);
            box.Text = "";
            rebuild();
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        var add = k.Button("Add", Add, DashKit.Pill.Accent, size: 13.5);
        add.Padding = new Thickness(16, 7, 16, 8);
        var row = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        row.Children.Add(add);
        var field = new Grid { Margin = new Thickness(0, 0, 12, 0), Children = { hint, box } };
        row.Children.Add(field);

        var chips = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (id, label) in new[] { ("none", "No date"), ("today", "Today"), ("tomorrow", "Tomorrow"), ("weekend", "This weekend"), ("week", "In a week") })
        {
            var chip = k.Button(label, () => { _when = id; rebuild(); }, _when == id ? DashKit.Pill.Accent : DashKit.Pill.Quiet, size: 12.5);
            chip.Padding = new Thickness(12, 5, 12, 6);
            chip.Margin = new Thickness(0, 0, 6, 0);
            chips.Children.Add(chip);
        }
        var stack = new StackPanel { Children = { row, chips } };
        var card = k.Card(stack, new Thickness(18, 12, 12, 12), 16);
        card.Margin = new Thickness(0, 0, 0, 8);
        return card;
    }

    void Section(StackPanel page, string label, IEnumerable<Todo> todos, Color? accent)
    {
        var list = todos.ToList();
        if (list.Count == 0) return;
        var head = k.Label(label);
        if (accent is { } a) head.Foreground = DashKit.B(a);
        head.Margin = new Thickness(2, 24, 0, 10);
        page.Children.Add(head);
        var rows = new StackPanel();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) rows.Children.Add(k.Rule());
            rows.Children.Add(Row(list[i]));
        }
        var card = k.Card(rows, new Thickness(0), 16);
        card.ClipToBounds = true;
        page.Children.Add(card);
    }

    UIElement Row(Todo todo)
    {
        var grid = new Grid { Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tick = k.IconButton(todo.Done ? "\uE73E" : "\uEA3A", () =>
        {
            todo.Done = !todo.Done;
            Todos.Update(todo);
            rebuild();
        }, todo.Done ? "Not done yet" : "Done", 17, todo.Done ? P.AccentEdge : P.Faint, 36);
        tick.Margin = new Thickness(12, 10, 4, 10);
        tick.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(tick);

        var what = new StackPanel { Margin = new Thickness(4, 14, 12, 14) };
        var text = k.Text(todo.Text, 15, todo.Done ? P.Faint : P.Text, wrap: true);
        if (todo.Done) text.TextDecorations = TextDecorations.Strikethrough;
        what.Children.Add(text);
        var meta = new List<string>();
        if (todo.DueText() is { Length: > 0 } due) meta.Add($"due {due}");
        if (todo.Who is { Length: > 0 } who) meta.Add($"for {who}");
        if (todo.From is { Length: > 0 } from) meta.Add($"from {from}");
        if (meta.Count > 0)
        {
            var line = k.Text(string.Join(", ", meta), 13, P.Muted, wrap: true);
            line.Margin = new Thickness(0, 3, 0, 0);
            what.Children.Add(line);
        }
        Grid.SetColumn(what, 1);
        grid.Children.Add(what);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
        if (todo.MeetingId is { } meetingId && host.OpenMeeting != null)
            actions.Children.Add(k.IconButton("\uE8F2", () => host.OpenMeeting(meetingId), "Open the meeting", 14, P.Muted));
        actions.Children.Add(k.IconButton("\uE74D", () => { Todos.Remove(todo); rebuild(); }, "Remove", 14, P.Muted));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        grid.MouseEnter += (_, _) => actions.Visibility = Visibility.Visible;
        grid.MouseLeave += (_, _) => actions.Visibility = Visibility.Hidden;
        return grid;
    }
}
