using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConvertedBro.Core;
using ConvertedBro.Shell;
using ConvertedBro.UI;

namespace ConvertedBro;

/// <summary>
/// Command line:
///   ConvertedBro                      desktop buddy + tray icon
///   ConvertedBro run &lt;action&gt; files   run an action (what the Explorer menu calls)
///   ConvertedBro menu files           show the radial menu at the cursor
///   ConvertedBro files...             same as menu (drag files onto the exe, "Open with")
///   ConvertedBro --register / --unregister   add or remove the Explorer menu
/// </summary>
public partial class App : Application
{
    // Explorer starts one process per selected file; collect them for a moment so a
    // 10-file selection becomes one job (and one merged PDF), not ten.
    static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(700);

    SingleInstance? _instance;
    Settings _settings = new();
    CatWindow? _buddy;
    TrayIcon? _tray;
    GlobalMouseHook? _hook;
    ConvertWheel? _dragWheel;
    BroMenu? _menu;
    Point _menuAnchor;
    bool _headless;
    readonly Dictionary<string, List<string>> _pending = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _batchTimer = new() { Interval = BatchWindow };
    readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    static string ExePath => Environment.ProcessPath!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled UI error", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Unhandled error", ex.ExceptionObject as Exception);

        var args = e.Args;
        if (args.Length == 2 && args[0] == "--export-sprites")
        {
            CatSprites.Export(args[1]);
            Shutdown();
            return;
        }
        if (args.Length > 0 && args[0] is "--register" or "--unregister")
        {
            if (args[0] == "--register") ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(CatSprites.Skin(Settings.Load().Cat)));
            else ContextMenuRegistrar.Unregister();
            Shutdown();
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsPrimary)
        {
            if (!SingleInstance.SendToPrimary(args.Length == 0 ? ["show"] : args))
                Log.Error($"Could not reach the running instance; dropped: {string.Join(" ", args)}");
            Shutdown();
            return;
        }

        _instance.MessageReceived += a => Dispatcher.BeginInvoke(() => Handle(a));
        _instance.StartServer();
        _settings = Settings.Load();
        Theme.Style = _settings.Style;
        _batchTimer.Tick += (_, _) => FlushBatches();

        JobRunner.JobStarted += job => Dispatcher.BeginInvoke(() =>
        {
            ToastHost.Show(new ToastWindow(job));
            _buddy?.SetBusy(JobRunner.ActiveCount);
        });
        JobRunner.JobFinished += job => Dispatcher.BeginInvoke(() =>
        {
            _buddy?.SetBusy(JobRunner.ActiveCount);
            if (job.State == JobState.Done) _buddy?.Celebrate();
        });

        if (args.Length == 0)
        {
            EnterBuddyMode();
        }
        else
        {
            // Launched from Explorer: do the work quietly and exit when idle.
            _headless = true;
            _idleTimer.Tick += (_, _) => ExitIfIdle();
            _idleTimer.Start();
        }
        Handle(args);
    }

    void Handle(string[] args)
    {
        if (args.Length == 0 || args[0] == "show")
        {
            EnterBuddyMode();
            SetBuddyVisible(true);
            return;
        }

        switch (args[0])
        {
            case "run" when args.Length >= 3:
                Queue(args[1], args[2..]);
                break;
            case "menu" when args.Length >= 2:
                Queue("menu", args[1..]);
                break;
            default:
                var files = args.Where(a => File.Exists(a) || Directory.Exists(a)).ToArray();
                if (files.Length > 0) Queue("menu", files);
                else Log.Error($"Unknown command: {string.Join(" ", args)}");
                break;
        }
    }

    void Queue(string key, IEnumerable<string> files)
    {
        if (!_pending.TryGetValue(key, out var list)) _pending[key] = list = [];
        list.AddRange(files);
        _batchTimer.Stop();
        _batchTimer.Start();
    }

    void FlushBatches()
    {
        _batchTimer.Stop();
        var batches = _pending.ToList();
        _pending.Clear();
        foreach (var (key, raw) in batches)
        {
            var files = FileList.Expand(raw);
            if (key == "menu")
            {
                NativeMethods.GetCursorPos(out var cursor);
                ShowWheel(files, new Point(cursor.X, cursor.Y));
                continue;
            }
            var action = ActionCatalog.Find(key);
            if (action == null)
            {
                Log.Error($"Unknown action {key}");
                continue;
            }
            StartJob(action, files);
        }
    }

    void StartJob(BroAction action, List<string> files)
    {
        // Merges follow Explorer's name order, since launch order is random.
        if (action.Combine) files.Sort(NaturalComparer.Instance);
        if (JobRunner.Start(action, files) == null)
            ToastHost.Show(ToastWindow.Message("Nothing to do", $"None of those files can be used for \"{action.Label}\"."));
    }

    public void ShowWheel(IReadOnlyList<string> files, Point centerPx)
    {
        if (files.Count == 0) return;
        var wheel = new ConvertWheel(ConvertWheel.WheelMode.Click, centerPx, files);
        wheel.ActionChosen += (action, chosen) => StartJob(action, chosen.ToList());
        wheel.Show();
    }

    /// <summary>Shift + drag: open the wheel right under the cursor so the file can be dropped on a wedge.</summary>
    void OpenDragWheel(int xPx, int yPx)
    {
        _dragWheel?.CloseAnimated();
        var wheel = new ConvertWheel(ConvertWheel.WheelMode.Drag, new Point(xPx, yPx));
        wheel.ActionChosen += (action, files) => StartJob(action, files.ToList());
        wheel.Closed += (_, _) => { if (_dragWheel == wheel) _dragWheel = null; };
        _dragWheel = wheel;
        _buddy?.Notice(true);
        wheel.Show();
    }

    CatSkin Skin => CatSprites.Skin(_settings.Cat);

    void ShowMenu(Point anchorPx)
    {
        _menu?.CloseAnimated();
        _menuAnchor = anchorPx;
        bool visible = _buddy?.IsVisible ?? false;
        _menu = new BroMenu(anchorPx, "Drop files on me, or Shift + drag one anywhere.",
        [
            new MenuEntry("\uE8E5", "Convert files…", PickFiles),
            new MenuEntry(visible ? "\uE7B3" : "\uE890", visible ? "Hide the cat" : "Show the cat", () => SetBuddyVisible(!visible)),
            null,
            new MenuChoice("Cat", CatSprites.Skins.Select(k => new ChoiceOption(k.Id, $"{k.Name}: {k.Description}", CatSprites.Head(k))).ToList(),
                () => _settings.Cat, SetCat),
            new MenuChoice("Look", [new ChoiceOption("arcade", "Arcade", Font: Theme.PixelFont), new ChoiceOption("glass", "Glass", Font: Theme.DisplayFont)],
                () => _settings.Style, SetStyle),
            null,
            new MenuEntry("\uE8B7", "Right-click menu in Explorer", ToggleExplorerMenu, ContextMenuRegistrar.IsRegistered),
            new MenuEntry("\uE7C9", "Shift + drag opens the wheel", ToggleShiftDrag, () => _settings.ShiftDragSummon),
            new MenuEntry("\uE7E8", "Start with Windows", () => StartupRegistration.Set(!StartupRegistration.IsEnabled(), ExePath), StartupRegistration.IsEnabled),
            null,
            new MenuEntry("\uE81C", "Open log folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.DataDir}\"") { UseShellExecute = true })),
            new MenuEntry("\uE711", "Quit", () => Shutdown()),
        ], CatSprites.Head(Skin));
        _menu.Closed += (_, _) => _menu = null;
        _menu.Show();
    }

    void SetCat(string id)
    {
        _settings.Cat = id;
        _settings.Save();
        _buddy?.SetSkin(Skin);
        _tray?.SetCat(Skin);
        // The Explorer menu shows the cat too.
        if (ContextMenuRegistrar.IsRegistered()) ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(Skin));
    }

    void SetStyle(string id)
    {
        if (_settings.Style == id) return;
        _settings.Style = id;
        _settings.Save();
        Theme.Style = id;
        // Reopen the menu so the change shows right away.
        var anchor = _menuAnchor;
        if (_menu is { } menu)
        {
            menu.Closed += (_, _) => Dispatcher.BeginInvoke(() => ShowMenu(anchor));
            menu.CloseAnimated();
        }
    }

    void ToggleExplorerMenu()
    {
        bool on = !ContextMenuRegistrar.IsRegistered();
        if (on) ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile(Skin));
        else ContextMenuRegistrar.Unregister();
        ToastHost.Show(ToastWindow.Message(on ? "Added to Explorer" : "Removed from Explorer",
            on ? "Right-click a file, choose Show more options, then Convert with Bro." : "Convert with Bro is no longer in the right-click menu."));
    }

    void ToggleShiftDrag()
    {
        _settings.ShiftDragSummon = !_settings.ShiftDragSummon;
        _settings.Save();
        if (_settings.ShiftDragSummon) _hook?.Start(); else _hook?.Stop();
    }

    // ---- Buddy mode ----

    void EnterBuddyMode()
    {
        if (_tray != null) return;
        _headless = false;
        _idleTimer.Stop();

        _tray = new TrayIcon(Skin);
        _tray.Clicked += () => SetBuddyVisible(!(_buddy?.IsVisible ?? false));
        _tray.MenuRequested += ShowMenu;

        _buddy = new CatWindow(_settings);
        _buddy.FilesDropped += (paths, center) => ShowWheel(FileList.Expand(paths), center);
        _buddy.MenuRequested += ShowMenu;
        _buddy.PickFilesRequested += PickFiles;
        ToastHost.Avoid = () => _buddy is { IsVisible: true } b ? new Rect(b.Left, b.Top, b.Width, b.Height) : null;
        if (_settings.BuddyVisible) _buddy.Show();

        _hook = new GlobalMouseHook();
        _hook.ShiftDragStarted += (x, y) => Dispatcher.BeginInvoke(() => OpenDragWheel(x, y));
        _hook.DragEnded += () => Dispatcher.BeginInvoke(() =>
        {
            _dragWheel?.OnDragEnded();
            _buddy?.Notice(false);
        });
        if (_settings.ShiftDragSummon) _hook.Start();
    }

    void SetBuddyVisible(bool visible)
    {
        if (_buddy == null) return;
        if (visible) _buddy.Show(); else _buddy.Hide();
        _settings.BuddyVisible = visible;
        _settings.Save();
    }

    void PickFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick files for the bro",
            Multiselect = true,
            Filter = "Everything I can convert|" + string.Join(";", FileKinds.AllExtensions.Select(x => "*" + x)) + "|All files|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        NativeMethods.GetCursorPos(out var cursor);
        var center = _buddy is { IsVisible: true } b ? b.CenterPx() : new Point(cursor.X, cursor.Y);
        ShowWheel(dialog.FileNames, center);
    }

    void ExitIfIdle()
    {
        bool idle = _headless && JobRunner.ActiveCount == 0 && _pending.Count == 0
            && !Windows.OfType<Window>().Any(w => w.IsVisible);
        if (idle) Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
