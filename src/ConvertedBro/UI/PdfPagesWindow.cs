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
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ConvertedBro.UI;

/// <summary>
/// Page editor: every page of the dropped PDFs as a thumbnail. Drag to reorder, rotate or delete,
/// then save one PDF. With several PDFs it doubles as merge-and-reorder.
/// </summary>
public sealed class PdfPagesWindow : Window
{
    const double ThumbWidth = 150;

    sealed class PageItem
    {
        public required string File { get; init; }
        public required int Index { get; init; }
        public int Rotation { get; set; }
        public BitmapSource? Thumb { get; set; }
        public Border? Card { get; set; }
        public Image? Picture { get; set; }
        public TextBlock? Number { get; set; }
    }

    readonly Palette _p = Theme.Current;
    readonly IReadOnlyList<string> _files;
    readonly List<PageItem> _pages = [];
    readonly WrapPanel _grid = new() { Margin = new Thickness(16) };
    readonly TextBlock _count = UiKit.Body("");
    readonly TextBlock _error = UiKit.Error();
    PageItem? _selected;

    public PdfPagesWindow(IReadOnlyList<string> files)
    {
        _files = files;
        Title = files.Count == 1 ? $"Pages of {Path.GetFileName(files[0])}" : $"Pages of {files.Count} PDFs";
        Width = 1040;
        Height = 740;
        MinWidth = 640;
        MinHeight = 480;
        UiKit.Prepare(this);

        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 4) };
        header.Children.Add(UiKit.Title("Pages"));
        header.Children.Add(UiKit.Body("Drag pages to reorder. Hover a page to turn or remove it. Delete removes the selected page."));
        header.Children.Add(_error);

        var scroller = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Theme.Brush(Color.FromRgb(0x0E, 0x0E, 0x10)) };

        var footer = new DockPanel { Margin = new Thickness(24, 12, 24, 16), LastChildFill = false };
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Margin = new Thickness(0);
        DockPanel.SetDock(_count, Dock.Left);
        footer.Children.Add(_count);
        var buttons = UiKit.Row(UiKit.Button("Cancel", Close), UiKit.Button("Save PDF", Save, primary: true));
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(scroller);
        Content = root;

        KeyDown += OnKeyDown;
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        foreach (var file in _files)
        {
            try
            {
                int count = await Task.Run(() =>
                {
                    lock (PdfTools.PdfiumGate)
                    {
                        using var doc = DocLib.Instance.GetDocReader(file, new PageDimensions(0.1));
                        return doc.GetPageCount();
                    }
                });
                for (int i = 0; i < count; i++) _pages.Add(new PageItem { File = file, Index = i });
            }
            catch (Exception ex)
            {
                Log.Error($"Page editor could not open {file}", ex);
                ShowError($"Couldn't open {Path.GetFileName(file)}. If it has a password, unlock it first.");
            }
        }
        Rebuild();

        // Thumbnails load in the background, page by page.
        foreach (var page in _pages.ToList())
        {
            page.Thumb = await Task.Run(() => RenderThumb(page.File, page.Index));
            if (page.Picture != null) page.Picture.Source = page.Thumb;
        }
    }

    static BitmapSource? RenderThumb(string file, int index)
    {
        try
        {
            lock (PdfTools.PdfiumGate)
            {
                using var doc = DocLib.Instance.GetDocReader(file, new PageDimensions(0.45));
                using var page = doc.GetPageReader(index);
                int w = page.GetPageWidth(), h = page.GetPageHeight();
                var raw = page.GetImage();
                // PDFium leaves the paper transparent; paint it white.
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
        catch
        {
            return null;
        }
    }

    // ---- Cards ----

    void Rebuild()
    {
        _grid.Children.Clear();
        for (int i = 0; i < _pages.Count; i++) _grid.Children.Add(Card(_pages[i], i));
        _count.Text = _pages.Count == 1 ? "1 page" : $"{_pages.Count} pages";
    }

    Border Card(PageItem page, int position)
    {
        var picture = new Image
        {
            Source = page.Thumb,
            Width = ThumbWidth,
            Height = ThumbWidth * 1.414,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(page.Rotation),
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        page.Picture = picture;

        var number = new TextBlock
        {
            Text = _files.Count > 1 ? $"{position + 1}  ·  {Path.GetFileNameWithoutExtension(page.File)} p{page.Index + 1}" : $"{position + 1}",
            FontFamily = Theme.LabelFont,
            FontSize = 12,
            Foreground = Theme.Brush(_p.SubtleText),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = ThumbWidth,
            Margin = new Thickness(0, 8, 0, 0),
        };
        page.Number = number;

        var tools = UiKit.Row(
            UiKit.Button("\uE7AD", () => Rotate(page), icon: true),
            UiKit.Button("\uE74D", () => Remove(page), icon: true));
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.VerticalAlignment = VerticalAlignment.Top;
        tools.Margin = new Thickness(0, 6, -2, 0);
        tools.Visibility = Visibility.Hidden;

        var sheet = new Grid { Children = { new Border { Background = Brushes.White, Child = picture }, tools } };
        var card = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(_p.Pixel ? 0 : 10),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = Theme.Brush(_p.RowHover),
            Cursor = Cursors.Hand,
            AllowDrop = true,
            Child = new StackPanel { Children = { sheet, number } },
            Tag = page,
        };
        page.Card = card;

        card.MouseEnter += (_, _) => tools.Visibility = Visibility.Visible;
        card.MouseLeave += (_, _) => tools.Visibility = Visibility.Hidden;
        Point? pressed = null;
        card.MouseLeftButtonDown += (_, e) => { pressed = e.GetPosition(card); SelectPage(page); };
        card.MouseMove += (_, e) =>
        {
            if (pressed is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(card);
            if (Math.Abs(now.X - start.X) + Math.Abs(now.Y - start.Y) < 8) return;
            pressed = null;
            card.Opacity = 0.4;
            DragDrop.DoDragDrop(card, new DataObject(typeof(PageItem), page), DragDropEffects.Move);
            card.Opacity = 1;
        };
        card.MouseLeftButtonUp += (_, _) => pressed = null;
        card.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(typeof(PageItem)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            Highlight(card, e.GetPosition(card).X < card.ActualWidth / 2 ? -1 : 1);
        };
        card.DragLeave += (_, _) => Highlight(card, 0);
        card.Drop += (_, e) =>
        {
            Highlight(card, 0);
            if (e.Data.GetData(typeof(PageItem)) is not PageItem moving || moving == page) return;
            bool after = e.GetPosition(card).X >= card.ActualWidth / 2;
            _pages.Remove(moving);
            int target = _pages.IndexOf(page) + (after ? 1 : 0);
            _pages.Insert(target, moving);
            Rebuild();
            SelectPage(moving);
        };
        if (page == _selected) card.BorderBrush = Theme.Brush(Theme.Brand);
        return card;
    }

    /// <summary>Shows which side of a card a dragged page will land on.</summary>
    void Highlight(Border card, int side) =>
        card.BorderBrush = side == 0
            ? Theme.Brush(card.Tag == _selected ? Theme.Brand : Colors.Transparent)
            : new LinearGradientBrush(
                side < 0 ? Theme.Brand : Colors.Transparent,
                side < 0 ? Colors.Transparent : Theme.Brand, 0);

    void SelectPage(PageItem page)
    {
        if (_selected?.Card != null) _selected.Card.BorderBrush = Brushes.Transparent;
        _selected = page;
        if (page.Card != null) page.Card.BorderBrush = Theme.Brush(Theme.Brand);
    }

    void Rotate(PageItem page)
    {
        page.Rotation = (page.Rotation + 90) % 360;
        if (page.Picture != null) page.Picture.RenderTransform = new RotateTransform(page.Rotation);
    }

    void Remove(PageItem page)
    {
        if (_pages.Count <= 1)
        {
            ShowError("A PDF needs at least one page.");
            return;
        }
        int at = _pages.IndexOf(page);
        _pages.Remove(page);
        if (_selected == page) _selected = null;
        Rebuild();
        if (_pages.Count > 0) SelectPage(_pages[Math.Min(at, _pages.Count - 1)]);
    }

    void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_selected == null) return;
        int at = _pages.IndexOf(_selected);
        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                Remove(_selected);
                break;
            case Key.R:
                Rotate(_selected);
                break;
            case Key.Left when at > 0:
            case Key.Right when at < _pages.Count - 1:
                // Ctrl+arrow moves the page; plain arrow moves the selection.
                int to = at + (e.Key == Key.Left ? -1 : 1);
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    var page = _selected;
                    _pages.RemoveAt(at);
                    _pages.Insert(to, page);
                    Rebuild();
                    SelectPage(page);
                }
                else
                {
                    SelectPage(_pages[to]);
                }
                break;
            case Key.Escape:
                Close();
                break;
        }
    }

    // ---- Saving ----

    void Save()
    {
        if (_pages.Count == 0) return;
        var plan = _pages.Select(p => (p.File, p.Index, p.Rotation)).ToList();
        var first = _files[0];
        var action = new BroAction
        {
            Id = "pdf-pages-run",
            Label = "Pages",
            Short = "PDF",
            Hint = "",
            Kind = FileKind.Pdf,
            AcceptsExtension = _ => true,
            Combine = true,
            Run = (inputs, progress, ct) => Task.Run<IReadOnlyList<string>>(() =>
            {
                var output = OutputPaths.Next(first, ".pdf", "edited");
                var sources = new Dictionary<string, PdfDocument>();
                try
                {
                    using var result = new PdfDocument();
                    for (int i = 0; i < plan.Count; i++)
                    {
                        var (file, index, rotation) = plan[i];
                        if (!sources.TryGetValue(file, out var source))
                            sources[file] = source = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                        var added = result.AddPage(source.Pages[index]);
                        added.Rotate = (added.Rotate + rotation) % 360;
                        progress.Report((i + 1) / (double)plan.Count);
                    }
                    result.Save(output);
                    return [output];
                }
                finally
                {
                    foreach (var doc in sources.Values) doc.Dispose();
                }
            }, ct),
        };
        JobRunner.Start(action, _files);
        Close();
    }
}
