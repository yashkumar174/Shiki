using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shiki.Core;

/// <summary>One thing a routine does: a PC control, an app, a website, one of the app's own tools, or a line to say.</summary>
public sealed class RoutineStep
{
    /// <summary>One of <see cref="Routines.Kinds"/>.</summary>
    public string Do { get; set; } = "";
    /// <summary>The app's name, the website, the control's number, the tool, or the words to say.</summary>
    public string? Value { get; set; }
}

/// <summary>A few things done together when you say a phrase ("start work mode").</summary>
public sealed class Routine
{
    public string Name { get; set; } = "";
    public List<string> Phrases { get; set; } = [];
    public List<RoutineStep> Steps { get; set; } = [];
}

/// <summary>The routines, saved in routines.json. Two come ready: work mode and done for the day.</summary>
public static class Routines
{
    /// <summary>What a step can do, and what its value means.</summary>
    public static readonly Dictionary<string, string> Kinds = new()
    {
        ["do_not_disturb_on"] = "no value",
        ["do_not_disturb_off"] = "no value",
        ["set_volume"] = "0 to 100",
        ["mute"] = "no value",
        ["unmute"] = "no value",
        ["set_brightness"] = "0 to 100",
        ["dark_mode_on"] = "no value",
        ["dark_mode_off"] = "no value",
        ["switch_sound_output"] = "the output's name, e.g. headphones",
        ["open_app"] = "the app's name as in the Start menu",
        ["close_app"] = "the app's name",
        ["open_website"] = "a full https:// address",
        ["app_tool"] = "one of: take_notes, stop_notes, open_tasks, open_meetings, snip, record_screen",
        ["say"] = "a short line the cat says",
    };

    static string FilePath => Path.Combine(Log.DataDir, "routines.json");
    static List<Routine>? _all;

    public static IReadOnlyList<Routine> All => _all ??= Load();

    static List<Routine> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<Routine>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the routines", ex);
            return [];
        }
        // First run: the two everyone gets.
        var ready = new List<Routine>
        {
            new()
            {
                Name = "Work mode",
                Phrases = ["start work mode", "work mode", "let's work", "time to work"],
                Steps = [new() { Do = "do_not_disturb_on" }, new() { Do = "app_tool", Value = "open_tasks" }, new() { Do = "say", Value = "Work mode: notifications are quiet, and here's your list." }],
            },
            new()
            {
                Name = "Done for the day",
                Phrases = ["done for the day", "i'm done for the day", "i'm done for today", "end of day", "wrap up for the day", "that's it for today"],
                Steps = [new() { Do = "do_not_disturb_off" }, new() { Do = "say", Value = "Good work today. Notifications are back on." }],
            },
        };
        Write(ready);
        return ready;
    }

    static void Write(List<Routine> list)
    {
        _all = list;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the routines", ex);
        }
    }

    /// <summary>Adds the routine, or replaces the one with the same name.</summary>
    public static void Save(Routine routine)
    {
        var list = All.Where(r => !r.Name.Equals(routine.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(routine);
        Write(list);
    }

    public static bool Remove(string name)
    {
        var list = All.ToList();
        if (list.RemoveAll(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        Write(list);
        return true;
    }

    public static Routine? Find(string name) =>
        All.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? All.FirstOrDefault(r => r.Phrases.Any(p => Plain(p) == Plain(name)));

    /// <summary>The routine whose phrase (or name) was said, exactly, give or take "please".</summary>
    public static Routine? Match(string said)
    {
        var words = Regex.Replace(Plain(said), @"^(please |okay |ok |now )+|( please| now)+$", "");
        return All.FirstOrDefault(r => Plain(r.Name) == words || r.Phrases.Any(p => Plain(p) == words));
    }

    static string Plain(string text) =>
        string.Join(" ", Regex.Replace(text.ToLowerInvariant().Replace("'", ""), @"[^a-z0-9 ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A step in a few words: "Do not disturb on", "Open Zoom".</summary>
    public static string Describe(RoutineStep step) => step.Do switch
    {
        "do_not_disturb_on" => "Do not disturb on",
        "do_not_disturb_off" => "Do not disturb off",
        "set_volume" => $"Volume {step.Value}%",
        "mute" => "Mute",
        "unmute" => "Unmute",
        "set_brightness" => $"Brightness {step.Value}%",
        "dark_mode_on" => "Dark mode",
        "dark_mode_off" => "Light mode",
        "switch_sound_output" => $"Sound to {step.Value}",
        "open_app" => $"Open {step.Value}",
        "close_app" => $"Close {step.Value}",
        "open_website" => $"Open {(Uri.TryCreate(step.Value, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "") : step.Value)}",
        "app_tool" => step.Value switch
        {
            "take_notes" => "Take meeting notes",
            "stop_notes" => "Stop the notes",
            "open_tasks" => "Show my tasks",
            "open_meetings" => "Show my meetings",
            "snip" => "Snip",
            "record_screen" => "Record the screen",
            _ => step.Value ?? "",
        },
        "say" => $"Say \"{step.Value}\"",
        _ => step.Do,
    };

    /// <summary>Checks a step; null when it's fine, otherwise what's wrong.</summary>
    public static string? Problem(RoutineStep step)
    {
        if (!Kinds.ContainsKey(step.Do)) return $"\"{step.Do}\" isn't a step a routine can do.";
        return step.Do switch
        {
            "set_volume" or "set_brightness" when !int.TryParse(step.Value, out var n) || n < 0 || n > 100 => $"{step.Do} needs a number from 0 to 100.",
            "open_app" or "close_app" or "switch_sound_output" or "say" when string.IsNullOrWhiteSpace(step.Value) => $"{step.Do} needs a value.",
            "open_website" when !Uri.TryCreate(step.Value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") => "open_website needs a full https:// address.",
            "app_tool" when step.Value is not ("take_notes" or "stop_notes" or "open_tasks" or "open_meetings" or "snip" or "record_screen") => "app_tool must be take_notes, stop_notes, open_tasks, open_meetings, snip or record_screen.",
            _ => null,
        };
    }
}
