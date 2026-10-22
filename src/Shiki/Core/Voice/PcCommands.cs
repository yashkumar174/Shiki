using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Shiki.Shell;

namespace Shiki.Core.Voice;

/// <summary>
/// The PC controls said in so many words ("mute", "volume 40", "dark mode on"): done straight
/// away, without asking Claude, so they're instant and free. Anything less plain goes to Claude,
/// which has the same controls as tools.
/// </summary>
public static class PcCommands
{
    static readonly Dictionary<string, int> NumberWords = new()
    {
        ["zero"] = 0, ["ten"] = 10, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
        ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90, ["hundred"] = 100,
        ["a hundred"] = 100, ["one hundred"] = 100, ["full"] = 100, ["max"] = 100, ["maximum"] = 100, ["half"] = 50,
    };

    const string Number = @"(?<n>\d{1,3}|zero|ten|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|a hundred|one hundred|hundred|full|max|maximum|half)";

    /// <summary>What to do for what was said, or null when it isn't one of these.</summary>
    /// <param name="words">Lower case, no punctuation (WakeMatcher.Normalize).</param>
    public static Func<string>? Match(string words)
    {
        words = Regex.Replace(words, @"^(please |can you |could you |just )+|( please| for me| now)+$", "");
        words = Regex.Replace(words, @"\b(the|my|(computer|pc|laptop|screen|system)( s)?)\b ?", "").Trim();
        words = Regex.Replace(words, @"\s+", " ").Replace(" percent", "").Replace(" per cent", "");

        // Sound
        if (words is "mute" or "mute it" or "mute sound" or "mute volume" or "mute audio" or "sound off" or "mute speakers" or "turn off sound" or "turn sound off")
            return () => PcControl.Mute(true);
        if (words is "unmute" or "unmute it" or "unmute sound" or "unmute volume" or "unmute audio" or "sound on" or "turn on sound" or "turn sound on")
            return () => PcControl.Mute(false);
        if (Regex.IsMatch(words, @"^((turn |turn it |turn volume |turn sound )?up|volume up|louder|increase volume|raise volume|turn up volume|turn volume up|a bit louder|bit louder|make it louder)$"))
            return () => PcControl.ChangeVolume(10);
        if (Regex.IsMatch(words, @"^((turn |turn it |turn volume |turn sound )?down|volume down|quieter|softer|decrease volume|lower volume|turn down volume|turn volume down|a bit quieter|bit quieter|make it quieter)$"))
            return () => PcControl.ChangeVolume(-10);
        if (Regex.Match(words, $@"^(set |change |turn |put )?(volume|sound)( to| at)? {Number}$") is { Success: true } v && Read(v) is { } volume)
            return () => PcControl.SetVolume(volume);

        // Brightness
        if (Regex.IsMatch(words, @"^(brightness up|brighter|increase brightness|turn up brightness|turn brightness up|make it brighter|more brightness)$"))
            return () => PcControl.ChangeBrightness(15);
        if (Regex.IsMatch(words, @"^(brightness down|dimmer|dim|decrease brightness|lower brightness|turn down brightness|turn brightness down|make it dimmer|less brightness|dim it)$"))
            return () => PcControl.ChangeBrightness(-15);
        if (Regex.Match(words, $@"^(set |change |turn |put )?brightness( to| at)? {Number}$") is { Success: true } b && Read(b) is { } brightness)
            return () => PcControl.SetBrightness(brightness);

        // Dark mode
        if (Regex.IsMatch(words, @"^((turn on |switch to |go |enable )?dark mode( on)?|(turn |switch )?off light mode|lights off)$"))
            return () => PcControl.SetDarkMode(true);
        if (Regex.IsMatch(words, @"^((turn on |switch to |go |enable )?light mode( on)?|(turn |switch )?off dark mode|dark mode off|disable dark mode)$"))
            return () => PcControl.SetDarkMode(false);

        // Do Not Disturb
        if (Regex.IsMatch(words, @"^((turn on |switch on |enable )?(do not disturb|dont disturb|focus mode|focus assist)( on)?|silence notifications|mute notifications|no notifications)$"))
            return () => PcControl.SetDoNotDisturb(true);
        if (Regex.IsMatch(words, @"^((turn |switch )off (do not disturb|dont disturb|focus mode|focus assist)|(do not disturb|dont disturb|focus mode|focus assist) off|disable (do not disturb|focus mode)|unmute notifications|notifications on|turn on notifications)$"))
            return () => PcControl.SetDoNotDisturb(false);
        return null;
    }

    /// <summary>Does it and says what happened ("Volume 40%"), or null when it isn't one of these.</summary>
    public static string? Try(string words)
    {
        if (Match(words) is not { } run) return null;
        try
        {
            return run();
        }
        catch (Exception ex)
        {
            Log.Error($"PC command \"{words}\" failed", ex);
            return "That didn't work on this PC.";
        }
    }

    static int? Read(Match m)
    {
        var n = m.Groups["n"].Value;
        if (NumberWords.TryGetValue(n, out var word)) return word;
        return int.TryParse(n, out var number) && number <= 100 ? number : null;
    }
}
