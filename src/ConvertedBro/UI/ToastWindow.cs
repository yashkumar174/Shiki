using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;
using IOPath = System.IO.Path;

namespace ConvertedBro.UI;

/// <summary>A card in the bottom-right corner showing one job's progress and result.</summary>
public sealed class ToastWindow : Window
{
    public const double CardWidth = 344, Pad = 14;

    readonly Job? _job;
    readonly Palette _p = Theme.Current;
    readonly bool _pixel = Theme.Current.Pixel;
    readonly Color _accent;
    // Arcade progress: a row of blocks that light up one by one.
    const int BlockCount = 26;
    readonly UniformGrid _blocks = new() { Rows = 1, Columns = BlockCount, Height = 6 };
    readonly DispatcherTimer _blockSweep = new() { Interval = TimeSpan.FromMilliseconds(70) };
    int _sweep;
    readonly TextBlock _title = new(), _status = new(), _tileGlyph = new();
    readonly Border _tile = new();
    readonly Grid _track = new() { Height = 3, ClipToBounds = true };
    readonly Rectangle _bar = new() { HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 1.5, RadiusY = 1.5, Width = 0 };
    readonly Rectangle _shimmer = new() { HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 1.5, RadiusY = 1.5, Width = 72, Opacity = 0 };
    readonly TranslateTransform _shimmerMove = new();
    readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    readonly Border _close = new();
    readonly TranslateTransform _slide = new(28, 0);
    // A brush, not an Image: the toast sizes itself to its content, and the glass must not count as content.
    readonly Border _glass = new() { IsHitTestVisible = false };
    readonly DispatcherTimer _glassRefresh = new() { Interval = TimeSpan.FromMilliseconds(900) };
    LiveGlass? _live;
    Border? _card;
    readonly DispatcherTimer _autoClose = new();
    bool _closing, _shimmering;

    public ToastWindow(Job job) : this(job.Title, "Queued", job.Action.Kind, Theme.KindGlyph(job.Action.Kind), showProgress: true)
    {
        _job = job;
        AddButton("Cancel", () => job.Cancel.Cancel(), primary: false);
        job.Changed += _ => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    /// <summary>A plain message with no job behind it.</summary>
    public static ToastWindow Message(string title, string message)
    {
        var toast = new ToastWindow(title, message, FileKind.Unknown, "\uE946", showProgress: false);
        toast.StartAutoClose(TimeSpan.FromSeconds(5));
        return toast;
    }

    ToastWindow(string title, string message, FileKind kind, string glyph, bool showProgress)
    {
        _accent = Theme.Accent(kind);
        Width = CardWidth + Pad * 2;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        UseLayoutRounding = true;
        if (_pixel) TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);

        _card = (Border)Build(title, message, glyph, showProgress);
        var layers = new Grid { RenderTransform = _slide };
        layers.Children.Add(_glass);
        layers.Children.Add(_card);
        _card.RenderTransform = null;
        Content = layers;
        SourceInitialized += (_, _) => MakeNonActivating();
        // After the first layout pass the height is known; we're still fully transparent, so the
        // capture sees only what's behind us. Then fade in.
        ContentRendered += (_, _) =>
        {
            if (!_pixel)
            {
                if (LiveGlass.Enabled) StartLiveGlass();
                else RenderGlass(Glass.Capture(Glass.ToPixels(this, new Rect(Left, Top, Width, ActualHeight))));
            }
            AnimateIn();
        };
        // Keep the glass in step with what's behind it (it moves when cards restack, and the desktop changes).
        _glassRefresh.Tick += (_, _) => RefreshGlass();
        Closed += (_, _) => { _glassRefresh.Stop(); _live?.Dispose(); };

        _autoClose.Tick += (_, _) => CloseAnimated();
        MouseEnter += (_, _) => { _autoClose.Stop(); FadeClose(1); };
        MouseLeave += (_, _) =>
        {
            FadeClose(0);
            if (_job == null || _job.State >= JobState.Done) _autoClose.Start();
        };
    }

