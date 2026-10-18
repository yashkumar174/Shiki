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
    BuddyWindow? _buddy;
    TrayIcon? _tray;
    GlobalMouseHook? _hook;
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
        if (args.Length > 0 && args[0] is "--register" or "--unregister")
        {
            if (args[0] == "--register") ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile());
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
                ShowRadial(files, new Point(cursor.X, cursor.Y));
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

    public void ShowRadial(IReadOnlyList<string> files, Point centerPx)
    {
        if (files.Count == 0) return;
        _buddy?.CancelReturn();
        var menu = new RadialMenuWindow(files, ActionCatalog.For(files), centerPx);
        menu.ActionChosen += action => StartJob(action, files.ToList());
        menu.Closed += (_, _) => _buddy?.ReturnHome();
        menu.Show();
    }

    // ---- Buddy mode ----

    void EnterBuddyMode()
    {
        if (_tray != null) return;
        _headless = false;
        _idleTimer.Stop();

        _tray = new TrayIcon();
        _tray.ToggleBuddy += () => SetBuddyVisible(!(_buddy?.IsVisible ?? false));
        _tray.PickFiles += PickFiles;
        _tray.ExplorerMenuChanged += on =>
        {
            if (on) ContextMenuRegistrar.Register(ExePath, BroArt.EnsureIconFile());
            else ContextMenuRegistrar.Unregister();
            ToastHost.Show(ToastWindow.Message(on ? "Explorer menu added" : "Explorer menu removed",
                on ? "Right-click a file → Show more options → Convert with Bro." : "The right-click entry is gone."));
            SyncTray();
        };
        _tray.StartupChanged += on => { StartupRegistration.Set(on, ExePath); SyncTray(); };
        _tray.ShiftDragChanged += on =>
        {
            _settings.ShiftDragSummon = on;
            _settings.Save();
            if (on) _hook?.Start(); else _hook?.Stop();
        };
        _tray.OpenDataFolder += () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.DataDir}\"") { UseShellExecute = true });
        _tray.Quit += () => Shutdown();

        _buddy = new BuddyWindow(_settings);
        _buddy.FilesDropped += (paths, center) => ShowRadial(FileList.Expand(paths), center);
        _buddy.MenuRequested += () => _tray.ShowMenuAtCursor();
        _buddy.PickFilesRequested += PickFiles;
        ToastHost.Avoid = () => _buddy is { IsVisible: true } b ? new Rect(b.Left, b.Top, b.Width, b.Height) : null;
        if (_settings.BuddyVisible) _buddy.Show();

        _hook = new GlobalMouseHook();
        _hook.ShiftDragStarted += (x, y) => Dispatcher.BeginInvoke(() =>
        {
            if (_buddy is { } b && _settings.BuddyVisible) b.SummonNear(x, y);
        });
        _hook.DragEnded += () => Dispatcher.BeginInvoke(() => _buddy?.OnSummonDragEnded());
        if (_settings.ShiftDragSummon) _hook.Start();

        SyncTray();
    }

    void SetBuddyVisible(bool visible)
    {
        if (_buddy == null) return;
        if (visible) _buddy.Show(); else _buddy.Hide();
        _settings.BuddyVisible = visible;
        _settings.Save();
        SyncTray();
    }

    void SyncTray() => _tray?.Sync(_buddy?.IsVisible ?? false, ContextMenuRegistrar.IsRegistered(),
        StartupRegistration.IsEnabled(), _settings.ShiftDragSummon);

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
        ShowRadial(dialog.FileNames, center);
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
