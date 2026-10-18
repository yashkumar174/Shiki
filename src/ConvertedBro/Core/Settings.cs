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
