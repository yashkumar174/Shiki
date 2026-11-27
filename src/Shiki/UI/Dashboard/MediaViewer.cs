using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiki.Core.Phone;

namespace Shiki.UI.Dashboard;

/// <summary>
/// One photo or video from the phone, big, with its details beside it the way the phone's own
/// info panel shows them: the day and time, the camera and lens, the settings it was taken with,
/// its size, and where it was. Arrow keys step through the rest; Esc goes back to the gallery.
/// </summary>
public sealed class MediaViewer
{
    readonly DashKit k;
    readonly DashboardHost host;
    readonly IReadOnlyList<string> _paths;
    readonly Action _close;
    int _index;

    readonly Grid _root = new();
    readonly Image _picture = new() { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
    readonly Grid _stage = new();
    readonly Border _play;
    readonly TextBlock _counter;
    readonly ContentControl _details = new() { Focusable = false };
    readonly Border _prev, _next;
    int _shown = -1;

    DashPalette P => k.P;
    public FrameworkElement Root => _root;

    public MediaViewer(DashKit kit, DashboardHost host, IReadOnlyList<string> paths, int index, Action close)
    {
        k = kit;
        this.host = host;
        _paths = paths;
        _index = Math.Clamp(index, 0, paths.Count - 1);
        _close = close;

        _root.ColumnDefinitions.Add(new ColumnDefinition());
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        _root.Background = DashKit.B(P.Panel);

        // ---- Left: the bar, then the picture ----
        var left = new DockPanel { Margin = new Thickness(28, 22, 18, 24) };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var back = k.Button("Back to the gallery", close, DashKit.Pill.Quiet, "\uE72B");
        DockPanel.SetDock(back, Dock.Left);
        bar.Children.Add(back);
        // Right: where you are, Share, and the "i" that shows or hides the details.
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _counter = k.Text("", 14, P.Muted, FontWeights.SemiBold);
        _counter.VerticalAlignment = VerticalAlignment.Center;
        _counter.Margin = new Thickness(0, 0, 14, 0);
        tools.Children.Add(_counter);
        var share = k.Button("Share", () => { if (Window.GetWindow(_root) is { } w) Shell.ShareSheet.Share(w, [_paths[_index]]); }, DashKit.Pill.Quiet, "\uE72D");
        share.Margin = new Thickness(0, 0, 8, 0);
        tools.Children.Add(share);
        _infoToggle = new ContentControl { Focusable = false };
        tools.Children.Add(_infoToggle);
        bar.Children.Add(tools);
        DockPanel.SetDock(bar, Dock.Top);
        left.Children.Add(bar);

        var frame = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = DashKit.B(P.Dark ? Color.FromRgb(0x05, 0x07, 0x0C) : Color.FromRgb(0x10, 0x16, 0x22)),
            ClipToBounds = true,
            Child = _stage,
        };
        _stage.Children.Add(new Border { Padding = new Thickness(12), Child = _picture });
        _play = new Border
        {
            Width = 74,
            Height = 74,
            CornerRadius = new CornerRadius(37),
            Background = DashKit.B(DashKit.A(Colors.Black, 0x99)),
            BorderBrush = DashKit.B(DashKit.A(Colors.White, 0x55)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Play",
            Child = Centered(k.Icon("\uE768", 26, Colors.White), new Thickness(4, 0, 0, 0)),
        };
        DashKit.Press(_play);
        _play.MouseLeftButtonUp += (_, e) => { e.Handled = true; host.OpenFile(_paths[_index]); };
        _stage.Children.Add(_play);
        _prev = Arrow("\uE76B", HorizontalAlignment.Left, () => Step(-1));
        _next = Arrow("\uE76C", HorizontalAlignment.Right, () => Step(1));
        _stage.Children.Add(_prev);
        _stage.Children.Add(_next);
        // A double-click opens it in its own app (Photos, the player).
        frame.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) host.OpenFile(_paths[_index]); };
        left.Children.Add(frame);
        _root.Children.Add(left);

