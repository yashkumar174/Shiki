using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Shiki.Shell;

/// <summary>
/// WhatsApp Web, read the way a screen reader reads it (Windows UI Automation): find its tab, press
/// "Use here" when WhatsApp says it's open in another window, wait for the chat's message box, and
/// after files are pasted, find the preview's Send button and press it. So nothing is typed or
/// pressed blind; each step is checked, and a step that doesn't come says where it stopped.
/// </summary>
public static class WhatsAppWeb
{
    static readonly string[] Browsers = ["chrome", "msedge", "brave", "vivaldi", "opera", "firefox"];

    /// <summary>Is the WhatsApp app installed (does anything open whatsapp: links)?</summary>
    public static bool AppInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"whatsapp\shell\open\command");
            return key?.GetValue(null) is string command && command.Trim().Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The browser window showing WhatsApp (its title says so), newest first.</summary>
    public static AutomationElement? Window()
    {
        try
        {
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            foreach (AutomationElement window in windows)
            {
                if (!window.Current.Name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var process = Process.GetProcessById(window.Current.ProcessId);
                    if (Browsers.Contains(process.ProcessName.ToLowerInvariant())) return window;
                }
                catch (ArgumentException) { }
            }
        }
        catch (ElementNotAvailableException) { }
        return null;
    }

    static AutomationElement? Find(AutomationElement scope, ControlType type, Func<string, bool> name)
    {
        try
        {
            foreach (AutomationElement element in scope.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type)))
            {
                try
                {
                    if (name(element.Current.Name ?? "")) return element;
                }
                catch (ElementNotAvailableException) { }
            }
        }
        catch (ElementNotAvailableException) { }
        return null;
    }

    /// <summary>
    /// Presses a button whatever kind it is: a plain one (invoke), one that opens a menu (expand:
    /// WhatsApp's Attach is one), a toggle, or a row to select.
    /// </summary>
    static bool Press(AutomationElement button)
    {
        try
        {
            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
                return true;
            }
            if (button.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand))
            {
                ((ExpandCollapsePattern)expand).Expand();
                return true;
            }
            if (button.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
            {
                ((TogglePattern)toggle).Toggle();
                return true;
            }
            if (button.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
            {
                ((SelectionItemPattern)select).Select();
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException) { }
        return false;
    }

    /// <summary>
    /// Waits for the chat to be ready: the WhatsApp window, then its message box ("Type a message").
    /// Presses "Use here" if WhatsApp is open in another window. Null if it doesn't come in time.
    /// </summary>
    /// <param name="reopen">Opens the chat's link again (once) when WhatsApp hangs on "Starting chat".</param>
    public static async Task<(AutomationElement Window, AutomationElement Box)?> WaitForChat(TimeSpan most, Action<string> note, Action? reopen = null, CancellationToken ct = default)
    {
        var start = DateTime.Now;
        var until = start + most;
        bool usedHere = false, sawWindow = false, sawLoading = false, reopened = false, invalid = false;
        DateTime? startingSince = null;
        NotOnWhatsApp = false;
        while (DateTime.Now < until && !ct.IsCancellationRequested)
        {
            var found = await Task.Run(() =>
            {
                if (Window() is not { } window) return ((AutomationElement, AutomationElement?)?)null;
                // Not opened for a while, it downloads the chats first ("Loading your chats"): wait longer, up to 2.5 minutes.
                if (Find(window, ControlType.Text, n => n.Contains("Loading your chats", StringComparison.OrdinalIgnoreCase) || n.Contains("messages are downloading", StringComparison.OrdinalIgnoreCase)) != null)
                {
                    if (!sawLoading) { sawLoading = true; note("WhatsApp Web is loading your chats (it can take a minute or two after a while away)"); }
                    var later = DateTime.Now.AddSeconds(30);
                    var cap = start.AddSeconds(150);
                    until = later > until ? (later < cap ? later : cap) : until;
                }
                if (!usedHere && Find(window, ControlType.Button, n => n.Equals("Use here", StringComparison.OrdinalIgnoreCase)) is { } useHere && Press(useHere))
                {
                    usedHere = true;
                    note("pressed \"Use here\" (WhatsApp was open in another window)");
                }
                // "Phone number shared via url is invalid": not on WhatsApp. Close the message and stop.
                if (Find(window, ControlType.Text, n => n.Contains("shared via url is invalid", StringComparison.OrdinalIgnoreCase)) != null)
                {
                    if (Find(window, ControlType.Button, n => n.Equals("OK", StringComparison.OrdinalIgnoreCase)) is { } ok) Press(ok);
                    invalid = true;
                    return (window, null);
                }
                // "Starting chat" that doesn't finish: cancel it and open the chat once more.
                if (Find(window, ControlType.Text, n => n.Equals("Starting chat", StringComparison.OrdinalIgnoreCase)) != null)
                {
                    startingSince ??= DateTime.Now;
                    if (DateTime.Now - startingSince > TimeSpan.FromSeconds(12) && !reopened && reopen != null)
                    {
                        if (Find(window, ControlType.Button, n => n.Equals("Cancel", StringComparison.OrdinalIgnoreCase)) is { } cancel) Press(cancel);
                        reopened = true;
                        startingSince = null;
                        note("WhatsApp was stuck on \"Starting chat\": cancelled it and opened the chat again");
                        reopen();
                        until = DateTime.Now.AddSeconds(30) > until ? DateTime.Now.AddSeconds(30) : until;
                    }
                }
                else startingSince = null;
                var box = Find(window, ControlType.Edit, n => n.Contains("message", StringComparison.OrdinalIgnoreCase) && !n.Contains("search", StringComparison.OrdinalIgnoreCase));
                return (window, box);
            }, ct);
            if (found is { } f)
            {
                if (!sawWindow) { sawWindow = true; note("the WhatsApp tab is open"); }
                if (f.Item2 is { } box) return (f.Item1, box);
            }
            if (invalid)
            {
                note("WhatsApp says this number isn't on WhatsApp");
                NotOnWhatsApp = true;
                return null;
            }
            await Task.Delay(700, ct);
        }
        note(sawWindow ? "the chat's message box never appeared (still loading, or the number isn't on WhatsApp)" : "no WhatsApp tab came up");
        return null;
    }

    /// <summary>The last wait ended because WhatsApp said the number isn't on WhatsApp.</summary>
    public static bool NotOnWhatsApp { get; private set; }

    /// <summary>Brings the window to the front and puts the cursor in the message box.</summary>
    public static bool FocusBox(AutomationElement window, AutomationElement box)
    {
        try
        {
            NativeMethods.ForceForeground(new IntPtr(window.Current.NativeWindowHandle));
            box.SetFocus();
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The Send button, once there's something to send (typed text, or the files' preview).</summary>
    public static Task<AutomationElement?> SendButton(AutomationElement window) =>
        Task.Run(() => Find(window, ControlType.Button, n => n.Equals("Send", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Waits for the Send button (up to <paramref name="most"/>) and presses it.</summary>
    public static async Task<bool> PressSend(AutomationElement window, TimeSpan most, CancellationToken ct = default)
    {
        var until = DateTime.Now + most;
        while (DateTime.Now < until && !ct.IsCancellationRequested)
        {
            if (await SendButton(window) is { } send && Press(send)) return true;
            await Task.Delay(400, ct);
        }
        return false;
    }

    static readonly string[] MediaTypes = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".bmp", ".mp4", ".mov", ".avi", ".mkv", ".webm", ".3gp", ".m4v"];

    /// <summary>The element with exactly this name (any kind), or null.</summary>
    static AutomationElement? Named(AutomationElement scope, string name)
    {
        try
        {
            return scope.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name, PropertyConditionFlags.IgnoreCase));
        }
        catch (ElementNotAvailableException) { return null; }
    }

    /// <summary>Presses it, or the nearest thing above it that can be pressed (a menu row's text, say).</summary>
    static bool PressUp(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        for (var e = element; e != null; e = walker.GetParent(e))
        {
            if (Press(e)) return true;
            try { if (e.Current.ControlType == ControlType.Document) break; } catch (ElementNotAvailableException) { break; }
        }
        return false;
    }

    /// <summary>
    /// Attaches files the way a person does: the Attach button (paperclip), then "Photos &amp; videos"
    /// (all pictures and videos) or "Document", then the file window: the path goes in the
    /// File name box and Open is pressed. True once Open was pressed; each step is noted.
    /// </summary>
    public static async Task<bool> AttachFiles(AutomationElement window, System.Collections.Generic.IReadOnlyList<string> files, Action<string> note)
    {
        bool media = files.All(f => MediaTypes.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));
        var attach = await Task.Run(() => Find(window, ControlType.Button, n => n.Equals("Attach", StringComparison.OrdinalIgnoreCase)));
        if (attach == null || !Press(attach)) { note("couldn't find WhatsApp's Attach button"); return false; }
        note("pressed Attach");

        string want = media ? "Photos & videos" : "Document";
        AutomationElement? item = null;
        for (int i = 0; i < 12 && item == null; i++)
        {
            await Task.Delay(250);
            item = await Task.Run(() => Named(window, want));
        }
        if (item == null || !await Task.Run(() => PressUp(item))) { note($"couldn't find \"{want}\" in the Attach menu"); return false; }
        note($"chose \"{want}\"");

        // The file window opens over Chrome (a standard Windows "Open" box).
        int process = window.Current.ProcessId;
        AutomationElement? dialog = null;
        for (int i = 0; i < 24 && dialog == null; i++)
        {
            await Task.Delay(250);
            dialog = await Task.Run(() => FileDialog(window, process));
        }
        if (dialog == null) { note("the file window didn't open"); return false; }
        note("the file window is open");

        bool picked = await Task.Run(() =>
        {
            if (FileNameBox(dialog) is not { } box) return false;
            var folders = files.Select(f => System.IO.Path.GetDirectoryName(f) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // One file: its full path. Several in one folder: go to the folder, then their names, quoted.
            string text = files.Count == 1 ? files[0]
                : folders.Count == 1 ? string.Join(" ", files.Select(f => $"\"{System.IO.Path.GetFileName(f)}\"")) : files[0];
            if (files.Count > 1 && folders.Count == 1)
            {
                if (!SetText(box, folders[0]) || !PressOpen(dialog)) return false;
                Thread.Sleep(700);
                if (FileNameBox(dialog) is not { } again) return false;
                box = again;
            }
            return SetText(box, text) && PressOpen(dialog);
        });
        if (!picked) { note("couldn't fill in the file window"); return false; }
        note(files.Count > 1 && files.Select(System.IO.Path.GetDirectoryName).Distinct().Count() > 1
            ? "picked the first file (the others are in other folders)" : $"picked {(files.Count == 1 ? "the file" : $"{files.Count} files")}");
        return true;
    }

    static AutomationElement? FileDialog(AutomationElement window, int process)
    {
        try
        {
            var dialogs = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"));
            foreach (AutomationElement d in dialogs)
                if (d.Current.ProcessId == process) return d;
            return window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"));
        }
        catch (ElementNotAvailableException) { return null; }
    }

    static AutomationElement? FileNameBox(AutomationElement dialog)
    {
        try
        {
            // The File name box (its id is 1148 in Windows' Open window).
            return dialog.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)))
                ?? Find(dialog, ControlType.Edit, n => n.StartsWith("File name", StringComparison.OrdinalIgnoreCase));
        }
        catch (ElementNotAvailableException) { return null; }
    }

    static bool SetText(AutomationElement box, string text)
    {
        try
        {
            if (box.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            {
                ((ValuePattern)pattern).SetValue(text);
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException) { }
        return false;
    }

    static bool PressOpen(AutomationElement dialog)
    {
        try
        {
            // Open is button 1 in Windows' Open window.
            var open = dialog.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "1"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                ?? Find(dialog, ControlType.Button, n => n.Equals("Open", StringComparison.OrdinalIgnoreCase));
            return open != null && Press(open);
        }
        catch (ElementNotAvailableException) { return false; }
    }

    /// <summary>Is the files' preview up (its caption box, or a Send button while the message box is empty)?</summary>
    public static Task<bool> PreviewUp(AutomationElement window) =>
        Task.Run(() => Find(window, ControlType.Edit, n => n.Contains("caption", StringComparison.OrdinalIgnoreCase)) != null
            || Find(window, ControlType.Button, n => n.Equals("Send", StringComparison.OrdinalIgnoreCase)) != null);
}
