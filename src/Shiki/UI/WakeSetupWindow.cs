using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Core;
using Shiki.Core.Voice;

namespace Shiki.UI;

/// <summary>
/// Meet your cat: give it a name, then say "Hey &lt;name&gt;" three times. Nothing is trained in
/// the machine-learning sense; the cat remembers how Whisper writes the name in your voice
/// ("hey lottie", "hey latte") and listens for those. Then try it once.
/// </summary>
public sealed class WakeSetupWindow : Window
{
    const int Takes = 3;

    readonly Palette _p = Theme.Editor;
    readonly TextBox _name = new();
    readonly TextBlock[] _heard = new TextBlock[Takes];
    readonly TextBlock _status = UiKit.Body("");
    readonly TextBlock _sayHeading;
    readonly Border _start, _test, _save;
    readonly List<string> _learned = [];
    readonly Action<string, List<string>, bool> _onSave;
    bool _listenOn = true, _busy;
    CancellationTokenSource? _cancel;
    Microphone? _mic;

    public WakeSetupWindow(string catName, IReadOnlyList<string> learned, bool wakeOn, Action<string, List<string>, bool> onSave)
    {
        _onSave = onSave;
        _learned.AddRange(learned);
        _listenOn = wakeOn || learned.Count == 0;
        Title = "Teach your cat its name";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        stack.Children.Add(UiKit.Title("Teach your cat its name"));
        bool cloud = Speech.WakeOnDeepgram && Deepgram.HasKey;
        stack.Children.Add(UiKit.Body("Say \"Hey\" and its name, and it wakes up and listens, no keys needed. It learns how the name sounds in your voice."
            + (cloud ? " The wake word is set to Deepgram, so these takes are written down by Deepgram too." : " Everything here happens on this PC.")));

        stack.Children.Add(UiKit.Heading("Its name"));
        _name.Text = catName;
        _name.MaxLength = 20;
        stack.Children.Add(UiKit.Field(_name));
        var tip = UiKit.Body("Two clear syllables work best: Latte, Mochi, Biscuit. Very short or very common words wake it by mistake more often.", 11.5);
        stack.Children.Add(tip);

        _sayHeading = UiKit.Heading("");
        stack.Children.Add(_sayHeading);
        for (int i = 0; i < Takes; i++)
        {
            _heard[i] = new TextBlock { FontFamily = Theme.TextFont, FontSize = 14, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(_heard[i]);
        }
        _status.Margin = new Thickness(0, 10, 0, 10);
        stack.Children.Add(_status);
        _start = UiKit.Button("Start", () => _ = Teach(), primary: true);
        _test = UiKit.Button("Try it", () => _ = Test());
        stack.Children.Add(UiKit.Row(_start, _test));

        stack.Children.Add(UiKit.Heading("Listening"));
        Border? toggle = null;
        toggle = UiKit.Button(_listenOn ? "On" : "Off", () =>
        {
            _listenOn = !_listenOn;
            ((TextBlock)toggle!.Child).Text = _listenOn ? "On" : "Off";
            UiKit.Select(toggle, _listenOn);
        });
        toggle.MinWidth = 64;
        UiKit.Select(toggle, _listenOn);
        var line = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        DockPanel.SetDock(toggle, Dock.Right);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(12, 0, 0, 0);
        line.Children.Add(toggle);
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = "Listen for \"Hey\" and its name", FontFamily = Theme.TextFont, FontSize = 14, Foreground = Theme.Brush(_p.Text) });
        words.Children.Add(UiKit.Body(cloud
            ? "The microphone stays on (Windows shows its mic icon). This PC notices when someone talks, and only those short bits of speech go to Deepgram to check for the name, including anyone else talking nearby. It pauses while you record the screen."
            : "The microphone stays on (Windows shows its mic icon), but what it hears stays on this PC: short bits of speech are checked for the name and then forgotten. It pauses while you record the screen.", 11.5));
        line.Children.Add(words);
        stack.Children.Add(line);

        _save = UiKit.Button("Save", Save, primary: true);
        var cancel = UiKit.Button("Cancel", Close);
        var buttons = UiKit.Row(_save, cancel);
        buttons.Margin = new Thickness(0, 22, 0, 0);
        stack.Children.Add(buttons);
        Content = stack;

