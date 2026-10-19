using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConvertedBro.Core;
using ImageMagick;
using IOPath = System.IO.Path;
using Path = System.Windows.Shapes.Path;

namespace ConvertedBro.UI;

/// <summary>
/// Crop a photo (or signature) to a form's exact size: the box starts on the face, keeps the
/// preset's shape while you move or resize it, and saving lands the file inside the size range.
/// </summary>
public sealed class FormPhotoWindow : Window
{
    const double PreviewMax = 1600;

    readonly Palette _p = Theme.Current;
    readonly string _file;
    FormPreset _preset;
    MagickImage? _preview;
    double _scale = 1;          // full-resolution pixels per preview pixel
    Size _full;
    Rect _crop;                 // in full-resolution pixels
    Rect _fit;

    readonly Grid _stage = new() { ClipToBounds = true, Background = Theme.Brush(Color.FromRgb(0x0E, 0x0E, 0x10)) };
    readonly Canvas _canvas = new();
    readonly Image _photo = new();
    readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle _frame = new() { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
    readonly Ellipse _guide = new() { Stroke = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), StrokeThickness = 1.2, StrokeDashArray = [5, 4], IsHitTestVisible = false };
    readonly Rectangle _handle = new() { Width = 14, Height = 14, Fill = Brushes.White, IsHitTestVisible = false };
    readonly TextBlock _title = UiKit.Title(""), _detail = UiKit.Body(""), _status = UiKit.Body("");
    readonly List<(FormPreset Preset, Border Chip)> _chips = [];
    readonly Border _white;
    readonly StackPanel _backgroundSection = new();
    BitmapSource? _plain, _whitened;
    bool _whiteBackground;
    Point _dragStart;
    Rect _cropStart;
    bool _dragging, _resizing;

