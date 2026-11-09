using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Shiki.Shell;

namespace Shiki.UI;

/// <summary>Shared pieces for the app's own windows, in the current look (Arcade or Glass).</summary>
public static class UiKit
{
    static Palette P => Theme.Editor;

    /// <summary>Solid background, dark title bar, right fonts; brings the window to the front when shown.</summary>
    public static void Prepare(Window window)
    {
        window.Background = Theme.Brush(Color.FromRgb(P.Surface.R, P.Surface.G, P.Surface.B));
        window.UseLayoutRounding = true;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (P.Pixel) TextOptions.SetTextRenderingMode(window, TextRenderingMode.Aliased);
        window.SourceInitialized += (_, _) => { if (P.Dark) NativeMethods.UseDarkTitleBar(new WindowInteropHelper(window).Handle); };
        window.Loaded += (_, _) =>
        {
            // Inside the dashboard the window is never shown on its own: nothing to bring forward.
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            NativeMethods.ForceForeground(handle);
            window.Activate();
        };
    }

    /// <summary>A window's title, in the cat's pixel type.</summary>
    public static TextBlock Title(string text) => new()
    {
        Text = text,
        FontFamily = Theme.PixelFont,
        FontSize = 26,
        Foreground = Theme.Brush(P.Text),
        Margin = new Thickness(0, 0, 0, 4),
    };

    public static TextBlock Body(string text, double size = 13) => new()
    {
        Text = text,
        FontFamily = Theme.TextFont,
        FontSize = size,
        Foreground = Theme.Brush(P.SubtleText),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
    };

    /// <summary>A section's name: plain and bold, so a page reads as a few clear groups.</summary>
    public static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontFamily = Theme.TextFont,
        FontSize = 15,
        FontWeight = FontWeights.Bold,
        Foreground = Theme.Brush(P.Text),
        Margin = new Thickness(0, 26, 0, 6),
        // Settings gathers everything under a heading into one card.
        Tag = "heading",
    };

    public static TextBlock Error() => new()
    {
        FontFamily = Theme.TextFont,
        FontSize = 12.5,
        Foreground = Theme.Brush(Color.FromRgb(0xFF, 0x8A, 0x9A)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>A text-input frame around a TextBox or PasswordBox.</summary>
    public static Border Field(Control input)
    {
        input.Background = Brushes.Transparent;
        input.BorderThickness = new Thickness(0);
        input.Foreground = Theme.Brush(P.Text);
        input.FontFamily = Theme.TextFont;
        input.FontSize = 14;
        input.VerticalContentAlignment = VerticalAlignment.Center;
        if (input is TextBox box) box.CaretBrush = Theme.Brush(P.Text);
        if (input is PasswordBox pass) pass.CaretBrush = Theme.Brush(P.Text);
        var frame = new Border
        {
            Background = Theme.Brush(P.RowHover),
            BorderBrush = Theme.Brush(P.SurfaceBorder),
            BorderThickness = new Thickness(P.Pixel ? 2 : 1),
            CornerRadius = new CornerRadius(P.Pixel ? 0 : 8),
            Padding = new Thickness(10, 7, 10, 7),
            Child = input,
        };
        input.GotKeyboardFocus += (_, _) => frame.BorderBrush = Theme.Brush(Theme.EditorAccent);
        input.LostKeyboardFocus += (_, _) => frame.BorderBrush = Theme.Brush(P.SurfaceBorder);
        return frame;
    }

    public static Border Button(string label, Action onClick, bool primary = false, bool icon = false, bool stretch = false)
    {
        var rest = primary ? Theme.EditorAccent : P.RowHover;
        var hot = primary ? Theme.Lighten(Theme.EditorAccent, 0.12) : Theme.Lighten(P.RowHover, 0.08);
        var background = new SolidColorBrush(rest);
        var button = new Border
        {
            Background = background,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(P.Pixel ? 2 : 1.5),
            CornerRadius = new CornerRadius(P.Pixel ? 0 : 10),
            Padding = new Thickness(icon ? 8 : 14, 7, icon ? 8 : 14, 8),
            Margin = new Thickness(0, 0, stretch ? 0 : 8, stretch ? 8 : 0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = label,
                FontFamily = icon ? Theme.IconFont : Theme.TextFont,
                FontSize = icon ? 13 : 13.5,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = Theme.Brush(primary ? Theme.EditorAccentText : P.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        button.MouseEnter += (_, _) => background.Color = hot;
        button.MouseLeave += (_, _) => background.Color = rest;
        Dashboard.DashKit.Press(button);
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }

    /// <summary>Outlines a chip when it's the selected one.</summary>
    public static void Select(Border chip, bool on) => chip.BorderBrush = Theme.Brush(on ? Theme.EditorAccent : Colors.Transparent);

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }
}
