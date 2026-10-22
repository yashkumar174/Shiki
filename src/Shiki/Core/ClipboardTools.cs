using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ConvertedBro.Core;

public static class ClipboardTools
{
    /// <summary>
    /// Puts an image on the clipboard as a picture (for chat apps and editors) and as a file (for
    /// Explorer and mail attachments), so it pastes sensibly anywhere.
    /// </summary>
    public static void CopyImage(string path)
    {
        using var image = ImageTools.Load(path);
        var bytes = image.ToByteArray(MagickFormat.Png);
        Application.Current.Dispatcher.Invoke(() =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();

            var data = new DataObject();
            data.SetImage(bitmap);
            data.SetData("PNG", new MemoryStream(bytes));
            data.SetFileDropList(new StringCollection { path });
            Retry(() => Clipboard.SetDataObject(data, true));
        });
        JobNotes.Say("Copied the image, paste it anywhere");
    }

    /// <summary>A finished job's results: one picture pastes as a picture, anything else as files.</summary>
    public static void CopyFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1 && File.Exists(paths[0]) && FileKinds.Of(paths[0]) == FileKind.Image && !paths[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            CopyImage(paths[0]);
            return;
        }
        var list = new StringCollection();
        foreach (var path in paths) list.Add(path);
        Application.Current.Dispatcher.Invoke(() => Retry(() => Clipboard.SetFileDropList(list)));
    }

    /// <summary>Another app may hold the clipboard for a moment; try a few times.</summary>
    public static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 6)
            {
                Thread.Sleep(80);
            }
        }
    }
}
