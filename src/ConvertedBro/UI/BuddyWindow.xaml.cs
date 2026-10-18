using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

public partial class BuddyWindow : Window
{
    const double EdgeGap = 24;
    static readonly Duration Quick = TimeSpan.FromMilliseconds(180);

    readonly Settings _settings;
    readonly DispatcherTimer _blinkTimer = new();
    readonly DispatcherTimer _lookTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly DispatcherTimer _returnTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    readonly DispatcherTimer _happyTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly Random _random = new();
    Point _home;
    bool _summoned, _excited;

    /// <summary>Files dropped on the bro, plus the bro's centre in physical screen pixels.</summary>
    public event Action<string[], Point>? FilesDropped;
    public event Action? MenuRequested;
    public event Action? PickFilesRequested;

    public BuddyWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        _home = ValidHome(settings.BuddyLeft, settings.BuddyTop);
        Left = _home.X;
        Top = _home.Y;

        SourceInitialized += (_, _) => HideFromAltTab();
        Loaded += (_, _) => StartIdleAnimations();

        Body.MouseLeftButtonDown += OnBodyMouseDown;
        MouseRightButtonUp += (_, e) => { e.Handled = true; MenuRequested?.Invoke(); };

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetExcited(false);
        Drop += OnDrop;

        _blinkTimer.Tick += (_, _) => Blink();
        _lookTimer.Tick += (_, _) => LookAtCursor();
        _returnTimer.Tick += (_, _) => { _returnTimer.Stop(); ReturnHome(); };
        _happyTimer.Tick += (_, _) => { _happyTimer.Stop(); Fade(HappyEyes, 0); Fade(Eyes, 1); };
    }

    public Point CenterPx()
    {
        var source = PresentationSource.FromVisual(this);
        var center = new Point(Left + ActualWidth / 2, Top + ActualHeight / 2);
        return source?.CompositionTarget?.TransformToDevice.Transform(center) ?? center;
    }

    // ---- Moods ----

    public void SetBusy(int jobCount)
    {
        bool busy = jobCount > 0;
        Fade(BusyRing, busy ? 1 : 0);
        if (busy)
            BusyRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
        else
            BusyRotate.BeginAnimation(RotateTransform.AngleProperty, null);

        Badge.Visibility = jobCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = jobCount.ToString();
    }

    public void Celebrate()
    {
        Fade(Eyes, 0);
        Fade(HappyEyes, 1);
        var hop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0.88, KeyTime.FromPercent(0.25)));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromPercent(0.6)));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        BodySquash.BeginAnimation(ScaleTransform.ScaleYProperty, hop);
        _happyTimer.Stop();
        _happyTimer.Start();
    }

    void SetExcited(bool excited)
    {
        if (_excited == excited) return;
        _excited = excited;
        var scale = new DoubleAnimation(excited ? 1.14 : 1, Quick) { EasingFunction = new BackEase { Amplitude = 0.5 } };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        Fade(Glow, excited ? 1 : 0);
        Fade(Smile, excited ? 0 : 1);
        Fade(MouthO, excited ? 1 : 0);
        EyesScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(excited ? 1.12 : 1, Quick));
    }

    static void Fade(UIElement element, double to) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Quick));

    void StartIdleAnimations()
    {
        BodyBob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        ScheduleBlink();
        _lookTimer.Start();
    }

    void ScheduleBlink()
    {
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(2500, 6500));
        _blinkTimer.Start();
    }

    void Blink()
    {
        _blinkTimer.Stop();
        if (!_excited)
            EyesScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(70)) { AutoReverse = true });
        ScheduleBlink();
    }

    void LookAtCursor()
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out var cursor)) return;
        var center = CenterPx();
        double dx = cursor.X - center.X, dy = cursor.Y - center.Y;
        double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double reach = Math.Min(1, length / 300) * 2.5;
        PupilShift.X = dx / length * reach;
        PupilShift.Y = dy / length * reach;
    }

    // ---- Drag and drop ----

    void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        // Always copy: with Shift held Explorer asks for "move", which would delete the original.
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        SetExcited(files);
        if (files) _returnTimer.Stop();
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        SetExcited(false);
        _returnTimer.Stop();
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        // Don't keep Explorer waiting inside the drop call.
        var center = CenterPx();
        Dispatcher.BeginInvoke(() => FilesDropped?.Invoke(paths, center));
    }

    // ---- Moving around ----

    void OnBodyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            PickFilesRequested?.Invoke();
            return;
        }
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        // Wherever the user puts the bro becomes his new home.
        _summoned = false;
        _home = new Point(Left, Top);
        _settings.BuddyLeft = Left;
        _settings.BuddyTop = Top;
        _settings.Save();
    }

    /// <summary>Hop next to the cursor (physical pixels) so a file being dragged can be dropped on us.</summary>
    public void SummonNear(int xPx, int yPx)
    {
        if (!IsVisible) Show();
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cursor = fromDevice.Transform(new Point(xPx, yPx));
        var area = WorkAreaAt(xPx, yPx, fromDevice);

        double left = Math.Clamp(cursor.X + 50, area.Left, area.Right - Width);
        double top = Math.Clamp(cursor.Y - Height - 40, area.Top, area.Bottom - Height);
        _summoned = true;
        _returnTimer.Stop();
        MoveTo(left, top);
    }

    /// <summary>The drag that summoned us ended; go home unless a drop is being handled.</summary>
    public void OnSummonDragEnded()
    {
        if (_summoned) _returnTimer.Start();
    }

    public void CancelReturn() => _returnTimer.Stop();

    public void ReturnHome()
    {
        if (!_summoned) return;
        _summoned = false;
        MoveTo(_home.X, _home.Y);
    }

    void MoveTo(double left, double top)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(240);
        var moveX = new DoubleAnimation(left, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var moveY = new DoubleAnimation(top, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        moveX.Completed += (_, _) => Left = left;
        moveY.Completed += (_, _) => Top = top;
        BeginAnimation(LeftProperty, moveX);
        BeginAnimation(TopProperty, moveY);
    }

    Point ValidHome(double? left, double? top)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (left is { } l && top is { } t && screen.Contains(new Rect(l, t, Width, Height)))
            return new Point(l, t);

        var area = SystemParameters.WorkArea;
        return new Point(area.Right - Width - EdgeGap, area.Bottom - Height - EdgeGap);
    }

    static Rect WorkAreaAt(int xPx, int yPx, Matrix fromDevice)
    {
        var px = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(xPx, yPx)).WorkingArea;
        var topLeft = fromDevice.Transform(new Point(px.Left, px.Top));
        var bottomRight = fromDevice.Transform(new Point(px.Right, px.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    void HideFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW));
    }
}
