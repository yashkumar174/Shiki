using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Shiki.Core;

/// <summary>One finished job, as the dashboard lists it.</summary>
public sealed class HistoryEntry
{
    public DateTime Time { get; set; }
    public string ActionId { get; set; } = "";
    /// <summary>What was done, e.g. "PNG" or "Compress".</summary>
    public string Action { get; set; } = "";
    public FileKind Kind { get; set; }
    public List<string> Inputs { get; set; } = [];
    public List<string> Outputs { get; set; } = [];
    /// <summary>The job's own one-liner ("Copied 214 words"), if it left one.</summary>
    public string? Note { get; set; }
    public bool Failed { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Every finished job, newest first, kept in history.json next to the settings. Capped so the
/// file stays small; removing an entry only forgets it (the files stay where they are).
/// </summary>
public static class History
{
    const int Max = 2000;
    static readonly object Gate = new();
    static List<HistoryEntry>? _entries;
    static string FilePath => Path.Combine(Log.DataDir, "history.json");

    /// <summary>Raised on the thread that added or removed something.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (Gate) return [.. Load()];
        }
    }

    public static void Add(Job job)
    {
        if (job.State is not (JobState.Done or JobState.Failed)) return;
        var entry = new HistoryEntry
        {
            Time = DateTime.Now,
            ActionId = job.Action.Id,
            Action = job.Action.Short,
            Kind = job.Action.Kind == FileKind.Unknown && job.Inputs.Count > 0 ? FileKinds.Of(job.Inputs[0]) : job.Action.Kind,
            Inputs = [.. job.Inputs],
            Outputs = [.. job.Outputs],
            Note = job.Note,
            Failed = job.State == JobState.Failed,
            Error = job.Error,
        };
        lock (Gate)
        {
            var list = Load();
            list.Insert(0, entry);
            if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
            Save(list);
        }
        Changed?.Invoke();
    }

    public static void Remove(HistoryEntry entry)
    {
        lock (Gate)
        {
            var list = Load();
            list.Remove(entry);
            Save(list);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// A folder was renamed: entries pointing inside it now point inside the new one, where the
    /// file is actually there. Safe to call again; returns how many paths changed.
    /// </summary>
    public static int Repoint(string oldFolder, string newFolder)
    {
        var from = oldFolder.TrimEnd('\\') + "\\";
        var to = newFolder.TrimEnd('\\') + "\\";
        int changed = 0;
        string Fix(string path)
        {
            if (!path.StartsWith(from, StringComparison.OrdinalIgnoreCase)) return path;
            var moved = to + path[from.Length..];
            if (!File.Exists(moved) && !Directory.Exists(moved)) return path;
            changed++;
            return moved;
        }
        lock (Gate)
        {
            var list = Load();
            foreach (var entry in list)
            {
                entry.Inputs = entry.Inputs.Select(Fix).ToList();
                entry.Outputs = entry.Outputs.Select(Fix).ToList();
            }
            if (changed > 0) Save(list);
        }
        if (changed > 0) Changed?.Invoke();
        return changed;
    }

    /// <summary>Days in a row (ending today, or yesterday if today has nothing yet) with at least one job.</summary>
    public static int Streak()
    {
        var days = Entries.Where(e => !e.Failed).Select(e => e.Time.Date).ToHashSet();
        var day = DateTime.Today;
        if (!days.Contains(day)) day = day.AddDays(-1);
        int streak = 0;
        while (days.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    static List<HistoryEntry> Load()
    {
        if (_entries != null) return _entries;
        try
        {
            _entries = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the history, starting a new one", ex);
            _entries = [];
        }
        return _entries;
    }

    static void Save(List<HistoryEntry> list)
    {
        try
        {
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the history", ex);
        }
    }
}
