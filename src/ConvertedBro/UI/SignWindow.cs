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
using ConvertedBro.Core;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using IOPath = System.IO.Path;

namespace ConvertedBro.UI;

/// <summary>
/// Place your drawn signature on PDF pages: click a page to put it there, drag to move, use the
/// corner to resize. A visual signature (an image on the page), not a certificate.
/// </summary>
public sealed class SignWindow : Window
{
    /// <summary>A signature on one page, in fractions of the page size (so it survives any zoom).</summary>
    sealed class Stamp
    {
        public int Page { get; init; }
        public Rect Box { get; set; }
    }

    readonly Palette _p = Theme.Current;
    readonly string _file;
    readonly List<Stamp> _stamps = [];
    readonly Grid _stage = new() { ClipToBounds = true, Background = Theme.Brush(Color.FromRgb(0x0E, 0x0E, 0x10)) };
    readonly Canvas _canvas = new();
    readonly Image _page = new();
    readonly Image _signaturePreview = new() { Height = 70, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _pager = UiKit.Body("");
    readonly TextBlock _status = UiKit.Body("");
    BitmapSource? _signature;
    int _pageCount, _index;
    Rect _fit;
    Stamp? _active;
    Point _dragStart;
    Rect _boxStart;
    bool _resizing;

    public SignWindow(IReadOnlyList<string> files)
    {
        _file = files[0];
        Title = $"Sign {IOPath.GetFileName(_file)}";
        Width = 1120;
        Height = 780;
        MinWidth = 760;
        MinHeight = 520;
        UiKit.Prepare(this);

        _canvas.Children.Add(_page);
        _stage.Children.Add(_canvas);
        _stage.SizeChanged += (_, _) => Layout();
        _canvas.MouseLeftButtonDown += OnPageClick;
        _canvas.MouseMove += OnDrag;
        _canvas.MouseLeftButtonUp += (_, _) => { _canvas.ReleaseMouseCapture(); _resizing = false; };

        var side = new DockPanel { Margin = new Thickness(22, 24, 22, 22), LastChildFill = false };
        var top = new StackPanel();
        top.Children.Add(UiKit.Title("Sign"));
        _status.Text = "Click the page where your signature goes. Drag it to move; drag its corner to resize.";
        top.Children.Add(_status);

        top.Children.Add(UiKit.Heading("Your signature"));
        top.Children.Add(new Border { Background = Brushes.White, Padding = new Thickness(10), Child = _signaturePreview, CornerRadius = new CornerRadius(_p.Pixel ? 0 : 8) });
        var signatureButtons = UiKit.Row(UiKit.Button("Draw new", DrawSignature));
        signatureButtons.Margin = new Thickness(0, 10, 0, 0);
        top.Children.Add(signatureButtons);

        top.Children.Add(UiKit.Heading("Page"));
        var pagerRow = UiKit.Row(UiKit.Button("", () => ShowPage(_index - 1), icon: true), _pager, UiKit.Button("", () => ShowPage(_index + 1), icon: true));
        _pager.VerticalAlignment = VerticalAlignment.Center;
        _pager.Margin = new Thickness(4, 0, 12, 0);
        top.Children.Add(pagerRow);
        top.Children.Add(UiKit.Body("Delete removes the selected signature."));
        DockPanel.SetDock(top, Dock.Top);
        side.Children.Add(top);

        var bottom = new StackPanel();
        bottom.Children.Add(UiKit.Button("Save signed PDF", Save, primary: true, stretch: true));
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

        KeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            LoadSignature();
            if (_signature == null) DrawSignature();
        };
        _ = OpenAsync();
    }

    async Task OpenAsync()
    {
        try
        {
            _pageCount = await Task.Run(() =>
            {
                lock (PdfTools.PdfiumGate)
                {
                    using var doc = DocLib.Instance.GetDocReader(_file, new PageDimensions(0.1));
                    return doc.GetPageCount();
                }
            });
            ShowPage(0);
        }
        catch (Exception ex)
        {
            Log.Error($"Sign could not open {_file}", ex);
            _status.Text = "Couldn't open this PDF. If it has a password, unlock it first.";
        }
    }

