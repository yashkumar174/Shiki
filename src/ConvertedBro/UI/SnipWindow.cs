using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConvertedBro.Core;
using ConvertedBro.Shell;
using Path = System.Windows.Shapes.Path;

namespace ConvertedBro.UI;

/// <summary>
/// Freezes the screen, lets you drag a box, saves that part as a PNG and hands it on (to the wheel).
/// The screenshot is taken before the overlay appears, so the overlay never shows up in it.
/// </summary>
public sealed class SnipWindow : Window
{
    readonly Int32Rect _screenPx;
    readonly BitmapSource? _shot;
    readonly double _scale = NativeMethods.SystemScale;
    readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle _frame = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly Border _size = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock _sizeText = new();
    readonly Canvas _canvas = new();
    Point? _start;
    Rect _selection;

    /// <summary>The saved PNG and the middle of the snip in physical screen pixels.</summary>
    public event Action<string, Point>? Snipped;

    public SnipWindow()
    {
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        _screenPx = new Int32Rect(virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height);
        _shot = Glass.Capture(_screenPx);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _screenPx.X / _scale;
        Top = _screenPx.Y / _scale;
        Width = _screenPx.Width / _scale;
        Height = _screenPx.Height / _scale;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;
        UseLayoutRounding = true;

        var photo = new Image { Source = _shot, Stretch = Stretch.Fill, Width = Width, Height = Height };
        RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.NearestNeighbor);
        _canvas.Children.Add(photo);
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_frame);

        _sizeText.FontFamily = Theme.TextFont;
        _sizeText.FontSize = 12;
        _sizeText.Foreground = Brushes.White;
        _size.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x14));
        _size.CornerRadius = new CornerRadius(Theme.Arcade ? 0 : 6);
        _size.Padding = new Thickness(8, 3, 8, 4);
        _size.Child = _sizeText;
        _canvas.Children.Add(_size);

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x10, 0x14)),
            CornerRadius = new CornerRadius(Theme.Arcade ? 0 : 18),
            Padding = new Thickness(16, 8, 16, 9),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Drag over what you want.  Esc cancels.",
                FontFamily = Theme.LabelFont,
                FontSize = 13.5,
                Foreground = Brushes.White,
            },
        };
        hint.Loaded += (_, _) =>
        {
            // Centre the hint on the primary screen, near the top.
            var primary = SystemParameters.WorkArea;
            Canvas.SetLeft(hint, primary.Left - Left + (primary.Width - hint.ActualWidth) / 2);
            Canvas.SetTop(hint, primary.Top - Top + 28);
        };
        _canvas.Children.Add(hint);
        Content = _canvas;
        UpdateDim();

        SourceInitialized += (_, _) => NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => { Activate(); Focus(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(_canvas); CaptureMouse(); };
        MouseMove += (_, e) => { if (_start is { } s) Select(s, e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, _) => Finish();
    }

    void Select(Point a, Point b)
    {
        _selection = new Rect(a, b);
        _frame.Visibility = Visibility.Visible;
        Canvas.SetLeft(_frame, _selection.X);
        Canvas.SetTop(_frame, _selection.Y);
        _frame.Width = _selection.Width;
        _frame.Height = _selection.Height;

        _size.Visibility = Visibility.Visible;
        _sizeText.Text = $"{Math.Round(_selection.Width * _scale)} × {Math.Round(_selection.Height * _scale)}";
        Canvas.SetLeft(_size, _selection.X);
        Canvas.SetTop(_size, _selection.Y > 30 ? _selection.Y - 28 : _selection.Bottom + 6);
        UpdateDim();
    }

    /// <summary>Everything outside the box stays dimmed.</summary>
    void UpdateDim() =>
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(_selection));

    void Finish()
    {
        ReleaseMouseCapture();
        if (_start == null) return;
        _start = null;
        var px = new Int32Rect(
            (int)Math.Round(_selection.X * _scale), (int)Math.Round(_selection.Y * _scale),
            (int)Math.Round(_selection.Width * _scale), (int)Math.Round(_selection.Height * _scale));
        if (_shot == null || px.Width < 6 || px.Height < 6)
        {
            // A click without a drag: nothing to snip.
            _selection = Rect.Empty;
            _frame.Visibility = _size.Visibility = Visibility.Collapsed;
            UpdateDim();
            return;
        }

        px.Width = Math.Min(px.Width, _shot.PixelWidth - px.X);
        px.Height = Math.Min(px.Height, _shot.PixelHeight - px.Y);
        string file;
        try
        {
            file = Save(new CroppedBitmap(_shot, px));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the snip", ex);
            Close();
            return;
        }
        var centre = new Point(_screenPx.X + px.X + px.Width / 2.0, _screenPx.Y + px.Y + px.Height / 2.0);
        Close();
        Snipped?.Invoke(file, centre);
    }

    /// <summary>Snips live in Pictures\Converted Bro, so they (and anything made from them) are easy to find.</summary>
    static string Save(BitmapSource snip)
    {
        var folder = Directory.CreateDirectory(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Converted Bro")).FullName;
        var file = System.IO.Path.Combine(folder, $"Snip {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(snip));
        using var stream = File.Create(file);
        encoder.Save(stream);
        return file;
    }
}
