using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Shiki.UI.Dashboard;

/// <summary>
/// The cat's answers drawn the way a chat answer reads best: short paragraphs, bullet and
/// numbered lists, bold for the key facts, small headings, inline code and links. Written by
/// Claude in light Markdown; selectable, so any of it can be copied.
/// </summary>
public static class ChatText
{
    public static RichTextBox View(string markdown, double size, Color text, Color muted, Color accent, Color codeBack)
    {
        var box = new RichTextBox
        {
            IsReadOnly = true,
            IsDocumentEnabled = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Foreground = new SolidColorBrush(text),
            SelectionBrush = new SolidColorBrush(accent),
            Cursor = Cursors.IBeam,
            Document = Document(markdown, size, text, muted, accent, codeBack),
        };
        // Wheel scrolling belongs to the chat around it, not this text.
        box.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = box };
            (VisualTreeHelper.GetParent(box) as UIElement)?.RaiseEvent(up);
        };
        return box;
    }

    static FlowDocument Document(string markdown, double size, Color text, Color muted, Color accent, Color codeBack)
    {
        var doc = new FlowDocument
        {
            FontFamily = DashKit.Sans,
            FontSize = size,
            Foreground = new SolidColorBrush(text),
            PagePadding = new Thickness(0),
            LineHeight = size * 1.45,
        };
        List? list = null;
        bool numbered = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var bullet = Regex.Match(line, @"^\s*([-*•])\s+(.*)$");
            var number = Regex.Match(line, @"^\s*(\d+)[.)]\s+(.*)$");
            if (bullet.Success || number.Success)
            {
                bool isNumber = number.Success;
                if (list == null || numbered != isNumber)
                {
                    list = new List
                    {
                        MarkerStyle = isNumber ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, size * 0.55),
                        Padding = new Thickness(size * 1.3, 0, 0, 0),
                        MarkerOffset = size * 0.45,
                    };
                    if (isNumber && int.TryParse(number.Groups[1].Value, out var startAt)) list.StartIndex = startAt;
                    numbered = isNumber;
                    doc.Blocks.Add(list);
                }
                var item = new Paragraph { Margin = new Thickness(0, 0, 0, size * 0.25) };
                Inlines(item, (isNumber ? number : bullet).Groups[2].Value, size, accent, codeBack);
                list.ListItems.Add(new ListItem(item));
                continue;
            }
            list = null;
            if (line.Trim().Length == 0)
            {
                // A blank line ends the paragraph: the next line starts a new one.
                if (doc.Blocks.LastBlock is Paragraph last) last.Tag = null;
                continue;
            }
            var heading = Regex.Match(line, @"^\s*#{1,4}\s+(.*)$");
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, size * 0.55) };
            if (heading.Success)
            {
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = size + 1;
                paragraph.Margin = new Thickness(0, size * 0.2, 0, size * 0.3);
                Inlines(paragraph, heading.Groups[1].Value, size, accent, codeBack);
            }
            else Inlines(paragraph, line, size, accent, codeBack);
            // Lines written one after another without a gap stay one paragraph.
            if (doc.Blocks.LastBlock is Paragraph previous && !heading.Success && previous.Tag is "open")
            {
                previous.Inlines.Add(new LineBreak());
                foreach (var inline in new System.Collections.Generic.List<Inline>(paragraph.Inlines)) previous.Inlines.Add(inline);
                continue;
            }
            paragraph.Tag = heading.Success ? null : "open";
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }

    /// <summary>**bold**, *italic*, `code` and [links](https://…) inside a line.</summary>
    static void Inlines(Paragraph paragraph, string text, double size, Color accent, Color codeBack)
    {
        int at = 0;
        foreach (Match m in Regex.Matches(text, @"\*\*(.+?)\*\*|`([^`]+)`|\[([^\]]+)\]\((https?://[^)\s]+)\)|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])"))
        {
            if (m.Index > at) paragraph.Inlines.Add(new Run(text[at..m.Index]));
            if (m.Groups[1].Success) paragraph.Inlines.Add(new Bold(new Run(m.Groups[1].Value)));
            else if (m.Groups[2].Success)
                paragraph.Inlines.Add(new Run(m.Groups[2].Value)
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = size - 1,
                    Background = new SolidColorBrush(codeBack),
                });
            else if (m.Groups[3].Success && Uri.TryCreate(m.Groups[4].Value, UriKind.Absolute, out var uri))
            {
                var link = new Hyperlink(new Run(m.Groups[3].Value)) { NavigateUri = uri, Foreground = new SolidColorBrush(accent), Cursor = Cursors.Hand };
                link.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
                paragraph.Inlines.Add(link);
            }
            else if (m.Groups[5].Success) paragraph.Inlines.Add(new Italic(new Run(m.Groups[5].Value)));
            at = m.Index + m.Length;
        }
        if (at < text.Length) paragraph.Inlines.Add(new Run(text[at..]));
    }

}
