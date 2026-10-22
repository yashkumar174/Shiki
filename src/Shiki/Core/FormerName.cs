using System;
using System.IO;
using Microsoft.Win32;
using Shiki.Core.Ai;

namespace Shiki.Core;

/// <summary>
/// The app used to be called Converted Bro. The first time Shiki starts, everything the old name
/// left behind moves over: the data folder (settings, history, models, signature), the saved API
/// keys, the results folders, the Explorer menu and the start-with-Windows entry. Nothing is lost
/// and each step is skipped once done.
/// </summary>
public static class FormerName
{
    const string OldData = "ConvertedBro", OldFolder = "Converted Bro";
    static readonly string[] Keys = ["ClaudeApiKey", "DeepgramApiKey", "WisprApiKey"];

    /// <summary>Before anything else touches the data folder (the log creates it on first use).</summary>
    public static void MoveData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var old = Path.Combine(local, OldData);
        var now = Path.Combine(local, "Shiki");
        if (!Directory.Exists(old)) return;
        if (!Directory.Exists(now))
        {
            try
            {
                Directory.Move(old, now);
                return;
            }
            catch (IOException)
            {
                // Something has a file open (an old copy still running): move what can be moved.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        Directory.CreateDirectory(now);
        foreach (var entry in new DirectoryInfo(old).EnumerateFileSystemInfos())
        {
            var target = Path.Combine(now, entry.Name);
            if (File.Exists(target) || Directory.Exists(target)) continue;
            try
            {
                if (entry is DirectoryInfo dir) dir.MoveTo(target);
                else ((FileInfo)entry).CopyTo(target);
            }
            catch (Exception)
            {
                // Left where it was; the old folder stays until everything has moved.
            }
        }
        try
        {
            if (new DirectoryInfo(old).GetDirectories().Length == 0) Directory.Delete(old, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Keys, Explorer menu, start with Windows and results folders, once settings are loaded.</summary>
    public static void MoveTheRest(Settings settings, string exePath, string iconPath)
    {
        foreach (var name in Keys)
        {
            try
            {
                var oldKey = new SecretStore($"{OldData}/{name}");
                var newKey = new SecretStore($"Shiki/{name}");
                if (oldKey.Read() is { } value)
                {
                    if (!newKey.Exists) newKey.Save(value);
                    oldKey.Remove();
                    Log.Info($"Moved {name} to Shiki");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Could not move {name}", ex);
            }
        }

        // The Explorer menu: the old entries go, and the new one takes their place if they were there.
        bool hadMenu;
        using (var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\SystemFileAssociations\.png\shell\{OldData}")) hadMenu = key != null;
        if (hadMenu)
        {
            foreach (var ext in FileKinds.AllExtensions)
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{ext}\shell\{OldData}", throwOnMissingSubKey: false);
            Shell.ContextMenuRegistrar.Register(exePath, iconPath);
            Log.Info("Explorer menu moved to Shiki");
        }

        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
        {
            if (run?.GetValue(OldData) != null)
            {
                run.DeleteValue(OldData, throwOnMissingValue: false);
                Shell.StartupRegistration.Set(true, exePath);
                Log.Info("Start with Windows moved to Shiki");
            }
        }

        // Results: the folder keeps your files, so it's renamed only where it still has the old default name.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (settings.OutputFolder is { } folder && string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.Combine(home, OldFolder), StringComparison.OrdinalIgnoreCase))
        {
            var renamed = Rename(folder, OutputPaths.DefaultFolder);
            settings.OutputFolder = renamed;
            OutputPaths.Folder = renamed;
            settings.Save();
        }
        Rename(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), OldFolder),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shiki"));
        Rename(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), OldFolder),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Shiki"));

        // History still points into the old folders; where the file is now in the new one, follow it.
        foreach (var place in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos })
        {
            var parent = Environment.GetFolderPath(place);
            if (History.Repoint(Path.Combine(parent, OldFolder), Path.Combine(parent, "Shiki")) is > 0 and var count)
                Log.Info($"History: {count} paths now point into {Path.Combine(parent, "Shiki")}");
        }
    }

    /// <summary>Renames a folder if it exists and the new name is free; returns where it is now.</summary>
    static string Rename(string from, string to)
    {
        if (!Directory.Exists(from) || Directory.Exists(to)) return Directory.Exists(to) && !Directory.Exists(from) ? to : from;
        try
        {
            Directory.Move(from, to);
            Log.Info($"Renamed {from} to {to}");
            return to;
        }
        catch (Exception ex)
        {
            // Open in another app, say: keep using it where it is.
            Log.Error($"Could not rename {from}", ex);
            return from;
        }
    }
}
