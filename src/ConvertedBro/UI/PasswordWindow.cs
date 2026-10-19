using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConvertedBro.Core;

namespace ConvertedBro.UI;

/// <summary>Asks for a PDF password, to unlock a copy or to lock one.</summary>
public sealed class PasswordWindow : Window
{
    public enum Purpose { Unlock, Lock }

    readonly IReadOnlyList<string> _files;
    readonly Purpose _purpose;
    readonly PasswordBox _password = new();
    readonly PasswordBox _confirm = new();
    readonly TextBlock _error = UiKit.Error();

    public PasswordWindow(IReadOnlyList<string> files, Purpose purpose)
    {
        _files = files;
        _purpose = purpose;
        bool unlock = purpose == Purpose.Unlock;
        Title = unlock ? "Unlock PDF" : "Lock PDF";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        UiKit.Prepare(this);

        var names = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} PDFs";
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        stack.Children.Add(UiKit.Title(unlock ? "Unlock PDF" : "Lock PDF"));
        stack.Children.Add(UiKit.Body(unlock
            ? $"Enter the password for {names}. You'll get a copy that opens without it; the original stays as it is."
            : $"Choose a password for {names}. You'll get a copy that asks for it when opened."));

        stack.Children.Add(UiKit.Heading("Password"));
        stack.Children.Add(UiKit.Field(_password));
        if (!unlock)
        {
            stack.Children.Add(UiKit.Heading("Type it again"));
            stack.Children.Add(UiKit.Field(_confirm));
        }
        stack.Children.Add(_error);

        var buttons = UiKit.Row(UiKit.Button(unlock ? "Unlock" : "Lock", Submit, primary: true), UiKit.Button("Cancel", Close));
        buttons.Margin = new Thickness(0, 20, 0, 0);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) => _password.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Submit();
            else if (e.Key == Key.Escape) Close();
        };
        _password.PasswordChanged += (_, _) => _error.Visibility = Visibility.Collapsed;
    }

    void Show(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    void Submit()
    {
        var password = _password.Password;
        if (password.Length == 0)
        {
            Show("Enter a password first.");
            return;
        }

        if (_purpose == Purpose.Lock)
        {
            if (password != _confirm.Password)
            {
                Show("The two passwords don't match.");
                return;
            }
            Start("Lock", "PDF", (input, _) => PdfSecurity.Lock(input, password));
            return;
        }

        // Check the password here, so a typo gets an instant answer instead of a failed job.
        var check = PdfSecurity.TryPassword(_files[0], password);
        if (check == PdfSecurity.Check.WrongPassword)
        {
            Show("That password didn't open it. Check for caps lock and try again.");
            _password.SelectAll();
            return;
        }
        if (check == PdfSecurity.Check.NotProtected)
        {
            Show("This PDF doesn't have a password. Nothing to unlock.");
            return;
        }
        Start("Unlock", "Unlock", (input, progress) => PdfSecurity.Unlock(input, password, progress));
    }

    void Start(string label, string shortName, Func<string, IProgress<double>, string> work)
    {
        var action = new BroAction
        {
            Id = $"pdf-{label.ToLowerInvariant()}-run",
            Label = label,
            Short = shortName,
            Hint = "",
            Kind = FileKind.Pdf,
            AcceptsExtension = _ => true,
            Run = (inputs, progress, ct) => Task.Run<IReadOnlyList<string>>(() => inputs.Select(i => work(i, progress)).ToList(), ct),
        };
        JobRunner.Start(action, _files);
        Close();
    }
}
