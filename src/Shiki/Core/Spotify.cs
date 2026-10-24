using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;
using Shiki.Core.Ai;
using Windows.Media.Control;

namespace Shiki.Core;

/// <summary>
/// Music on Spotify without searching on screen: a song's link opens and starts it in the Spotify
/// app, and Windows' media controls (what the taskbar's media flyout shows) say what's really
/// playing, so the cat never claims a song is on when it isn't. What the person asked for ("AC/DC")
/// is remembered with the song it found, so asking again plays it at once, without Claude.
/// </summary>
public static class Spotify
{
    public sealed record Pick(string Uri, string Label);

    static string FilePath => Path.Combine(Log.DataDir, "spotify-picks.json");
    static Dictionary<string, Pick>? _picks;

    static Dictionary<string, Pick> Picks
    {
        get
        {
            if (_picks != null) return _picks;
            try
            {
                _picks = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, Pick>>(File.ReadAllText(FilePath)) ?? [] : [];
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the Spotify picks", ex);
                _picks = [];
            }
            return _picks;
        }
    }

    /// <summary>"AC/DC", "acdc", "some AC DC songs" all come to "acdc".</summary>
    public static string Key(string said)
    {
        var words = Regex.Replace(said.ToLowerInvariant(), @"\b(some|songs?|music|by|the songs of|a song by|on spotify|spotify|please)\b", " ");
        return Regex.Replace(words, @"[^a-z0-9ऀ-ॿ]", "");
    }

    public static Pick? Find(string said) => Key(said) is { Length: > 0 } key && Picks.TryGetValue(key, out var pick) ? pick : null;

