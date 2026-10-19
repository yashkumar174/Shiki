using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ConvertedBro.Core;

namespace ConvertedBro.UI.Dashboard;

/// <summary>
/// Home: a greeting, the black drop card (drop files on it, or pick some), the cat's numbers,
/// one helpful tip, and what was converted lately.
/// </summary>
public sealed class HomePage(DashKit k, DashboardHost host, Action seeAll, Action search)
{
    DashPalette P => k.P;

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 48), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
        var hello = k.Text(Greeting(), 30, weight: FontWeights.SemiBold, font: DashKit.Display);
        hello.Margin = new Thickness(0, 0, 0, 26);
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

    // ---- The black card: drop files here ----

    UIElement Hero()
    {
        var title = new TextBlock { FontFamily = DashKit.Serif, FontSize = 40, Foreground = DashKit.B(P.HeroText), TextWrapping = TextWrapping.Wrap };
        title.Inlines.Add(new System.Windows.Documents.Run("Convert anything, "));
        title.Inlines.Add(new System.Windows.Documents.Run("right here.") { FontStyle = FontStyles.Italic });
        var sub = k.Text("Drop files on this card, or pick some. Nothing leaves your PC.", 15.5, DashKit.A(Colors.White, 0xD8), wrap: true);
        sub.Margin = new Thickness(0, 8, 0, 22);
        var buttons = new WrapPanel();
        var choose = k.Button("Choose files", host.PickFiles, DashKit.Pill.Light);
        choose.Margin = new Thickness(0, 0, 10, 10);
        buttons.Children.Add(choose);
        var snip = k.Button("Snip the screen", host.Snip, DashKit.Pill.Outline, "\uE924");
        snip.Margin = new Thickness(0, 0, 0, 10);
        buttons.Children.Add(snip);
        var text = new StackPanel { Margin = new Thickness(40, 34, 12, 34), VerticalAlignment = VerticalAlignment.Center, Children = { title, sub, buttons } };

        // The cat, big, in a warm glow on the right.
        var glow = new Ellipse
        {
            Width = 340,
            Height = 340,
            Fill = new RadialGradientBrush(DashKit.A(P.Accent, 0x66), DashKit.A(P.Accent, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -40, -50, -40),
            IsHitTestVisible = false,
        };
        var cat = new Image
        {
            Source = CatSprites.Frame(CatSprites.Skin(host.Settings.Cat), CatPose.Happy),
            Width = 20 * 7,
            Height = 20 * 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 40, 0),
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.NearestNeighbor);

        // Text on the left, the cat in its own column on the right, so they never overlap.
        var layers = new Grid { ClipToBounds = true, MinHeight = 230 };
        layers.ColumnDefinitions.Add(new ColumnDefinition());
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        Grid.SetColumnSpan(glow, 2);
        layers.Children.Add(glow);
        Grid.SetColumn(cat, 1);
        layers.Children.Add(cat);
        layers.Children.Add(text);

        var card = new Border
        {
            Background = DashKit.B(P.Hero),
            BorderBrush = DashKit.B(P.Dark ? P.Line : P.Hero),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(22),
            Child = layers,
            AllowDrop = true,
        };
        layers.Clip = null;
        card.Loaded += (_, _) => layers.Clip = new RectangleGeometry(new Rect(0, 0, card.ActualWidth - 3, card.ActualHeight - 3), 21, 21);
        card.SizeChanged += (_, _) => layers.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, card.ActualWidth - 3), Math.Max(0, card.ActualHeight - 3)), 21, 21);

        // Dropping: the edge lights up orange, and letting go opens the wheel at the mouse.
        card.DragEnter += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; card.BorderBrush = DashKit.B(P.Accent); e.Handled = true; };
        card.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        card.DragLeave += (_, _) => card.BorderBrush = DashKit.B(P.Dark ? P.Line : P.Hero);
        card.Drop += (_, e) =>
        {
            card.BorderBrush = DashKit.B(P.Dark ? P.Line : P.Hero);
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
        var card = k.Card(stack, new Thickness(0), 22, P.Dark ? P.Card : Color.FromRgb(0xF5, 0xF3, 0xEF));
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    UIElement Stat(string number, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var big = k.Text(number, 34, font: DashKit.Serif);
        var small = k.Text(label, 16, P.Text);
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
            !host.ExplorerMenuOn() ? ("Right-click to convert", "Add Converted Bro to Explorer's right-click menu.", "Add it", host.AddExplorerMenu)
            : !s.UsedConvertHotkey ? ("The fastest way in", $"Select a file in File Explorer and press {s.ConvertHotkey}.", "Got it", () => { s.UsedConvertHotkey = true; host.SaveSettings(); })
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