    UIElement Build(string title, string message, string glyph, bool showProgress)
    {
        _tile.Width = _tile.Height = 38;
        _tile.CornerRadius = new CornerRadius(_pixel ? 0 : 10);
        _tile.VerticalAlignment = VerticalAlignment.Top;
        _tile.Background = new LinearGradientBrush(Theme.Lighten(_accent, 0.18), _accent, 60);
        _tileGlyph.Text = glyph;
        _tileGlyph.FontFamily = Theme.IconFont;
        _tileGlyph.FontSize = 17;
        _tileGlyph.Foreground = Brushes.White;
        _tileGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _tileGlyph.VerticalAlignment = VerticalAlignment.Center;
        _tile.Child = _tileGlyph;

        _title.Text = title;
        _title.FontFamily = _pixel ? Theme.PixelFont : Theme.TextFont;
        _title.FontSize = _pixel ? 15 : 13.5;
        _title.FontWeight = _pixel ? FontWeights.Normal : FontWeights.SemiBold;
        _title.Foreground = _pixel ? Theme.Brush(_p.Text) : Brushes.White;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Margin = new Thickness(0, 0, 22, 0);

        _status.Text = message;
        _status.FontFamily = Theme.TextFont;
        _status.FontSize = 12;
        _status.Foreground = _pixel ? Theme.Brush(_p.SubtleText) : Theme.Brush(Color.FromArgb(0xD0, 255, 255, 255));
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 3, 0, 0);