    void LoadSignature()
    {
        _signature = SignatureStore.Load();
        _signaturePreview.Source = _signature;
        Redraw();
    }

    void DrawSignature()
    {
        var pad = new SignaturePadWindow { Owner = this };
        pad.ShowDialog();
        if (pad.Saved) LoadSignature();
    }

    // ---- Page display ----

    async void ShowPage(int index)
    {
        if (_pageCount == 0) return;
        _index = Math.Clamp(index, 0, _pageCount - 1);
        _pager.Text = $"{_index + 1} of {_pageCount}";
        int wanted = _index;
        var bitmap = await Task.Run(() => Render(_file, wanted));
        if (wanted != _index) return;
        _page.Source = bitmap;
        Layout();
    }

    static BitmapSource Render(string file, int index)
    {
        lock (PdfTools.PdfiumGate)
        {
            using var doc = DocLib.Instance.GetDocReader(file, new PageDimensions(1.6));
            using var page = doc.GetPageReader(index);
            int w = page.GetPageWidth(), h = page.GetPageHeight();
            var raw = page.GetImage();
            for (int i = 0; i < raw.Length; i += 4)
            {
                int a = raw[i + 3];
                raw[i] = (byte)(raw[i] * a / 255 + 255 - a);
                raw[i + 1] = (byte)(raw[i + 1] * a / 255 + 255 - a);
                raw[i + 2] = (byte)(raw[i + 2] * a / 255 + 255 - a);
                raw[i + 3] = 255;
            }
            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, raw, w * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }

    void Layout()
    {
        if (_page.Source is not BitmapSource bitmap) return;
        double pad = 30;
        double scale = Math.Min((_stage.ActualWidth - pad * 2) / bitmap.PixelWidth, (_stage.ActualHeight - pad * 2) / bitmap.PixelHeight);
        double w = bitmap.PixelWidth * scale, h = bitmap.PixelHeight * scale;
        _fit = new Rect((_stage.ActualWidth - w) / 2, (_stage.ActualHeight - h) / 2, w, h);
        _page.Width = w;
        _page.Height = h;
        Canvas.SetLeft(_page, _fit.X);
        Canvas.SetTop(_page, _fit.Y);
        Redraw();
    }

    /// <summary>Draws this page's signatures, each with a frame and resize corner when selected.</summary>
    void Redraw()
    {
        for (int i = _canvas.Children.Count - 1; i >= 1; i--) _canvas.Children.RemoveAt(i);
        if (_signature == null) return;
        foreach (var stamp in _stamps.Where(s => s.Page == _index))
        {
            var box = ToCanvas(stamp.Box);
            var image = new Image { Source = _signature, Width = box.Width, Height = box.Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
            Canvas.SetLeft(image, box.X);
            Canvas.SetTop(image, box.Y);
            _canvas.Children.Add(image);
            if (stamp != _active) continue;
            var frame = new System.Windows.Shapes.Rectangle { Width = box.Width, Height = box.Height, Stroke = Theme.Brush(Theme.Brand), StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false };
            Canvas.SetLeft(frame, box.X);
            Canvas.SetTop(frame, box.Y);
            _canvas.Children.Add(frame);
            var corner = new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Fill = Brushes.White, Stroke = Theme.Brush(Theme.Brand), StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(corner, box.Right - 6);
            Canvas.SetTop(corner, box.Bottom - 6);
            _canvas.Children.Add(corner);
        }
    }

    Rect ToCanvas(Rect fraction) => new(_fit.X + fraction.X * _fit.Width, _fit.Y + fraction.Y * _fit.Height, fraction.Width * _fit.Width, fraction.Height * _fit.Height);

    // ---- Placing, moving, resizing ----

    void OnPageClick(object sender, MouseButtonEventArgs e)
    {
        if (_signature == null || !_fit.Contains(e.GetPosition(_canvas))) return;
        var at = e.GetPosition(_canvas);

        // Grab an existing signature (or its corner) if the click is on one.
        foreach (var stamp in _stamps.Where(s => s.Page == _index).Reverse())
        {
            var box = ToCanvas(stamp.Box);
            var corner = new Rect(box.Right - 10, box.Bottom - 10, 20, 20);
            if (corner.Contains(at) || box.Contains(at))
            {
                _active = stamp;
                _resizing = corner.Contains(at);
                Begin(at, stamp);
                return;
            }
        }

        // New signature, a quarter of the page wide, centred on the click.
        double width = 0.26, height = width * (_signature.PixelHeight / (double)_signature.PixelWidth) * (_fit.Width / _fit.Height);
        double fx = (at.X - _fit.X) / _fit.Width, fy = (at.Y - _fit.Y) / _fit.Height;
        var fresh = new Stamp { Page = _index, Box = new Rect(fx - width / 2, fy - height / 2, width, height) };
        _stamps.Add(fresh);
        _active = fresh;
        _resizing = false;
        Begin(at, fresh);
        Redraw();
        _status.Text = "Placed. Drag to move, drag the corner to resize, or click elsewhere to sign again.";
    }

    void Begin(Point at, Stamp stamp)
    {
        _dragStart = at;
        _boxStart = stamp.Box;
        _canvas.CaptureMouse();
        Redraw();
    }

    void OnDrag(object sender, MouseEventArgs e)
    {
        if (!_canvas.IsMouseCaptured || _active == null) return;
        var now = e.GetPosition(_canvas);
        double dx = (now.X - _dragStart.X) / _fit.Width, dy = (now.Y - _dragStart.Y) / _fit.Height;
        if (_resizing)
        {
            // Keep the signature's proportions while resizing.
            double width = Math.Clamp(_boxStart.Width + dx, 0.04, 1);
            double height = width * (_boxStart.Height / _boxStart.Width);
            _active.Box = new Rect(_boxStart.X, _boxStart.Y, width, height);
        }
        else
        {
            _active.Box = new Rect(_boxStart.X + dx, _boxStart.Y + dy, _boxStart.Width, _boxStart.Height);
        }
        Redraw();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Delete or Key.Back when _active != null:
                _stamps.Remove(_active);
                _active = null;
                Redraw();
                break;
            case Key.PageDown or Key.Right:
                ShowPage(_index + 1);
                break;
            case Key.PageUp or Key.Left:
                ShowPage(_index - 1);
                break;
            case Key.Escape:
                Close();
                break;
        }
    }

    // ---- Saving ----

    void Save()
    {
        if (_stamps.Count == 0 || _signature == null)
        {
            _status.Text = "Place your signature on a page first.";
            return;
        }
        var stamps = _stamps.Select(s => (s.Page, s.Box)).ToList();
        var file = _file;
        var signaturePath = SignatureStore.Path;
        var action = new BroAction
        {
            Id = "pdf-sign-run",
            Label = "Sign",
            Short = "Signed",
            Hint = "",
            Kind = FileKind.Pdf,
            AcceptsExtension = _ => true,
            Run = (inputs, progress, ct) => Task.Run<IReadOnlyList<string>>(() =>
            {
                var output = OutputPaths.Next(file, ".pdf", "signed");
                using var doc = PdfReader.Open(file, PdfDocumentOpenMode.Modify);
                using var image = XImage.FromFile(signaturePath);
                foreach (var (page, box) in stamps)
                {
                    using var gfx = XGraphics.FromPdfPage(doc.Pages[page], XGraphicsPdfPageOptions.Append);
                    var size = gfx.PageSize;
                    gfx.DrawImage(image, box.X * size.Width, box.Y * size.Height, box.Width * size.Width, box.Height * size.Height);
                }
                doc.Save(output);
                return [output];
            }, ct),
        };
        JobRunner.Start(action, [file]);
        Close();
    }
}
