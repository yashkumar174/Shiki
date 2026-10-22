using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// The PC's own controls, for voice: volume and mute, the sound output, screen brightness, dark
/// mode, Do Not Disturb, and lock, sleep, restart and shut down. Each one says what happened in a
/// short sentence, so the cat can repeat it.
/// </summary>
public static class PcControl
{
    // ---- Sound ----

    static MMDevice Speakers() => new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    /// <summary>The volume (0 to 100), whether it's muted, and where the sound goes.</summary>
    public static (int Percent, bool Muted, string Output) Volume()
    {
        using var device = Speakers();
        var volume = device.AudioEndpointVolume;
        return ((int)Math.Round(volume.MasterVolumeLevelScalar * 100), volume.Mute, device.FriendlyName);
    }

    public static string SetVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        using var device = Speakers();
        device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
        // Turning it up means you want to hear it.
        if (percent > 0) device.AudioEndpointVolume.Mute = false;
        return $"Volume {percent}%";
    }

    public static string ChangeVolume(int by) => SetVolume(Volume().Percent + by);

    public static string Mute(bool mute)
    {
        using var device = Speakers();
        device.AudioEndpointVolume.Mute = mute;
        return mute ? "Sound muted" : $"Sound on, at {(int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100)}%";
    }

    public sealed record Output(string Id, string Name, bool Current);

    /// <summary>The speakers and headphones plugged in now.</summary>
    public static List<Output> Outputs()
    {
        var enumerator = new MMDeviceEnumerator();
        string current;
        using (var speakers = Speakers()) current = speakers.ID;
        var list = new List<Output>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            list.Add(new Output(device.ID, device.FriendlyName, device.ID == current));
            device.Dispose();
        }
        return list;
    }

    /// <summary>Sends the sound to the output whose name best fits (e.g. "headphones", "Realtek").</summary>
    public static string SetOutput(string name)
    {
        var outputs = Outputs();
        if (outputs.Count == 0) return "No speakers or headphones are plugged in.";
        var pick = NameMatch.Best(outputs, o => o.Name, name);
        if (pick == null)
            return $"No output called \"{name}\". There's: {string.Join(", ", outputs.Select(o => o.Name))}.";
        if (pick.Current) return $"Sound already goes to {pick.Name}.";
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            foreach (var role in new[] { 0, 1, 2 }) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(pick.Id, role));
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
        return $"Sound now goes to {pick.Name}";
    }

    // Windows' own (undocumented, long-stable) way to pick the default output; Settings uses it too.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient;

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        // Only the order matters for the ones before SetDefaultEndpoint.
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }

    // ---- Brightness ----

    /// <summary>The screen's brightness (0 to 100), or null when it can't be read (most desktop monitors).</summary>
    public static int? Brightness()
    {
        try
        {
            using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject screen in search.Get())
                using (screen) return Convert.ToInt32(screen["CurrentBrightness"]);
        }
        catch (ManagementException)
        {
            // No built-in screen.
        }
        return MonitorBrightness.Get();
    }

    public static string SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        bool done = false;
        try
        {
            using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject screen in search.Get())
                using (screen)
                {
                    screen.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)percent]);
                    done = true;
                }
        }
        catch (ManagementException)
        {
            // No built-in screen: try the monitor's own controls below.
        }
        // A desktop monitor, or a second screen on a laptop.
        done |= MonitorBrightness.Set(percent);
        return done ? $"Brightness {percent}%" : "This screen doesn't let apps change its brightness; use the buttons on the monitor.";
    }

    public static string ChangeBrightness(int by) =>
        Brightness() is { } now ? SetBrightness(now + by) : "This screen doesn't let apps change its brightness; use the buttons on the monitor.";

    // ---- Dark mode ----

    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool DarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Personalize);
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    public static string SetDarkMode(bool dark)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
        {
            key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        }
        // Tell open windows and the taskbar, as Settings does.
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out _);
        return dark ? "Dark mode on" : "Light mode on";
    }

    // ---- Do Not Disturb ----

    // The state Windows' notification centre listens to (what Focus Assist / Do Not Disturb sets):
    // 0 off, 1 priority only, 2 alarms only. Not documented, so anything odd falls back to Settings.
    const ulong QuietHoursProfile = 0x0D83063EA3BF1C75;

    /// <summary>Whether Do Not Disturb is on, or null when it can't be read.</summary>
    public static bool? DoNotDisturb()
    {
        var state = QuietHoursProfile;
        var buffer = new byte[4];
        int size = buffer.Length;
        try
        {
            if (NtQueryWnfStateData(ref state, IntPtr.Zero, IntPtr.Zero, out _, buffer, ref size) != 0) return null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
        return size >= 4 && BitConverter.ToInt32(buffer) != 0;
    }

    public static string SetDoNotDisturb(bool on)
    {
        var state = QuietHoursProfile;
        var data = BitConverter.GetBytes(on ? 1 : 0);
        try
        {
            if (NtUpdateWnfStateData(ref state, data, data.Length, IntPtr.Zero, IntPtr.Zero, 0, 0) == 0 && DoNotDisturb() == on)
                return on ? "Do not disturb is on: notifications wait quietly" : "Do not disturb is off";
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Fall through to Settings.
        }
        Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true })?.Dispose();
        return $"Windows didn't let me switch it, so I opened the notification settings: turn Do not disturb {(on ? "on" : "off")} there.";
    }

    // ---- Power ----

    public enum Power { Lock, Sleep, Restart, ShutDown }

    public static Power? ParsePower(string? word) => word?.Replace(" ", "").Replace("_", "").ToLowerInvariant() switch
    {
        "lock" => Power.Lock,
        "sleep" => Power.Sleep,
        "restart" => Power.Restart,
        "shutdown" => Power.ShutDown,
        _ => null,
    };

    public static string Describe(Power power) => power switch
    {
        Power.Lock => "Lock the PC",
        Power.Sleep => "Put the PC to sleep",
        Power.Restart => "Restart the PC",
        _ => "Shut down the PC",
    };

    /// <summary>Does it. Restart and shut down let apps ask to save first, as usual.</summary>
    public static string Do(Power power)
    {
        switch (power)
        {
            case Power.Lock:
                LockWorkStation();
                return "Locked.";
            case Power.Sleep:
                SetSuspendState(false, false, false);
                return "Slept, and woke up again.";
            case Power.Restart:
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
                return "Restarting in a few seconds.";
            default:
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 5") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
                return "Shutting down in a few seconds.";
        }
    }

    /// <summary>Everything at once, for "what's my volume?" and friends.</summary>
    public static string Status()
    {
        var parts = new List<string>();
        try
        {
            var (percent, muted, output) = Volume();
            parts.Add($"volume {percent}%{(muted ? " (muted)" : "")} on {output}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the volume", ex);
            parts.Add("no sound output");
        }
        parts.Add(Brightness() is { } b ? $"brightness {b}%" : "brightness can't be read on this screen");
        parts.Add(DarkMode() ? "dark mode" : "light mode");
        if (DoNotDisturb() is { } quiet) parts.Add(quiet ? "do not disturb on" : "do not disturb off");
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            parts.Add($"battery {power.BatteryLifePercent * 100:0}%{(power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? ", charging" : "")}");
        return string.Join("; ", parts) + ".";
    }

    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventsDisabled);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);
    [DllImport("ntdll.dll")]
    static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope, out uint changeStamp, byte[] buffer, ref int bufferSize);
    [DllImport("ntdll.dll")]
    static extern int NtUpdateWnfStateData(ref ulong stateName, byte[] buffer, int length, IntPtr typeId, IntPtr explicitScope, uint matchingChangeStamp, int checkStamp);
}

