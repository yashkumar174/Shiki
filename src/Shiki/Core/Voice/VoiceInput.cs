using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core.Voice;

public enum VoiceState { Off, GettingReady, Waiting, Hearing, Understanding }

/// <summary>
/// Talking to the cat: listen, turn it into text, and hand it to the chat. A plain "yes" or "no"
/// while a plan card waits answers the card instead. After every spoken request the cat keeps
/// listening for a few seconds (a conversation), so the next thing needs no "Hey Sky" and no
/// button; "thanks" or "that's all", or a few quiet seconds, ends it. While it waits for you to
/// speak again nothing goes to Deepgram: it connects once you start talking.
/// Raises Changed on the thread it was made on (the UI thread).
/// </summary>
public sealed class VoiceInput(CatChat chat, Settings settings, Func<string> catName)
{
    readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    Microphone? _mic;
    CancellationTokenSource? _cancel;
    int _lastLevelTick, _turns;
    const int MaxTurns = 12, MaxFollowUpWords = 35;

    public VoiceState State { get; private set; }
    /// <summary>Loudness right now, 0 to 1 (while Waiting or Hearing).</summary>
    public double Level { get; private set; }
    /// <summary>Model download progress, 0 to 1 (while GettingReady).</summary>
    public double ReadyProgress { get; private set; }
    /// <summary>True while listening for the answer to a plan card.</summary>
    public bool Answering { get; private set; }
    /// <summary>True while waiting for a follow-up in a conversation (no "Hey Sky" needed).</summary>
    public bool FollowingUp { get; private set; }
    /// <summary>The words so far, while Deepgram writes them down live.</summary>
    public string Partial { get; private set; } = "";

    public bool Active => State != VoiceState.Off;

    public event Action? Changed;

    /// <summary>
    /// Things the app does straight away when said ("take notes", "mark that"), without asking
    /// Claude; returns what to do when it's one of them (run after what was said is shown).
    /// </summary>
    public Func<string, Action?>? Commands { get; set; }

    /// <summary>Waits until the cat has finished reading its reply aloud, so it doesn't hear itself.</summary>
    public Func<Task>? Quiet { get; set; }

    /// <summary>Called as listening starts: cuts the cat off if it's still talking.</summary>
    public Action? Interrupt { get; set; }

    /// <summary>The conversation ended (quiet, or "thanks"): the bubble can step back.</summary>
    public event Action? ConversationEnded;

    void Set(VoiceState state)
    {
        State = state;
        if (state is VoiceState.Off or VoiceState.GettingReady) Level = 0;
        Changed?.Invoke();
    }

