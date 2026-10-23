using System;
using System.IO;
using System.Text.Json;

namespace Shiki.Core;

public sealed class Settings
{
    /// <summary>Horizontal spot along the bottom edge where the cat lives (DIPs).</summary>
    public double? CatLeft { get; set; }
    public string Cat { get; set; } = "mochi";
    /// <summary>"arcade" (pixel) or "glass".</summary>
    public string Style { get; set; } = "arcade";
    public bool BuddyVisible { get; set; } = true;
    public bool ShiftDragSummon { get; set; } = true;
    /// <summary>Glass look: keep the glass in step with what's behind it (hides popups from screen recordings).</summary>
    public bool LiveGlass { get; set; } = true;
    /// <summary>Global shortcut for snip and convert, e.g. "Ctrl+Shift+Q".</summary>
    public string SnipHotkey { get; set; } = "Ctrl+Shift+Q";
    /// <summary>Global shortcut for the colour picker. Not Ctrl+Shift+C: terminals copy and browsers inspect with that.</summary>
    public string ColourHotkey { get; set; } = "Ctrl+Alt+C";
    /// <summary>Global shortcut that opens the wheel on the files selected in Explorer or on the desktop.</summary>
    public string ConvertHotkey { get; set; } = "Ctrl+Alt+X";
    /// <summary>Screen recording: start (and, pressed again, stop). Not Ctrl+Shift+R: browsers hard-reload with that.</summary>
    public string RecordHotkey { get; set; } = "Ctrl+Alt+R";
    public bool RecordMic { get; set; }
    public bool RecordSound { get; set; } = true;
    /// <summary>Where results go: a folder, or null to keep them next to the original.</summary>
    public string? OutputFolder { get; set; } = OutputPaths.DefaultFolder;
    /// <summary>In the results folder, sort into Images, Videos, Audio, PDFs…</summary>
    public bool SortOutputs { get; set; }
    /// <summary>The cat's tally: files it has handled, and bytes saved where results came out smaller.</summary>
    public int FilesDone { get; set; }
    /// <summary>Dashboard look: true dark, false light, null follows Windows.</summary>
    public bool? DashboardDark { get; set; }
    /// <summary>Setup checklist: done once the convert shortcut has been used, or the results folder visited.</summary>
    public bool UsedConvertHotkey { get; set; }
    public bool ChoseOutputFolder { get; set; }
    /// <summary>Home tips the person closed.</summary>
    public System.Collections.Generic.List<string> DismissedTips { get; set; } = [];
    public long BytesSaved { get; set; }

    /// <summary>Ask the cat (Claude): on unless switched off. It also needs an API key, kept in Credential Manager.</summary>
    public bool AiEnabled { get; set; } = true;
    /// <summary>Global shortcut that opens the ask bubble by the cat.</summary>
    public string AskHotkey { get; set; } = "Ctrl+Alt+A";
    /// <summary>What the person calls their cat; null uses the skin's own name.</summary>
    public string? CatName { get; set; }
    /// <summary>What asking the cat has cost so far, in US dollars, and how many requests that was.</summary>
    public double AiSpentUsd { get; set; }
    public int AiRequests { get; set; }
    /// <summary>Global shortcut that opens the ask bubble already listening.</summary>
    public string TalkHotkey { get; set; } = "Ctrl+Alt+V";
    /// <summary>Paw to ask: swipe the cat's paw around something on screen and ask about it.</summary>
    public string PawHotkey { get; set; } = "Ctrl+Alt+P";
    /// <summary>"What's this error?": the cat looks at the window in front and explains it.</summary>
    public string ErrorHotkey { get; set; } = "Ctrl+Alt+E";
    /// <summary>Meeting notes: start, and pressed again, stop.</summary>
    public string NotesHotkey { get; set; } = "Ctrl+Alt+N";
    /// <summary>Dictation: hold, talk, let go and the words are typed where the cursor is (or tap: until a pause).</summary>
    public string DictateHotkey { get; set; } = "Ctrl+Alt+D";
    /// <summary>Also keep a meeting's sound (as M4A, with recordings). Off: only the notes and transcript stay.</summary>
    public bool KeepMeetingAudio { get; set; }
    /// <summary>When a call starts (Zoom, Teams, Meet…), offer once to take notes.</summary>
    public bool OfferNotes { get; set; } = true;
    /// <summary>A new download that's probably wanted another way (HEIC, a huge video, an archive): suggest it.</summary>
    public bool SuggestDownloads { get; set; } = true;
    /// <summary>The cat says a short line now and then (welcome back, what a job saved).</summary>
    public bool Quips { get; set; } = true;
    /// <summary>Hide card numbers, Aadhaar, PAN, passwords and keys in text before it goes to Claude.</summary>
    public bool PrivacyGuard { get; set; } = true;
    /// <summary>What the cat wears (an unlocked accessory id), or null.</summary>
    public string? Accessory { get; set; }
    /// <summary>The level the cat had reached last time, to notice a new one.</summary>
    public int CatLevel { get; set; }
    /// <summary>How much the cat moves on its own: "lively", "calm" or "still".</summary>
    public string CatLife { get; set; } = "calm";
    /// <summary>Speech to text for commands: "whisper" (on this PC), "deepgram", or "wispr" (Wispr Flow, once its API is open).</summary>
    public string VoiceEngine { get; set; } = "whisper";
    /// <summary>Who checks speech for the wake word: "whisper" (on this PC) or "deepgram" (speech bursts only).</summary>
    public string WakeEngine { get; set; } = "whisper";
    /// <summary>Seconds of audio sent to Deepgram so far.</summary>
    public double DeepgramSeconds { get; set; }
    /// <summary>After a spoken request, listen for "yes" or "no" when the cat shows a plan.</summary>
    public bool VoiceAnswers { get; set; } = true;
    /// <summary>After a spoken request and the cat's reply, keep listening this many seconds for the next one (0: off).</summary>
    public int FollowUpSeconds { get; set; } = 8;
    /// <summary>Listen all the time (on this PC) for "Hey &lt;cat name&gt;".</summary>
    public bool WakeWord { get; set; }
    /// <summary>How Whisper wrote "Hey &lt;cat name&gt;" when the person taught it, e.g. "hey lottie".</summary>
    public System.Collections.Generic.List<string> WakeVariants { get; set; } = [];
    /// <summary>Only the owner's voice wakes the cat (needs the voiceprint from teaching it its name).</summary>
    public bool VoiceLock { get; set; } = true;
    /// <summary>How alike the voice must be: "relaxed", "normal" or "strict".</summary>
    public string VoiceLockLevel { get; set; } = "normal";
    /// <summary>What the person speaks: "en", or "hi-en" (Hindi, English and Hinglish).</summary>
    public string SpeechLanguage { get; set; } = "en";
    /// <summary>How dictated Hindi is typed: "devanagari" (हिंदी) or "roman" (Hinglish, as on WhatsApp).</summary>
    public string HindiScript { get; set; } = "devanagari";
    /// <summary>Read the cat's replies aloud: "spoken" (when you spoke to it), "always", or "off".</summary>
    public string TalkBack { get; set; } = "spoken";
    /// <summary>The cat's voice: "dg:aura-2-…" (Deepgram) or "win:&lt;id&gt;" (Windows); empty picks one.</summary>
    public string TalkVoice { get; set; } = "";
    /// <summary>Characters read aloud by Deepgram so far.</summary>
    public long DeepgramSpokenChars { get; set; }

    static string FilePath => Path.Combine(Log.DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
