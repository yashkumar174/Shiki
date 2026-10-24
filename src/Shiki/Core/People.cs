using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core;

/// <summary>
/// The people the person talks about, so their names come out right: names from their Google
/// contacts and from what the cat remembers go to the speech engine as words to expect; a name it
/// still mishears is matched by how it sounds ("Tosef" is Tausif); and a correction ("it's Tausif,
/// not torsive") is kept, so the same mishearing fixes itself. Also knows who they've already
/// messaged or emailed (a send to them gets a countdown instead of a card).
/// </summary>
public static class People
{
    static string ContactsFile => Path.Combine(Log.DataDir, "contact-names.json");
    static string CorrectionsFile => Path.Combine(Log.DataDir, "corrections.json");

    sealed record ContactNames(DateTime Fetched, List<string> Names);

    static ContactNames? _contacts;
    static Dictionary<string, string>? _corrections;

    // ---- Names ----

    /// <summary>Contact names, as last fetched (empty until Google is connected).</summary>
    static ContactNames Contacts
    {
        get
        {
            if (_contacts != null) return _contacts;
            try
            {
                _contacts = File.Exists(ContactsFile) ? JsonSerializer.Deserialize<ContactNames>(File.ReadAllText(ContactsFile)) : null;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the contact names", ex);
            }
            return _contacts ??= new ContactNames(DateTime.MinValue, []);
        }
    }

    /// <summary>People the cat remembers ("Tausif's email is …" gives Tausif).</summary>
    static IEnumerable<string> Remembered() =>
        Preferences.All.Select(f => Regex.Match(f, @"^(?:My )?(.+?)'s (?:email|e-mail|WhatsApp|number|phone|mobile)\b", RegexOptions.IgnoreCase))
            .Where(m => m.Success).Select(m => m.Groups[1].Value.Trim()).Where(n => n.Length > 1 && !n.Equals("own", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Names for the speech engine to expect, most useful first: people remembered and corrected,
    /// then contacts' first names, then their full names.
    /// </summary>
    public static List<string> ForSpeech(int most)
    {
        var names = new List<string>();
        names.AddRange(Remembered());
        names.AddRange(Corrections.Values);
        names.AddRange(Contacts.Names.Select(n => n.Split(' ')[0]));
        names.AddRange(Contacts.Names.Where(n => n.Contains(' ')));
        return names.Where(n => n.Length > 2 && n.Any(char.IsLetter) && !n.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(most).ToList();
    }

    /// <summary>Fetches the contact names again if they're more than a day old (quietly, in the background).</summary>
    public static async Task RefreshContacts(bool force = false)
    {
        if (!Mail.Gmail.HasContacts || !force && DateTime.Now - Contacts.Fetched < TimeSpan.FromDays(1)) return;
        try
        {
            var names = new List<string>();
            foreach (var url in new[]
            {
                "https://people.googleapis.com/v1/people/me/connections?personFields=names&pageSize=1000",
                "https://people.googleapis.com/v1/otherContacts?readMask=names&pageSize=1000",
            })
            {
                using var json = await Mail.Gmail.Google(HttpMethod.Get, url, null, CancellationToken.None);
                var list = json.RootElement.TryGetProperty("connections", out var c) ? c : json.RootElement.TryGetProperty("otherContacts", out var o) ? o : default;
                if (list.ValueKind != JsonValueKind.Array) continue;
                foreach (var person in list.EnumerateArray())
                    if (person.TryGetProperty("names", out var n) && n.ValueKind == JsonValueKind.Array)
                        foreach (var name in n.EnumerateArray())
                            if (name.TryGetProperty("displayName", out var d) && d.GetString() is { Length: > 1 } display)
                                names.Add(display.Trim());
            }
            _contacts = new ContactNames(DateTime.Now, names.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            File.WriteAllText(ContactsFile, JsonSerializer.Serialize(_contacts));
            Log.Info($"Contact names: {_contacts.Names.Count}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            Log.Error("Could not fetch the contact names", ex);
        }
    }

    // ---- Sounds like ----

    /// <summary>
    /// How a name sounds, roughly: the spellings that sound alike made the same ("Tausif",
    /// "Tosif", "Tousif" all come to "tosif"; "ph" is "f", "w" is "v", doubled letters once).
    /// </summary>
    public static string Sound(string name)
    {
        var s = Regex.Replace(name.ToLowerInvariant(), "[^a-z]", "");
        foreach (var (from, to) in new[]
        {
            ("ph", "f"), ("bh", "b"), ("dh", "d"), ("th", "t"), ("kh", "k"), ("gh", "g"), ("sh", "s"), ("ch", "c"), ("ck", "k"), ("q", "k"),
            ("x", "ks"), ("z", "s"), ("w", "v"), ("v", "f"), ("au", "o"), ("ou", "o"), ("aw", "o"), ("ow", "o"), ("ee", "i"), ("ea", "i"),
            ("oo", "u"), ("ai", "e"), ("ay", "e"), ("ei", "e"), ("y", "i"), ("or", "o"), ("ar", "a"),
        })
            s = s.Replace(from, to);
        s = Regex.Replace(s, @"(.)\1+", "$1");
        if (s.Length > 3 && s.EndsWith('e')) s = s[..^1];
        return s;
    }

    static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int last = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int was = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), last + (a[i - 1] == b[j - 1] ? 0 : 1));
                last = was;
            }
        }
        return row[b.Length];
    }

