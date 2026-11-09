using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shiki.Core;
using Shiki.Shell;
using Path = System.Windows.Shapes.Path;

namespace Shiki.UI;

/// <summary>
/// One screen recording, start to finish: pick an area, choose mic and sound, count down, record,
/// stop, save. The frame and the control bar stay out of the recording itself.
/// </summary>
public sealed class RecordingSession
{
    readonly Settings _settings;
    RegionPicker? _picker;
    RecordFrame? _frame;
    RecordBar? _bar;
    ScreenRecorder? _recorder;
    Int32Rect _areaPx;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public enum Stage { Picking, Ready, CountingDown, Recording, Saving, Done }
    public Stage State { get; private set; } = Stage.Picking;

    /// <summary>The saved MP4 and the middle of the recorded area (physical pixels), for the wheel.</summary>
    public event Action<string, Point>? Saved;
    public event Action? Ended;

    public RecordingSession(Settings settings)
    {
        _settings = settings;
        _tick.Tick += (_, _) => { if (_recorder != null) _bar?.ShowTime(_recorder.Elapsed); };
    }

    public void Begin()
    {
        _picker = new RegionPicker();
        _picker.Picked += area =>
        {
            _picker = null;
            _areaPx = area;
            Ready();
        };
        _picker.Canceled += End;
        _picker.Show();
    }

    /// <summary>Dev aid: skip the picker, press Record after a moment, and stop after <paramref name="seconds"/>.</summary>
    public async void Demo(Int32Rect area, int seconds)
    {
        _areaPx = area;
        Ready();
        await Task.Delay(2500);
        _ = CountDownAndRecord(_settings.RecordMic, _settings.RecordSound);
        while (State != Stage.Recording && State != Stage.Done) await Task.Delay(100);
        await Task.Delay(seconds * 1000);
        await Stop();
    }

    /// <summary>The record shortcut again: stop if recording, cancel if just set up.</summary>
    public void Toggle()
    {
        if (State == Stage.Recording) _ = Stop();
        else if (State is Stage.Ready or Stage.CountingDown) End();
    }

    void Ready()
    {
        State = Stage.Ready;
        _frame = new RecordFrame(_areaPx);
        _frame.Show();
        _bar = new RecordBar(_areaPx, _settings.RecordMic, _settings.RecordSound);
        _bar.StartRequested += (mic, sound) =>
        {
            _settings.RecordMic = mic;
            _settings.RecordSound = sound;
            _settings.Save();
            _ = CountDownAndRecord(mic, sound);
        };
        _bar.StopRequested += () => _ = Stop();
        _bar.CancelRequested += End;
        _bar.Show();
    }

    async Task CountDownAndRecord(bool mic, bool sound)
    {
        State = Stage.CountingDown;
        _bar!.ShowCountdown();
        for (int n = 3; n >= 1; n--)
        {
            if (State != Stage.CountingDown) return;
            _frame!.ShowNumber(n);
            await Task.Delay(1000);
        }
        if (State != Stage.CountingDown) return;
        _frame!.ShowNumber(null);
        try
        {
            _recorder = new ScreenRecorder((_areaPx.X, _areaPx.Y, _areaPx.Width, _areaPx.Height), mic, sound);
            await _recorder.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed to start", ex);
            ToastHost.Show(ToastWindow.Message("Couldn't start recording", ex.Message));
            End();
            return;
        }
        State = Stage.Recording;
        _frame.Recording(true);
        _bar.ShowRecording();
        _tick.Start();
    }

    async Task Stop()
    {
        if (State != Stage.Recording || _recorder == null) return;
        State = Stage.Saving;
        _tick.Stop();
        _bar?.ShowSaving();
        var recorder = _recorder;
        var centre = new Point(_areaPx.X + _areaPx.Width / 2.0, _areaPx.Y + _areaPx.Height / 2.0);
        var output = OutputPaths.Next(System.IO.Path.Combine(OutputPaths.RecordingsFolder, $"Recording {DateTime.Now:yyyy-MM-dd HH.mm.ss}.mp4"), ".mp4");
        CloseWindows();
        // Saving runs as a job: it gets a progress card, lands in History, and the cat drums along.
        var job = JobRunner.Start(new BroAction
        {
            Id = "record-save",
            Label = "Screen recording",
            Short = "Recording",
            Hint = "",
            Kind = FileKind.Video,
            AcceptsExtension = _ => true,
            Run = async (_, progress, _) =>
            {
                try
                {
                    await recorder.StopAsync(output, progress);
                    JobNotes.Say($"{recorder.Elapsed:m\\:ss} recorded, saved as {System.IO.Path.GetFileName(output)}");
                    return [output];
                }
                finally
                {
                    recorder.Dispose();
                }
            },
        }, [output]);
        if (job != null)
        {
            var done = new TaskCompletionSource();
            job.Changed += j => { if (j.State >= JobState.Done) done.TrySetResult(); };
            if (job.State < JobState.Done) await done.Task;
            if (job.State == JobState.Done) Application.Current.Dispatcher.Invoke(() => Saved?.Invoke(output, centre));
        }
        State = Stage.Done;
        Ended?.Invoke();
    }

