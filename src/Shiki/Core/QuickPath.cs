using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shiki.Core;

/// <summary>
/// The everyday requests understood on this PC, without asking Claude: "WhatsApp Mummy that I'm on
/// my way", "email this to me", "remind me at 5 to call the bank", and two or three of them joined
/// by "and". Anything it isn't sure of is left to Claude.
/// </summary>
public static class QuickPath
{
    public abstract record Item;
    /// <summary>A WhatsApp text to someone by name; Web when they said Chrome or WhatsApp Web.</summary>
    public sealed record Message(string Name, string Text, bool Web) : Item;
    /// <summary>The selected files (or a phrase the cat learned, "the logo photos") to someone, by email or WhatsApp.</summary>
    public sealed record Files(string Who, bool Email, string What) : Item;
    public sealed record Reminder(string Text, DateTime Due) : Item;

    const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>What was asked, as quick items in order; null when any of it needs Claude.</summary>
    public static List<Item>? Plan(string said, DateTime now)
    {
        var text = Clean(said);
        if (text.Length == 0) return null;
        // "WhatsApp Mummy that I'm coming and remind me at 5 to call her": split before each new request.
        var parts = Regex.Split(text, @",?\s+(?:and then|and also|and|then|aur|phir|also)\s+(?=(?:remind me|set (?:a |me a )?reminder|whats ?app|email|mail|send|tell|message|text)\b)", I);
        if (parts.Length > 3) return null;
        var items = new List<Item>();
        foreach (var part in parts)
        {
            if (One(part.Trim().TrimEnd(','), now) is not { } item) return null;
            items.Add(item);
        }
        return items;
    }

    static Item? One(string text, DateTime now) => Remind(text, now) ?? (Item?)WhatsApp(text) ?? SendFiles(text);

    static string Clean(string said)
    {
        var s = said.Trim();
        for (string was = ""; was != s;)
        {
            was = s;
            s = Regex.Replace(s, @"^(?:(?:can|could|would) you(?: please)?|please|just|quickly|ok(?:ay)?|so|hey)[,\s]+", "", I);
        }
        s = Regex.Replace(s, @"\b([ap])\.\s?m\.?(?=\s|$)", "$1m", I);
        return s.Trim().TrimEnd('.', '!', '?', ' ', ',');
    }

    // ---- WhatsApp ----

    // One to three words of a name ("Mummy", "Tausif Ansari"), never "him" or "the group".
    const string Name = @"(?<name>[\p{L}][\p{L}'.]*(?: [\p{L}][\p{L}'.]*){0,2}?)";
    const string App = @"(?:whats ?app)";
    const string OnWeb = @"(?<web> on (?:chrome|whats ?app web|the web|the browser)| via (?:chrome|whats ?app web))";

    static readonly Regex[] Messages =
    [
        new($@"^(?:send (?:a )?)?{App}(?: message)? (?:to )?{Name}{OnWeb}?[,:]? (?:that|saying|and say|and tell (?:him|her|them)(?: that)?|to say|ki) (?<text>.+)$", I),
        new($@"^(?:send |write )?(?:a )?(?:message|msg|text) (?:to )?{Name} on {App}(?<web> web)?[,:]? (?:that |saying |ki )?(?<text>.+)$", I),
        new($@"^(?<verb>tell|ask) {Name} on {App}(?<web> web)?[,:]? (?:that )?(?<text>.+)$", I),
        new($@"^{Name} ko {App}(?: pe| par)? (?:karo|kar do|bhejo|bhej do|message karo|msg karo)(?: ki)? (?<text>.+)$", I),
    ];

    static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
        { "him", "her", "them", "me", "myself", "everyone", "everybody", "someone", "somebody", "it", "this", "that", "us", "all" };

    static bool IsName(string name) =>
        !NotNames.Contains(name) && !Regex.IsMatch(name, @"^(?:the|my|our|a|an|this|that|these|his|her|their) ", I) && !Regex.IsMatch(name, @"\b(?:on|that|to|and)\b", I);

