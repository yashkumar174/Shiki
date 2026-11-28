using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>A browser Shiki can work in: the person's own (through the extension) or Shiki's own window.</summary>
public interface IBrowserLink
{
    /// <summary>What to call it on a card ("Chrome 158", "Shiki's browser window").</summary>
    string? Browser { get; }
    bool Connected { get; }
    /// <summary>A tab opened ("tabCreated", with its opener), closed, or was let go.</summary>
    event Action<string, JsonElement>? Event;
    /// <summary>tabs, open, activate, attach, detach, cdp, page: the same requests the extension takes.</summary>
    Task<JsonElement> Call(string op, JsonObject? args = null, CancellationToken ct = default, int seconds = 30);
    Task<JsonElement> Cdp(int tabId, string method, JsonObject? parameters = null, CancellationToken ct = default, int seconds = 30);
}

/// <summary>
/// Shiki's own browser window: Chrome (or Edge) started with a profile of Shiki's own, controlled
/// directly through the DevTools protocol, so nothing needs installing. Sites remember a sign-in
/// there like in any browser. It runs the very same page jobs as the extension (page-jobs.js).
/// </summary>
public sealed class ShikiBrowser : IBrowserLink, IDisposable
{
    static ShikiBrowser? _current;
    static readonly SemaphoreSlim Starting = new(1, 1);

    readonly ClientWebSocket _socket = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _waiting = new();
    readonly SemaphoreSlim _sending = new(1, 1);
    readonly CancellationTokenSource _stop = new();
    // The protocol names tabs by long strings; Shiki's agent counts them.
    readonly ConcurrentDictionary<int, string> _targets = new();
    readonly ConcurrentDictionary<string, int> _numbers = new();
    readonly ConcurrentDictionary<int, string> _sessions = new();
    int _next, _nextTab;
    string _pageJobs = "";

    public string? Browser { get; private set; }
    public bool Connected => _socket.State == WebSocketState.Open;
    public event Action<string, JsonElement>? Event;

    static string Profile => Path.Combine(Log.DataDir, "Browser profile");

    /// <summary>Shiki's browser, started (or found still open) and connected.</summary>
    public static async Task<ShikiBrowser> Start(CancellationToken ct)
    {
        await Starting.WaitAsync(ct);
        try
        {
            if (_current is { Connected: true } open) return open;
            _current?.Dispose();
            var browser = new ShikiBrowser();
            await browser.Launch(ct);
            return _current = browser;
        }
        finally { Starting.Release(); }
    }

    async Task Launch(CancellationToken ct)
    {
        _pageJobs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-extension", "page-jobs.js"));
        var (name, exe, _, supported) = BrowserSetup.Default();
        if (!supported || exe == null)
        {
            exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
            name = "Edge";
        }
        Browser = $"Shiki's {name} window";
        Directory.CreateDirectory(Profile);
        var portFile = Path.Combine(Profile, "DevToolsActivePort");

        // Still open from before? Use it; otherwise start it.
        var endpoint = await Endpoint(portFile, ct);
        if (endpoint == null)
        {
            try { File.Delete(portFile); } catch (Exception) { }
            Process.Start(new ProcessStartInfo(exe,
                $"--user-data-dir=\"{Profile}\" --remote-debugging-port=0 --no-first-run --no-default-browser-check --disable-features=ChromeWhatsNewUI about:blank")
            { UseShellExecute = false })?.Dispose();
            for (int i = 0; i < 60 && endpoint == null; i++)
            {
                await Task.Delay(250, ct);
                endpoint = await Endpoint(portFile, ct);
            }
            if (endpoint == null) throw new BrowserException($"{name} didn't start. Is it installed?");
        }
        await _socket.ConnectAsync(new Uri(endpoint), ct);
        _ = Listen();
        await Send("Target.setDiscoverTargets", new JsonObject { ["discover"] = true }, null, ct);
        Log.Info($"Shiki's browser connected ({Browser})");
    }

