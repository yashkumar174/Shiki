using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shiki.Core;
using ImageMagick;
using IOPath = System.IO.Path;

namespace Shiki.UI;

/// <summary>
/// Remove a photo's background: the cut-out appears on a checkerboard, a brush paints back
/// anything missed (or erases what shouldn't be there), and it saves as a transparent PNG or on a
/// solid colour.
/// </summary>
public sealed class CutoutWindow : Window
{
    const int PreviewMax = 2048;
    static readonly double[] BrushSizes = [8, 16, 32, 64];
    static readonly (string Name, Color? Colour)[] Swatches =
    [
        ("Transparent", null),
        ("White", Colors.White),
        ("Black", Colors.Black),
        ("Light grey", Color.FromRgb(0xE6, 0xE6, 0xE6)),
        ("Blue", Color.FromRgb(0x43, 0x8E, 0xDB)),
        ("Red", Color.FromRgb(0xD9, 0x37, 0x2B)),
    ];

    readonly Palette _p = Theme.Editor;
    readonly string _file;
    readonly CancellationTokenSource _closing = new();

    // Full resolution
    int _fw, _fh;
    float[]? _alpha;
    // Preview resolution
    MagickImage? _small;
    int _sw, _sh;
    byte[] _rgb = [];
    float[] _baseAlpha = [], _editAlpha = [];
    byte[] _pixels = [];
    WriteableBitmap? _bitmap;
    BitmapSource? _original;
    int _composeVersion;

    readonly List<BrushStroke> _strokes = [];
    List<(double X, double Y)>? _stroke;
    bool _keep;
    int _brush = 1;
    Color? _background;

