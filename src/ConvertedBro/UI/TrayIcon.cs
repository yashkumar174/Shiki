using System;
using System.Windows.Forms;

namespace ConvertedBro.UI;

/// <summary>The notification-area icon. Its menu is also what right-clicking the bro shows.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _toggleBuddy, _explorerMenu, _startup, _shiftDrag;

    public event Action? ToggleBuddy;
    public event Action? PickFiles;
    public event Action<bool>? ExplorerMenuChanged;
    public event Action<bool>? StartupChanged;
    public event Action<bool>? ShiftDragChanged;
    public event Action? OpenDataFolder;
    public event Action? Quit;

    public TrayIcon()
    {
        _toggleBuddy = new ToolStripMenuItem("Hide bro", null, (_, _) => ToggleBuddy?.Invoke());
        _explorerMenu = new ToolStripMenuItem("Right-click menu in Explorer") { CheckOnClick = true };
        _explorerMenu.Click += (_, _) => ExplorerMenuChanged?.Invoke(_explorerMenu.Checked);
        _startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startup.Click += (_, _) => StartupChanged?.Invoke(_startup.Checked);
        _shiftDrag = new ToolStripMenuItem("Shift + drag calls the bro") { CheckOnClick = true };
        _shiftDrag.Click += (_, _) => ShiftDragChanged?.Invoke(_shiftDrag.Checked);

        _menu.Items.Add(new ToolStripMenuItem("Convert files…", null, (_, _) => PickFiles?.Invoke()) { Font = new System.Drawing.Font(_menu.Font, System.Drawing.FontStyle.Bold) });
        _menu.Items.Add(_toggleBuddy);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_explorerMenu);
        _menu.Items.Add(_shiftDrag);
        _menu.Items.Add(_startup);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenDataFolder?.Invoke()));
        _menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = BroArt.LoadIcon(SystemInformation.SmallIconSize.Width),
            Text = "Converted Bro — drop files on the bro",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleBuddy?.Invoke(); };
    }

    public void Sync(bool buddyVisible, bool explorerMenu, bool startup, bool shiftDrag)
    {
        _toggleBuddy.Text = buddyVisible ? "Hide bro" : "Show bro";
        _explorerMenu.Checked = explorerMenu;
        _startup.Checked = startup;
        _shiftDrag.Checked = shiftDrag;
    }

    /// <summary>Opens the menu at the mouse, for right-clicks on the bro himself.</summary>
    public void ShowMenuAtCursor() => _menu.Show(Cursor.Position);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
