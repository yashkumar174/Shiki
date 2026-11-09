using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>
/// The cats: one tall card each, in the cat's own colour, with its name and its nature; pick one
/// to have it on your desktop. Then things to wear, and the levels: every task that brings the
/// next cat, with how far along it is.
/// </summary>
public sealed class CatsPage(DashKit k, DashboardHost host, Action refresh, Action? openLevels = null)
{
    DashPalette P => k.P;

    /// <summary>Each cat's card colour, the ink on it, and what it's like.</summary>
    static readonly Dictionary<string, (Color Fill, Color Ink, string Nature)> Looks = new()
    {
        ["mochi"] = (Rgb(0xF0913A), Rgb(0x1B1525), "Loud when she's excited and proud of every file she shrinks. First to say hello, never one to skip a nap."),
        ["latte"] = (Rgb(0xC9A27A), Rgb(0x2A1A14), "Elegant, chatty and a little bossy. Has opinions about your folders and shares them, politely."),
        ["sky"] = (Rgb(0x9CC6F2), Rgb(0x10233F), "Calm as a cloud. Quiet company for long work nights, with the softest paws on your keyboard."),
        ["kuro"] = (Rgb(0x1E2433), Rgb(0xF2F4F8), "A night owl with golden eyes. Sees everything, says little, and gets it done while you sleep."),
        ["ginger"] = (Rgb(0xE8562A), Rgb(0xFFFFFF), "Pure chaos with good intentions. Fastest paws in the house, first to the snacks, last to sit still."),
        ["ash"] = (Rgb(0x4F7A6B), Rgb(0xFFFFFF), "Wise and patient. Remembers where everything is and never loses his cool, even at 2 am."),
        ["sumi"] = (Rgb(0xE9E4D8), Rgb(0x141218), "Always dressed for a meeting. Punctual, tidy, and takes better notes than anyone in the room."),
        ["maple"] = (Rgb(0x7A3E22), Rgb(0xFFE9D2), "Stubborn, warm and fiercely loyal. Maple picks you, not the other way round."),
        ["biscuit"] = (Rgb(0xF2C14E), Rgb(0x2B1D08), "Soft, sunny and always a little hungry. Turns any bad day into a good nap."),
    };

    /// <summary>A cat's card colour, the ink on it and its nature (Home shows them too).</summary>
    public static (Color Fill, Color Ink, string Nature) Look(CatSkin skin) =>
        Looks.TryGetValue(skin.Id, out var look) ? look : (Color.FromRgb(0x14, 0x1A, 0x27), Color.FromRgb(0xE9, 0xED, 0xF3), skin.Description);