    // View: board coordinates → screen = board × zoom + offset
    readonly Grid _stage = new() { ClipToBounds = true, Background = Theme.Brush(Color.FromRgb(0x0E, 0x0E, 0x10)) };
    readonly Canvas _view = new();
    readonly Border _board = new();
    readonly Image _image = new();
    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly Ellipse _ringOuter = new() { Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), StrokeThickness = 3 };
    readonly Ellipse _ringInner = new() { Stroke = Brushes.White, StrokeThickness = 1.2 };
    readonly ScaleTransform _scale = new();
    readonly TranslateTransform _move = new();
    double _zoom = 1, _ox, _oy, _boardW, _boardH;
    Point _panStart, _panOrigin;
    bool _panning, _spaceDown;

    readonly TextBlock _status = UiKit.Body(""), _hint = UiKit.Body("", 11.5);
    readonly Border _track, _fill;
    readonly TranslateTransform _fillMove = new();
    readonly List<(Color? Colour, Border Swatch)> _swatches = [];
    readonly Border _custom;
    readonly Border _erase, _restore;
    readonly List<Border> _sizes = [];
    readonly Border _save;
    readonly StackPanel _tools = new();

    public CutoutWindow(string file)
    {
        _file = file;
        Title = $"Remove background: {IOPath.GetFileName(file)}";
        Width = 1120;
        Height = 780;
        MinWidth = 800;
        MinHeight = 560;
        UiKit.Prepare(this);

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _board.Child = _image;
        _board.Background = Checkerboard();
        _view.Children.Add(_board);
        _view.RenderTransform = new TransformGroup { Children = { _scale, _move } };
        _overlay.Children.Add(_ringOuter);
        _overlay.Children.Add(_ringInner);
        _stage.Children.Add(_view);
        _stage.Children.Add(_overlay);
        _stage.SizeChanged += (_, _) => FitView();
        _stage.MouseDown += OnDown;
        _stage.MouseMove += OnMove;
        _stage.MouseUp += OnUp;
        _stage.MouseLeave += (_, _) => ShowRing(false);
        _stage.MouseWheel += OnWheel;

        // Side panel
        var side = new DockPanel { Margin = new Thickness(22, 24, 22, 22), LastChildFill = false };
        var top = new StackPanel();
        top.Children.Add(UiKit.Title("Remove background"));
        top.Children.Add(_status);
        _fill = new Border { Background = Theme.Brush(Theme.EditorAccent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, RenderTransform = _fillMove };
        _track = new Border
        {
            Height = 4,
            Margin = new Thickness(0, 10, 0, 0),
            Background = Theme.Brush(_p.RowHover),
            CornerRadius = new CornerRadius(_p.Pixel ? 0 : 2),
            ClipToBounds = true,
            Child = _fill,
        };
        top.Children.Add(_track);

        top.Children.Add(UiKit.Heading("Background"));
        var swatches = new WrapPanel();
        foreach (var (name, colour) in Swatches)
        {
            var swatch = Swatch(colour, name, () => SetBackground(colour));
            _swatches.Add((colour, swatch));
            swatches.Children.Add(swatch);
        }
        _custom = Swatch(null, "Pick a colour", PickColour, label: "+");
        swatches.Children.Add(_custom);
        top.Children.Add(swatches);

        _tools.Children.Add(UiKit.Heading("Touch up"));
        _erase = UiKit.Button("Erase", () => SetMode(false));
        _restore = UiKit.Button("Restore", () => SetMode(true));
        _tools.Children.Add(UiKit.Row(_erase, _restore));
        var sizes = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        for (int i = 0; i < BrushSizes.Length; i++)
        {
            int index = i;
            var size = UiKit.Button(new[] { "S", "M", "L", "XL" }[i], () => SetBrush(index));
            size.ToolTip = "Brush size ([ and ] keys)";
            _sizes.Add(size);
            sizes.Children.Add(size);
        }
        _tools.Children.Add(sizes);
        var undo = UiKit.Button("Undo", Undo);
        undo.ToolTip = "Ctrl+Z";
        var compare = UiKit.Button("Hold to compare", () => { });
        compare.PreviewMouseLeftButtonDown += (_, _) => { if (_original != null) _image.Source = _original; };
        compare.PreviewMouseLeftButtonUp += (_, _) => _image.Source = _bitmap;
        compare.MouseLeave += (_, _) => _image.Source = _bitmap;
        var actions = UiKit.Row(undo, compare);
        actions.Margin = new Thickness(0, 10, 0, 0);
        _tools.Children.Add(actions);
        _hint.Text = "Paint over anything the cut missed. Scroll to zoom, right-drag to move around.";
        _tools.Children.Add(_hint);
        _tools.IsEnabled = false;
        _tools.Opacity = 0.45;
        top.Children.Add(_tools);
        DockPanel.SetDock(top, Dock.Top);
        side.Children.Add(top);

        var bottom = new StackPanel();
        _save = UiKit.Button("Save PNG", Save, primary: true, stretch: true);
        bottom.Children.Add(_save);
        bottom.Children.Add(UiKit.Button("Copy", Copy, stretch: true));
        bottom.Children.Add(UiKit.Button("Cancel", Close, stretch: true));
        DockPanel.SetDock(bottom, Dock.Bottom);
        side.Children.Add(bottom);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.Children.Add(_stage);
        Grid.SetColumn(side, 1);
        root.Children.Add(side);
        Content = root;

        PreviewKeyDown += OnKey;
        PreviewKeyUp += (_, e) => { if (e.Key == Key.Space) _spaceDown = false; };
        Closed += (_, _) => { _closing.Cancel(); _small?.Dispose(); };
        SetMode(false);
        SetBrush(1);
        SetBackground(null);
        _ = LoadAsync();
    }

    bool Ready => _alpha != null;

    // ---- Loading and cutting out ----

    async Task LoadAsync()
    {
        var ct = _closing.Token;
        try
        {
            _status.Text = "Opening…";
            await Task.Run(() =>
            {
                using var full = ImageTools.Load(_file);
                _fw = (int)full.Width;
                _fh = (int)full.Height;
                var small = (MagickImage)full.Clone();
                if (Math.Max(_fw, _fh) > PreviewMax) small.Resize(new MagickGeometry(PreviewMax, PreviewMax));
                ImageTools.FlattenOnWhite(small);
                _small = small;
                _sw = (int)small.Width;
                _sh = (int)small.Height;
                _rgb = small.GetPixels().ToByteArray(PixelMapping.RGB)!;
            }, ct);
            _original = ToBitmap(_rgb, null);
            _image.Source = _original;
            _image.Opacity = 0.4;
            _board.Background = Brushes.Transparent;
            FitView();

            if (!Cutout.IsDownloaded)
            {
                _status.Text = $"Getting the background model ({Cutout.DownloadSize}, one time)…";
                await Cutout.EnsureModel(new Progress<double>(ShowProgress), ct);
            }

            _status.Text = "Finding the subject…";
            Busy(true);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var alpha = await Task.Run(() =>
            {
                using var full = ImageTools.Load(_file);
                return Cutout.Matte(full);
            }, ct);
            var preview = await Task.Run(() => Cutout.Resize(alpha, _fw, _fh, _sw, _sh), ct);
            if (ct.IsCancellationRequested) return;
            Log.Info($"Cutout of {_fw}x{_fh} in {watch.ElapsedMilliseconds} ms on the {Cutout.Device}");

            _alpha = alpha;
            _baseAlpha = preview;
            _editAlpha = (float[])preview.Clone();
            _pixels = new byte[_sw * _sh * 4];
            _bitmap = new WriteableBitmap(_sw, _sh, 96, 96, PixelFormats.Bgra32, null);
            await Recompose();
            _image.Source = _bitmap;
            _image.Opacity = 1;
            SetBackground(_background);
            Busy(false);
            _tools.IsEnabled = true;
            _tools.Opacity = 1;

            double covered = preview.Average();
            _status.Text = covered < 0.005
                ? "Couldn't find a clear subject. Use Restore to paint it in."
                : "Done. Touch up anything the cut missed, then save.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Remove background failed for {_file}", ex);
            Busy(false);
            _status.Text = ex is System.Net.Http.HttpRequestException
                ? "Couldn't download the background model. Check the internet connection and try again."
                : "Couldn't remove the background of this picture.";
        }
    }

    void ShowProgress(double fraction)
    {
        _fill.BeginAnimation(WidthProperty, null);
        _fillMove.BeginAnimation(TranslateTransform.XProperty, null);
        _fillMove.X = 0;
        _fill.Width = Math.Clamp(fraction, 0, 1) * _track.ActualWidth;
    }

    /// <summary>A sliding bar while the model works (it can't report its own progress).</summary>
    void Busy(bool on)
    {
        if (!on)
        {
            _fillMove.BeginAnimation(TranslateTransform.XProperty, null);
            _track.Visibility = Visibility.Collapsed;
            return;
        }
        _track.Visibility = Visibility.Visible;
        double w = Math.Max(_track.ActualWidth, 200);
        _fill.Width = w * 0.3;
        _fillMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-w * 0.3, w, TimeSpan.FromSeconds(1.3))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    /// <summary>Cleans the preview's edge colours with the current matte (in the background).</summary>
    async Task Recompose()
    {
        if (_small == null) return;
        int version = ++_composeVersion;
        var alpha = (float[])_editAlpha.Clone();
        var small = _small;
        var bgra = await Task.Run(() =>
        {
            using var cut = Cutout.Compose(small, alpha);
            return cut.GetPixels().ToByteArray(PixelMapping.BGRA)!;
        });
        if (version != _composeVersion || _bitmap == null) return;
        _pixels = bgra;
        _bitmap.WritePixels(new Int32Rect(0, 0, _sw, _sh), _pixels, _sw * 4, 0);
    }

    BitmapSource ToBitmap(byte[] rgb, float[]? alpha)
    {
        var bgra = new byte[_sw * _sh * 4];
        for (int i = 0, n = _sw * _sh; i < n; i++)
        {
            bgra[i * 4] = rgb[i * 3 + 2];
            bgra[i * 4 + 1] = rgb[i * 3 + 1];
            bgra[i * 4 + 2] = rgb[i * 3];
            bgra[i * 4 + 3] = alpha == null ? (byte)255 : (byte)(alpha[i] * 255 + 0.5f);
        }
        var bitmap = BitmapSource.Create(_sw, _sh, 96, 96, PixelFormats.Bgra32, null, bgra, _sw * 4);
        bitmap.Freeze();
        return bitmap;
    }

    // ---- View ----

    void FitView()
    {
        if (_sw == 0 || _stage.ActualWidth == 0) return;
        double pad = 30;
        double scale = Math.Min((_stage.ActualWidth - pad * 2) / _sw, (_stage.ActualHeight - pad * 2) / _sh);
        _boardW = _sw * scale;
        _boardH = _sh * scale;
        _board.Width = _image.Width = _boardW;
        _board.Height = _image.Height = _boardH;
        _zoom = 1;
        _ox = (_stage.ActualWidth - _boardW) / 2;
        _oy = (_stage.ActualHeight - _boardH) / 2;
        ApplyView();
    }

    void ApplyView()
    {
        _scale.ScaleX = _scale.ScaleY = _zoom;
        _move.X = _ox;
        _move.Y = _oy;
        // Pixel peeping: show real pixels once zoomed well past the preview's size.
        RenderOptions.SetBitmapScalingMode(_image, _zoom * _boardW / _sw > 2.5 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_sw == 0) return;
        var at = e.GetPosition(_stage);
        double next = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.5, 24);
        double bx = (at.X - _ox) / _zoom, by = (at.Y - _oy) / _zoom;
        _zoom = next;
        _ox = at.X - bx * _zoom;
        _oy = at.Y - by * _zoom;
        ApplyView();
        UpdateRing(at);
    }

    /// <summary>Screen (stage) point → preview pixel.</summary>
    (double X, double Y) ToPreview(Point at) =>
        ((at.X - _ox) / _zoom / _boardW * _sw, (at.Y - _oy) / _zoom / _boardH * _sh);

    double PreviewPerScreen => _sw / (_boardW * _zoom);
    double BrushScreenRadius => BrushSizes[_brush] / 2;

    // ---- Painting and panning ----

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(_stage);
        if (e.ChangedButton is MouseButton.Right or MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceDown))
        {
            _panning = true;
            _panStart = at;
            _panOrigin = new Point(_ox, _oy);
            _stage.CaptureMouse();
            _stage.Cursor = Cursors.SizeAll;
            ShowRing(false);
            return;
        }
        if (e.ChangedButton != MouseButton.Left || !Ready) return;
        _composeVersion++;  // an edge clean-up still running would undo this stroke's live preview
        _stroke = [];
        _stage.CaptureMouse();
        PaintTo(at);
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(_stage);
        if (_panning)
        {
            _ox = _panOrigin.X + at.X - _panStart.X;
            _oy = _panOrigin.Y + at.Y - _panStart.Y;
            ApplyView();
            return;
        }
        UpdateRing(at);
        if (_stroke != null) PaintTo(at);
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        _stage.ReleaseMouseCapture();
        if (_panning)
        {
            _panning = false;
            _stage.Cursor = null;
            UpdateRing(e.GetPosition(_stage));
            return;
        }
        if (_stroke == null) return;
        // Strokes are kept in full-resolution pixels so saving redraws them exactly.
        double toFull = (double)_fw / _sw;
        _strokes.Add(new BrushStroke(_stroke.Select(p => (p.X * toFull, p.Y * toFull)).ToList(), BrushScreenRadius * PreviewPerScreen * toFull, _keep));
        _stroke = null;
        _ = Recompose();
    }

    void PaintTo(Point at)
    {
        var point = ToPreview(at);
        double r = BrushScreenRadius * PreviewPerScreen;
        var from = _stroke!.Count > 0 ? _stroke[^1] : point;
        _stroke.Add(point);
        double length = Math.Sqrt((point.X - from.X) * (point.X - from.X) + (point.Y - from.Y) * (point.Y - from.Y));
        int steps = Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.5, r / 4)));
        for (int k = _stroke.Count == 1 ? 0 : 1; k <= steps; k++)
        {
            double t = (double)k / steps;
            Cutout.Dab(_editAlpha, _sw, _sh, from.X + (point.X - from.X) * t, from.Y + (point.Y - from.Y) * t, r, _keep);
        }
        // Show it straight away; the edge colours are cleaned when the stroke ends.
        int x0 = Math.Max(0, (int)(Math.Min(from.X, point.X) - r - 2)), x1 = Math.Min(_sw - 1, (int)(Math.Max(from.X, point.X) + r + 2));
        int y0 = Math.Max(0, (int)(Math.Min(from.Y, point.Y) - r - 2)), y1 = Math.Min(_sh - 1, (int)(Math.Max(from.Y, point.Y) + r + 2));
        if (x1 < x0 || y1 < y0) return;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * _sw + x;
                byte a = (byte)(_editAlpha[i] * 255 + 0.5f);
                if (_keep && a > _pixels[i * 4 + 3])
                {
                    _pixels[i * 4] = _rgb[i * 3 + 2];
                    _pixels[i * 4 + 1] = _rgb[i * 3 + 1];
                    _pixels[i * 4 + 2] = _rgb[i * 3];
                }
                _pixels[i * 4 + 3] = a;
            }
        _bitmap!.WritePixels(new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), _pixels, _sw * 4, x0 * 4 + y0 * _sw * 4);
    }

    void Undo()
    {
        if (!Ready || _strokes.Count == 0) return;
        _strokes.RemoveAt(_strokes.Count - 1);
        _editAlpha = (float[])_baseAlpha.Clone();
        Cutout.Paint(_editAlpha, _sw, _sh, _strokes, (double)_sw / _fw);
        _ = Recompose();
    }

    void UpdateRing(Point at)
    {
        bool over = Ready && !_panning && at.X >= _ox && at.Y >= _oy && at.X <= _ox + _boardW * _zoom && at.Y <= _oy + _boardH * _zoom;
        ShowRing(over);
        if (!over) return;
        double d = BrushScreenRadius * 2;
        foreach (var ring in new[] { _ringOuter, _ringInner })
        {
            ring.Width = ring.Height = d;
            Canvas.SetLeft(ring, at.X - d / 2);
            Canvas.SetTop(ring, at.Y - d / 2);
        }
    }

    void ShowRing(bool on)
    {
        _ringOuter.Visibility = _ringInner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!_panning) _stage.Cursor = on ? Cursors.None : null;
    }

    void SetMode(bool keep)
    {
        _keep = keep;
        UiKit.Select(_erase, !keep);
        UiKit.Select(_restore, keep);
        _ringInner.Stroke = keep ? Theme.Brush(Color.FromRgb(0x7D, 0xE3, 0x9A)) : Brushes.White;
    }

    void SetBrush(int index)
    {
        _brush = Math.Clamp(index, 0, BrushSizes.Length - 1);
        for (int i = 0; i < _sizes.Count; i++) UiKit.Select(_sizes[i], i == _brush);
        UpdateRing(Mouse.GetPosition(_stage));
    }

    // ---- Background colour ----

    Border Swatch(Color? colour, string name, Action onClick, string? label = null)
    {
        var swatch = new Border
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(_p.Pixel ? 0 : 8),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = colour is { } c ? Theme.Brush(c) : label == null ? Checkerboard(6) : Theme.Brush(_p.RowHover),
            Cursor = Cursors.Hand,
            ToolTip = name,
            Child = label == null ? null : new TextBlock
            {
                Text = label,
                FontFamily = Theme.TextFont,
                FontSize = 15,
                Foreground = Theme.Brush(_p.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        swatch.MouseLeftButtonUp += (_, _) => onClick();
        return swatch;
    }

    void SetBackground(Color? colour)
    {
        _background = colour;
        _board.Background = colour is { } c ? Theme.Brush(c) : Checkerboard();
        bool preset = Swatches.Any(s => s.Colour == colour);
        foreach (var (swatchColour, swatch) in _swatches) UiKit.Select(swatch, preset && swatchColour == colour);
        UiKit.Select(_custom, !preset);
        if (!preset && colour is { } custom) _custom.Background = Theme.Brush(custom);
        ((TextBlock)_save.Child).Text = colour == null ? "Save PNG" : "Save JPG";
    }

    void PickColour()
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (_background is { } c) dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        SetBackground(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    Brush Checkerboard(double cell = 10)
    {
        var (a, b) = _p.Dark ? (Color.FromRgb(0x3A, 0x3A, 0x3E), Color.FromRgb(0x2C, 0x2C, 0x30)) : (Colors.White, Color.FromRgb(0xE4, 0xE4, 0xE8));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Theme.Brush(a), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        group.Children.Add(new GeometryDrawing(Theme.Brush(b), null, new GeometryGroup
        {
            Children = { new RectangleGeometry(new Rect(0, 0, 1, 1)), new RectangleGeometry(new Rect(1, 1, 1, 1)) },
        }));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell * 2, cell * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
        brush.Freeze();
        return brush;
    }

    // ---- Keys ----

    void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.S when ctrl: Save(); break;
            case Key.C when ctrl: Copy(); break;
            case Key.Enter: Save(); break;
            case Key.OemOpenBrackets: SetBrush(_brush - 1); break;
            case Key.OemCloseBrackets: SetBrush(_brush + 1); break;
            case Key.E: SetMode(false); break;
            case Key.R: SetMode(true); break;
            case Key.D0 or Key.NumPad0: FitView(); break;
            case Key.Space: _spaceDown = true; break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- Saving ----

    /// <summary>Redraws the touch-ups on the full-resolution matte and composes the result.</summary>
    static MagickImage Render(string file, float[] alpha, int w, int h, IReadOnlyList<BrushStroke> strokes, Color? background)
    {
        using var photo = ImageTools.Load(file);
        var matte = (float[])alpha.Clone();
        Cutout.Paint(matte, w, h, strokes, 1);
        var cut = Cutout.Compose(photo, matte);
        if (background is { } c)
        {
            cut.BackgroundColor = new MagickColor(c.R, c.G, c.B);
            cut.Alpha(AlphaAction.Remove);
            cut.Alpha(AlphaAction.Off);
        }
        return cut;
    }

    BroAction Job(string id, string label, ActionRunner run) => new()
    {
        Id = id,
        Label = label,
        Short = label,
        Hint = "",
        Kind = FileKind.Image,
        AcceptsExtension = _ => true,
        Run = run,
    };

    void Save()
    {
        if (!Ready) return;
        var (file, alpha, w, h, strokes, background) = (_file, _alpha!, _fw, _fh, _strokes.ToList(), _background);
        JobRunner.Start(Job("cutout-save", "Remove background", (_, _, ct) => Task.Run<IReadOnlyList<string>>(() =>
        {
            using var cut = Render(file, alpha, w, h, strokes, background);
            string output;
            if (background == null)
            {
                output = OutputPaths.Next(file, ".png", "no background");
                Cutout.WritePng(cut, output);
            }
            else
            {
                output = OutputPaths.Next(file, ".jpg", "new background");
                cut.Quality = 95;
                cut.Write(output, MagickFormat.Jpeg);
            }
            return [output];
        }, ct)), [file]);
        Close();
    }

    /// <summary>Copies the result to paste straight into a chat, doc or design (the window stays open).</summary>
    void Copy()
    {
        if (!Ready) return;
        var (file, alpha, w, h, strokes, background) = (_file, _alpha!, _fw, _fh, _strokes.ToList(), _background);
        JobRunner.Start(Job("cutout-copy", "Copy cut-out", (_, _, ct) => Task.Run<IReadOnlyList<string>>(() =>
        {
            using var cut = Render(file, alpha, w, h, strokes, background);
            var folder = IOPath.Combine(IOPath.GetTempPath(), "Shiki");
            Directory.CreateDirectory(folder);
            var temp = IOPath.Combine(folder, IOPath.GetFileNameWithoutExtension(file) + " (no background).png");
            Cutout.WritePng(cut, temp);
            ClipboardTools.CopyImage(temp);
            return [];
        }, ct)), [file]);
    }
}
