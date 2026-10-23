using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shiki.Core.Mail;

public sealed record CalendarEvent(string Id, string Title, DateTime Start, DateTime End, bool AllDay, string? Location, string? JoinLink, IReadOnlyList<string> Attendees, string? Link);

/// <summary>
/// Google Calendar (the person's main calendar), through the same Google sign-in as Gmail:
/// what's on, adding, moving and deleting events. Times are the PC's own time zone.
/// </summary>
public static class Calendar
{
    const string Api = "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    /// <summary>The PC's time zone the way Google writes it ("Asia/Kolkata").</summary>
    static string Zone => TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";

    static string Stamp(DateTime t) => new DateTimeOffset(t, TimeZoneInfo.Local.GetUtcOffset(t)).ToString("yyyy-MM-ddTHH:mm:sszzz");

    /// <summary>Events between two times, soonest first (repeating ones as single days).</summary>
    public static async Task<List<CalendarEvent>> Between(DateTime from, DateTime to, CancellationToken ct)
    {
        using var json = await Gmail.Google(HttpMethod.Get,
            $"{Api}?timeMin={Uri.EscapeDataString(Stamp(from))}&timeMax={Uri.EscapeDataString(Stamp(to))}&singleEvents=true&orderBy=startTime&maxResults=50", null, ct);
        if (!json.RootElement.TryGetProperty("items", out var items)) return [];
        return items.EnumerateArray().Where(e => e.TryGetProperty("start", out _) && Str(e, "status") != "cancelled").Select(Read).ToList();
    }

    static CalendarEvent Read(JsonElement e)
    {
        var (start, allDay) = When(e.GetProperty("start"));
        var (end, _) = e.TryGetProperty("end", out var endJson) ? When(endJson) : (start.AddHours(1), allDay);
        string? join = Str(e, "hangoutLink");
        if (join == null && e.TryGetProperty("conferenceData", out var conference) && conference.TryGetProperty("entryPoints", out var points))
            join = points.EnumerateArray().Where(p => Str(p, "entryPointType") == "video").Select(p => Str(p, "uri")).FirstOrDefault();
        // A Zoom or Teams link written in the place or the notes.
        var location = Str(e, "location");
        if (join == null)
            foreach (var text in new[] { location, Str(e, "description") })
                if (text != null && System.Text.RegularExpressions.Regex.Match(text, @"https://[^\s""<>]*(zoom\.us|teams\.microsoft\.com|meet\.google\.com)[^\s""<>]*") is { Success: true } m) { join = m.Value; break; }
        var attendees = e.TryGetProperty("attendees", out var a)
            ? a.EnumerateArray().Select(x => Str(x, "displayName") ?? Str(x, "email") ?? "").Where(x => x.Length > 0).ToList()
            : [];
        return new CalendarEvent(Str(e, "id") ?? "", Str(e, "summary") ?? "(no title)", start, end, allDay, location, join, attendees, Str(e, "htmlLink"));
    }

    static (DateTime When, bool AllDay) When(JsonElement time) =>
        time.TryGetProperty("dateTime", out var dt) ? (DateTimeOffset.Parse(dt.GetString()!).LocalDateTime, false)
        : (DateTime.Parse(time.GetProperty("date").GetString()!), true);

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static object Time(DateTime t, bool allDay) => allDay
        ? new Dictionary<string, string> { ["date"] = t.ToString("yyyy-MM-dd") }
        : new Dictionary<string, string> { ["dateTime"] = Stamp(t), ["timeZone"] = Zone };

    /// <summary>Adds an event; with attendees, Google emails them an invitation.</summary>
    public static async Task<CalendarEvent> Add(string title, DateTime start, DateTime? end, bool allDay, string? location, string? notes, IReadOnlyList<string> attendees, CancellationToken ct)
    {
        var finish = end ?? (allDay ? start.AddDays(1) : start.AddHours(1));
        var body = new Dictionary<string, object> { ["summary"] = title, ["start"] = Time(start, allDay), ["end"] = Time(finish, allDay) };
        if (!string.IsNullOrWhiteSpace(location)) body["location"] = location;
        if (!string.IsNullOrWhiteSpace(notes)) body["description"] = notes;
        if (attendees.Count > 0) body["attendees"] = attendees.Select(email => new { email }).ToArray();
        var url = Api + (attendees.Count > 0 ? "?sendUpdates=all" : "");
        using var json = await Gmail.Google(HttpMethod.Post, url, body, ct);
        Ai.AiLog.Write("calendar add", $"{title} {start:yyyy-MM-dd HH:mm}");
        return Read(json.RootElement);
    }