        // ---- Right: the details ----
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _details,
            Padding = new Thickness(0, 22, 24, 24),
        };
        Grid.SetColumn(scroll, 1);
        _root.Children.Add(scroll);
        _detailsPane = scroll;

        ShowDetails(DetailsOn);
        Show();
    }

    /// <summary>Whether the details sit beside the picture (kept from one look to the next, like the phone's "i").</summary>
    static bool DetailsOn = true;
    readonly ContentControl _infoToggle;
    ScrollViewer? _detailsPane;

    void ShowDetails(bool on)
    {
        DetailsOn = on;
        _root.ColumnDefinitions[1].Width = on ? new GridLength(400) : new GridLength(0);
        if (_detailsPane != null) _detailsPane.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var toggle = k.Button("Details", () => ShowDetails(!DetailsOn), on ? DashKit.Pill.Accent : DashKit.Pill.Quiet, "\uE946");
        toggle.ToolTip = on ? "Hide the details (I)" : "Show the details (I)";
        _infoToggle.Content = toggle;
    }

    /// <summary>Esc, and the arrow keys. True when it used the key.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape: _close(); return true;
            case Key.Left: Step(-1); return true;
            case Key.Right: Step(1); return true;
            case Key.Space or Key.Enter: host.OpenFile(_paths[_index]); return true;
            case Key.I: ShowDetails(!DetailsOn); return true;
        }
        return false;
    }

    void Step(int by)
    {
        int next = _index + by;
        if (next < 0 || next >= _paths.Count) return;
        _index = next;
        Show();
    }

    void Show()
    {
        if (_shown == _index) return;
        _shown = _index;
        int index = _index;
        var path = _paths[index];
        bool video = PhonePage.IsVideo(path);
        _counter.Text = $"{index + 1} of {_paths.Count}";
        _prev.Visibility = index > 0 ? Visibility.Visible : Visibility.Hidden;
        _next.Visibility = index < _paths.Count - 1 ? Visibility.Visible : Visibility.Hidden;
        _play.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        _details.Content = Waiting(path);

        // The small thumbnail at once (it's usually ready), then a big one.
        _picture.Source = PhonePage.CachedThumb(path);
        var ui = _root.Dispatcher;
        Task.Run(() =>
        {
            BitmapSource? big = null;
            try { big = PhonePage.ShellThumb(path, 1600); } catch { }
            if (big != null) ui.BeginInvoke(() => { if (_index == index) _picture.Source = big; });
        });
        Task.Run(async () =>
        {
            var info = await MediaInfo.ReadAsync(path);
            await ui.BeginInvoke(() => { if (_index == index) _details.Content = Details(info); });
        });
    }

    // ---- The details panel ----

    UIElement Waiting(string path)
    {
        var stack = new StackPanel();
        stack.Children.Add(k.Text(Path.GetFileName(path), 20, P.Text, FontWeights.Bold));
        var wait = k.Text("Reading its details…", 14, P.Muted);
        wait.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(wait);
        return stack;
    }

    UIElement Details(MediaInfo m)
    {
        var stack = new StackPanel();

        // When.
        if (m.Taken is { } when)
        {
            stack.Children.Add(k.Text(when.Date == DateTime.Today ? "Today" : when.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : when.ToString("dddd"), 26, P.Text, FontWeights.Bold));
            var date = k.Text($"{when:d MMMM yyyy} at {when:HH:mm}", 15, P.Muted);
            date.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(date);
        }

        // The name, and what kind of file.
        var name = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var chip = Chip(m.Format);
        DockPanel.SetDock(chip, Dock.Right);
        name.Children.Add(chip);
        var file = k.Text(Path.GetFileName(m.Path), 14, P.Muted, FontWeights.SemiBold);
        file.TextTrimming = TextTrimming.CharacterEllipsis;
        file.VerticalAlignment = VerticalAlignment.Center;
        name.Children.Add(file);
        stack.Children.Add(name);

        stack.Children.Add(m.Video ? VideoCard(m) : PhotoCard(m));
        if (m.HasPlace) stack.Children.Add(PlaceCard(m));
        stack.Children.Add(FileCard(m));
        return stack;
    }

    UIElement PhotoCard(MediaInfo m)
    {
        var body = new StackPanel();
        body.Children.Add(k.Text(m.Camera ?? "No camera details", 17, m.Camera != null ? P.Text : P.Muted, FontWeights.Bold));
        if (LensLine(m) is { } lens) body.Children.Add(Line(lens));
        var size = new List<string>();
        if (m.Width is { } w && m.Height is { } h)
        {
            double mp = w * (double)h / 1_000_000;
            if (mp >= 0.95) size.Add($"{Math.Round(mp):0} MP");
            size.Add($"{w:N0} × {h:N0}");
        }
        size.Add(PhonePage.Size(m.Bytes));
        body.Children.Add(Line(string.Join(",  ", size)));

        var cells = new List<(string, string)>();
        if (m.Iso is { } iso) cells.Add(($"ISO {iso}", "ISO"));
        if ((m.Focal35 ?? (m.FocalLength is { } real ? (int)Math.Round(real) : null)) is { } mm) cells.Add(($"{mm} mm", m.Focal35 != null ? "35 mm equivalent" : "Focal length"));
        if (m.ExposureBias is { } ev) cells.Add(($"{(ev > 0 ? "+" : "")}{ev.ToString("0.#", CultureInfo.InvariantCulture)} ev", "Exposure compensation"));
        if (m.FNumber is { } f) cells.Add(($"ƒ{f.ToString("0.0#", CultureInfo.InvariantCulture)}", "Aperture"));
        if (m.Exposure is { } s) cells.Add((Shutter(s), "Shutter speed"));
        return Card(body, cells, m.FlashFired == true ? "Flash fired" : null);
    }

    UIElement VideoCard(MediaInfo m)
    {
        var body = new StackPanel();
        body.Children.Add(k.Text(m.Camera ?? "Video", 17, P.Text, FontWeights.Bold));
        if (LensLine(m) is { } lens) body.Children.Add(Line(lens));
        var picture = new List<string>();
        if (m.Width is { } w && m.Height is { } h)
        {
            picture.Add(Quality(w, h));
            picture.Add($"{w:N0} × {h:N0}");
        }
        if (m.FrameRate is { } fps) picture.Add($"{Math.Round(fps):0} fps");
        if (picture.Count > 0) body.Children.Add(Line(string.Join(",  ", picture)));
        var stream = new List<string>();
        if (m.Codec != null) stream.Add(m.Codec);
        if (m.Bitrate is { } br) stream.Add($"{br / 1_000_000.0:0.#} Mb/s");
        if (m.Audio != null) stream.Add($"{m.Audio} sound");
        if (stream.Count > 0) body.Children.Add(Line(string.Join(",  ", stream)));

        var cells = new List<(string, string)>();
        if (m.Duration is { } d) cells.Add((Length(d), "Length"));
        cells.Add((PhonePage.Size(m.Bytes), "File size"));
        if (m.Width is { } vw && m.Height is { } vh) cells.Add((Quality(vw, vh), "Resolution"));
        if (m.FrameRate is { } rate) cells.Add(($"{Math.Round(rate):0} fps", "Frames per second"));
        if (m.Codec != null) cells.Add((m.Codec, "Video format"));
        return Card(body, cells, null);
    }

    UIElement PlaceCard(MediaInfo m)
    {
        double lat = m.Latitude!.Value, lon = m.Longitude!.Value;
        var body = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(k.Icon("\uE81D", 15, P.Accent));
        var title = k.Text("Where it was taken", 17, P.Text, FontWeights.Bold);
        title.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(title);
        body.Children.Add(head);
        string Deg(double v, string pos, string neg) => $"{Math.Abs(v).ToString("0.00000", CultureInfo.InvariantCulture)}° {(v >= 0 ? pos : neg)}";
        body.Children.Add(Line($"{Deg(lat, "N", "S")},  {Deg(lon, "E", "W")}"));
        if (m.Altitude is { } alt && Math.Abs(alt) > 0.5) body.Children.Add(Line($"{Math.Round(alt):0} m above sea level"));
        var map = k.Button("Show on a map", () => OpenUrl($"https://www.openstreetmap.org/?mlat={lat.ToString("0.000000", CultureInfo.InvariantCulture)}&mlon={lon.ToString("0.000000", CultureInfo.InvariantCulture)}#map=16/{lat.ToString("0.00000", CultureInfo.InvariantCulture)}/{lon.ToString("0.00000", CultureInfo.InvariantCulture)}"), DashKit.Pill.Quiet, "\uE774", 13);
        map.HorizontalAlignment = HorizontalAlignment.Left;
        map.Margin = new Thickness(0, 14, 0, 0);
        map.ToolTip = "Opens OpenStreetMap in your browser";
        body.Children.Add(map);
        var card = k.Card(body, new Thickness(20, 18, 20, 18));
        card.Margin = new Thickness(0, 14, 0, 0);
        return card;
    }

    UIElement FileCard(MediaInfo m)
    {
        var body = new StackPanel();
        body.Children.Add(k.Text("On this PC", 17, P.Text, FontWeights.Bold));
        DateTime arrived;
        try { arrived = File.GetCreationTime(m.Path); } catch { arrived = DateTime.Now; }
        body.Children.Add(Line($"Came from your phone on {arrived:d MMM} at {arrived:HH:mm}"));
        var where = Line(Path.GetDirectoryName(m.Path) ?? "");
        where.TextTrimming = TextTrimming.CharacterEllipsis;
        where.ToolTip = m.Path;
        body.Children.Add(where);
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var open = k.Button(m.Video ? "Play" : "Open", () => host.OpenFile(m.Path), DashKit.Pill.Accent, m.Video ? "\uE768" : "\uE8A7", 13);
        open.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(open);
        var folder = k.Button("Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{m.Path}\"") { UseShellExecute = true })?.Dispose(), DashKit.Pill.Quiet, "\uE838", 13);
        folder.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(folder);
        buttons.Children.Add(k.Button("Copy", () => Shell.ShareSheet.Copy([m.Path]), DashKit.Pill.Quiet, "\uE8C8", 13));
        body.Children.Add(buttons);
        var card = k.Card(body, new Thickness(20, 18, 20, 18));
        card.Margin = new Thickness(0, 14, 0, 0);
        return card;
    }

    /// <summary>A details card: the lines, then a strip of the numbers, side by side.</summary>
    Border Card(StackPanel body, List<(string Value, string Meaning)> cells, string? footnote)
    {
        var all = new StackPanel();
        body.Margin = new Thickness(20, 18, 20, 16);
        all.Children.Add(body);
        if (cells.Count > 0)
        {
            var strip = new UniformGrid { Rows = 1, Columns = cells.Count };
            for (int i = 0; i < cells.Count; i++)
            {
                var value = k.Text(cells[i].Value, 13.5, P.Text, FontWeights.SemiBold);
                value.HorizontalAlignment = HorizontalAlignment.Center;
                value.TextTrimming = TextTrimming.CharacterEllipsis;
                strip.Children.Add(new Border
                {
                    BorderBrush = DashKit.B(P.Line),
                    BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                    Padding = new Thickness(4, 11, 4, 12),
                    Background = Brushes.Transparent,
                    ToolTip = cells[i].Meaning,
                    Child = value,
                });
            }
            all.Children.Add(new Border { BorderBrush = DashKit.B(P.Line), BorderThickness = new Thickness(0, 1, 0, 0), Child = strip });
        }
        if (footnote != null)
        {
            var note = k.Text(footnote, 12.5, P.Faint);
            note.Margin = new Thickness(20, 0, 20, 12);
            all.Children.Add(note);
        }
        var card = k.Card(all, new Thickness(0));
        card.Margin = new Thickness(0, 18, 0, 0);
        card.ClipToBounds = true;
        return card;
    }

    TextBlock Line(string text)
    {
        var line = k.Text(text, 14, P.Muted, wrap: true);
        line.Margin = new Thickness(0, 6, 0, 0);
        return line;
    }

    Border Chip(string text) => new()
    {
        CornerRadius = new CornerRadius(7),
        Background = DashKit.B(P.Hover),
        BorderBrush = DashKit.B(P.Line),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 2, 8, 3),
        Margin = new Thickness(10, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = k.Text(text, 12, P.Muted, FontWeights.Bold),
    };

    Border Arrow(string glyph, HorizontalAlignment side, Action go)
    {
        var arrow = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = DashKit.B(DashKit.A(Colors.Black, 0x80)),
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            Cursor = Cursors.Hand,
            Child = Centered(k.Icon(glyph, 16, Colors.White), new Thickness(0)),
        };
        DashKit.Press(arrow);
        arrow.MouseLeftButtonUp += (_, e) => { e.Handled = true; go(); };
        return arrow;
    }

    static UIElement Centered(TextBlock icon, Thickness nudge)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = nudge;
        return icon;
    }

    /// <summary>"Back dual wide camera, 29 mm, ƒ1.5" (or as much of it as is known).</summary>
    static string? LensLine(MediaInfo m)
    {
        var parts = new List<string>();
        if (m.Lens != null) parts.Add(m.Lens);
        if (m.Video && m.Focal35 is { } mm) parts.Add($"{mm} mm");
        if (m.Lens != null && m.FNumber is { } f) parts.Add($"ƒ{f.ToString("0.0#", CultureInfo.InvariantCulture)}");
        return parts.Count == 0 ? null : string.Join(",  ", parts);
    }

    static string Shutter(double seconds) =>
        seconds >= 1 ? $"{seconds.ToString("0.#", CultureInfo.InvariantCulture)} s"
        : $"1/{Math.Round(1 / seconds):0} s";

    static string Quality(int w, int h)
    {
        int shortSide = Math.Min(w, h);
        return shortSide >= 2160 ? "4K" : shortSide >= 1440 ? "1440p" : shortSide >= 1080 ? "HD" : shortSide >= 720 ? "720p" : $"{shortSide}p";
    }

    static string Length(TimeSpan d)
    {
        d = TimeSpan.FromSeconds(Math.Max(1, Math.Round(d.TotalSeconds)));
        return d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss");
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); } catch { }
    }
}
