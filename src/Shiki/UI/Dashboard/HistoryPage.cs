using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>
/// Results as Flow lists transcripts: one white card, a row per job (time on the left, what
/// happened on the right), grouped under day labels. Hovering a row shows Open, Copy and Folder.
/// </summary>
public sealed class HistoryList(DashKit k, DashboardHost host, bool canRemove = false)
{
    DashPalette P => k.P;

    /// <param name="accessory">Sits at the right of the first day label (Home puts search there).</param>
    public UIElement Build(IReadOnlyList<HistoryEntry> entries, string emptyText, UIElement? accessory = null)
    {
        var stack = new StackPanel();
        if (entries.Count == 0 && accessory != null)
        {
            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 12) };
            DockPanel.SetDock(accessory, Dock.Right);
            head.Children.Add(accessory);
            head.Children.Add(k.Label(DayName(DateTime.Today)));
            stack.Children.Add(head);
        }
        if (entries.Count == 0)
        {
            var empty = k.Text(emptyText, 15, P.Muted, wrap: true);
            stack.Children.Add(k.Card(empty, new Thickness(28, 26, 28, 26), 18));
            return stack;
        }

        foreach (var day in entries.GroupBy(e => e.Time.Date))
        {
            var header = new DockPanel { Margin = new Thickness(2, stack.Children.Count == 0 ? 0 : 26, 0, 12), MinHeight = 28 };
            if (stack.Children.Count == 0 && accessory != null)
            {
                DockPanel.SetDock(accessory, Dock.Right);
                header.Children.Add(accessory);
            }
            header.Children.Add(k.Label(DayName(day.Key)));
            stack.Children.Add(header);
            var rows = new StackPanel();
            bool first = true;
            foreach (var entry in day)
            {
                if (!first) rows.Children.Add(k.Rule());
                rows.Children.Add(Row(entry));
                first = false;
            }
            var card = k.Card(rows, new Thickness(0), 18);
            card.ClipToBounds = true;
            stack.Children.Add(card);
        }
        return stack;
    }

    static string DayName(DateTime day) =>
        day == DateTime.Today ? "Today"
        : day == DateTime.Today.AddDays(-1) ? "Yesterday"
        : day > DateTime.Today.AddDays(-7) ? day.ToString("dddd")
        : day.ToString("d MMMM yyyy");

    UIElement Row(HistoryEntry entry)
    {
        var outputs = entry.Outputs.Where(o => File.Exists(o) || Directory.Exists(o)).ToList();
        var grid = new Grid { Background = Brushes.Transparent, Cursor = outputs.Count > 0 ? Cursors.Hand : Cursors.Arrow };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var time = k.Text(entry.Time.ToString("h:mmtt").ToLowerInvariant(), 14, P.Faint);
        time.Margin = new Thickness(22, 20, 0, 0);
        time.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(time);

        var what = new StackPanel { Margin = new Thickness(0, 17, 16, 18) };
        var title = new TextBlock { FontFamily = DashKit.Sans, FontSize = 15.5, Foreground = DashKit.B(P.Text), TextTrimming = TextTrimming.CharacterEllipsis };
        if (entry.ActionId == "record-save")
        {
            // A recording has no "from": it just is one.
            title.Inlines.Add(new System.Windows.Documents.Run("Screen recording") { FontWeight = FontWeights.SemiBold });
        }
        else
        {
            title.Inlines.Add(new System.Windows.Documents.Run(Subject(entry)));
            title.Inlines.Add(new System.Windows.Documents.Run("  →  ") { Foreground = DashKit.B(P.Faint) });
            title.Inlines.Add(new System.Windows.Documents.Run(entry.Action) { FontWeight = FontWeights.SemiBold });
        }
        what.Children.Add(title);
        var detail = k.Text(Detail(entry, outputs), 13.5, entry.Failed ? P.Danger : P.Muted, wrap: true);
        detail.Margin = new Thickness(0, 4, 0, 0);
        what.Children.Add(detail);
        Grid.SetColumn(what, 1);
        grid.Children.Add(what);

        // Actions show on hover, so the list stays calm.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
        bool playable = outputs.Count == 1 && File.Exists(outputs[0]) && PlayerView.CanPlay(outputs[0]) && host.Play != null;
        if (playable) actions.Children.Add(k.IconButton("\uE768", () => host.Play!(outputs[0]), "Play here", 14, P.Muted));
        if (outputs.Count > 0)
        {
            actions.Children.Add(k.IconButton("\uE8A7", () => Open(outputs), outputs.Count == 1 ? "Open in its usual app" : "Open the folder", 14, P.Muted));
            actions.Children.Add(k.IconButton("\uE8C8", () => Copy(outputs), "Copy", 14, P.Muted));
            actions.Children.Add(k.IconButton("\uE838", () => ShowInFolder(outputs[0]), "Show in folder", 14, P.Muted));
        }
        var inputs = entry.Inputs.Where(File.Exists).ToList();
        if (inputs.Count > 0 && ActionCatalog.Find(entry.ActionId) is { } action && action.Editor == null)
            actions.Children.Add(k.IconButton("\uE72C", () => host.Run(action, inputs), "Do it again", 14, P.Muted));
        if (canRemove)
            actions.Children.Add(k.IconButton("\uE74D", () => History.Remove(entry), "Remove from the list (the files stay)", 14, P.Muted));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var hover = new SolidColorBrush(Colors.Transparent);
        grid.Background = hover;
        grid.MouseEnter += (_, _) => { hover.Color = DashKit.A(P.Hover, P.Dark ? (byte)0xFF : (byte)0x90); actions.Visibility = Visibility.Visible; };
        grid.MouseLeave += (_, _) => { hover.Color = Colors.Transparent; actions.Visibility = Visibility.Hidden; };
        // Videos and sound play right here; anything else opens in its usual app.
        grid.MouseLeftButtonUp += (_, _) =>
        {
            if (playable) host.Play!(outputs[0]);
            else if (outputs.Count > 0) Open(outputs);
        };
        return grid;
    }

    static string Subject(HistoryEntry e)
    {
        var picked = FileList.TopLevel(e.Inputs);
        if (picked.Count == 0) return "Files";
        var name = Path.GetFileName(picked[0].TrimEnd('\\'));
        return picked.Count == 1 ? name : $"{name} and {picked.Count - 1} more";
    }

    static string Detail(HistoryEntry e, List<string> outputs)
    {
        if (e.Failed) return e.Error ?? "Didn't work.";
        if (e.Note != null) return e.Note;
        if (e.Outputs.Count == 0) return "Done";
        if (outputs.Count == 0) return e.Outputs.Count == 1 ? $"{Path.GetFileName(e.Outputs[0])} (moved or deleted)" : "The results were moved or deleted";
        long bytes = outputs.Where(File.Exists).Sum(f => new FileInfo(f).Length);
        var size = bytes > 0 ? $", {Size(bytes)}" : "";
        return outputs.Count == 1 ? $"Saved as {Path.GetFileName(outputs[0])}{size}" : $"{outputs.Count} files{size}";
    }

    static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1L << 30 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    static void Open(List<string> outputs)
    {
        if (outputs.Count == 1) Launch(outputs[0], null);
        else Launch("explorer.exe", $"\"{Path.GetDirectoryName(outputs[0])}\"");
    }

    static void ShowInFolder(string path) => Launch("explorer.exe", $"/select,\"{path}\"");

    static void Copy(List<string> outputs)
    {
        try
        {
            ClipboardTools.CopyFiles(outputs);
            ToastHost.Show(ToastWindow.Message("Copied", outputs.Count == 1 ? Path.GetFileName(outputs[0]) : $"{outputs.Count} files"));
        }
        catch (Exception ex)
        {
            Log.Error("Copy from history failed", ex);
        }
    }

    static void Launch(string file, string? args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {file}", ex);
        }
    }
}