    /// <summary>Moves an event to a new start (keeping its length unless an end is given).</summary>
    public static async Task<CalendarEvent> Move(string id, DateTime start, DateTime? end, CancellationToken ct)
    {
        using var current = await Gmail.Google(HttpMethod.Get, $"{Api}/{Uri.EscapeDataString(id)}", null, ct);
        var old = Read(current.RootElement);
        var finish = end ?? start + (old.End - old.Start);
        var body = new Dictionary<string, object> { ["start"] = Time(start, old.AllDay), ["end"] = Time(finish, old.AllDay) };
        using var json = await Gmail.Google(HttpMethod.Patch, $"{Api}/{Uri.EscapeDataString(id)}?sendUpdates=all", body, ct);
        Ai.AiLog.Write("calendar move", $"{old.Title} to {start:yyyy-MM-dd HH:mm}");
        return Read(json.RootElement);
    }

    public static async Task Delete(string id, CancellationToken ct)
    {
        using var _ = await Gmail.Google(HttpMethod.Delete, $"{Api}/{Uri.EscapeDataString(id)}?sendUpdates=all", null, ct);
        Ai.AiLog.Write("calendar delete", id);
    }

    /// <summary>"Thu 24 Oct, 17:00–18:00: Dentist (Koregaon Park)".</summary>
    public static string Describe(CalendarEvent e) =>
        $"{e.Start:ddd d MMM}{(e.AllDay ? " (all day)" : $", {e.Start:HH:mm}–{e.End:HH:mm}")}: {e.Title}"
        + (e.Location is { Length: > 0 } where ? $" ({where})" : "")
        + (e.Attendees.Count > 0 ? $", with {string.Join(", ", e.Attendees.Take(5))}" : "")
        + (e.JoinLink != null ? " [video call]" : "");
}

public sealed record Contact(string Name, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones);

/// <summary>The person's Google contacts, and "other contacts" (people they've emailed), by name.</summary>
public static class Contacts
{
    static bool _warmed;

    public static async Task<List<Contact>> Find(string name, CancellationToken ct)
    {
        const string Mask = "readMask=names,emailAddresses,phoneNumbers";
        // Google's search cache wants one empty search before the first real one.
        if (!_warmed)
        {
            _warmed = true;
            try { using var _ = await Gmail.Google(HttpMethod.Get, $"https://people.googleapis.com/v1/people:searchContacts?query=&{Mask}", null, ct); } catch (InvalidOperationException) { }
            await Task.Delay(400, ct);
        }
        var found = new List<Contact>();
        foreach (var url in new[]
        {
            $"https://people.googleapis.com/v1/people:searchContacts?query={Uri.EscapeDataString(name)}&{Mask}&pageSize=10",
            $"https://people.googleapis.com/v1/otherContacts:search?query={Uri.EscapeDataString(name)}&{Mask}&pageSize=10",
        })
        {
            using var json = await Gmail.Google(HttpMethod.Get, url, null, ct);
            if (!json.RootElement.TryGetProperty("results", out var results)) continue;
            foreach (var r in results.EnumerateArray())
            {
                var person = r.GetProperty("person");
                var names = person.TryGetProperty("names", out var n) ? n.EnumerateArray().Select(x => x.TryGetProperty("displayName", out var d) ? d.GetString() : null).FirstOrDefault(x => x != null) : null;
                var emails = person.TryGetProperty("emailAddresses", out var e) ? e.EnumerateArray().Select(x => x.GetProperty("value").GetString()!).ToList() : [];
                var phones = person.TryGetProperty("phoneNumbers", out var p)
                    ? p.EnumerateArray().Select(x => x.TryGetProperty("canonicalForm", out var c) ? c.GetString()! : x.GetProperty("value").GetString()!).ToList()
                    : [];
                var contact = new Contact(names ?? emails.FirstOrDefault() ?? "", emails, phones);
                if (!found.Any(f => f.Name == contact.Name && f.Emails.SequenceEqual(contact.Emails))) found.Add(contact);
            }
        }
        Ai.AiLog.Write("contacts", $"\"{name}\": {found.Count}");
        return found;
    }
}
