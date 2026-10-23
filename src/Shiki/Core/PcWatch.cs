using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Shiki.Core;

/// <summary>
/// What the PC is up to, for the cat to react to: the battery running low or being plugged in,
/// the processor working flat out for a while, a download finishing, and you coming back after
/// being away. Raised on the UI thread. Checks are light: a few numbers every 20 seconds and a
/// folder watcher.
/// </summary>
public sealed class PcWatch : IDisposable
{
    const double BusyPercent = 85, LowBattery = 0.2;
    static readonly TimeSpan Away = TimeSpan.FromMinutes(10);
    static readonly string[] Partial = [".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload", ".!ut"];

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    readonly DispatcherTimer _input = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly FileSystemWatcher? _downloads;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    long _idle, _kernel, _user;
    int _busyCount;
    bool? _low, _plugged;
    bool _away;
    readonly Dictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);

    public bool BatteryLow { get; private set; }
    public bool Busy { get; private set; }

    /// <summary>Battery low or the processor busy changed.</summary>
    public event Action? StateChanged;
    public event Action? PluggedIn;
    public event Action<string>? Downloaded;
    /// <summary>Any finished download, however small (Downloaded is only for the ones worth a cheer).</summary>
    public event Action<string>? FileArrived;
    public event Action? CameBack;

    public PcWatch()
    {
        GetSystemTimes(out _idle, out _kernel, out _user);
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        _input.Tick += (_, _) => CheckAway();
        _input.Start();
        try
        {
            var folder = Ai.FileFinder.PlacePath("downloads", null);
            if (folder != null && Directory.Exists(folder))
            {
                _downloads = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size };
                _downloads.Created += (_, e) => Arrived(e.FullPath);
                _downloads.Renamed += (_, e) => Arrived(e.FullPath);
                _downloads.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not watch Downloads", ex);
        }
        Check();
    }

    void Check()
    {
        // Battery: only matters on battery power.
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        if (!power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery))
        {
            bool plugged = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            bool low = !plugged && power.BatteryLifePercent < LowBattery;
            if (_plugged == false && plugged) PluggedIn?.Invoke();
            _plugged = plugged;
            if (_low != low) { _low = low; BatteryLow = low; StateChanged?.Invoke(); }
        }

        // Processor: busy for three checks in a row (about a minute), not a passing spike.
        if (GetSystemTimes(out long idle, out long kernel, out long user))
        {
            long total = kernel - _kernel + user - _user, quiet = idle - _idle;
            double percent = total > 0 ? 100.0 * (total - quiet) / total : 0;
            (_idle, _kernel, _user) = (idle, kernel, user);
            _busyCount = percent > BusyPercent ? _busyCount + 1 : 0;
            bool busy = _busyCount >= 3;
            if (busy != Busy) { Busy = busy; StateChanged?.Invoke(); }
        }
    }

    void CheckAway()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return;
        var idle = TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime);
        if (idle > Away) _away = true;
        else if (_away && idle < TimeSpan.FromSeconds(3))
        {
            _away = false;
            CameBack?.Invoke();
        }
    }

    /// <summary>A file showed up in Downloads: a finished download, once it isn't a partial and has settled.</summary>
    void Arrived(string path)
    {
        var ext = Path.GetExtension(path);
        if (Partial.Contains(ext, StringComparer.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith('.') || Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return;
        lock (_seen)
        {
            if (_seen.TryGetValue(path, out var when) && DateTime.Now - when < TimeSpan.FromMinutes(1)) return;
            _seen[path] = DateTime.Now;
        }
        // Give the browser a moment to finish writing, then check it's real and not tiny.
        var check = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(2) };
        check.Tick += (_, _) =>
        {
            check.Stop();
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0) return;
                FileArrived?.Invoke(path);
                if (info.Length > 50 * 1024) Downloaded?.Invoke(path);
            }
            catch (IOException)
            {
            }
        };
        _ui.BeginInvoke(check.Start);
    }

    public void Dispose()
    {
        _timer.Stop();
        _input.Stop();
        _downloads?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
