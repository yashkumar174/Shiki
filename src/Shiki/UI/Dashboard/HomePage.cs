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
/// Home: a greeting, the night window (drop files on it, or pick some), the cat's numbers,
/// one helpful tip, and what was converted lately.
/// </summary>
public sealed class HomePage(DashKit k, DashboardHost host, Action seeAll, Action search)
{
    DashPalette P => k.P;

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 48), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
        var hello = k.Text(Greeting(), 34, font: DashKit.Pixel);
        hello.Margin = new Thickness(0, 0, 0, 24);
        page.Children.Add(hello);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(312) });

        var left = new StackPanel();
        left.Children.Add(Hero());
        var find = k.IconButton("\uE721", search, "Search everything (Ctrl+F)", 15, P.Faint, 30);
        var recent = new HistoryList(k, host).Build(History.Entries.Take(12).ToList(),
            "Nothing converted yet. Drop a file on the card above, or on the cat.", find);
        ((FrameworkElement)recent).Margin = new Thickness(0, 34, 0, 0);
        left.Children.Add(recent);
        if (History.Entries.Count > 12)
        {
            var more = k.Button("See all in History", seeAll, DashKit.Pill.Quiet);
            more.Margin = new Thickness(0, 14, 0, 0);
            left.Children.Add(more);
        }
        grid.Children.Add(left);

        var right = Side();
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        page.Children.Add(grid);
        return page;
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
        const int cell = 5, r = 8;
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
        moon.Margin = new Thickness(0, 26, 40, 0);

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

    // ---- Right column: numbers and a tip ----

    UIElement Side()
    {
        var s = host.Settings;
        var numbers = new StackPanel();
        numbers.Children.Add(Stat(Compact(s.FilesDone), s.FilesDone == 1 ? "file converted" : "files converted"));
        numbers.Children.Add(Stat(Bytes(s.BytesSaved), "saved"));
        int streak = History.Streak();
        numbers.Children.Add(Stat(streak.ToString(), streak == 1 ? "day streak" : "day streak"));

        var stack = new StackPanel();
        stack.Children.Add(new Border { Padding = new Thickness(30, 26, 30, 26), Child = numbers });
        if (Tip() is { } tip)
        {
            stack.Children.Add(k.Rule());
            stack.Children.Add(tip);
        }
        var card = k.Card(stack, new Thickness(0), 18);
        card.VerticalAlignment = VerticalAlignment.Top;
        if (host.Level == null || host.Points == null) return card;
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        column.Children.Add(card);
        var grows = Grows();
        ((FrameworkElement)grows).Margin = new Thickness(0, 18, 0, 0);
        column.Children.Add(grows);
        return column;
    }

    /// <summary>
    /// The cat's level: points so far and to the next level, what's next, and the cats and things
    /// to wear it has unlocked (locked ones show the level that brings them).
    /// </summary>
    UIElement Grows()
    {
        int level = host.Level!(), points = host.Points!();
        int from = CatLevel.At(level), to = CatLevel.At(level + 1);
        var s = host.Settings;
        var stack = new StackPanel();
        var head = new DockPanel();
        var face = new Image { Source = CatSprites.Head(CatSprites.Dressed(CatSprites.Skin(s.Cat), s.Accessory)), Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) };
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        DockPanel.SetDock(face, Dock.Left);
        head.Children.Add(face);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(k.Text($"{host.CatName()}, level {level}", 17, weight: FontWeights.SemiBold));
        titles.Children.Add(k.Text($"{points:N0} points, {Math.Max(0, to - points):N0} to level {level + 1}", 13, P.Muted));
        head.Children.Add(titles);
        stack.Children.Add(head);

        var track = new Grid { Height = 6, Margin = new Thickness(0, 14, 0, 6) };
        track.Children.Add(new Border { Background = DashKit.B(P.Hover), CornerRadius = new CornerRadius(3) });
        // The cat's own progress: in its eye colour.
        var fill = new Border { Background = DashKit.B(DashPalette.Eye), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp((points - from) / (double)Math.Max(1, to - from), 0, 1);
        track.Children.Add(fill);
        stack.Children.Add(track);
        if (CatLevel.Next(level) is { } next)
            stack.Children.Add(k.Text($"Next: {next.Name}{(next.IsCat ? " (a new cat)" : "")} at level {next.Level}", 13, P.Faint));

        stack.Children.Add(Picker("Cats", CatSprites.Skins.Select(c => (c.Id, c.Name, (ImageSource?)CatSprites.Head(c),
            CatLevel.CatUnlocked(c.Id, level), CatLevel.Unlocks.FirstOrDefault(u => u.Id == c.Id)?.Level ?? 1)).ToList(), s.Cat, id => host.SetCat(id)));
        var wearables = new List<(string Id, string Name, ImageSource? Picture, bool Open, int Level)> { ("", "Nothing", null, true, 1) };
        wearables.AddRange(CatSprites.Accessories.Select(a => (a.Id, a.Name, (ImageSource?)CatSprites.Head(CatSprites.Dressed(CatSprites.Skin(s.Cat), a.Id)),
            CatLevel.AccessoryUnlocked(a.Id, level), CatLevel.Unlocks.First(u => u.Id == a.Id).Level)));
        stack.Children.Add(Picker("Wears", wearables, s.Accessory ?? "", id => host.SetAccessory?.Invoke(id.Length == 0 ? null : id)));
        return k.Card(stack, new Thickness(24, 20, 24, 20), 18);
    }

    UIElement Picker(string label, List<(string Id, string Name, ImageSource? Picture, bool Open, int Level)> options, string current, Action<string> pick)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        stack.Children.Add(k.Label(label));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (id, name, picture, open, needs) in options)
        {
            bool chosen = id == current;
            var content = new Grid { Width = 36, Height = 36 };
            if (picture != null)
            {
                var image = new Image { Source = picture, Width = 30, Height = 30, Opacity = open ? 1 : 0.28 };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                content.Children.Add(image);
            }
            else content.Children.Add(k.Icon("\uE711", 13, P.Faint));
            if (!open) content.Children.Add(k.Icon("\uE72E", 12, P.Text));
            var tile = new Border
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1.5),
                BorderBrush = DashKit.B(chosen ? P.AccentEdge : Colors.Transparent),
                Background = DashKit.B(chosen ? P.Selected : P.Hover),
                Cursor = open ? Cursors.Hand : Cursors.Arrow,
                ToolTip = open ? name : $"{name}: unlocks at level {needs}",
                Child = content,
            };
            if (open) tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; pick(id); };
            row.Children.Add(tile);
        }
        stack.Children.Add(row);
        return stack;
    }

    UIElement Stat(string number, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var big = k.Text(number, 34, font: DashKit.Pixel);
        var small = k.Text(label, 15, P.Muted);
        small.Margin = new Thickness(10, 0, 0, 7);
        small.VerticalAlignment = VerticalAlignment.Bottom;
        row.Children.Add(big);
        row.Children.Add(small);
        return row;
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
