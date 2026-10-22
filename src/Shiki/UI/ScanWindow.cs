using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shiki.Core;
using Shiki.Shell;
using ImageMagick;
using IOPath = System.IO.Path;
using Path = System.Windows.Shapes.Path;
using FontWeight = System.Windows.FontWeight;
using FontWeights = System.Windows.FontWeights;
using Orientation = System.Windows.Controls.Orientation;

namespace Shiki.UI;

/// <summary>
/// The scanner: one or more photos of paper, each with four draggable corners that start where
/// auto-detect thinks the page is. Saves straightened, cleaned-up pages as one PDF or as images.
/// </summary>
public sealed class ScanWindow : Window
{
    const double PreviewMax = 1600, HandleSize = 22, LoupeSize = 150, LoupeZoom = 3;

    sealed class Page
    {
        public required string Path { get; init; }
        public required MagickImage Preview { get; init; }
        public required BitmapSource PreviewBitmap { get; init; }
        /// <summary>Full-resolution size after camera rotation.</summary>
        public required Size FullSize { get; init; }
        public Point[] AutoCorners { get; set; } = [];
        /// <summary>Corners in full-resolution pixels: top-left, top-right, bottom-right, bottom-left.</summary>
        public Point[] Corners { get; set; } = [];
        public int Rotation { get; set; }
        public double Scale => FullSize.Width / Preview.Width;
    }

    readonly Palette _p = Theme.Editor;
    readonly bool _pixel = Theme.Editor.Pixel;
    readonly List<Page> _pages = [];
    int _index;
    ScanFilter _filter = ScanFilter.Color;
    bool _showResult;
    int _resultVersion;

    readonly Grid _stage = new() { ClipToBounds = true };
    readonly Canvas _canvas = new();
    readonly Image _photo = new();
    readonly Image _result = new() { Visibility = Visibility.Collapsed };
    readonly Path _dim = new() { IsHitTestVisible = false };
    readonly Polygon _outline = new() { StrokeThickness = 2, IsHitTestVisible = false };
    readonly Ellipse[] _handles = new Ellipse[4];
    readonly Grid _loupe = new() { Width = LoupeSize, Height = LoupeSize, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly ImageBrush _loupeBrush = new() { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
    readonly TextBlock _status = new(), _pager = new();
    readonly StackPanel _pagerRow = new() { Orientation = Orientation.Horizontal };
    readonly List<(ScanFilter Filter, Border Chip)> _filterChips = [];
    Border? _resultChip;
    Rect _fit;
    int _dragging = -1;

    public ScanWindow(IReadOnlyList<string> files)
    {
        Title = files.Count == 1 ? $"Scan {IOPath.GetFileName(files[0])}" : $"Scan {files.Count} photos";
        Width = 1140;
        Height = 760;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Brush(Color.FromRgb(_p.Surface.R, _p.Surface.G, _p.Surface.B));
        UseLayoutRounding = true;
        if (_pixel) TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);

        var accent = Theme.Accent(FileKind.Image);
        _outline.Stroke = Theme.Brush(accent);
        _dim.Fill = Theme.Brush(Color.FromArgb(0x99, 0, 0, 0));
        RenderOptions.SetBitmapScalingMode(_photo, BitmapScalingMode.HighQuality);

        _canvas.Children.Add(_photo);
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_outline);
        for (int i = 0; i < 4; i++)
        {
            int corner = i;
            var handle = new Ellipse
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = Theme.Brush(accent),
                StrokeThickness = 3,
                Cursor = Cursors.SizeAll,
            };
            handle.MouseLeftButtonDown += (_, e) => { _dragging = corner; handle.CaptureMouse(); e.Handled = true; ShowLoupe(); };
            handle.MouseMove += (_, e) => { if (_dragging == corner) Drag(e.GetPosition(_canvas)); };
            handle.MouseLeftButtonUp += (_, _) => { _dragging = -1; handle.ReleaseMouseCapture(); _loupe.Visibility = Visibility.Collapsed; RefreshResult(); };
            _handles[i] = handle;
            _canvas.Children.Add(handle);
        }