    void CloseWindows()
    {
        _picker?.Close();
        _frame?.Close();
        _bar?.Close();
        _picker = null;
        _frame = null;
        _bar = null;
    }

    void End()
    {
        if (State is Stage.Saving or Stage.Done) return;
        _tick.Stop();
        CloseWindows();
        _recorder?.Dispose();
        _recorder = null;
        State = Stage.Done;
        Ended?.Invoke();
    }

    /// <summary>Keeps a window out of screenshots and recordings (Windows 10 2004 and later).</summary>
    internal static void HideFromCapture(Window window)
    {
        // Dev aid: CB_SHOW_RECORDER=1 leaves them visible, so screenshots of the UI work.
        if (Environment.GetEnvironmentVariable("CB_SHOW_RECORDER") == "1") return;
        window.SourceInitialized += (_, _) => NativeMethods.SetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, 0x11);
    }

    internal static readonly Color Ink = Color.FromRgb(0xF2, 0xEF, 0xEA);
    internal static readonly Color Panel = Color.FromRgb(0x1A, 0x19, 0x18);
    internal static readonly Color Line = Color.FromRgb(0x33, 0x30, 0x2C);
    internal static readonly Color Accent = Theme.EditorAccent;
    internal static readonly Color Red = Color.FromRgb(0xE5, 0x48, 0x4D);
}

/// <summary>A live (not frozen) dim over the screens: drag a box, or Enter for the whole screen.</summary>
public sealed class RegionPicker : Window
{
    readonly Int32Rect _screenPx;
    readonly double _scale = NativeMethods.SystemScale;
    readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle _frame = new() { Stroke = new SolidColorBrush(RecordingSession.Accent), StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly Border _size = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock _sizeText = new() { FontFamily = Theme.TextFont, FontSize = 12, Foreground = Brushes.White };
    readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    Point? _start;
    Rect _selection = Rect.Empty;

    public event Action<Int32Rect>? Picked;
    public event Action? Canceled;

    public RegionPicker()
    {
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        _screenPx = new Int32Rect(virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _screenPx.X / _scale;
        Top = _screenPx.Y / _scale;
        Width = _screenPx.Width / _scale;
        Height = _screenPx.Height / _scale;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;

        _size.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x19, 0x18));
        _size.CornerRadius = new CornerRadius(6);
        _size.Padding = new Thickness(8, 3, 8, 4);
        _size.Child = _sizeText;
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_frame);
        _canvas.Children.Add(_size);

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1A, 0x19, 0x18)),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 9, 18, 10),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Drag over what to record.  Enter records the whole screen.  Esc cancels.",
                FontFamily = Theme.TextFont,
                FontSize = 13.5,
                Foreground = Brushes.White,
            },
        };
        hint.Loaded += (_, _) =>
        {
            var primary = SystemParameters.WorkArea;
            Canvas.SetLeft(hint, primary.Left - Left + (primary.Width - hint.ActualWidth) / 2);
            Canvas.SetTop(hint, primary.Top - Top + 28);
        };
        _canvas.Children.Add(hint);
        Content = _canvas;
        UpdateDim();

        SourceInitialized += (_, _) => NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => { Activate(); Focus(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Cancel();
            else if (e.Key == Key.Enter) PickWholeScreen();
        };
        MouseRightButtonUp += (_, _) => Cancel();
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(_canvas); CaptureMouse(); };
        MouseMove += (_, e) => { if (_start is { } s) Select(s, e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, _) => Finish();
    }

    void Select(Point a, Point b)
    {
        _selection = new Rect(a, b);
        _frame.Visibility = _size.Visibility = Visibility.Visible;
        Canvas.SetLeft(_frame, _selection.X - 1);
        Canvas.SetTop(_frame, _selection.Y - 1);
        _frame.Width = _selection.Width + 2;
        _frame.Height = _selection.Height + 2;
        _sizeText.Text = $"{Math.Round(_selection.Width * _scale)} × {Math.Round(_selection.Height * _scale)}";
        Canvas.SetLeft(_size, _selection.X);
        Canvas.SetTop(_size, _selection.Y > 30 ? _selection.Y - 28 : _selection.Bottom + 6);
        UpdateDim();
    }

    void UpdateDim() =>
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)),
            _selection.IsEmpty ? Geometry.Empty : new RectangleGeometry(_selection));

    void Finish()
    {
        ReleaseMouseCapture();
        if (_start == null) return;
        _start = null;
        var px = new Int32Rect(
            _screenPx.X + (int)Math.Round(_selection.X * _scale), _screenPx.Y + (int)Math.Round(_selection.Y * _scale),
            (int)Math.Round(_selection.Width * _scale), (int)Math.Round(_selection.Height * _scale));
        if (px.Width < 32 || px.Height < 32)
        {
            // A click without a real drag: try again.
            _selection = Rect.Empty;
            _frame.Visibility = _size.Visibility = Visibility.Collapsed;
            UpdateDim();
            return;
        }
        Done(px);
    }

    void PickWholeScreen()
    {
        NativeMethods.GetCursorPos(out var p);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)).Bounds;
        Done(new Int32Rect(screen.X, screen.Y, screen.Width, screen.Height));
    }

    void Done(Int32Rect px)
    {
        Close();
        Picked?.Invoke(px);
    }

    void Cancel()
    {
        Close();
        Canceled?.Invoke();
    }
}