    public static void Remember(string said, string uri, string label)
    {
        var key = Key(said);
        if (key.Length == 0) return;
        Picks[key] = new Pick(uri, label);
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Picks));
        }
        catch (IOException ex)
        {
            Log.Error("Could not keep the Spotify pick", ex);
        }
    }

    static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    // Words that mean a mood, a list or the player, not a song or artist: those go to the chat.
    static readonly Regex NotAName = new(@"\b(playlist|my|liked|something|anything|music|mood|relaxing|calm|sad|happy|party|workout|gym|lofi|lo-fi|chill|study|sleep|video|youtube|it|that|this|next|previous|again|random|radio|album|podcast|episode)\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// "Play Pink Floyd" the first time, without the whole chat: Apple's free music search picks the
    /// song (an artist's most popular one, or the song named), then one quick web search finds that
    /// song's Spotify link. Null when it isn't clearly a song or artist, or nothing fits: the chat
    /// handles those. About two to three seconds.
    /// </summary>
    public static async Task<Pick?> Lookup(string asked, CancellationToken ct)
    {
        var query = Regex.Replace(asked, @"\b(some|songs?|by|the songs of|a song by|on spotify|spotify|please|for me)\b", " ", RegexOptions.IgnoreCase).Trim();
        query = Regex.Replace(query, @"\s+", " ");
        if (query.Length < 2 || query.Split(' ').Length > 6 || NotAName.IsMatch(query) || ClaudeKey.Read() is not { } key) return null;

        // The song: Apple's search (works in India, no key), best match first.
        string title, artist;
        try
        {
            var url = "https://itunes.apple.com/search?" + $"term={Uri.EscapeDataString(query)}&entity=song&limit=3&country=IN";
            using var json = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            var results = json.RootElement.GetProperty("results").EnumerateArray().ToList();
            if (results.Count == 0) return null;
            var top = results[0];
            title = top.GetProperty("trackName").GetString() ?? "";
            artist = top.GetProperty("artistName").GetString() ?? "";
            // Only if it's clearly what was asked for (the artist, or the song by name).
            if (!Key(artist).Contains(Key(query)) && !Key(title).Contains(Key(query)) && !Key(query).Contains(Key(title)))
            {
                Log.Info($"Music lookup: \"{query}\" gave {title} by {artist}, which isn't clearly it");
                return null;
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Error("Apple's music search didn't answer", ex);
            return null;
        }

        // Its Spotify link: one web search, read straight from the results.
        try
        {
            var client = new AnthropicClient { ApiKey = key };
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cancel.CancelAfter(TimeSpan.FromSeconds(12));
            var message = await client.Messages.Create(new MessageCreateParams
            {
                Model = CatChat.Model,
                // Low effort: no long thinking before the one search (it ran out of room once).
                MaxTokens = 600,
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                Tools = [new WebSearchTool20250305 { MaxUses = 1 }],
                Messages = [new UserMessageParam { Content = $"Search the web once for: \"{title}\" {artist} open.spotify.com/track. Then reply with only that song's open.spotify.com/track link." }],
            }, cancel.Token);
            var text = string.Join("\n", message.Content.Select(block => JsonSerializer.Serialize(block, block.GetType())));
            var found = Regex.Match(text, @"open\.spotify\.com\\?/(?:intl-[a-z]+\\?/)?track\\?/([A-Za-z0-9]{22})");
            if (!found.Success)
            {
                Log.Info($"Music lookup: no Spotify link for {title} by {artist} in: {(text.Length > 1500 ? text[..1500] : text)}");
                return null;
            }
            AiLog.Write("found", $"{title} by {artist} for \"{asked}\"");
            return new Pick($"spotify:track:{found.Groups[1].Value}", $"{title} by {artist}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Error("Couldn't find the song's Spotify link", ex);
            return null;
        }
    }

    /// <summary>
    /// Opens the link in the Spotify app and waits until something new is playing: a song or album
    /// usually starts by itself; if it opened but sits paused, a real "play" (not the play/pause key,
    /// which would pause a song that had already started) starts it. Says what's playing.
    /// </summary>
    public static async Task<string> Play(string uri, string label)
    {
        GlobalSystemMediaTransportControlsSessionManager? controls = null;
        try { controls = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch (Exception ex) { Log.Error("Windows' media controls aren't available", ex); }

        var before = controls != null ? await Now(controls) : null;
        // Something already playing keeps playing when a new song's page opens: pause it first,
        // so the new song takes over.
        if (before is { Playing: true } && controls != null && Session(controls) is { } playing)
        {
            await playing.TryPauseAsync();
            await Task.Delay(250);
        }
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        AiLog.Write("spotify", $"{label} ({uri})");
        if (controls == null) return $"Opened {label} in Spotify.";

        bool pressed = false, again = false;
        (string Title, string Artist, bool Playing)? now = null;
        for (int i = 0; i < 48; i++)
        {
            await Task.Delay(250);
            // Nothing new after three seconds (Spotify was busy coming to the front): open it once more.
            if (i == 12 && !again && now is { } still && !Same(label, still.Title) && (label == "it" ? before is { } old && still.Title == old.Title : true))
            {
                again = true;
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
            }
            now = await Now(controls);
            if (now is not { } n || n.Title.Length == 0) continue;
            bool changed = before is not { } b || b.Title != n.Title || b.Artist != n.Artist;
            // When the song is known, only that song counts (Spotify sometimes resumes the old one
            // for a moment while it switches); "it" (any link) counts as soon as something new plays.
            bool known = label != "it";
            bool right = Same(label, n.Title);
            bool asked = known ? right : changed;
            if (asked && n.Playing) return $"Playing {n.Title} by {n.Artist}.";
            // Opened on the song but not started: press play, once.
            if (asked && !pressed && i >= 10 && Session(controls) is { } session)
            {
                pressed = true;
                await session.TryPlayAsync();
            }
        }
        // The same song was already on, and still is.
        if (now is { Playing: true } same && before is { } was && was.Title == same.Title && label == "it") return $"Playing {same.Title} by {same.Artist}.";
        if (now is { Playing: true } other && label != "it") return $"Spotify is playing {other.Title} by {other.Artist}, not {label}: it didn't switch. Pick {label} in Spotify.";
        return $"Spotify is open on {label}, but it didn't start by itself: press play there.";
    }

    /// <summary>Is this title the song in the label? "Comfortably Numb - 2011 Remaster" is "Comfortably Numb by Pink Floyd".</summary>
    static bool Same(string label, string title)
    {
        var core = Key(Regex.Replace(title, @"\s*(\s-\s|\(|\[).*$", ""));
        var wanted = Key(Regex.Replace(label.Split(" by ")[0], @"\s*(\s-\s|\(|\[).*$", ""));
        return core.Length > 0 && wanted.Length > 0 && (core.Contains(wanted) || wanted.Contains(core));
    }

    static GlobalSystemMediaTransportControlsSession? Session(GlobalSystemMediaTransportControlsSessionManager controls) =>
        controls.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase));

    static async Task<(string Title, string Artist, bool Playing)?> Now(GlobalSystemMediaTransportControlsSessionManager controls)
    {
        try
        {
            if (Session(controls) is not { } session) return null;
            var media = await session.TryGetMediaPropertiesAsync();
            bool playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return (media?.Title ?? "", media?.Artist ?? "", playing);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