/// <summary>Everything ever converted: search, filter by kind, re-run, open, or forget.</summary>
public sealed class HistoryPage(DashKit k, DashboardHost host, string? search, Action<string?> remember, Action rebuild)
{
    DashPalette P => k.P;
    static FileKind? _filter;
    const int PageSize = 150;

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 48), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
        var title = k.Text("History", 30, weight: FontWeights.SemiBold, font: DashKit.Display);
        page.Children.Add(title);
        var sub = k.Text("Everything you've converted. Removing a line here only forgets it; your files stay put.", 15, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 22);
        page.Children.Add(sub);

        var listHost = new ContentControl();
        var box = new TextBox
        {
            Text = search ?? "",
            FontFamily = DashKit.Sans,
            FontSize = 15,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = k.Text("Search files, actions or notes", 15, P.Faint);
        hint.IsHitTestVisible = false;
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid();
        field.Children.Add(hint);
        field.Children.Add(box);
        var searchRow = new DockPanel();
        var icon = k.Icon("\uE721", 15, P.Faint);
        icon.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(icon, Dock.Left);
        searchRow.Children.Add(icon);
        searchRow.Children.Add(field);
        var searchCard = k.Card(searchRow, new Thickness(18, 12, 18, 12), 14);
        page.Children.Add(searchCard);

        var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 22) };
        void Refresh() => listHost.Content = List(box.Text);
        foreach (var (label, kind) in new (string, FileKind?)[] { ("All", null), ("Images", FileKind.Image), ("Video", FileKind.Video), ("Audio", FileKind.Audio), ("PDF", FileKind.Pdf), ("Documents", FileKind.Document), ("Files", FileKind.Archive) })
        {
            var chip = k.Button(label, () => { }, _filter == kind ? DashKit.Pill.Accent : DashKit.Pill.Quiet, size: 13);
            chip.Padding = new Thickness(14, 6, 14, 7);
            chip.Margin = new Thickness(0, 0, 8, 8);
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _filter = kind;
                remember(box.Text);
                chips.Dispatcher.BeginInvoke(rebuild);
            };
            chips.Children.Add(chip);
        }
        page.Children.Add(chips);
        page.Children.Add(listHost);

        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            remember(box.Text);
            Refresh();
        };
        if (search != null) box.Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
        Refresh();
        return page;
    }

    UIElement List(string query)
    {
        IEnumerable<HistoryEntry> entries = History.Entries;
        if (_filter is { } kind) entries = entries.Where(e => (kind == FileKind.Archive ? e.ActionId is "zip" or "archive-extract" : e.Kind == kind));
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
            entries = entries.Where(e => words.All(w =>
                e.Action.Contains(w, StringComparison.OrdinalIgnoreCase)
                || (e.Note?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Inputs.Concat(e.Outputs).Any(f => Path.GetFileName(f).Contains(w, StringComparison.OrdinalIgnoreCase))));
        var list = entries.ToList();
        var stack = new StackPanel();
        stack.Children.Add(new HistoryList(k, host, canRemove: true).Build(list.Take(PageSize).ToList(),
            emptyText: words.Length > 0 || _filter != null ? "Nothing matches. Try another word or filter." : "Nothing here yet. Everything you convert shows up here."));
        if (list.Count > PageSize)
        {
            var more = k.Text($"Showing the newest {PageSize} of {list.Count}. Search to find older ones.", 13.5, P.Faint);
            more.Margin = new Thickness(2, 14, 0, 0);
            stack.Children.Add(more);
        }
        return stack;
    }
}
