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
    /// <summary>Screen recording: start (and, pressed again, stop). Not Ctrl+Shift+R: browsers hard-reload with that.</summary>
    public string RecordHotkey { get; set; } = "Ctrl+Alt+R";
    public bool RecordMic { get; set; }
    public bool RecordSound { get; set; } = true;
    /// <summary>Where results go: a folder, or null to keep them next to the original.</summary>
    public string? OutputFolder { get; set; } = OutputPaths.DefaultFolder;
    /// <summary>In the results folder, sort into Images, Videos, Audio, PDFs…</summary>
    public bool SortOutputs { get; set; }
    /// <summary>The cat's tally: files it has handled, and bytes saved where results came out smaller.</summary>
    public int FilesDone { get; set; }
    /// <summary>Dashboard look: true dark, false light, null follows Windows.</summary>
    public bool? DashboardDark { get; set; }
    /// <summary>Setup checklist: done once the convert shortcut has been used, or the results folder visited.</summary>
    public bool UsedConvertHotkey { get; set; }
    public bool ChoseOutputFolder { get; set; }
    /// <summary>Home tips the person closed.</summary>
    public System.Collections.Generic.List<string> DismissedTips { get; set; } = [];
    public long BytesSaved { get; set; }

    /// <summary>Ask the cat (Claude): on unless switched off. It also needs an API key, kept in Credential Manager.</summary>
    public bool AiEnabled { get; set; } = true;
    /// <summary>Global shortcut that opens the ask bubble by the cat.</summary>
    public string AskHotkey { get; set; } = "Ctrl+Alt+A";
    /// <summary>What the person calls their cat; null uses the skin's own name.</summary>
    public string? CatName { get; set; }
    /// <summary>What asking the cat has cost so far, in US dollars, and how many requests that was.</summary>
    public double AiSpentUsd { get; set; }
    public int AiRequests { get; set; }

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
