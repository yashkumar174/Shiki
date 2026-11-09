using System;
using System.Collections.Generic;
using System.Linq;

namespace Shiki.Core;

/// <summary>Something a level unlocks: a cat (skin id), something to wear (accessory id), or a title.</summary>
public sealed record Unlock(int Level, bool IsCat, string Id, string Name);

/// <summary>What the person has done with Shiki, counted for the level tasks.</summary>
public sealed record CatStats(int Files, int Requests, int Meetings, int TasksDone, int Streak, int Routines, int Remembered,
    bool PhoneConnected, bool NameTaught);

/// <summary>One task on the way to a level: "Convert 25 files", with how far along it is.</summary>
public sealed record Quest(string Text, Func<CatStats, int> Have, int Need)
{
    public int Progress(CatStats s) => Math.Clamp(Have(s), 0, Need);
    public bool Done(CatStats s) => Have(s) >= Need;
}

/// <summary>A level: its name, and the tasks that reach it.</summary>
public sealed record LevelGoal(int Level, string Title, IReadOnlyList<Quest> Quests);

/// <summary>
/// The cat grows with use, level by level: each level is a few plain tasks (convert files, ask
/// the cat things, finish tasks, keep a streak…), and most bring a new cat or something to wear.
/// A level once reached stays reached.
/// </summary>
public static class CatLevel
{
    public const int Max = 12;

    static Quest Files(int n) => new($"Convert {n:N0} files", s => s.Files, n);
    static Quest Ask(int n) => new($"Ask the cat {n:N0} things", s => s.Requests, n);
    static Quest Tasks(int n) => new($"Finish {n:N0} tasks", s => s.TasksDone, n);
    static Quest Streak(int n) => new($"Convert something {n} days in a row", s => s.Streak, n);
    static Quest Meetings(int n) => new(n == 1 ? "Take notes in a meeting" : $"Take notes in {n} meetings", s => s.Meetings, n);
    static Quest Remember(int n) => new($"Teach the cat {n} things to remember", s => s.Remembered, n);

    public static readonly IReadOnlyList<LevelGoal> Goals =
    [
        new(1, "New friends", []),
        new(2, "First steps", [Files(5), Ask(5)]),
        new(3, "Getting comfy", [Tasks(3), Ask(25)]),
        new(4, "Regulars", [Files(15), Streak(2)]),
        new(5, "Good listener", [Meetings(1), Ask(75)]),
        new(6, "Best friends", [Remember(3), Tasks(8)]),
        new(7, "Party time", [Files(30), new("Make a routine", s => s.Routines, 1), Ask(150)]),
        new(8, "Sharp dresser", [Tasks(15), Streak(3), new("Teach the cat its name", s => s.NameTaught ? 1 : 0, 1)]),
        new(9, "Homebody", [Files(60), Meetings(3), Ask(300)]),
        new(10, "Royalty", [new("Connect your phone", s => s.PhoneConnected ? 1 : 0, 1), Streak(5), Ask(500)]),
        new(11, "Sunbeam", [Files(120), Tasks(30), Remember(10)]),
        new(12, "Shiki legend", [Ask(1000), Streak(7), Meetings(10)]),
    ];

    public static readonly IReadOnlyList<Unlock> Unlocks =
    [
        new(2, true, "kuro", "Kuro"),
        new(3, false, "bow", "Bow tie"),
        new(4, true, "ginger", "Ginger"),
        new(5, false, "headphones", "Headphones"),
        new(6, true, "ash", "Ash"),
        new(7, false, "party", "Party hat"),
        new(8, true, "sumi", "Sumi"),
        new(9, true, "maple", "Maple"),
        new(10, false, "crown", "Crown"),
        new(11, true, "biscuit", "Biscuit"),
    ];

    /// <summary>The cats everyone has from the start.</summary>
    public static readonly string[] Starters = ["mochi", "latte", "sky"];

    public static LevelGoal Goal(int level) => Goals[Math.Clamp(level, 1, Max) - 1];

    /// <summary>
    /// The level now: the one reached before, then each next level whose tasks are all done.
    /// </summary>
    public static int LevelNow(CatStats stats, int reached)
    {
        int level = Math.Clamp(reached, 1, Max);
        while (level < Max && Goal(level + 1).Quests.All(q => q.Done(stats))) level++;
        return level;
    }

    /// <summary>How far through the next level's tasks (0 to 1), counting part-done tasks.</summary>
    public static double TowardNext(CatStats stats, int level)
    {
        if (level >= Max) return 1;
        var quests = Goal(level + 1).Quests;
        return quests.Count == 0 ? 1 : quests.Average(q => q.Progress(stats) / (double)q.Need);
    }

    // ---- The old points, only to keep the level someone had before levels became tasks ----

    static readonly int[] Steps = [0, 20, 60, 120, 200, 320, 480, 700, 1000, 1400];

    public static int Points(Settings s, int meetings, int tasksDone, int streak) =>
        s.FilesDone + s.AiRequests + meetings * 15 + tasksDone * 3 + streak * 5;

    public static int LevelFor(int points)
    {
        int level = 1;
        while (level < Steps.Length && points >= Steps[level]) level++;
        return level;
    }

    public static bool CatUnlocked(string id, int level) =>
        Starters.Contains(id) || Unlocks.Any(u => u.IsCat && u.Id == id && u.Level <= level);

    public static bool AccessoryUnlocked(string id, int level) => Unlocks.Any(u => !u.IsCat && u.Id == id && u.Level <= level);

    /// <summary>The level that brings this cat or thing to wear (1 for a starter).</summary>
    public static int UnlockLevel(string id) => Unlocks.FirstOrDefault(u => u.Id == id)?.Level ?? 1;

    /// <summary>What reaching this level brings (empty for some).</summary>
    public static IEnumerable<Unlock> NewAt(int level) => Unlocks.Where(u => u.Level == level);

    public static Unlock? Next(int level) => Unlocks.FirstOrDefault(u => u.Level > level);
}
