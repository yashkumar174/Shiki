using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>
/// The pixel cat in the bottom-left corner. It sits on the taskbar edge while there's something
/// to do (a drag, a conversion, the mouse on it) and otherwise ducks below the edge so only its
/// ears and paws peek over.
/// </summary>
public sealed class CatWindow : Window
{
    /// <summary>Physical screen pixels per art pixel. Whole numbers keep pixel art sharp.</summary>
    const int PixelSize = 3;
    const int HopRoom = 3; // art pixels above the cat for hopping
    const int StageWidth = CatSprites.PeekWidth, StageHeight = CatSprites.Height + HopRoom;
    const int CatX = (StageWidth - CatSprites.Width) / 2;

    readonly Settings _settings;
    readonly double _cell;
    readonly Image _cat = new(), _peek = new();
    readonly TranslateTransform _catShift = new(), _peekShift = new();
    readonly Rectangle _hit = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _hideDelay = new();
    readonly DispatcherTimer _dragLeave = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly Random _random = new();
    CatSkin _skin;

    bool _out, _hover, _dragOver, _noticing, _celebrating, _greeting = true;
    int _busy, _tick, _nextBlink = 30, _nextPeekTap = 40, _clickPop, _moveVersion;
    CatPose _shownPose = (CatPose)(-1);
    PeekPose _shownPeek = (PeekPose)(-1);
    bool _dragging;
    double _dragStartScreenX, _dragStartLeft;

    public event Action<string[], Point>? FilesDropped;
    public event Action<Point>? MenuRequested;
    public event Action? PickFilesRequested;

