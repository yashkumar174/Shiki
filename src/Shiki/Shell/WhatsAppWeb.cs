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

    static bool Press(AutomationElement button)
    {
        try
        {
            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                ((InvokePattern)pattern).Invoke();
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

    /// <summary>Is the files' preview up (its caption box, or a Send button while the message box is empty)?</summary>
    public static Task<bool> PreviewUp(AutomationElement window) =>
        Task.Run(() => Find(window, ControlType.Edit, n => n.Contains("caption", StringComparison.OrdinalIgnoreCase)) != null
            || Find(window, ControlType.Button, n => n.Equals("Send", StringComparison.OrdinalIgnoreCase)) != null);
}
