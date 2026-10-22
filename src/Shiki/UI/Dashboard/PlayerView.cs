using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>
/// Plays a video or audio file inside the dashboard: a black card for the picture, a slim control
/// row under it (play, seek, time, volume, speed, full screen) and the usual Open / Copy / Folder /
/// Convert. Uses Windows' own media engine, so it plays what Windows plays (MP4, MOV, MKV, MP3…).
/// </summary>
public sealed class PlayerView
{
    static double _volume = 0.8;
    static bool _muted;

    readonly DashKit _k;
    readonly DashboardHost _host;
    readonly Action _back, _toggleFullscreen;
    readonly MediaElement _media = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly TextBlock _time, _duration, _playGlyph, _muteGlyph, _speedText, _message;
    readonly Border _seekTrack = new() { Height = 6, CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand };
    readonly Border _seekFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    readonly Border _seekThumb = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    readonly Border _volumeTrack = new() { Height = 4, Width = 84, CornerRadius = new CornerRadius(2), Cursor = Cursors.Hand };
    readonly Border _volumeFill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    readonly Border _bigPlay;
    readonly bool _audioOnly;
    bool _playing, _seeking, _ended;
    double _speed = 1;

    public string Path { get; }
    public FrameworkElement Root { get; }
    public bool Fullscreen { get; set; }

