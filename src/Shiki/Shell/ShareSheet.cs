using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Shiki.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Shiki.Shell;

/// <summary>
/// Windows' own Share window for some files (to WhatsApp, Mail, Nearby Share, Outlook…), plus the
/// two plain ways of handing files on: copied (paste them anywhere) and moved to the Recycle Bin.
/// </summary>
public static class ShareSheet
{
    static readonly Dictionary<IntPtr, DataTransferManager> Managers = [];
    static readonly Dictionary<IntPtr, List<string>> Pending = [];

    /// <summary>Opens Share over this window for these files. False if Windows couldn't.</summary>
    public static bool Share(Window owner, IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return false;
        try
        {
            var hwnd = new WindowInteropHelper(owner).Handle;
            if (hwnd == IntPtr.Zero) return false;
            Pending[hwnd] = files;
            if (!Managers.ContainsKey(hwnd))
            {
                var manager = DataTransferManagerInterop.GetForWindow(hwnd);
                manager.DataRequested += async (_, e) =>
                {
                    var request = e.Request;
                    var deferral = request.GetDeferral();
                    try
                    {
                        var list = Pending.TryGetValue(hwnd, out var p) ? p : [];
                        var items = new List<Windows.Storage.IStorageItem>();
                        foreach (var path in list) items.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
                        request.Data.Properties.Title = list.Count == 1 ? Path.GetFileName(list[0]) : $"{list.Count} files from your phone";
                        request.Data.Properties.Description = "Shared from Shiki";
                        request.Data.SetStorageItems(items);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Getting files ready to share", ex);
                        request.FailWithDisplayText("Those files couldn't be shared.");
                    }
                    finally { deferral.Complete(); }
                };
                Managers[hwnd] = manager;
                owner.Closed += (_, _) => { Managers.Remove(hwnd); Pending.Remove(hwnd); };
            }
            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Opening Share", ex);
            return false;
        }
    }

    /// <summary>Puts the files on the clipboard, as Explorer's Copy does: paste them into a folder, a chat, a mail.</summary>
    public static bool Copy(IReadOnlyList<string> paths)
    {
        var files = new StringCollection();
        foreach (var path in paths.Where(File.Exists)) files.Add(path);
        if (files.Count == 0) return false;
        try { System.Windows.Clipboard.SetFileDropList(files); return true; }
        catch (Exception ex) { Log.Error("Copying files", ex); return false; }
    }

    /// <summary>Moves them to the Recycle Bin (they can be put back from there). How many went.</summary>
    public static int Recycle(IReadOnlyList<string> paths)
    {
        int gone = 0;
        foreach (var path in paths.Where(File.Exists))
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                gone++;
            }
            catch (Exception ex) { Log.Error($"Moving {Path.GetFileName(path)} to the Recycle Bin", ex); }
        }
        return gone;
    }
}
