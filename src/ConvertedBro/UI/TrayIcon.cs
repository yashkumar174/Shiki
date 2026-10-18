using System;
using System.Windows.Forms;

namespace ConvertedBro.UI;

/// <summary>The notification-area icon. Its menu is <see cref="BroMenu"/>, opened by the app.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;

    public event Action? Clicked;
    /// <summary>Right-click; the point is the cursor in physical pixels.</summary>
    public event Action<System.Windows.Point>? MenuRequested;

    public TrayIcon()
    {
        _icon = new NotifyIcon
        {
            Icon = BroArt.LoadIcon(SystemInformation.SmallIconSize.Width),
            Text = "Converted Bro",
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Clicked?.Invoke();
            else if (e.Button == MouseButtons.Right)
            {
                Shell.NativeMethods.GetCursorPos(out var p);
                MenuRequested?.Invoke(new System.Windows.Point(p.X, p.Y));
            }
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
