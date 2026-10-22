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
using Shiki.Core;
using Shiki.Shell;

namespace Shiki.UI;

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
    const int FloatRoom = 8; // and above that, room for floating Z's, hearts and sparkles
    const int CatTop = FloatRoom + HopRoom;
    const int StageWidth = CatSprites.PeekWidth, StageHeight = CatSprites.Height + CatTop;
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

    // Phase 4 moods: sleep, surprise, effort, petting.
    readonly Canvas _floats = new() { IsHitTestVisible = false };
    DateTime _lastActivity = DateTime.Now, _startleCooldown, _petCooldown, _lastTurn;
    DateTime? _busySince, _pettingSince;
    TimeSpan _lastBusy;
    bool _asleep;
    int _yawn, _startled, _grumpy, _nextZ, _nextSweat, _nextHeart, _petDirection;
    double _petLastX = double.NaN;
    readonly System.Collections.Generic.Queue<DateTime> _petTurns = new();

    static TimeSpan SleepAfter => DateTime.Now.Hour is >= 22 or < 6 ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(5);
    static readonly TimeSpan LongJob = TimeSpan.FromSeconds(8);
    bool Petting => _pettingSince != null && DateTime.Now - _lastTurn < TimeSpan.FromSeconds(1.2);

    public event Action<string[], Point>? FilesDropped;
    public event Action<Point>? MenuRequested;
    public event Action? PickFilesRequested;

    public CatWindow(Settings settings)
    {
        _settings = settings;
        _skin = CatSprites.Skin(settings.Cat);
        _cell = PixelSize / NativeMethods.SystemScale;

        Title = "Shiki";
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
        Canvas.SetTop(_cat, CatTop * _cell);
        _peek.Width = CatSprites.PeekWidth * _cell;
        _peek.Height = CatSprites.PeekHeight * _cell;
        _peek.RenderTransform = _peekShift;
        Canvas.SetTop(_peek, (StageHeight - CatSprites.PeekHeight) * _cell);
        stage.Children.Add(_hit);
        stage.Children.Add(_peek);
        stage.Children.Add(_cat);
        stage.Children.Add(_floats);
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
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            _ticker.Stop();
            SystemParameters.StaticPropertyChanged -= OnSystemChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        };

        _ticker.Tick += (_, _) => Animate();
        _hideDelay.Tick += (_, _) => { _hideDelay.Stop(); Duck(); };
        _dragLeave.Tick += (_, _) => { _dragLeave.Stop(); _dragOver = false; Evaluate(); };

        MouseEnter += (_, _) =>
        {
            // Hovering over a sleeping cat wakes it with a yawn.
            if (_asleep) { _asleep = false; _yawn = 9; }
            _hover = true;
            Touch();
            Evaluate();
        };
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
        if (jobs > 0 && _busy == 0) _busySince = DateTime.Now;
        if (jobs == 0 && _busySince is { } since) { _lastBusy = DateTime.Now - since; _busySince = null; }
        _busy = jobs;
        if (jobs > 0) Touch();
        Evaluate();
    }

    /// <summary>Someone started a Shift + drag somewhere: come up and watch.</summary>
    public void Notice(bool noticing)
    {
        _noticing = noticing;
        if (noticing) Touch();
        Evaluate();
    }

    public void Celebrate()
    {
        _celebrating = true;
        Evaluate();
        Hop();
        // A long job earns a double hop and some sparkles.
        if (_lastBusy > LongJob)
        {
            _lastBusy = TimeSpan.Zero;
            Later(450, Hop);
            Sparkles(3);
        }
        var done = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        done.Tick += (_, _) => { done.Stop(); _celebrating = false; Evaluate(); };
        done.Start();
    }

    /// <summary>A huge file arrived: wide eyes, a "!" and a hop.</summary>
    public void Startle()
    {
        if (DateTime.Now < _startleCooldown) return;
        _startleCooldown = DateTime.Now.AddSeconds(2);
        _asleep = false;
        _startled = 12;
        Touch();
        Evaluate();
        Later(260, () =>
        {
            Float(Floater.Alert, StageWidth / 2 - 1, CatTop - 6, rise: 4, ms: 900);
            Hop();
        });
    }

    /// <summary>Dev aid ("cat &lt;mood&gt;" on the command line): act out a mood without the mouse.</summary>
    public void Act(string mood)
    {
        var now = DateTime.Now;
        switch (mood)
        {
            case "sleep":
                _greeting = _hover = false;
                _lastActivity = now.AddHours(-1);
                Evaluate();
                break;
            case "startle": _startleCooldown = now; Startle(); break;
            case "party": Party(); break;
            case "purr":
                _pettingSince = now;
                _lastTurn = now.AddSeconds(3);
                Touch();
                Evaluate();
                break;
            case "grumpy": _grumpy = 26; Touch(); Evaluate(); break;
            case "strain":
                SetBusy(1);
                _busySince = now.AddSeconds(-10);
                Later(4000, () => { SetBusy(0); Celebrate(); });
                break;
        }
    }

    /// <summary>A milestone (100 files and so on): a little party.</summary>
    public void Party()
    {
        _celebrating = true;
        _asleep = false;
        Touch();
        Evaluate();
        Later(200, Hop);
        Later(700, Hop);
        Later(1200, Hop);
        Sparkles(6);
        for (int i = 0; i < 3; i++)
        {
            int n = i;
            Later(300 + n * 500, () => Float(Floater.Heart, n % 2 == 0 ? 4 : 15, CatTop - 4, rise: 7, ms: 1300));
        }
        Later(2600, () => { _celebrating = false; Evaluate(); });
    }

    void Sparkles(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int delay = 150 + i * 220;
            Later(delay, () => Float(Floater.Sparkle, _random.Next(2, StageWidth - 5), CatTop - _random.Next(1, 5), rise: 3, ms: 700));
        }
    }

    /// <summary>Something happened: the cat isn't idle (and wakes if it was asleep).</summary>
    void Touch()
    {
        _lastActivity = DateTime.Now;
        if (_asleep) _asleep = false;
    }

    void Later(int ms, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
    }

    /// <summary>
    /// Floats a little sprite up from (column, row) in art pixels, in whole-pixel steps like the
    /// cat itself, fading out in hard steps at the end.
    /// </summary>
    void Float(Floater kind, int column, int row, int rise, int ms)
    {
        var (w, h) = CatSprites.FloatSize(kind);
        var image = new Image { Source = CatSprites.Float(_skin, kind), Width = w * _cell, Height = h * _cell };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var shift = new TranslateTransform();
        image.RenderTransform = shift;
        Canvas.SetLeft(image, Math.Clamp(column, 0, StageWidth - w) * _cell);
        Canvas.SetTop(image, Math.Max(0, row) * _cell);
        _floats.Children.Add(image);

        var up = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
        int steps = Math.Min(rise, Math.Max(0, row));
        for (int i = 1; i <= steps; i++)
            up.KeyFrames.Add(new DiscreteDoubleKeyFrame(-i * _cell, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * i / (double)(steps + 1)))));
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.6, KeyTime.FromPercent(0.7)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.3, KeyTime.FromPercent(0.85)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) => _floats.Children.Remove(image);
        shift.BeginAnimation(TranslateTransform.YProperty, up);
        image.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Where to centre the wheel (physical pixels) so it sits just above the cat instead of
    /// covering it: the cat stays in view and reacts while you choose.
    /// </summary>
    public Point WheelSpotPx(double discRadiusDip)
    {
        double scale = NativeMethods.SystemScale;
        double catTop = Top + (CatTop - HopRoom) * _cell;
        return new Point((Left + Width / 2) * scale, (catTop - discRadiusDip - 4) * scale);
    }

    /// <summary>The cat's centre as it sits on the edge, in physical pixels.</summary>
    public Point CenterPx()
    {
        double scale = NativeMethods.SystemScale;
        return new Point((Left + Width / 2) * scale, (Top + (CatTop + CatSprites.Height / 2.0) * _cell) * scale);
    }

    // ---- Out and in ----

    bool WantsOut => _hover || _dragOver || _noticing || _busy > 0 || _celebrating || _greeting || _dragging || _startled > 0 || _yawn > 0 || _grumpy > 0 || Petting;

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
            ? new Rect(CatX * _cell, CatTop * _cell, CatSprites.Width * _cell, CatSprites.Height * _cell)
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
        var now = DateTime.Now;
        if (!_asleep && !_out && !WantsOut && now - _lastActivity > SleepAfter)
        {
            _asleep = true;
            _nextZ = _tick + 4;
        }
        if (_pettingSince is { } petStart)
        {
            if (now - _lastTurn > TimeSpan.FromSeconds(1.2)) _pettingSince = null;
            else if (now - petStart > TimeSpan.FromSeconds(6))
            {
                // Enough is enough: a grumpy tail flick, and no petting for a moment.
                _pettingSince = null;
                _grumpy = 26;
                _petCooldown = now.AddSeconds(5);
                _petTurns.Clear();
            }
        }

        if (!_out && _catShift.Y >= CatSprites.Height * _cell - 0.5)
        {
            if (_asleep)
            {
                if (_shownPeek != PeekPose.Sleep) { _peek.Source = CatSprites.Peek(_skin, PeekPose.Sleep); _shownPeek = PeekPose.Sleep; }
                if (_tick >= _nextZ)
                {
                    // Z's drift up from above the head, alternating sides a little.
                    Float(Floater.Z, (_tick / 24) % 2 == 0 ? 17 : 20, StageHeight - CatSprites.PeekHeight - 4, rise: 9, ms: 2200);
                    _nextZ = _tick + 24;
                }
                return;
            }
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
        else if (_startled > 0) { pose = CatPose.Startled; if (--_startled == 0) Evaluate(); }
        else if (_yawn > 0) { pose = CatPose.Yawn; if (--_yawn == 0) Evaluate(); }
        else if (_grumpy > 0) { pose = (_tick / 3) % 2 == 0 ? CatPose.Grumpy : CatPose.GrumpyFlick; if (--_grumpy == 0) Evaluate(); }
        else if (_celebrating) pose = CatPose.Happy;
        else if (_dragOver) pose = (_tick / 2) % 2 == 0 ? CatPose.Pop : CatPose.Idle; // pop, pop
        else if (Petting)
        {
            pose = CatPose.Purr;
            if (_tick >= _nextHeart)
            {
                Float(Floater.Heart, (_tick / 6) % 2 == 0 ? 4 : 15, CatTop - 3, rise: 6, ms: 1100);
                _nextHeart = _tick + 6;
            }
        }
        else if (_busy > 0 && _busySince is { } since && now - since > LongJob)
        {
            // A long job: harder drumming, squeezed eyes, the odd drop of sweat.
            pose = _tick % 2 == 0 ? CatPose.StrainLeft : CatPose.StrainRight;
            if (_tick >= _nextSweat)
            {
                Float(Floater.Sweat, StageWidth - 6, CatTop + 1, rise: 3, ms: 800);
                _nextSweat = _tick + 30;
            }
        }
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
        Touch();
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
        if (!_dragging) TrackPetting(e.GetPosition(this));
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

    /// <summary>Rubbing back and forth over the head (three turns in a second and a half) is petting.</summary>
    void TrackPetting(Point at)
    {
        if (!_out) return;
        double x = at.X / _cell, y = at.Y / _cell;
        bool onHead = x >= CatX && x <= CatX + CatSprites.Width && y >= CatTop && y <= CatTop + 13;
        if (!onHead) { _petLastX = double.NaN; return; }
        if (double.IsNaN(_petLastX)) { _petLastX = x; return; }
        double dx = x - _petLastX;
        if (Math.Abs(dx) < 1.5) return;
        int direction = Math.Sign(dx);
        _petLastX = x;
        if (_petDirection != 0 && direction != _petDirection)
        {
            var now = DateTime.Now;
            _petTurns.Enqueue(now);
            while (_petTurns.Count > 0 && now - _petTurns.Peek() > TimeSpan.FromSeconds(1.5)) _petTurns.Dequeue();
            if (_petTurns.Count >= 3 && now >= _petCooldown && _grumpy == 0)
            {
                _pettingSince ??= now;
                _lastTurn = now;
                Touch();
            }
        }
        _petDirection = direction;
    }

    // ---- Drag and drop ----

    void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        // Always copy: with Shift held Explorer asks for "move", which would delete the original.
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        _dragLeave.Stop();
        if (files) Touch();
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

    // ---- Staying drawn ----

    // A see-through window can lose its drawing surface when the PC sleeps, the screen is locked
    // or the display changes, and then stays blank. Rebuild it once things have settled.
    void OnPowerChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(() => RedrawSoon("resume"));
    }

    void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect or Microsoft.Win32.SessionSwitchReason.RemoteConnect)
            Dispatcher.BeginInvoke(() => RedrawSoon("unlock"));
    }

    void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => RedrawSoon("display change"));

    /// <summary>Redraws twice: the graphics driver can take a few seconds to come back.</summary>
    void RedrawSoon(string why)
    {
        Log.Info($"Cat redraw after {why}");
        foreach (var delay in new[] { 1.5, 6 })
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) => { timer.Stop(); Redraw(); };
            timer.Start();
        }
    }

    void Redraw()
    {
        if (!IsVisible) return;
        // Hiding and showing again gives the window a fresh surface.
        Hide();
        Show();
        Place();
        _shownPose = (CatPose)(-1);
        _shownPeek = (PeekPose)(-1);
        Animate();
    }
}
