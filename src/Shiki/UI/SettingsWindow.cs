using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Core.Ai;
using Shiki.Shell;

namespace Shiki.UI;

/// <summary>A switch in the settings window: a label, how to read and flip it, and the tab it lives on.</summary>
public sealed record SettingSwitch(string Label, string Hint, Func<bool> IsOn, Action Toggle, string Tab = "general");

/// <summary>A global shortcut in the settings window. Set returns false when another app owns it.</summary>
public sealed record ShortcutSetting(string Label, Func<string> Get, Func<string, bool> Set);

/// <summary>Where results are saved: a folder (null keeps them next to the original), and whether to sort them.</summary>
public sealed record OutputSetting(Func<string?> GetFolder, Action<string?> SetFolder, Func<bool> GetSort, Action<bool> SetSort, Action<string> Open);

/// <summary>Ask the cat: the name it goes by, the on/off switch, what it has cost, and its log. The key itself is handled here.</summary>
public sealed record AiSetting(string CatName, Func<bool> IsOn, Action Toggle,
    Func<string> GetVoiceEngine, Action<string> SetVoiceEngine, Func<string> GetWakeEngine, Action<string> SetWakeEngine, Func<string> DeepgramUsage,
    Func<bool> VoiceAnswers, Action ToggleVoiceAnswers, Func<int> GetFollowUp, Action<int> SetFollowUp,
    Func<bool> WakeOn, Action ToggleWake, Action TeachName,
    Func<bool> VoiceLock, Action ToggleVoiceLock, Func<string> GetVoiceLockLevel, Action<string> SetVoiceLockLevel,
    Func<string> GetSpeechLanguage, Action<string> SetSpeechLanguage, Func<string> GetHindiScript, Action<string> SetHindiScript,
    Func<string> GetTalkBack, Action<string> SetTalkBack, Func<string> GetTalkVoice, Action<string> SetTalkVoice, Func<string> SpokenUsage,
    Func<string> Spent, Action OpenLog, Func<string>? TalkShortcut = null);

/// <summary>The routines: run one now, or remove it. New ones are made by telling the cat.</summary>
public sealed record RoutineSetting(string CatName, Func<IReadOnlyList<Core.Routine>> All, Action<Core.Routine> Run, Action<string> Remove,
    Func<IReadOnlyList<string>> Remembered, Action<string> Forget, Func<IReadOnlyList<Core.AutoRule>> Automatic, Action<string> StopAutomatic);

/// <summary>The private index of what's inside documents: on or off, how far it's got, and starting over.</summary>
public sealed record IndexSetting(Func<bool> IsOn, Action Toggle, Func<string> Status, Action Rebuild);

/// <summary>The cat itself: which one (of those unlocked), the look, and how much it moves about.</summary>
public sealed record CatSetting(Func<IReadOnlyList<(string Id, string Name, ImageSource Head)>> Cats, Func<string> GetCat, Action<string> SetCat,
    Func<string> GetLook, Action<string> SetLook, Func<string> GetMoves, Action<string> SetMoves);

/// <summary>Gmail through Google's API: connected or not, the person's own OAuth client, and connecting.</summary>
public sealed record GmailSetting(Func<string?> Account, Func<bool> HasClient, Action<string, string> SaveClient,
    Func<System.Threading.Tasks.Task<string>> Connect, Func<System.Threading.Tasks.Task> Disconnect, Action<string> OpenLink,
    Func<bool> HasCalendar, Func<bool> HasContacts);

/// <summary>Phones connected to Shiki: each one (and whether it's there now), connecting one, removing one, where files go.</summary>
public sealed record PhoneSetting(Func<IReadOnlyList<(string Id, string Name, string Status)>> Phones, Action Connect, Action<string> Remove,
    Func<string> Folder, Action OpenFolder);

/// <summary>
/// Settings, in tabs: General, Voice, AI and accounts, Meetings, Automation, Shortcuts. The tab
/// last open is the one that opens next time.
/// </summary>
public sealed class SettingsWindow : Window
{
    static string _tab = "general";

    /// <summary>The tab the window opens on (the last one used).</summary>
    public static string OpenTab { get => _tab; set => _tab = value; }

    readonly Palette _p = Theme.Editor;
    readonly TextBlock _error = UiKit.Error();
    (ShortcutSetting Setting, Border Chip)? _recording;

    readonly OutputSetting _output;
    readonly Border _nextTo, _oneFolder;
    readonly StackPanel _folderPart = new();
    readonly TextBlock _folderPath = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    string _lastFolder;

