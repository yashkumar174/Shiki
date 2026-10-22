using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ConvertedBro.Core;

namespace ConvertedBro.Shell;

/// <summary>Opens File Explorer with files highlighted ("show me"), one window per folder.</summary>
public static class ExplorerReveal
{
    public static void Show(IEnumerable<string> paths)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToList();
        // A lone folder: open it, rather than show it selected in its parent.
        if (list.Count == 1 && Directory.Exists(list[0]))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{list[0]}\"") { UseShellExecute = true });
            return;
        }
        foreach (var group in list.GroupBy(p => Path.GetDirectoryName(p) ?? p, StringComparer.OrdinalIgnoreCase).Take(4))
            Select(group.Key, group.ToList());
    }

    static void Select(string folder, List<string> items)
    {
        var folderId = ILCreateFromPath(folder);
        var itemIds = items.Select(ILCreateFromPath).Where(p => p != IntPtr.Zero).ToArray();
        try
        {
            if (folderId == IntPtr.Zero || SHOpenFolderAndSelectItems(folderId, (uint)itemIds.Length, itemIds, 0) != 0)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{items[0]}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not show {folder} in Explorer", ex);
        }
        finally
        {
            foreach (var id in itemIds) ILFree(id);
            if (folderId != IntPtr.Zero) ILFree(folderId);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr ILCreateFromPath(string path);

    [DllImport("shell32.dll")]
    static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll")]
    static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);
}
