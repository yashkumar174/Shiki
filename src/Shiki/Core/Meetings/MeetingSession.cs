using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;
using Shiki.Core.Voice;

namespace Shiki.Core.Meetings;

/// <summary>
/// One meeting being noted: the sound, the words as they come, marks, and at the end the notes
/// and tasks. Raises Changed on the thread it was started on (the UI thread).
/// </summary>
public sealed class MeetingSession
{
    readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    readonly MeetingAudio _audio;
    readonly IMeetingTranscriber _words;
    readonly string? _wav;
    readonly object _gate = new();

    public Meeting Meeting { get; }
    /// <summary>What's being said right now, before it's final: [0] you, [1] them.</summary>
    public string[] Partial { get; } = ["", ""];
    public double LevelMe { get; private set; }
    public double LevelThem { get; private set; }
    public double Seconds => _audio.Seconds;
    public string Engine => _words.Name;
    public bool Stopping { get; private set; }

    public event Action? Changed;

    MeetingSession(Meeting meeting, MeetingAudio audio, IMeetingTranscriber words, string? wav)
    {
        Meeting = meeting;
        _audio = audio;
        _words = words;
        _wav = wav;
        _words.Line += line => Post(() =>
        {
            lock (_gate) Meeting.Add(line);
        });
        _words.Partial += (side, text) => Post(() => Partial[side] = text);
        int tick = 0;
        _audio.Levels += (me, them) =>
        {
            LevelMe = me;
            LevelThem = them;
            if (++tick % 3 == 0) Post(() => { });
        };
        _audio.Chunk += _words.Send;
    }

    void Post(Action action) => _ui.Post(_ => { action(); Changed?.Invoke(); }, null);

    /// <summary>Starts listening; the sound is also kept (as M4A, with recordings) when the person keeps recordings.</summary>
    public static MeetingSession Start(Settings settings, string catName, string? app)
    {
        var meeting = new Meeting { App = app };
        string? wav = settings.KeepMeetingAudio ? Path.Combine(Path.GetTempPath(), $"shiki-meeting-{meeting.Id}.wav") : null;
        var audio = new MeetingAudio(wav);
        IMeetingTranscriber words = Speech.CommandsOnDeepgram(settings) || (settings.WakeEngine == "deepgram" && Deepgram.HasKey)
            ? new DeepgramMeeting(Deepgram.Key.Read()!, Speech.KeyTerms(catName))
            : new WhisperMeeting();
        var session = new MeetingSession(meeting, audio, words, wav);
        audio.Start();
        AiLog.Write("notes", $"started ({words.Name})");
        return session;
    }

    /// <summary>"Mark that": the last thing said (or this moment) is flagged as important.</summary>
    public void Mark()
    {
        lock (_gate)
        {
            var now = Seconds;
            var recent = Meeting.Lines.LastOrDefault(l => now - l.At < 30);
            if (recent != null) recent.Marked = true;
            else Meeting.Lines.Add(new MeetingLine { At = now, Speaker = "You", Text = "(marked this moment)", Marked = true });
        }
        AiLog.Write("marked", Meeting.Clock(Seconds));
        Changed?.Invoke();
    }

