using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shiki.Core;

/// <summary>
/// Habits picked up from the person's corrections ("no, on Chrome", "just the photos, no text",
/// "the logo photos are the ones in Designs"). Kept as plain sentences among the remembered
/// preferences, so they show (and can be forgotten) with the rest; these few are also read back
/// by the app itself, so the quick path follows them too.
/// </summary>
public static class Learned
{
    public const string WhatsAppWebFact = "WhatsApp: use WhatsApp Web in Chrome, not the app.";
    public const string WhatsAppAppFact = "WhatsApp: use the WhatsApp app, not Chrome.";
    public const string NoTextWithFilesFact = "Emails with only files: no message text, just the files (and a short subject).";
    public const string TextWithFilesFact = "Emails with only files: add a short friendly line, not an empty email.";

    /// <summary>WhatsApp goes through WhatsApp Web unless they've said otherwise.</summary>
    public static bool WhatsAppWeb => Preferences.All.Contains(WhatsAppWebFact);

    /// <summary>Files go by email with no words (the quick path's own way, unless they asked for a line).</summary>
    public static bool TextWithFiles => Preferences.All.Contains(TextWithFilesFact);

    /// <summary>
    /// Saves one habit, replacing its opposite; returns what was learned in plain words, or null
    /// when the habit isn't one of these.
    /// </summary>
    public static string? Set(string habit, string value, IReadOnlyList<string>? paths = null)
    {
        switch (habit, value.Trim().ToLowerInvariant())
        {
            case ("whatsapp_via", "web" or "chrome"):
                Swap(WhatsAppAppFact, WhatsAppWebFact);
                return "WhatsApp goes through WhatsApp Web in Chrome from now on";
            case ("whatsapp_via", "app"):
                Swap(WhatsAppWebFact, WhatsAppAppFact);
                return "WhatsApp goes through the WhatsApp app from now on";
            case ("email_files_text", "none"):
                Swap(TextWithFilesFact, NoTextWithFilesFact);
                return "Files go by email with no message text";
            case ("email_files_text", "short"):
                Swap(NoTextWithFilesFact, TextWithFilesFact);
                return "Emails with files get a short friendly line";
            case ("means", _):
            {
                var phrase = Phrase(value);
                var found = (paths ?? []).Where(p => File.Exists(p) || Directory.Exists(p)).Take(12).ToList();
                if (phrase.Length < 3 || found.Count == 0) return null;
                foreach (var old in Preferences.All.Where(f => f.StartsWith($"\"{phrase}\" means", StringComparison.OrdinalIgnoreCase)).ToList()) Preferences.Remove(old);
                Preferences.Add($"\"{phrase}\" means: {string.Join(" | ", found)}");
                return $"\"{phrase}\" means {(found.Count == 1 ? Path.GetFileName(found[0].TrimEnd('\\')) : $"those {found.Count} files")}";
            }
        }
        return null;
    }

    static void Swap(string remove, string add)
    {
        Preferences.Remove(remove);
        Preferences.Add(add);
    }

    static string Phrase(string text) => Regex.Replace(text.Trim().Trim('"', '\'').ToLowerInvariant(), @"\s+", " ");

    /// <summary>What a learned phrase ("the logo photos") stands for: files and folders that still exist, or null.</summary>
    public static List<string>? Means(string what)
    {
        var phrase = Phrase(what);
        if (phrase.Length < 3) return null;
        foreach (var fact in Preferences.All)
        {
            var m = Regex.Match(fact, "^\"(?<p>[^\"]+)\" means: (?<paths>.+)$");
            if (!m.Success) continue;
            // "The logo photos", "my logo photos", "logo photos": the same thing.
            static string Bare(string s) => Regex.Replace(s, @"^(?:the|my|our|those|these) ", "");
            if (Bare(m.Groups["p"].Value) != Bare(phrase)) continue;
            var paths = m.Groups["paths"].Value.Split(" | ").Select(p => p.Trim()).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            return paths.Count > 0 ? paths : null;
        }
        return null;
    }
}
