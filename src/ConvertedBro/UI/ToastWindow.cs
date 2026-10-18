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
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>Small card in the bottom-right corner showing one job's progress and result.</summary>
public sealed class ToastWindow : Window
{
    const double ToastWidth = 340;

    readonly Job? _job;
    readonly TextBlock _title = new();
    readonly TextBlock _status = new();
    readonly Border _bar = new();
    readonly Border _track = new();
    readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly DispatcherTimer _autoClose = new();
    bool _closing;

    public ToastWindow(Job job) : this(job.Title, "Waiting…", job.Action.Kind, job.Action.Short, showProgress: true)
    {
        _job = job;
        AddButton("Cancel", () => job.Cancel.Cancel());
        job.Changed += _ => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    /// <summary>A plain message toast with no job behind it.</summary>
    public static ToastWindow Message(string title, string message)
    {
        var toast = new ToastWindow(title, message, FileKind.Unknown, "!", showProgress: false);
        toast.StartAutoClose(TimeSpan.FromSeconds(5));
        return toast;
    }

    ToastWindow(string title, string message, FileKind kind, string badge, bool showProgress)
    {
        Width = ToastWidth;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

        Content = Build(title, message, kind, badge, showProgress);
        SourceInitialized += (_, _) => MakeNonActivating();
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));

        _autoClose.Tick += (_, _) => CloseAnimated();
        MouseEnter += (_, _) => _autoClose.Stop();
        MouseLeave += (_, _) => { if (_job == null || _job.State >= JobState.Done) _autoClose.Start(); };
    }

    UIElement Build(string title, string message, FileKind kind, string badge, bool showProgress)
    {
        var color = kind switch
        {
            FileKind.Video => (Color)FindResource("VideoColor"),
            FileKind.Audio => (Color)FindResource("AudioColor"),
            FileKind.Pdf => (Color)FindResource("PdfColor"),
            FileKind.Image => (Color)FindResource("ImageColor"),
            _ => Color.FromRgb(0x8B, 0x7C, 0xFF),
        };
        var font = (FontFamily)FindResource("BroFont");

        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = badge,
                Foreground = Brushes.White,
                FontFamily = font,
                FontWeight = FontWeights.Bold,
                FontSize = badge.Length > 4 ? 9.5 : 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        _title.Text = title;
        _title.FontFamily = font;
        _title.FontSize = 13.5;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = (Brush)FindResource("TextBrush");
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        _status.Text = message;
        _status.FontFamily = font;
        _status.FontSize = 12;
        _status.Foreground = (Brush)FindResource("SubtleTextBrush");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 2, 0, 0);

        _bar.Background = new SolidColorBrush(color);
        _bar.CornerRadius = new CornerRadius(2);
        _bar.HorizontalAlignment = HorizontalAlignment.Left;
        _bar.Width = 0;
        _track.Height = 4;
        _track.CornerRadius = new CornerRadius(2);
        _track.Background = (Brush)FindResource("TrackBrush");
        _track.Margin = new Thickness(0, 8, 0, 0);
        _track.Child = _bar;
        _track.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;

        _buttons.Margin = new Thickness(0, 8, 0, 0);

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Children = { _title, _status, _track, _buttons } };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);

        return new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.4 },
            Child = row,
        };
    }

    void Refresh()
    {
        if (_job == null || _closing) return;
        _bar.Width = Math.Max(0, (_track.ActualWidth > 0 ? _track.ActualWidth : ToastWidth - 106) * _job.Progress);

        switch (_job.State)
        {
            case JobState.Queued:
                _status.Text = "Waiting for a free slot…";
                break;
            case JobState.Running:
                _status.Text = _job.Progress > 0 ? $"Converting… {_job.Progress:P0}" : "Converting…";
                break;
            case JobState.Done:
                _status.Text = DoneText(_job.Outputs);
                _buttons.Children.Clear();
                if (_job.Outputs.Count > 0)
                {
                    AddButton("Show in folder", () => ShowInFolder(_job.Outputs[0]));
                    AddButton(_job.Outputs.Count == 1 ? "Open" : "Open folder", () => Open(_job.Outputs));
                }
                StartAutoClose(TimeSpan.FromSeconds(8));
                break;
            case JobState.Failed:
                _status.Text = _job.Error ?? "Something went wrong.";
                _status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xA8));
                _track.Visibility = Visibility.Collapsed;
                _buttons.Children.Clear();
                AddButton("Open log", () => Launch("explorer.exe", Log.DataDir));
                StartAutoClose(TimeSpan.FromSeconds(14));
                break;
            case JobState.Canceled:
                _status.Text = "Canceled.";
                _buttons.Children.Clear();
                StartAutoClose(TimeSpan.FromSeconds(2));
                break;
        }
    }

    static string DoneText(IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 0) return "Done.";
        long bytes = outputs.Sum(f => File.Exists(f) ? new FileInfo(f).Length : 0);
        var what = outputs.Count == 1 ? Path.GetFileName(outputs[0]) : $"{outputs.Count} files";
        return $"Done · {what} · {FormatSize(bytes)}";
    }

    static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    void AddButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("ToastButton"),
            Margin = new Thickness(6, 0, 0, 0),
            Focusable = false,
        };
        button.Click += (_, _) => onClick();
        _buttons.Children.Add(button);
    }

    static void ShowInFolder(string path) => Launch("explorer.exe", $"/select,\"{path}\"");

    static void Open(IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 1) Launch(outputs[0], null);
        else Launch("explorer.exe", $"\"{Path.GetDirectoryName(outputs[0])}\"");
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

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        _autoClose.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
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

/// <summary>Stacks toasts upward from the bottom-right corner of the work area.</summary>
public static class ToastHost
{
    static readonly List<ToastWindow> Open = [];

    /// <summary>Screen area (DIPs) the toasts should stay clear of, i.e. where the bro sits.</summary>
    public static Func<Rect?>? Avoid { get; set; }

    public static void Show(ToastWindow toast)
    {
        Open.Add(toast);
        toast.Closed += (_, _) => { Open.Remove(toast); Reflow(); };
        toast.SizeChanged += (_, _) => Reflow();
        // Too many cards is noise; drop the oldest finished ones.
        foreach (var old in Open.Take(Math.Max(0, Open.Count - 5)).ToList()) old.CloseAnimated();
        toast.Left = -10000;
        toast.Show();
        Reflow();
    }

    static void Reflow()
    {
        var area = SystemParameters.WorkArea;
        double right = area.Right - 8;
        double bottom = area.Bottom - 8;

        // If the bro lives in the corner, stack the cards beside him instead of on top of him.
        if (Avoid?.Invoke() is { } bro && bro.Right > right - 340 && bro.Bottom > area.Bottom - 320)
            right = Math.Max(area.Left + 340, bro.Left + 10);

        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var toast = Open[i];
            double height = toast.ActualHeight > 0 ? toast.ActualHeight : 110;
            bottom -= height;
            toast.Left = right - toast.Width;
            toast.Top = bottom;
        }
    }
}
