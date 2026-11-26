using System;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
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

/// <summary>Which edge of the screen the cat lives on.</summary>
public enum CatEdge { Bottom, Left, Right, Top }

/// <summary>
/// The pixel cat on an edge of the screen. On the bottom (the taskbar) and the sides it sits on
/// the edge while there's something to do (a drag, a conversion, the mouse on it) and otherwise
/// ducks behind it so only its ears and paws peek over; on the sides the whole picture is turned,
/// so it peeks in from the wall. From the top it hangs by its front paws and swings, and hides by
/// climbing up, leaving its paws hooked over the edge. Drag it anywhere: it dangles from the
/// pointer, swinging, and jumps to the nearest edge when let go.
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

    // ---- Edges, carrying and swinging ----
    CatEdge _edge;
    /// <summary>Being carried by the pointer (dangling from it).</summary>
    bool _carried;
    double _dragStartScreenY, _dragStartTop, _lastDragX;
    DateTime _lastDragTime;
    readonly RotateTransform _turn = new();
    readonly RotateTransform _swing = new();
    readonly DispatcherTimer _physics = new() { Interval = TimeSpan.FromMilliseconds(16) };
    double _swingVelocity, _swingPull;

    // Phase 4 moods: sleep, surprise, effort, petting.
    readonly Canvas _floats = new() { IsHitTestVisible = false };
    DateTime _lastActivity = DateTime.Now, _startleCooldown, _petCooldown, _lastTurn;
    DateTime? _busySince, _pettingSince;
    TimeSpan _lastBusy;
    bool _asleep;
    int _yawn, _startled, _grumpy, _nextZ, _nextSweat, _nextHeart, _petDirection;
    double _petLastX = double.NaN;
    readonly System.Collections.Generic.Queue<DateTime> _petTurns = new();

    // Sleepier late at night, and when the battery's nearly empty.
    TimeSpan SleepAfter => _lowBattery ? TimeSpan.FromMinutes(1) : DateTime.Now.Hour is >= 22 or < 6 ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(5);

    // ---- Alive, without the distraction ----
    // Listening to you, pointing at something it found, small walks and stretches now and then;
    // and out of the way while you type, and gone while something runs full screen.
    bool _listening, _talking, _pointLeft, _fullScreen;
    int _pointing;
    DateTime _nextAct = DateTime.Now.AddMinutes(2), _typingUntil;
    uint _lastInput;
    System.Drawing.Point _lastCursor;
    int _walkLeft, _walkStep;
    Canvas? _stage;

    // The PC's state: a nearly empty battery, or the processor working flat out.
    bool _lowBattery, _pcBusy;
    int _nextPcFloat;
    static readonly TimeSpan LongJob = TimeSpan.FromSeconds(8);
    bool Petting => _pettingSince != null && DateTime.Now - _lastTurn < TimeSpan.FromSeconds(1.2);

    public event Action<string[], Point>? FilesDropped;
    public event Action<Point>? MenuRequested;
    public event Action? PickFilesRequested;

    public CatWindow(Settings settings)
    {
        _settings = settings;
        _skin = CatSprites.Dressed(CatSprites.Skin(settings.Cat), settings.Accessory);
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
        _edge = Enum.TryParse<CatEdge>(settings.CatEdge, true, out var edge) ? edge : CatEdge.Bottom;
        Width = StageWidth * _cell;
        Height = StageHeight * _cell;

        var stage = new Canvas { Width = StageWidth * _cell, Height = StageHeight * _cell, ClipToBounds = true, LayoutTransform = _turn };
        _stage = stage;
        foreach (var image in new[] { _cat, _peek }) RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        _cat.Width = CatSprites.Width * _cell;
        _cat.Height = CatSprites.Height * _cell;
        _cat.RenderTransform = new TransformGroup { Children = { _swing, _catShift } };
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

        // Start out on the edge to say hello, then duck down.
        _out = true;
        ApplyLayout();
        Place();
        _physics.Tick += (_, _) => SwingStep();

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

    /// <summary>The PC's battery or processor changed: the cat shows it now and then, quietly, from where it is.</summary>
    public void SetPcState(bool lowBattery, bool busy)
    {
        _lowBattery = lowBattery;
        _pcBusy = busy;
        _nextPcFloat = _tick + 5;
    }

    /// <summary>Good news from the PC (plugged in, a download done): a sprite floats up, a little hop if it's out.</summary>
    public void Cheer(Floater kind)
    {
        int row = _out ? CatTop - 5 : StageHeight - CatSprites.PeekHeight - 5;
        Float(kind, StageWidth / 2 - 2, row, rise: 6, ms: 1400);
        if (kind == Floater.Bolt) Later(500, () => Float(Floater.Heart, StageWidth / 2 + 3, row, rise: 6, ms: 1300));
        else Sparkles(2);
        if (_out) Hop();
    }

    /// <summary>You came back after a while away: the cat pops up, hops and says hello.</summary>
    public void WelcomeBack()
    {
        _asleep = false;
        Touch();
        _greeting = true;
        Evaluate();
        Later(380, Hop);
        Later(520, () => Float(Floater.Heart, StageWidth / 2 - 3, CatTop - 4, rise: 6, ms: 1300));
        Later(2600, () => { _greeting = false; Evaluate(); });
    }

    /// <summary>You're talking to it: it comes up with attentive eyes until you're done.</summary>
    public void Listening(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on) { _asleep = false; Touch(); }
        Evaluate();
    }

    /// <summary>A full-screen app is in front (the cat keeps out of the way).</summary>
    public bool FullScreenNow => _fullScreen;

    /// <summary>Reading a reply aloud: the mouth moves.</summary>
    public void Talking(bool on)
    {
        if (on == _talking) return;
        _talking = on;
        if (on) { _asleep = false; Touch(); }
        Evaluate();
    }

    /// <summary>"There it is": a paw raised toward something it found (a point on screen, in physical pixels).</summary>
    public void PointToward(double screenX)
    {
        _pointLeft = screenX < CenterPx().X;
        _pointing = 28;
        _asleep = false;
        Touch();
        Evaluate();
    }

    /// <summary>How much it moves on its own: lively, calm or still.</summary>
    public void SetLife(string life)
    {
        _nextAct = DateTime.Now + NextActIn(life);
    }

    static TimeSpan NextActIn(string life) => life switch
    {
        "lively" => TimeSpan.FromSeconds(Random.Shared.Next(40, 100)),
        "still" => TimeSpan.FromDays(1),
        _ => TimeSpan.FromSeconds(Random.Shared.Next(180, 420)),
    };

    /// <summary>
    /// Once a second: is something full screen on this screen (then the cat steps out of sight),
    /// are you typing (then it stays put), and is it time for a little stretch or walk?
    /// </summary>
    void Life()
    {
        WatchOut();
        // Full screen: a video, a game, a presentation, on the cat's own screen.
        var front = NativeMethods.GetForegroundWindow();
        bool fullScreen = false;
        if (front != IntPtr.Zero && front != new WindowInteropHelper(this).Handle)
        {
            NativeMethods.GetWindowThreadProcessId(front, out uint pid);
            if (pid != (uint)Environment.ProcessId && GetWindowRect(front, out var r))
            {
                var screen = System.Windows.Forms.Screen.FromHandle(front).Bounds;
                var mine = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                var cls = new System.Text.StringBuilder(32);
                GetClassName(front, cls, cls.Capacity);
                fullScreen = cls.ToString() is not ("Progman" or "WorkerW") && mine.Bounds == screen
                    && r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
            }
        }
        if (fullScreen != _fullScreen)
        {
            _fullScreen = fullScreen;
            if (_stage != null) _stage.Visibility = fullScreen ? Visibility.Hidden : Visibility.Visible;
        }
        if (!fullScreen) KeepOnTop(front);

        // Typing: input keeps arriving but the pointer hasn't moved.
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref info))
        {
            NativeMethods.GetCursorPos(out var cursor);
            var at = new System.Drawing.Point(cursor.X, cursor.Y);
            if (info.dwTime != _lastInput && at == _lastCursor) _typingUntil = DateTime.Now.AddSeconds(4);
            _lastInput = info.dwTime;
            _lastCursor = at;
        }

        // Now and then, if nothing's going on: a few steps along the edge, or a stretch.
        var life = _settings.CatLife;
        if (DateTime.Now < _nextAct) return;
        _nextAct = DateTime.Now + NextActIn(life);
        if (life == "still" || _fullScreen || DateTime.Now < _typingUntil || _out || _asleep || WantsOut || _walkStep > 0) return;
        if (_random.NextDouble() < (life == "lively" ? 0.3 : 0.15))
        {
            _yawn = 12;
            Touch();
            Evaluate();
            return;
        }
        // From the top, now and then a little swing instead of a walk.
        if (_edge == CatEdge.Top && _out) { Kick(_random.Next(2) == 0 ? 3 : -3); return; }
        // Walk a few steps, staying near home; back toward it if it has wandered.
        double home = SavedAlong ?? AlongNow;
        int offset = (int)Math.Round((AlongNow - home) / _cell);
        int steps = _random.Next(3, 7);
        _walkLeft = Math.Abs(offset) > 6 ? -Math.Sign(offset) * Math.Abs(offset) : (_random.Next(2) == 0 ? -steps : steps);
        _walkStep = Math.Abs(_walkLeft);
    }

    /// <summary>One step of a walk: the cat shifts a pixel along the edge, paws tapping in turn.</summary>
    void WalkStep()
    {
        if (_walkStep <= 0) return;
        _walkStep--;
        var area = SystemParameters.WorkArea;
        if (_edge is CatEdge.Left or CatEdge.Right)
            Top = Snap(Math.Clamp(Top + Math.Sign(_walkLeft) * _cell, area.Top, area.Bottom - Height));
        else
            Left = Snap(Math.Clamp(Left + Math.Sign(_walkLeft) * _cell, area.Left, area.Right - Width));
        var paw = _walkStep % 2 == 0 ? PeekPose.LeftPaw : PeekPose.RightPaw;
        if (!_out && !_asleep) { _peek.Source = PeekFrame(paw); _shownPeek = paw; }
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
            case "battery": SetPcState(!_lowBattery, _pcBusy); break;
            case "busy": SetPcState(_lowBattery, !_pcBusy); break;
            case "plugged": Cheer(Floater.Bolt); break;
            case "download": Cheer(Floater.Download); break;
            case "back": WelcomeBack(); break;
            case "listen": Listening(!_listening); break;
            case "point": PointToward(0); break;
            case "walk": _nextAct = DateTime.Now; _yawn = 0; break;
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
        if (_carried) return;
        // The rows given are for the bottom edge (the stage's edge at its bottom). Hanging, the edge
        // is at the top: the same distance from it, drifting down instead of up.
        int sign = -1;
        if (Hanging)
        {
            int fromEdge = StageHeight - row;
            row = Math.Clamp(fromEdge - h + (_out ? CatSprites.HangArms : 0), 0, StageHeight - h);
            rise = Math.Min(rise, StageHeight - h - row);
            sign = 1;
        }
        var image = new Image { Source = CatSprites.Float(_skin, kind), Width = w * _cell, Height = h * _cell };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var shift = new TranslateTransform();
        // On the sides the stage is turned; turn the sprite back so a Z still reads as a Z.
        image.RenderTransform = _turn.Angle == 0 ? shift : new TransformGroup { Children = { new RotateTransform(-_turn.Angle, w * _cell / 2, h * _cell / 2), shift } };
        Canvas.SetLeft(image, Math.Clamp(column, 0, StageWidth - w) * _cell);
        Canvas.SetTop(image, Math.Max(0, row) * _cell);
        _floats.Children.Add(image);

        var up = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
        int steps = sign < 0 ? Math.Min(rise, Math.Max(0, row)) : Math.Max(0, rise);
        for (int i = 1; i <= steps; i++)
            up.KeyFrames.Add(new DiscreteDoubleKeyFrame(sign * i * _cell, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * i / (double)(steps + 1)))));
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
        var body = BodyDip();
        var away = Away;
        double reach = (away.Y != 0 ? body.Height : body.Width) / 2 + HopRoom * _cell + discRadiusDip + 4;
        var centre = new Point(body.X + body.Width / 2, body.Y + body.Height / 2) + away * reach;
        return new Point(centre.X * scale, centre.Y * scale);
    }

    /// <summary>The cat's centre as it sits on the edge, in physical pixels.</summary>
    public Point CenterPx()
    {
        double scale = NativeMethods.SystemScale;
        var body = BodyDip();
        return new Point((body.X + body.Width / 2) * scale, (body.Y + body.Height / 2) * scale);
    }

    /// <summary>
    /// Every fifteen seconds or so while it lasts: a little red battery when it's running low, a
    /// drop of sweat when the processor is working flat out. Small, slow and from the corner.
    /// </summary>
    void PcNews()
    {
        if ((!_lowBattery && !_pcBusy) || _tick < _nextPcFloat) return;
        _nextPcFloat = _tick + 150;
        int row = StageHeight - CatSprites.PeekHeight - 5;
        if (_lowBattery) Float(Floater.Battery, 3, row, rise: 5, ms: 2200);
        else Float(Floater.Sweat, StageWidth - 6, row + 2, rise: 4, ms: 1400);
    }

    // ---- Out and in ----

    /// <summary>The reasons it's up right now, for the log ("noticing, listening").</summary>
    public string WhyOut() => string.Join(", ", new (bool On, string Name)[]
    {
        (_hover, "pointer over it"), (_dragOver, "a drag over it"), (_noticing, "noticing"), (_busy > 0, $"busy ({_busy})"),
        (_celebrating, "celebrating"), (_greeting, "greeting"), (_dragging, "being dragged"), (_startled > 0, "startled"),
        (_yawn > 0, "yawning"), (_grumpy > 0, "grumpy"), (Petting, "being petted"), (_listening, "listening"),
        (_talking, "talking"), (_pointing > 0, "pointing"),
    }.Where(r => r.On).Select(r => r.Name));

    public bool IsNoticing => _noticing;
    public bool IsListening => _listening;
    public bool IsTalking => _talking;

    DateTime? _outSince;
    bool _toldOut;

    /// <summary>Notes it in the log when the cat has stayed up over a minute, and why (a stuck reason shows there).</summary>
    void WatchOut()
    {
        if (!WantsOut || _hover || Petting || _dragging) { _outSince = null; _toldOut = false; return; }
        _outSince ??= DateTime.Now;
        if (!_toldOut && DateTime.Now - _outSince > TimeSpan.FromMinutes(1))
        {
            _toldOut = true;
            Log.Info($"Cat has stayed up over a minute: {WhyOut()}");
        }
    }

    bool WantsOut => _hover || _dragOver || _noticing || _busy > 0 || _celebrating || _greeting || _dragging || _startled > 0 || _yawn > 0 || _grumpy > 0 || Petting || _listening || _talking || _pointing > 0;

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

    // Hanging (from the top, or carried) everything moves the other way: in is up, out is down.
    bool Hanging => _edge == CatEdge.Top || _carried;
    double CatIn => Hanging ? -CatSprites.HangHeight : CatSprites.Height;
    double PeekAway => Hanging ? -CatSprites.HangPeekHeight : CatSprites.PeekHeight;
    int Toward => Hanging ? 1 : -1; // the direction "out of hiding", in stage pixels
    bool Tucked => Hanging ? _catShift.Y <= CatIn * _cell + 0.5 : _catShift.Y >= CatIn * _cell - 0.5;
    System.Windows.Media.Imaging.BitmapSource CatFrame(CatPose pose) => Hanging ? CatSprites.Hang(_skin, pose) : CatSprites.Frame(_skin, pose);
    System.Windows.Media.Imaging.BitmapSource PeekFrame(PeekPose pose) => Hanging ? CatSprites.HangPeek(_skin, pose) : CatSprites.Peek(_skin, pose);

    void PopUp()
    {
        _out = true;
        int version = ++_moveVersion;
        SetHitArea();
        Animate();
        // Paws let go first, then the cat springs out with a tiny overshoot (from the top: drops down and swings).
        Slide(_peekShift, PeekAway, 90, new QuadraticEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (version != _moveVersion) return;
            Slide(_catShift, 2 * Toward, 200, new CubicEase { EasingMode = EasingMode.EaseOut },
                () => { if (version == _moveVersion) Slide(_catShift, 0, 90, new QuadraticEase()); });
            if (Hanging) Later(180, () => Kick(_random.Next(2) == 0 ? 7 : -7));
        });
    }

    void Duck()
    {
        if (WantsOut || _carried) return;
        _out = false;
        int version = ++_moveVersion;
        SetHitArea();
        Slide(_catShift, CatIn, 260, new QuadraticEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (version != _moveVersion) return;
            Animate();
            Slide(_peekShift, Toward, 120, new CubicEase { EasingMode = EasingMode.EaseOut },
                () => { if (version == _moveVersion) Slide(_peekShift, 0, 80, new QuadraticEase()); });
        });
    }

    void Hop()
    {
        if (!_out) return;
        // Hanging, a hop is a swing.
        if (Hanging) { Kick(_random.Next(2) == 0 ? 9 : -9); return; }
        int version = ++_moveVersion;
        Slide(_catShift, -HopRoom, 140, new QuadraticEase { EasingMode = EasingMode.EaseOut },
            () => { if (version == _moveVersion) Slide(_catShift, 0, 160, new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }); });
    }

    // ---- Swinging (hanging from the top, or carried) ----

    /// <summary>A push: the cat swings, then settles back straight (where its pixels are crisp).</summary>
    void Kick(double degreesPerTick)
    {
        if (!Hanging) return;
        _swingVelocity += degreesPerTick;
        if (!_physics.IsEnabled) _physics.Start();
    }

    /// <summary>A pendulum: pulled back to straight (or toward where the carry leans it), losing a little each swing.</summary>
    void SwingStep()
    {
        double target = _carried ? _swingPull : 0;
        double angle = _swing.Angle;
        _swingVelocity += (target - angle) * 0.05;
        _swingVelocity *= _carried ? 0.86 : 0.955;
        angle += _swingVelocity;
        angle = Math.Clamp(angle, -40, 40);
        if (!_carried && Math.Abs(angle) < 0.4 && Math.Abs(_swingVelocity) < 0.15)
        {
            _swing.Angle = 0;
            _swingVelocity = 0;
            _physics.Stop();
            return;
        }
        _swing.Angle = angle;
        _swingPull *= 0.9;
    }

    // ---- Edges ----

    /// <summary>The cat's edge now.</summary>
    public CatEdge Edge => _edge;

    /// <summary>The way out from the edge (into the screen): up from the bottom, down from the top, and so on.</summary>
    public Vector Away => _edge switch
    {
        CatEdge.Top => new Vector(0, 1),
        CatEdge.Left => new Vector(1, 0),
        CatEdge.Right => new Vector(-1, 0),
        _ => new Vector(0, -1),
    };

    /// <summary>Sizes, turns and positions everything for the edge (or for being carried).</summary>
    void ApplyLayout()
    {
        bool hang = Hanging;
        _cat.Height = (hang ? CatSprites.HangHeight : CatSprites.Height) * _cell;
        Canvas.SetTop(_cat, (hang ? 0 : CatTop) * _cell);
        _peek.Height = (hang ? CatSprites.HangPeekHeight : CatSprites.PeekHeight) * _cell;
        Canvas.SetTop(_peek, hang ? 0 : (StageHeight - CatSprites.PeekHeight) * _cell);
        _swing.CenterX = CatSprites.Width * _cell / 2;
        _swing.CenterY = 0;
        _swing.Angle = 0;
        _turn.Angle = _carried ? 0 : _edge switch { CatEdge.Left => 90, CatEdge.Right => -90, _ => 0 };
        bool side = !_carried && _edge is CatEdge.Left or CatEdge.Right;
        Width = (side ? StageHeight : StageWidth) * _cell;
        Height = (side ? StageWidth : StageHeight) * _cell;
        _moveVersion++;
        _catShift.BeginAnimation(TranslateTransform.YProperty, null);
        _peekShift.BeginAnimation(TranslateTransform.YProperty, null);
        _catShift.Y = _out ? 0 : CatIn * _cell;
        _peekShift.Y = _out ? PeekAway * _cell : 0;
        _floats.Children.Clear();
        _shownPose = (CatPose)(-1);
        _shownPeek = (PeekPose)(-1);
        SetHitArea();
        Animate();
    }

    /// <summary>A rectangle on the (unturned) stage, in art pixels, as it lands in the window (DIPs), after the edge's turn.</summary>
    Rect StageToWindow(Rect cells)
    {
        var r = new Rect(cells.X * _cell, cells.Y * _cell, cells.Width * _cell, cells.Height * _cell);
        double w = StageWidth * _cell, h = StageHeight * _cell;
        if (_carried) return r;
        return _edge switch
        {
            CatEdge.Left => new Rect(h - r.Bottom, r.X, r.Height, r.Width),
            CatEdge.Right => new Rect(r.Y, w - r.Right, r.Height, r.Width),
            _ => r,
        };
    }

    /// <summary>The cat as it sits out of hiding, on screen (DIPs).</summary>
    public Rect BodyDip()
    {
        var cells = Hanging
            ? new Rect(CatX, 0, CatSprites.Width, CatSprites.HangHeight)
            : new Rect(CatX, CatTop, CatSprites.Width, CatSprites.Height);
        var r = StageToWindow(cells);
        r.Offset(Left, Top);
        return r;
    }

    /// <summary>
    /// Where a popup of this size goes (its top-left, DIPs): on the open side of the cat, a little
    /// gap away, kept on screen.
    /// </summary>
    public static Point Beside(Rect body, Vector away, Size popup, double gap = 8)
    {
        var area = SystemParameters.WorkArea;
        double cx = body.X + body.Width / 2, cy = body.Y + body.Height / 2;
        double x, y;
        if (away.Y < 0) { x = cx - popup.Width / 2; y = body.Top - gap - popup.Height; }
        else if (away.Y > 0) { x = cx - popup.Width / 2; y = body.Bottom + gap; }
        else if (away.X > 0) { x = body.Right + gap; y = cy - popup.Height / 2; }
        else { x = body.Left - gap - popup.Width; y = cy - popup.Height / 2; }
        return new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - popup.Width)), Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - popup.Height)));
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
        Rect area;
        if (Hanging)
            area = _out
                ? new Rect(CatX * _cell, 0, CatSprites.Width * _cell, CatSprites.HangHeight * _cell)
                : new Rect(0, 0, StageWidth * _cell, (CatSprites.HangPeekHeight + 3) * _cell);
        else
            area = _out
                ? new Rect(CatX * _cell, CatTop * _cell, CatSprites.Width * _cell, CatSprites.Height * _cell)
                : new Rect(0, (StageHeight - CatSprites.PeekHeight - 3) * _cell, StageWidth * _cell, (CatSprites.PeekHeight + 3) * _cell);
        Canvas.SetLeft(_hit, area.X);
        Canvas.SetTop(_hit, area.Y);
        _hit.Width = area.Width;
        _hit.Height = area.Height;
    }

    // ---- Frame-by-frame animation ----

    void Animate()
    {
        _tick++;
        if (_tick % 10 == 0) Life();
        if (_walkStep > 0 && _tick % 2 == 0) WalkStep();
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

        if (!_out && Tucked)
        {
            if (_asleep)
            {
                if (_shownPeek != PeekPose.Sleep) { _peek.Source = PeekFrame(PeekPose.Sleep); _shownPeek = PeekPose.Sleep; }
                if (_tick >= _nextZ)
                {
                    // Z's drift up from above the head, alternating sides a little.
                    Float(Floater.Z, (_tick / 24) % 2 == 0 ? 17 : 20, StageHeight - CatSprites.PeekHeight - 4, rise: 9, ms: 2200);
                    _nextZ = _tick + 24;
                }
                PcNews();
                return;
            }
            PcNews();
            // Hiding: now and then a paw taps the edge.
            var peekPose = PeekPose.Idle;
            if (_tick >= _nextPeekTap)
            {
                peekPose = _tick % 2 == 0 ? PeekPose.LeftPaw : PeekPose.RightPaw;
                if (_tick >= _nextPeekTap + 2) _nextPeekTap = _tick + _random.Next(40, 90);
            }
            if (peekPose != _shownPeek) { _peek.Source = PeekFrame(peekPose); _shownPeek = peekPose; }
            if (_shownPose != CatPose.Idle) { _cat.Source = CatFrame(CatPose.Idle); _shownPose = CatPose.Idle; }
            return;
        }

        if (_shownPeek != PeekPose.Idle) { _peek.Source = PeekFrame(PeekPose.Idle); _shownPeek = PeekPose.Idle; }

        CatPose pose;
        if (_carried) pose = _startled > 0 ? CatPose.Startled : Math.Abs(_swing.Angle) > 12 ? CatPose.Pop : CatPose.Listen;
        else if (_clickPop > 0) { pose = CatPose.Pop; _clickPop--; }
        else if (_startled > 0) { pose = CatPose.Startled; if (--_startled == 0) Evaluate(); }
        else if (_yawn > 0) { pose = CatPose.Yawn; if (--_yawn == 0) Evaluate(); }
        else if (_grumpy > 0) { pose = (_tick / 3) % 2 == 0 ? CatPose.Grumpy : CatPose.GrumpyFlick; if (--_grumpy == 0) Evaluate(); }
        else if (_pointing > 0) { pose = _pointLeft ? CatPose.PointLeft : CatPose.PointRight; if (--_pointing == 0) Evaluate(); }
        else if (_talking) pose = _tick % 3 == 2 ? CatPose.Idle : CatPose.Talk;
        else if (_listening) pose = _tick % 30 < 2 ? CatPose.Blink : CatPose.Listen;
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

        if (_carried && _startled > 0) _startled--;
        if (pose != _shownPose)
        {
            _cat.Source = CatFrame(pose);
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
        _dragStartScreenY = p.Y / NativeMethods.SystemScale;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragging = true;
        CaptureMouse();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) TrackPetting(e.GetPosition(_stage));
        if (!_dragging || !IsMouseCaptured) return;
        NativeMethods.GetCursorPos(out var p);
        double x = p.X / NativeMethods.SystemScale, y = p.Y / NativeMethods.SystemScale;
        // A few pixels of wobble is still a click; past that, the cat is picked up.
        if (!_carried && Math.Abs(x - _dragStartScreenX) + Math.Abs(y - _dragStartScreenY) > 6) StartCarry();
        if (!_carried) return;
        // Held by its paws: they stay at the pointer, and the body swings behind the movement.
        Left = x - Width / 2;
        Top = y - 2 * _cell;
        var now = DateTime.Now;
        double dt = Math.Max(1, (now - _lastDragTime).TotalMilliseconds);
        double speed = (x - _lastDragX) / dt; // DIPs per millisecond
        _swingPull = Math.Clamp(-speed * 22, -34, 34);
        _lastDragX = x;
        _lastDragTime = now;
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        if (_carried)
        {
            NativeMethods.GetCursorPos(out var p);
            LetGo(new Point(p.X / NativeMethods.SystemScale, p.Y / NativeMethods.SystemScale));
            return;
        }
        _clickPop = 3; // a tap on the head gets a little "pop"
        Animate();
        Evaluate();
    }

    /// <summary>Picked up: out of hiding, hanging from the pointer by its paws, a little startled.</summary>
    void StartCarry()
    {
        _carried = true;
        _asleep = false;
        _out = true;
        _walkStep = 0;
        _startled = 5;
        _swingPull = 0;
        _swingVelocity = 0;
        NativeMethods.GetCursorPos(out var p);
        _lastDragX = p.X / NativeMethods.SystemScale;
        _lastDragTime = DateTime.Now;
        Touch();
        ApplyLayout();
        _physics.Start();
    }

    /// <summary>
    /// Let go: the cat picks the nearest edge of the screen (a corner goes to whichever edge is
    /// closer), jumps to it, and settles there. Where it landed is remembered.
    /// </summary>
    void LetGo(Point at)
    {
        var area = SystemParameters.WorkArea;
        var distances = new (CatEdge Edge, double Distance)[]
        {
            (CatEdge.Bottom, area.Bottom - at.Y), (CatEdge.Top, at.Y - area.Top),
            (CatEdge.Left, at.X - area.Left), (CatEdge.Right, area.Right - at.X),
        };
        var edge = distances.OrderBy(d => d.Distance).First().Edge;
        _carried = false;
        _physics.Stop();
        _edge = edge;
        _out = true;
        ApplyLayout();
        // Start where it was let go (centred on the pointer), then jump to the edge.
        double startLeft = at.X - Width / 2, startTop = at.Y - Height / 2;
        var target = Spot(edge, edge is CatEdge.Left or CatEdge.Right ? at.Y - Height / 2 : at.X - Width / 2);
        Left = startLeft;
        Top = startTop;
        var land = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 260 : 0);
        IEasingFunction ease = edge == CatEdge.Bottom ? new BounceEase { Bounces = 1, Bounciness = 4, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut };
        var moveX = new DoubleAnimation(startLeft, target.X, land) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var moveY = new DoubleAnimation(startTop, target.Y, land) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        moveY.Completed += (_, _) =>
        {
            Left = target.X;
            Top = target.Y;
            if (Hanging) Kick(_random.Next(2) == 0 ? 10 : -10);
        };
        BeginAnimation(LeftProperty, moveX);
        BeginAnimation(TopProperty, moveY);
        Left = target.X;
        Top = target.Y;
        _settings.CatEdge = edge.ToString().ToLowerInvariant();
        if (edge == CatEdge.Bottom) _settings.CatLeft = target.X;
        else _settings.CatAlong = edge is CatEdge.Left or CatEdge.Right ? target.Y : target.X;
        _settings.Save();
        Log.Info($"Cat moved to the {_settings.CatEdge} edge");
        // A happy moment on arrival, then back to hiding as usual.
        _celebrating = true;
        Evaluate();
        Later(1300, () => { _celebrating = false; Evaluate(); });
    }

    /// <summary>Where the window sits on an edge (top-left, DIPs), "along" being its left (top and bottom) or top (sides).</summary>
    Point Spot(CatEdge edge, double along)
    {
        var area = SystemParameters.WorkArea;
        return edge switch
        {
            CatEdge.Top => new Point(Snap(Math.Clamp(along, area.Left, area.Right - Width)), Snap(area.Top)),
            CatEdge.Left => new Point(Snap(area.Left), Snap(Math.Clamp(along, area.Top, area.Bottom - Height))),
            CatEdge.Right => new Point(Snap(area.Right - Width), Snap(Math.Clamp(along, area.Top, area.Bottom - Height))),
            // The bottom of the window is the top of the taskbar: that's the edge the cat hides behind.
            _ => new Point(Snap(Math.Clamp(along, area.Left, area.Right - Width)), Snap(area.Bottom - Height)),
        };
    }

    double AlongNow => _edge is CatEdge.Left or CatEdge.Right ? Top : Left;
    double? SavedAlong => _edge == CatEdge.Bottom ? _settings.CatLeft : _settings.CatAlong;

    /// <summary>Dev aid: moves the cat to an edge as if it had been dragged there and let go at this point (DIPs).</summary>
    public void DropAt(Point at)
    {
        _carried = true;
        ApplyLayout();
        LetGo(at);
    }

    /// <summary>Rubbing back and forth over the head (three turns in a second and a half) is petting.</summary>
    void TrackPetting(Point at)
    {
        if (!_out) return;
        double x = at.X / _cell, y = at.Y / _cell;
        int headTop = Hanging ? CatSprites.HangArms : CatTop;
        bool onHead = x >= CatX && x <= CatX + CatSprites.Width && y >= headTop && y <= headTop + 13;
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

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    int _lifted;

    /// <summary>
    /// The cat is meant to stay above everything, but Windows sometimes lets a window it brings
    /// forward (a browser opening a link, say) slip over it while it still says "on top". If the
    /// window in front is above the cat, lift the cat back.
    /// </summary>
    void KeepOnTop(IntPtr front)
    {
        var me = new WindowInteropHelper(this).Handle;
        if (front == IntPtr.Zero || me == IntPtr.Zero || !IsVisible) return;
        // Only an ordinary window from another app: the Start menu and Shiki's own cards may sit above.
        if ((GetWindowLong(front, -20) & 0x8) != 0) return;
        NativeMethods.GetWindowThreadProcessId(front, out uint pid);
        if (pid == (uint)Environment.ProcessId) return;
        // Walk up from the cat: meeting the front window means it's above.
        var w = me;
        for (int i = 0; i < 2000; i++)
        {
            w = GetWindow(w, 3); // the window above
            if (w == IntPtr.Zero) return;
            if (w == front) break;
        }
        if (w != front) return;
        const uint NoMove = 0x2, NoSize = 0x1, NoActivate = 0x10, NoOwnerOrder = 0x200;
        SetWindowPos(me, new IntPtr(-2), 0, 0, 0, 0, NoMove | NoSize | NoActivate | NoOwnerOrder); // not on top
        SetWindowPos(me, new IntPtr(-1), 0, 0, 0, 0, NoMove | NoSize | NoActivate | NoOwnerOrder); // on top again
        if (_lifted++ < 5) Log.Info("Cat lifted back on top (a window had slipped over it)");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int count);

    void Place()
    {
        if (_carried) return;
        var area = SystemParameters.WorkArea;
        bool side = _edge is CatEdge.Left or CatEdge.Right;
        double fallback = side ? area.Top + area.Height * 0.4 : area.Left + 10;
        var spot = Spot(_edge, SavedAlong ?? fallback);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = spot.X;
        Top = spot.Y;
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
