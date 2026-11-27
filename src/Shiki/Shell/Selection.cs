using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// The text selected in the app in front: read with a quick Ctrl+C, and replaced with Ctrl+V.
/// Whatever was on the clipboard before is put back afterwards, so copying stays yours.
/// </summary>
public static class Selection
{
    /// <summary>The selected text in that window (null if nothing is selected, or it isn't text).</summary>
    public static async Task<string?> Read(IntPtr window, bool all = false)
    {
        await TypeText.WaitForModifiersUp();
        if (window != IntPtr.Zero && NativeMethods.GetForegroundWindow() != window) NativeMethods.ForceForeground(window);
        var saved = Saved.Take();
        uint before = NativeMethods.GetClipboardSequenceNumber();
        if (all)
        {
            TypeText.SelectAll();
            await Task.Delay(60);
        }
        TypeText.Copy();
        // The app copies when it gets to it: wait for the clipboard to change (it doesn't when nothing is selected).
        string? text = null;
        for (int i = 0; i < 16; i++)
        {
            await Task.Delay(40);
            if (NativeMethods.GetClipboardSequenceNumber() == before) continue;
            await Task.Delay(30);
            text = Try(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);
            break;
        }
        saved.PutBack();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Puts this text in place of the selection in that window. False if the window's gone.</summary>
    public static async Task<bool> Replace(IntPtr window, string text)
    {
        await TypeText.WaitForModifiersUp();
        if (window != IntPtr.Zero)
        {
            if (!NativeMethods.IsWindow(window)) return false;
            NativeMethods.ForceForeground(window);
            await Task.Delay(120);
        }
        var saved = Saved.Take();
        if (!Try(() => { Clipboard.SetText(text); return true; })) return false;
        await Task.Delay(60);
        TypeText.Paste();
        // Let the app take it before the clipboard goes back to what it was.
        await Task.Delay(450);
        saved.PutBack();
        return true;
    }

    /// <summary>Ctrl+Z in that window (the app's own undo puts the old text back).</summary>
    public static async Task Undo(IntPtr window)
    {
        await TypeText.WaitForModifiersUp();
        if (window != IntPtr.Zero && NativeMethods.IsWindow(window)) NativeMethods.ForceForeground(window);
        await Task.Delay(120);
        TypeText.Undo();
    }

    static T? Try<T>(Func<T?> read)
    {
        // The clipboard is briefly locked by whoever is writing to it: try a few times.
        for (int i = 0; i < 5; i++)
        {
            try { return read(); }
            catch (Exception) { System.Threading.Thread.Sleep(30); }
        }
        return default;
    }

    /// <summary>What was on the clipboard, in the formats that matter, to put back after.</summary>
    sealed class Saved
    {
        readonly Dictionary<string, object> _formats = [];
        static readonly string[] Kept = [DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html, DataFormats.FileDrop, DataFormats.Bitmap, "PNG"];

        public static Saved Take()
        {
            var saved = new Saved();
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return saved;
                foreach (var format in Kept)
                {
                    try
                    {
                        if (!data.GetDataPresent(format, false)) continue;
                        var value = data.GetData(format, false);
                        // Streams are read now: the app that owns them may be gone later.
                        if (value is System.IO.Stream stream)
                        {
                            var copy = new System.IO.MemoryStream();
                            stream.CopyTo(copy);
                            copy.Position = 0;
                            value = copy;
                        }
                        if (value != null) saved._formats[format] = value;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception ex) { Log.Error("Keeping the clipboard", ex); }
            return saved;
        }

        public void PutBack()
        {
            try
            {
                if (_formats.Count == 0) { Clipboard.Clear(); return; }
                var data = new DataObject();
                foreach (var (format, value) in _formats) data.SetData(format, value);
                Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex) { Log.Error("Putting the clipboard back", ex); }
        }
    }
}
