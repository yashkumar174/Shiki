using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>
/// "What can Sky do?": everything the cat understands, grouped, each with what it does and a Try
/// button (or the shortcut to press). Typing in the box narrows the list as you go.
/// </summary>
public sealed class SkillsPage(DashKit k, DashboardHost host)
{
    DashPalette P => k.P;

    enum How
    {
        /// <summary>Runs on the PC at once, no Claude needed.</summary>
        Instant,
        /// <summary>Goes to Claude (anything that changes things shows a card first).</summary>
        Ask,
        /// <summary>Needs something selected or pointed at first: no Try, just how.</summary>
        Select,
        /// <summary>A shortcut: the key, and a button that starts it.</summary>
        Key,
    }

    sealed record Skill(string Say, string What, How How, string? Key = null, string? Tool = null);

    static IEnumerable<(string Group, string Blurb, Skill[] Skills)> Groups(string cat, Settings keys) =>
    [
        ("Your PC", "Said plainly, these happen at once, without Claude.",
        [
            new("mute", "Sound off (\"unmute\" brings it back).", How.Instant),
            new("volume 40", "Any number from 0 to 100. Or \"turn it up\", \"quieter\".", How.Instant),
            new("brightness 70", "Laptop screens and monitors that allow it. Or \"brighter\", \"dimmer\".", How.Instant),
            new("dark mode", "Windows goes dark (\"light mode\" goes back).", How.Instant),
            new("do not disturb", "Notifications wait quietly (\"turn off do not disturb\").", How.Instant),
            new("open Notepad", "Any app in the Start menu.", How.Instant),
            new("captions on", "Subtitles for whatever the PC is playing (\"captions off\").", How.Instant),
            new("pause", "Play or pause whatever's playing. Also \"next song\", \"previous song\".", How.Instant),
            new("what's my battery and volume?", "Battery, volume, brightness and more, in a sentence.", How.Ask),
            new("switch sound to my headphones", "Moves the sound to another output.", How.Ask),
            new("close Notepad", "Like the app's own X; it asks you first.", How.Ask),
            new("lock my PC", "Also sleep, restart, shut down. Always a card first.", How.Ask),
        ]),
        ("Hindi and Hinglish", keys.SpeechLanguage == "hi-en" ? "Turned on: say these the way you'd say them." : "Turn on Settings, Voice, I speak: Hindi + English.",
        [
            new("awaaz kam karo", "Volume down (\"awaaz badhao\" for up, \"awaaz band karo\" to mute).", How.Instant),
            new("volume chalis pe kar do", "Volume 40.", How.Instant),
            new("Chrome kholo", "Opens an app.", How.Instant),
            new("roshni badhao", "Brighter screen.", How.Instant),
            new("captions chalu karo", "Subtitles on.", How.Instant),
            new("agla gaana", "Next song (\"gaana roko\" pauses it).", How.Instant),
            new("meri downloads saaf kar do", "Anything else in Hinglish goes to Claude, who answers in Hinglish.", How.Ask),
        ]),
        ("The web, email and calendar", "Answered in seconds, without opening the browser.",
        [
            new("what's the weather in Pune today?", "Searches the web and says where the answer came from.", How.Ask),
            new("latest news about the Indian cricket team", "News, prices, opening hours, facts.", How.Ask),
            new("any new emails today?", "Searches your Gmail (connect it once in Settings).", How.Ask),
            new("read my latest email", "Reads it and tells you what it says.", How.Ask),
            new("reply to my latest email saying thanks, I'll get back to you tomorrow", "Shows you the whole email on a card; sent only when you press Do it.", How.Ask),
            new("find emails with invoices from this month", "Gmail's own search, from your words.", How.Ask),
            new("download the attachments from my latest email", "Saved straight into Downloads, no browser.", How.Ask),
            new("what's on today?", "Your Google Calendar (connect Google once in Settings).", How.Ask),
            new("add dentist tomorrow at 5", "Straight into your calendar. With guests, a card first.", How.Ask),
            new("email Rahul saying I'll be late", "Finds Rahul in your contacts; shows the email before sending.", How.Ask),
            new("play Pink Floyd on Spotify", "Finds it and starts it in the Spotify app.", How.Ask),
            new("search YouTube for lofi music", "Opens the results straight away.", How.Ask),
        ]),
        ("Files", "Anything that changes files shows a card first; tidying and renaming have Undo.",
        [
            new("tidy my downloads", "Sorts loose files into Images, Videos, PDFs and more.", How.Ask),
            new("what's eating my disk space?", "Free space, the biggest folders and files, and what to do.", How.Ask),
            new("find my newest PDF", "Searches your usual folders.", How.Ask),
            new("find the document where I paid the deposit", "Searches inside your documents by their words or numbers, instantly.", How.Ask),
            new("compress my newest video", "Or turn it into an MP4, a GIF, just the sound…", How.Ask),
            new("back up my documents to D", "Copies only what changed since last time.", How.Ask),
            new("make a passport photo from my newest photo", "The right size for forms, face centred.", How.Ask),
            new("rename these by what they show", "Select photos or PDFs in File Explorer first.", How.Select),
            new("what is this file? is it safe?", "Select it first. Says who made it and whether it's signed.", How.Select),
        ]),
        ("Documents", "Select a PDF, Word, PowerPoint or Excel file in File Explorer, then say:",
        [
            new("summarise this", "The main points in a few lines (scans work too).", How.Select),
            new("what's the total in this invoice?", "Any question about what's inside.", How.Select),
            new("read this to me", "Reads it aloud, with a Stop button.", How.Select),
            new("translate this into Hindi", "A translated Word file; the card shows the cost first.", How.Select),
            new("make a table of the dates and totals from these", "Pulls data out of several files into a table.", How.Select),
        ]),
        ("The screen", "Point at things, ask about them, record them.",
        [
            new("Point and ask", "Draw around part of the screen with the paw, then ask about it.", How.Key, keys.PawHotkey, "paw"),
            new("What's this error?", "Looks at the window in front and explains the error.", How.Key, keys.ErrorHotkey, "error"),
            new("copy the text on my screen", "Reads the window in front and puts the text on the clipboard.", How.Ask),
            new("Snip", "Cut out part of the screen, then convert or copy it.", How.Key, keys.SnipHotkey, "snip"),
            new("Record the screen", "Pick an area; say \"stop recording\" to finish.", How.Key, keys.RecordHotkey, "record"),
            new("Pick a colour", "Click anywhere to get its colour code.", How.Key, keys.ColourHotkey, "colour"),
            new("Dictation", "Hold the keys, talk, let go: typed where your cursor is, in any app.", How.Key, keys.DictateHotkey),
        ]),
        ("Copy, remember, routines", "",
        [
            new("make what I copied into a table", "Paste it into Excel as cells.", How.Ask),
            new("clean up what I copied", "Fixes spelling and spacing, ready to paste.", How.Ask),
            new("remember that my PDFs should be under 2 MB", "Kept for good and used from then on (\"forget that\").", How.Ask),
            new("do the same as last time", "It knows what you asked and what it did lately.", How.Ask),
            new("remind me to call the bank tomorrow at 5", "A task with a reminder when it's due.", How.Ask),
            new("what do I have to do?", "Your open tasks.", How.Ask),
            new("start work mode", "Do not disturb on and your task list (\"done for the day\" to finish).", How.Instant),
            new("when I say meeting, turn on do not disturb and open Teams", "Makes your own routine.", How.Ask),
        ]),
        ("Meetings", "Notes for any call: a summary, decisions and tasks at the end.",
        [
            new("take notes", "Starts meeting notes (\"stop notes\" when it's over).", How.Instant),
            new("Meeting notes", "The same, from the keyboard.", How.Key, keys.NotesHotkey, "notes"),
        ]),
        ("Do it for me", $"Quick ways first; when nothing else will do, {cat} uses the mouse and keyboard. It asks before sending or submitting anything, and pauses when you click or type.",
        [
            new("send Mom on WhatsApp: on my way", "Opens the chat with the message, sends after your OK. Asks for the number once and remembers it.", How.Ask),
            new("fill in this form with my name and email", "Fills the form on screen; asks before submitting.", How.Ask),
        ]),
    ];

