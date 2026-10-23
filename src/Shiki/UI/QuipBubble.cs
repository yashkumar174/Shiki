using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Shiki.UI.Dashboard;

namespace Shiki.UI;

/// <summary>A little speech bubble above the cat for a few seconds: never takes a click or the keyboard.</summary>
public sealed class QuipBubble : Window
{
    public QuipBubble(string text, Rect cat)
    {
        var k = new DashKit(DashPalette.Night);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;

        var words = k.Text(text, 13.5, k.P.Text, wrap: true);
        words.MaxWidth = 260;
        Content = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(14, 9, 14, 10),
            Background = DashKit.B(k.P.Card),
            BorderBrush = DashKit.B(k.P.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14, 14, 14, 4),
            Child = words,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.4 },
        };
        Opacity = 0;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Click-through, never activated, not in Alt+Tab.
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80 | 0x20);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Clamp(cat.Left + cat.Width / 2 - 24, area.Left, area.Right - ActualWidth);
            Top = Math.Max(area.Top, cat.Top - ActualHeight + 6);
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 180 : 0)));
        };
        // Long enough to read: a second per few words, at least four.
        var stay = TimeSpan.FromSeconds(Math.Clamp(text.Split(' ').Length / 2.5, 4, 9));
        var timer = new DispatcherTimer { Interval = stay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 300 : 0));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
