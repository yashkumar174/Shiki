using System;
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
