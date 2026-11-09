using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>
/// Home, a board of tiles: the greeting and what's switched on, your cat in its own colour, the
/// night window to drop files on, your level, asking the cat, quick tools, your numbers, what
/// was done lately, today's tasks, your phone and meetings. Everything important on one screen.
/// </summary>
public sealed class HomePage(DashKit k, DashboardHost host, Action seeAll, Action search, Action? refresh = null,
    Func<UIElement?>? setup = null, Action? openCats = null, Action<DashPage>? go = null)
{
    DashPalette P => k.P;
    const double Gap = 16;

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(40, 34, 40, 48), MaxWidth = 1280, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(Header());
        var holder = new Border { Margin = new Thickness(0, 26, 0, 0) };
        bool? wide = null;
        void Layout(double width)
        {
            bool isWide = width >= 1060;
            if (wide == isWide) return;
            wide = isWide;
            holder.Child = Board(isWide);
        }
        holder.SizeChanged += (_, e) => Layout(e.NewSize.Width);
        Layout(1280);
        page.Children.Add(holder);
        return page;
    }

    /// <summary>The tiles, in three columns (or two, in a narrow window).</summary>
    Grid Board(bool wide)
    {
        var board = new Grid();
        foreach (var width in wide ? new[] { 270.0, Gap, -1, Gap, 300 } : new[] { 250.0, Gap, -1 })
            board.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        var heights = wide ? new[] { 340.0, Gap, 250, Gap, -1, Gap, -1 } : new[] { 330.0, Gap, 250, Gap, -1, Gap, -1, Gap, -1, Gap, -1 };
        foreach (var h in heights) board.RowDefinitions.Add(new RowDefinition { Height = h < 0 ? GridLength.Auto : new GridLength(h) });
        void Put(UIElement e, int row, int col, int rowSpan = 1, int colSpan = 1)
        {
            Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetRowSpan(e, rowSpan); Grid.SetColumnSpan(e, colSpan);
            board.Children.Add(e);
        }
        var extra = setup?.Invoke() ?? (Tip() is { } tip ? Tile(tip, P.Card, P.Line, padding: new Thickness(0)) : null);
        if (wide)
        {
            Put(CatTile(), 0, 0, 3); Put(Hero(), 0, 2); Put(LevelTile(), 0, 4);
            Put(Cells(AskTile(), ToolsTile()), 2, 2); Put(NumbersTile(), 2, 4);
            Put(LatelyTile(), 4, 0, 1, 3); Put(TodayTile(), 4, 4);
            Put(Cells(PhoneTile(), MeetingsTile(), extra), 6, 0, 1, 5);
        }
        else
        {
            Put(CatTile(), 0, 0, 3); Put(Hero(), 0, 2);
            Put(Cells(AskTile(), ToolsTile()), 2, 2);
            Put(Cells(LevelTile(), NumbersTile()), 4, 0, 1, 3);
            Put(LatelyTile(), 6, 0, 1, 3);
            Put(Cells(TodayTile(), PhoneTile()), 8, 0, 1, 3);
            Put(Cells(MeetingsTile(), extra), 10, 0, 1, 3);
        }
        return board;
    }

    /// <summary>Tiles side by side in one cell of the board, equal widths.</summary>
    static Grid Cells(params UIElement?[] items)
    {
        var shown = items.Where(i => i != null).Cast<UIElement>().ToArray();
        var cells = new Grid();
        for (int i = 0; i < shown.Length; i++)
        {
            if (i > 0) cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            cells.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(shown[i], i * 2);
            cells.Children.Add(shown[i]);
        }
        return cells;
    }

    // ---- The top: the greeting, the date, and what's switched on ----

    UIElement Header()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(k.Text(Greeting(), 44, font: DashKit.Pixel));
        var date = k.Text(DateTime.Now.ToString("dddd, d MMMM"), 15, P.Muted, FontWeights.Medium);
        date.Margin = new Thickness(2, 6, 0, 0);
        left.Children.Add(date);
        grid.Children.Add(left);

        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 4) };
        var s = host.Settings;
        chips.Children.Add(Chip("\uE720", $"Talk: {s.TalkHotkey}", true));
        chips.Children.Add(Chip("\uE715", Core.Mail.Gmail.Connected ? "Gmail connected" : "Gmail not connected", Core.Mail.Gmail.Connected));
        var phone = Core.Phone.PhoneLink.Current?.Connected();
        var paired = Core.Phone.PhoneLink.Current?.Phones.FirstOrDefault();
        chips.Children.Add(Chip("\uE8EA", phone != null ? $"{phone.Name} here" : paired != null ? $"{paired.Name} away" : "No phone yet", phone != null));
        Grid.SetColumn(chips, 1);
        grid.Children.Add(chips);
        return grid;
    }

    Border Chip(string glyph, string text, bool on)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 7, Height = 7, Fill = DashKit.B(on ? Color.FromRgb(0x4C, 0xC3, 0x8A) : P.Faint), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(dot);
        var icon = k.Icon(glyph, 13, P.Muted);
        icon.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(icon);
        row.Children.Add(k.Text(text, 13, P.Text, FontWeights.SemiBold));
        return new Border
        {
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12, 7, 14, 8),
            Margin = new Thickness(8, 0, 0, 8),
            Child = row,
        };
    }

    // ---- Tiles ----

    /// <summary>A tile: a rounded block in its own colour; with a page to open, an arrow in its corner and a lift under the mouse.</summary>
    Border Tile(UIElement content, Color fill, Color? edge = null, Action? open = null, Color? arrowInk = null, Thickness? padding = null)
    {
        var layers = new Grid();
        layers.Children.Add(content);
        if (open != null)
        {
            var ink = arrowInk ?? P.Text;
            var arrow = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                BorderBrush = DashKit.B(DashKit.A(ink, 0x55)),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = k.Icon("\uE72A", 12, ink),
            };
            arrow.Child.RenderTransformOrigin = new Point(0.5, 0.5);
            arrow.Child.RenderTransform = new RotateTransform(-45);
            layers.Children.Add(arrow);
        }
        var tile = new Border
        {
            Background = DashKit.B(fill),
            BorderBrush = DashKit.B(edge ?? Colors.Transparent),
            BorderThickness = new Thickness(edge == null ? 0 : 1),
            CornerRadius = new CornerRadius(24),
            Padding = padding ?? new Thickness(22, 20, 22, 22),
            Child = layers,
            ClipToBounds = false,
        };
        if (open != null)
        {
            tile.Cursor = Cursors.Hand;
            DashKit.Press(tile);
            tile.MouseLeftButtonUp += (_, e) => { if (e.Handled) return; e.Handled = true; open(); };
        }
        return tile;
    }

    void Go(DashPage page) => go?.Invoke(page);

    /// <summary>Your cat, big, in its own colour, with what it's like.</summary>
    UIElement CatTile()
    {
        var s = host.Settings;
        var skin = CatSprites.Skin(s.Cat);
        var (fill, ink, nature) = CatsPage.Look(skin);
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new StackPanel { Margin = new Thickness(0, 0, 40, 0) };
        top.Children.Add(k.Text("Your cat", 13, DashKit.A(ink, 0xB0), FontWeights.Bold));
        top.Children.Add(k.Text(skin.Name, 36, ink, font: DashKit.Pixel));
        stack.Children.Add(top);
        var cat = new Image
        {
            Source = CatSprites.Frame(CatSprites.Dressed(skin, s.Accessory), CatPose.Happy),
            Width = 20 * 9,
            Height = 20 * 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.NearestNeighbor);
        Grid.SetRow(cat, 1);
        stack.Children.Add(cat);
        var bottom = new StackPanel();
        bottom.Children.Add(k.Text(nature, 14, DashKit.A(ink, 0xE6), wrap: true));
        var meet = k.Text("Meet all the cats", 14, ink, FontWeights.Bold);
        meet.Margin = new Thickness(0, 12, 0, 0);
        bottom.Children.Add(meet);
        Grid.SetRow(bottom, 2);
        stack.Children.Add(bottom);
        return Tile(stack, fill, open: () => { if (openCats != null) openCats(); else Go(DashPage.Cats); }, arrowInk: ink);
    }

    /// <summary>The level, what the next one brings, and how close it is (in Mochi's orange).</summary>
    UIElement LevelTile()
    {
        var ink = Color.FromRgb(0x2B, 0x1D, 0x08);
        var fill = CatsPage.Look(CatSprites.Skin(host.Settings.Cat)).Fill == DashPalette.Eye ? Color.FromRgb(0xF0, 0x91, 0x3A) : DashPalette.Eye;
        int level = host.Level?.Invoke() ?? 1;
        var stats = host.Stats?.Invoke();
        var stack = new StackPanel();
        stack.Children.Add(k.Text("Level", 13, DashKit.A(ink, 0xB0), FontWeights.Bold));
        var big = new StackPanel { Orientation = Orientation.Horizontal };
        big.Children.Add(k.Text(level.ToString(), 64, ink, font: DashKit.Pixel));
        var name = k.Text(CatLevel.Goal(level).Title, 16, ink, FontWeights.Bold, wrap: true);
        name.VerticalAlignment = VerticalAlignment.Bottom;
        name.Margin = new Thickness(12, 0, 0, 14);
        name.MaxWidth = 150;
        big.Children.Add(name);
        stack.Children.Add(big);
        if (level < CatLevel.Max && stats != null)
        {
            var next = CatLevel.Goal(level + 1);
            var reward = CatLevel.NewAt(level + 1).FirstOrDefault();
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (reward != null)
            {
                var picture = reward.IsCat ? CatSprites.Head(CatSprites.Skin(reward.Id)) : CatSprites.Head(CatSprites.Dressed(CatSprites.Skin(host.Settings.Cat), reward.Id));
                var image = new Image { Source = picture, Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0) };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                DockPanel.SetDock(image, Dock.Left);
                row.Children.Add(image);
            }
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(k.Text(reward != null ? $"Next: {reward.Name}" : $"Next: level {level + 1}", 15, ink, FontWeights.Bold));
            words.Children.Add(k.Text($"{next.Quests.Count(q => q.Done(stats))} of {next.Quests.Count} tasks done", 13.5, DashKit.A(ink, 0xC0)));
            row.Children.Add(words);
            stack.Children.Add(row);
            var track = new Grid { Height = 10, Margin = new Thickness(0, 16, 0, 0) };
            track.Children.Add(new Border { Background = DashKit.B(DashKit.A(ink, 0x26)), CornerRadius = new CornerRadius(5) });
            var bar = new Border { Background = DashKit.B(ink), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
            double toward = CatLevel.TowardNext(stats, level);
            track.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * Math.Clamp(toward, 0, 1);
            track.Children.Add(bar);
            stack.Children.Add(track);
        }
        else stack.Children.Add(k.Text("Every level reached.", 15, ink, FontWeights.Bold));
        return Tile(stack, fill, open: () => Go(DashPage.Levels), arrowInk: ink);
    }

    /// <summary>Ask the cat: a message box that opens the Ask page (in Sky's blue).</summary>
    UIElement AskTile()
    {
        var ink = Color.FromRgb(0x10, 0x23, 0x3F);
        var stack = new StackPanel();
        stack.Children.Add(k.Text($"Ask {host.CatName()}", 26, ink, font: DashKit.Pixel));
        var hint = k.Text("\"Email the invoice to Tausif\", \"play Seedhe Maut\", \"remind me at 5\".", 13.5, DashKit.A(ink, 0xC8), wrap: true);
        hint.Margin = new Thickness(0, 6, 40, 16);
        stack.Children.Add(hint);
        var box = new DockPanel();
        var mic = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
            Background = DashKit.B(ink),
            Child = k.Icon("\uE720", 14, Colors.White),
        };
        DockPanel.SetDock(mic, Dock.Right);
        box.Children.Add(mic);
        var placeholder = k.Text($"Tell {host.CatName()} what to do…", 14, DashKit.A(ink, 0x99));
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        box.Children.Add(placeholder);
        stack.Children.Add(new Border
        {
            Background = DashKit.B(Colors.White),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16, 5, 5, 5),
            Child = box,
        });
        var keys = k.Text($"or press {host.Settings.TalkHotkey} anywhere and talk", 12.5, DashKit.A(ink, 0xB0), FontWeights.SemiBold);
        keys.Margin = new Thickness(4, 10, 0, 0);
        stack.Children.Add(keys);
        return Tile(stack, Color.FromRgb(0x9C, 0xC6, 0xF2), open: () => Go(DashPage.Ask), arrowInk: ink);
    }

    /// <summary>Round buttons for the tools used most, straight from Home.</summary>
    UIElement ToolsTile()
    {
        var stack = new StackPanel();
        stack.Children.Add(k.Text("Quick tools", 16, P.Text, FontWeights.Bold));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(-4, 12, -4, 0) };
        var tools = new List<(string Glyph, string Label, Action? Do)>
        {
            ("\uE924", "Snip", host.Snip),
            ("\uE714", "Record", host.Record),
            ("\uE790", "Colour", host.PickColour),
            ("\uED14", "QR", host.MakeQr),
            ("\uE7C9", "Paw", host.Paw),
            ("\uE77F", "Paste", host.Paste),
            ("\uE783", "Error?", host.ExplainError),
            ("\uE8F2", host.NotesRunning?.Invoke() == true ? "Stop notes" : "Notes", host.ToggleNotes),
        };
        foreach (var (glyph, label, action) in tools.Where(t => t.Do != null))
        {
            var cell = new StackPanel { Margin = new Thickness(4, 0, 4, 10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var circle = new Border
            {
                Width = 48, Height = 48, CornerRadius = new CornerRadius(24),
                Background = DashKit.B(P.Hover),
                BorderBrush = DashKit.B(P.Line), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = k.Icon(glyph, 17, P.Text),
            };
            cell.Children.Add(circle);
            var name = k.Text(label, 12, P.Muted, FontWeights.SemiBold);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 5, 0, 0);
            cell.Children.Add(name);
            cell.MouseEnter += (_, _) => circle.Background = DashKit.B(DashKit.A(P.Accent, 0x33));
            cell.MouseLeave += (_, _) => circle.Background = DashKit.B(P.Hover);
            DashKit.Press(circle);
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; action!(); };
            grid.Children.Add(cell);
        }
        stack.Children.Add(grid);
        return Tile(stack, P.Card, P.Line, open: () => Go(DashPage.Tools));
    }

    /// <summary>The cat's tally, big (in Sumi's paper white).</summary>
    UIElement NumbersTile()
    {
        var ink = Color.FromRgb(0x14, 0x12, 0x18);
        var s = host.Settings;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        void Add(string number, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(k.Text(number, 40, ink, font: DashKit.Pixel));
            var small = k.Text(label, 14.5, DashKit.A(ink, 0xB0), FontWeights.SemiBold);
            small.VerticalAlignment = VerticalAlignment.Bottom;
            small.Margin = new Thickness(10, 0, 0, 9);
            row.Children.Add(small);
            stack.Children.Add(row);
        }
        Add(Compact(s.FilesDone), s.FilesDone == 1 ? "file converted" : "files converted");
        Add(Bytes(s.BytesSaved), "saved");
        int streak = History.Streak();
        Add(streak.ToString(), "day streak");
        return Tile(stack, Color.FromRgb(0xE9, 0xE4, 0xD8), open: () => Go(DashPage.History), arrowInk: ink);
    }

    /// <summary>The last few things done, each opening its result.</summary>
    UIElement LatelyTile()
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 44, 10) };
        var find = k.IconButton("\uE721", search, "Search everything (Ctrl+F)", 14, P.Muted, 30);
        DockPanel.SetDock(find, Dock.Right);
        head.Children.Add(find);
        head.Children.Add(k.Text("Lately", 16, P.Text, FontWeights.Bold));
        stack.Children.Add(head);
        var entries = History.Entries.Where(e => !e.Failed).Take(5).ToList();
        if (entries.Count == 0)
            stack.Children.Add(k.Text("Nothing yet. Drop a file on the night window, or on the cat.", 14, P.Muted, wrap: true));
        foreach (var entry in entries)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(9),
                Background = DashKit.B(DashKit.A(Theme.Accent(entry.Kind), 0x2E)),
                Child = k.Icon(Theme.KindGlyph(entry.Kind), 13, Theme.Accent(entry.Kind)),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            row.Children.Add(badge);
            var name = entry.Inputs.Count > 0 ? System.IO.Path.GetFileName(entry.Inputs[0]) : entry.Action;
            if (entry.Inputs.Count > 1) name += $" and {entry.Inputs.Count - 1} more";
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(k.Text(name, 14, P.Text, FontWeights.SemiBold));
            words.Children.Add(k.Text(entry.Note ?? entry.Action, 12.5, P.Muted));
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var when = k.Text(Ago(entry.Time), 12.5, P.Faint, FontWeights.SemiBold);
            when.VerticalAlignment = VerticalAlignment.Center;
            when.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(when, 2);
            row.Children.Add(when);
            var output = entry.Outputs.FirstOrDefault(System.IO.File.Exists);
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (output != null) host.OpenFile(output); else seeAll(); };
            stack.Children.Add(row);
        }
        return Tile(stack, P.Card, P.Line, open: seeAll);
    }

    static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        return span.TotalMinutes < 1 ? "now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h"
            : span.TotalDays < 7 ? $"{(int)span.TotalDays} d"
            : time.ToString("d MMM");
    }

    /// <summary>What's due today (or late), ticked off right here.</summary>
    UIElement TodayTile()
    {
        var stack = new StackPanel();
        stack.Children.Add(Spaced(k.Text("Today", 16, P.Text, FontWeights.Bold), 0, 10));
        var due = Todos.All.Where(t => !t.Done && t.Due is { } d && d.Date <= DateTime.Today).OrderBy(t => t.Due).Take(3).ToList();
        if (due.Count == 0)
            stack.Children.Add(k.Text("Nothing due today. Enjoy it.", 14, P.Muted, wrap: true));
        foreach (var todo in due)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var tick = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                BorderBrush = DashKit.B(P.Muted), BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top, ToolTip = "Done",
            };
            tick.MouseEnter += (_, _) => tick.Child = k.Icon("\uE73E", 11, P.Accent);
            tick.MouseLeave += (_, _) => tick.Child = null;
            tick.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                todo.Done = true;
                Todos.Update(todo);
                refresh?.Invoke();
            };
            DockPanel.SetDock(tick, Dock.Left);
            row.Children.Add(tick);
            var late = todo.Due!.Value.Date < DateTime.Today;
            var words = new StackPanel();
            var what = k.Text(todo.Text, 14, P.Text, FontWeights.Medium, wrap: true);
            what.MaxHeight = 40;
            what.TextTrimming = TextTrimming.CharacterEllipsis;
            words.Children.Add(what);
            words.Children.Add(k.Text(late ? "Late" : todo.HasTime ? todo.Due.Value.ToString("h:mm tt") : "Today", 12, late ? P.Danger : P.Faint, FontWeights.SemiBold));
            row.Children.Add(words);
            stack.Children.Add(row);
        }
        return Tile(stack, P.Card, P.Line, open: () => Go(DashPage.Tasks));
    }

    static FrameworkElement Spaced(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }

    /// <summary>The phone: here or away, and the way to connect one.</summary>
    UIElement PhoneTile()
    {
        var link = Core.Phone.PhoneLink.Current;
        var here = link?.Connected();
        var paired = link?.Phones.FirstOrDefault();
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(k.Icon("\uE8EA", 16, P.Text));
        var title = k.Text("Your phone", 16, P.Text, FontWeights.Bold);
        title.Margin = new Thickness(10, 0, 0, 0);
        head.Children.Add(title);
        stack.Children.Add(head);
        stack.Children.Add(k.Text(here != null ? $"{here.Name} is here. Say \"get my last photo from my phone\"."
            : paired != null ? $"{paired.Name} is away. Open Shiki on it to send photos."
            : "Send photos and files from your phone at full quality.", 14, P.Muted, wrap: true));
        if (paired == null && host.ConnectPhone != null)
        {
            var connect = k.Button("Connect your phone", host.ConnectPhone, DashKit.Pill.Accent);
            connect.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(connect);
        }
        return Tile(stack, P.Card, P.Line);
    }

    /// <summary>Meetings: notes running now, or the last one, and the button to start.</summary>
    UIElement MeetingsTile()
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(k.Icon("\uE8F2", 16, P.Text));
        var title = k.Text("Meetings", 16, P.Text, FontWeights.Bold);
        title.Margin = new Thickness(10, 0, 0, 0);
        head.Children.Add(title);
        stack.Children.Add(head);
        bool running = host.NotesRunning?.Invoke() == true;
        var last = Core.Meetings.MeetingStore.All().OrderByDescending(m => m.Started).FirstOrDefault();
        stack.Children.Add(k.Text(running ? "Taking notes now. Stop when the call ends."
            : last != null ? $"Last: {last.Title}, {last.Started:d MMM}." : "Notes for any call: the summary, decisions and everyone's tasks.", 14, P.Muted, wrap: true));
        if (host.ToggleNotes != null)
        {
            var button = k.Button(running ? "Stop notes" : "Take notes", host.ToggleNotes, running ? DashKit.Pill.Accent : DashKit.Pill.Quiet);
            button.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(button);
        }
        return Tile(stack, P.Card, P.Line, open: () => Go(DashPage.Meetings));
    }

    static string Greeting()
    {
        int hour = DateTime.Now.Hour;
        var part = hour < 5 ? "Up late" : hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        return Names.First is { Length: > 0 } name ? $"{part}, {name}" : part;
    }

    // ---- The night window: drop files here ----

    /// <summary>
    /// The one big moment: a window onto the cat's night, pixel stars and a crescent moon, the cat
    /// sitting on the sill. Drop files on it, or pick some.
    /// </summary>
    UIElement Hero()
    {
        var title = new TextBlock
        {
            Text = "Convert anything," + Environment.NewLine + "right here.",
            FontFamily = DashKit.Pixel,
            FontSize = 31,
            LineHeight = 38,
            Foreground = DashKit.B(P.HeroText),
            TextWrapping = TextWrapping.Wrap,
        };
        var sub = k.Text("Drop files on this window, or pick some. Nothing leaves your PC.", 15.5, Color.FromRgb(0xC3, 0xCD, 0xDD), wrap: true);
        sub.Margin = new Thickness(0, 12, 0, 24);
        var buttons = new WrapPanel();
        var choose = k.Button("Choose files", host.PickFiles, DashKit.Pill.Light);
        choose.Margin = new Thickness(0, 0, 10, 10);
        buttons.Children.Add(choose);
        var snip = k.Button("Snip the screen", host.Snip, DashKit.Pill.Outline, "\uE924");
        snip.Margin = new Thickness(0, 0, 0, 10);
        buttons.Children.Add(snip);
        var text = new StackPanel { Margin = new Thickness(40, 36, 12, 40), VerticalAlignment = VerticalAlignment.Center, Children = { title, sub, buttons } };

        // The sky: a handful of pixel stars, the same every time (a fixed seed), dim enough to read over.
        var sky = new Canvas { IsHitTestVisible = false };
        var random = new Random(11);
        for (int i = 0; i < 54; i++)
        {
            double size = random.NextDouble() < 0.2 ? 3 : 2;
            var star = new Rectangle { Width = size, Height = size, Fill = DashKit.B(DashKit.A(Colors.White, (byte)random.Next(55, 200))) };
            Canvas.SetLeft(star, Math.Round(random.NextDouble() * 1100));
            Canvas.SetTop(star, Math.Round(random.NextDouble() * 230));
            sky.Children.Add(star);
        }

        // The moon, drawn on a pixel grid: a full disc with a darker one taken out of it.
        const int cell = 4, r = 8;
        var moon = new Canvas { Width = (2 * r + 1) * cell, Height = (2 * r + 1) * cell, IsHitTestVisible = false };
        var moonInk = DashKit.B(Color.FromRgb(0xF4, 0xEB, 0xCB));
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                bool disc = x * x + y * y <= r * r;
                bool shadow = (x + 5) * (x + 5) + (y - 3) * (y - 3) <= r * r;
                if (!disc || shadow) continue;
                var px = new Rectangle { Width = cell, Height = cell, Fill = moonInk };
                Canvas.SetLeft(px, (x + r) * cell);
                Canvas.SetTop(px, (y + r) * cell);
                moon.Children.Add(px);
            }
        var moonGlow = new Ellipse
        {
            Width = 260,
            Height = 260,
            Fill = new RadialGradientBrush(DashKit.A(Color.FromRgb(0x5A, 0xB9, 0xFC), 0x38), DashKit.A(Color.FromRgb(0x5A, 0xB9, 0xFC), 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -84, -46, 0),
            IsHitTestVisible = false,
        };
        moon.HorizontalAlignment = HorizontalAlignment.Right;
        moon.VerticalAlignment = VerticalAlignment.Top;
        moon.Margin = new Thickness(0, 18, 34, 0);

        // The sill the cat sits on.
        var sill = new Border
        {
            Height = 18,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = DashKit.B(Color.FromRgb(0x13, 0x20, 0x38)),
            BorderBrush = DashKit.B(Color.FromRgb(0x24, 0x38, 0x5E)),
            BorderThickness = new Thickness(0, 2, 0, 0),
            IsHitTestVisible = false,
        };
        var cat = new Image
        {
            Source = CatSprites.Frame(CatSprites.Skin(host.Settings.Cat), CatPose.Happy),
            Width = 20 * 7,
            Height = 20 * 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 44, 16),
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.NearestNeighbor);

        // Text on the left, the cat and the moon in their own column on the right, so they never overlap.
        var layers = new Grid { ClipToBounds = true, MinHeight = 250 };
        layers.ColumnDefinitions.Add(new ColumnDefinition());
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        foreach (var back in new UIElement[] { sky, sill })
        {
            Grid.SetColumnSpan(back, 2);
            layers.Children.Add(back);
        }
        foreach (var right in new UIElement[] { moonGlow, moon, cat })
        {
            Grid.SetColumn(right, 1);
            layers.Children.Add(right);
        }
        layers.Children.Add(text);

        var night = new LinearGradientBrush(Color.FromRgb(0x08, 0x0F, 0x1C), Color.FromRgb(0x16, 0x27, 0x47), 90);
        night.Freeze();
        var rest = DashKit.B(P.Dark ? P.Line : Color.FromRgb(0x0B, 0x13, 0x22));
        var card = new Border
        {
            Background = night,
            BorderBrush = rest,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(20),
            Child = layers,
            AllowDrop = true,
        };
        layers.Clip = null;
        card.Loaded += (_, _) => layers.Clip = new RectangleGeometry(new Rect(0, 0, card.ActualWidth - 3, card.ActualHeight - 3), 19, 19);
        card.SizeChanged += (_, _) => layers.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, card.ActualWidth - 3), Math.Max(0, card.ActualHeight - 3)), 19, 19);

        // Dropping: the frame lights up moonlight blue, and letting go opens the wheel at the mouse.
        card.DragEnter += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; card.BorderBrush = DashKit.B(Color.FromRgb(0x5A, 0xB9, 0xFC)); e.Handled = true; };
        card.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        card.DragLeave += (_, _) => card.BorderBrush = rest;
        card.Drop += (_, e) =>
        {
            card.BorderBrush = rest;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                card.Dispatcher.BeginInvoke(() => host.Convert(FileList.Expand(paths)));
            e.Handled = true;
        };
        return card;
    }

    /// <summary>One tip at a time, picked from what isn't set up yet.</summary>
    UIElement? Tip()
    {
        var s = host.Settings;
        (string Title, string Body, string Button, Action Do)? tip =
            !host.ExplorerMenuOn() ? ("Right-click to convert", "Add Shiki to Explorer's right-click menu.", "Add it", host.AddExplorerMenu)
            : !s.UsedConvertHotkey ? ("The fastest way in", $"Select a file in File Explorer and press {s.ConvertHotkey}.", "Got it", () => { s.UsedConvertHotkey = true; host.SaveSettings(); })
            : host.TeachName != null && Core.Ai.ClaudeKey.Exists && s.WakeVariants.Count == 0
                ? ($"Just say \"Hey {host.CatName()}\"", "Teach the cat its name once (about a minute) and it wakes up when you call it, no keys needed.", "Teach it", host.TeachName)
            : host.TeachName != null && s.WakeVariants.Count > 0 && s.VoiceLock && !Core.Voice.VoicePrint.HasPrint
                ? ($"Only your voice wakes {host.CatName()}", "Say its name again, plus one sentence, and \"Hey " + host.CatName() + "\" from a video or someone else won't wake it.", "Record my voice", host.TeachName)
            : null;
        if (tip is not { } t || s.DismissedTips.Contains(t.Title)) return null;

        var stack = new StackPanel { Margin = new Thickness(30, 24, 30, 28) };
        var head = new DockPanel();
        var close = k.IconButton("\uE711", () => { s.DismissedTips.Add(t.Title); host.SaveSettings(); Hide(stack); }, "Dismiss", 11, P.Muted, 28);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(k.Text(t.Title, 18, weight: FontWeights.SemiBold));
        stack.Children.Add(head);
        var body = k.Text(t.Body, 15, P.Muted, wrap: true);
        body.Margin = new Thickness(0, 8, 0, 20);
        stack.Children.Add(body);
        var button = k.Button(t.Button, () => { t.Do(); Hide(stack); }, DashKit.Pill.Accent);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(button);
        return stack;
    }

    static void Hide(FrameworkElement tip)
    {
        tip.Visibility = Visibility.Collapsed;
        if (tip.Parent is Panel parent)
            foreach (var rule in parent.Children.OfType<Border>().Where(b => b.Height == 1)) rule.Visibility = Visibility.Collapsed;
    }

    static string Compact(int n) => n switch
    {
        < 1000 => n.ToString(),
        < 1_000_000 => $"{n / 1000.0:0.#}K",
        _ => $"{n / 1_000_000.0:0.#}M",
    };

    static string Bytes(long b) => b switch
    {
        < 1L << 20 => "0 MB",
        < 1L << 30 => $"{b / 1024.0 / 1024:0} MB",
        _ => $"{b / 1024.0 / 1024 / 1024:0.0} GB",
    };
}

/// <summary>The person's first name for the greeting, from their Windows account (when it shares it).</summary>
public static class Names
{
    public static string? First { get; private set; }
    /// <summary>The name arrived (it's looked up in the background at start).</summary>
    public static event Action? Found;

    public static async void Load()
    {
        try
        {
            var users = await Windows.System.User.FindAllAsync(Windows.System.UserType.LocalUser, Windows.System.UserAuthenticationStatus.LocallyAuthenticated);
            if (users.Count == 0) return;
            var name = await users[0].GetPropertyAsync(Windows.System.KnownUserProperties.FirstName) as string;
            if (string.IsNullOrWhiteSpace(name))
                name = (await users[0].GetPropertyAsync(Windows.System.KnownUserProperties.DisplayName) as string)?.Split(' ')[0];
            First = string.IsNullOrWhiteSpace(name) ? null : char.ToUpper(name[0]) + name[1..];
            if (First != null) Found?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Info($"No first name for the greeting: {ex.Message}");
        }
    }
}