    /// <summary>
    /// Stops listening, waits for the last words, saves the transcript, then writes the notes and
    /// adds the person's own tasks to their list. The meeting is saved even if the notes fail.
    /// </summary>
    public async Task<Meeting> StopAsync(string myName, Action<double> spent)
    {
        Stopping = true;
        Changed?.Invoke();
        await _audio.StopAsync();
        Meeting.Seconds = _audio.Seconds;
        _audio.Dispose();
        try
        {
            await _words.FinishAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Finishing the meeting transcript", ex);
        }
        await _words.DisposeAsync();
        MeetingStore.Save(Meeting);
        AiLog.Write("notes", $"stopped after {Meeting.Length}, {Meeting.Lines.Count} lines");

        if (_wav != null && File.Exists(_wav))
        {
            try
            {
                var keep = Path.Combine(OutputPaths.RecordingsFolder, $"Meeting {Meeting.Started:yyyy-MM-dd HH.mm}.m4a");
                await MediaTools.RunAsync(_wav, keep, ["-c:a", "aac", "-b:a", "64k"], new Progress<double>(), CancellationToken.None);
                Meeting.Audio = keep;
                File.Delete(_wav);
            }
            catch (Exception ex)
            {
                Log.Error("Could not keep the meeting recording", ex);
            }
        }

        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await MeetingNotes.Write(Meeting, myName, spent, cancel.Token);
            foreach (var task in Meeting.Tasks.Where(t => t.Owner.Equals("me", StringComparison.OrdinalIgnoreCase)))
                Todos.Add(task.Text, task.Due, task.Due is { } due && due.TimeOfDay != TimeSpan.Zero, Meeting.Title, Meeting.Id);
        }
        catch (Exception ex)
        {
            Log.Error("Could not write meeting notes", ex);
            Meeting.NotesProblem = ex is InvalidOperationException ? ex.Message : $"The notes couldn't be written ({ex.Message}). The transcript is saved; try again from the Meetings page.";
        }
        MeetingStore.Save(Meeting);
        return Meeting;
    }
}

/// <summary>
/// Notices a call starting: Windows records which apps are using the microphone right now.
/// When a calling app (Zoom, Teams, a browser for Meet…) starts using it, Started fires once.
/// </summary>
public sealed class CallWatcher : IDisposable
{
    static readonly string[] CallApps = ["zoom", "teams", "msteams", "slack", "discord", "webex", "skype", "whatsapp", "telegram", "signal",
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "meet", "facetime", "gotomeeting", "ringcentral", "bluejeans"];
    readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly System.Collections.Generic.HashSet<string> _inCall = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An app started a call: its friendly name ("Zoom", "Chrome").</summary>
    public event Action<string>? Started;

    public CallWatcher()
    {
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        Check(first: true);
    }

    void Check(bool first = false)
    {
        var now = UsingMic();
        foreach (var app in now)
            if (_inCall.Add(app) && !first) Started?.Invoke(Friendly(app));
        _inCall.RemoveWhere(a => !now.Contains(a));
    }

    /// <summary>Calling apps using the microphone now (from Windows' privacy records).</summary>
    static System.Collections.Generic.HashSet<string> UsingMic()
    {
        var apps = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var store = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone");
            if (store == null) return apps;
            void Look(Microsoft.Win32.RegistryKey parent, string name)
            {
                using var key = parent.OpenSubKey(name);
                if (key == null) return;
                if (key.GetValue("LastUsedTimeStop") is long stop && stop == 0 && key.GetValue("LastUsedTimeStart") is long start && start > 0)
                {
                    var app = AppOf(name);
                    if (app != null) apps.Add(app);
                }
            }
            foreach (var name in store.GetSubKeyNames())
            {
                if (name == "NonPackaged")
                {
                    using var desktop = store.OpenSubKey(name);
                    if (desktop != null) foreach (var exe in desktop.GetSubKeyNames()) Look(desktop, exe);
                }
                else Look(store, name);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read which apps use the microphone", ex);
        }
        return apps;
    }

    /// <summary>The calling app a microphone record belongs to, or null (Shiki itself, other apps).</summary>
    static string? AppOf(string record)
    {
        var name = Path.GetFileNameWithoutExtension(record.Replace('#', '\\')).ToLowerInvariant();
        if (name.StartsWith("shiki")) return null;
        return CallApps.FirstOrDefault(a => name.Contains(a));
    }

    static string Friendly(string app) => app switch
    {
        "msteams" or "teams" => "Teams",
        "msedge" => "Edge",
        "whatsapp" => "WhatsApp",
        _ => char.ToUpperInvariant(app[0]) + app[1..],
    };

    public void Dispose() => _timer.Stop();
}
