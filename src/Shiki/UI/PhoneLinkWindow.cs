using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiki.Core;
using Shiki.Core.Phone;

namespace Shiki.UI;

/// <summary>
/// Connecting a phone, once. iPhone: the Shiki app (in Expo Go) scans a QR code and it's done.
/// Android: a QR code gets Shiki for Android onto it (from this PC), the phone finds the PC by
/// itself, and the person checks the same code shows on both.
/// </summary>
public sealed class PhoneLinkWindow : Window
{
    readonly PhoneLink _link;
    readonly StackPanel _iphone = new(), _android = new();
    readonly Border _iphoneChip, _androidChip;
    readonly StackPanel _request = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock _code = new() { FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
    readonly TextBlock _who = UiKit.Body("");
    readonly TextBlock _status = UiKit.Body("", 12.5);
    PairRequest? _pending;
    bool _androidReady;

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
        _iphoneChip = UiKit.Button("iPhone", () => Show(iphone: true));
        _androidChip = UiKit.Button("Android", () => Show(iphone: false));
        var chips = UiKit.Row(_iphoneChip, _androidChip);
        chips.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(chips);
        stack.Children.Add(_iphone);
        stack.Children.Add(_android);

        // ---- iPhone ----
        _iphone.Children.Add(UiKit.Heading("1. Open Shiki on your iPhone"));
        _iphone.Children.Add(UiKit.Body("Shiki for iPhone runs inside Expo Go (free, on the App Store) until it's in the App Store itself."));
        _iphone.Children.Add(UiKit.Heading("2. Tap Scan, and scan this"));
        var pairing = link.StartIphonePairing();
        if (pairing != null)
        {
            _iphone.Children.Add(Qr(pairing, 9));
            _iphone.Children.Add(UiKit.Body("The code works once, for 15 minutes. Your iPhone needs to be on the same Wi-Fi as this PC (or this PC on its hotspot).", 12));
            if (link.PairingByHand is { } hand)
            {
                _iphone.Children.Add(UiKit.Heading("Can't scan it? Type it instead"));
                _iphone.Children.Add(UiKit.Body("In the app, tap Type the code instead, and enter:", 12.5));
                var digits = new TextBlock
                {
                    Text = $"{hand.Host}    {hand.Pin[..3]} {hand.Pin[3..]}",
                    FontFamily = Theme.PixelFont,
                    FontSize = 26,
                    Foreground = Theme.Brush(Theme.Editor.Text),
                    Margin = new Thickness(0, 8, 0, 0),
                };
                _iphone.Children.Add(digits);
                _iphone.Children.Add(UiKit.Body("The address first, then the six digits.", 11.5));
            }
        }
        else _iphone.Children.Add(UiKit.Body("This PC doesn't seem to be on a home network right now. Connect it to your Wi-Fi (or your iPhone's hotspot), then open this again."));

        // ---- Android (made when first shown: it starts the app download) ----
        _request.Margin = new Thickness(0, 14, 0, 0);
        _request.Children.Add(UiKit.Heading("3. Check the code"));
        _request.Children.Add(_who);
        _code.FontFamily = Theme.TextFont;
        _code.Foreground = Theme.Brush(Theme.Editor.Text);
        _request.Children.Add(_code);
        _request.Children.Add(UiKit.Row(UiKit.Button("Pair", Accept, primary: true), UiKit.Button("Not now", Decline)));

        var hint = UiKit.Body("If Windows asks, press Allow. On a Wi-Fi Windows calls public (a phone's hotspot, say), tick public networks too, or the phone can't reach this PC.", 12);
        stack.Children.Add(hint);
        stack.Children.Add(FirewallGate(hint));
        _status.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(_status);
        var close = UiKit.Button("Close", Close);
        close.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(close);
        Content = stack;
        Show(iphone: true);

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) =>
        {
            _pending?.Decline();
            link.StopAppDownload();
            link.StopIphonePairing();
        };
    }

    /// <summary>
    /// When Windows Firewall would keep the phone out: says so, with a button that asks Windows to
    /// let Shiki in (and the general "if Windows asks" hint goes, since this replaces it).
    /// </summary>
    UIElement FirewallGate(TextBlock hint)
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        var said = UiKit.Body("", 12.5);
        said.Foreground = new SolidColorBrush(Dashboard.DashPalette.Eye);
        bool busy = false, isPublic = false;
        var button = UiKit.Button("Let your phone in", async () =>
        {
            if (busy) return;
            busy = true;
            said.Text = "Press Yes when Windows asks.";
            var problem = await Shell.Firewall.LetIn(isPublic);
            busy = false;
            if (problem != null) { said.Text = problem; return; }
            panel.Visibility = Visibility.Collapsed;
            _status.Text = "Done: your phone can reach this PC now.";
        }, primary: true);
        button.Margin = new Thickness(0, 10, 0, 0);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(said);
        panel.Children.Add(button);
        _ = Task.Run(Shell.Firewall.Look).ContinueWith(t =>
        {
            var look = t.Result;
            Log.Info("Firewall for the phone link: " + look.Detail);
            if (look.Verdict != Shell.Firewall.Verdict.Closed) return;
            isPublic = look.PublicNetwork;
            said.Text = look.PublicNetwork
                ? "Windows Firewall is keeping your phone out: Windows calls this network public and Shiki isn't allowed in on it. Let it in once, and Windows asks you to confirm."
                : "Windows Firewall is keeping your phone out: Shiki isn't allowed in on this network. Let it in once, and Windows asks you to confirm.";
            hint.Visibility = Visibility.Collapsed;
            panel.Visibility = Visibility.Visible;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return panel;
    }

    void Show(bool iphone)
    {
        if (!iphone && !_androidReady) BuildAndroid();
        _iphone.Visibility = iphone ? Visibility.Visible : Visibility.Collapsed;
        _android.Visibility = iphone ? Visibility.Collapsed : Visibility.Visible;
        UiKit.Select(_iphoneChip, iphone);
        UiKit.Select(_androidChip, !iphone);
    }

    void BuildAndroid()
    {
        _androidReady = true;
        _android.Children.Add(UiKit.Heading("1. Get Shiki on your phone"));
        var url = _link.StartAppDownload();
        if (url != null)
        {
            _android.Children.Add(UiKit.Body("Scan this with your phone's camera, download Shiki and install it. Your phone needs to be on the same Wi-Fi as this PC."));
            _android.Children.Add(Qr(url, 10));
            var address = UiKit.Body(url, 11.5);
            address.HorizontalAlignment = HorizontalAlignment.Center;
            _android.Children.Add(address);
        }
        else
        {
            _android.Children.Add(UiKit.Body(File.Exists(PhoneLink.ApkPath)
                ? "This PC doesn't seem to be on a home network right now. Connect it to your Wi-Fi (or a cable to the same router), then open this again."
                : "Shiki for Android isn't in this copy of Shiki. Update Shiki, then open this again."));
        }
        _android.Children.Add(UiKit.Heading("2. Open it and tap this PC"));
        _android.Children.Add(UiKit.Body($"Shiki on the phone finds \"{_link.PcName}\" by itself."));
        _android.Children.Add(_request);
    }

    static Border Qr(string text, int pixels)
    {
        var image = new Image { Width = 200, Height = 200, Source = Png(QrTools.MakePng(text, pixels)) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(Theme.Editor.Pixel ? 0 : 14),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 12, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = image,
        };
    }

    /// <summary>A phone asks to pair (Android): show its code, for the person to compare.</summary>
    public void ShowRequest(PairRequest request)
    {
        Show(iphone: false);
        _pending?.Decline();
        _pending = request;
        _who.Text = $"\"{request.Name}\" wants to connect. Pair only if your phone shows this same code:";
        _code.Text = request.Code;
        _request.Visibility = Visibility.Visible;
        _status.Text = "";
        Activate();
    }

    /// <summary>A phone is connected (an iPhone that scanned the code, or an Android that was paired).</summary>
    public void ShowPaired(PairedPhone phone)
    {
        _status.Text = phone.Kind == "iphone"
            ? $"Connected to {phone.Name}. Pick photos in the app to send them, or say \"get my last photo from my phone\" while Shiki is open on it. They land in {_link.PhotoFolder}."
            : $"Connected to {phone.Name}. Share anything to Shiki on the phone, or say \"get my last photo from my phone\". New photos and videos come over by themselves when you're on this Wi-Fi (switch that off in the phone app). They land in {_link.PhotoFolder}.";
        Activate();
    }

    void Accept()
    {
        if (_pending == null) return;
        _pending.Accept();
        _pending = null;
        _request.Visibility = Visibility.Collapsed;
        _status.Text = "Connecting…";
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
