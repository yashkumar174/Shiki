using Microsoft.Win32;

namespace Shiki.Shell;

public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Shiki";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        // --background: at sign-in only the cat appears, not the dashboard.
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" --background");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