    public FormPhotoWindow(string file, FormPreset preset)
    {
        _file = file;
        _preset = preset;
        Title = $"Form photo: {IOPath.GetFileName(file)}";
        Width = 1080;
        Height = 760;
        MinWidth = 760;
        MinHeight = 520;
        UiKit.Prepare(this);

        _handle.Stroke = Theme.Brush(Theme.Brand);
        _handle.StrokeThickness = 2;
        RenderOptions.SetBitmapScalingMode(_photo, BitmapScalingMode.HighQuality);
        foreach (UIElement e in new UIElement[] { _photo, _dim, _guide, _frame, _handle }) _canvas.Children.Add(e);
        _stage.Children.Add(_canvas);
        _stage.SizeChanged += (_, _) => Layout();
        _canvas.MouseLeftButtonDown += OnDown;
        _canvas.MouseMove += OnMove;
        _canvas.MouseLeftButtonUp += (_, _) => { _canvas.ReleaseMouseCapture(); _dragging = _resizing = false; };
        _canvas.MouseWheel += (_, e) => Zoom(e.Delta > 0 ? 0.95 : 1.05);

        var side = new DockPanel { Margin = new Thickness(22, 24, 22, 22), LastChildFill = false };
        var top = new StackPanel();
        top.Children.Add(_title);
        top.Children.Add(_detail);
        top.Children.Add(UiKit.Heading("Size"));
        var chips = new WrapPanel();
        foreach (var option in FormPhotos.Presets)
        {
            var chip = UiKit.Button(option.Name, () => SetPreset(option));
            chip.Margin = new Thickness(0, 0, 8, 8);
            _chips.Add((option, chip));
            chips.Children.Add(chip);
        }
        top.Children.Add(chips);
        top.Children.Add(UiKit.Heading("Crop"));
        top.Children.Add(UiKit.Row(UiKit.Button("Auto", () => _ = AutoCrop())));
        _status.Text = "Drag the box to move it, drag its corner or scroll to resize.";
        top.Children.Add(_status);

        _white = UiKit.Button("White background", () => _ = ToggleWhite());
        _backgroundSection.Children.Add(UiKit.Heading("Background"));
        _backgroundSection.Children.Add(_white);
        _backgroundSection.Children.Add(UiKit.Body("Swaps whatever is behind you for plain white, as most forms ask.", 11.5));
        top.Children.Add(_backgroundSection);
        DockPanel.SetDock(top, Dock.Top);
        side.Children.Add(top);

        var bottom = new StackPanel();
        bottom.Children.Add(UiKit.Button("Save JPG", Save, primary: true, stretch: true));
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

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) Save(); };
        Closed += (_, _) => _preview?.Dispose();
        UpdateText();
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        try
        {
            var (preview, full, bitmap) = await Task.Run(() =>
            {
                var image = ImageTools.Load(_file);
                var size = new Size(image.Width, image.Height);
                if (Math.Max(image.Width, image.Height) > PreviewMax) image.Resize(new MagickGeometry((uint)PreviewMax, (uint)PreviewMax));
                return (image, size, ToBitmap(image));
            });
            _preview = preview;
            _full = full;
            _scale = full.Width / preview.Width;
            _photo.Source = _plain = bitmap;
            await AutoCrop();
        }
        catch (Exception ex)
        {
            Log.Error($"Form photo could not open {_file}", ex);
            _status.Text = "Couldn't open this picture.";
        }
    }

    static BitmapSource ToBitmap(MagickImage image)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(image.ToByteArray(MagickFormat.Bmp));
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    async Task AutoCrop()
    {
        if (_preview == null) return;
        _status.Text = _preset.Signature ? "Finding the signature…" : "Finding the face…";
        var suggestion = await FormPhotos.SuggestCrop(_preview, _preset);
        _crop = new Rect(suggestion.X * _scale, suggestion.Y * _scale, suggestion.Width * _scale, suggestion.Height * _scale);
        _status.Text = "Drag the box to move it, drag its corner or scroll to resize.";
        Layout();
    }

    async Task ToggleWhite()
    {
        if (_preview == null) return;
        _whiteBackground = !_whiteBackground;
        UiKit.Select(_white, _whiteBackground);
        if (!_whiteBackground)
        {
            _photo.Source = _plain;
            return;
        }
        if (_whitened == null)
        {
            _status.Text = Cutout.IsDownloaded ? "Removing the background…" : $"Getting the background model ({Cutout.DownloadSize}, one time)…";
            var preview = _preview;
            try
            {
                _whitened = await Task.Run(async () =>
                {
                    await Cutout.EnsureModel(new Progress<double>(), default);
                    using var cut = Cutout.Remove(preview);
                    ImageTools.FlattenOnWhite(cut);
                    return ToBitmap(cut);
                });
            }
            catch (Exception ex)
            {
                Log.Error("White background preview failed", ex);
                _status.Text = "Couldn't remove the background. Check the internet connection for the first use.";
                _whiteBackground = false;
                UiKit.Select(_white, false);
                return;
            }
            _status.Text = "Drag the box to move it, drag its corner or scroll to resize.";
        }
        if (_whiteBackground) _photo.Source = _whitened;
    }

    void SetPreset(FormPreset preset)
    {
        _preset = preset;
        UpdateText();
        _ = AutoCrop();
    }

    void UpdateText()
    {
        _title.Text = _preset.Name;
        _backgroundSection.Visibility = _preset.Signature ? Visibility.Collapsed : Visibility.Visible;
        _detail.Text = $"{_preset.Detail}, {_preset.WidthPx} × {_preset.HeightPx} px at {_preset.Dpi} DPI, {_preset.SizeText}, JPG.";
        foreach (var (preset, chip) in _chips) UiKit.Select(chip, preset == _preset);
    }

    // ---- Drawing ----

    void Layout()
    {
        if (_full.Width == 0) return;
        double pad = 30;
        double scale = Math.Min((_stage.ActualWidth - pad * 2) / _full.Width, (_stage.ActualHeight - pad * 2) / _full.Height);
        double w = _full.Width * scale, h = _full.Height * scale;
        _fit = new Rect((_stage.ActualWidth - w) / 2, (_stage.ActualHeight - h) / 2, w, h);
        _photo.Width = w;
        _photo.Height = h;
        Canvas.SetLeft(_photo, _fit.X);
        Canvas.SetTop(_photo, _fit.Y);
        Redraw();
    }

    Rect CropOnCanvas => new(
        _fit.X + _crop.X / _full.Width * _fit.Width, _fit.Y + _crop.Y / _full.Height * _fit.Height,
        _crop.Width / _full.Width * _fit.Width, _crop.Height / _full.Height * _fit.Height);

    void Redraw()
    {
        var box = CropOnCanvas;
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(_fit), new RectangleGeometry(box));
        Place(_frame, box);
        // Where a face should sit for a passport-style photo.
        _guide.Visibility = _preset.Signature ? Visibility.Collapsed : Visibility.Visible;
        Place(_guide, new Rect(box.X + box.Width * 0.27, box.Y + box.Height * 0.2, box.Width * 0.46, box.Height * 0.52));
        Canvas.SetLeft(_handle, box.Right - 7);
        Canvas.SetTop(_handle, box.Bottom - 7);
    }

    static void Place(FrameworkElement element, Rect box)
    {
        Canvas.SetLeft(element, box.X);
        Canvas.SetTop(element, box.Y);
        element.Width = Math.Max(0, box.Width);
        element.Height = Math.Max(0, box.Height);
    }

    // ---- Moving and resizing (the shape stays locked to the preset) ----

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(_canvas);
        var box = CropOnCanvas;
        _resizing = new Rect(box.Right - 14, box.Bottom - 14, 28, 28).Contains(at);
        _dragging = _resizing || box.Contains(at);
        if (!_dragging) return;
        _dragStart = at;
        _cropStart = _crop;
        _canvas.CaptureMouse();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var at = e.GetPosition(_canvas);
        double perPixel = _full.Width / _fit.Width;
        double dx = (at.X - _dragStart.X) * perPixel, dy = (at.Y - _dragStart.Y) * perPixel;
        var bounds = new Rect(0, 0, _full.Width, _full.Height);
        if (_resizing)
        {
            double w = Math.Clamp(_cropStart.Width + dx, 40, bounds.Width - _cropStart.X);
            double h = w / _preset.Aspect;
            if (_cropStart.Y + h > bounds.Height) { h = bounds.Height - _cropStart.Y; w = h * _preset.Aspect; }
            _crop = new Rect(_cropStart.X, _cropStart.Y, w, h);
        }
        else
        {
            _crop = new Rect(Math.Clamp(_cropStart.X + dx, 0, bounds.Width - _cropStart.Width), Math.Clamp(_cropStart.Y + dy, 0, bounds.Height - _cropStart.Height), _cropStart.Width, _cropStart.Height);
        }
        Redraw();
    }

    void Zoom(double factor)
    {
        if (_full.Width == 0) return;
        var centre = new Point(_crop.X + _crop.Width / 2, _crop.Y + _crop.Height / 2);
        var grown = new Rect(centre.X - _crop.Width * factor / 2, centre.Y - _crop.Height * factor / 2, _crop.Width * factor, _crop.Height * factor);
        _crop = FormPhotos.FitAspect(grown, _preset.Aspect, new Rect(0, 0, _full.Width, _full.Height));
        Redraw();
    }

    // ---- Saving ----

    void Save()
    {
        if (_full.Width == 0) return;
        var crop = _crop;
        var preset = _preset;
        var file = _file;
        bool white = _whiteBackground;
        var action = new BroAction
        {
            Id = $"form-{preset.Id}-run",
            Label = preset.Name,
            Short = preset.Name,
            Hint = "",
            Kind = FileKind.Image,
            AcceptsExtension = _ => true,
            Run = (_, _, ct) => Task.Run<IReadOnlyList<string>>(() => [FormPhotos.Produce(file, crop, preset, white)], ct),
        };
        JobRunner.Start(action, [file]);
        Close();
    }
}
