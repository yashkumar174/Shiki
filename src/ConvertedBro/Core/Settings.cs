using System;
using System.IO;
using System.Text.Json;

namespace ConvertedBro.Core;

public sealed class Settings
{
    public double? BuddyLeft { get; set; }
    public double? BuddyTop { get; set; }
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