/// <summary>Brightness of external monitors through their own controls (DDC/CI), where they allow it.</summary>
static class MonitorBrightness
{
    public static int? Get()
    {
        int? found = null;
        Each((handle) =>
        {
            if (found == null && GetMonitorBrightness(handle, out var min, out var now, out var max) && max > min)
                found = (int)Math.Round((now - min) * 100.0 / (max - min));
        });
        return found;
    }

    public static bool Set(int percent)
    {
        bool any = false;
        Each((handle) =>
        {
            if (GetMonitorBrightness(handle, out var min, out _, out var max) && max > min)
                any |= SetMonitorBrightness(handle, (uint)(min + (max - min) * percent / 100));
        });
        return any;
    }

    static void Each(Action<IntPtr> use)
    {
        var monitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, _, _) => { monitors.Add(m); return true; }, IntPtr.Zero);
        foreach (var monitor in monitors)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) continue;
            var physical = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) continue;
            try
            {
                foreach (var p in physical) use(p.Handle);
            }
            finally
            {
                DestroyPhysicalMonitors(count, physical);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint now, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
}

/// <summary>Picks the name that best fits what was said ("word" → "Microsoft Word", not "WordPad").</summary>
public static class NameMatch
{
    public static T? Best<T>(IEnumerable<T> items, Func<T, string> name, string said, int least = 20) where T : class
    {
        var want = Clean(said);
        if (want.Length == 0) return null;
        var wantWords = want.Split(' ');
        T? best = null;
        int bestScore = 0;
        foreach (var item in items)
        {
            var have = Clean(name(item));
            if (have.Length == 0) continue;
            var haveWords = have.Split(' ');
            int score =
                have == want ? 100 :
                // "word" fits "microsoft word" better than "wordpad"
                haveWords.Length > 1 && haveWords[^1] == want ? 80 :
                haveWords.Contains(want) ? 70 :
                have.StartsWith(want + " ") ? 65 :
                have.StartsWith(want) ? 50 :
                wantWords.All(w => haveWords.Any(h => h.StartsWith(w))) ? 45 :
                have.Contains(want) ? 30 :
                // Every word said is in the name, in another order ("studio visual" for "Visual Studio").
                wantWords.All(w => w.Length <= 2 || haveWords.Contains(w)) && wantWords.Any(w => w.Length > 2) ? 25 : 0;
            // Shorter names win ties: "Teams" over "Teams Machine-Wide Installer".
            if (score > bestScore || (score == bestScore && score > 0 && best != null && have.Length < Clean(name(best)).Length))
            {
                best = item;
                bestScore = score;
            }
        }
        return bestScore >= least ? best : null;
    }

    public static string Clean(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
