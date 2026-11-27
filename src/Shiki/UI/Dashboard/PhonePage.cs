using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiki.UI.Dashboard;

/// <summary>
/// What the phone has sent: every photo and video in Pictures\From phone, newest first, grouped by
/// the day it arrived, as a grid of thumbnails. Clicking one opens it; the page redraws itself
/// when more come in, and the newest wear a "New" badge for a while.
/// </summary>
public sealed class PhonePage(DashKit k, DashboardHost host)
{
    DashPalette P => k.P;

    static readonly HashSet<string> Photos = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".dng", ".raw", ".bmp" };
    static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".3gp", ".mkv", ".webm", ".m4v" };

    /// <summary>How many tiles one page shows; the rest are a click away in the folder.</summary>
    const int Most = 240;
    /// <summary>Arrived in the last this-many minutes: wears "New".</summary>
    const int NewFor = 15;
    const double Gap = 12;

    string PhotoFolder => Core.Phone.PhoneLink.Current?.PhotoFolder
        ?? host.Settings.PhoneFolder
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "From phone");

    static string FileFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "From phone");

    readonly List<(UniformGrid Grid, List<Border> Tiles)> _days = [];
    /// <summary>What the gallery shows, in order (the viewer steps through them).</summary>
    List<string> _shown = [];

    // Choosing several: Select (or Ctrl+click) puts a ring on every tile; the bar floats at the bottom.
    bool _selecting;
    readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Action> _tileLooks = [];
    Action? _barUpdate;
    Action? _selectButtonUpdate;

    /// <summary>Choosing photos right now (the dashboard doesn't redraw the page under you).</summary>
    public bool Selecting => _selecting;

    internal static bool IsVideo(string path) => Videos.Contains(Path.GetExtension(path));

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(48, 40, 48, 56), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
        var items = Media(PhotoFolder);
        var others = Files(FileFolder);
        _shown = items.Take(Most).Select(i => i.Path).ToList();
        page.Children.Add(Header(items));
        page.Children.Add(Connection());
        if (items.Count == 0)
        {
            page.Children.Add(Empty());
        }
        else
        {
            foreach (var day in items.Take(Most).GroupBy(i => i.Arrived.Date))
            {
                var label = k.Text(DayName(day.Key), 18, P.Text, FontWeights.Bold);
                label.Margin = new Thickness(2, 30, 0, 12);
                var count = k.Text($"  {Count(day.ToList())}", 14, P.Muted, FontWeights.Medium);
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Children = { label } };
                count.VerticalAlignment = VerticalAlignment.Bottom;
                count.Margin = new Thickness(0, 0, 0, 13);
                heading.Children.Add(count);
                page.Children.Add(heading);
                var grid = new UniformGrid { Columns = 5 };
                var tiles = day.Select(Tile).ToList();
                foreach (var tile in tiles) grid.Children.Add(tile);
                _days.Add((grid, tiles));
                page.Children.Add(grid);
            }
            if (items.Count > Most)
            {
                var more = k.Text($"And {items.Count - Most} older ones in the folder.", 14, P.Muted);
                more.Margin = new Thickness(2, 24, 0, 0);
                page.Children.Add(more);
            }
        }
        if (others.Count > 0) page.Children.Add(OtherFiles(others));

        // Columns follow the width: tiles about 210 wide, square.
        page.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        return page;
    }

    void Fit(double width)
    {
        if (width <= 0) return;
        int columns = Math.Max(2, (int)Math.Floor((width + Gap) / (210 + Gap)));
        double side = (width - Gap * (columns - 1)) / columns;
        foreach (var (grid, tiles) in _days)
        {
            grid.Columns = columns;
            grid.Margin = new Thickness(0, 0, -Gap, 0);
            foreach (var tile in tiles)
            {
                tile.Width = side;
                tile.Height = side;
            }
        }
    }

    // ---- The top ----

    UIElement Header(List<Item> items)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(k.Text("From your phone", 40, font: DashKit.Pixel));
        string line = items.Count == 0
            ? "What your phone sends shows up here, full quality."
            : $"{Count(items)}, {Size(items.Sum(i => i.Bytes))}, each checked byte for byte when it arrived.";
        var intro = k.Text(line, 15, P.Muted, wrap: true);
        intro.Margin = new Thickness(0, 8, 0, 0);
        intro.MaxWidth = 760;
        intro.HorizontalAlignment = HorizontalAlignment.Left;
        left.Children.Add(intro);
        grid.Children.Add(left);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 2) };
        if (items.Count > 0)
        {
            var select = new ContentControl { Focusable = false, Margin = new Thickness(0, 0, 8, 0) };
            _selectButtonUpdate = () => select.Content = _selecting
                ? k.Button("Cancel", () => Choose(false), DashKit.Pill.Quiet, "\uE711")
                : k.Button("Select", () => Choose(true), DashKit.Pill.Quiet, "\uE762");
            _selectButtonUpdate();
            buttons.Children.Add(select);
        }
        buttons.Children.Add(k.Button("Open folder", () => OpenFolder(PhotoFolder), DashKit.Pill.Quiet, "\uE838"));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    // ---- The phone's connection ----

    /// <summary>
    /// Is the phone there right now? A dot (moonlight: connected, amber: away, grey: none yet),
    /// what that means, and one button: connect, scan the code again, or connect another phone.
    /// It keeps itself up to date while the page is open.
    /// </summary>
    UIElement Connection()
    {
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 16, 0) };
        var title = k.Text("", 17, P.Text, FontWeights.Bold);
        var sub = k.Text("", 14, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 4, 0, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };
        var button = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Focusable = false };
        var row = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(dot);
        row.Children.Add(button);
        row.Children.Add(text);
        var gate = Gate();
        var card = k.Card(new StackPanel { Children = { row, gate.View } }, new Thickness(22, 18, 20, 18));
        card.Margin = new Thickness(0, 26, 0, 0);

        string? shown = null;
        void Update()
        {
            var link = Core.Phone.PhoneLink.Current;
            var here = link?.Connected();
            var phone = here ?? link?.Phones.OrderByDescending(f => f.LastSeen ?? f.Paired).FirstOrDefault();
            string state = here != null ? "here" : phone != null ? "away" : "none";
            Color colour = here != null ? P.Accent : phone != null ? DashPalette.Eye : P.Faint;
            dot.Background = DashKit.B(colour);
            dot.Effect = here != null ? new System.Windows.Media.Effects.DropShadowEffect { Color = colour, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 } : null;
            if (phone == null)
            {
                title.Text = "No phone connected yet";
                sub.Text = "Connect once by scanning a code with Shiki on your phone. After that it connects by itself whenever the app is open, even when this PC's address changes.";
            }
            else if (here != null)
            {
                title.Text = $"{phone.Name} is connected";
                sub.Text = $"Heard from {Ago(phone.LastSeen)}{(phone.LastIp is { } ip ? $" at {ip}" : "")}. Send from the Shiki app, or ask here: \"get my last photo from my phone\".";
            }
            else
            {
                title.Text = $"{phone.Name} is away";
                sub.Text = $"Last seen {Ago(phone.LastSeen)}. Open Shiki on the phone, on the same Wi-Fi or this PC on its hotspot: it finds this PC by itself, even at a new address. Not connecting? Scan the code again.";
            }
            if (state == shown || host.ConnectPhone == null) return;
            shown = state;
            button.Content = state switch
            {
                "none" => k.Button("Connect your phone", host.ConnectPhone, DashKit.Pill.Accent, "\uE8EA"),
                "away" => k.Button("Scan the code again", host.ConnectPhone, DashKit.Pill.Accent, "\uED14"),
                _ => k.Button("Connect another phone", host.ConnectPhone, DashKit.Pill.Quiet, "\uE710"),
            };
        }
        Update();
        int ticks = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            Update();
            // The network can change under it (home Wi-Fi to a hotspot): look at the firewall again now and then.
            if (++ticks % 10 == 0) gate.Recheck();
        };
        card.Loaded += (_, _) => { timer.Start(); gate.Recheck(); };
        card.Unloaded += (_, _) => timer.Stop();
        return card;
    }

    /// <summary>
    /// Under the connection: shown only when Windows Firewall would keep the phone out, with one
    /// button that asks Windows to let it in.
    /// </summary>
    (UIElement View, Action Recheck) Gate()
    {
        var title = k.Text("Windows Firewall is keeping your phone out", 15, DashPalette.Eye, FontWeights.Bold);
        var sub = k.Text("", 13.5, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 4, 0, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };
        bool busy = false, isPublic = false;
        var view = new Border
        {
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(30, 16, 0, 0),
            Padding = new Thickness(0, 14, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        Action recheck = null!;
        var button = k.Button("Let your phone in", () =>
        {
            if (busy) return;
            busy = true;
            sub.Text = "Press Yes when Windows asks.";
            _ = Ask();
        }, DashKit.Pill.Accent, "\uE83D");
        async Task Ask()
        {
            var problem = await Shell.Firewall.LetIn(isPublic);
            busy = false;
            if (problem != null) { sub.Text = problem; return; }
            recheck();
        }
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(16, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(text);
        view.Child = row;

        recheck = () => _ = Task.Run(Shell.Firewall.Look).ContinueWith(t =>
        {
            if (busy) return;
            var look = t.Result;
            isPublic = look.PublicNetwork;
            view.Visibility = look.Verdict == Shell.Firewall.Verdict.Closed ? Visibility.Visible : Visibility.Collapsed;
            sub.Text = look.PublicNetwork
                ? "Windows calls this network public, and Shiki isn't allowed in on it, so your phone can't reach this PC. This lets Shiki in on home and public networks. Windows asks you once."
                : "Shiki isn't allowed in on this network, so your phone can't reach this PC. This lets Shiki in on home networks. Windows asks you once.";
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return (view, () => recheck());
    }

    static string Ago(DateTime? when)
    {
        if (when is not { } t) return "not yet";
        var gone = DateTime.Now - t;
        return gone.TotalSeconds < 60 ? "just now"
            : gone.TotalMinutes < 60 ? $"{(int)gone.TotalMinutes} min ago"
            : gone.TotalHours < 24 ? $"{(int)gone.TotalHours} h ago"
            : t.ToString("d MMM, HH:mm");
    }

    UIElement Empty()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) };
        var cat = new Image
        {
            Source = CatSprites.Frame(CatSprites.Skin(host.Settings.Cat), CatPose.Idle),
            Width = 20 * 7,
            Height = 20 * 7,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.NearestNeighbor);
        stack.Children.Add(cat);
        var title = k.Text("Nothing from your phone yet", 24, P.Text, font: DashKit.Pixel);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 18, 0, 8);
        stack.Children.Add(title);
        var tip = k.Text("Open Shiki on your phone and tap Send photos and videos. They show up here the moment they land.", 15, P.Muted, wrap: true);
        tip.TextAlignment = TextAlignment.Center;
        tip.MaxWidth = 460;
        stack.Children.Add(tip);
        return stack;
    }

    UIElement OtherFiles(List<Item> files)
    {
        var row = new Grid { Margin = new Thickness(0, 34, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(k.Text(files.Count == 1 ? "1 other file" : $"{files.Count} other files", 16, P.Text, FontWeights.Bold));
        var names = string.Join(", ", files.Take(4).Select(f => Path.GetFileName(f.Path))) + (files.Count > 4 ? "…" : "");
        var sub = k.Text($"In Downloads\\From phone: {names}", 14, P.Muted);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        sub.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(sub);
        row.Children.Add(text);
        var open = k.Button("Open", () => OpenFolder(FileFolder));
        open.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(open, 1);
        row.Children.Add(open);
        return k.Card(row, new Thickness(22, 18, 22, 18));
    }

    // ---- A tile ----

    Border Tile(Item item)
    {
        bool video = Videos.Contains(Path.GetExtension(item.Path));
        var layers = new Grid();
        // Until the thumbnail is ready (or if there isn't one): the kind of file, quietly.
        var stand = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stand.Children.Add(k.Icon(video ? "\uE714" : "\uE91B", 26, P.Faint));
        var ext = k.Text(Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant(), 12, P.Faint, FontWeights.SemiBold);
        ext.HorizontalAlignment = HorizontalAlignment.Center;
        ext.Margin = new Thickness(0, 6, 0, 0);
        stand.Children.Add(ext);
        layers.Children.Add(stand);

        if (video)
        {
            var play = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = DashKit.B(DashKit.A(Colors.Black, 0x88)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10),
                Child = k.Icon("\uE768", 13, Colors.White),
            };
            layers.Children.Add(play);
        }
        if (DateTime.Now - item.Arrived < TimeSpan.FromMinutes(NewFor))
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = DashKit.B(P.Accent),
                Padding = new Thickness(9, 3, 9, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10),
                Child = k.Text("New", 12, P.AccentText, FontWeights.Bold),
            };
            layers.Children.Add(badge);
        }

        var tile = new Border
        {
            Width = 200,
            Height = 200,
            Margin = new Thickness(0, 0, Gap, Gap),
            CornerRadius = new CornerRadius(16),
            Background = DashKit.B(P.Card),
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = layers,
            ToolTip = $"{Path.GetFileName(item.Path)}\n{Size(item.Bytes)}, {item.Arrived:d MMM, HH:mm}",
        };
        DashKit.Press(tile);

        // Top right: an "i" (on hover) for the details; while choosing, a ring that fills when picked.
        var info = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = DashKit.B(DashKit.A(Colors.Black, 0x8C)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(9),
            Cursor = Cursors.Hand,
            ToolTip = "Details",
            Visibility = Visibility.Collapsed,
            Child = Middle(k.Icon("\uE946", 14, Colors.White)),
        };
        info.MouseLeftButtonUp += (_, e) => { e.Handled = true; View(item.Path); };
        layers.Children.Add(info);
        var tick = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            BorderBrush = DashKit.B(Colors.White),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(11),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = Middle(k.Icon("\uE73E", 13, P.AccentText)),
        };
        layers.Children.Add(tick);
        var ring = new Border { CornerRadius = new CornerRadius(15), BorderBrush = DashKit.B(P.Accent), BorderThickness = new Thickness(3), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        layers.Children.Add(ring);
        void Look()
        {
            bool picked = _picked.Contains(item.Path);
            info.Visibility = !_selecting && tile.IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
            tick.Visibility = _selecting ? Visibility.Visible : Visibility.Collapsed;
            tick.Background = picked ? DashKit.B(P.Accent) : DashKit.B(DashKit.A(Colors.Black, 0x40));
            tick.BorderBrush = DashKit.B(picked ? P.Accent : Colors.White);
            tick.Child.Visibility = picked ? Visibility.Visible : Visibility.Hidden;
            ring.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        }
        _tileLooks.Add(Look);
        tile.MouseEnter += (_, _) => Look();
        tile.MouseLeave += (_, _) => Look();
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_selecting || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (!_selecting) Choose(true);
                if (!_picked.Remove(item.Path)) _picked.Add(item.Path);
                Look();
                _barUpdate?.Invoke();
                return;
            }
            View(item.Path);
        };
        var menu = new ContextMenu();
        var details = new MenuItem { Header = "Details" };
        details.Click += (_, _) => View(item.Path);
        menu.Items.Add(details);
        var shareIt = new MenuItem { Header = "Share" };
        shareIt.Click += (_, _) => { if (Window.GetWindow(tile) is { } w) Shell.ShareSheet.Share(w, [item.Path]); };
        menu.Items.Add(shareIt);
        var copyIt = new MenuItem { Header = "Copy" };
        copyIt.Click += (_, _) => Shell.ShareSheet.Copy([item.Path]);
        menu.Items.Add(copyIt);
        var openIt = new MenuItem { Header = video ? "Play" : "Open" };
        openIt.Click += (_, _) => host.OpenFile(item.Path);
        menu.Items.Add(openIt);
        var show = new MenuItem { Header = "Show in folder" };
        show.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true })?.Dispose();
        menu.Items.Add(show);
        tile.ContextMenu = menu;

        Thumb(item.Path, image =>
        {
            stand.Visibility = Visibility.Collapsed;
            tile.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        });
        return tile;
    }

    static UIElement Middle(TextBlock icon)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    void View(string path)
    {
        // The details view, like the phone's; without one (an older host), just open it.
        if (host.ShowViewer is { } view) view(_shown, Math.Max(0, _shown.IndexOf(path)));
        else host.OpenFile(path);
    }

    // ---- Choosing several ----

    void Choose(bool on)
    {
        _selecting = on;
        if (!on) _picked.Clear();
        foreach (var look in _tileLooks) look();
        _selectButtonUpdate?.Invoke();
        _barUpdate?.Invoke();
    }

    /// <summary>Dev aid: choose the first few, as if clicked.</summary>
    public void PickFirst(int count)
    {
        Choose(true);
        foreach (var p in _shown.Take(count)) _picked.Add(p);
        foreach (var look in _tileLooks) look();
        _barUpdate?.Invoke();
    }

    /// <summary>The bar that floats over the bottom of the page while choosing: how many, and what to do with them.</summary>
    public UIElement Floating()
    {
        var count = k.Text("", 15, P.Text, FontWeights.Bold);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(6, 0, 12, 0);
        var note = k.Text("", 13.5, P.Muted);
        note.VerticalAlignment = VerticalAlignment.Center;
        note.Margin = new Thickness(0, 0, 16, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { count, note, actions } };
        var bar = new Border
        {
            Background = DashKit.B(P.Card),
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(16, 9, 9, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24, 0, 24, 26),
            Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = P.Dark ? 0.55 : 0.18, Color = Colors.Black },
            Child = row,
        };
        bool confirming = false;

        void Add(string label, Action go, DashKit.Pill kind, string glyph)
        {
            var button = k.Button(label, go, kind, glyph, 13);
            button.Margin = new Thickness(0, 0, 6, 0);
            actions.Children.Add(button);
        }
        List<string> Picked() => _shown.Where(_picked.Contains).ToList();

        void Update()
        {
            bar.Visibility = _selecting ? Visibility.Visible : Visibility.Collapsed;
            actions.Children.Clear();
            note.Text = "";
            int n = _picked.Count;
            if (confirming && n > 0)
            {
                count.Text = n == 1 ? "Move this one to the Recycle Bin?" : $"Move these {n} to the Recycle Bin?";
                note.Text = "You can put them back from there.";
                Add("Move", () =>
                {
                    var gone = Shell.ShareSheet.Recycle(Picked());
                    confirming = false;
                    Choose(false);
                    if (gone > 0 && Window.GetWindow(bar) is DashboardWindow dash) dash.PhoneFilesChanged();
                }, DashKit.Pill.Accent, "\uE74D");
                Add("Keep them", () => { confirming = false; Update(); }, DashKit.Pill.Quiet, "\uE711");
                return;
            }
            confirming = false;
            count.Text = n == 0 ? "Choose photos and videos" : n == 1 ? "1 selected" : $"{n} selected";
            if (n > 0) note.Text = Size(Picked().Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } }));
            bool all = n == _shown.Count;
            Add(all ? "Select none" : "Select all", () =>
            {
                if (all) _picked.Clear(); else foreach (var p in _shown) _picked.Add(p);
                foreach (var look in _tileLooks) look();
                Update();
            }, DashKit.Pill.Quiet, all ? "\uE8E6" : "\uE8B3");
            if (n > 0)
            {
                Add("Share", () => { if (Window.GetWindow(bar) is { } w && !Shell.ShareSheet.Share(w, Picked())) note.Text = "Windows couldn't open Share."; }, DashKit.Pill.Accent, "\uE72D");
                Add("Copy", () => { if (Shell.ShareSheet.Copy(Picked())) note.Text = "Copied. Paste them anywhere."; }, DashKit.Pill.Quiet, "\uE8C8");
                if (n == 1) Add("Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Picked()[0]}\"") { UseShellExecute = true })?.Dispose(), DashKit.Pill.Quiet, "\uE838");
                Add("Delete", () => { confirming = true; Update(); }, DashKit.Pill.Quiet, "\uE74D");
            }
            Add("Done", () => Choose(false), DashKit.Pill.Quiet, "\uE73E");
        }
        _barUpdate = Update;
        Update();
        return bar;
    }

    // ---- Thumbnails: Windows' own (so HEIC and videos have one too), made off the UI thread ----

    static readonly ConcurrentDictionary<string, (DateTime Stamp, BitmapSource Image)> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly System.Threading.SemaphoreSlim Makers = new(3);

    /// <summary>The gallery's thumbnail of it, if it's been made.</summary>
    internal static BitmapSource? CachedThumb(string path) => Cache.TryGetValue(path, out var hit) ? hit.Image : null;

    static void Thumb(string path, Action<BitmapSource> done)
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { return; }
        if (Cache.TryGetValue(path, out var hit) && hit.Stamp == stamp) { done(hit.Image); return; }
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Task.Run(async () =>
        {
            await Makers.WaitAsync();
            try
            {
                var image = ShellThumb(path, 420);
                if (image == null) return;
                Cache[path] = (stamp, image);
                await ui.BeginInvoke(() => done(image));
            }
            catch { /* no thumbnail: the tile keeps its quiet stand-in */ }
            finally { Makers.Release(); }
        });
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);

    internal static BitmapSource? ShellThumb(string path, int size)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        try
        {
            // SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY: a real thumbnail, never just the file's icon.
            if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x8, out var bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            try
            {
                var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally { DeleteObject(bitmap); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    // ---- The files ----

    record Item(string Path, DateTime Arrived, long Bytes);

    static List<Item> Media(string folder) => List(folder, f => Photos.Contains(System.IO.Path.GetExtension(f)) || Videos.Contains(System.IO.Path.GetExtension(f)));
    static List<Item> Files(string folder) => List(folder, _ => true);

    static List<Item> List(string folder, Func<string, bool> keep)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            return new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => keep(f.Name) && !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && (f.Attributes & FileAttributes.Hidden) == 0)
                // Saved files keep the time they were taken; the time they were made here is when they arrived.
                .Select(f => new Item(f.FullName, f.CreationTime, f.Length))
                .OrderByDescending(i => i.Arrived)
                .ToList();
        }
        catch { return []; }
    }

    static string Count(List<Item> items)
    {
        int videos = items.Count(i => Videos.Contains(Path.GetExtension(i.Path)));
        int photos = items.Count - videos;
        var parts = new List<string>();
        if (photos > 0) parts.Add(photos == 1 ? "1 photo" : $"{photos} photos");
        if (videos > 0) parts.Add(videos == 1 ? "1 video" : $"{videos} videos");
        return string.Join(" and ", parts);
    }

    /// <summary>Sizes the way the iPhone (and the Mac) count them, 1 MB = 1,000,000 bytes, so the numbers match the phone's.</summary>
    internal static string Size(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1e9:0.0#} GB",
        >= 100_000_000 => $"{bytes / 1e6:0} MB",
        >= 1_000_000 => $"{bytes / 1e6:0.#} MB",
        _ => $"{Math.Max(1, bytes / 1000)} KB",
    };

    static string DayName(DateTime day) =>
        day == DateTime.Today ? "Today"
        : day == DateTime.Today.AddDays(-1) ? "Yesterday"
        : day.Year == DateTime.Today.Year ? day.ToString("dddd, d MMMM")
        : day.ToString("d MMMM yyyy");

    static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
        }
        catch { }
    }
}
