using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        AiLog.Write("spotify", $"{label} ({uri})");
        if (controls == null) return $"Opened {label} in Spotify.";

        bool pressed = false;
        (string Title, string Artist, bool Playing)? now = null;
        for (int i = 0; i < 48; i++)
        {
            await Task.Delay(250);
            now = await Now(controls);
            if (now is not { } n || n.Title.Length == 0) continue;
            bool changed = before is not { } b || b.Title != n.Title || b.Artist != n.Artist;
            // The song asked for was already the one loaded (paused, say): it counts too.
            bool asked = changed || label.Contains(n.Title, StringComparison.OrdinalIgnoreCase);
            if (asked && n.Playing) return $"Playing {n.Title} by {n.Artist}.";
            // Opened on the song but not started: press play, once.
            if (asked && !pressed && i >= 10 && Session(controls) is { } session)
            {
                pressed = true;
                await session.TryPlayAsync();
            }
        }
        // The same song was already on, and still is.
        if (now is { Playing: true } same && before is { } was && was.Title == same.Title) return $"Playing {same.Title} by {same.Artist}.";
        return $"Spotify is open on {label}, but it didn't start by itself: press play there.";
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
