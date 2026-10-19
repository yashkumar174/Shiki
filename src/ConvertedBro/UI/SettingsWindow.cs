using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConvertedBro.Shell;

namespace ConvertedBro.UI;

/// <summary>A switch in the settings window: a label and how to read and flip it.</summary>
public sealed record SettingSwitch(string Label, string Hint, Func<bool> IsOn, Action Toggle);

/// <summary>Shortcuts and the main switches, in one place.</summary>
public sealed class SettingsWindow : Window
{
    readonly Palette _p = Theme.Current;
    readonly Func<string> _getSnip;
    readonly Func<string, bool> _setSnip;
    readonly Border _shortcut;
    readonly TextBlock _shortcutText;
    readonly TextBlock _error = UiKit.Error();
    bool _recording;

    /// <param name="setSnip">Applies a new shortcut; returns false if another app owns it.</param>
    public SettingsWindow(Func<string> getSnip, Func<string, bool> setSnip, IReadOnlyList<SettingSwitch> switches)
    {
        _getSnip = getSnip;
        _setSnip = setSnip;
        Title = "Converted Bro settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        stack.Children.Add(UiKit.Title("Settings"));

        stack.Children.Add(UiKit.Heading("Shortcuts"));
        _shortcut = UiKit.Button(getSnip(), StartRecording);
        _shortcutText = (TextBlock)_shortcut.Child;
        stack.Children.Add(Line("Snip and convert", "Click, then press the keys you want.", _shortcut));
        stack.Children.Add(_error);

        stack.Children.Add(UiKit.Heading("Behaviour"));
        foreach (var setting in switches) stack.Children.Add(Line(setting.Label, setting.Hint, Toggle(setting)));

        var done = UiKit.Button("Done", Close, primary: true);
        done.Margin = new Thickness(0, 22, 0, 0);
        stack.Children.Add(done);
        Content = stack;

        PreviewKeyDown += OnKey;
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

    void StartRecording()
    {
        _recording = true;
        _shortcutText.Text = "Press keys…";
        UiKit.Select(_shortcut, true);
        _error.Visibility = Visibility.Collapsed;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (!_recording)
        {
            if (e.Key == Key.Escape) Close();
            return;
        }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Finish(_getSnip());
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
        if (_setSnip(text)) Finish(text);
        else
        {
            _error.Text = $"{text} is already used by another app. Try a different one.";
            _error.Visibility = Visibility.Visible;
            Finish(_getSnip());
        }
    }

    void Finish(string shown)
    {
        _recording = false;
        _shortcutText.Text = shown;
        UiKit.Select(_shortcut, false);
    }
}