    /// <summary>The browser's DevTools address from the file it writes when it starts (if it's still answering).</summary>
    static async Task<string?> Endpoint(string portFile, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(portFile)) return null;
            var lines = await File.ReadAllLinesAsync(portFile, ct);
            if (lines.Length < 2 || !int.TryParse(lines[0], out var port)) return null;
            using var probe = new System.Net.Sockets.TcpClient();
            var connecting = probe.ConnectAsync(System.Net.IPAddress.Loopback, port);
            if (await Task.WhenAny(connecting, Task.Delay(800, ct)) != connecting || !probe.Connected) return null;
            return $"ws://127.0.0.1:{port}{lines[1]}";
        }
        catch (Exception) { return null; }
    }

    async Task Listen()
    {
        var buffer = new byte[1 << 16];
        using var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                var got = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (got.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, got.Count);
                if (!got.EndOfMessage) continue;
                var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                try { Handle(JsonDocument.Parse(json).RootElement.Clone()); } catch (JsonException) { }
            }
        }
        catch (Exception) { }
        foreach (var (_, wait) in _waiting) wait.TrySetException(new BrowserException("Shiki's browser window was closed."));
        _waiting.Clear();
    }

    void Handle(JsonElement message)
    {
        if (message.TryGetProperty("id", out var id) && _waiting.TryRemove(id.GetInt32(), out var wait))
        {
            if (message.TryGetProperty("error", out var error)) wait.TrySetException(new BrowserException(error.TryGetProperty("message", out var m) ? m.GetString() ?? "The browser said no." : "The browser said no."));
            else wait.TrySetResult(message.TryGetProperty("result", out var result) ? result : default);
            return;
        }
        if (!message.TryGetProperty("method", out var method)) return;
        var p = message.TryGetProperty("params", out var ps) ? ps : default;
        switch (method.GetString())
        {
            case "Target.targetCreated" when p.GetProperty("targetInfo").GetProperty("type").GetString() == "page":
            {
                var info = p.GetProperty("targetInfo");
                int tab = Number(info.GetProperty("targetId").GetString()!);
                int? opener = info.TryGetProperty("openerId", out var o) && o.GetString() is { } openerId && _numbers.TryGetValue(openerId, out var n) ? n : null;
                Event?.Invoke("tabCreated", JsonSerializer.SerializeToElement(new { tabId = tab, openerTabId = opener }));
                break;
            }
            case "Target.targetDestroyed":
                if (_numbers.TryGetValue(p.GetProperty("targetId").GetString()!, out var gone))
                    Event?.Invoke("tabClosed", JsonSerializer.SerializeToElement(new { tabId = gone }));
                break;
        }
    }

    int Number(string targetId) => _numbers.GetOrAdd(targetId, t =>
    {
        int n = Interlocked.Increment(ref _nextTab);
        _targets[n] = t;
        return n;
    });

    string Target(int tab) => _targets.TryGetValue(tab, out var t) ? t : throw new BrowserException("That tab isn't open any more.");

    async Task<JsonElement> Send(string method, JsonObject? parameters, string? session, CancellationToken ct, int seconds = 30)
    {
        if (!Connected) throw new BrowserException("Shiki's browser window was closed.");
        int id = Interlocked.Increment(ref _next);
        var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
        if (session != null) message["sessionId"] = session;
        var wait = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = wait;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
            await _sending.WaitAsync(ct);
            try { await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
            finally { _sending.Release(); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            using (timeout.Token.Register(() => wait.TrySetCanceled()))
                return await wait.Task;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BrowserException("The browser didn't answer in time.");
        }
        finally { _waiting.TryRemove(id, out _); }
    }

    public async Task<JsonElement> Call(string op, JsonObject? args = null, CancellationToken ct = default, int seconds = 30)
    {
        int Tab() => args?["tabId"]?.GetValue<int>() ?? throw new BrowserException("Which tab?");
        switch (op)
        {
            case "tabs":
            {
                var all = await Send("Target.getTargets", null, null, ct);
                var list = new JsonArray();
                foreach (var t in all.GetProperty("targetInfos").EnumerateArray())
                    if (t.GetProperty("type").GetString() == "page")
                        list.Add(new JsonObject { ["id"] = Number(t.GetProperty("targetId").GetString()!), ["url"] = t.GetProperty("url").GetString(), ["title"] = t.GetProperty("title").GetString() });
                return JsonSerializer.SerializeToElement(list);
            }
            case "open":
            {
                var made = await Send("Target.createTarget", new JsonObject { ["url"] = args?["url"]?.GetValue<string>() ?? "about:blank" }, null, ct);
                int tab = Number(made.GetProperty("targetId").GetString()!);
                try { await Send("Target.activateTarget", new JsonObject { ["targetId"] = Target(tab) }, null, ct); } catch (BrowserException) { }
                return JsonSerializer.SerializeToElement(new { id = tab });
            }
            case "activate":
                await Send("Target.activateTarget", new JsonObject { ["targetId"] = Target(Tab()) }, null, ct);
                return default;
            case "attach":
            {
                int tab = Tab();
                if (_sessions.ContainsKey(tab)) return default;
                var attached = await Send("Target.attachToTarget", new JsonObject { ["targetId"] = Target(tab), ["flatten"] = true }, null, ct);
                _sessions[tab] = attached.GetProperty("sessionId").GetString()!;
                return default;
            }
            case "detach":
            {
                if (_sessions.TryRemove(Tab(), out var session))
                    try { await Send("Target.detachFromTarget", new JsonObject { ["sessionId"] = session }, null, ct); } catch (BrowserException) { }
                return default;
            }
            case "page":
            {
                int tab = Tab();
                var action = args?["action"]?.GetValue<string>() ?? "";
                var values = args?["args"]?.ToJsonString() ?? "[]";
                // The same jobs the extension runs, from the same file.
                var expression = $"(() => {{ {_pageJobs}\n return PAGE[{JsonSerializer.Serialize(action)}](...{values}); }})()";
                var answer = await Send("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true, ["awaitPromise"] = true, ["userGesture"] = true }, Session(tab), ct, seconds);
                if (answer.TryGetProperty("exceptionDetails", out var problem))
                    throw new BrowserException(problem.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var d) ? d.GetString() ?? "The page's script failed." : "The page's script failed.");
                var value = answer.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v) ? v.Clone() : default;
                return JsonSerializer.SerializeToElement(new JsonObject { ["value"] = value.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(value.GetRawText()) });
            }
            case "cdp":
                return await Cdp(Tab(), args?["method"]?.GetValue<string>() ?? "", args?["params"] as JsonObject, ct, seconds);
        }
        throw new BrowserException("Unknown request: " + op);
    }

    string Session(int tab) => _sessions.TryGetValue(tab, out var s) ? s : throw new BrowserException("Shiki isn't working in that tab.");

    public Task<JsonElement> Cdp(int tabId, string method, JsonObject? parameters = null, CancellationToken ct = default, int seconds = 30) =>
        Send(method, parameters?.DeepClone() as JsonObject, Session(tabId), ct, seconds);

    public void Dispose()
    {
        _stop.Cancel();
        try { _socket.Abort(); } catch (Exception) { }
        _socket.Dispose();
    }
}