    /// <summary>
    /// The known names that sound like a misheard one, closest first: by the whole name, or its
    /// first word ("Tosef" finds "Tausif Ansari").
    /// </summary>
    public static List<string> SoundsLike(string heard, int most = 3)
    {
        var want = Sound(heard);
        if (want.Length < 3) return [];
        var all = Remembered().Concat(Corrections.Values).Concat(Contacts.Names).Distinct(StringComparer.OrdinalIgnoreCase);
        return all
            .Select(name =>
            {
                var options = new[] { name, name.Split(' ')[0] }.Select(Sound).Where(s => s.Length >= 2).ToList();
                double best = options.Count == 0 ? 1 : options.Min(o => Distance(want, o) / (double)Math.Max(want.Length, o.Length));
                return (Name: name, Score: best);
            })
            .Where(x => x.Score <= 0.34)
            .OrderBy(x => x.Score).ThenBy(x => x.Name.Length)
            .Take(most).Select(x => x.Name).ToList();
    }

    // ---- Corrections ----

    /// <summary>What was misheard and what was meant ("torsive" → "Tausif").</summary>
    public static IReadOnlyDictionary<string, string> Corrections
    {
        get
        {
            if (_corrections != null) return _corrections;
            try
            {
                _corrections = File.Exists(CorrectionsFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CorrectionsFile)) : null;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the corrections", ex);
            }
            return _corrections ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Learn(string heard, string meant)
    {
        heard = heard.Trim().Trim('"', '.', ',');
        meant = meant.Trim().Trim('"', '.', ',');
        if (heard.Length < 2 || meant.Length < 2 || heard.Equals(meant, StringComparison.OrdinalIgnoreCase)) return;
        _ = Corrections;
        _corrections = new Dictionary<string, string>(_corrections!, StringComparer.OrdinalIgnoreCase) { [heard] = meant };
        try
        {
            File.WriteAllText(CorrectionsFile, JsonSerializer.Serialize(_corrections));
        }
        catch (IOException ex)
        {
            Log.Error("Could not keep the correction", ex);
        }
        AiLog.Write("learned", $"\"{heard}\" means \"{meant}\"");
    }

    /// <summary>What was heard, with the known mishearings put right (whole words only).</summary>
    public static string Fix(string text)
    {
        foreach (var (heard, meant) in Corrections)
            text = Regex.Replace(text, $@"(?<![\w']){Regex.Escape(heard)}(?![\w'])", meant, RegexOptions.IgnoreCase);
        return text;
    }

    // ---- Who they've written to ----

    /// <summary>
    /// Have they messaged or emailed this address or number before (it's remembered, or a send to
    /// it went through)? Then a new send gets a countdown rather than a card.
    /// </summary>
    public static bool Known(string addressOrPhone)
    {
        var target = addressOrPhone.Trim();
        if (target.Length < 5 || target.Contains(',')) return false;
        bool isPhone = target.All(char.IsDigit);
        bool Matches(string line) => isPhone ? Regex.Replace(line, @"\D", "").Contains(target) : line.Contains(target, StringComparison.OrdinalIgnoreCase);
        if (Preferences.All.Any(Matches)) return true;
        try
        {
            if (!File.Exists(AiLog.FilePath)) return false;
            // "gmail sent  to x@y: …" and "whatsapp  to 9188…" followed by "done … Sent".
            return File.ReadLines(AiLog.FilePath).TakeLast(5000).Any(line =>
                (line.Contains("gmail sent") || line.Contains("  whatsapp  ")) && Matches(line));
        }
        catch (IOException)
        {
            return false;
        }
    }
}
