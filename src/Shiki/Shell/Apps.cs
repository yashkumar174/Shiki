using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Shiki.Core;
using Shiki.Core.Ai;

namespace Shiki.Shell;

/// <summary>
/// Opening and closing apps by name. Opening only reaches what's in the Start menu (desktop and
/// Store apps alike), never a file path. Closing asks the app to close as its own X button would,
/// so it can still ask to save.
/// </summary>
public static class Apps
{
    public sealed record App(string Name, string Id);

    static List<App>? _cache;
    static DateTime _cachedAt;

    /// <summary>Everything in the Start menu's "All apps" (read through the shell; cached for a few minutes).</summary>
    public static List<App> All()
    {
        if (_cache != null)
        {
            // Stale: answer from what we have and look again in the background.
            if (DateTime.Now - _cachedAt > TimeSpan.FromMinutes(5))
            {
                _cachedAt = DateTime.Now;
                System.Threading.Tasks.Task.Run(Refresh);
            }
            return _cache;
        }
        return Refresh();
    }

    static List<App> Refresh()
    {
        List<App> list = [];
        // The shell's objects want a single-threaded apartment.
        var thread = new Thread(() => list = Read()) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));
        _cache = list;
        _cachedAt = DateTime.Now;
        return list;
    }

    static List<App> Read()
    {
        var list = new List<App>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            try
            {
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    try
                    {
                        string name = item.Name, id = item.Path;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                        // Web links and uninstallers aren't apps anyone asks for.
                        if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase) || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new App(name, id));
                    }
                    catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                    {
                        // A broken shortcut: skip it.
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not list the Start menu apps", ex);
        }
        return list.DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name).ToList();
    }

    // What people call apps, against what the Start menu calls them.
    static readonly Dictionary<string, string> Nicknames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google Chrome",
        ["browser"] = "Microsoft Edge",
        ["edge"] = "Microsoft Edge",
        ["excel"] = "Excel",
        ["powerpoint"] = "PowerPoint",
        ["notepad"] = "Notepad",
        ["calculator"] = "Calculator",
        ["camera"] = "Camera",
        ["settings"] = "Settings",
        ["file explorer"] = "File Explorer",
        ["explorer"] = "File Explorer",
        ["files"] = "File Explorer",
        ["paint"] = "Paint",
        ["photos"] = "Photos",
        ["store"] = "Microsoft Store",
        ["mail"] = "Outlook",
        ["vs code"] = "Visual Studio Code",
        ["vscode"] = "Visual Studio Code",
        ["terminal"] = "Terminal",
        ["task manager"] = "Task Manager",
    };

    /// <summary>The Start menu app that best fits the name said, or null.</summary>
    /// <param name="strict">Only a clear fit (the app's whole name, or its last word: "Word").</param>
    public static App? Find(string name, bool strict = false)
    {
        var apps = All();
        int least = strict ? 65 : 20;
        if (Nicknames.TryGetValue(name.Trim(), out var real) && NameMatch.Best(apps, a => a.Name, real, 45) is { } known) return known;
        return NameMatch.Best(apps, a => a.Name, name, least);
    }

    /// <summary>Opens the app; returns what happened, for the cat to say.</summary>
    public static string Open(string name)
    {
        if (name.Trim().Equals("task manager", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })?.Dispose();
            return "Opened Task Manager";
        }
        if (Find(name) is not { } app)
        {
            var close = All().Where(a => NameMatch.Clean(a.Name).Split(' ').Any(w => NameMatch.Clean(name).Split(' ').Any(n => n.Length > 2 && w.StartsWith(n[..3])))).Take(8).Select(a => a.Name).ToList();
            return close.Count > 0
                ? $"There's no app called \"{name}\" in the Start menu. Close ones: {string.Join(", ", close)}."
                : $"There's no app called \"{name}\" in the Start menu.";
        }
        // The shell opens Start menu entries by their id, Store and desktop apps alike.
        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.Id}") { UseShellExecute = false })?.Dispose();
        AiLog.Write("opened app", app.Name);
        return $"Opened {app.Name}";
    }

    public sealed record Running(Process Process, string Name, string Title);

    /// <summary>Apps with a window on screen now (not background helpers).</summary>
    public static List<Running> Windows()
    {
        var list = new List<Running>();
        int self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self || !OnScreen(process.MainWindowHandle) || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    process.Dispose();
                    continue;
                }
                string name = process.ProcessName;
                try
                {
                    if (process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description) name = description;
                }
                catch (Exception)
                {
                    // Elevated or protected: the process name will do.
                }
                list.Add(new Running(process, name, process.MainWindowTitle));
            }
            catch (InvalidOperationException)
            {
                process.Dispose();
            }
        }
        return list;
    }

    /// <summary>A real window on the taskbar: shown, not a tool palette, not hidden by Windows (cloaked).</summary>
    static bool OnScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return false;
        if ((GetWindowLong(hwnd, -20) & 0x80) != 0) return false;
        return DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    /// <summary>Asks each window of the app to close, like its X button. Never forces it.</summary>
    /// <param name="justCheck">Only say what would close: "OK " and its name, or why nothing would.</param>
    public static string Close(string name, bool justCheck = false)
    {
        if (Nicknames.TryGetValue(name.Trim(), out var real)) name = real;
        if (name == "File Explorer") return justCheck ? "OK File Explorer" : CloseExplorer();
        var running = Windows();
        try
        {
            var want = NameMatch.Clean(name);
            var matches = running.Where(r =>
                NameMatch.Clean(r.Name).Contains(want) || NameMatch.Clean(r.Process.ProcessName) == want.Replace(" ", "")
                || NameMatch.Best([r], x => x.Name, name) != null).ToList();
            if (matches.Count == 0)
            {
                // Maybe it was the window's title ("close the invoice").
                matches = running.Where(r => NameMatch.Clean(r.Title).Contains(want)).ToList();
            }
            if (matches.Count == 0)
                return $"Nothing called \"{name}\" is open. Open now: {string.Join(", ", running.Select(r => r.Name).Distinct().Take(15))}.";
            // The taskbar and desktop live in Explorer too.
            matches.RemoveAll(m => m.Process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase));
            if (matches.Count == 0) return justCheck ? "OK File Explorer" : CloseExplorer();
            var label = matches[0].Name;
            if (justCheck) return $"OK {label}{(matches.Count > 1 ? $" ({matches.Count} windows)" : "")}";
            foreach (var match in matches) match.Process.CloseMainWindow();
            AiLog.Write("closed app", label);
            return matches.Count == 1 ? $"Asked {label} to close" : $"Asked {matches.Count} windows of {label} to close";
        }
        finally
        {
            foreach (var r in running) r.Process.Dispose();
        }
    }

    /// <summary>Closes the File Explorer windows (not the taskbar or desktop).</summary>
    static string CloseExplorer()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            var cls = new System.Text.StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is "CabinetWClass" && IsWindowVisible(hwnd)) windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        if (windows.Count == 0) return "No File Explorer windows are open.";
        foreach (var hwnd in windows) PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
        AiLog.Write("closed app", "File Explorer");
        return windows.Count == 1 ? "Closed File Explorer" : $"Closed {windows.Count} File Explorer windows";
    }

    delegate bool WindowEnum(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowEnum callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
