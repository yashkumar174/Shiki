using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConvertedBro.Core;

namespace ConvertedBro.UI.Dashboard;

/// <summary>
/// Every tool in one place, by kind of file: "convert to" chips, then tiles for the tools. Click
/// one and pick the files; editors (scan, sign, pages…) open as they do from the wheel.
/// </summary>
public sealed class ToolsPage(DashKit k, DashboardHost host)
{
    DashPalette P => k.P;

    static readonly (FileKind Kind, string Title, string Blurb)[] Sections =
    [
        (FileKind.Pdf, "PDF", "Merge, split, sign, lock and tidy up PDFs."),
        (FileKind.Image, "Images", "Convert, shrink, cut out backgrounds and make form photos."),
        (FileKind.Video, "Video", "Convert, compress and trim the sound off."),
        (FileKind.Audio, "Audio", "Convert and even out the volume."),
        (FileKind.Document, "Documents", "Word, Excel and PowerPoint to PDF, with the Office on this PC."),
        (FileKind.Archive, "Archives", "Unpack zip, 7z, rar and tar."),
    ];

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 48), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(k.Text("Tools", 30, weight: FontWeights.SemiBold, font: DashKit.Display));
        var sub = k.Text("Pick a tool, then the files. Or just drop files on the cat, or select them and press " + host.Settings.ConvertHotkey + ".", 15, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 8);
        page.Children.Add(sub);

        page.Children.Add(Section("Capture and quick tools", "Things that start from your screen or clipboard.", Quick()));
        foreach (var (kind, title, blurb) in Sections)
        {
            // Only what works on this PC (to Word needs Word, say).
            var actions = ActionCatalog.All.Where(a => a.Kind == kind && (a.Available?.Invoke() ?? true)).ToList();
            if (actions.Count == 0) continue;
            page.Children.Add(Section(title, blurb, Content(actions)));
        }
        var zip = ActionCatalog.Find("zip");
        if (zip != null)
            page.Children.Add(Section("Any files", "Works with anything, folders included.", Tiles([Tile(zip.Glyph ?? "", "Zip", zip.Hint, () => Pick(zip, folders: true))])));
        return page;
    }

    UIElement Section(string title, string blurb, UIElement body)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 34, 0, 0) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var name = k.Text(title, 26, font: DashKit.Serif);
        head.Children.Add(name);
        var note = k.Text(blurb, 14, P.Muted);
        note.Margin = new Thickness(14, 0, 0, 5);
        note.VerticalAlignment = VerticalAlignment.Bottom;
        head.Children.Add(note);
        stack.Children.Add(head);
        body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 14, 0, 0));
        stack.Children.Add(body);
        return stack;
    }

    UIElement Content(List<BroAction> actions)
    {
        var stack = new StackPanel();
        // Formats, size limits and form presets read best as chips; tools as tiles.
        foreach (var (group, label) in new[] { (ActionGroup.Format, "Convert to"), (ActionGroup.Size, "Shrink under"), (ActionGroup.Form, "Form photos") })
        {
            var items = actions.Where(a => a.Group == group).ToList();
            if (items.Count == 0) continue;
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var tag = k.Text(label, 14, P.Faint);
            tag.Width = 110;
            tag.Margin = new Thickness(2, 8, 0, 0);
            row.Children.Add(tag);
            foreach (var action in items)
            {
                var chip = k.Button(action.Short, () => Pick(action), DashKit.Pill.Quiet, size: 13.5);
                chip.Padding = new Thickness(14, 7, 14, 8);
                chip.Margin = new Thickness(0, 0, 8, 8);
                chip.ToolTip = action.Hint;
                row.Children.Add(chip);
            }
            stack.Children.Add(row);
        }
        var tools = actions.Where(a => a.Group == ActionGroup.Tool).ToList();
        if (tools.Count > 0) stack.Children.Add(Tiles(tools.Select(a => Tile(a.Glyph ?? "\uE90F", a.Short, a.Hint, () => Pick(a))).ToList()));
        return stack;
    }

    UIElement Quick() => Tiles(
    [
        Tile("\uE924", "Snip and convert", $"Drag a box on screen ({host.Settings.SnipHotkey})", host.Snip),
        Tile("\uE714", "Record the screen", $"Video of an area or the whole screen ({host.Settings.RecordHotkey})", host.Record),
        Tile("\uE790", "Pick a colour", $"Copy any colour on screen ({host.Settings.ColourHotkey})", host.PickColour),
        Tile("\uED14", "Make a QR code", "From a link or any text", host.MakeQr),
        Tile("\uE77F", "Paste from clipboard", "Files, a screenshot or text", host.Paste),
    ]);

    static UIElement Tiles(List<Border> tiles)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var tile in tiles) grid.Children.Add(tile);
        grid.SizeChanged += (_, _) => grid.Columns = grid.ActualWidth < 760 ? 3 : 4;
        return grid;
    }

    Border Tile(string glyph, string title, string hint, Action onClick)
    {
        var stack = new StackPanel();
        var badge = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(11),
            Background = DashKit.B(DashKit.A(P.Accent, P.Dark ? (byte)0x26 : (byte)0x2E)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = k.Icon(glyph, 17, P.Dark ? P.Accent : P.AccentEdge),
        };
        stack.Children.Add(badge);
        var name = k.Text(title, 15.5, weight: FontWeights.SemiBold);
        name.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(name);
        var text = k.Text(hint, 13, P.Muted, wrap: true);
        text.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(text);

        var fill = new SolidColorBrush(P.Card);
        var tile = new Border
        {
            Background = fill,
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 18, 18, 18),
            Margin = new Thickness(0, 0, 12, 12),
            Cursor = Cursors.Hand,
            Child = stack,
            MinHeight = 140,
        };
        tile.MouseEnter += (_, _) => { fill.Color = P.Dark ? P.Hover : Color.FromRgb(0xFA, 0xF8, 0xF5); tile.BorderBrush = DashKit.B(DashKit.A(P.AccentEdge, 0x90)); };
        tile.MouseLeave += (_, _) => { fill.Color = P.Card; tile.BorderBrush = DashKit.B(P.Line); };
        tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return tile;
    }

    /// <summary>Asks for the files this action takes, then runs it (or opens its editor).</summary>
    void Pick(BroAction action, bool folders = false)
    {
        var extensions = FileKinds.AllExtensions.Where(action.AcceptsExtension).ToList();
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = action.Label.TrimEnd('…'),
            Multiselect = true,
            Filter = folders || extensions.Count == 0
                ? "All files|*.*"
                : $"{action.Label.TrimEnd('…')}|{string.Join(";", extensions.Select(x => "*" + x))}|All files|*.*",
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;
        host.Run(action, [.. dialog.FileNames]);
    }
}
