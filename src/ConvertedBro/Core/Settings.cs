using System;
using System.IO;
using System.Text.Json;

namespace ConvertedBro.Core;

public sealed class Settings
{
    /// <summary>Horizontal spot along the bottom edge where the cat lives (DIPs).</summary>
    public double? CatLeft { get; set; }
    public string Cat { get; set; } = "mochi";
    /// <summary>"arcade" (pixel) or "glass".</summary>
    public string Style { get; set; } = "arcade";
    public bool BuddyVisible { get; set; } = true;
    public bool ShiftDragSummon { get; set; } = true;
    /// <summary>Glass look: keep the glass in step with what's behind it (hides popups from screen recordings).</summary>
    public bool LiveGlass { get; set; } = true;
    /// <summary>Global shortcut for snip and convert, e.g. "Ctrl+Shift+Q".</summary>
    public string SnipHotkey { get; set; } = "Ctrl+Shift+Q";
    /// <summary>Global shortcut for the colour picker. Not Ctrl+Shift+C: terminals copy and browsers inspect with that.</summary>
    public string ColourHotkey { get; set; } = "Ctrl+Alt+C";
    /// <summary>Global shortcut that opens the wheel on the files selected in Explorer or on the desktop.</summary>
    public string ConvertHotkey { get; set; } = "Ctrl+Alt+X";

    static string FilePath => Path.Combine(Log.DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
