using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Shiki.Core;

/// <summary>
/// The words inside a document, so the cat can answer questions about it, summarise it or read
/// it aloud: PDFs, Word, PowerPoint and Excel files (the modern formats are read directly, no
/// Office needed), and plain text. A PDF with no text in it (a scan) comes back as a smaller PDF
/// of its first pages instead, for Claude to look at.
/// </summary>
public static class Documents
{
    public sealed record Contents(string Text, int Pages, bool Cut, byte[]? ScannedPdf);

    public static readonly string[] Readable = [".pdf", ".docx", ".docm", ".pptx", ".pptm", ".xlsx", ".xlsm", ".txt", ".md", ".csv", ".json", ".log", ".xml", ".html", ".htm"];

    /// <summary>Reads the document, up to about <paramref name="most"/> characters.</summary>
    public static Contents Read(string path, int most = 60000)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var (text, pages, scanned) = ext switch
        {
            ".pdf" => Pdf(path),
            ".docx" or ".docm" => (Word(path), 0, null),
            ".pptx" or ".pptm" => Slides(path),
            ".xlsx" or ".xlsm" => (Sheets(path, most), 0, null),
            ".html" or ".htm" => (Regex.Replace(WebUtilityDecode(Regex.Replace(File.ReadAllText(path), @"<(script|style)[\s\S]*?</\1>|<[^>]+>", " ")), @"[ \t]+", " "), 0, null),
            _ when Readable.Contains(ext) => (File.ReadAllText(path), 0, (byte[]?)null),
            _ => throw new NotSupportedException($"I can't read {ext} files yet. PDF, Word (.docx), PowerPoint (.pptx), Excel (.xlsx) and text files work; an older .doc can be turned into a PDF first."),
        };
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        bool cut = text.Length > most;
        if (cut) text = text[..most];
        return new Contents(text, pages, cut, scanned);
    }

    static string WebUtilityDecode(string s) => System.Net.WebUtility.HtmlDecode(s);

    static (string, int, byte[]?) Pdf(string path)
    {
        var text = new StringBuilder();
        int pages;
        using (var doc = DocLib.Instance.GetDocReader(path, new PageDimensions(1.0)))
        {
            pages = doc.GetPageCount();
            for (int i = 0; i < pages; i++)
            {
                using var page = doc.GetPageReader(i);
                text.Append($"\n--- Page {i + 1} ---\n").Append(page.GetText());
                if (text.Length > 200_000) break;
            }
        }
        // Next to nothing to read: a scan. Hand over the first pages as a PDF to look at instead.
        var words = Regex.Matches(text.ToString(), @"\p{L}{2,}").Count;
        if (words < 20 * Math.Min(pages, 5))
            return ("", pages, FirstPages(path, 20));
        return (text.ToString(), pages, null);
    }

    /// <summary>The first pages as a new PDF (in memory).</summary>
    static byte[] FirstPages(string path, int count)
    {
        using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        if (source.PageCount <= count && new FileInfo(path).Length < 20 * 1024 * 1024) return File.ReadAllBytes(path);
        using var first = new PdfDocument();
        for (int i = 0; i < Math.Min(count, source.PageCount); i++) first.AddPage(source.Pages[i]);
        using var memory = new MemoryStream();
        first.Save(memory, false);
        return memory.ToArray();
    }

    static XDocument? Part(ZipArchive zip, string name) =>
        zip.GetEntry(name) is { } entry ? XDocument.Load(entry.Open()) : null;

    /// <summary>A Word file's paragraphs (tables come out a cell per line).</summary>
    static string Word(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var doc = Part(zip, "word/document.xml") ?? throw new InvalidDataException("That doesn't look like a Word file.");
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = new StringBuilder();
        foreach (var paragraph in doc.Descendants(w + "p"))
        {
            var line = string.Concat(paragraph.Descendants().Select(e =>
                e.Name == w + "t" ? e.Value : e.Name == w + "tab" ? "\t" : e.Name == w + "br" ? "\n" : ""));
            text.AppendLine(line);
        }
        return text.ToString();
    }

    static (string, int, byte[]?) Slides(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var slides = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"))
            .OrderBy(e => int.Parse(Regex.Match(e.FullName, @"\d+").Value)).ToList();
        var text = new StringBuilder();
        for (int i = 0; i < slides.Count; i++)
        {
            var slide = XDocument.Load(slides[i].Open());
            text.Append($"\n--- Slide {i + 1} ---\n");
            foreach (var paragraph in slide.Descendants(a + "p"))
            {
                var line = string.Concat(paragraph.Descendants(a + "t").Select(t => t.Value));
                if (line.Length > 0) text.AppendLine(line);
            }
        }
        return (text.ToString(), slides.Count, null);
    }

    /// <summary>Each sheet as rows of tab-separated cells (values as last saved, not formulas).</summary>
    static string Sheets(string path, int most)
    {
        using var zip = ZipFile.OpenRead(path);
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var shared = Part(zip, "xl/sharedStrings.xml")?.Root?.Elements(s + "si")
            .Select(si => string.Concat(si.Descendants(s + "t").Select(t => t.Value))).ToList() ?? [];
        var workbook = Part(zip, "xl/workbook.xml");
        var rels = Part(zip, "xl/_rels/workbook.xml.rels");
        XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
        var targets = rels?.Root?.Elements(pr + "Relationship").ToDictionary(x => (string)x.Attribute("Id")!, x => (string)x.Attribute("Target")!) ?? [];
        var text = new StringBuilder();
        foreach (var sheet in workbook?.Descendants(s + "sheet") ?? [])
        {
            var id = (string?)sheet.Attribute(r + "id");
            if (id == null || !targets.TryGetValue(id, out var target)) continue;
            var file = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            if (Part(zip, file) is not { } data) continue;
            text.Append($"\n--- Sheet \"{sheet.Attribute("name")?.Value}\" ---\n");
            foreach (var row in data.Descendants(s + "row"))
            {
                // Empty cells aren't stored: place each by its reference (C7 is the third column).
                var cells = new List<string>();
                foreach (var c in row.Elements(s + "c"))
                {
                    var type = (string?)c.Attribute("t");
                    var value = c.Element(s + "v")?.Value ?? string.Concat(c.Descendants(s + "t").Select(t => t.Value));
                    if (type == "s" && int.TryParse(value, out var n) && n < shared.Count) value = shared[n];
                    int column = Column((string?)c.Attribute("r")) ?? cells.Count;
                    while (cells.Count < column) cells.Add("");
                    cells.Add(value);
                }
                text.AppendLine(string.Join("\t", cells));
                if (text.Length > most) return text.ToString();
            }
        }
        return text.ToString();
    }

    static int? Column(string? reference)
    {
        if (reference == null) return null;
        int column = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return column > 0 ? column - 1 : null;
    }
}