        _bar.Fill = Theme.Brush(_accent);
        _shimmer.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = { new GradientStop(Theme.WithAlpha(_accent, 0), 0), new GradientStop(_accent, 0.5), new GradientStop(Theme.WithAlpha(_accent, 0), 1) },
        };
        _shimmer.RenderTransform = _shimmerMove;
        _track.Margin = new Thickness(0, 10, 0, 0);
        _track.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (_pixel)
        {
            _track.Height = 6;
            for (int i = 0; i < BlockCount; i++)
                _blocks.Children.Add(new Rectangle { Fill = Theme.Brush(_p.Track), Margin = new Thickness(0, 0, 2, 0) });
            _track.Children.Add(_blocks);
            _blockSweep.Tick += (_, _) => { _sweep = (_sweep + 1) % (BlockCount + 6); PaintBlocks(0); };
        }
        else
        {
            _track.Children.Add(new Rectangle { Fill = Theme.Brush(_p.Track), RadiusX = 1.5, RadiusY = 1.5 });
            _track.Children.Add(_bar);
            _track.Children.Add(_shimmer);
        }

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Children = { _title, _status, _track, _buttons } };
        var row = new DockPanel();
        DockPanel.SetDock(_tile, Dock.Left);
        row.Children.Add(_tile);
        row.Children.Add(text);

        _close.Width = _close.Height = 24;
        _close.CornerRadius = new CornerRadius(6);
        _close.HorizontalAlignment = HorizontalAlignment.Right;
        _close.VerticalAlignment = VerticalAlignment.Top;
        _close.Margin = new Thickness(0, -4, -6, 0);
        _close.Background = Brushes.Transparent;
        _close.Cursor = Cursors.Hand;
        _close.Opacity = 0;
        _close.Child = new TextBlock { Text = "\uE711", FontFamily = Theme.IconFont, FontSize = 10, Foreground = Theme.Brush(_p.SubtleText), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _close.MouseEnter += (_, _) => _close.Background = Theme.Brush(_p.RowHover);
        _close.MouseLeave += (_, _) => _close.Background = Brushes.Transparent;
        _close.MouseLeftButtonUp += (_, _) => CloseAnimated();

        var card = new Grid { Children = { row, _close } };
        return new Border
        {
            Margin = new Thickness(Pad),
            Padding = new Thickness(14, 14, 16, 14),
            CornerRadius = new CornerRadius(_pixel ? 0 : 18),
            Background = _pixel ? Theme.Brush(_p.Surface) : Brushes.Transparent,
            BorderBrush = _pixel ? Theme.Brush(_p.SurfaceBorder) : null,
            BorderThickness = new Thickness(_pixel ? 2 : 0),
            // Arcade: a hard shadow with no blur. Glass: a soft lift.
            Effect = _pixel
                ? new DropShadowEffect { BlurRadius = 0, ShadowDepth = 6, Direction = 315, Opacity = 0.55 }
                : new DropShadowEffect { BlurRadius = 7, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black },
            RenderTransform = _slide,
            Child = card,
        };
    }

    void Refresh()
    {
        if (_job == null || _closing) return;
        switch (_job.State)
        {
            case JobState.Queued:
                _status.Text = "Queued";
                SetShimmer(true);
                break;
            case JobState.Running:
                bool known = _job.Progress > 0.001;
                _status.Text = known ? $"Converting, {_job.Progress:P0}" : "Converting";
                SetShimmer(!known);
                if (known) AnimateBar(_job.Progress);
                break;
            case JobState.Done:
                SetShimmer(false);
                AnimateBar(1);
                _tileGlyph.Text = "\uE73E";
                _status.Text = _job.Note ?? DoneText(_job.Outputs);
                _buttons.Children.Clear();
                if (_job.Outputs.Count > 0)
                {
                    AddButton(_job.Outputs.Count == 1 ? "Open" : "Open folder", () => Open(_job.Outputs), primary: true);
                    AddButton("Show in folder", () => ShowInFolder(_job.Outputs[0]), primary: false);
                }
                StartAutoClose(TimeSpan.FromSeconds(8));
                break;
            case JobState.Failed:
                SetShimmer(false);
                var coral = Theme.Accent(FileKind.Pdf);
                _tile.Background = new LinearGradientBrush(Theme.Lighten(coral, 0.18), coral, 60);
                _tileGlyph.Text = "\uE7BA";
                _status.Text = _job.Error ?? "The conversion stopped unexpectedly.";
                _track.Visibility = Visibility.Collapsed;
                _buttons.Children.Clear();
                AddButton("Open log", () => Launch("explorer.exe", $"\"{Log.DataDir}\""), primary: false);
                StartAutoClose(TimeSpan.FromSeconds(14));
                break;
            case JobState.Canceled:
                SetShimmer(false);
                _status.Text = "Canceled";
                _track.Visibility = Visibility.Collapsed;
                _buttons.Children.Clear();
                StartAutoClose(TimeSpan.FromSeconds(2));
                break;
        }
    }

    void AnimateBar(double fraction)
    {
        if (_pixel)
        {
            PaintBlocks(fraction);
            return;
        }
        double full = _track.ActualWidth > 0 ? _track.ActualWidth : CardWidth - 82;
        _bar.BeginAnimation(WidthProperty, new DoubleAnimation(full * fraction, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>A light that sweeps the track while we can't tell how far along we are.</summary>
    void SetShimmer(bool on)
    {
        if (on == _shimmering) return;
        _shimmering = on;
        if (_pixel)
        {
            if (on) _blockSweep.Start(); else _blockSweep.Stop();
            return;
        }
        _shimmer.Opacity = on ? 1 : 0;
        if (on)
            _shimmerMove.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-80, CardWidth - 60, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        else
            _shimmerMove.BeginAnimation(TranslateTransform.XProperty, null);
    }

    /// <summary>Lights the blocks up to <paramref name="fraction"/>; while sweeping, a short run moves along instead.</summary>
    void PaintBlocks(double fraction)
    {
        int lit = (int)Math.Round(fraction * BlockCount);
        var on = Theme.Brush(_accent);
        var dim = Theme.Brush(Theme.WithAlpha(_accent, 0x60));
        var off = Theme.Brush(_p.Track);
        for (int i = 0; i < BlockCount; i++)
        {
            var block = (Rectangle)_blocks.Children[i];
            if (_shimmering) block.Fill = i <= _sweep && i > _sweep - 4 ? (i == _sweep ? on : dim) : off;
            else block.Fill = i < lit ? on : off;
        }
    }

    static string DoneText(IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 0) return "Done";
        long bytes = outputs.Sum(f => File.Exists(f) ? new FileInfo(f).Length : 0);
        return outputs.Count == 1
            ? $"Saved as {IOPath.GetFileName(outputs[0])} ({FormatSize(bytes)})"
            : $"Saved {outputs.Count} files ({FormatSize(bytes)})";
    }

    static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    void AddButton(string text, Action onClick, bool primary)
    {
        var rest = primary ? Theme.WithAlpha(_accent, 0x2E) : _p.RowHover;
        var hot = primary ? Theme.WithAlpha(_accent, 0x48) : Theme.WithAlpha(_p.Text, 0x1C);
        var background = new SolidColorBrush(rest);
        var button = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(_pixel ? 0 : 6),
            Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = Theme.LabelFont,
                FontSize = _pixel ? 13 : 12,
                FontWeight = primary && !_pixel ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Theme.Brush(primary ? Theme.Lighten(_accent, _p.Dark ? 0.35 : 0) : _p.Text),
            },
        };
        var fast = TimeSpan.FromMilliseconds(90);
        button.MouseEnter += (_, _) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hot, fast));
        button.MouseLeave += (_, _) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(rest, fast));
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        _buttons.Children.Add(button);
    }

    void FadeClose(double to) => _close.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));

    static void ShowInFolder(string path) => Launch("explorer.exe", $"/select,\"{path}\"");

    static void Open(IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 1) Launch(outputs[0], null);
        else Launch("explorer.exe", $"\"{IOPath.GetDirectoryName(outputs[0])}\"");
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

    void StartAutoClose(TimeSpan after)
    {
        _autoClose.Stop();
        _autoClose.Interval = after;
        if (!IsMouseOver) _autoClose.Start();
    }

    void AnimateIn()
    {
        if (_pixel)
        {
            var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(150) };
            foreach (var (x, ms) in new[] { (24.0, 0), (12.0, 50), (4.0, 100), (0.0, 150) })
                frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            _slide.BeginAnimation(TranslateTransform.XProperty, frames);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100)));
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        _autoClose.Stop();
        _blockSweep.Stop();
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(20, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    void StartLiveGlass()
    {
        var pixels = Glass.ToPixels(this, new Rect(Left, Top, Width, ActualHeight));
        _live = new LiveGlass(this, source => _glass.Background = new ImageBrush(source) { Stretch = Stretch.Fill }, fps: 20);
        _live.SetPlan(GlassPlan.Create(GlassShapes.Field(pixels.Width, pixels.Height, ToastShape(pixels)), pixels.Width, pixels.Height, ToastStyle(pixels)));
        _live.Start();
    }

    Func<double, double, double> ToastShape(Int32Rect pixels)
    {
        double scale = pixels.Width / Width;
        var card = new Rect(Pad * scale, Pad * scale, (Width - Pad * 2) * scale, (pixels.Height / scale - Pad * 2) * scale);
        return GlassShapes.RoundedRect(card, 18 * scale);
    }

    LiquidGlassStyle ToastStyle(Int32Rect pixels)
    {
        double scale = pixels.Width / Width;
        return ConvertWheel.GlassStyle(_p, bezel: 18 * scale, refraction: 18 * scale, frost: 10);
    }

    void RenderGlass(BitmapSource? snapshot)
    {
        if (snapshot == null || _card == null) return;
        double scale = snapshot.PixelWidth / Width;
        var card = new Rect(Pad * scale, Pad * scale, (Width - Pad * 2) * scale, (snapshot.PixelHeight / scale - Pad * 2) * scale);
        var shape = GlassShapes.RoundedRect(card, 18 * scale);
        var style = ConvertWheel.GlassStyle(_p, bezel: 18 * scale, refraction: 18 * scale, frost: 10);
        _glass.Background = new ImageBrush(LiquidGlass.Render(snapshot, GlassShapes.Field(snapshot.PixelWidth, snapshot.PixelHeight, shape), style)) { Stretch = Stretch.Fill };
    }

    /// <summary>
    /// Re-captures what's behind the toast. The window is briefly excluded from screen capture
    /// (it stays visible on screen), so the snapshot shows the desktop, not the toast itself.
    /// </summary>
    void RefreshGlass()
    {
        if (_closing || !IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE)) return;
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            try
            {
                if (!_closing) RenderGlass(Glass.Capture(Glass.ToPixels(this, new Rect(Left, Top, Width, ActualHeight))));
            }
            finally
            {
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_NONE);
            }
        };
        settle.Start();
    }

    void MakeNonActivating()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
    }
}

