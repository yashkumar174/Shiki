using System;
using System.Collections.Generic;
using System.Linq;

namespace Shiki.Core;

/// <summary>Something a level unlocks: a cat (skin id) or something to wear (accessory id).</summary>
public sealed record Unlock(int Level, bool IsCat, string Id, string Name);

/// <summary>
/// The cat grows with use: points for files handled, requests, meetings, tasks finished and
/// days in a row, and every level or so something new to wear or a new cat to be.
/// </summary>
public static class CatLevel
{
    static readonly int[] Steps = [0, 20, 60, 120, 200, 320, 480, 700, 1000, 1400];

    public static readonly IReadOnlyList<Unlock> Unlocks =
    [
        new(2, true, "kuro", "Kuro"),
        new(3, false, "bow", "Bow tie"),
        new(4, true, "ginger", "Ginger"),
        new(5, false, "headphones", "Headphones"),
        new(6, true, "ash", "Ash"),
        new(7, false, "party", "Party hat"),
        new(10, false, "crown", "Crown"),
    ];

    /// <summary>The cats everyone has from the start.</summary>
    static readonly string[] Starters = ["mochi", "latte", "sky"];

    public static int Points(Settings s, int meetings, int tasksDone, int streak) =>
        s.FilesDone + s.AiRequests + meetings * 15 + tasksDone * 3 + streak * 5;

    public static int LevelFor(int points)
    {
        int level = 1;
        while (points >= At(level + 1)) level++;
        return level;
    }

    /// <summary>Points needed to reach a level: the table, then 500 more for each level after 10.</summary>
    public static int At(int level) => level <= Steps.Length ? Steps[level - 1] : Steps[^1] + (level - Steps.Length) * 500;

    public static bool CatUnlocked(string id, int level) =>
        Starters.Contains(id) || Unlocks.Any(u => u.IsCat && u.Id == id && u.Level <= level);

    public static bool AccessoryUnlocked(string id, int level) => Unlocks.Any(u => !u.IsCat && u.Id == id && u.Level <= level);

    /// <summary>What reaching this level brings (empty for most).</summary>
    public static IEnumerable<Unlock> NewAt(int level) => Unlocks.Where(u => u.Level == level);

    public static Unlock? Next(int level) => Unlocks.FirstOrDefault(u => u.Level > level);
}
