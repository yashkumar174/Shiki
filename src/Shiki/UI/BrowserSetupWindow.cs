using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Shell;

namespace Shiki.UI;

/// <summary>
/// Adding Shiki's extension to the person's browser, once: open the extensions page, turn on
/// Developer mode, Load unpacked, pick the folder. It shows "Connected" the moment the extension
/// says hello.
/// </summary>
public sealed class BrowserSetupWindow : Window
{
    readonly TextBlock _status = UiKit.Body("", 13.5);

    public BrowserSetupWindow()
    {
        var (browser, exe, page, supported) = BrowserSetup.Default();
        Title = $"Let Shiki work in {browser}";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);
        BrowserSetup.Install();
        var folder = BrowserSetup.RealFolder;

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        stack.Children.Add(UiKit.Title($"Let Shiki work in {browser}"));
        if (!supported)
        {
            stack.Children.Add(UiKit.Body($"Your usual browser is {browser}, which doesn't take Shiki's extension yet. Shiki can still do web tasks with your mouse and keyboard, or you can add the extension to Edge or Chrome."));
        }
        stack.Children.Add(UiKit.Body($"Shiki can do things on websites for you in your own {(supported ? browser : "Chrome or Edge")}, where you're already signed in: fill in forms, search and compare, download from portals. It needs its small extension, added once:"));

        stack.Children.Add(UiKit.Heading($"1. Open {(supported ? browser : "the browser")}'s extensions page"));
        var open = UiKit.Button("Open the extensions page", () =>
        {
            if (!BrowserSetup.OpenExtensionsPage()) _status.Text = $"Open {browser} and type {page} in the address bar.";
        }, primary: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(open);
        stack.Children.Add(UiKit.Body($"(Or type {page} in the address bar.)", 12));

        stack.Children.Add(UiKit.Heading("2. Turn on Developer mode"));
        stack.Children.Add(UiKit.Body("The switch is at the top right of that page (in Edge, on the left).", 12.5));

        stack.Children.Add(UiKit.Heading("3. Click Load unpacked, and pick this folder"));
        var path = new TextBox
        {
            Text = folder,
            IsReadOnly = true,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(6, 5, 6, 5),
            Background = Theme.Brush(Theme.Editor.HubPill),
            Foreground = Theme.Brush(Theme.Editor.Text),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Brush(Theme.Editor.SurfaceBorder),
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(path);
        var copy = UiKit.Button("Copy the address", () =>
        {
            try { Clipboard.SetText(folder); _status.Text = "Copied: paste it into the folder box after Load unpacked."; } catch (Exception) { }
        });
        var show = UiKit.Button("Show the folder", () =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose(); } catch (Exception) { }
        });
        var row = UiKit.Row(copy, show);
        row.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(row);

        stack.Children.Add(UiKit.Body("The extension talks only to Shiki on this PC, and only works when you ask Shiki to do something. While it works in a tab, the browser shows \"Shiki started debugging this browser\": that's normal.", 12));
        _status.Margin = new Thickness(0, 16, 0, 0);
        stack.Children.Add(_status);
        var close = UiKit.Button("Close", Close);
        close.Margin = new Thickness(0, 16, 0, 0);
        close.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(close);
        Content = stack;

        void Update()
        {
            var bridge = BrowserBridge.Current;
            if (bridge is { Connected: true })
            {
                _status.Text = $"Connected to {bridge.Browser ?? browser}. You're all set: ask Shiki to do something on a website.";
                _status.Foreground = new SolidColorBrush(Theme.EditorAccent);
            }
            else if (bridge == null) _status.Text = "Shiki's browser link didn't start (another app may be using its port). Restart Shiki and try again.";
            else _status.Text = "Waiting for the extension…";
        }
        Update();
        void Changed() => Dispatcher.BeginInvoke(Update);
        if (BrowserBridge.Current is { } b)
        {
            b.ConnectionChanged += Changed;
            Closed += (_, _) => b.ConnectionChanged -= Changed;
        }
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
