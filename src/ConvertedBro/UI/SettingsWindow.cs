using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConvertedBro.Core.Ai;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>A switch in the settings window: a label and how to read and flip it.</summary>
public sealed record SettingSwitch(string Label, string Hint, Func<bool> IsOn, Action Toggle);

/// <summary>A global shortcut in the settings window. Set returns false when another app owns it.</summary>
public sealed record ShortcutSetting(string Label, Func<string> Get, Func<string, bool> Set);

/// <summary>Where results are saved: a folder (null keeps them next to the original), and whether to sort them.</summary>
public sealed record OutputSetting(Func<string?> GetFolder, Action<string?> SetFolder, Func<bool> GetSort, Action<bool> SetSort, Action<string> Open);

/// <summary>Ask the cat: the name it goes by, the on/off switch, what it has cost, and its log. The key itself is handled here.</summary>
public sealed record AiSetting(string CatName, Func<bool> IsOn, Action Toggle,
    Func<string> GetVoiceEngine, Action<string> SetVoiceEngine, Func<string> GetWakeEngine, Action<string> SetWakeEngine, Func<string> DeepgramUsage,
    Func<bool> VoiceAnswers, Action ToggleVoiceAnswers,
    Func<bool> WakeOn, Action ToggleWake, Action TeachName,
    Func<string> Spent, Action OpenLog);

/// <summary>Shortcuts and the main switches, in one place.</summary>
public sealed class SettingsWindow : Window
{
    readonly Palette _p = Theme.Editor;
    readonly TextBlock _error = UiKit.Error();
    (ShortcutSetting Setting, Border Chip)? _recording;

    readonly OutputSetting _output;
    readonly Border _nextTo, _oneFolder;
    readonly StackPanel _folderPart = new();
    readonly TextBlock _folderPath = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    string _lastFolder;