/// <summary>Stacks toasts upward from the bottom-right corner, sliding them into place.</summary>
public static class ToastHost
{
    static readonly List<ToastWindow> Open = [];

    /// <summary>Screen area (DIPs) the toasts should stay clear of, i.e. where the bro sits.</summary>
    public static Func<Rect?>? Avoid { get; set; }

    public static void Show(ToastWindow toast)
    {
        Open.Add(toast);
        toast.Closed += (_, _) => { Open.Remove(toast); Reflow(animate: true); };
        toast.SizeChanged += (_, _) => Reflow(animate: true);
        foreach (var old in Open.Take(Math.Max(0, Open.Count - 4)).ToList()) old.CloseAnimated();
        Reflow(animate: false, newest: toast);
        toast.Show();
    }

    static void Reflow(bool animate, ToastWindow? newest = null)
    {
        var area = SystemParameters.WorkArea;
        // Cards carry a transparent margin for their shadow; the visible card sits 10px from the edges.
        double right = area.Right + ToastWindow.Pad - 10;
        double bottom = area.Bottom + ToastWindow.Pad - 10;

        if (Avoid?.Invoke() is { } bro && bro.Right > right - ToastWindow.CardWidth - 40 && bro.Bottom > area.Bottom - 320)
            right = Math.Max(area.Left + ToastWindow.CardWidth, bro.Left + 22);

        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var toast = Open[i];
            double height = toast.ActualHeight > 0 ? toast.ActualHeight : 104;
            double top = bottom - height;
            bottom = top + ToastWindow.Pad; // next card's shadow margin overlaps this one's
            toast.Left = right - toast.Width;

            if (!animate || toast == newest || toast.ActualHeight == 0)
            {
                toast.BeginAnimation(Window.TopProperty, null);
                toast.Top = top;
            }
            else
            {
                toast.BeginAnimation(Window.TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
        }
    }
}