    public CatWindow(Settings settings)
    {
        _settings = settings;
        _skin = CatSprites.Skin(settings.Cat);
        _cell = PixelSize / NativeMethods.SystemScale;

        Title = "Converted Bro";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        AllowDrop = true;
        UseLayoutRounding = true;
        Width = StageWidth * _cell;
        Height = StageHeight * _cell;

        var stage = new Canvas { Width = Width, Height = Height, ClipToBounds = true };
        foreach (var image in new[] { _cat, _peek }) RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        _cat.Width = CatSprites.Width * _cell;
        _cat.Height = CatSprites.Height * _cell;
        _cat.RenderTransform = _catShift;
        Canvas.SetLeft(_cat, CatX * _cell);
        Canvas.SetTop(_cat, HopRoom * _cell);
        _peek.Width = CatSprites.PeekWidth * _cell;
        _peek.Height = CatSprites.PeekHeight * _cell;
        _peek.RenderTransform = _peekShift;
        Canvas.SetTop(_peek, (StageHeight - CatSprites.PeekHeight) * _cell);
        stage.Children.Add(_hit);
        stage.Children.Add(_peek);
        stage.Children.Add(_cat);
        Content = stage;

        // Start out sitting on the edge to say hello, then duck down.
        _catShift.Y = 0;
        _peekShift.Y = CatSprites.PeekHeight * _cell;
        _out = true;
        SetHitArea();
        Place();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW));
        };
        Loaded += (_, _) =>
        {
            _ticker.Start();
            var hello = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            hello.Tick += (_, _) => { hello.Stop(); _greeting = false; Evaluate(); };
            hello.Start();
        };
        Closed += (_, _) => _ticker.Stop();
        SystemParameters.StaticPropertyChanged += OnSystemChanged;

        _ticker.Tick += (_, _) => Animate();
        _hideDelay.Tick += (_, _) => { _hideDelay.Stop(); Duck(); };
        _dragLeave.Tick += (_, _) => { _dragLeave.Stop(); _dragOver = false; Evaluate(); };

        MouseEnter += (_, _) => { _hover = true; Evaluate(); };
        MouseLeave += (_, _) => { _hover = false; Evaluate(); };
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            NativeMethods.GetCursorPos(out var p);
            MenuRequested?.Invoke(new Point(p.X, p.Y));
        };

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _dragLeave.Start();
        Drop += OnDrop;

        Animate();
    }

    // ---- Public moods ----

    public void SetSkin(CatSkin skin)
    {
        _skin = skin;
        _shownPose = (CatPose)(-1);
        _shownPeek = (PeekPose)(-1);
        Animate();
    }

    public void SetBusy(int jobs)
    {
        _busy = jobs;
        Evaluate();
    }

    /// <summary>Someone started a Shift + drag somewhere: come up and watch.</summary>
    public void Notice(bool noticing)
    {
        _noticing = noticing;
        Evaluate();
    }

    public void Celebrate()
    {
        _celebrating = true;
        Evaluate();
        Hop();
        var done = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        done.Tick += (_, _) => { done.Stop(); _celebrating = false; Evaluate(); };
        done.Start();
    }

    /// <summary>The cat's centre as it sits on the edge, in physical pixels.</summary>
    public Point CenterPx()
    {
        double scale = NativeMethods.SystemScale;
        return new Point((Left + Width / 2) * scale, (Top + (HopRoom + CatSprites.Height / 2.0) * _cell) * scale);
    }

    // ---- Out and in ----

    bool WantsOut => _hover || _dragOver || _noticing || _busy > 0 || _celebrating || _greeting || _dragging;

    void Evaluate()
    {
        if (WantsOut)
        {
            _hideDelay.Stop();
            if (!_out) PopUp();
        }
        else if (_out && !_hideDelay.IsEnabled)
        {
            _hideDelay.Interval = TimeSpan.FromMilliseconds(_busy == 0 && !_hover ? 1200 : 400);
            _hideDelay.Start();
        }
    }

    void PopUp()
    {
        _out = true;
        int version = ++_moveVersion;
        SetHitArea();
        Animate();
        // Paws let go first, then the cat springs up with a tiny overshoot.
        Slide(_peekShift, CatSprites.PeekHeight, 90, new QuadraticEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (version != _moveVersion) return;
            Slide(_catShift, -2, 200, new CubicEase { EasingMode = EasingMode.EaseOut },
                () => { if (version == _moveVersion) Slide(_catShift, 0, 90, new QuadraticEase()); });
        });
    }

    void Duck()
    {
        if (WantsOut) return;
        _out = false;
        int version = ++_moveVersion;
        SetHitArea();
        Slide(_catShift, CatSprites.Height, 260, new QuadraticEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (version != _moveVersion) return;
            Animate();
            Slide(_peekShift, -1, 120, new CubicEase { EasingMode = EasingMode.EaseOut },
                () => { if (version == _moveVersion) Slide(_peekShift, 0, 80, new QuadraticEase()); });
        });
    }

    void Hop()
    {
        if (!_out) return;
        int version = ++_moveVersion;
        Slide(_catShift, -HopRoom, 140, new QuadraticEase { EasingMode = EasingMode.EaseOut },
            () => { if (version == _moveVersion) Slide(_catShift, 0, 160, new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }); });
    }

    /// <summary>
    /// Moves in whole art pixels (discrete frames), so the sprite never lands between pixels
    /// and blurs. Easing still shapes the timing.
    /// </summary>
    void Slide(TranslateTransform shift, double toPixels, int ms, IEasingFunction ease, Action? done = null)
    {
        double from = Math.Round(shift.Y / _cell);
        int steps = (int)Math.Max(1, Math.Abs(toPixels - from));
        var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double value = Math.Round(from + (toPixels - from) * ease.Ease(t));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(value * _cell, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * (i - 0.5) / steps))));
        }
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(toPixels * _cell, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        if (done != null) frames.Completed += (_, _) => done();
        shift.BeginAnimation(TranslateTransform.YProperty, frames);
    }

    void SetHitArea()
    {
        // Out: the cat itself. Hiding: a strip along the edge, so the peek is easy to hover.
        var area = _out
            ? new Rect(CatX * _cell, HopRoom * _cell, CatSprites.Width * _cell, CatSprites.Height * _cell)
            : new Rect(0, (StageHeight - CatSprites.PeekHeight - 3) * _cell, Width, (CatSprites.PeekHeight + 3) * _cell);
        Canvas.SetLeft(_hit, area.X);
        Canvas.SetTop(_hit, area.Y);
        _hit.Width = area.Width;
        _hit.Height = area.Height;
    }

    // ---- Frame-by-frame animation ----

    void Animate()
    {
        _tick++;
        if (!_out && _catShift.Y >= CatSprites.Height * _cell - 0.5)
        {
            // Hiding: now and then a paw taps the edge.
            var peekPose = PeekPose.Idle;
            if (_tick >= _nextPeekTap)
            {
                peekPose = _tick % 2 == 0 ? PeekPose.LeftPaw : PeekPose.RightPaw;
                if (_tick >= _nextPeekTap + 2) _nextPeekTap = _tick + _random.Next(40, 90);
            }
            if (peekPose != _shownPeek) { _peek.Source = CatSprites.Peek(_skin, peekPose); _shownPeek = peekPose; }
            if (_shownPose != CatPose.Idle) { _cat.Source = CatSprites.Frame(_skin, CatPose.Idle); _shownPose = CatPose.Idle; }
            return;
        }

        if (_shownPeek != PeekPose.Idle) { _peek.Source = CatSprites.Peek(_skin, PeekPose.Idle); _shownPeek = PeekPose.Idle; }

        CatPose pose;
        if (_clickPop > 0) { pose = CatPose.Pop; _clickPop--; }
        else if (_celebrating) pose = CatPose.Happy;
        else if (_dragOver) pose = (_tick / 2) % 2 == 0 ? CatPose.Pop : CatPose.Idle; // pop, pop
        else if (_busy > 0) pose = (_tick / 2) % 2 == 0 ? CatPose.TapLeft : CatPose.TapRight; // bongo paws
        else if (_tick >= _nextBlink)
        {
            pose = CatPose.Blink;
            _nextBlink = _tick + _random.Next(28, 65);
        }
        else pose = LookTowardCursor();

        if (pose != _shownPose)
        {
            _cat.Source = CatSprites.Frame(_skin, pose);
            _shownPose = pose;
        }
    }

    CatPose LookTowardCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return CatPose.Idle;
        double dx = cursor.X - CenterPx().X;
        return dx < -90 ? CatPose.LookLeft : dx > 90 ? CatPose.LookRight : CatPose.Idle;
    }

    // ---- Mouse: click to meow, drag along the edge, double-click to pick files ----

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            PickFilesRequested?.Invoke();
            return;
        }
        NativeMethods.GetCursorPos(out var p);
        _dragStartScreenX = p.X / NativeMethods.SystemScale;
        _dragStartLeft = Left;
        _dragging = true;
        CaptureMouse();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || !IsMouseCaptured) return;
        NativeMethods.GetCursorPos(out var p);
        var area = SystemParameters.WorkArea;
        double left = _dragStartLeft + p.X / NativeMethods.SystemScale - _dragStartScreenX;
        Left = Snap(Math.Clamp(left, area.Left, area.Right - Width));
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        if (Math.Abs(Left - _dragStartLeft) < 2)
        {
            _clickPop = 3; // a tap on the head gets a little "pop"
            Animate();
        }
        else
        {
            _settings.CatLeft = Left;
            _settings.Save();
        }
        Evaluate();
    }

    // ---- Drag and drop ----

    void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        // Always copy: with Shift held Explorer asks for "move", which would delete the original.
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        _dragLeave.Stop();
        if (files && !_dragOver)
        {
            _dragOver = true;
            Evaluate();
        }
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        _dragLeave.Stop();
        _dragOver = false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var center = CenterPx();
        Dispatcher.BeginInvoke(() => FilesDropped?.Invoke(paths, center));
    }

    // ---- Placement ----

    void Place()
    {
        var area = SystemParameters.WorkArea;
        double left = _settings.CatLeft is { } saved && saved >= area.Left && saved <= area.Right - Width ? saved : area.Left + 10;
        Left = Snap(left);
        // The bottom of the window is the top of the taskbar: that's the edge the cat hides behind.
        Top = Snap(area.Bottom - Height);
    }

    static double Snap(double dip)
    {
        double scale = NativeMethods.SystemScale;
        return Math.Round(dip * scale) / scale;
    }

    void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(Place);
    }
}
