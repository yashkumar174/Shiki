using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiki.Core;
using Shiki.Core.Ai;

namespace Shiki.UI;

/// <summary>
/// A pretend screen for testing the agent (a dev aid): a little mail app drawn off screen, with an
/// email from Rahul, Reply, and Send. Clicks and typing go into it, never to the real PC.
/// </summary>
public sealed class AgentSandbox : IAgentScreen
{
    const int W = 1280, H = 800;
    readonly Grid _root = new() { Width = W, Height = H, Background = Brushes.White };
    readonly StackPanel _compose = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 24, 0, 0) };
    readonly TextBox _to, _subject, _body;
    readonly TextBlock _banner;
    TextBox? _focused;
    (int X, int Y) _cursor = (W / 2, H / 2);

    public bool Sent { get; private set; }

    /// <summary>Where a button is, in screenshot pixels (for the self-test).</summary>
    public (int X, int Y) Where(string label)
    {
        Layout();
        foreach (var b in Descendants(_root).OfType<Button>())
            if (b.Content as string == label && Shown(b))
            {
                var p = b.TransformToAncestor(_root).Transform(new Point(b.ActualWidth / 2, b.ActualHeight / 2));
                return ((int)p.X, (int)p.Y);
            }
        throw new InvalidOperationException($"No button {label}");
    }

    // Off screen nothing counts as IsVisible: check each parent's own Visibility instead.
    static bool Shown(DependencyObject node)
    {
        for (; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
    public string To => _to.Text;
    public string Body => _body.Text;

    public AgentSandbox()
    {
        TextBlock T(string text, double size, bool bold = false, Brush? colour = null) => new()
        {
            Text = text, FontSize = size, FontFamily = new FontFamily("Segoe UI"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = colour ?? Brushes.Black, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap,
        };
        Button B(string text, Action click)
        {
            var b = new Button { Content = text, FontSize = 15, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (_, _) => click();
            return b;
        }
        TextBox Box(double height = 32) => new() { FontSize = 15, Height = height, Margin = new Thickness(0, 0, 0, 10), AcceptsReturn = height > 40, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top };

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _root.ColumnDefinitions.Add(new ColumnDefinition());
        var list = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7)) };
        list.Children.Add(new Border { Padding = new Thickness(18), Child = T("Inbox - Mail", 20, true) });
        foreach (var (from, subject, bold) in new[] { ("Rahul Sharma", "Lunch on Friday?", true), ("Bank of Pune", "Your statement is ready", false), ("Asha Verma", "Rent receipt", false) })
        {
            var item = new StackPanel { Margin = new Thickness(18, 8, 18, 8) };
            item.Children.Add(T(from, 15, bold));
            item.Children.Add(T(subject, 13, false, Brushes.DimGray));
            list.Children.Add(item);
        }
        _root.Children.Add(list);

        var reading = new StackPanel { Margin = new Thickness(36, 24, 36, 24) };
        Grid.SetColumn(reading, 1);
        _banner = T("", 15, true, Brushes.SeaGreen);
        reading.Children.Add(_banner);
        reading.Children.Add(T("Lunch on Friday?", 24, true));
        reading.Children.Add(T("From: Rahul Sharma <rahul.sharma@example.com>", 14, false, Brushes.DimGray));
        reading.Children.Add(T("Hi! Are you free for lunch this Friday at 1 pm, at the usual place? Let me know. Rahul", 16));
        reading.Children.Add(B("Reply", () =>
        {
            _compose.Visibility = Visibility.Visible;
            _to.Text = "rahul.sharma@example.com";
            _subject.Text = "Re: Lunch on Friday?";
            _focused = _body;
        }));
        _to = Box();
        _subject = Box();
        _body = Box(160);
        _compose.Children.Add(T("To", 13, false, Brushes.DimGray));
        _compose.Children.Add(_to);
        _compose.Children.Add(T("Subject", 13, false, Brushes.DimGray));
        _compose.Children.Add(_subject);
        _compose.Children.Add(T("Message", 13, false, Brushes.DimGray));
        _compose.Children.Add(_body);
        _compose.Children.Add(B("Send", () =>
        {
            Sent = true;
            _compose.Visibility = Visibility.Collapsed;
            _banner.Text = "Message sent.";
            Log.Info($"agent-sandbox: SENT to {_to.Text}: {_body.Text}");
        }));
        reading.Children.Add(_compose);
        _root.Children.Add(reading);
        Layout();
    }

    void Layout()
    {
        _root.Measure(new Size(W, H));
        _root.Arrange(new Rect(0, 0, W, H));
        _root.UpdateLayout();
    }

    public (int Width, int Height) Size => (W, H);

    BitmapSource Render()
    {
        Layout();
        var bitmap = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        return bitmap;
    }

    static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public byte[] Screenshot() => Png(Render());

    public byte[] Zoom(int x0, int y0, int x1, int y1)
    {
        var area = new Int32Rect(Math.Clamp(Math.Min(x0, x1), 0, W - 1), Math.Clamp(Math.Min(y0, y1), 0, H - 1), 0, 0);
        area.Width = Math.Clamp(Math.Abs(x1 - x0), 8, W - area.X);
        area.Height = Math.Clamp(Math.Abs(y1 - y0), 8, H - area.Y);
        return Png(new CroppedBitmap(Render(), area));
    }

    public void Move(int x, int y) => _cursor = (x, y);

    public void Click(string button, int count, (int X, int Y)? at, string? modifiers)
    {
        if (at is { } p) _cursor = p;
        Layout();
        var hit = VisualTreeHelper.HitTest(_root, new Point(_cursor.X, _cursor.Y))?.VisualHit;
        for (var node = hit; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase b)
            {
                b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Log.Info($"agent-sandbox: clicked \"{(b as Button)?.Content}\"");
                return;
            }
            if (node is TextBox box)
            {
                _focused = box;
                return;
            }
        }
        Log.Info($"agent-sandbox: clicked nothing at {_cursor}");
    }

    public void Drag((int X, int Y) from, (int X, int Y) to, string? modifiers) => _cursor = to;
    public void Button(bool down) { }
    public (int X, int Y) Cursor() => _cursor;
    public void Scroll(string direction, int amount, (int X, int Y)? at, string? modifiers) { }

    public void Type(string text)
    {
        if (_focused != null)
        {
            // After Ctrl+A, typing replaces what was there.
            _focused.Text = _focused.Tag as string == "all" ? text : _focused.Text + text;
            _focused.Tag = null;
        }
        Log.Info($"agent-sandbox: typed \"{text}\"");
    }

    public void Key(string combo, int repeat)
    {
        Log.Info($"agent-sandbox: key {combo} x{repeat}");
        if (_focused == null) return;
        var keys = combo.ToLowerInvariant();
        if (keys is "ctrl+a") _focused.Tag = "all";
        else if (keys is "backspace" or "delete")
        {
            if (_focused.Tag as string == "all") _focused.Text = "";
            else for (int i = 0; i < repeat && _focused.Text.Length > 0; i++) _focused.Text = _focused.Text[..^1];
            _focused.Tag = null;
        }
        else if (keys is "return" or "enter" && _focused.AcceptsReturn) _focused.Text += "\n";
        else if (keys is "tab")
        {
            var boxes = new[] { _to, _subject, _body };
            _focused = boxes[(Array.IndexOf(boxes, _focused) + 1) % boxes.Length];
        }
    }

    public void HoldKey(string combo, double seconds) { }
}
