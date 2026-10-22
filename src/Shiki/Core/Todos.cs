using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Shiki.Core;

/// <summary>Something to do: from a meeting, or added by the person (or by asking the cat).</summary>
public sealed class Todo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Text { get; set; } = "";
    public DateTime? Due { get; set; }
    /// <summary>The due date has a time of day ("at 5 pm"), not just a day.</summary>
    public bool HasTime { get; set; }
    /// <summary>Where it came from, e.g. the meeting's title.</summary>
    public string? From { get; set; }
    public string? MeetingId { get; set; }
    /// <summary>Who asked for it, or who it's for, when known.</summary>
    public string? Who { get; set; }
    public bool Done { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime? Reminded { get; set; }
    public DateTime? SnoozedUntil { get; set; }

    /// <summary>When to remind: the time if there is one, otherwise 9 in the morning of the day.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime? RemindAt => SnoozedUntil ?? (Due is { } due ? HasTime ? due : due.Date.AddHours(9) : null);

    public string DueText()
    {
        if (Due is not { } due) return "";
        var day = due.Date == DateTime.Today ? "today" : due.Date == DateTime.Today.AddDays(1) ? "tomorrow"
            : due.Date < DateTime.Today ? due.ToString("d MMM") : due.Date < DateTime.Today.AddDays(7) ? due.ToString("dddd") : due.ToString("d MMM");
        return HasTime ? $"{day}, {due:h:mm tt}".Replace("AM", "am").Replace("PM", "pm") : day;
    }
}

/// <summary>The person's tasks, kept in tasks.json in the data folder.</summary>
public static class Todos
{
    static readonly object Gate = new();
    static List<Todo>? _list;
    static string FilePath => Path.Combine(Log.DataDir, "tasks.json");

    public static event Action? Changed;

    public static IReadOnlyList<Todo> All
    {
        get { lock (Gate) return [.. Load()]; }
    }

    public static Todo Add(string text, DateTime? due = null, bool hasTime = false, string? from = null, string? meetingId = null, string? who = null)
    {
        var todo = new Todo { Text = text.Trim(), Due = due, HasTime = hasTime, From = from, MeetingId = meetingId, Who = who };
        lock (Gate)
        {
            Load().Add(todo);
            Save();
        }
        Changed?.Invoke();
        return todo;
    }

    /// <summary>Saves a change made to a task (done, snoozed, edited).</summary>
    public static void Update(Todo todo)
    {
        lock (Gate)
        {
            var list = Load();
            int i = list.FindIndex(t => t.Id == todo.Id);
            if (i >= 0) list[i] = todo;
            Save();
        }
        Changed?.Invoke();
    }

    public static void Remove(Todo todo)
    {
        lock (Gate)
        {
            Load().RemoveAll(t => t.Id == todo.Id);
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Tasks whose reminder time has come and that haven't been reminded about since.</summary>
    public static List<Todo> DueNow()
    {
        var now = DateTime.Now;
        lock (Gate)
            return Load().Where(t => !t.Done && t.RemindAt is { } at && at <= now && (t.Reminded == null || t.Reminded < at)).ToList();
    }

    static List<Todo> Load()
    {
        if (_list != null) return _list;
        try
        {
            _list = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Todo>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception ex)
        {
            Log.Error("Could not read tasks", ex);
            _list = [];
        }
        return _list;
    }

    static void Save()
    {
        try
        {
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(_list));
            File.Move(FilePath + ".tmp", FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save tasks", ex);
        }
    }
}
