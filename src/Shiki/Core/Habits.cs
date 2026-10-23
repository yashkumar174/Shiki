using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Shiki.Core;

/// <summary>Things the person told the cat to remember ("my PDFs should be under 2 MB"), kept in preferences.json.</summary>
public static class Preferences
{
    static string FilePath => Path.Combine(Log.DataDir, "preferences.json");
    static List<string>? _all;

    public static IReadOnlyList<string> All => _all ??= Load();

    static List<string> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the preferences", ex);
            return [];
        }
    }

    static void Write(List<string> list)
    {
        _all = list;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the preferences", ex);
        }
    }

    public static void Add(string fact)
    {
        fact = fact.Trim();
        if (fact.Length == 0 || All.Contains(fact, StringComparer.OrdinalIgnoreCase)) return;
        // A short list stays useful (and cheap): the oldest go first.
        Write(All.Append(fact).TakeLast(40).ToList());
    }

    public static bool Remove(string fact)
    {
        var list = All.ToList();
        if (list.RemoveAll(f => f.Equals(fact.Trim(), StringComparison.OrdinalIgnoreCase)) == 0) return false;
        Write(list);
        return true;
    }
}

/// <summary>A conversion done by itself: every new download of this kind gets this action.</summary>
public sealed class AutoRule
{
    public string Extension { get; set; } = "";
    public string ActionId { get; set; } = "";
    public DateTime Since { get; set; }
}

/// <summary>
/// Habits: when the same conversion keeps being done to the same kind of download ("HEIC to JPG"
/// three times in two weeks), the cat offers to do it automatically from then on. Rules and
/// "no thanks" answers are kept in habits.json.
/// </summary>
public static class Habits
{
    sealed class Store
    {
        public List<AutoRule> Rules { get; set; } = [];
        public List<string> Declined { get; set; } = [];
    }

    static string FilePath => Path.Combine(Log.DataDir, "habits.json");
    static Store? _store;

    static Store Data
    {
        get
        {
            if (_store != null) return _store;
            try
            {
                _store = File.Exists(FilePath) ? JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) : null;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the habits", ex);
            }
            return _store ??= new Store();
        }
    }

    static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the habits", ex);
        }
    }

    public static IReadOnlyList<AutoRule> Rules => Data.Rules;

    static string Key(string ext, string actionId) => $"{ext.ToLowerInvariant()}|{actionId}";

    /// <summary>
    /// Actions that fit doing by themselves: one file in, results out, no window.
    /// Only format changes: a result of another kind can't set the rule off again.
    /// </summary>
    static bool Suits(BroAction action) =>
        action.Group == ActionGroup.Format && !action.Combine && action.Editor == null && !action.AnyFile && action.MinFiles <= 1;

    /// <summary>A habit worth offering, if there is one: the same action on downloads of one kind, 3+ times in 14 days.</summary>
    public static (string Extension, BroAction Action, int Times)? Spot() =>
        Ai.FileFinder.PlacePath("downloads", null) is { } downloads ? Spot(History.Entries, downloads) : null;

    public static (string Extension, BroAction Action, int Times)? Spot(IEnumerable<HistoryEntry> entries, string downloads)
    {
        var since = DateTime.Now.AddDays(-14);
        var groups = entries
            .Where(e => !e.Failed && e.Time > since)
            .SelectMany(e => e.Inputs.Select(i => (Input: i, e.ActionId, e.Time)))
            .Where(x => string.Equals(Path.GetDirectoryName(x.Input), downloads, StringComparison.OrdinalIgnoreCase))
            // One job on many files is one decision, not many.
            .GroupBy(x => (Ext: Path.GetExtension(x.Input).ToLowerInvariant(), x.ActionId))
            .Select(g => (g.Key.Ext, g.Key.ActionId, Times: g.Select(x => x.Time).Distinct().Count()))
            .Where(g => g.Times >= 3 && g.Ext.Length > 0)
            .OrderByDescending(g => g.Times);
        foreach (var (ext, actionId, times) in groups)
        {
            if (Data.Declined.Contains(Key(ext, actionId)) || Data.Rules.Any(r => r.Extension == ext)) continue;
            if (ActionCatalog.Find(actionId) is { } action && Suits(action) && action.AcceptsExtension(ext)) return (ext, action, times);
        }
        return null;
    }

    public static void Accept(string ext, BroAction action)
    {
        Data.Rules.RemoveAll(r => r.Extension == ext);
        Data.Rules.Add(new AutoRule { Extension = ext.ToLowerInvariant(), ActionId = action.Id, Since = DateTime.Now });
        Save();
        Ai.AiLog.Write("habit on", $"{ext} -> {action.Id}");
    }

    public static void Decline(string ext, BroAction action)
    {
        Data.Declined.Add(Key(ext, action.Id));
        Save();
    }

    public static void Stop(string ext)
    {
        Data.Rules.RemoveAll(r => r.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase));
        // Don't offer the same again.
        Save();
        Ai.AiLog.Write("habit off", ext);
    }

    /// <summary>The action to run on a new download, if a rule covers its kind.</summary>
    public static BroAction? For(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        return Data.Rules.FirstOrDefault(r => r.Extension == ext) is { } rule && ActionCatalog.Find(rule.ActionId) is { } action && action.Accepts(file) ? action : null;
    }

    /// <summary>"New .heic downloads: to JPG".</summary>
    public static string Describe(AutoRule rule) =>
        $"New {rule.Extension} downloads: {(ActionCatalog.Find(rule.ActionId)?.Label.TrimEnd('…') ?? rule.ActionId)}";
}
