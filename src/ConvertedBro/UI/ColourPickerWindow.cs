using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>
/// Freezes the screen and shows a magnifier under the cursor; a click picks the pixel's colour.
/// Arrow keys nudge one pixel at a time, Enter picks, Esc or right-click cancels.
/// </summary>
public sealed class ColourPickerWindow : Window
{
    const int Span = 15;          // pixels across the loupe
    const double Cell = 10;       // loupe size of one screen pixel, in DIPs
    const double LoupeSize = Span * Cell;

    readonly Int32Rect _screenPx;
    readonly double _scale = NativeMethods.SystemScale;
    readonly byte[] _pixels = [];  // BGRA, whole virtual screen
    readonly Canvas _canvas = new();
    readonly Image _zoom = new() { Width = LoupeSize, Height = LoupeSize };
    readonly Border _loupe;
    readonly Border _label;
    readonly Border _chip = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 7, 0), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
    readonly TextBlock _hex = new() { FontFamily = Theme.LabelFont, FontSize = 13, Foreground = Brushes.White };
    readonly StackPanel _panel = new() { IsHitTestVisible = false };
    Color _current;

    /// <summary>The colour picked and where (physical screen pixels).</summary>
    public event Action<Color>? Picked;

    public ColourPickerWindow()
    {
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        _screenPx = new Int32Rect(virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height);
        var shot = Glass.Capture(_screenPx);
        if (shot != null)
        {
            var bgra = new FormatConvertedBitmap(shot, PixelFormats.Bgra32, null, 0);
            _pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(_pixels, bgra.PixelWidth * 4, 0);
        }

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

        var photo = new Image { Source = shot, Stretch = Stretch.Fill, Width = Width, Height = Height };
        RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.NearestNeighbor);
        _canvas.Children.Add(photo);

        // The loupe: real pixels, big, with the one you'll pick outlined.
        RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
        var centre = new Grid { Width = Cell + 2, Height = Cell + 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Span / 2 * Cell - 1, Span / 2 * Cell - 1, 0, 0) };
        centre.Children.Add(new Rectangle { Stroke = Brushes.Black, StrokeThickness = 1 });
        centre.Children.Add(new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, Margin = new Thickness(1) });
        var inside = new Grid { Width = LoupeSize, Height = LoupeSize, ClipToBounds = true };
        inside.Children.Add(_zoom);
        inside.Children.Add(GridLines());
        inside.Children.Add(centre);
        bool pixel = Theme.Arcade;
        _loupe = new Border
        {
            Child = inside,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(pixel ? 3 : 2),
            CornerRadius = new CornerRadius(pixel ? 0 : 14),
            Background = Brushes.Black,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = pixel ? 0 : 14, ShadowDepth = pixel ? 5 : 0, Opacity = 0.6 },
        };
        if (!pixel) inside.Clip = new RectangleGeometry(new Rect(0, 0, LoupeSize, LoupeSize), 12, 12);

        var labelRow = new StackPanel { Orientation = Orientation.Horizontal };
        labelRow.Children.Add(_chip);
        labelRow.Children.Add(_hex);
        _label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x14, 0x18)),
            CornerRadius = new CornerRadius(pixel ? 0 : 8),
            Padding = new Thickness(9, 5, 10, 6),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = labelRow,
        };
        _panel.Children.Add(_loupe);
        _panel.Children.Add(_label);
        _canvas.Children.Add(_panel);
        Content = _canvas;

        SourceInitialized += (_, _) => NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => { Activate(); Focus(); Follow(); };
        MouseMove += (_, _) => Follow();
        MouseLeftButtonUp += (_, _) => Pick();
        MouseRightButtonUp += (_, _) => Close();
        KeyDown += OnKey;
    }

    /// <summary>Faint lines between the magnified pixels.</summary>
    static UIElement GridLines()
    {
        var lines = new GeometryGroup();
        for (int i = 1; i < Span; i++)
        {
            lines.Children.Add(new LineGeometry(new Point(i * Cell, 0), new Point(i * Cell, LoupeSize)));
            lines.Children.Add(new LineGeometry(new Point(0, i * Cell), new Point(LoupeSize, i * Cell)));
        }
        return new Path { Data = lines, Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)), StrokeThickness = 1, IsHitTestVisible = false };
    }

    /// <summary>The cursor in physical pixels, relative to the captured screen.</summary>
    (int X, int Y) CursorPx()
    {
        NativeMethods.GetCursorPos(out var p);
        return (Math.Clamp(p.X - _screenPx.X, 0, _screenPx.Width - 1), Math.Clamp(p.Y - _screenPx.Y, 0, _screenPx.Height - 1));
    }

    void Follow()
    {
        if (_pixels.Length == 0) return;
        var (x, y) = CursorPx();
        int w = _screenPx.Width, h = _screenPx.Height;

        var patch = new byte[Span * Span * 4];
        for (int dy = 0; dy < Span; dy++)
            for (int dx = 0; dx < Span; dx++)
            {
                int sx = x + dx - Span / 2, sy = y + dy - Span / 2;
                int o = (dy * Span + dx) * 4;
                if (sx < 0 || sy < 0 || sx >= w || sy >= h) { patch[o + 3] = 255; continue; }
                int i = (sy * w + sx) * 4;
                patch[o] = _pixels[i];
                patch[o + 1] = _pixels[i + 1];
                patch[o + 2] = _pixels[i + 2];
                patch[o + 3] = 255;
            }
        _zoom.Source = BitmapSource.Create(Span, Span, 96, 96, PixelFormats.Bgra32, null, patch, Span * 4);

        int c = (y * w + x) * 4;
        _current = Color.FromRgb(_pixels[c + 2], _pixels[c + 1], _pixels[c]);
        _chip.Background = new SolidColorBrush(_current);
        _hex.Text = Hex(_current);

        // Sit below-right of the cursor; flip when that would leave the screen.
        _panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _panel.DesiredSize;
        double cx = x / _scale, cy = y / _scale;
        double left = cx + 24, top = cy + 24;
        if (left + size.Width > Width) left = cx - 24 - size.Width;
        if (top + size.Height > Height) top = cy - 24 - size.Height;
        Canvas.SetLeft(_panel, left);
        Canvas.SetTop(_panel, top);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        (int dx, int dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if (dx != 0 || dy != 0)
        {
            NativeMethods.GetCursorPos(out var p);
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            NativeMethods.SetCursorPos(p.X + dx * step, p.Y + dy * step);
            Follow();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape) Close();
        else if (e.Key is Key.Enter or Key.Space) Pick();
    }

    void Pick()
    {
        if (_pixels.Length == 0) { Close(); return; }
        var colour = _current;
        Close();
        Picked?.Invoke(colour);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static string Rgb(Color c) => $"rgb({c.R}, {c.G}, {c.B})";

    public static string Hsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, s = 0, h = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }
        return $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)";
    }
}
