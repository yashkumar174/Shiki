using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiki.Shell;

namespace Shiki.UI;

/// <summary>
/// Adding Shiki's extension to the person's browser, once. With Shiki in the store: one button
/// opens its store page, where "Add to Chrome" is the only click. Before that (or if they'd
/// rather), the steps to add it by hand. It shows "Connected" the moment the extension says hello.
/// </summary>
public sealed class BrowserSetupWindow : Window
{
    readonly TextBlock _status = UiKit.Body("", 13.5);

    public BrowserSetupWindow()
    {
        var (browser, _, page, supported) = BrowserSetup.Default();
        var target = supported ? browser : "Chrome";
        var store = BrowserSetup.StoreUrl(target);
        Title = $"Add Shiki to {target}";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);
        BrowserSetup.Install();

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        stack.Children.Add(UiKit.Title($"Add Shiki to {target}"));
        if (!supported)
            stack.Children.Add(UiKit.Body($"Your usual browser is {browser}, which doesn't take Shiki's extension yet. Add it to Chrome or Edge, or Shiki does web tasks with your mouse and keyboard instead."));
        stack.Children.Add(UiKit.Body($"Then Shiki does things on websites for you in your own {target}, where you're already signed in: fill in forms, search and compare, download from portals."));
        stack.Children.Add(UiKit.Body("You don't have to: without it, Shiki works in a browser window of its own, where you sign in to a site once and it remembers.", 12.5));

        // By hand: Developer mode and Load unpacked (for now, or for people who'd rather).
        var byHand = HandSteps(target, page);

        if (store != null)
        {
            var add = UiKit.Button($"Add Shiki to {target}", () =>
            {
                if (!BrowserSetup.OpenStorePage()) _status.Text = $"Open {store} in {target}.";
                else _status.Text = $"In {target}: click Add to {target}, then Add extension. This window shows Connected when it's in.";
            }, primary: true);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 16, 0, 0);
            stack.Children.Add(add);
            stack.Children.Add(UiKit.Body($"{target} opens Shiki's page in its store: click Add to {target}. That's all.", 12.5));
            byHand.Visibility = Visibility.Collapsed;
            var other = UiKit.Button("Add it by hand instead", () => byHand.Visibility = Visibility.Visible);
            other.HorizontalAlignment = HorizontalAlignment.Left;
            other.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(other);
        }
        else
        {
            stack.Children.Add(UiKit.Body("Shiki is on its way to the Chrome Web Store, where adding it will be one click. Until then, it takes three steps:", 12.5));
        }
        stack.Children.Add(byHand);

        stack.Children.Add(UiKit.Body($"The extension talks only to Shiki on this PC, and only works when you ask Shiki to do something. While it works in a tab, {target} shows \"Shiki started debugging this browser\": that's normal.", 12));
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
                _status.Text = $"Connected to {bridge.Browser ?? target}. You're all set: ask Shiki to do something on a website.";
                _status.Foreground = new SolidColorBrush(Theme.EditorAccent);
            }
            else if (bridge == null) _status.Text = "Shiki's browser link didn't start (another app may be using its port). Restart Shiki and try again.";
            else if (_status.Text.Length == 0 || _status.Text.StartsWith("Waiting")) _status.Text = "Waiting for the extension…";
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

    /// <summary>The three steps to add the extension from Shiki's folder: extensions page, Developer mode, Load unpacked.</summary>
    StackPanel HandSteps(string browser, string page)
    {
        var folder = BrowserSetup.RealFolder;
        var steps = new StackPanel();
        steps.Children.Add(UiKit.Heading($"1. Open {browser}'s extensions page"));
        var open = UiKit.Button("Open the extensions page", () =>
        {
            if (!BrowserSetup.OpenExtensionsPage()) _status.Text = $"Open {browser} and type {page} in the address bar.";
        });
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 8, 0, 0);
        steps.Children.Add(open);
        steps.Children.Add(UiKit.Body($"(Or type {page} in the address bar.)", 12));

        steps.Children.Add(UiKit.Heading("2. Turn on Developer mode"));
        steps.Children.Add(UiKit.Body("The switch is at the top right of that page (in Edge, on the left).", 12.5));

        steps.Children.Add(UiKit.Heading("3. Click Load unpacked, and pick this folder"));
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
        steps.Children.Add(path);
        var copy = UiKit.Button("Copy the address", () =>
        {
            try { Clipboard.SetText(folder); _status.Text = "Copied: paste it into the folder box after Load unpacked."; } catch (Exception) { }
        });
        var show = UiKit.Button("Show the folder", () =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose(); } catch (Exception) { }
        });
        var row = UiKit.Row(copy, show);
        row.Margin = new Thickness(0, 8, 0, 8);
        steps.Children.Add(row);
        return steps;
    }
}
