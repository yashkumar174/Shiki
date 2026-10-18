using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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
    readonly Color _accent;
    readonly TextBlock _title = new(), _status = new(), _tileGlyph = new();
    readonly Border _tile = new();
    readonly Grid _track = new() { Height = 3, ClipToBounds = true };
    readonly Rectangle _bar = new() { HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 1.5, RadiusY = 1.5, Width = 0 };
    readonly Rectangle _shimmer = new() { HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 1.5, RadiusY = 1.5, Width = 72, Opacity = 0 };
    readonly TranslateTransform _shimmerMove = new();
    readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    readonly Border _close = new();
    readonly TranslateTransform _slide = new(28, 0);
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
        var toast = new ToastWindow(title, message, FileKind.Unknown, "", showProgress: false);
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

        Content = Build(title, message, glyph, showProgress);
        SourceInitialized += (_, _) => MakeNonActivating();
        Loaded += (_, _) => AnimateIn();

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
        _tile.CornerRadius = new CornerRadius(10);
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
        _title.FontFamily = Theme.TextFont;
        _title.FontSize = 13.5;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = Theme.Brush(_p.Text);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Margin = new Thickness(0, 0, 22, 0);

        _status.Text = message;
        _status.FontFamily = Theme.TextFont;
        _status.FontSize = 12;
        _status.Foreground = Theme.Brush(_p.SubtleText);
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
        _track.Children.Add(new Rectangle { Fill = Theme.Brush(_p.Track), RadiusX = 1.5, RadiusY = 1.5 });
        _track.Children.Add(_bar);
        _track.Children.Add(_shimmer);

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
        _close.Child = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 10, Foreground = Theme.Brush(_p.SubtleText), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _close.MouseEnter += (_, _) => _close.Background = Theme.Brush(_p.RowHover);
        _close.MouseLeave += (_, _) => _close.Background = Brushes.Transparent;
        _close.MouseLeftButtonUp += (_, _) => CloseAnimated();

        var card = new Grid { Children = { row, _close } };
        return new Border
        {
            Margin = new Thickness(Pad),
            Padding = new Thickness(14, 14, 16, 14),
            CornerRadius = new CornerRadius(12),
            Background = Theme.Brush(_p.Surface),
            BorderBrush = Theme.Brush(_p.SurfaceBorder),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Direction = 270, Opacity = _p.Dark ? 0.45 : 0.18 },
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
                _tileGlyph.Text = "";
                _status.Text = DoneText(_job.Outputs);
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
                _tileGlyph.Text = "";
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
        _shimmer.Opacity = on ? 1 : 0;
        if (on)
            _shimmerMove.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-80, CardWidth - 60, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        else
            _shimmerMove.BeginAnimation(TranslateTransform.XProperty, null);
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
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = Theme.TextFont,
                FontSize = 12,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
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
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        _autoClose.Stop();
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(20, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
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
