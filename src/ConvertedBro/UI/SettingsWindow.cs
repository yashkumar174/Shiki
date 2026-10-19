using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>A switch in the settings window: a label and how to read and flip it.</summary>
public sealed record SettingSwitch(string Label, string Hint, Func<bool> IsOn, Action Toggle);

/// <summary>A global shortcut in the settings window. Set returns false when another app owns it.</summary>
public sealed record ShortcutSetting(string Label, Func<string> Get, Func<string, bool> Set);

/// <summary>Where results are saved: a folder (null keeps them next to the original), and whether to sort them.</summary>
public sealed record OutputSetting(Func<string?> GetFolder, Action<string?> SetFolder, Func<bool> GetSort, Action<bool> SetSort, Action<string> Open);

/// <summary>Shortcuts and the main switches, in one place.</summary>
public sealed class SettingsWindow : Window
{
    readonly Palette _p = Theme.Current;
    readonly TextBlock _error = UiKit.Error();
    (ShortcutSetting Setting, Border Chip)? _recording;

    readonly OutputSetting _output;
    readonly Border _nextTo, _oneFolder;
    readonly StackPanel _folderPart = new();
    readonly TextBlock _folderPath = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    string _lastFolder;

    public SettingsWindow(OutputSetting output, IReadOnlyList<ShortcutSetting> shortcuts, IReadOnlyList<SettingSwitch> switches)
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

        var done = UiKit.Button("Done", Close, primary: true);
        done.Margin = new Thickness(0, 22, 0, 0);
        stack.Children.Add(done);
        Content = stack;

        PreviewKeyDown += OnKey;
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
        if (dialog.ShowDialog(this) != true) return;
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
        text.Children.Add(new TextBlock { Text = label, FontFamily = Theme.LabelFont, FontSize = 14, Foreground = Theme.Brush(_p.Text) });
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