    static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    int Level => host.Level?.Invoke() ?? 1;
    CatStats Stats => host.Stats?.Invoke() ?? new CatStats(0, 0, 0, 0, 0, 0, 0, false, false);
    string Name => host.CatName();

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 56), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(k.Text("Cats", 34, font: DashKit.Pixel));
        var intro = k.Text($"Pick who keeps you company. New cats join as {Name} levels up: the tasks for each level are on the Levels page.", 15, P.Muted, wrap: true);
        intro.Margin = new Thickness(0, 8, 0, 26);
        page.Children.Add(intro);

        page.Children.Add(Now());

        page.Children.Add(Section("Your cats"));
        var grid = new UniformGrid { Columns = 3 };
        foreach (var skin in CatSprites.Skins) grid.Children.Add(Card(skin));
        // Narrow windows: two cards a row.
        grid.SizeChanged += (_, e) => grid.Columns = e.NewSize.Width < 820 ? 2 : 3;
        page.Children.Add(grid);

        page.Children.Add(Section("Things to wear"));
        page.Children.Add(Wears());

        if (openLevels != null)
        {
            var levels = k.Button("See every level and its tasks", openLevels, DashKit.Pill.Accent);
            levels.Margin = new Thickness(0, 30, 0, 0);
            page.Children.Add(levels);
        }
        return page;
    }

    TextBlock Section(string text)
    {
        var heading = k.Text(text, 24, font: DashKit.Pixel);
        heading.Margin = new Thickness(0, 40, 0, 16);
        return heading;
    }

    // ---- Where the cat is now ----

    UIElement Now()
    {
        int level = Level;
        var stats = Stats;
        var row = new DockPanel();
        var face = new Image { Source = CatSprites.Head(CatSprites.Dressed(CatSprites.Skin(host.Settings.Cat), host.Settings.Accessory)), Width = 48, Height = 48, Margin = new Thickness(0, 0, 16, 0) };
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        DockPanel.SetDock(face, Dock.Left);
        row.Children.Add(face);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(k.Text($"{Name} is level {level}: {CatLevel.Goal(level).Title}", 18, weight: FontWeights.Bold));
        if (level < CatLevel.Max)
        {
            var next = CatLevel.Goal(level + 1);
            int done = next.Quests.Count(q => q.Done(stats));
            var reward = CatLevel.NewAt(level + 1).FirstOrDefault();
            text.Children.Add(k.Text($"{done} of {next.Quests.Count} tasks done for level {level + 1}" + (reward != null ? $", which brings {reward.Name}." : "."), 14, P.Muted));
            var track = Bar(CatLevel.TowardNext(stats, level), DashPalette.Eye, 8);
            track.Margin = new Thickness(0, 12, 0, 0);
            text.Children.Add(track);
        }
        else text.Children.Add(k.Text("Every level reached. A true Shiki legend.", 14, P.Muted));
        row.Children.Add(text);
        return k.Card(row, new Thickness(24, 20, 24, 22));
    }

    static Grid Bar(double fraction, Color colour, double height)
    {
        var track = new Grid { Height = height };
        track.Children.Add(new Border { Background = DashKit.B(DashKit.A(Colors.Gray, 0x40)), CornerRadius = new CornerRadius(height / 2) });
        var fill = new Border { Background = DashKit.B(colour), CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(fraction, 0, 1);
        track.Children.Add(fill);
        return track;
    }

    // ---- One card per cat ----

    UIElement Card(CatSkin skin)
    {
        var (fill, ink, nature) = Looks.TryGetValue(skin.Id, out var look) ? look : (P.Card, P.Text, skin.Description);
        bool open = CatLevel.CatUnlocked(skin.Id, Level);
        bool chosen = host.Settings.Cat == skin.Id;
        int needs = CatLevel.UnlockLevel(skin.Id);

        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top: the name, and what it is (or how to get it).
        var top = new DockPanel();
        var badge = Badge(chosen ? "With you" : open ? "" : $"Level {needs}", ink, fill, chosen, !open);
        if (badge != null) { DockPanel.SetDock(badge, Dock.Right); top.Children.Add(badge); }
        var names = new StackPanel();
        names.Children.Add(k.Text(skin.Name, 30, ink, font: DashKit.Pixel));
        names.Children.Add(k.Text(skin.Description, 13, DashKit.A(ink, 0xB0), wrap: true));
        top.Children.Add(names);
        stack.Children.Add(top);

        // The cat itself, big.
        var cat = new Image
        {
            Source = CatSprites.Frame(skin, chosen ? CatPose.Happy : CatPose.Idle),
            Width = 20 * 8,
            Height = 20 * 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 18),
            Opacity = open ? 1 : 0.35,
        };
        RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.NearestNeighbor);
        Grid.SetRow(cat, 1);
        stack.Children.Add(cat);
        if (!open)
        {
            var lockIcon = k.Icon("\uE72E", 30, ink);
            Grid.SetRow(lockIcon, 1);
            stack.Children.Add(lockIcon);
        }

        // Bottom: its nature, and the way to pick it.
        var bottom = new StackPanel();
        bottom.Children.Add(k.Text(nature, 14, DashKit.A(ink, 0xE6), wrap: true));
        string action = chosen ? $"{skin.Name} is on your desktop" : open ? $"Choose {skin.Name}" : $"Reach level {needs} to meet {skin.Name}";
        var foot = k.Text(action, 13.5, ink, chosen || open ? FontWeights.Bold : FontWeights.SemiBold, wrap: true);
        foot.Margin = new Thickness(0, 14, 0, 0);
        bottom.Children.Add(foot);
        Grid.SetRow(bottom, 2);
        stack.Children.Add(bottom);

        var card = new Border
        {
            Background = DashKit.B(fill),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(22, 20, 22, 22),
            Margin = new Thickness(0, 0, 16, 16),
            MinHeight = 420,
            BorderThickness = new Thickness(3),
            BorderBrush = DashKit.B(chosen ? P.Accent : Colors.Transparent),
            Child = stack,
            Cursor = open && !chosen ? Cursors.Hand : Cursors.Arrow,
        };
        if (open && !chosen)
        {
            DashKit.Press(card);
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                host.SetCat(skin.Id);
                refresh();
            };
        }
        return card;
    }

    /// <summary>A small rounded label on the card: "With you" (solid) or the level that unlocks it.</summary>
    Border? Badge(string text, Color ink, Color fill, bool solid, bool locked)
    {
        if (text.Length == 0) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (locked)
        {
            var icon = k.Icon("\uE72E", 11, solid ? fill : ink);
            icon.Margin = new Thickness(0, 1, 6, 0);
            row.Children.Add(icon);
        }
        row.Children.Add(k.Text(text, 12.5, solid ? fill : ink, FontWeights.Bold));
        return new Border
        {
            Background = DashKit.B(solid ? ink : DashKit.A(ink, 0x1F)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 5),
            VerticalAlignment = VerticalAlignment.Top,
            Child = row,
        };
    }

    // ---- Things to wear ----

    UIElement Wears()
    {
        var s = host.Settings;
        var skin = CatSprites.Skin(s.Cat);
        var row = new WrapPanel();
        var options = new List<(string Id, string Name, ImageSource? Picture)> { ("", "Nothing", null) };
        options.AddRange(CatSprites.Accessories.Select(a => (a.Id, a.Name, (ImageSource?)CatSprites.Frame(CatSprites.Dressed(skin, a.Id), CatPose.Idle))));
        foreach (var (id, name, picture) in options)
        {
            bool open = id.Length == 0 || CatLevel.AccessoryUnlocked(id, Level);
            bool chosen = (s.Accessory ?? "") == id;
            var content = new StackPanel { Width = 104 };
            var art = new Grid { Height = 64 };
            if (picture != null)
            {
                var image = new Image { Source = picture, Width = 60, Height = 60, Opacity = open ? 1 : 0.3 };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                art.Children.Add(image);
            }
            else art.Children.Add(k.Icon("\uE711", 18, P.Faint));
            if (!open) art.Children.Add(k.Icon("\uE72E", 16, P.Text));
            content.Children.Add(art);
            var label = k.Text(name, 13, chosen ? P.Text : P.Muted, chosen ? FontWeights.Bold : FontWeights.Medium);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 6, 0, 0);
            content.Children.Add(label);
            if (!open)
            {
                var needs = k.Text($"Level {CatLevel.UnlockLevel(id)}", 12, P.Faint);
                needs.HorizontalAlignment = HorizontalAlignment.Center;
                content.Children.Add(needs);
            }
            var tile = new Border
            {
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(8, 12, 8, 12),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(2),
                BorderBrush = DashKit.B(chosen ? P.Accent : P.Line),
                Background = DashKit.B(P.Card),
                Cursor = open && !chosen ? Cursors.Hand : Cursors.Arrow,
                Child = content,
            };
            if (open && !chosen)
            {
                DashKit.Press(tile);
                tile.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    host.SetAccessory?.Invoke(id.Length == 0 ? null : id);
                    refresh();
                };
            }
            row.Children.Add(tile);
        }
        return row;
    }
}