/// <summary>An outline just outside the recorded area (clicks pass through), plus the countdown.</summary>
public sealed class RecordFrame : Window
{
    const double Gap = 3;
    readonly Rectangle _outline = new() { StrokeThickness = 2, IsHitTestVisible = false };
    readonly TextBlock _number = new()
    {
        FontFamily = Theme.PixelFont,
        FontSize = 96,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, -6, 0, 0),
    };
    // A dark round badge so the number reads over anything.
    readonly Border _badge = new()
    {
        Width = 150,
        Height = 150,
        CornerRadius = new CornerRadius(75),
        Background = new SolidColorBrush(Color.FromArgb(0xF7, 0x1A, 0x19, 0x18)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x30, 0x2C)),
        BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
        RenderTransformOrigin = new Point(0.5, 0.5),
    };

    public RecordFrame(Int32Rect areaPx)
    {
        double scale = NativeMethods.SystemScale;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = areaPx.X / scale - Gap;
        Top = areaPx.Y / scale - Gap;
        Width = areaPx.Width / scale + Gap * 2;
        Height = areaPx.Height / scale + Gap * 2;
        _outline.Stroke = new SolidColorBrush(RecordingSession.Accent);
        _outline.StrokeDashArray = [4, 3];
        _badge.Child = _number;
        Content = new Grid { Children = { _outline, _badge } };
        RecordingSession.HideFromCapture(this);
        SourceInitialized += (_, _) =>
        {
            // Clicks go straight through to whatever is being recorded.
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | 0x20 | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
        };
    }

    public void ShowNumber(int? n)
    {
        _badge.Visibility = n == null ? Visibility.Collapsed : Visibility.Visible;
        if (n == null) return;
        _number.Text = n.ToString();
        var pop = new DoubleAnimation(1.15, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scale = new ScaleTransform(1, 1);
        _badge.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>While recording the outline turns solid red.</summary>
    public void Recording(bool on)
    {
        _outline.Stroke = new SolidColorBrush(on ? RecordingSession.Red : RecordingSession.Accent);
        _outline.StrokeDashArray = on ? null : [4, 3];
    }
}

/// <summary>The small control pill next to the area: mic, sound, record; then the timer and stop.</summary>
public sealed class RecordBar : Window
{
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Int32Rect _areaPx;
    bool _mic, _sound;
    readonly TextBlock _time = Label("0:00", 15, FontWeights.SemiBold);
    readonly Ellipse _dot = new() { Width = 10, Height = 10, Fill = new SolidColorBrush(RecordingSession.Red), Margin = new Thickness(4, 0, 9, 0) };

    public event Action<bool, bool>? StartRequested;
    public event Action? StopRequested;
    public event Action? CancelRequested;

    public RecordBar(Int32Rect areaPx, bool mic, bool sound)
    {
        _areaPx = areaPx;
        _mic = mic;
        _sound = sound;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF4, RecordingSession.Panel.R, RecordingSession.Panel.G, RecordingSession.Panel.B)),
            BorderBrush = new SolidColorBrush(RecordingSession.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 },
            Child = _row,
        };
        RecordingSession.HideFromCapture(this);
        ContentRendered += (_, _) => Place();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CancelRequested?.Invoke(); };
        ShowSetup();
    }

    /// <summary>Below the area when there's room, else above it, else inside its bottom edge.</summary>
    void Place()
    {
        double scale = NativeMethods.SystemScale;
        var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(_areaPx.X, _areaPx.Y, _areaPx.Width, _areaPx.Height)).WorkingArea;
        double left = (_areaPx.X + _areaPx.Width / 2.0) / scale - ActualWidth / 2;
        double below = (_areaPx.Y + _areaPx.Height) / scale + 4;
        double above = _areaPx.Y / scale - ActualHeight - 4;
        double top = below + ActualHeight <= screen.Bottom / scale ? below
            : above >= screen.Top / scale ? above
            : (_areaPx.Y + _areaPx.Height) / scale - ActualHeight - 12;
        Left = Math.Clamp(left, screen.Left / scale, screen.Right / scale - ActualWidth);
        Top = top;
    }

    void ShowSetup()
    {
        _row.Children.Clear();
        _row.Children.Add(Toggle("\uE720", "Microphone", () => _mic, v => _mic = v));
        _row.Children.Add(Toggle("\uE767", "System sound", () => _sound, v => _sound = v));
        _row.Children.Add(Divider());
        var record = Pill("Record", () => StartRequested?.Invoke(_mic, _sound), RecordingSession.Red, dot: true);
        _row.Children.Add(record);
        _row.Children.Add(IconButton("\uE711", "Cancel (Esc)", () => CancelRequested?.Invoke()));
    }

    public void ShowCountdown()
    {
        _row.Children.Clear();
        _row.Children.Add(Label("Starting…", 14));
        _row.Children.Add(IconButton("\uE711", "Cancel (Esc)", () => CancelRequested?.Invoke()));
        Dispatcher.BeginInvoke(Place, DispatcherPriority.Loaded);
    }

    public void ShowRecording()
    {
        _row.Children.Clear();
        _row.Children.Add(new Border { Padding = new Thickness(8, 0, 4, 0), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dot, _time } } });
        _row.Children.Add(Divider());
        _row.Children.Add(Pill("Stop", () => StopRequested?.Invoke(), Color.FromRgb(0x2C, 0x2A, 0x27), glyph: "\uE71A"));
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        Dispatcher.BeginInvoke(Place, DispatcherPriority.Loaded);
    }

    public void ShowTime(TimeSpan elapsed) => _time.Text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");

    public void ShowSaving()
    {
        _row.Children.Clear();
        _row.Children.Add(Label("Saving…", 14));
    }

    static TextBlock Label(string text, double size, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = Theme.TextFont,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = new SolidColorBrush(RecordingSession.Ink),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 1),
    };

    static Border Divider() => new() { Width = 1, Height = 22, Background = new SolidColorBrush(RecordingSession.Line), Margin = new Thickness(6, 0, 6, 0) };

    static Border IconButton(string glyph, string tip, Action onClick)
    {
        var fill = new SolidColorBrush(Colors.Transparent);
        var b = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = fill,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 12, Foreground = new SolidColorBrush(RecordingSession.Ink), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.MouseEnter += (_, _) => fill.Color = Color.FromRgb(0x2C, 0x2A, 0x27);
        b.MouseLeave += (_, _) => fill.Color = Colors.Transparent;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    /// <summary>An on/off icon: lit orange when on, dim and crossed when off.</summary>
    static Border Toggle(string glyph, string name, Func<bool> get, Action<bool> set)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var slash = new Line { X1 = 9, Y1 = 27, X2 = 27, Y2 = 9, StrokeThickness = 1.6, Stroke = new SolidColorBrush(RecordingSession.Ink), IsHitTestVisible = false };
        var fill = new SolidColorBrush(Colors.Transparent);
        var b = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = fill, Cursor = Cursors.Hand, Child = new Grid { Children = { icon, slash } } };
        void Paint()
        {
            bool on = get();
            icon.Foreground = new SolidColorBrush(on ? RecordingSession.Accent : Color.FromRgb(0x8A, 0x85, 0x7E));
            slash.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            fill.Color = on ? Color.FromArgb(0x30, RecordingSession.Accent.R, RecordingSession.Accent.G, RecordingSession.Accent.B) : Colors.Transparent;
            b.ToolTip = $"{name}: {(on ? "on" : "off")}";
        }
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; set(!get()); Paint(); };
        Paint();
        b.Margin = new Thickness(2, 0, 2, 0);
        return b;
    }

    static Border Pill(string text, Action onClick, Color fillColour, bool dot = false, string? glyph = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot) row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        if (glyph != null) row.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 11, Foreground = Brushes.White, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontFamily = Theme.TextFont, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var fill = new SolidColorBrush(fillColour);
        var b = new Border { Background = fill, CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 7, 16, 8), Margin = new Thickness(2, 0, 4, 0), Cursor = Cursors.Hand, Child = row };
        b.MouseEnter += (_, _) => fill.Color = Theme.Lighten(fillColour, 0.1);
        b.MouseLeave += (_, _) => fill.Color = fillColour;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }
}