    /// <summary>
    /// Listen for a request (or, with a plan waiting, its answer). Pressing again while listening
    /// sends what was heard.
    /// </summary>
    /// <param name="followUp">A conversation's next turn: waits a few seconds for you to speak, and says nothing if you don't.</param>
    public async Task Listen(bool answering = false, bool followUp = false)
    {
        if (Active)
        {
            _mic?.Stop();
            return;
        }
        if (chat.Busy) return;
        if (!Microphone.Present())
        {
            chat.Say(new ChatProblem("I can't find a microphone. Plug one in, or check Windows' sound settings."));
            return;
        }
        Interrupt?.Invoke();
        Answering = answering;
        FollowingUp = followUp;
        _cancel = new CancellationTokenSource();
        var ct = _cancel.Token;
        var engine = Speech.ForCommands(settings, catName);
        Partial = "";
        DeepgramLive? live = null;
        Task<DeepgramLive?>? connecting = null;
        Microphone? mic = null;
        string? said = null;
        bool quiet = false;
        try
        {
            if (engine is WhisperLocal local && !local.IsDownloaded)
            {
                ReadyProgress = 0;
                Set(VoiceState.GettingReady);
                await engine.Prepare(new Progress<double>(p => { ReadyProgress = p; Changed?.Invoke(); }), ct);
            }

            Heard heard;
            // Waiting for a follow-up: only as long as the setting says. Otherwise the usual few seconds.
            double wait = followUp || answering ? Math.Max(settings.FollowUpSeconds, answering ? 7 : 3) : 7;
            // With Deepgram, Deepgram decides when you've finished; the local pause is only a backstop.
            // This listen's own microphone: _mic is only there so Stop and Finish can reach it.
            using (mic = _mic = new Microphone(pauseEnds: engine is Deepgram ? 6 : 1.9, waitForSpeech: wait))
            {
                var key = engine is Deepgram ? Deepgram.Key.Read() : null;
                var thisMic = mic;
                // Connects and starts streaming what's been heard so far (the microphone keeps it meanwhile).
                async Task<DeepgramLive?> Connect()
                {
                    try
                    {
                        var stream = await DeepgramLive.Open(key!, thisMic.SampleRate, Speech.KeyTerms(catName()), ct);
                        stream.Partial += text => Post(() => { Partial = text; Changed?.Invoke(); });
                        stream.Ended += thisMic.Stop;
                        thisMic.StreamTo(stream.Send);
                        return stream;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        // Offline or refused: keep listening as usual and let Whisper write it down.
                        Log.Error("Deepgram stream couldn't open; using Whisper", ex);
                        return null;
                    }
                }

                mic.Level += level => Post(() =>
                {
                    Level = level;
                    // About twenty updates a second is plenty for a meter.
                    if (Environment.TickCount - _lastLevelTick < 45) return;
                    _lastLevelTick = Environment.TickCount;
                    Changed?.Invoke();
                });
                mic.SpeechStarted += () => Post(() =>
                {
                    Set(VoiceState.Hearing);
                    // A follow-up connects only now that you're talking: waiting in silence sends nothing.
                    if (key != null && connecting == null) connecting = Connect();
                });
                Set(VoiceState.Waiting);
                var listening = mic.Listen(ct);
                // Asked on purpose (the mic, the shortcut, "Hey Sky"): connect straight away.
                if (key != null && !followUp) connecting = Connect();
                heard = await listening;
                if (connecting != null) live = await connecting;
            }
            if (ct.IsCancellationRequested) return;
            if (engine is Deepgram && live == null) engine = Speech.Local;
            string? streamed = live != null ? await live.Finish() : null;
            if (heard.Kind != HeardKind.Speech && string.IsNullOrWhiteSpace(streamed))
            {
                quiet = true;
                if (!answering && !followUp) chat.Say(new CatDid("\uE720", "I didn't hear anything."));
                return;
            }

            Set(VoiceState.Understanding);
            var text = streamed is { Length: > 0 } ? streamed : await Transcribe(engine, heard.Samples, ct);
            AiLog.Write("heard", text);
            if (text.Length == 0)
            {
                if (followUp) { quiet = true; return; }
                chat.Say(new CatDid("\uE720", "I couldn't make that out. Try again a little closer to the mic?"));
                return;
            }
            said = text;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Voice failed", ex);
            chat.Say(new ChatProblem(ex is System.Net.Http.HttpRequestException
                ? "I couldn't download my ears (the speech model). Check the internet connection and try again."
                : $"Voice didn't work: {ex.Message}"));
        }
        finally
        {
            if (live != null) await live.DisposeAsync();
            if (_mic == mic) _mic = null;
            _cancel = null;
            Answering = false;
            FollowingUp = false;
            Partial = "";
            if (State != VoiceState.Off) Set(VoiceState.Off);
        }
        // Only once this listen is fully over: using it may start the next one.
        if (said != null) await Use(said, fromFollowUp: followUp);
        else if (quiet && (followUp || answering)) EndConversation(quietly: true);
    }

    async Task<string> Transcribe(ISpeechToText engine, float[] samples, CancellationToken ct)
    {
        var hint = $"{catName()}. Convert, merge, compress, shrink, PDF, JPG, PNG, WEBP, MP4, MP3, GIF, Word, zip, Downloads, Desktop, Documents, screenshot, photos, videos.";
        try
        {
            return await engine.Transcribe(samples, hint, ct);
        }
        catch (Exception ex) when (engine is not WhisperLocal && ex is System.Net.Http.HttpRequestException or DeepgramException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Log.Error($"{engine.Name} failed; using Whisper", ex);
            return await Speech.Local.Transcribe(samples, hint, ct);
        }
    }

    /// <summary>Stops listening and throws away what was heard (and ends the conversation).</summary>
    public void Cancel()
    {
        _cancel?.Cancel();
        _mic?.Stop();
    }

    /// <summary>Stops listening and uses what was heard so far.</summary>
    public void Finish() => _mic?.Stop();