    public static bool CanPlay(string path) => FileKinds.Of(path) is FileKind.Video or FileKind.Audio || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    public PlayerView(DashKit k, DashboardHost host, string path, Action back, Action toggleFullscreen)
    {
        _k = k;
        _host = host;
        Path = path;
        _back = back;
        _toggleFullscreen = toggleFullscreen;
        _audioOnly = FileKinds.Of(path) == FileKind.Audio;
        var p = k.P;

        // ---- Header: back, name, actions ----
        var head = new DockPanel { Margin = new Thickness(18, 14, 22, 6) };
        var backButton = k.IconButton("\uE72B", back, "Back (Esc)", 14, p.Muted);
        DockPanel.SetDock(backButton, Dock.Left);
        head.Children.Add(backButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(k.IconButton("\uE8A7", () => Launch(path, null), "Open in your usual player", 14, p.Muted));
        actions.Children.Add(k.IconButton("\uE8C8", () => { ClipboardTools.CopyFiles([path]); ToastHost.Show(ToastWindow.Message("Copied", System.IO.Path.GetFileName(path))); }, "Copy", 14, p.Muted));
        actions.Children.Add(k.IconButton("\uE838", () => Launch("explorer.exe", $"/select,\"{path}\""), "Show in folder", 14, p.Muted));
        var convert = k.Button("Convert…", () => { Pause(); host.Convert([path]); }, DashKit.Pill.Quiet, "\uE895", 13);
        convert.Padding = new Thickness(14, 7, 14, 8);
        convert.Margin = new Thickness(8, 0, 0, 0);
        convert.ToolTip = "GIF, compress, trim the sound and more";
        actions.Children.Add(convert);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var title = k.Text(System.IO.Path.GetFileName(path), 15, weight: FontWeights.SemiBold);
        title.Margin = new Thickness(8, 0, 12, 1);
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);

        // ---- The picture ----
        _message = k.Text("", 15, Colors.White, wrap: true);
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.TextAlignment = TextAlignment.Center;
        _message.MaxWidth = 420;
        _message.Visibility = Visibility.Collapsed;
        _bigPlay = new Border
        {
            Width = 76,
            Height = 76,
            CornerRadius = new CornerRadius(38),
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new TextBlock { Text = "\uE768", FontFamily = DashKit.Icons, FontSize = 26, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) },
        };
        var stage = new Grid { Background = Brushes.Black, Cursor = Cursors.Hand };
        stage.Children.Add(_media);
        if (_audioOnly) stage.Children.Add(AudioArt());
        stage.Children.Add(_bigPlay);
        stage.Children.Add(_message);
        stage.MouseLeftButtonUp += (_, e) => { if (e.ClickCount == 1) TogglePlay(); };
        stage.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) _toggleFullscreen(); };
        var picture = new Border { CornerRadius = new CornerRadius(18), Background = Brushes.Black, Margin = new Thickness(22, 4, 22, 0), Child = stage };
        picture.SizeChanged += (_, _) => stage.Clip = new RectangleGeometry(new Rect(0, 0, picture.ActualWidth, picture.ActualHeight), 18, 18);

        // ---- Controls ----
        _playGlyph = new TextBlock { Text = "\uE768", FontFamily = DashKit.Icons, FontSize = 16, Foreground = DashKit.B(p.Text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var play = Circle(_playGlyph, TogglePlay, "Play / pause (Space)");
        _time = k.Text("0:00", 13.5, p.Muted);
        _duration = k.Text("0:00", 13.5, p.Muted);
        foreach (var t in new[] { _time, _duration }) { t.VerticalAlignment = VerticalAlignment.Center; t.MinWidth = 44; }
        _time.TextAlignment = TextAlignment.Right;

        _seekTrack.Background = DashKit.B(p.Selected);
        _seekFill.Background = DashKit.B(p.Accent);
        _seekThumb.Background = DashKit.B(p.Accent);
        _seekThumb.BorderBrush = DashKit.B(p.Card);
        _seekThumb.BorderThickness = new Thickness(2);
        var seek = new Grid { Height = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _seekTrack.VerticalAlignment = _seekFill.VerticalAlignment = _seekThumb.VerticalAlignment = VerticalAlignment.Center;
        seek.Children.Add(_seekTrack);
        seek.Children.Add(_seekFill);
        seek.Children.Add(_seekThumb);
        seek.MouseLeftButtonDown += (_, e) => { _seeking = true; seek.CaptureMouse(); SeekTo(e.GetPosition(seek).X / seek.ActualWidth); };
        seek.MouseMove += (_, e) => { if (_seeking) SeekTo(e.GetPosition(seek).X / seek.ActualWidth); };
        seek.MouseLeftButtonUp += (_, _) => { _seeking = false; seek.ReleaseMouseCapture(); };
        seek.SizeChanged += (_, _) => PaintSeek();

        _muteGlyph = new TextBlock { FontFamily = DashKit.Icons, FontSize = 15, Foreground = DashKit.B(p.Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mute = Square(_muteGlyph, ToggleMute, "Mute (M)");
        _volumeTrack.Background = DashKit.B(p.Selected);
        _volumeFill.Background = DashKit.B(p.Muted);
        var volume = new Grid { Width = 84, Height = 20, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 10, 0) };
        _volumeTrack.VerticalAlignment = _volumeFill.VerticalAlignment = VerticalAlignment.Center;
        volume.Children.Add(_volumeTrack);
        volume.Children.Add(_volumeFill);
        bool dragging = false;
        void SetVolumeAt(double x) { _volume = Math.Clamp(x / volume.ActualWidth, 0, 1); _muted = false; ApplyVolume(); }
        volume.MouseLeftButtonDown += (_, e) => { dragging = true; volume.CaptureMouse(); SetVolumeAt(e.GetPosition(volume).X); };
        volume.MouseMove += (_, e) => { if (dragging) SetVolumeAt(e.GetPosition(volume).X); };
        volume.MouseLeftButtonUp += (_, _) => { dragging = false; volume.ReleaseMouseCapture(); };

        _speedText = k.Text("1×", 13, p.Text, FontWeights.SemiBold);
        _speedText.HorizontalAlignment = HorizontalAlignment.Center;
        _speedText.VerticalAlignment = VerticalAlignment.Center;
        var speed = Square(_speedText, NextSpeed, "Speed: 1×, 1.5×, 2×, 0.5×", 44);
        var full = Square(new TextBlock { Text = "\uE740", FontFamily = DashKit.Icons, FontSize = 14, Foreground = DashKit.B(p.Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, () => _toggleFullscreen(), "Full screen (F)");

        var controls = new Grid { Margin = new Thickness(22, 14, 22, 18) };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        UIElement[] parts = [play, _time, seek, _duration, mute, volume, speed, full];
        for (int i = 0; i < parts.Length; i++)
        {
            Grid.SetColumn(parts[i], i);
            controls.Children.Add(parts[i]);
        }
        _time.Margin = new Thickness(12, 0, 0, 0);
        _duration.Margin = new Thickness(0, 0, 10, 0);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(head);
        Grid.SetRow(picture, 1);
        root.Children.Add(picture);
        Grid.SetRow(controls, 2);
        root.Children.Add(controls);
        Root = root;

        // ---- Media ----
        _media.MediaOpened += (_, _) =>
        {
            _duration.Text = Format(Length);
            PaintSeek();
        };
        _media.MediaEnded += (_, _) =>
        {
            _ended = true;
            SetPlaying(false);
            PaintSeek();
        };
        _media.MediaFailed += (_, e) =>
        {
            Log.Error($"Player could not open {path}", e.ErrorException);
            SetPlaying(false);
            _bigPlay.Visibility = Visibility.Collapsed;
            _message.Text = "Windows can't play this format here. Open it in your usual player (top right), or convert it to MP4.";
            _message.Visibility = Visibility.Visible;
        };
        _tick.Tick += (_, _) => { if (!_seeking) PaintSeek(); };
        _media.Source = new Uri(path);
        ApplyVolume();
        Play();
    }

    TimeSpan Length => _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan : TimeSpan.Zero;

    UIElement AudioArt()
    {
        var head = new Image { Source = CatSprites.Frame(CatSprites.Skin(_host.Settings.Cat), CatPose.Happy), Width = 140, Height = 140, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
        RenderOptions.SetBitmapScalingMode(head, BitmapScalingMode.NearestNeighbor);
        var name = _k.Text(System.IO.Path.GetFileNameWithoutExtension(Path), 22, Colors.White, font: DashKit.Serif);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(0, 170, 0, 0);
        return new Grid { IsHitTestVisible = false, Children = { head, name } };
    }

    Border Circle(UIElement content, Action onClick, string tip)
    {
        var fill = new SolidColorBrush(_k.P.Hover);
        var b = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = fill, Cursor = Cursors.Hand, ToolTip = tip, Child = content };
        b.MouseEnter += (_, _) => fill.Color = _k.P.Selected;
        b.MouseLeave += (_, _) => fill.Color = _k.P.Hover;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    Border Square(UIElement content, Action onClick, string tip, double width = 34)
    {
        var fill = new SolidColorBrush(Colors.Transparent);
        var b = new Border { Width = width, Height = 34, CornerRadius = new CornerRadius(9), Background = fill, Cursor = Cursors.Hand, ToolTip = tip, Child = content, VerticalAlignment = VerticalAlignment.Center };
        b.MouseEnter += (_, _) => fill.Color = _k.P.Hover;
        b.MouseLeave += (_, _) => fill.Color = Colors.Transparent;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    // ---- Playback ----

    public void Play()
    {
        if (_ended) { _media.Position = TimeSpan.Zero; _ended = false; }
        _media.Play();
        SetPlaying(true);
    }

    public void Pause()
    {
        _media.Pause();
        SetPlaying(false);
    }

    void TogglePlay()
    {
        if (_playing) Pause(); else Play();
    }

    void SetPlaying(bool on)
    {
        _playing = on;
        _playGlyph.Text = on ? "\uE769" : "\uE768";
        _playGlyph.Margin = new Thickness(on ? 0 : 3, 0, 0, 0);
        _bigPlay.Visibility = on || _message.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (on) _tick.Start(); else _tick.Stop();
    }

    void SeekTo(double fraction)
    {
        if (Length == TimeSpan.Zero) return;
        _media.Position = TimeSpan.FromTicks((long)(Length.Ticks * Math.Clamp(fraction, 0, 1)));
        _ended = false;
        PaintSeek();
    }

    public void SeekBy(double seconds)
    {
        if (Length == TimeSpan.Zero) return;
        var to = _media.Position + TimeSpan.FromSeconds(seconds);
        _media.Position = to < TimeSpan.Zero ? TimeSpan.Zero : to > Length ? Length : to;
        _ended = false;
        PaintSeek();
    }

    void PaintSeek()
    {
        var length = Length;
        double fraction = length > TimeSpan.Zero ? Math.Clamp(_media.Position.TotalSeconds / length.TotalSeconds, 0, 1) : 0;
        if (_ended) fraction = 1;
        double width = Math.Max(0, ((FrameworkElement)_seekTrack.Parent).ActualWidth);
        _seekFill.Width = width * fraction;
        _seekThumb.Margin = new Thickness(Math.Max(0, width * fraction - 7), 0, 0, 0);
        _time.Text = Format(_ended ? length : _media.Position);
    }

    public void NudgeVolume(double by)
    {
        _volume = Math.Clamp(_volume + by, 0, 1);
        _muted = false;
        ApplyVolume();
    }

    public void ToggleMute()
    {
        _muted = !_muted;
        ApplyVolume();
    }

    void ApplyVolume()
    {
        _media.Volume = _muted ? 0 : _volume;
        _muteGlyph.Text = _muted || _volume == 0 ? "\uE74F" : _volume < 0.5 ? "\uE993" : "\uE767";
        _volumeFill.Width = 84 * (_muted ? 0 : _volume);
    }

    void NextSpeed()
    {
        _speed = _speed switch { 1 => 1.5, 1.5 => 2, 2 => 0.5, _ => 1 };
        _media.SpeedRatio = _speed;
        _speedText.Text = $"{_speed:0.#}×";
    }

    /// <summary>The player's own keys; returns true when it used the key.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space: case Key.K: TogglePlay(); return true;
            case Key.Left: SeekBy(-5); return true;
            case Key.Right: SeekBy(5); return true;
            case Key.J: SeekBy(-10); return true;
            case Key.L: SeekBy(10); return true;
            case Key.Up: NudgeVolume(0.1); return true;
            case Key.Down: NudgeVolume(-0.1); return true;
            case Key.M: ToggleMute(); return true;
            case Key.F: _toggleFullscreen(); return true;
            case Key.Escape: if (Fullscreen) _toggleFullscreen(); else _back(); return true;
            default: return false;
        }
    }

    public void Close()
    {
        _tick.Stop();
        _media.Stop();
        _media.Close();
    }

    static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

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