        BuildLoupe(accent);
        _canvas.Children.Add(_loupe);
        _stage.Children.Add(_canvas);
        _stage.Children.Add(_result);
        _stage.Background = Theme.Brush(Color.FromRgb(0x0E, 0x0E, 0x10));
        _stage.SizeChanged += (_, _) => Layout();

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.Children.Add(_stage);
        var side = BuildSidebar();
        Grid.SetColumn(side, 1);
        root.Children.Add(side);
        Content = root;

        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        // Opened from Explorer or the wheel: come to the front even if another app has focus.
        Loaded += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return; // inside the dashboard
            NativeMethods.ForceForeground(handle);
            Activate();
        };
        KeyDown += OnKeyDown;
        Closed += (_, _) => { foreach (var page in _pages) page.Preview.Dispose(); };

        _status.Text = "Finding the page…";
        _ = LoadAsync(files);
    }

    // ---- Loading ----

    async Task LoadAsync(IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                var page = await Task.Run(() =>
                {
                    var photo = ImageTools.Load(file);
                    var full = new Size(photo.Width, photo.Height);
                    if (Math.Max(photo.Width, photo.Height) > PreviewMax) photo.Resize(new MagickGeometry((uint)PreviewMax, (uint)PreviewMax));
                    var bitmap = ToBitmap(photo);
                    var p = new Page { Path = file, Preview = photo, PreviewBitmap = bitmap, FullSize = full };
                    double scale = p.Scale;
                    p.AutoCorners = ScanTools.DetectPage(photo).Select(c => new Point(c.X * scale, c.Y * scale)).ToArray();
                    p.Corners = p.AutoCorners.ToArray();
                    return p;
                });
                _pages.Add(page);
                if (_pages.Count == 1) ShowPage(0);
                UpdatePager();
            }
            catch (Exception ex)
            {
                Log.Error($"Scan could not open {file}", ex);
            }
        }
        if (_pages.Count == 0)
        {
            _status.Text = "Couldn't open these photos.";
            return;
        }
        _status.Text = "Drag the corners onto the edges of the page.";
    }

    static BitmapSource ToBitmap(MagickImage image)
    {
        var bytes = image.ToByteArray(MagickFormat.Bmp);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    Page? Current => _index < _pages.Count ? _pages[_index] : null;

    void ShowPage(int index)
    {
        _index = Math.Clamp(index, 0, _pages.Count - 1);
        _photo.Source = Current?.PreviewBitmap;
        _loupeBrush.ImageSource = Current?.PreviewBitmap;
        Layout();
        UpdatePager();
        RefreshResult();
    }

    // ---- Geometry: full-resolution pixels to canvas and back ----

    void Layout()
    {
        if (Current is not { } page) return;
        double pad = 36;
        double aw = Math.Max(10, _stage.ActualWidth - pad * 2), ah = Math.Max(10, _stage.ActualHeight - pad * 2);
        double scale = Math.Min(aw / page.FullSize.Width, ah / page.FullSize.Height);
        double w = page.FullSize.Width * scale, h = page.FullSize.Height * scale;
        _fit = new Rect((_stage.ActualWidth - w) / 2, (_stage.ActualHeight - h) / 2, w, h);
        _photo.Width = w;
        _photo.Height = h;
        Canvas.SetLeft(_photo, _fit.X);
        Canvas.SetTop(_photo, _fit.Y);
        UpdateOverlay();
    }

    Point ToCanvas(Point full) => new(_fit.X + full.X / Current!.FullSize.Width * _fit.Width, _fit.Y + full.Y / Current.FullSize.Height * _fit.Height);

    Point ToFull(Point canvas) => new(
        Math.Clamp((canvas.X - _fit.X) / _fit.Width, 0, 1) * Current!.FullSize.Width,
        Math.Clamp((canvas.Y - _fit.Y) / _fit.Height, 0, 1) * Current.FullSize.Height);

    void UpdateOverlay()
    {
        if (Current is not { } page || page.Corners.Length != 4) return;
        var points = page.Corners.Select(ToCanvas).ToArray();
        _outline.Points = new PointCollection(points);
        var quad = new StreamGeometry();
        using (var ctx = quad.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        // Darken everything outside the page so it's obvious what will be kept.
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(_fit), quad);
        for (int i = 0; i < 4; i++)
        {
            Canvas.SetLeft(_handles[i], points[i].X - HandleSize / 2);
            Canvas.SetTop(_handles[i], points[i].Y - HandleSize / 2);
        }
    }

    void Drag(Point canvas)
    {
        if (Current is not { } page) return;
        page.Corners[_dragging] = ToFull(canvas);
        UpdateOverlay();
        ShowLoupe();
    }

    // ---- Loupe: a magnified view under the corner being dragged ----

    void BuildLoupe(Color accent)
    {
        var lens = new Ellipse { Fill = _loupeBrush, Stroke = Brushes.White, StrokeThickness = 3 };
        var ring = new Ellipse { Stroke = Theme.Brush(accent), StrokeThickness = 1.5, Margin = new Thickness(3) };
        var crossH = new Rectangle { Height = 1.5, Width = 22, Fill = Theme.Brush(accent) };
        var crossV = new Rectangle { Width = 1.5, Height = 22, Fill = Theme.Brush(accent) };
        _loupe.Children.Add(lens);
        _loupe.Children.Add(ring);
        _loupe.Children.Add(crossH);
        _loupe.Children.Add(crossV);
        _loupe.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.5 };
    }

    void ShowLoupe()
    {
        if (Current is not { } page || _dragging < 0) return;
        var corner = page.Corners[_dragging];
        var at = ToCanvas(corner);
        double previewX = corner.X / page.Scale, previewY = corner.Y / page.Scale;
        double span = LoupeSize / LoupeZoom * (page.Preview.Width / _fit.Width);
        _loupeBrush.Viewbox = new Rect(previewX - span / 2, previewY - span / 2, span, span);
        // Sit away from the finger/cursor, toward the middle of the photo.
        double ox = at.X < _fit.X + _fit.Width / 2 ? 40 : -40 - LoupeSize;
        double oy = at.Y < _fit.Y + _fit.Height / 2 ? 40 : -40 - LoupeSize;
        Canvas.SetLeft(_loupe, at.X + ox);
        Canvas.SetTop(_loupe, at.Y + oy);
        _loupe.Visibility = Visibility.Visible;
    }

    // ---- Result preview ----

    void RefreshResult()
    {
        if (!_showResult || Current is not { } page)
        {
            _result.Visibility = Visibility.Collapsed;
            _canvas.Visibility = Visibility.Visible;
            return;
        }
        int version = ++_resultVersion;
        var corners = page.Corners.Select(c => new Point(c.X / page.Scale, c.Y / page.Scale)).ToArray();
        int rotation = page.Rotation;
        var filter = _filter;
        var source = page.Preview;
        Task.Run(() =>
        {
            using var processed = ScanTools.Process(source, corners, rotation, filter);
            return ToBitmap(processed);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) { Log.Error("Scan preview failed", t.Exception); return; }
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _resultVersion || !_showResult) return;
                _result.Source = t.Result;
                _result.Margin = new Thickness(36);
                _result.Visibility = Visibility.Visible;
                _canvas.Visibility = Visibility.Collapsed;
            });
        });
    }

    // ---- Sidebar ----

    UIElement BuildSidebar()
    {
        var side = new DockPanel { Margin = new Thickness(22, 24, 22, 22), LastChildFill = false };

        var top = new StackPanel();
        top.Children.Add(Text("Scan", Theme.DisplayFont, 22, _p.Text, _pixel ? FontWeights.Normal : FontWeights.SemiBold));
        _status.FontFamily = Theme.TextFont;
        _status.FontSize = 12.5;
        _status.Foreground = Theme.Brush(_p.SubtleText);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 4, 0, 0);
        top.Children.Add(_status);

        _pager.FontFamily = Theme.TextFont;
        _pager.FontSize = 13;
        _pager.Foreground = Theme.Brush(_p.Text);
        _pager.VerticalAlignment = VerticalAlignment.Center;
        _pager.Margin = new Thickness(10, 0, 10, 0);
        _pagerRow.Margin = new Thickness(0, 16, 0, 0);
        _pagerRow.Children.Add(Button("\uE76B", () => ShowPage(_index - 1), icon: true));
        _pagerRow.Children.Add(_pager);
        _pagerRow.Children.Add(Button("\uE76C", () => ShowPage(_index + 1), icon: true));
        top.Children.Add(_pagerRow);

        top.Children.Add(Heading("Crop"));
        var crop = new WrapPanel();
        crop.Children.Add(Button("Auto-detect", () => { if (Current is { } p) { p.Corners = p.AutoCorners.ToArray(); UpdateOverlay(); RefreshResult(); } }));
        crop.Children.Add(Button("Whole photo", () =>
        {
            if (Current is not { } p) return;
            p.Corners = [new(0, 0), new(p.FullSize.Width, 0), new(p.FullSize.Width, p.FullSize.Height), new(0, p.FullSize.Height)];
            UpdateOverlay();
            RefreshResult();
        }));
        crop.Children.Add(Button("Rotate", () => { if (Current is { } p) { p.Rotation = (p.Rotation + 90) % 360; SetResult(true); } }));
        top.Children.Add(crop);

        top.Children.Add(Heading("Look"));
        var looks = new WrapPanel();
        foreach (var (filter, label) in new[] { (ScanFilter.Color, "Clean colour"), (ScanFilter.BlackWhite, "Black and white"), (ScanFilter.Original, "Original") })
        {
            var chip = Button(label, () => { _filter = filter; UpdateChips(); SetResult(true); });
            _filterChips.Add((filter, (Border)chip));
            looks.Children.Add(chip);
        }
        top.Children.Add(looks);

        top.Children.Add(Heading("View"));
        _resultChip = (Border)Button("Show result", () => SetResult(!_showResult));
        top.Children.Add(_resultChip);
        UpdateChips();

        DockPanel.SetDock(top, Dock.Top);
        side.Children.Add(top);

        var bottom = new StackPanel();
        bottom.Children.Add(Button("Save as PDF", () => Save(pdf: true), primary: true, stretch: true));
        bottom.Children.Add(Button("Save as images", () => Save(pdf: false), stretch: true));
        bottom.Children.Add(Button("Cancel", Close, stretch: true));
        DockPanel.SetDock(bottom, Dock.Bottom);
        side.Children.Add(bottom);
        return side;
    }

    void SetResult(bool on)
    {
        _showResult = on;
        UpdateChips();
        RefreshResult();
    }

    void UpdateChips()
    {
        foreach (var (filter, chip) in _filterChips) Select(chip, filter == _filter);
        if (_resultChip != null) Select(_resultChip, _showResult);
    }

    void Select(Border chip, bool on) => chip.BorderBrush = Theme.Brush(on ? Theme.EditorAccent : Colors.Transparent);

    void UpdatePager()
    {
        _pagerRow.Visibility = _pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _pager.Text = $"Page {_index + 1} of {_pages.Count}";
    }

    TextBlock Text(string text, FontFamily font, double size, Color color, FontWeight weight) => new()
    {
        Text = text,
        FontFamily = font,
        FontSize = size,
        FontWeight = weight,
        Foreground = Theme.Brush(color),
    };

    TextBlock Heading(string text)
    {
        var heading = Text(text, Theme.TextFont, 12.5, _p.SubtleText, FontWeights.Normal);
        heading.Margin = new Thickness(0, 22, 0, 8);
        return heading;
    }

    FrameworkElement Button(string label, Action onClick, bool primary = false, bool icon = false, bool stretch = false)
    {
        var accent = Theme.EditorAccent;
        var rest = primary ? accent : _p.RowHover;
        var hot = primary ? Theme.Lighten(accent, 0.12) : Theme.Lighten(_p.RowHover, 0.08);
        var background = new SolidColorBrush(rest);
        var button = new Border
        {
            Background = background,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(_pixel ? 2 : 1.5),
            CornerRadius = new CornerRadius(_pixel ? 0 : 8),
            Padding = new Thickness(icon ? 8 : 12, 7, icon ? 8 : 12, 8),
            Margin = new Thickness(0, 0, stretch ? 0 : 6, stretch ? 8 : 6),
            Cursor = Cursors.Hand,
            HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = label,
                FontFamily = icon ? Theme.IconFont : Theme.TextFont,
                FontSize = icon ? 12 : 13,
                Foreground = Theme.Brush(primary ? Theme.EditorAccentText : _p.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        button.MouseEnter += (_, _) => background.Color = hot;
        button.MouseLeave += (_, _) => background.Color = rest;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }

    // ---- Keys and saving ----

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter: Save(pdf: true); break;
            case Key.Left: ShowPage(_index - 1); break;
            case Key.Right: ShowPage(_index + 1); break;
        }
    }

    void Save(bool pdf)
    {
        if (_pages.Count == 0) return;
        var pages = _pages.Select(p => (p.Path, Corners: p.Corners.ToArray(), p.Rotation)).ToList();
        var filter = _filter;
        var action = new BroAction
        {
            Id = pdf ? "scan-pdf" : "scan-images",
            Label = "Scan",
            Short = pdf ? "PDF" : "Scan",
            Hint = "",
            Kind = FileKind.Image,
            AcceptsExtension = _ => true,
            Combine = true,
            Run = (inputs, progress, ct) => Task.Run<IReadOnlyList<string>>(() =>
            {
                var done = new List<(string Source, MagickImage Page)>();
                try
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var photo = ImageTools.Load(pages[i].Path);
                        done.Add((pages[i].Path, ScanTools.Process(photo, pages[i].Corners, pages[i].Rotation, filter)));
                        progress.Report((i + 1) / (pages.Count + 1.0));
                    }
                    return pdf ? [ScanTools.SavePdf(done)] : ScanTools.SaveImages(done);
                }
                finally
                {
                    foreach (var (_, page) in done) page.Dispose();
                }
            }, ct),
        };
        JobRunner.Start(action, pages.Select(p => p.Path).ToList());
        Close();
    }
}