    /// <summary>
    /// What was said, written down: a yes or no for a waiting plan, the end of the conversation,
    /// or a request. Then, unless it ended, the cat keeps listening for the next thing.
    /// </summary>
    public async Task Use(string text, bool listenAfter = true, bool fromFollowUp = false)
    {
        // Hindi written the Roman way (Hinglish), if that's how the person writes.
        if (Speech.Hindi && settings.HindiScript == "roman") text = Hinglish.ToRoman(text);
        // A conversation is turns of you talking to the cat. A long stretch of words while it waited,
        // or turn after turn, is more likely a video or other people: stop rather than keep going.
        _turns = fromFollowUp ? _turns + 1 : 0;
        if (fromFollowUp && (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxFollowUpWords || _turns > MaxTurns))
        {
            AiLog.Write("ignored", $"{text.Split(' ').Length} words while waiting for a follow-up");
            chat.Say(new CatDid("\uE720", $"That sounded like something else talking, so I stopped listening. Say \"Hey {catName()}\" when you need me."));
            ConversationEnded?.Invoke();
            return;
        }

        // "Hey Sky, compress it" in the middle of a conversation: the name isn't part of the request.
        var (named, after) = WakeMatcher.Match(text, catName(), settings.WakeVariants);
        if (named)
        {
            if (after.Length == 0) { if (listenAfter) Continue(); return; }
            text = after;
        }

        if (Commands?.Invoke(text) is { } command)
        {
            chat.Say(new YouSaid(text, spoken: true));
            command();
            if (listenAfter) Continue();
            return;
        }

        if (chat.WaitingPlan is { } plan && Answer(text) is { } yes)
        {
            chat.Say(new YouSaid(text, spoken: true));
            if (yes) await chat.Confirm(plan);
            else chat.Decline(plan);
            if (IsGoodbye(text)) { EndConversation(); return; }
        }
        else if (IsGoodbye(text))
        {
            chat.Say(new YouSaid(text, spoken: true));
            EndConversation();
            return;
        }
        else await chat.Send(text, spoken: true);

        if (listenAfter) Continue();
    }

    /// <summary>The next turn: a yes or no for a plan card, or whatever comes next.</summary>
    async void Continue()
    {
        // Listening while the reply is read aloud would hear the cat itself.
        if (Quiet != null) await Quiet();
        if (Active || chat.Busy) return;
        if (settings.VoiceAnswers && chat.WaitingPlan != null) _ = Listen(answering: true);
        else if (settings.FollowUpSeconds > 0) _ = Listen(followUp: true);
        else ConversationEnded?.Invoke();
    }

    void EndConversation(bool quietly = false)
    {
        if (!quietly) chat.Say(new CatDid("\uE720", $"Okay. Say \"Hey {catName()}\" when you need me."));
        AiLog.Write("ended", quietly ? "conversation (quiet)" : "conversation (said so)");
        ConversationEnded?.Invoke();
    }

    static readonly string[] Goodbyes = ["thanks", "thank you", "thanks a lot", "thank you so much", "that's all", "thats all", "that's it", "thats it", "that is all",
        "stop listening", "bye", "goodbye", "good bye", "see you", "never mind", "nevermind", "nothing", "nothing else", "i'm done", "im done", "done", "all done"];

    /// <summary>"Thanks", "that's all", "stop listening"… on its own (with "Sky" or "no" around it), not "thanks, now compress it".</summary>
    public static bool IsGoodbye(string text)
    {
        if (Hinglish.IsGoodbye(text)) return true;
        var words = Regex.Replace(text.ToLowerInvariant(), @"[^a-z' ]", " ");
        var list = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (list.Count == 0 || list.Count > 6) return false;
        // Leading "no", "okay", and the cat's name at the end, don't change the meaning.
        while (list.Count > 1 && list[0] is "no" or "ok" or "okay" or "alright" or "great" or "cool") list.RemoveAt(0);
        var phrase = string.Join(" ", list);
        return Goodbyes.Any(g => phrase == g || phrase.StartsWith(g + " ") && phrase.Split(' ').Length - g.Split(' ').Length <= 1);
    }

    static readonly string[] Yes = ["yes", "yeah", "yep", "yup", "sure", "ok", "okay", "do it", "go ahead", "go for it", "please do", "confirm", "sounds good", "alright", "all right", "correct", "perfect", "great", "do that", "send", "send it", "send it now", "send that", "go"];
    static readonly string[] YesStarts = ["yes ", "yeah ", "yep ", "yup ", "sure ", "ok ", "okay ", "do it ", "go ahead ", "go for it ", "send it "];
    static readonly string[] No = ["no", "nope", "nah", "not now", "cancel", "stop", "don't", "dont", "no thanks", "no thank you", "never mind", "nevermind", "not yet"];

    /// <summary>
    /// True for a yes, false for a no, null when it's something else. "No, make it a JPG" is null:
    /// it's a new request, which sets the card aside by itself.
    /// </summary>
    public static bool? Answer(string text)
    {
        if (Hinglish.Answer(text) is { } hinglish) return hinglish;
        var words = Regex.Replace(text.ToLowerInvariant(), @"[^a-z' ]", " ");
        words = string.Join(" ", words.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (words.Length == 0 || words.Split(' ').Length > 5) return null;
        if (No.Contains(words)) return false;
        if (Yes.Contains(words) || YesStarts.Any(words.StartsWith)) return true;
        return null;
    }

    void Post(Action action) => _ui.Post(_ => action(), null);
}
