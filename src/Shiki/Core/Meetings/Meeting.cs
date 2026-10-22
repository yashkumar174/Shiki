using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Shiki.Core.Meetings;

/// <summary>One thing said in a meeting: when (seconds from the start), who, and what.</summary>
public sealed class MeetingLine
{
    public double At { get; set; }
    /// <summary>"You", or "Speaker 1", "Speaker 2"… (renamed by the person later).</summary>
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>"Hey Sky, mark that": a moment the person wanted to remember.</summary>
    public bool Marked { get; set; }
}

/// <summary>Something someone has to do, found in a meeting.</summary>
public sealed class MeetingTask
{
    public string Text { get; set; } = "";
    /// <summary>"me" when it's the person's own, otherwise who.</summary>
    public string Owner { get; set; } = "me";
    public DateTime? Due { get; set; }
    /// <summary>What was said that made it a task.</summary>
    public string? Quote { get; set; }
}

public sealed class Meeting
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "Meeting";
    public DateTime Started { get; set; } = DateTime.Now;
    public double Seconds { get; set; }
    /// <summary>Which app the call was in, when known (Zoom, Teams, a browser…).</summary>
    public string? App { get; set; }
    public List<MeetingLine> Lines { get; set; } = [];
    /// <summary>Names the person gave the speakers: "Speaker 2" → "Rahul".</summary>
    public Dictionary<string, string> Names { get; set; } = [];

    public string? Summary { get; set; }
    public List<string> Decisions { get; set; } = [];
    public List<string> Questions { get; set; } = [];
    public List<MeetingTask> Tasks { get; set; } = [];
    public string? EmailSubject { get; set; }
    public string? EmailBody { get; set; }
    /// <summary>The recording, when the person keeps recordings.</summary>
    public string? Audio { get; set; }
    /// <summary>Why notes couldn't be written (no key, offline), if they couldn't.</summary>
    public string? NotesProblem { get; set; }

    /// <summary>
    /// Adds a line, joining it to the one before when it's the same person finishing a sentence
    /// (speech recognition sometimes cuts mid-sentence at a pause).
    /// </summary>
    public void Add(MeetingLine line)
    {
        var last = Lines.LastOrDefault(l => l.Speaker == line.Speaker);
        if (last != null && Lines[^1] == last && line.At - last.At < 12 && last.Text.Length > 0 && !".?!".Contains(last.Text[^1]))
        {
            last.Text = $"{last.Text} {line.Text}";
            last.Marked |= line.Marked;
            return;
        }
        Lines.Add(line);
        if (Lines.Count > 1 && Lines[^2].At > line.At) Lines.Sort((a, b) => a.At.CompareTo(b.At));
    }

    public string Who(string speaker) => Names.TryGetValue(speaker, out var name) && name.Length > 0 ? name : speaker;

    /// <summary>The transcript as plain text, "[12:04] Rahul: …", for Claude and for copying.</summary>
    public string Transcript()
    {
        var text = new StringBuilder();
        foreach (var line in Lines)
            text.Append('[').Append(Clock(line.At)).Append("] ").Append(Who(line.Speaker)).Append(line.Marked ? " (marked)" : "").Append(": ").AppendLine(line.Text);
        return text.ToString();
    }

    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    public string Length => Seconds < 60 ? "under a minute" : Seconds < 3600 ? $"{Seconds / 60:0} min" : $"{(int)(Seconds / 3600)} h {(int)(Seconds % 3600 / 60)} min";
}

/// <summary>Meetings are kept as one JSON file each in the data folder.</summary>
public static class MeetingStore
{
    static readonly object Gate = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder => Directory.CreateDirectory(Path.Combine(Log.DataDir, "meetings")).FullName;

    public static event Action? Changed;

    public static IReadOnlyList<Meeting> All()
    {
        lock (Gate)
        {
            var list = new List<Meeting>();
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<Meeting>(File.ReadAllText(file)) is { } meeting) list.Add(meeting);
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not read meeting {file}", ex);
                }
            }
            return list.OrderByDescending(m => m.Started).ToList();
        }
    }

    public static Meeting? Load(string id) => All().FirstOrDefault(m => m.Id == id);

    public static void Save(Meeting meeting)
    {
        lock (Gate)
        {
            var file = Path.Combine(Folder, meeting.Id + ".json");
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(meeting, Json));
            File.Move(file + ".tmp", file, overwrite: true);
        }
        Changed?.Invoke();
    }

    /// <summary>Forgets a meeting's notes (the person asked; a kept recording stays where it is).</summary>
    public static void Remove(Meeting meeting)
    {
        lock (Gate) File.Delete(Path.Combine(Folder, meeting.Id + ".json"));
        Changed?.Invoke();
    }
}
