using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConvertedBro.Core;

namespace ConvertedBro.UI;

/// <summary>Type or paste a link or any text; the QR code updates as you type. Save it or copy it.</summary>
public sealed class QrWindow : Window
{
    // QR codes top out around 2,900 bytes; past ~1,000 characters they're too dense to scan comfortably.
    const int MaxLength = 1500;

    readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 74, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = MaxLength };
    readonly Image _code = new() { Width = 260, Height = 260 };
    readonly Border _frame;
    readonly TextBlock _info = UiKit.Body("", 11.5);
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    byte[]? _png;

    public QrWindow(string initial = "")
    {
        Title = "QR code";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        stack.Children.Add(UiKit.Title("QR code"));
        stack.Children.Add(UiKit.Body("Type or paste a link or any text. Phones open links straight away."));
        var field = UiKit.Field(_text);
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        field.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(field);

        RenderOptions.SetBitmapScalingMode(_code, BitmapScalingMode.NearestNeighbor);
        _frame = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(Theme.Editor.Pixel ? 0 : 14),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _code,
        };
        stack.Children.Add(_frame);
        _info.HorizontalAlignment = HorizontalAlignment.Center;
        _info.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_info);

        var buttons = UiKit.Row(UiKit.Button("Save PNG", Save, primary: true), UiKit.Button("Copy", Copy), UiKit.Button("Close", Close));
        buttons.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(buttons);
        Content = stack;

        _text.Text = initial.Length > MaxLength ? initial[..MaxLength] : initial;
        _text.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Render(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save(); e.Handled = true; }
        };
        Loaded += (_, _) => { _text.Focus(); _text.CaretIndex = _text.Text.Length; };
        Render();
    }

    void Render()
    {
        var text = _text.Text;
        if (text.Trim().Length == 0)
        {
            _png = null;
            _code.Source = null;
            _frame.Opacity = 0.25;
            _info.Text = "Nothing to encode yet.";
            return;
        }
        try
        {
            _png = QrTools.MakePng(text);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(_png);
            bitmap.EndInit();
            bitmap.Freeze();
            _code.Source = bitmap;
            _frame.Opacity = 1;
            _info.Text = QrTools.IsLink(text) ? "A link: scanning opens it." : $"{text.Length} characters of text.";
        }
        catch (Exception ex)
        {
            Log.Error("QR code could not be made", ex);
            _png = null;
            _frame.Opacity = 0.25;
            _info.Text = "That's too much for one QR code. Try a shorter text or a link.";
        }
    }

    /// <summary>Saved with the other results, named after the text so it's easy to find again.</summary>
    void Save()
    {
        if (_png == null) return;
        var folder = OutputPaths.CreatedFolder;
        var name = Safe(_text.Text);
        var file = OutputPaths.Next(Path.Combine(folder, $"QR {name}.png"), ".png");
        File.WriteAllBytes(file, _png);
        ToastHost.Show(ToastWindow.Message("QR code saved", Path.GetFileName(file), null,
            ("Open", () => Launch(file, null)),
            ("Show in folder", () => Launch("explorer.exe", $"/select,\"{file}\""))));
        Close();
    }

    void Copy()
    {
        if (_png == null) return;
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ConvertedBro")).FullName;
        var file = Path.Combine(folder, $"QR {Safe(_text.Text)}.png");
        File.WriteAllBytes(file, _png);
        ClipboardTools.CopyImage(file);
        _info.Text = "Copied. Paste it anywhere.";
    }

    static string Safe(string text)
    {
        var name = text.Trim();
        if (Uri.TryCreate(name, UriKind.Absolute, out var uri) && uri.Host.Length > 0) name = uri.Host + uri.AbsolutePath.TrimEnd('/');
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, ' ');
        name = string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Length > 40 ? name[..40].Trim() : name;
    }

    static void Launch(string file, string? args)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {file}", ex);
        }
    }
}