    public static Message? WhatsApp(string text)
    {
        foreach (var pattern in Messages)
        {
            var m = pattern.Match(text);
            if (!m.Success || !IsName(m.Groups["name"].Value)) continue;
            var body = m.Groups["text"].Value.Trim();
            // "Ask Tausif on WhatsApp to call me": said to them, it's a please.
            if (m.Groups["verb"].Value.Equals("ask", StringComparison.OrdinalIgnoreCase) && body.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
                body = "Please " + body[3..];
            else if (m.Groups["verb"].Value.Equals("tell", StringComparison.OrdinalIgnoreCase) && body.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
                body = "Please " + body[3..];
            body = Direct(body);
            if (body.Length == 0) continue;
            return new Message(Title(m.Groups["name"].Value), body, m.Groups["web"].Success);
        }
        return null;
    }

    /// <summary>"He should call me" was said about them; the message speaks to them ("Please call me").</summary>
    public static string Direct(string text)
    {
        var s = text.Trim();
        s = Regex.Replace(s, @"^(?:he|she|they) (?:should|must|needs to|need to|has to|have to) ", "Please ", I);
        s = Regex.Replace(s, @"^(?:he|she|they) (?:can|could) ", "You can ", I);
        s = Regex.Replace(s, @"^(?:he|she) is |^they are ", "You're ", I);
        s = Regex.Replace(s, @"^(?:he|she|they) (?:will|'ll) ", "You'll ", I);
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    static string Title(string name) => string.Join(" ", name.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    // ---- Files ----

    static readonly Regex FilesTo = new($@"^(?<verb>email|e-mail|mail|send|forward|{App})(?<me> me)? (?<what>.+?)(?: (?:over )?to (?<who>me|myself|{Name}))?(?<via> (?:on|by|over|through|via) (?:e-?mail|mail|gmail|{App}))?$", I);

    /// <summary>"Email this to me", "WhatsApp these to Tausif", "send the logo photos to Mummy on WhatsApp".</summary>
    public static Files? SendFiles(string text)
    {
        var m = FilesTo.Match(text);
        if (!m.Success) return null;
        var who = m.Groups["me"].Success ? "me" : m.Groups["who"].Value;
        if (who.Length == 0 || m.Groups["me"].Success && m.Groups["who"].Success) return null;
        if (who is "myself") who = "me";
        if (who != "me" && !IsName(who)) return null;
        var what = m.Groups["what"].Value.Trim();
        if (!IsSelection(what) && Learned.Means(what) == null) return null;
        var verb = m.Groups["verb"].Value.ToLowerInvariant();
        var via = m.Groups["via"].Value.ToLowerInvariant();
        bool whatsapp = verb.StartsWith("whats") || via.Contains("whats");
        bool email = verb is "email" or "e-mail" or "mail" || via.Contains("mail");
        // "Send this to Tausif": which way isn't said (to me, it's email).
        if (!whatsapp && !email)
        {
            if (who != "me") return null;
            email = true;
        }
        return new Files(who == "me" ? "me" : Title(who), !whatsapp, what);
    }

    /// <summary>"This", "these", "the selected photos": whatever is selected in File Explorer.</summary>
    public static bool IsSelection(string what) =>
        Regex.IsMatch(what, @"^(?:this|these|that|those|it|them|(?:this|that|these|those|the selected|the) (?:file|files|photo|photos|picture|pictures|image|images|pdf|pdfs|document|documents|video|videos)|the selected (?:ones|items))$", I);

    // ---- Reminders ----

    static readonly string[] Numbers = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];
    const string Hour = @"(?<h>1[0-2]|0?[1-9]|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)";
    const string Day = @"(?:today|tomorrow|tonight|this (?:morning|afternoon|evening)|tomorrow (?:morning|afternoon|evening|night)|in the (?:morning|afternoon|evening)|at night)";
    static readonly string When =
        $@"(?:in (?<n>\d+|a|an|one|two|three|four|five|ten|fifteen|twenty|thirty|half an?) ?(?<unit>minutes?|mins?|hours?|hrs?)"
        + $@"|(?:(?<day>{Day}) )?(?:at|by|around|for) {Hour}(?:[:.](?<m>[0-5]\d)| (?<m>[0-5]\d)(?= ?[ap]m\b))? ?(?<ap>am|pm|o'?clock)?(?: (?<day2>{Day}))?"
        + $@"|(?<day>{Day}))";
    static readonly Regex RemindStart = new(@"^(?:remind me|set (?:a |me a )?reminder|add a reminder|reminder)(?: for me)?(?: to)? (?<rest>.+)$", I);
    static readonly Regex WhatThenWhen = new($@"^(?:to |that |about )?(?<what>.+?),? (?<when>{When})$", I);
    static readonly Regex WhenThenWhat = new($@"^(?<when>{When}),? (?:to |that |about )(?<what>.+)$", I);

    public static Reminder? Remind(string text, DateTime now)
    {
        var start = RemindStart.Match(text);
        if (!start.Success) return null;
        var rest = start.Groups["rest"].Value.Trim();
        var m = WhenThenWhat.Match(rest);
        if (!m.Success) m = WhatThenWhen.Match(rest);
        if (!m.Success) return null;
        var what = Regex.Replace(m.Groups["what"].Value.Trim(), @"^(?:to |that )?(?:i (?:need|have|should|must) to |i should |i must )?", "", I).Trim();
        if (what.Length < 2 || Due(m, now) is not { } due) return null;
        return new Reminder(char.ToUpperInvariant(what[0]) + what[1..], due);
    }

    static DateTime? Due(Match m, DateTime now)
    {
        if (m.Groups["unit"].Success)
        {
            var n = m.Groups["n"].Value.ToLowerInvariant();
            bool half = n.StartsWith("half");
            double amount = half ? 0.5 : n is "a" or "an" ? 1 : int.TryParse(n, out var d) ? d
                : n switch { "fifteen" => 15, "twenty" => 20, "thirty" => 30, _ => Array.IndexOf(Numbers, n) };
            if (amount <= 0) return null;
            return m.Groups["unit"].Value.StartsWith('h') ? now.AddHours(amount) : now.AddMinutes(half ? 30 : amount);
        }
        var day = (m.Groups["day"].Value + " " + m.Groups["day2"].Value).Trim().ToLowerInvariant();
        bool tomorrow = day.Contains("tomorrow");
        var date = tomorrow ? now.Date.AddDays(1) : now.Date;
        bool evening = Regex.IsMatch(day, "afternoon|evening|night|tonight");
        bool morning = day.Contains("morning");
        if (!m.Groups["h"].Success)
        {
            if (day.Length == 0) return null;
            int at = morning || day == "tomorrow" ? 9 : day.Contains("afternoon") ? 14 : day.Contains("evening") ? 18 : 21;
            var only = date.AddHours(at);
            return only > now ? only : null;
        }
        var h = m.Groups["h"].Value.ToLowerInvariant();
        int hour = int.TryParse(h, out var parsed) ? parsed : Array.IndexOf(Numbers, h);
        int minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        var ap = m.Groups["ap"].Value.ToLowerInvariant();
        if (hour is < 1 or > 12) return null;
        var options = new List<int>();
        if (ap == "pm" || evening) options.Add(hour % 12 + 12);
        else if (ap == "am" || morning) options.Add(hour % 12);
        // "At 5": the coming 5 o'clock; small hours are evenings ("at 5" is 5 pm), others whichever comes first.
        else if (hour <= 6) options.Add(hour + 12);
        else if (hour == 12) options.Add(12);
        else options.AddRange([hour, hour + 12]);
        foreach (var o in options)
        {
            var due = date.AddHours(o).AddMinutes(minute);
            if (due > now) return due;
        }
        // Gone by today, with no day said: tomorrow at that time.
        return day.Length == 0 ? date.AddDays(1).AddHours(options[0]).AddMinutes(minute) : null;
    }

    // ---- Who's who (from what the cat remembers) ----

    /// <summary>A remembered WhatsApp number for this name ("Mummy's WhatsApp is 91…"), sounds-alike names included.</summary>
    public static (string Name, string Phone)? RememberedPhone(string name)
    {
        if (name == "me" && Preferences.All.Select(f => Regex.Match(f, @"^my own (?:whats ?app|number|phone)\b.*? is (?<a>\+?\d[\d ]{8,})", I)).FirstOrDefault(m => m.Success) is { } own)
            return ("me", new string(own.Groups["a"].Value.Where(char.IsAsciiDigit).ToArray()));
        return Remembered(name, @"(?<a>\+?\d[\d ]{8,})", "WhatsApp|number|phone|mobile") is { } r
            ? (r.Name, new string(r.Value.Where(char.IsAsciiDigit).ToArray()))
            : null;
    }

    /// <summary>A remembered email for this name, or the person's own for "me".</summary>
    public static (string Name, string Email)? RememberedEmail(string name)
    {
        if (name == "me")
        {
            var own = Preferences.All.Select(f => Regex.Match(f, @"^my own email\b.*? is (?<a>\S+@\S+?)\.?$", I)).FirstOrDefault(m => m.Success);
            return own == null ? null : ("me", own.Groups["a"].Value);
        }
        return Remembered(name, @"(?<a>[\w.%+'\-]+@[\w\-]+(?:\.[\w\-]+)+)", "email|mail");
    }

    static (string Name, string Value)? Remembered(string name, string address, string kind)
    {
        foreach (var candidate in new[] { name }.Concat(People.SoundsLike(name)))
        {
            var who = Regex.Escape(candidate);
            foreach (var fact in Preferences.All)
            {
                // "Tausif's email is …", or "When I say Yash … I mean Yash Choudhary (…)".
                var m = Regex.Match(fact, $@"^{who}'s (?:{kind})\b.*?{address}", I);
                if (!m.Success) m = Regex.Match(fact, $@"^when i say {who}\b.*?{address}", I);
                if (m.Success && (kind.Contains("mail") || !fact.Contains('@'))) return (candidate, m.Groups["a"].Value.TrimEnd('.'));
            }
        }
        return null;
    }
}
