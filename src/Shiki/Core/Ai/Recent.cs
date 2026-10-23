using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shiki.Core.Ai;

/// <summary>
/// What the person asked and what the cat did lately (from its own log), so a new conversation
/// can pick up where the last one left off: "do the same as last time", "send it to him again".
/// </summary>
public static class Recent
{
    // The log's own words for the things worth remembering, and how to say them.
    static readonly Dictionary<string, string> Kinds = new()
    {
        ["said"] = "they said", ["asked"] = "they asked", ["confirmed"] = "they confirmed", ["done"] = "done",
        ["gmail sent"] = "email sent", ["gmail draft"] = "draft saved", ["agent done"] = "on screen", ["routine"] = "routine",
        ["opened app"] = "opened", ["tidied"] = "tidied", ["renamed"] = "renamed", ["translated"] = "translated",
        ["remembered"] = "remembered", ["attachments"] = "saved attachments", ["whatsapp"] = "WhatsApp", ["link"] = "opened",
    };

    /// <summary>A few lines on the last day or two, newest last; empty when there's nothing.</summary>
    public static string Summary(int most = 18)
    {
        try
        {
            if (!File.Exists(AiLog.FilePath)) return "";
            var since = DateTime.Now.AddHours(-36);
            var lines = new List<string>();
            foreach (var line in File.ReadLines(AiLog.FilePath).TakeLast(600))
            {
                // "2026-10-23 19:21:02  said      Can you reply…"
                var m = Regex.Match(line, @"^(\d{4}-\d\d-\d\d \d\d:\d\d):\d\d  (.*)$");
                if (!m.Success || !DateTime.TryParse(m.Groups[1].Value, out var when) || when < since) continue;
                // Kinds are padded to nine characters, and some are longer ("gmail sent"): match them by start.
                var rest = m.Groups[2].Value;
                var kind = Kinds.Keys.OrderByDescending(k => k.Length).FirstOrDefault(k => rest.StartsWith(k + " ") || rest.StartsWith(k.PadRight(9)));
                if (kind == null) continue;
                var label = Kinds[kind];
                var detail = rest[kind.Length..].Trim();
                if (detail.Length > 160) detail = detail[..160] + "…";
                lines.Add($"- {when:ddd HH:mm} {label}: {detail}");
            }
            return string.Join("\n", lines.TakeLast(most));
        }
        catch (IOException)
        {
            return "";
        }
    }
}
