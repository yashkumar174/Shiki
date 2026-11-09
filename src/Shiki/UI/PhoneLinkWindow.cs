using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiki.Core;
using Shiki.Core.Phone;

namespace Shiki.UI;

/// <summary>
/// Connecting a phone, once: a QR code gets Shiki for Android onto it (over the Wi-Fi, from this
/// PC), the phone finds the PC by itself, and the person checks the same code shows on both.
/// </summary>
public sealed class PhoneLinkWindow : Window
{
    readonly PhoneLink _link;
    readonly StackPanel _request = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock _code = new() { FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
    readonly TextBlock _who = UiKit.Body("");
    readonly TextBlock _status = UiKit.Body("", 12.5);
    PairRequest? _pending;

    public PhoneLinkWindow(PhoneLink link)
    {
        _link = link;
        Title = "Connect your phone";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);
        link.Listen();

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        stack.Children.Add(UiKit.Title("Connect your phone"));
        stack.Children.Add(UiKit.Body("Once it's connected, photos and files go from your phone to this PC at full quality, over your Wi-Fi. You do this once."));

        stack.Children.Add(UiKit.Heading("1. Get Shiki on your phone"));
        var url = link.StartAppDownload();
        if (url != null)
        {
            stack.Children.Add(UiKit.Body("Scan this with your phone's camera (Android), download Shiki and install it. Your phone needs to be on the same Wi-Fi as this PC."));
            var image = new Image { Width = 200, Height = 200, Source = Png(QrTools.MakePng(url, 10)) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            stack.Children.Add(new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(Theme.Editor.Pixel ? 0 : 14),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 12, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = image,
            });
            var address = UiKit.Body(url, 11.5);
            address.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(address);
        }
        else
        {
            stack.Children.Add(UiKit.Body(File.Exists(PhoneLink.ApkPath)
                ? "This PC doesn't seem to be on a home network right now. Connect it to your Wi-Fi (or a cable to the same router), then open this again."
                : "Shiki for Android isn't in this copy of Shiki. Update Shiki, then open this again."));
        }

        stack.Children.Add(UiKit.Heading("2. Open it and tap this PC"));
        stack.Children.Add(UiKit.Body($"Shiki on the phone finds \"{link.PcName}\" by itself. If Windows asks, press Allow. On a Wi-Fi Windows calls public (a phone's hotspot, say), tick public networks too, or the phone can't reach this PC."));

        _request.Margin = new Thickness(0, 14, 0, 0);
        _request.Children.Add(UiKit.Heading("3. Check the code"));
        _request.Children.Add(_who);
        _code.FontFamily = Theme.TextFont;
        _code.Foreground = Theme.Brush(Theme.Editor.Text);
        _request.Children.Add(_code);
        var buttons = UiKit.Row(UiKit.Button("Pair", Accept, primary: true), UiKit.Button("Not now", Decline));
        _request.Children.Add(buttons);
        stack.Children.Add(_request);

        _status.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(_status);
        var close = UiKit.Button("Close", Close);
        close.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(close);
        Content = stack;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) =>
        {
            _pending?.Decline();
            link.StopAppDownload();
        };
    }

    /// <summary>A phone asks to pair: show its code, for the person to compare.</summary>
    public void ShowRequest(PairRequest request)
    {
        _pending?.Decline();
        _pending = request;
        _who.Text = $"\"{request.Name}\" wants to connect. Pair only if your phone shows this same code:";
        _code.Text = request.Code;
        _request.Visibility = Visibility.Visible;
        _status.Text = "";
        Activate();
    }

    void Accept()
    {
        if (_pending == null) return;
        var name = _pending.Name;
        _pending.Accept();
        _pending = null;
        _request.Visibility = Visibility.Collapsed;
        _status.Text = $"Connected to {name}. Share anything to Shiki on the phone, or say \"get my last photo from my phone\". New photos and videos come over by themselves when you're on this Wi-Fi (switch that off in the phone app). They land in {_link.PhotoFolder}.";
    }

    void Decline()
    {
        _pending?.Decline();
        _pending = null;
        _request.Visibility = Visibility.Collapsed;
        _status.Text = "Not paired.";
    }

    static BitmapImage Png(byte[] png)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