    public SettingsWindow(OutputSetting output, IReadOnlyList<ShortcutSetting> shortcuts, IReadOnlyList<SettingSwitch> switches, AiSetting? ai = null)
    {
        _output = output;
        _lastFolder = output.GetFolder() ?? Core.OutputPaths.DefaultFolder;
        Title = "Converted Bro settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        stack.Children.Add(UiKit.Title("Settings"));

        stack.Children.Add(UiKit.Heading("Where results go"));
        _nextTo = UiKit.Button("Next to the original", () => SetMode(false));
        _oneFolder = UiKit.Button("One folder", () => SetMode(true));
        var modes = UiKit.Row(_nextTo, _oneFolder);
        modes.Margin = new Thickness(0, 2, 0, 6);
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

        stack.Children.Add(UiKit.Heading("Shortcuts"));
        foreach (var shortcut in shortcuts)
        {
            Border? chip = null;
            chip = UiKit.Button(shortcut.Get(), () => StartRecording(shortcut, chip!));
            stack.Children.Add(Line(shortcut.Label, "Click, then press the keys you want.", chip));
        }
        stack.Children.Add(_error);

        stack.Children.Add(UiKit.Heading("Behaviour"));
        foreach (var setting in switches) stack.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));

        if (ai != null) AddAi(stack, ai);

        var done = UiKit.Button("Done", Close, primary: true);
        done.Margin = new Thickness(0, 22, 0, 0);
        stack.Children.Add(done);
        Content = stack;

        PreviewKeyDown += OnKey;
    }

    void AddAi(StackPanel stack, AiSetting ai)
    {
        stack.Children.Add(UiKit.Heading("Ask the cat (uses Claude)"));
        stack.Children.Add(UiKit.Body($"Tell {ai.CatName} what to do with your files. Your words and the names of the files involved go to Claude Haiku 5.5 with your own API key; the files themselves are never uploaded. Nothing is sent while this is off."));
        stack.Children.Add(Line($"Ask {ai.CatName}", "On the Ask page and with the shortcut above.", Toggle(new SettingSwitch("", "", ai.IsOn, ai.Toggle))));

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

        AddVoice(stack, ai);

        var spent = UiKit.Body(ai.Spent());
        spent.Margin = new Thickness(0, 14, 0, 8);
        stack.Children.Add(spent);
        stack.Children.Add(UiKit.Button("Open the cat's log", ai.OpenLog));
    }

    /// <summary>
    /// Voice: which engine writes down commands (Whisper here, Deepgram, Wispr Flow later) and
    /// which hears the wake word (Whisper here, or Deepgram for the speech bursts), plus their keys.
    /// </summary>
    void AddVoice(StackPanel stack, AiSetting ai)
    {
        stack.Children.Add(UiKit.Heading("Voice"));
        stack.Children.Add(UiKit.Body($"Press the mic or the talk shortcut and speak (English). The microphone is only on while {ai.CatName} is listening, unless \"Hey {ai.CatName}\" is on."));

        Action refresh = () => { };

        // ---- Commands ----
        var cmdWhisper = UiKit.Button("On this PC", () => { ai.SetVoiceEngine("whisper"); refresh(); });
        var cmdDeepgram = UiKit.Button("Deepgram", () => { ai.SetVoiceEngine("deepgram"); refresh(); });
        var cmdWispr = UiKit.Button("Wispr Flow", () => { ai.SetVoiceEngine("wispr"); refresh(); });
        stack.Children.Add(Line("What you say", "Who writes down your requests.", new Border()));
        var cmdRow = UiKit.Row(cmdWhisper, cmdDeepgram, cmdWispr);
        cmdRow.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(cmdRow);
        var cmdNote = UiKit.Body("");
        stack.Children.Add(cmdNote);

        // ---- Wake word ----
        var wakeWhisper = UiKit.Button("On this PC", () => { ai.SetWakeEngine("whisper"); refresh(); });
        var wakeDeepgram = UiKit.Button("Deepgram", () => { ai.SetWakeEngine("deepgram"); refresh(); });
        stack.Children.Add(Line($"\"Hey {ai.CatName}\"", "Who listens for the name.", new Border()));
        var wakeRow = UiKit.Row(wakeWhisper, wakeDeepgram);
        wakeRow.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(wakeRow);
        var wakeNote = UiKit.Body("");
        stack.Children.Add(wakeNote);

        var deepgramKey = KeyPart("Deepgram", Core.Voice.Deepgram.Key, Core.Voice.Deepgram.TestKey, () =>
        {
            // Saving a Deepgram key means wanting Deepgram: use it for both, unless already chosen.
            if (ai.GetVoiceEngine() == "whisper") ai.SetVoiceEngine("deepgram");
            if (ai.GetWakeEngine() == "whisper") ai.SetWakeEngine("deepgram");
        }, "Create one at console.deepgram.com.");
        var wisprKey = KeyPart("Wispr", Core.Voice.WisprFlow.Key, null, null, "");
        stack.Children.Add(deepgramKey.Root);
        stack.Children.Add(wisprKey.Root);
        var usage = UiKit.Body("");
        stack.Children.Add(usage);

        void Show()
        {
            var cmd = ai.GetVoiceEngine();
            var wake = ai.GetWakeEngine();
            bool hasDeepgram = Core.Voice.Deepgram.HasKey;
            UiKit.Select(cmdWhisper, cmd == "whisper");
            UiKit.Select(cmdDeepgram, cmd == "deepgram");
            UiKit.Select(cmdWispr, cmd == "wispr");
            UiKit.Select(wakeWhisper, wake == "whisper");
            UiKit.Select(wakeDeepgram, wake == "deepgram");
            cmdNote.Text = cmd switch
            {
                "deepgram" => hasDeepgram
                    ? "Deepgram Nova-3 writes your words down live, as you speak, and judges when you've finished. Your voice goes to Deepgram; if it can't be reached, Whisper here takes over."
                    : "Add a Deepgram key below. Until then Whisper on this PC is used.",
                "wispr" => Core.Voice.WisprFlow.Available
                    ? "What you say goes to Wispr Flow to be written down."
                    : "Wispr Flow's API isn't open to Converted Bro yet (access has been asked for). Until it is, Whisper on this PC is used. You can save your Wispr key now.",
                _ => $"Free and private: Whisper turns your voice into text on this PC (the graphics card when it can). It downloads once ({Core.Voice.WhisperLocal.DownloadSize}).",
            };
            wakeNote.Text = wake == "deepgram"
                ? hasDeepgram
                    ? $"This PC notices when someone talks; only those short bits of speech (under six seconds) go to Deepgram to check for the name. That includes anyone talking near the PC while listening is on. Silence never leaves the PC. Offline, Whisper here takes over."
                    : "Add a Deepgram key below. Until then Whisper on this PC listens."
                : "Free and private: short bits of speech are checked for the name on this PC and then forgotten.";
            deepgramKey.Root.Visibility = cmd == "deepgram" || wake == "deepgram" ? Visibility.Visible : Visibility.Collapsed;
            wisprKey.Root.Visibility = cmd == "wispr" ? Visibility.Visible : Visibility.Collapsed;
            usage.Text = ai.DeepgramUsage();
            usage.Visibility = usage.Text.Length > 0 && deepgramKey.Root.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
        refresh = Show;
        deepgramKey.Changed += Show;
        Show();

        stack.Children.Add(Line("Answer plans by voice", "After a spoken request, listen for \"yes\" or \"no\" when a plan card shows.",
            Toggle(new SettingSwitch("", "", ai.VoiceAnswers, ai.ToggleVoiceAnswers))));
        stack.Children.Add(Line($"Listen for \"Hey {ai.CatName}\"", "The mic stays on while this is on (Windows shows its mic icon). Pauses while you record the screen.",
            Toggle(new SettingSwitch("", "", ai.WakeOn, ai.ToggleWake))));
        var teach = UiKit.Button("Name your cat and teach it…", ai.TeachName);
        teach.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(teach);
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
