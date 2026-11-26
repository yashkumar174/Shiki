using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiki.Core;

/// <summary>
/// Screenshots kept together: each one taken while collecting joins the pile, and the pile is
/// what pastes. Each shot is a PNG in %LOCALAPPDATA%\Shiki\shots (cleared after three days).
/// The clipboard always holds the whole pile: as files (chat apps, mail, Explorer take them all)
/// and as one picture of them stacked (apps that only take a picture).
/// </summary>
public static class ShotStack
{
    static readonly List<string> _shots = [];

    public static IReadOnlyList<string> Shots => _shots;
    public static bool Collecting { get; private set; }

    /// <summary>The pile changed (a shot added, the pile cleared).</summary>
    public static event Action? Changed;

    /// <summary>The clipboard's sequence number right after Shiki itself wrote to it: that change isn't a new screenshot.</summary>
    public static uint OwnSequence { get; private set; }

    public static string Folder
    {
        get
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiki", "shots");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Saves a picture as a shot file and returns its path (not yet in the pile).</summary>
    public static string Save(byte[] png, string? label = null)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss.fff");
        var path = Path.Combine(Folder, $"Screenshot {stamp}{(label == null ? "" : " " + label)}.png");
        File.WriteAllBytes(path, png);
        return path;
    }

    /// <summary>Starts collecting with these shots (the first screenshot, or a page in parts).</summary>
    public static void Start(IEnumerable<string> first)
    {
        _shots.Clear();
        _shots.AddRange(first);
        Collecting = true;
        Publish();
    }

    public static void Add(IEnumerable<string> more)
    {
        _shots.AddRange(more);
        Publish();
    }

    /// <summary>Takes one shot back out of the pile.</summary>
    public static void Remove(string path)
    {
        if (!_shots.Remove(path)) return;
        if (_shots.Count == 0) { Clear(); return; }
        Publish();
    }

    /// <summary>Stops collecting. The clipboard keeps what it last had.</summary>
    public static void Clear()
    {
        _shots.Clear();
        Collecting = false;
        Changed?.Invoke();
    }

    static void Publish()
    {
        PutPileOnClipboard();
        Changed?.Invoke();
    }

    // ---- The clipboard ----

    /// <summary>The whole pile: every shot as a file, plus all of them stacked as one picture.</summary>
    public static void PutPileOnClipboard()
    {
        if (_shots.Count == 0) return;
        var files = new StringCollection();
        foreach (var shot in _shots) files.Add(shot);
        var picture = _shots.Count == 1 ? Load(_shots[0]) : Stacked(_shots);
        var data = new DataObject();
        data.SetFileDropList(files);
        if (picture != null)
        {
            data.SetImage(picture);
            data.SetData("PNG", new MemoryStream(Png(picture)));
        }
        Write(data);
    }

    /// <summary>One shot as a picture only (for pasting them one after another).</summary>
    public static void PutOneOnClipboard(string path)
    {
        var picture = Load(path);
        if (picture == null) return;
        var data = new DataObject();
        data.SetImage(picture);
        data.SetData("PNG", new MemoryStream(Png(picture)));
        Write(data);
    }

    /// <summary>All shots stacked into one picture, as a picture only.</summary>
    public static void PutStackOnClipboard()
    {
        var picture = Stacked(_shots);
        if (picture == null) return;
        var data = new DataObject();
        data.SetImage(picture);
        data.SetData("PNG", new MemoryStream(Png(picture)));
        Write(data);
    }

    static void Write(DataObject data)
    {
        ClipboardTools.Retry(() => Clipboard.SetDataObject(data, true));
        OwnSequence = Shell.NativeMethods.GetClipboardSequenceNumber();
    }

    // ---- Pictures ----

    public static BitmapSource? Load(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Error("Reading a screenshot", ex);
            return null;
        }
    }

    /// <summary>The shots one under another, left-aligned on white, with a thin gap between them.</summary>
    public static BitmapSource? Stacked(IReadOnlyList<string> paths)
    {
        var pictures = paths.Select(Load).Where(p => p != null).Cast<BitmapSource>().ToList();
        if (pictures.Count == 0) return null;
        const int Gap = 12;
        int width = pictures.Max(p => p.PixelWidth);
        int height = pictures.Sum(p => p.PixelHeight) + Gap * (pictures.Count - 1);
        // Very tall stacks are scaled down to a size apps still accept.
        double scale = Math.Min(1, 16000.0 / height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width * scale, height * scale));
            double y = 0;
            foreach (var picture in pictures)
            {
                dc.DrawImage(picture, new Rect(0, y * scale, picture.PixelWidth * scale, picture.PixelHeight * scale));
                y += picture.PixelHeight + Gap;
            }
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(BitmapSource picture)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Shots older than three days go (once, at start).</summary>
    public static void Tidy()
    {
        Task.Run(() =>
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Folder, "*.png"))
                    if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-3)) File.Delete(file);
            }
            catch (Exception ex) { Log.Error("Tidying old screenshots", ex); }
        });
    }
}