    public UIElement Build()
    {
        var cat = host.CatName();
        var page = new StackPanel { Margin = new Thickness(52, 44, 52, 48), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(k.Text($"What can {cat} do?", 34, font: DashKit.Pixel));
        var sub = k.Text($"Say it after \"Hey {cat}\", press {host.Settings.TalkHotkey} and talk, or type it in Ask {cat}. Try does it now. Anything that changes files, sends or closes something shows you a card first.", 15, P.Muted, wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 22);
        page.Children.Add(sub);

        var search = new TextBox
        {
            FontFamily = DashKit.Sans,
            FontSize = 15,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = DashKit.B(P.Text),
            CaretBrush = DashKit.B(P.Text),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = k.Text("Search: email, volume, PDF, Hindi…", 15, P.Faint);
        hint.IsHitTestVisible = false;
        var field = new DockPanel();
        var glass = k.Icon("\uE721", 15, P.Faint);
        glass.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(glass, Dock.Left);
        field.Children.Add(glass);
        field.Children.Add(new Grid { Children = { hint, search } });
        var searchCard = k.Card(field, new Thickness(18, 12, 18, 12), 16);
        searchCard.Margin = new Thickness(0, 0, 0, 6);
        page.Children.Add(searchCard);

        // Each row knows its words, so the search can show and hide rows (and empty groups) in place.
        var groups = new List<(UIElement Head, UIElement Card, List<(UIElement Rule, UIElement Row, string Words)> Rows)>();
        foreach (var (name, blurb, skills) in Groups(cat, host.Settings))
        {
            var head = new StackPanel { Margin = new Thickness(2, 26, 0, 10) };
            head.Children.Add(k.Label(name));
            if (blurb.Length > 0)
            {
                var b = k.Text(blurb, 13, P.Muted, wrap: true);
                b.Margin = new Thickness(0, 4, 0, 0);
                head.Children.Add(b);
            }
            page.Children.Add(head);
            var list = new StackPanel();
            var rows = new List<(UIElement, UIElement, string)>();
            foreach (var skill in skills)
            {
                var rule = k.Rule();
                var row = Row(skill);
                list.Children.Add(rule);
                list.Children.Add(row);
                rows.Add((rule, row, $"{name} {skill.Say} {skill.What} {skill.Key}".ToLowerInvariant()));
            }
            var card = k.Card(list, new Thickness(0), 16);
            card.ClipToBounds = true;
            page.Children.Add(card);
            groups.Add((head, card, rows));
        }
        var none = k.Text("Nothing here matches. Just ask in your own words: the cat understands far more than this list.", 15, P.Muted, wrap: true);
        none.Margin = new Thickness(2, 26, 0, 0);
        none.Visibility = Visibility.Collapsed;
        page.Children.Add(none);

        void Filter()
        {
            var words = search.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            bool any = false;
            foreach (var (head, card, rows) in groups)
            {
                bool first = true, shown = false;
                foreach (var (rule, row, text) in rows)
                {
                    bool match = words.All(text.Contains);
                    row.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    // No line above the first row that shows.
                    rule.Visibility = match && !first ? Visibility.Visible : Visibility.Collapsed;
                    if (match) { first = false; shown = true; }
                }
                head.Visibility = card.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                any |= shown;
            }
            none.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        }
        search.TextChanged += (_, _) => Filter();
        Filter();
        return page;
    }

    UIElement Row(Skill skill)
    {
        var grid = new Grid { Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var what = new StackPanel { Margin = new Thickness(20, 13, 12, 13) };
        var say = new WrapPanel();
        say.Children.Add(k.Text(skill.How == How.Key ? skill.Say : $"“{skill.Say}”", 15, P.Text, FontWeights.SemiBold, wrap: true));
        if (skill.How == How.Instant)
        {
            // Said plainly, these never wait for Claude.
            var tag = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = DashKit.B(DashKit.A(P.Accent, 0x26)),
                Padding = new Thickness(7, 1, 7, 2),
                Margin = new Thickness(8, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = k.Text("instant", 11.5, P.AccentEdge),
                ToolTip = "Runs on your PC straight away, without Claude.",
            };
            say.Children.Add(tag);
        }
        what.Children.Add(say);
        var line = k.Text(skill.What, 13, P.Muted, wrap: true);
        line.Margin = new Thickness(0, 3, 0, 0);
        what.Children.Add(line);
        grid.Children.Add(what);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        if (skill.Key is { } key)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = DashKit.B(P.Line),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 3, 9, 4),
                Margin = new Thickness(0, 0, skill.Tool != null ? 8 : 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = k.Text(key, 12.5, P.Muted),
            };
            right.Children.Add(chip);
        }
        if (skill.How == How.Select)
            right.Children.Add(k.Text("select first", 12.5, P.Faint));
        else if (skill.How != How.Key || skill.Tool != null)
        {
            var tryIt = k.Button("Try", () =>
            {
                if (skill.Tool is { } tool) host.TryTool?.Invoke(tool);
                else host.TrySaying?.Invoke(skill.Say);
            }, DashKit.Pill.Quiet, "\uE768", 12.5);
            tryIt.Padding = new Thickness(12, 5, 14, 6);
            tryIt.ToolTip = skill.How == How.Instant ? "Does it now" : $"Asks {host.CatName()} now";
            right.Children.Add(tryIt);
        }
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
}
