using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// Shiki's browser extension on this PC: kept in a folder of its own (copied from the app, so an
/// update never pulls it out from under the browser), and the person's usual browser, to add it to.
/// </summary>
public static class BrowserSetup
{
    /// <summary>Where the extension lives for the browser to load (Shiki's data folder).</summary>
    public static string Folder => Path.Combine(Log.DataDir, "Browser extension");

    /// <summary>Copies the app's extension into its folder when it's new or changed.</summary>
    public static void Install()
    {
        try
        {
            var from = Path.Combine(AppContext.BaseDirectory, "browser-extension");
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(Folder);
            foreach (var file in Directory.GetFiles(from))
            {
                var to = Path.Combine(Folder, Path.GetFileName(file));
                if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(file).Length && File.ReadAllBytes(to).AsSpan().SequenceEqual(File.ReadAllBytes(file))) continue;
                File.Copy(file, to, overwrite: true);
            }
        }
        catch (Exception ex) { Log.Error("Putting the browser extension in place", ex); }
    }

    /// <summary>
    /// The folder as the browser sees it. When Shiki was started from inside a packaged app (the
    /// Claude app, say), Windows keeps its files in that app's own folder; this finds the real one.
    /// </summary>
    public static string RealFolder
    {
        get
        {
            var manifest = Path.Combine(Folder, "manifest.json");
            if (!File.Exists(manifest)) return Folder;
            return Path.GetDirectoryName(Firewall.RealPath(manifest)) ?? Folder;
        }
    }

    /// <summary>The person's usual browser: its name, program, and its extensions page; Supported is false for Firefox and the like.</summary>
    public static (string Name, string? Exe, string ExtensionsPage, bool Supported) Default()
    {
        string progId = "";
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            progId = choice?.GetValue("ProgId") as string ?? "";
        }
        catch (Exception) { }
        string? exe = null;
        try
        {
            using var command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var line = command?.GetValue(null) as string ?? "";
            exe = line.StartsWith('"') ? line[1..line.IndexOf('"', 1)] : line.Split(' ')[0];
            if (!File.Exists(exe)) exe = null;
        }
        catch (Exception) { }
        var id = progId.ToLowerInvariant();
        if (id.StartsWith("chromehtml")) return ("Chrome", exe, "chrome://extensions", true);
        if (id.StartsWith("msedgehtm")) return ("Edge", exe, "edge://extensions", true);
        if (id.StartsWith("bravehtml")) return ("Brave", exe, "brave://extensions", true);
        if (id.StartsWith("opera")) return ("Opera", exe, "opera://extensions", true);
        if (id.StartsWith("vivaldi")) return ("Vivaldi", exe, "vivaldi://extensions", true);
        if (id.StartsWith("firefox")) return ("Firefox", exe, "about:addons", false);
        // Unknown: Edge is always there and takes the extension.
        var edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
        return ("Edge", File.Exists(edge) ? edge : null, "edge://extensions", true);
    }

    /// <summary>Shiki's page in the store for this browser (Edge Add-ons for Edge, else the Chrome Web Store), or null before it's published.</summary>
    public static string? StoreUrl(string browser) =>
        browser == "Edge" && BrowserBridge.EdgeStoreId is { } edge ? $"https://microsoftedge.microsoft.com/addons/detail/{edge}"
        : BrowserBridge.ChromeStoreId is { } chrome ? $"https://chromewebstore.google.com/detail/{chrome}"
        : null;

    /// <summary>Opens Shiki's store page in the person's usual browser, where "Add to Chrome" is one click.</summary>
    public static bool OpenStorePage()
    {
        var (name, exe, _, _) = Default();
        if (StoreUrl(name) is not { } url) return false;
        try
        {
            if (exe != null) Process.Start(new ProcessStartInfo(exe, url) { UseShellExecute = false })?.Dispose();
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Opening Shiki's store page", ex);
            return false;
        }
    }

    /// <summary>Opens the browser's extensions page (where "Load unpacked" is).</summary>
    public static bool OpenExtensionsPage()
    {
        var (_, exe, page, _) = Default();
        if (exe == null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, page) { UseShellExecute = false })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Opening the browser's extensions page", ex);
            return false;
        }
    }
}
