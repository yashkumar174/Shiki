using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// The files selected in the File Explorer window (or on the desktop) that's in front, read
/// through the shell's own automation objects. Windows 11 tabs share one window, so the tab
/// that's showing is matched by its own window handle.
/// </summary>
public static class ExplorerSelection
{
    const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1;

    /// <summary>Null when the window in front isn't Explorer or the desktop; otherwise the selection (maybe empty).</summary>
    public static List<string>? FromForeground()
    {
        var front = NativeMethods.GetForegroundWindow();
        var className = ClassOf(front);
        bool desktop = className is "Progman" or "WorkerW";
        if (!desktop && className is not ("CabinetWClass" or "ExploreWClass")) return null;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic windows = shell.Windows();
                if (desktop) return DesktopSelection(windows);

                // The tab on show is the first tab child of the window.
                var activeTab = FindWindowEx(front, IntPtr.Zero, "ShellTabWindowClass", null);
                dynamic? match = null;
                foreach (dynamic window in windows)
                {
                    try
                    {
                        if (new IntPtr((long)window.HWND) != front) continue;
                        if (activeTab == IntPtr.Zero || TabOf(window) == activeTab) return Selected(window);
                        match ??= window;
                    }
                    catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                    {
                        // Internet Explorer leftovers and half-closed windows: skip.
                    }
                }
                return match == null ? [] : Selected(match);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the Explorer selection", ex);
            return [];
        }
    }

    /// <summary>
    /// The File Explorer window nearest the top (it needn't have focus: the dashboard may), with
    /// the folder of the tab on show and what's selected there. Null when no Explorer is open.
    /// </summary>
    public static (string? Folder, List<string> Selected)? Frontmost()
    {
        IntPtr explorer = IntPtr.Zero;
        for (var hwnd = GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, GW_HWNDNEXT))
            if (IsWindowVisible(hwnd) && ClassOf(hwnd) is "CabinetWClass" or "ExploreWClass") { explorer = hwnd; break; }
        if (explorer == IntPtr.Zero) return null;

        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            try
            {
                var activeTab = FindWindowEx(explorer, IntPtr.Zero, "ShellTabWindowClass", null);
                dynamic? match = null;
                foreach (dynamic window in shell.Windows())
                {
                    try
                    {
                        if (new IntPtr((long)window.HWND) != explorer) continue;
                        if (activeTab == IntPtr.Zero || TabOf(window) == activeTab) { match = window; break; }
                        match ??= window;
                    }
                    catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                    {
                    }
                }
                if (match == null) return null;
                string? folder = null;
                try
                {
                    string path = match.Document.Folder.Self.Path;
                    if (!string.IsNullOrEmpty(path) && !path.StartsWith("::")) folder = path;
                }
                catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // Home, This PC and other virtual places have no folder.
                }
                return (folder, Selected(match));
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the front Explorer window", ex);
            return null;
        }
    }

    /// <summary>True when the desktop itself is the window in front.</summary>
    public static bool DesktopInFront() => ClassOf(NativeMethods.GetForegroundWindow()) is "Progman" or "WorkerW";

    /// <summary>What's selected on the desktop, whatever window is in front (the dev test uses this).</summary>
    public static List<string> FromDesktop()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            return DesktopSelection(shell.Windows());
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    static List<string> DesktopSelection(dynamic windows)
    {
        object location = Type.Missing, root = Type.Missing;
        dynamic view = windows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out int _, SWFO_NEEDDISPATCH);
        return Selected(view);
    }

    static List<string> Selected(dynamic window)
    {
        var paths = new List<string>();
        foreach (dynamic item in window.Document.SelectedItems())
        {
            string path = item.Path;
            // Virtual items (This PC, Recycle Bin, phone folders) have no real path.
            if (!string.IsNullOrEmpty(path) && !path.StartsWith("::")) paths.Add(path);
        }
        return paths;
    }

    /// <summary>The window of the tab that an Explorer automation object belongs to.</summary>
    static IntPtr TabOf(object window)
    {
        if (window is not IServiceProvider provider) return IntPtr.Zero;
        var service = SID_STopLevelBrowser;
        var iid = typeof(IShellBrowser).GUID;
        if (provider.QueryService(ref service, ref iid, out var pointer) != 0 || pointer == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pointer);
            browser.GetWindow(out var hwnd);
            return hwnd;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    static string ClassOf(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    static Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, out IntPtr ppv);
    }

    // Only the first method (from IOleWindow) is used; the rest of the interface isn't declared.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    const uint GW_HWNDNEXT = 2;

    [DllImport("user32.dll")]
    static extern IntPtr GetTopWindow(IntPtr parent);

    [DllImport("user32.dll")]
    static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
}
