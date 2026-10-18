using System;
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
    static readonly Duration Quick = TimeSpan.FromMilliseconds(200);

    readonly Settings _settings;
    readonly DispatcherTimer _blinkTimer = new();
    readonly DispatcherTimer _happyTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly Random _random = new();
    bool _excited, _noticing;
    Vector _pupil;

    /// <summary>Files dropped on the bro, plus the bro's centre in physical screen pixels.</summary>
    public event Action<string[], Point>? FilesDropped;
    /// <summary>Right-click on the bro; the point is the cursor in physical pixels.</summary>
    public event Action<Point>? MenuRequested;
    public event Action? PickFilesRequested;

    public BuddyWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        var home = ValidHome(settings.BuddyLeft, settings.BuddyTop);
        Left = home.X;
        Top = home.Y;

        SourceInitialized += (_, _) => HideFromAltTab();
        Loaded += (_, _) => StartIdleAnimations();
        IsVisibleChanged += (_, _) =>
        {
            // Only follow the cursor while we're on screen.
            CompositionTarget.Rendering -= OnFrame;
            if (IsVisible) CompositionTarget.Rendering += OnFrame;
        };

        Body.MouseLeftButtonDown += OnBodyMouseDown;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            NativeMethods.GetCursorPos(out var p);
            MenuRequested?.Invoke(new Point(p.X, p.Y));
        };

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetExcited(false);
        Drop += OnDrop;

        _blinkTimer.Tick += (_, _) => Blink();
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
        {
            if (BusyRotate.HasAnimatedProperties) return;
            BusyRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            BusyRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
        Badge.Visibility = jobCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = jobCount.ToString();
    }

    public void Celebrate()
    {
        Fade(Eyes, 0);
        Fade(HappyEyes, 1);
        // Squash, stretch, settle: a small hop of joy.
        var hop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(620) };
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromPercent(0.22), new QuadraticEase()));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromPercent(0.55), new QuadraticEase()));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new ElasticEase { Oscillations = 1, Springiness = 4 }));
        var widen = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(620) };
        widen.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromPercent(0.22), new QuadraticEase()));
        widen.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromPercent(0.55), new QuadraticEase()));
        widen.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new ElasticEase { Oscillations = 1, Springiness = 4 }));
        BodySquash.BeginAnimation(ScaleTransform.ScaleYProperty, hop);
        BodySquash.BeginAnimation(ScaleTransform.ScaleXProperty, widen);
        _happyTimer.Stop();
        _happyTimer.Start();
    }

    /// <summary>Someone is Shift-dragging a file somewhere: perk up.</summary>
    public void Notice(bool noticing)
    {
        if (_noticing == noticing) return;
        _noticing = noticing;
        EyesScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(noticing ? 1.14 : 1, Quick) { EasingFunction = new BackEase { Amplitude = 0.6 } });
        Fade(Smile, noticing ? 0 : 1);
        Fade(MouthO, noticing ? 1 : 0);
    }

    void SetExcited(bool excited)
    {
        if (_excited == excited) return;
        _excited = excited;
        var scale = new DoubleAnimation(excited ? 1.12 : 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = excited ? new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        Fade(Glow, excited ? 1 : 0);
        Fade(Smile, excited || _noticing ? 0 : 1);
        Fade(MouthO, excited || _noticing ? 1 : 0);
        EyesScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(excited ? 1.15 : 1, Quick));
    }

    static void Fade(UIElement element, double to) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Quick) { EasingFunction = new QuadraticEase() });

    void StartIdleAnimations()
    {
        var float_ = TimeSpan.FromSeconds(2.2);
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        BodyBob.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -4, float_) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        // The shadow tightens and fades as he rises, in the same rhythm.
        ShadowScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.86, float_) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        Shadow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0.7, float_) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        // Slow breathing, out of step with the float so the motion never looks looped.
        BodyBreath.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 1.025, TimeSpan.FromSeconds(3.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        BodyBreath.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.988, TimeSpan.FromSeconds(3.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        ScheduleBlink();
    }

    void ScheduleBlink()
    {
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(2800, 6800));
        _blinkTimer.Start();
    }

    void Blink()
    {
        _blinkTimer.Stop();
        if (!_excited && !_noticing)
        {
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(180) };
            blink.KeyFrames.Add(new EasingDoubleKeyFrame(0.08, KeyTime.FromPercent(0.4), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            blink.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            EyesScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        }
        ScheduleBlink();
    }

    /// <summary>Every frame: ease the pupils toward the cursor so the eyes glide instead of jump.</summary>
    void OnFrame(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        var center = CenterPx();
        double dx = cursor.X - center.X, dy = cursor.Y - center.Y;
        double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double reach = Math.Min(1, length / 260) * 2.6;
        var target = new Vector(dx / length * reach, dy / length * reach);
        _pupil += (target - _pupil) * 0.16;
        if (Math.Abs(PupilShift.X - _pupil.X) > 0.01 || Math.Abs(PupilShift.Y - _pupil.Y) > 0.01)
        {
            PupilShift.X = _pupil.X;
            PupilShift.Y = _pupil.Y;
        }
    }

    // ---- Drag and drop ----

    void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        // Always copy: with Shift held Explorer asks for "move", which would delete the original.
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        SetExcited(files);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        SetExcited(false);
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
        // Wherever the user puts the bro becomes his home.
        _settings.BuddyLeft = Left;
        _settings.BuddyTop = Top;
        _settings.Save();
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

    void HideFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW));
    }
}
