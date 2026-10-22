using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConvertedBro.Core.Ai;

namespace ConvertedBro.Core.Voice;

public enum VoiceState { Off, GettingReady, Waiting, Hearing, Understanding }

/// <summary>
/// Talking to the cat: listen once, turn it into text, and hand it to the chat. A plain "yes" or
/// "no" while a plan card waits answers the card instead. After a spoken request that ends in a
/// plan, it listens once more for that yes or no, so the whole thing works hands-free.
/// Raises Changed on the thread it was made on (the UI thread).
/// </summary>
public sealed class VoiceInput(CatChat chat, Settings settings, Func<string> catName)
{
    readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    Microphone? _mic;
    CancellationTokenSource? _cancel;
    int _lastLevelTick;

    public VoiceState State { get; private set; }
    /// <summary>Loudness right now, 0 to 1 (while Waiting or Hearing).</summary>
    public double Level { get; private set; }
    /// <summary>Model download progress, 0 to 1 (while GettingReady).</summary>
    public double ReadyProgress { get; private set; }
    /// <summary>True while listening for the answer to a plan card.</summary>
    public bool Answering { get; private set; }

    public bool Active => State != VoiceState.Off;

    public event Action? Changed;

    void Set(VoiceState state)
    {
        State = state;
        if (state is VoiceState.Off or VoiceState.GettingReady) Level = 0;
        Changed?.Invoke();
    }

    /// <summary>Listen for a request (or, with a plan waiting, its answer). Pressing again while listening sends what was heard.</summary>
    public async Task Listen(bool answering = false)
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
        Answering = answering;
        _cancel = new CancellationTokenSource();
        var ct = _cancel.Token;
        var engine = Speech.ForCommands(settings);
        try
        {
            if (engine is WhisperLocal && !WhisperLocal.Downloaded)
            {
                ReadyProgress = 0;
                Set(VoiceState.GettingReady);
                await engine.Prepare(new Progress<double>(p => { ReadyProgress = p; Changed?.Invoke(); }), ct);
            }

            Heard heard;
            using (_mic = new Microphone())
            {
                _mic.Level += level => Post(() =>
                {
                    Level = level;
                    // About twenty updates a second is plenty for a meter.
                    if (Environment.TickCount - _lastLevelTick < 45) return;
                    _lastLevelTick = Environment.TickCount;
                    Changed?.Invoke();
                });
                _mic.SpeechStarted += () => Post(() => Set(VoiceState.Hearing));
                Set(VoiceState.Waiting);
                heard = await _mic.Listen(ct);
            }
            _mic = null;
            if (ct.IsCancellationRequested) return;
            if (heard.Kind != HeardKind.Speech)
            {
                if (!answering) chat.Say(new CatDid("\uE720", "I didn't hear anything."));
                return;
            }

            Set(VoiceState.Understanding);
            var hint = $"{catName()}. Convert, merge, compress, shrink, PDF, JPG, PNG, WEBP, MP4, MP3, GIF, Word, zip, Downloads, Desktop, Documents, screenshot, photos, videos.";
            var text = await engine.Transcribe(heard.Samples, hint, ct);
            AiLog.Write("heard", text);
            if (text.Length == 0)
            {
                chat.Say(new CatDid("\uE720", "I couldn't make that out. Try again a little closer to the mic?"));
                return;
            }
            Set(VoiceState.Off);
            await Use(text);
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
            _mic = null;
            _cancel = null;
            Answering = false;
            if (State != VoiceState.Off) Set(VoiceState.Off);
        }
    }

    /// <summary>Stops listening and throws away what was heard.</summary>
    public void Cancel()
    {
        _cancel?.Cancel();
        _mic?.Stop();
    }

    /// <summary>Stops listening and uses what was heard so far.</summary>
    public void Finish() => _mic?.Stop();

    /// <summary>What was said, written down: a yes or no for a waiting plan, or a request.</summary>
    public async Task Use(string text, bool listenForAnswer = true)
    {
        if (chat.WaitingPlan is { } plan)
        {
            switch (Answer(text))
            {
                case true:
                    chat.Say(new YouSaid(text, spoken: true));
                    await chat.Confirm(plan);
                    return;
                case false:
                    chat.Say(new YouSaid(text, spoken: true));
                    chat.Decline(plan);
                    return;
            }
        }
        await chat.Send(text, spoken: true);
        // A plan from a spoken request: listen for its answer straight away.
        if (listenForAnswer && settings.VoiceAnswers && chat.WaitingPlan != null && !Active)
            _ = Listen(answering: true);
    }

    static readonly string[] Yes = ["yes", "yeah", "yep", "yup", "sure", "ok", "okay", "do it", "go ahead", "go for it", "please do", "confirm", "sounds good", "alright", "all right", "correct", "perfect", "great", "do that"];
    static readonly string[] YesStarts = ["yes ", "yeah ", "yep ", "yup ", "sure ", "ok ", "okay ", "do it ", "go ahead ", "go for it "];
    static readonly string[] No = ["no", "nope", "nah", "not now", "cancel", "stop", "don't", "dont", "no thanks", "no thank you", "never mind", "nevermind", "not yet"];

    /// <summary>
    /// True for a yes, false for a no, null when it's something else. "No, make it a JPG" is null:
    /// it's a new request, which sets the card aside by itself.
    /// </summary>
    public static bool? Answer(string text)
    {
        var words = Regex.Replace(text.ToLowerInvariant(), @"[^a-z' ]", " ");
        words = string.Join(" ", words.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (words.Length == 0 || words.Split(' ').Length > 5) return null;
        if (No.Contains(words)) return false;
        if (Yes.Contains(words) || YesStarts.Any(words.StartsWith)) return true;
        return null;
    }

    void Post(Action action) => _ui.Post(_ => action(), null);
}