    public SettingsWindow(OutputSetting output, IReadOnlyList<ShortcutSetting> shortcuts, IReadOnlyList<SettingSwitch> switches, AiSetting? ai = null, RoutineSetting? routines = null, GmailSetting? gmail = null, CatSetting? cat = null, IndexSetting? index = null, PhoneSetting? phone = null)
    {
        _output = output;
        _lastFolder = output.GetFolder() ?? Core.OutputPaths.DefaultFolder;
        Title = "Shiki settings";
        Width = 680;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        root.Children.Add(UiKit.Title("Settings"));
        var tabRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        root.Children.Add(tabRow);
        var pages = new List<(string Id, string Label, StackPanel Page)>();
        StackPanel Tab(string id, string label)
        {
            var tabPage = new StackPanel();
            pages.Add((id, label, tabPage));
            return tabPage;
        }
        IEnumerable<SettingSwitch> On(string tab) => switches.Where(s => s.Tab == tab);

        // ---- General: the cat, files, Windows ----
        var stack = Tab("general", "General");
        if (cat != null) AddCat(stack, cat);
        foreach (var setting in On("cat")) stack.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));

        stack.Children.Add(UiKit.Heading("Files"));
        stack.Children.Add(Line("Where results go", "Converted, compressed and edited files.", new Border()));
        _nextTo = UiKit.Button("Next to the original", () => SetMode(false));
        _oneFolder = UiKit.Button("One folder", () => SetMode(true));
        var modes = UiKit.Row(_nextTo, _oneFolder);
        modes.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(modes);
        _folderPath.FontFamily = Theme.TextFont;
        _folderPath.FontSize = 13;
        _folderPath.Foreground = Theme.Brush(_p.Text);
        var pathBox = new Border
        {
            Background = Theme.Brush(_p.RowHover),
            CornerRadius = new CornerRadius(_p.Pixel ? 0 : 8),
            Padding = new Thickness(10, 7, 10, 8),
            Margin = new Thickness(0, 4, 0, 8),
            Child = _folderPath,
        };
        _folderPart.Children.Add(pathBox);
        _folderPart.Children.Add(UiKit.Row(UiKit.Button("Change…", PickFolder), UiKit.Button("Open", () => _output.Open(_lastFolder))));
        var sort = new SettingSwitch("Sort by type", "Images, Videos, Audio, PDFs and Archives each get their own folder.",
            _output.GetSort, () => _output.SetSort(!_output.GetSort()));
        _folderPart.Children.Add(Line(sort.Label, sort.Hint, Toggle(sort)));
        stack.Children.Add(_folderPart);
        ShowMode();
        if (index != null)
        {
            Grid? indexLine = null;
            stack.Children.Add(Line("Search inside files", "\"The PDF where I signed the rental agreement\". A private index of Documents, Desktop and Downloads, only on this PC.",
                Toggle(new SettingSwitch("", "", index.IsOn, () => { index.Toggle(); SetHint(indexLine!, index.Status()); }))));
            indexLine = Line("The index", index.Status(), UiKit.Button("Read again", () => { index.Rebuild(); SetHint(indexLine!, index.Status()); }));
            indexLine.Margin = new Thickness(16, 0, 0, 6);
            stack.Children.Add(indexLine);
        }
        foreach (var setting in On("files")) stack.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));

        stack.Children.Add(UiKit.Heading("Windows"));
        foreach (var setting in On("general")) stack.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));

        // ---- Voice ----
        if (ai != null) AddVoice(Tab("voice", "Voice"), ai, On("voiceadvanced"));

        // ---- Automation: how the cat goes about things by itself ----
        var automation = Tab("automation", "Automation");
        if (On("sending").Any())
        {
            automation.Children.Add(UiKit.Heading("Sending"));
            foreach (var setting in On("sending")) automation.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));
        }
        if (On("day").Any())
        {
            automation.Children.Add(UiKit.Heading("Your day"));
            foreach (var setting in On("day")) automation.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));
        }
        if (On("meetings").Any())
        {
            automation.Children.Add(UiKit.Heading("Meetings"));
            foreach (var setting in On("meetings")) automation.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));
        }
        if (routines != null) AddRoutines(automation, routines);

        // ---- Accounts: Claude, Google, your phone ----
        var accounts = Tab("accounts", "Accounts");
        if (ai != null) AddAi(accounts, ai);
        foreach (var setting in On("privacy")) accounts.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));
        if (ai != null)
        {
            var row = UiKit.Row(UiKit.Button("Open the cat's log", ai.OpenLog));
            accounts.Children.Add(Line(ai.Spent(), "Everything the cat was asked and did, in plain words.", row));
        }
        if (gmail != null) AddGmail(accounts, gmail);
        if (phone != null) AddPhone(accounts, phone);

        // ---- Shortcuts ----
        var keys = Tab("shortcuts", "Shortcuts");
        keys.Children.Add(UiKit.Heading("Shortcuts"));
        keys.Children.Add(UiKit.Body("They work everywhere in Windows. Click one, then press the keys you want."));
        foreach (var shortcut in shortcuts)
        {
            Border? chip = null;
            chip = UiKit.Button(shortcut.Get(), () => StartRecording(shortcut, chip!));
            keys.Children.Add(Line(shortcut.Label, "", chip));
        }
        keys.Children.Add(_error);

        // Each section becomes a card of its own, so a tab reads as a few clear groups.
        foreach (var (_, _, tabPage) in pages) Group(tabPage);

        // The tabs: one page shows at a time.
        var shownPages = new StackPanel();
        var chips = new List<(string Id, Border Chip)>();
        void ShowTab(string id)
        {
            id = id switch { "ai" or "phone" => "accounts", "meetings" => "automation", _ => id };
            if (pages.All(p => p.Id != id)) id = pages[0].Id;
            _tab = id;
            foreach (var (pageId, _, page) in pages) page.Visibility = pageId == id ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (chipId, chip) in chips) UiKit.Select(chip, chipId == id);
        }
        foreach (var (id, label, page) in pages.Where(p => p.Page.Children.Count > 0))
        {
            var chip = UiKit.Button(label, () => ShowTab(id));
            chip.Margin = new Thickness(0, 0, 6, 6);
            chips.Add((id, chip));
            tabRow.Children.Add(chip);
            shownPages.Children.Add(page);
        }
        pages.RemoveAll(p => p.Page.Children.Count == 0);
        root.Children.Add(shownPages);
        ShowTab(_tab);

        var done = UiKit.Button("Done", Close, primary: true);
        done.Margin = new Thickness(0, 22, 0, 0);
        root.Children.Add(done);
        Content = root;

        PreviewKeyDown += OnKey;
    }

    void AddAi(StackPanel stack, AiSetting ai)
    {
        stack.Children.Add(UiKit.Heading($"Ask {ai.CatName} (Claude)"));
        stack.Children.Add(UiKit.Body($"{ai.CatName} understands you with Claude Haiku 5.5 and your own API key. Only what a request needs is sent; nothing while this is off.", 11.5));
        stack.Children.Add(Line($"Ask {ai.CatName}", "The Ask page, voice, and the Ask shortcut.", Toggle(new SettingSwitch("", "", ai.IsOn, ai.Toggle))));

        var status = UiKit.Body("");
        status.Margin = new Thickness(0, 10, 0, 6);
        var key = new PasswordBox();
        var field = UiKit.Field(key);
        field.Width = 300;
        // The buttons need Save and Show, which need the buttons: wire them up once both exist.
        Action saveKey = () => { }, showKey = () => { };
        var save = UiKit.Button("Save", () => saveKey());
        var remove = UiKit.Button("Remove", () => { ClaudeKey.Remove(); showKey(); });
        void Show()
        {
            status.Text = ClaudeKey.Hint is { } end ? $"API key saved (ends in {end}), kept in Windows Credential Manager." : "No API key yet. Create one at console.anthropic.com, then paste it here.";
            status.Foreground = Theme.Brush(_p.SubtleText);
            remove.Visibility = ClaudeKey.Exists ? Visibility.Visible : Visibility.Collapsed;
        }
        async void Save()
        {
            var value = key.Password.Trim();
            if (value.Length < 20) return;
            status.Text = "Checking the key with Claude…";
            var problem = await CatChat.TestKey(value);
            if (problem != null)
            {
                status.Text = problem;
                status.Foreground = Theme.Brush(Color.FromRgb(0xE8, 0x5A, 0x3C));
                return;
            }
            ClaudeKey.Save(value);
            key.Clear();
            Show();
        }
        saveKey = Save;
        showKey = Show;
        key.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Save(); } };
        stack.Children.Add(status);
        var row = UiKit.Row(field, save, remove);
        field.Margin = new Thickness(0, 0, 8, 0);
        stack.Children.Add(row);
        Show();
    }

    /// <summary>
    /// Voice, everyday things first: how you talk to the cat, the language, how it talks back.
    /// Which engines write your words down (and their keys) are folded away under one button.
    /// </summary>
    void AddVoice(StackPanel stack, AiSetting ai, IEnumerable<SettingSwitch> advancedSwitches)
    {
        var cat = ai.CatName;

        // ---- Talking to the cat ----
        stack.Children.Add(UiKit.Heading($"Talking to {cat}"));
        var how = UiKit.Body("");
        stack.Children.Add(how);
        var wakeGroup = new StackPanel { Margin = new Thickness(16, 0, 0, 4) };
        void ShowHow()
        {
            var shortcut = ai.TalkShortcut?.Invoke() ?? "the talk shortcut";
            how.Text = ai.WakeOn()
                ? $"Press {shortcut} and talk, or just say \"Hey {cat}\"."
                : $"Press {shortcut} anywhere and talk. The microphone is on only while {cat} is listening.";
            wakeGroup.Visibility = ai.WakeOn() ? Visibility.Visible : Visibility.Collapsed;
        }
        stack.Children.Add(Line($"Listen for \"Hey {cat}\"", "Wake the cat by saying its name. The mic then stays on (Windows shows its mic icon).",
            Toggle(new SettingSwitch("", "", ai.WakeOn, () => { ai.ToggleWake(); ShowHow(); }))));
        // Only matters while the name is listened for.
        wakeGroup.Children.Add(Line($"Only my voice wakes {cat}", Core.Voice.VoicePrint.HasPrint
                ? $"\"Hey {cat}\" from a video, a call or someone else is ignored. Checked on this PC."
                : "Teach your cat its name again to record your voice (one extra sentence).",
            Toggle(new SettingSwitch("", "", ai.VoiceLock, ai.ToggleVoiceLock))));
        wakeGroup.Children.Add(Chips("How strict", "Relaxed if it sometimes ignores you; Strict if other voices still wake it.",
            [("relaxed", "Relaxed"), ("normal", "Normal"), ("strict", "Strict")], ai.GetVoiceLockLevel, ai.SetVoiceLockLevel));
        stack.Children.Add(wakeGroup);
        ShowHow();

        stack.Children.Add(Chips("Keep listening after a reply", $"Say the next thing straight away. \"Thanks\" or \"that's all\" ends it.",
            [("0", "Off"), ("5", "5 s"), ("8", "8 s"), ("15", "15 s")],
            () => ai.GetFollowUp().ToString(), v => ai.SetFollowUp(int.Parse(v))));
        stack.Children.Add(Line("Answer cards by voice", "When a card asks to send or do something, say \"yes\" or \"no\".",
            Toggle(new SettingSwitch("", "", ai.VoiceAnswers, ai.ToggleVoiceAnswers))));
        var teach = UiKit.Button($"Rename {cat}…", ai.TeachName);
        teach.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(teach);

        // ---- Language ----
        stack.Children.Add(UiKit.Heading("Language"));
        var script = Chips("Write Hindi as", "In what you say and in dictation.",
            [("roman", "Roman (aap kaise ho)"), ("devanagari", "हिंदी")], ai.GetHindiScript, ai.SetHindiScript);
        void ShowScript() => script.Visibility = ai.GetSpeechLanguage() == "hi-en" ? Visibility.Visible : Visibility.Collapsed;
        stack.Children.Add(Chips("I speak", "Hindi + English understands Hinglish, even mixed in one sentence.",
            [("en", "English"), ("hi-en", "Hindi + English")], ai.GetSpeechLanguage, v => { ai.SetSpeechLanguage(v); ShowScript(); }));
        stack.Children.Add(script);
        ShowScript();

        // ---- Talking back ----
        stack.Children.Add(UiKit.Heading("Talking back"));
        stack.Children.Add(Chips($"{cat} reads replies aloud", "The mic waits until it's done; press the shortcut to cut it short.",
            [("spoken", "When I speak"), ("always", "Always"), ("off", "Off")], ai.GetTalkBack, ai.SetTalkBack));
        var current = Core.Voice.Speaker.Pick(ai.GetTalkVoice());
        var voiceName = new TextBlock();
        var voicePanel = new StackPanel { Visibility = Visibility.Collapsed };
        Border? changeVoice = null;
        changeVoice = UiKit.Button("Change", () =>
        {
            bool open = voicePanel.Visibility != Visibility.Visible;
            voicePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)changeVoice!.Child).Text = open ? "Done" : "Change";
        });
        var voiceLine = Line("Voice", "", changeVoice);
        stack.Children.Add(voiceLine);
        var voiceRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        var voiceNote = UiKit.Body("", 11.5);
        voicePanel.Children.Add(UiKit.Body("Pick one to hear it.", 11.5));
        voicePanel.Children.Add(voiceRow);
        voicePanel.Children.Add(voiceNote);
        stack.Children.Add(voicePanel);
        var voices = new List<(string Id, Border Chip)>();
        void ShowVoice()
        {
            foreach (var (id, chip) in voices) UiKit.Select(chip, current == id);
            var label = Core.Voice.Speaker.Voices().FirstOrDefault(v => v.Id == current)?.Label ?? "Windows voice";
            if (voiceLine.Children[0] is StackPanel text && text.Children[1] is TextBlock hint) hint.Text = label;
        }
        void FillVoices()
        {
            voiceRow.Children.Clear();
            voices.Clear();
            foreach (var voice in Core.Voice.Speaker.Voices())
            {
                var chip = UiKit.Button(voice.Label, () => { current = voice.Id; ai.SetTalkVoice(voice.Id); ShowVoice(); });
                chip.Margin = new Thickness(0, 0, 6, 6);
                voices.Add((voice.Id, chip));
                voiceRow.Children.Add(chip);
            }
            ShowVoice();
            var notes = new List<string>();
            if (Core.Voice.ElevenLabs.HasKey) notes.Add($"ElevenLabs voices use your ElevenLabs credit; if it runs out, {(Core.Voice.Deepgram.HasKey ? "Deepgram's voice carries on" : "a Windows voice carries on")}.");
            if (Core.Voice.Deepgram.HasKey) notes.Add("Deepgram's voices cost about $0.03 per 1,000 characters.");
            notes.Add("Windows voices are free and stay on this PC.");
            if (Core.Voice.Deepgram.HasKey && ai.SpokenUsage() is { Length: > 0 } spoken) notes.Add(spoken);
            voiceNote.Text = string.Join(" ", notes);
        }
        FillVoices();

        // ---- Folded away: engines and keys ----
        var advanced = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        Border? more = null;
        more = UiKit.Button("Speech engines and keys", () =>
        {
            bool open = advanced.Visibility != Visibility.Visible;
            advanced.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)more!.Child).Text = open ? "Hide speech engines and keys" : "Speech engines and keys";
        });
        more.Margin = new Thickness(0, 22, 0, 0);
        stack.Children.Add(more);
        stack.Children.Add(advanced);

        Action refresh = () => { };
        var cmdWhisper = UiKit.Button("On this PC", () => { ai.SetVoiceEngine("whisper"); refresh(); });
        var cmdDeepgram = UiKit.Button("Deepgram", () => { ai.SetVoiceEngine("deepgram"); refresh(); });
        var cmdScribe = UiKit.Button("ElevenLabs Scribe", () => { ai.SetVoiceEngine("elevenlabs"); refresh(); });
        var cmdWispr = UiKit.Button("Wispr Flow", () => { ai.SetVoiceEngine("wispr"); refresh(); });
        advanced.Children.Add(Line("Writes down what you say", "", new Border()));
        var cmdRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var chip in new[] { cmdWhisper, cmdDeepgram, cmdScribe, cmdWispr }) { chip.Margin = new Thickness(0, 0, 6, 6); cmdRow.Children.Add(chip); }
        advanced.Children.Add(cmdRow);
        var cmdNote = UiKit.Body("", 11.5);
        advanced.Children.Add(cmdNote);

        var wakePart = new StackPanel();
        var wakeWhisper = UiKit.Button("On this PC", () => { ai.SetWakeEngine("whisper"); refresh(); });
        var wakeDeepgram = UiKit.Button("Deepgram", () => { ai.SetWakeEngine("deepgram"); refresh(); });
        wakePart.Children.Add(Line($"Listens for \"Hey {cat}\"", "", new Border()));
        var wakeRow = UiKit.Row(wakeWhisper, wakeDeepgram);
        wakeRow.Margin = new Thickness(0, 0, 0, 4);
        wakePart.Children.Add(wakeRow);
        var wakeNote = UiKit.Body("", 11.5);
        wakePart.Children.Add(wakeNote);
        advanced.Children.Add(wakePart);

        advanced.Children.Add(UiKit.Heading("Keys"));
        var deepgramKey = KeyPart("Deepgram", Core.Voice.Deepgram.Key, Core.Voice.Deepgram.TestKey, () =>
        {
            if (ai.GetVoiceEngine() == "whisper") ai.SetVoiceEngine("deepgram");
            if (ai.GetWakeEngine() == "whisper") ai.SetWakeEngine("deepgram");
        }, "Create one at console.deepgram.com.");
        var elevenKey = KeyPart("ElevenLabs", Core.Voice.ElevenLabs.Key, Core.Voice.ElevenLabs.TestKey, () =>
        {
            // A new key: try its first voice straight away (it plays, so you hear it).
            if (!ai.GetTalkVoice().StartsWith("el:") && Core.Voice.ElevenLabs.Voices.FirstOrDefault() is { } first)
            {
                current = "el:" + first.Id;
                ai.SetTalkVoice(current);
            }
        }, "elevenlabs.io, Developers, API keys (Speech to Text, Text to Speech and Voices read access).");
        var wisprKey = KeyPart("Wispr", Core.Voice.WisprFlow.Key, null, null, "");
        advanced.Children.Add(deepgramKey.Root);
        advanced.Children.Add(elevenKey.Root);
        advanced.Children.Add(wisprKey.Root);
        var usage = UiKit.Body("", 11.5);
        advanced.Children.Add(usage);
        foreach (var setting in advancedSwitches) advanced.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));

        void Show()
        {
            var cmd = ai.GetVoiceEngine();
            var wake = ai.GetWakeEngine();
            bool hasDeepgram = Core.Voice.Deepgram.HasKey;
            UiKit.Select(cmdWhisper, cmd == "whisper");
            UiKit.Select(cmdDeepgram, cmd == "deepgram");
            UiKit.Select(cmdScribe, cmd == "elevenlabs");
            UiKit.Select(cmdWispr, cmd == "wispr");
            UiKit.Select(wakeWhisper, wake == "whisper");
            UiKit.Select(wakeDeepgram, wake == "deepgram");
            cmdNote.Text = cmd switch
            {
                "deepgram" => hasDeepgram
                    ? "Deepgram Nova-3 writes your words down live and judges when you've finished. If it can't be reached, Whisper on this PC takes over."
                    : "Add a Deepgram key below. Until then Whisper on this PC is used.",
                "elevenlabs" => Core.Voice.ElevenLabs.HasKey
                    ? "ElevenLabs Scribe v2 writes your words down live, careful with names and Hinglish, with background noise filtered out. If it can't be reached, Deepgram (then Whisper) takes over."
                    : "Add your ElevenLabs key below. Until then Deepgram or Whisper is used.",
                "wispr" => Core.Voice.WisprFlow.Available
                    ? "What you say goes to Wispr Flow to be written down."
                    : "Wispr Flow's API isn't open to Shiki yet. Until it is, Whisper on this PC is used.",
                _ => $"Free and private: Whisper turns your voice into text on this PC. It downloads once ({Core.Voice.WhisperLocal.DownloadSize}).",
            };
            wakePart.Visibility = ai.WakeOn() ? Visibility.Visible : Visibility.Collapsed;
            wakeNote.Text = wake == "deepgram"
                ? hasDeepgram
                    ? "Short bits of speech (under six seconds) go to Deepgram to check for the name; silence never leaves the PC."
                    : "Add a Deepgram key below. Until then Whisper on this PC listens."
                : "Free and private: short bits of speech are checked for the name on this PC.";
            wisprKey.Root.Visibility = cmd == "wispr" || Core.Voice.WisprFlow.Key.Exists ? Visibility.Visible : Visibility.Collapsed;
            usage.Text = ai.DeepgramUsage();
            usage.Visibility = usage.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        refresh = () => { Show(); ShowHow(); };
        deepgramKey.Changed += Show;
        elevenKey.Changed += () =>
        {
            current = Core.Voice.Speaker.Pick(ai.GetTalkVoice());
            FillVoices();
            Show();
        };
        Show();
        // Voices added on the ElevenLabs website show up the next time Settings opens.
        if (Core.Voice.ElevenLabs.HasKey)
            _ = Core.Voice.ElevenLabs.Refresh().ContinueWith(_ => Dispatcher.BeginInvoke(FillVoices), System.Threading.Tasks.TaskScheduler.Default);
    }

    /// <summary>A labelled row of chips, one picked; picking calls set.</summary>
    StackPanel Chips(string label, string hint, (string Id, string Label)[] options, Func<string> get, Action<string> set)
    {
        var panel = new StackPanel();
        panel.Children.Add(Line(label, hint, new Border()));
        var chips = new List<(string Id, Border Chip)>();
        void Show() { foreach (var (id, chip) in chips) UiKit.Select(chip, get() == id); }
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (id, text) in options)
        {
            var chip = UiKit.Button(text, () => { set(id); Show(); });
            chip.Margin = new Thickness(0, 0, 6, 6);
            chips.Add((id, chip));
            row.Children.Add(chip);
        }
        panel.Children.Add(row);
        Show();
        return panel;
    }

    sealed class KeyBox
    {
        public required StackPanel Root { get; init; }
        public event Action? Changed;
        public void Raise() => Changed?.Invoke();
    }

    /// <summary>
    /// A saved-or-not line, a box to paste a key in, Save and Remove. With a test, the key is
    /// checked with the service before it's saved.
    /// </summary>
    KeyBox KeyPart(string service, Core.Ai.SecretStore store, Func<string, System.Threading.Tasks.Task<string?>>? test, Action? saved, string where)
    {
        var root = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var box = new KeyBox { Root = root };
        var status = UiKit.Body("");
        var key = new PasswordBox();
        var field = UiKit.Field(key);
        field.Width = 300;
        field.Margin = new Thickness(0, 0, 8, 0);
        Action saveKey = () => { }, showKey = () => { };
        var save = UiKit.Button("Save", () => saveKey());
        var remove = UiKit.Button("Remove", () => { store.Remove(); showKey(); box.Raise(); });
        void ShowKey()
        {
            status.Text = store.Hint is { } end ? $"{service} key saved (ends in {end}), kept in Windows Credential Manager." : $"No {service} key yet. {where}".Trim();
            status.Foreground = Theme.Brush(_p.SubtleText);
            remove.Visibility = store.Exists ? Visibility.Visible : Visibility.Collapsed;
        }
        async void SaveKey()
        {
            var value = key.Password.Trim();
            if (value.Length < 10) return;
            if (test != null)
            {
                status.Text = $"Checking the key with {service}…";
                if (await test(value) is { } problem)
                {
                    status.Text = problem;
                    status.Foreground = Theme.Brush(Color.FromRgb(0xE8, 0x5A, 0x3C));
                    return;
                }
            }
            store.Save(value);
            key.Clear();
            saved?.Invoke();
            ShowKey();
            box.Raise();
        }
        saveKey = SaveKey;
        showKey = ShowKey;
        key.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; SaveKey(); } };
        root.Children.Add(status);
        var row = UiKit.Row(field, save, remove);
        row.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(row);
        ShowKey();
        return box;
    }

    void SetMode(bool oneFolder)
    {
        _output.SetFolder(oneFolder ? _lastFolder : null);
        ShowMode();
    }

    void ShowMode()
    {
        bool oneFolder = _output.GetFolder() != null;
        UiKit.Select(_nextTo, !oneFolder);
        UiKit.Select(_oneFolder, oneFolder);
        _folderPart.Visibility = oneFolder ? Visibility.Visible : Visibility.Collapsed;
        _folderPath.Text = _lastFolder;
        _folderPath.ToolTip = _lastFolder;
    }

    void PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Where should results go?", InitialDirectory = Directory.Exists(_lastFolder) ? _lastFolder : null };
        if (dialog.ShowDialog(GetWindow(_folderPath) ?? this) != true) return;
        _lastFolder = dialog.FolderName;
        _output.SetFolder(_lastFolder);
        ShowMode();
    }

    void AddCat(StackPanel stack, CatSetting cat)
    {
        stack.Children.Add(UiKit.Heading("The cat"));
        // A row of chips, one picked.
        void Choice(string label, string hint, IEnumerable<(string Id, string Label, ImageSource? Icon)> options, Func<string> get, Action<string> set)
        {
            stack.Children.Add(Line(label, hint, new Border()));
            var chips = new List<(string Id, Border Chip)>();
            void Show() { foreach (var (id, chip) in chips) UiKit.Select(chip, get() == id); }
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (id, text, icon) in options)
            {
                var chip = UiKit.Button(text, () => { set(id); Show(); });
                if (icon != null && chip.Child is FrameworkElement content)
                {
                    // The cat's face beside its name.
                    var face = new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
                    chip.Child = null;
                    chip.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { face, content } };
                }
                chip.Margin = new Thickness(0, 0, 6, 6);
                chips.Add((id, chip));
                row.Children.Add(chip);
            }
            stack.Children.Add(row);
            Show();
        }
        Choice("Which cat", "More cats come as it levels up with use.", cat.Cats().Select(c => (c.Id, c.Name, (ImageSource?)c.Head)), cat.GetCat, cat.SetCat);
        Choice("Look", "Menus and popups: pixel arcade, or liquid glass.", [("arcade", "Arcade", null), ("glass", "Glass", null)], cat.GetLook, cat.SetLook);
        Choice("Moves", "Lively wanders and stretches; Calm now and then; Still stays put.", [("lively", "Lively", null), ("calm", "Calm", null), ("still", "Still", null)], cat.GetMoves, cat.SetMoves);
    }

    void AddGmail(StackPanel stack, GmailSetting gmail)
    {
        stack.Children.Add(UiKit.Heading("Google: Gmail, Calendar, Contacts"));
        var part = new StackPanel();
        stack.Children.Add(part);
        void Fill()
        {
            part.Children.Clear();
            if (gmail.Account() is { } account)
            {
                Border? disconnect = null;
                disconnect = UiKit.Button("Disconnect", async () =>
                {
                    await gmail.Disconnect();
                    Fill();
                });
                part.Children.Add(Line($"Connected: {account}", "Searching, reading and drafting take a moment, with no browser. Sending always shows you the email first.", disconnect));
                if (gmail.HasCalendar() && gmail.HasContacts())
                {
                    part.Children.Add(UiKit.Body("Calendar and Contacts are on too: \"what's on today?\", \"add dentist tomorrow at 5\", and people found by name.", 11.5));
                    return;
                }
                // Connected before Calendar and Contacts came along: two APIs to switch on, then connect again.
                part.Children.Add(UiKit.Body("Calendar and Contacts need two things, once:\n"
                    + "1. In Google Cloud, APIs & Services, Library: find Google Calendar API and press Enable, then People API and press Enable.\n"
                    + "2. Press Connect again below and allow the new permissions.", 11.5));
                var again = UiKit.Button("Connect again…", async () =>
                {
                    try { await gmail.Connect(); } catch (Exception) { }
                    Fill();
                }, primary: true);
                var cloud = UiKit.Button("Open Google Cloud", () => gmail.OpenLink("https://console.cloud.google.com/apis/library/calendar-json.googleapis.com"));
                var buttons = UiKit.Row(again, cloud);
                buttons.Margin = new Thickness(0, 8, 0, 0);
                part.Children.Add(buttons);
                return;
            }
            part.Children.Add(UiKit.Body("Connect Gmail and the cat finds, reads and answers your email in a moment, without opening the browser. Google asks you to sign in on its own page; Shiki never sees your password, and you can disconnect any time.", 11.5));
            var status = UiKit.Body("", 11.5);
            if (gmail.HasClient())
            {
                var connect = UiKit.Button("Connect Gmail…", async () =>
                {
                    status.Text = "Finish signing in on Google's page in your browser…";
                    try
                    {
                        var address = await gmail.Connect();
                        Fill();
                    }
                    catch (Exception ex)
                    {
                        status.Text = ex.Message;
                    }
                }, primary: true);
                connect.Margin = new Thickness(0, 8, 0, 4);
                part.Children.Add(connect);
                part.Children.Add(status);
                var change = UiKit.Button("Use a different Google client", () => { gmail.SaveClient("", ""); Fill(); });
                change.Margin = new Thickness(0, 4, 0, 0);
                part.Children.Add(change);
                return;
            }
            // One time: the person's own OAuth client from Google Cloud.
            part.Children.Add(UiKit.Body("First, one time (about 10 minutes), make Shiki's key in your Google account:\n"
                + "1. Open Google Cloud and create a project called Shiki.\n"
                + "2. APIs & Services, Library: find Gmail API and press Enable.\n"
                + "3. OAuth consent screen: External, app name Shiki, your email. Add your Gmail under Test users, then press Publish app (otherwise Google signs you out every 7 days; it will say the app isn't verified, which is fine for your own use).\n"
                + "4. Credentials, Create credentials, OAuth client ID, type Desktop app. Copy the Client ID and Client secret into the boxes below.", 11.5));
            var open = UiKit.Button("Open Google Cloud", () => gmail.OpenLink("https://console.cloud.google.com/apis/library/gmail.googleapis.com"));
            open.Margin = new Thickness(0, 8, 0, 8);
            part.Children.Add(open);
            var id = new TextBox();
            var secret = new PasswordBox();
            var idField = UiKit.Field(id);
            idField.Margin = new Thickness(0, 0, 0, 6);
            part.Children.Add(UiKit.Body("Client ID", 11.5));
            part.Children.Add(idField);
            part.Children.Add(UiKit.Body("Client secret", 11.5));
            var secretField = UiKit.Field(secret);
            secretField.Margin = new Thickness(0, 0, 0, 8);
            part.Children.Add(secretField);
            var save = UiKit.Button("Save", () =>
            {
                if (!id.Text.Trim().EndsWith(".apps.googleusercontent.com") || secret.Password.Trim().Length < 10)
                {
                    status.Text = "That doesn't look like a Desktop app client: the ID ends in .apps.googleusercontent.com.";
                    return;
                }
                gmail.SaveClient(id.Text, secret.Password);
                Fill();
            }, primary: true);
            part.Children.Add(save);
            part.Children.Add(status);
        }
        Fill();
    }

    void AddRoutines(StackPanel stack, RoutineSetting routines)
    {
        stack.Children.Add(UiKit.Heading("Routines"));
        stack.Children.Add(UiKit.Body($"Say a routine's phrase after \"Hey {routines.CatName}\" and it all happens at once. To make your own, tell {routines.CatName} something like \"when I say meeting, turn on do not disturb and open Zoom\".", 11.5));
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Fill()
        {
            list.Children.Clear();
            foreach (var routine in routines.All())
            {
                var phrase = routine.Phrases.FirstOrDefault() ?? routine.Name;
                var steps = string.Join(", ", routine.Steps.Select(Core.Routines.Describe));
                var buttons = UiKit.Row(UiKit.Button("Run", () => routines.Run(routine)), UiKit.Button("Remove", () => { routines.Remove(routine.Name); Fill(); }));
                list.Children.Add(Line(routine.Name, $"Say \"{phrase}\": {steps}", buttons));
            }
            if (list.Children.Count == 0) list.Children.Add(UiKit.Body("No routines yet.", 12));
        }
        Fill();
        stack.Children.Add(list);

        // What it remembers, and what it does by itself.
        stack.Children.Add(UiKit.Heading($"What {routines.CatName} remembers"));
        stack.Children.Add(UiKit.Body($"Tell {routines.CatName} \"remember that my PDFs should be under 2 MB\" and it keeps it in mind. When you keep converting the same kind of download the same way, it offers to do it by itself.", 11.5));
        var memory = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        void FillMemory()
        {
            memory.Children.Clear();
            foreach (var fact in routines.Remembered())
                memory.Children.Add(Line(fact, "Remembered", UiKit.Button("Forget", () => { routines.Forget(fact); FillMemory(); })));
            foreach (var rule in routines.Automatic())
                memory.Children.Add(Line(Core.Habits.Describe(rule), $"Done by itself since {rule.Since:d MMM}", UiKit.Button("Stop", () => { routines.StopAutomatic(rule.Extension); FillMemory(); })));
            if (memory.Children.Count == 0) memory.Children.Add(UiKit.Body("Nothing yet.", 12));
        }
        FillMemory();
        stack.Children.Add(memory);
    }

    /// <summary>
    /// Gathers what follows each heading into a rounded card, with a thin line between its
    /// settings. Anything before the first heading stays as it is.
    /// </summary>
    void Group(StackPanel page)
    {
        var items = page.Children.Cast<UIElement>().ToList();
        page.Children.Clear();
        StackPanel? card = null;
        foreach (var item in items)
        {
            if (item is TextBlock { Tag: "heading" } heading)
            {
                heading.Margin = new Thickness(4, page.Children.Count == 0 ? 6 : 28, 0, 10);
                page.Children.Add(heading);
                card = new StackPanel();
                page.Children.Add(new Border
                {
                    Background = Theme.Brush(_p.WedgeRest),
                    BorderBrush = Theme.Brush(_p.SurfaceBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(_p.Pixel ? 0 : 16),
                    Padding = new Thickness(20, 10, 20, 14),
                    Child = card,
                });
                continue;
            }
            if (card == null) { page.Children.Add(item); continue; }
            // A setting (its name and switch, or its name and choices) gets a line above it.
            bool setting = item is Grid || item is StackPanel { Children.Count: > 0 } panel && panel.Children[0] is Grid;
            if (setting && card.Children.Count > 0)
                card.Children.Add(new Border { Height = 1, Background = Theme.Brush(_p.SurfaceBorder), Margin = new Thickness(0, 6, 0, 6) });
            card.Children.Add(item);
        }
    }

    /// <summary>Changes the small grey line under a setting's name.</summary>
    static void SetHint(Grid line, string hint)
    {
        if (line.Children[0] is StackPanel text && text.Children[1] is TextBlock sub) sub.Text = hint;
    }

    Grid Line(string label, string hint, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, FontFamily = Theme.TextFont, FontSize = 14, Foreground = Theme.Brush(_p.Text) });
        var sub = UiKit.Body(hint, 11.5);
        sub.Margin = new Thickness(0, 2, 12, 0);
        text.Children.Add(sub);
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(0); }
        grid.Children.Add(control);
        return grid;
    }

    Border Toggle(SettingSwitch setting)
    {
        Border? chip = null;
        chip = UiKit.Button(setting.IsOn() ? "On" : "Off", () =>
        {
            setting.Toggle();
            Paint(chip!, setting.IsOn());
        });
        chip.MinWidth = 64;
        Paint(chip, setting.IsOn());
        return chip;
    }

    void Paint(Border chip, bool on)
    {
        ((TextBlock)chip.Child).Text = on ? "On" : "Off";
        UiKit.Select(chip, on);
    }

    void StartRecording(ShortcutSetting setting, Border chip)
    {
        if (_recording is { } previous) Finish(previous.Setting.Get());
        _recording = (setting, chip);
        ((TextBlock)chip.Child).Text = "Press keys…";
        UiKit.Select(chip, true);
        _error.Visibility = Visibility.Collapsed;
    }

    void AddPhone(StackPanel stack, PhoneSetting phone)
    {
        stack.Children.Add(UiKit.Heading("Your phone"));
        stack.Children.Add(UiKit.Body("Photos, videos and files from your iPhone or Android, at full quality, over your Wi-Fi. Send them from Shiki on the phone, or ask the cat: \"get my last 5 photos from my phone\".", 11.5));
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(list);
        void Show()
        {
            list.Children.Clear();
            var phones = phone.Phones();
            if (phones.Count == 0) list.Children.Add(UiKit.Body("No phone connected yet.", 12.5));
            foreach (var (id, name, status) in phones)
            {
                var remove = UiKit.Button("Remove", () =>
                {
                    if (MessageBox.Show(this, $"Remove {name}? It won't be able to send to this PC until you connect it again.", "Remove phone",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    phone.Remove(id);
                    Show();
                });
                list.Children.Add(Line(name, status, remove));
            }
        }
        Show();
        var connect = UiKit.Button("Connect a phone", () => { phone.Connect(); });
        var refresh = UiKit.Button("Refresh", Show);
        var buttons = UiKit.Row(connect, refresh);
        buttons.Margin = new Thickness(0, 4, 0, 4);
        stack.Children.Add(buttons);
        stack.Children.Add(Line("Where they go", $"Photos and videos: {phone.Folder()}. Other files: Downloads\\From phone.", UiKit.Button("Open", phone.OpenFolder)));
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (_recording is not { } recording)
        {
            if (e.Key == Key.Escape) Close();
            return;
        }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Finish(recording.Setting.Get());
            return;
        }
        // Wait for a real key; modifiers alone aren't a shortcut.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)
        {
            _error.Text = "Use Ctrl, Alt or Win with a key, so normal typing isn't caught.";
            _error.Visibility = Visibility.Visible;
            return;
        }
        var text = new Hotkey(modifiers, key).ToString();
        if (recording.Setting.Set(text)) Finish(text);
        else
        {
            _error.Text = $"{text} is already in use. Try a different one.";
            _error.Visibility = Visibility.Visible;
            Finish(recording.Setting.Get());
        }
    }

    void Finish(string shown)
    {
        if (_recording is not { } recording) return;
        _recording = null;
        ((TextBlock)recording.Chip.Child).Text = shown;
        UiKit.Select(recording.Chip, false);
    }
}
