using System;
using ConvertedBro.Core;
using Microsoft.Win32;

namespace ConvertedBro.Shell;

/// <summary>
/// Adds "Convert with Bro" to the right-click menu of every supported file type.
/// Uses HKCU only, so no admin rights are needed. On Windows 11 it appears under
/// "Show more options" (or Shift + right-click).
/// </summary>
public static class ContextMenuRegistrar
{
    const string Root = @"Software\Classes\SystemFileAssociations";
    const string VerbName = "ConvertedBro";
    const int SeparatorBefore = 0x20; // ECF_SEPARATORBEFORE

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\.png\shell\{VerbName}");
        return key != null;
    }

    public static void Register(string exePath, string iconPath)
    {
        foreach (var ext in FileKinds.AllExtensions)
        {
            var actions = ActionCatalog.ForExtension(ext);
            if (actions.Count == 0) continue;

            var verbPath = $@"{Root}\{ext}\shell\{VerbName}";
            Registry.CurrentUser.DeleteSubKeyTree(verbPath, throwOnMissingSubKey: false);
            using var verb = Registry.CurrentUser.CreateSubKey(verbPath);
            verb.SetValue("MUIVerb", "Convert with Bro");
            verb.SetValue("Icon", iconPath);
            verb.SetValue("SubCommands", "");
            // Without this Windows hides the verb when more than 15 files are selected.
            verb.SetValue("MultiSelectModel", "Player");

            for (int i = 0; i < actions.Count; i++)
                AddCommand(verb, $"{i:00}-{actions[i].Id}", actions[i].Label, $"\"{exePath}\" run {actions[i].Id} \"%1\"");

            using var more = AddCommand(verb, "99-more", "More options…", $"\"{exePath}\" menu \"%1\"");
            more.SetValue("CommandFlags", SeparatorBefore, RegistryValueKind.DWord);
        }
        NotifyShell();
        Log.Info($"Explorer menu registered for {exePath}");
    }

    public static void Unregister()
    {
        foreach (var ext in FileKinds.AllExtensions)
            Registry.CurrentUser.DeleteSubKeyTree($@"{Root}\{ext}\shell\{VerbName}", throwOnMissingSubKey: false);
        NotifyShell();
        Log.Info("Explorer menu removed");
    }

    static RegistryKey AddCommand(RegistryKey verb, string keyName, string label, string command)
    {
        var item = verb.CreateSubKey($@"shell\{keyName}");
        item.SetValue("MUIVerb", label);
        item.SetValue("MultiSelectModel", "Player");
        using var cmd = item.CreateSubKey("command");
        cmd.SetValue("", command);
        return item;
    }

    static void NotifyShell() =>
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
}
