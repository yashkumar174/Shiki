using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shiki.Core;
using Shiki.Shell;
using Path = System.Windows.Shapes.Path;

namespace Shiki.UI;

/// <summary>What the paw picked: the picture (PNG, sized for Claude), and where it was on screen in physical pixels.</summary>
public sealed record PawPick(byte[] Png, Int32Rect ScreenPx, string? WindowTitle);

/// <summary>
/// Paw to ask: the screen holds still, the pointer becomes the cat's paw, and swiping it around
/// something leaves a trail of paw prints. Letting go picks what the trail went around; a plain
/// click picks the whole window under the paw. The picture is taken before this window appears,
/// so it's never in it.
/// </summary>
public sealed class PawWindow : Window
{
    const double PrintEvery = 20, PrintScale = 2, PawScale = 3;
    // Claude reads pictures up to about this many pixels on the long side; bigger ones are shrunk.
    const int MaxSide = 1568;

    readonly Int32Rect _screenPx;
    readonly BitmapSource? _shot;
    readonly double _scale = NativeMethods.SystemScale;
    readonly Canvas _canvas = new();
    readonly Canvas _prints = new() { IsHitTestVisible = false };
    readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), IsHitTestVisible = false };
    readonly Image _paw;
    readonly CatSkin _skin;
    readonly List<Point> _path = [];
    Point _lastPrint;
    bool _left, _dragging, _done;

    public event Action<PawPick>? Picked;

    public PawWindow(CatSkin skin)
    {
        _skin = skin;
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
        Cursor = Cursors.None;
        Background = Brushes.Black;
        UseLayoutRounding = true;

        var photo = new Image { Source = _shot, Stretch = Stretch.Fill, Width = Width, Height = Height };
        RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.NearestNeighbor);
        _canvas.Children.Add(photo);
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_prints);

        var pointer = CatSprites.PawPointer(skin);
        _paw = new Image { Source = pointer, Width = pointer.PixelWidth * PawScale, Height = pointer.PixelHeight * PawScale, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(_paw, BitmapScalingMode.NearestNeighbor);
        _canvas.Children.Add(_paw);

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x10, 0x14)),
            CornerRadius = new CornerRadius(Theme.Arcade ? 0 : 18),
            Padding = new Thickness(16, 8, 16, 9),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Swipe the paw around what you want to ask about, or click a window.  Esc cancels.",
                FontFamily = Theme.LabelFont,
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
        Dim(Rect.Empty);

        SourceInitialized += (_, _) => NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            MovePaw(Mouse.GetPosition(_canvas));
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
        MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _path.Clear();
            _prints.Children.Clear();
            var at = e.GetPosition(_canvas);
            _path.Add(at);
            _lastPrint = at;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            var at = e.GetPosition(_canvas);
            MovePaw(at);
            if (!_dragging) return;
            _path.Add(at);
            Stamp(at);
        };
        MouseLeftButtonUp += (_, e) => Finish(e.GetPosition(_canvas));
    }

    /// <summary>The paw's toes are the pointer's tip.</summary>
    void MovePaw(Point at)
    {
        Canvas.SetLeft(_paw, at.X - _paw.Width / 2);
        Canvas.SetTop(_paw, at.Y - 2);
    }

    /// <summary>Prints every so often along the swipe, stepping left and right, toes pointing the way it goes.</summary>
    void Stamp(Point at)
    {
        var step = at - _lastPrint;
        if (step.Length < PrintEvery) return;
        step.Normalize();
        var side = new Vector(-step.Y, step.X) * (_left ? -6 : 6);
        _left = !_left;
        var print = CatSprites.PawPrint(_skin);
        var image = new Image { Source = print, Width = print.PixelWidth * PrintScale, Height = print.PixelHeight * PrintScale, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        // Toes point up in the art; turn them toward the direction of travel.
        double angle = Math.Atan2(step.Y, step.X) * 180 / Math.PI + 90;
        image.RenderTransformOrigin = new Point(0.5, 0.5);
        image.RenderTransform = new RotateTransform(angle);
        Canvas.SetLeft(image, at.X + side.X - image.Width / 2);
        Canvas.SetTop(image, at.Y + side.Y - image.Height / 2);
        _prints.Children.Add(image);
        image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.95, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 120 : 0)));
        _lastPrint = at;
    }

    void Finish(Point at)
    {
        if (!_dragging || _done) return;
        _dragging = false;
        ReleaseMouseCapture();
        string? title = null;
        Rect area;
        double travelled = _path.Zip(_path.Skip(1), (a, b) => (b - a).Length).Sum();
        if (travelled < 14)
        {
            // A click: the whole window under the paw.
            if (WindowUnder(at) is not { } window)
            {
                _prints.Children.Clear();
                return;
            }
            (area, title) = window;
        }
        else
        {
            double pad = 10;
            area = new Rect(new Point(_path.Min(p => p.X) - pad, _path.Min(p => p.Y) - pad), new Point(_path.Max(p => p.X) + pad, _path.Max(p => p.Y) + pad));
            area.Intersect(new Rect(0, 0, Width, Height));
        }
        if (_shot == null || area.Width < 12 || area.Height < 12)
        {
            _prints.Children.Clear();
            return;
        }
        _done = true;
        Dim(area);

        var px = new Int32Rect((int)Math.Round(area.X * _scale), (int)Math.Round(area.Y * _scale),
            (int)Math.Round(area.Width * _scale), (int)Math.Round(area.Height * _scale));
        px.Width = Math.Min(px.Width, _shot.PixelWidth - px.X);
        px.Height = Math.Min(px.Height, _shot.PixelHeight - px.Y);
        byte[] png;
        try
        {
            png = Encode(new CroppedBitmap(_shot, px));
        }
        catch (Exception ex)
        {
            Log.Error("Could not take the paw's picture", ex);
            Close();
            return;
        }
        var screen = new Int32Rect(_screenPx.X + px.X, _screenPx.Y + px.Y, px.Width, px.Height);
        // A moment to see what was picked, then out of the way.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
            Picked?.Invoke(new PawPick(png, screen, title));
        };
        timer.Start();
    }

    void Dim(Rect keep) =>
        _dim.Data = keep.IsEmpty
            ? new RectangleGeometry(new Rect(0, 0, Width, Height))
            : new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(keep));

    /// <summary>The top window under a point (not this one, not hidden ones), in this window's coordinates.</summary>
    (Rect Area, string Title)? WindowUnder(Point at)
    {
        var mine = new WindowInteropHelper(this).Handle;
        var screen = new POINT { X = (int)(at.X * _scale) + _screenPx.X, Y = (int)(at.Y * _scale) + _screenPx.Y };
        for (var hwnd = GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, 2))
        {
            if (hwnd == mine || !IsWindowVisible(hwnd)) continue;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (DwmGetWindowAttribute(hwnd, 9, out RECT r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd, out r)) continue;
            if (screen.X < r.Left || screen.X >= r.Right || screen.Y < r.Top || screen.Y >= r.Bottom) continue;
            if (r.Right - r.Left < 40 || r.Bottom - r.Top < 40) continue;
            var title = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            var area = new Rect((r.Left - _screenPx.X) / _scale, (r.Top - _screenPx.Y) / _scale, (r.Right - r.Left) / _scale, (r.Bottom - r.Top) / _scale);
            area.Intersect(new Rect(0, 0, Width, Height));
            return (area, title.ToString());
        }
        return null;
    }

    /// <summary>PNG, shrunk if it's bigger than Claude can use.</summary>
    static byte[] Encode(BitmapSource picture)
    {
        int longest = Math.Max(picture.PixelWidth, picture.PixelHeight);
        if (longest > MaxSide)
        {
            double s = (double)MaxSide / longest;
            picture = new TransformedBitmap(picture, new ScaleTransform(s, s));
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