        _name.TextChanged += (_, _) =>
        {
            // The takes were for the old name.
            _learned.Clear();
            Refresh();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (_busy) Stop(); else Close(); } };
        Closed += (_, _) => Stop();
        Refresh();
    }

    /// <summary>True while it's using the microphone (the always-on listener steps aside).</summary>
    public bool Listening => _busy;

    string CatName => _name.Text.Trim();

    void Refresh()
    {
        _sayHeading.Text = $"Say \"Hey {(CatName.Length > 0 ? CatName : "…")}\" three times";
        for (int i = 0; i < Takes; i++)
        {
            bool done = i < _learned.Count;
            _heard[i].Text = done ? $"{i + 1}.  Heard \"{_learned[i]}\"" : $"{i + 1}.  –";
            _heard[i].Foreground = Theme.Brush(done ? _p.Text : _p.SubtleText);
        }
        ((TextBlock)_start.Child).Text = _learned.Count >= Takes ? "Again" : "Start";
        _test.Visibility = _learned.Count >= Takes ? Visibility.Visible : Visibility.Collapsed;
        _save.Opacity = Ready ? 1 : 0.5;
        if (!_busy && _status.Text.Length == 0)
            _status.Text = _learned.Count >= Takes ? "" : "Press Start, then say it each time the cat asks.";
    }

    bool Ready => CatName.Length > 0 && _learned.Count >= Takes;

    void Say(string text, bool problem = false)
    {
        _status.Text = text;
        _status.Foreground = Theme.Brush(problem ? Color.FromRgb(0xE8, 0x5A, 0x3C) : _p.SubtleText);
    }

    async Task Teach()
    {
        if (_busy) return;
        if (CatName.Length == 0) { Say("Give your cat a name first.", true); return; }
        if (!Microphone.Present()) { Say("I can't find a microphone. Plug one in, or check Windows' sound settings.", true); return; }
        _busy = true;
        _cancel = new CancellationTokenSource();
        var ct = _cancel.Token;
        _learned.Clear();
        Refresh();
        try
        {
            await Ready_(ct);
            int misses = 0;
            while (_learned.Count < Takes && !ct.IsCancellationRequested)
            {
                Say($"Say \"Hey {CatName}\" now ({_learned.Count + 1} of {Takes})…");
                var text = await HearOnce(ct);
                if (text == null) return;
                var said = WakeMatcher.Normalize(text);
                if (said.Length == 0 || said.Split(' ').Length > 5)
                {
                    if (++misses > 4) { Say("I keep missing it. Check the microphone, then press Start again.", true); return; }
                    Say(said.Length == 0 ? "I didn't catch that. Once more?" : "That was a bit long: just \"Hey\" and the name. Once more?", true);
                    await Task.Delay(1200, ct);
                    continue;
                }
                _learned.Add(said);
                Refresh();
            }
            // Saved straight away: three good takes is the whole job.
            var unique = _learned.Distinct().ToList();
            _onSave(CatName, unique, _listenOn);
            ((TextBlock)_save.Child).Text = "Done";
            var heard = unique.Count == 1 ? $"\"{unique[0]}\"" : string.Join(", ", unique.Select(u => $"\"{u}\""));
            Say(_listenOn
                ? $"Saved. I wake up to {heard}. Say \"Hey {CatName}\" any time, or press Try it."
                : $"Saved. I'll answer to {heard} once listening is on.");
        }
        catch (OperationCanceledException)
        {
            Say("Stopped.");
        }
        catch (Exception ex)
        {
            Log.Error("Teaching the name failed", ex);
            Say(ex is System.Net.Http.HttpRequestException ? "I couldn't download the speech model. Check the internet connection." : $"That didn't work: {ex.Message}", true);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    async Task Test()
    {
        if (_busy || !Ready) return;
        _busy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            Say($"Say \"Hey {CatName}\"…");
            var text = await HearOnce(_cancel.Token);
            if (text == null) return;
            var (hit, _) = WakeMatcher.Match(text, CatName, _learned);
            Say(hit ? $"That's me! (heard \"{text}\")" : $"That didn't sound like my name: I heard \"{text}\". Press Again to teach me once more.", !hit);
        }
        catch (OperationCanceledException)
        {
            Say("Stopped.");
        }
        catch (Exception ex)
        {
            Log.Error("Testing the name failed", ex);
            Say($"That didn't work: {ex.Message}", true);
        }
        finally
        {
            _busy = false;
        }
    }

    async Task Ready_(CancellationToken ct)
    {
        if (WhisperLocal.Downloaded) return;
        await Speech.Whisper.Prepare(new Progress<double>(p => Say($"Getting my ears ready (one time, {WhisperLocal.DownloadSize})… {p:0%}")), ct);
    }

    /// <summary>One listen and what Whisper wrote down; empty when nothing was said, null when stopped.</summary>
    async Task<string?> HearOnce(CancellationToken ct)
    {
        Heard heard;
        // "Hey Sky" is short: a brief pause is enough to know it's been said.
        using (_mic = new Microphone(pauseEnds: 0.7))
            heard = await _mic.Listen(ct);
        _mic = null;
        if (ct.IsCancellationRequested) return null;
        if (heard.Kind != HeardKind.Speech) return "";
        // Heard exactly the way the wake word hears, so the spellings learned are the ones it'll see.
        return await WakeListener.Recognize(heard.Samples, CatName, ct);
    }

    void Stop()
    {
        _cancel?.Cancel();
        _mic?.Stop();
    }

    void Save()
    {
        if (!Ready)
        {
            Say(CatName.Length == 0 ? "Give your cat a name first." : "Say the name three times first (press Start).", true);
            return;
        }
        Stop();
        // Already saved after the third take; Done still picks up a change to the listening switch.
        _onSave(CatName, _learned.Distinct().ToList(), _listenOn);
        Close();
    }
}
