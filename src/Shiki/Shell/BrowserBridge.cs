using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// The line to Shiki's extension in the person's own browser (Chrome, Edge, Brave): a WebSocket on
/// 127.0.0.1 that only that extension may open (the browser stamps its id on the connection, and
/// web pages can't fake it). Through it Shiki lists and opens tabs and sends Chrome DevTools
/// commands to a tab: reading the page, clicking, typing.
/// </summary>
public sealed class BrowserBridge : IDisposable, IBrowserLink
{
    public const int Port = 47820;
    /// <summary>The extension's id when added by hand from Shiki's folder (fixed by the key in its manifest).</summary>
    public const string ExtensionId = "eimendniolefjdmbjdohaaekkhmalnmm";
    /// <summary>Its id in the Chrome Web Store (the store gives it one), once it's published there; null until then.</summary>
    public const string? ChromeStoreId = null;
    /// <summary>Its id in Microsoft's Edge Add-ons store, once it's published there; null until then.</summary>
    public const string? EdgeStoreId = null;

    /// <summary>Every id Shiki's own extension can have; only these may connect.</summary>
    static readonly string[] Ours = new[] { ExtensionId, ChromeStoreId, EdgeStoreId }.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToArray();

    public static BrowserBridge? Current { get; private set; }

    readonly TcpListener _listener = new(IPAddress.Loopback, Port);
    readonly CancellationTokenSource _stop = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _waiting = new();
    readonly SemaphoreSlim _sending = new(1, 1);
    WebSocket? _socket;
    int _next;

    /// <summary>The browser the extension lives in ("Chrome 158"), while it's connected.</summary>
    public string? Browser { get; private set; }
    public bool Connected => _socket is { State: WebSocketState.Open };

    /// <summary>Something happened in the browser: a tab opened ("tabCreated"), closed, or the debugger was let go.</summary>
    public event Action<string, JsonElement>? Event;
    public event Action? ConnectionChanged;

    public static void Start()
    {
        if (Current != null) return;
        try
        {
            var bridge = new BrowserBridge();
            bridge._listener.Start();
            Current = bridge;
            _ = bridge.Accept();
            Log.Info($"Browser bridge listening on 127.0.0.1:{Port}");
        }
        catch (Exception ex) { Log.Error("The browser bridge didn't start", ex); }
    }

    async Task Accept()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = Serve(client);
        }
    }

    async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var request = await ReadHead(stream);
            if (request == null) return;
            string? Header(string name)
            {
                foreach (var line in request.Split("\r\n"))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0 && line[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return line[(colon + 1)..].Trim();
                }
                return null;
            }
            var origin = Header("Origin") ?? "";
            var key = Header("Sec-WebSocket-Key");
            // Only Shiki's extension: a web page (or anything else) is turned away.
            if (key == null || !Ours.Any(id => origin.Equals($"chrome-extension://{id}", StringComparison.OrdinalIgnoreCase)))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                return;
            }
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
            using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(20) });
            // One browser at a time: the newest connection wins.
            var old = _socket;
            _socket = socket;
            if (old != null) try { old.Abort(); } catch (Exception) { }
            ConnectionChanged?.Invoke();
            using var ping = new Timer(_ => _ = SendRaw(new JsonObject { ["ping"] = 1 }), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
            await Listen(socket);
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException) { }
        catch (Exception ex) { Log.Error("Browser bridge connection", ex); }
        finally
        {
            if (_socket != null && _socket.State != WebSocketState.Open)
            {
                _socket = null;
                Browser = null;
                foreach (var (_, wait) in _waiting) wait.TrySetException(new IOException("The browser went away."));
                _waiting.Clear();
                ConnectionChanged?.Invoke();
            }
        }
    }

    static async Task<string?> ReadHead(NetworkStream stream)
    {
        var head = new StringBuilder();
        var buffer = new byte[1];
        while (head.Length < 8192)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return null;
            head.Append((char)buffer[0]);
            if (head.Length >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n') return head.ToString();
        }
        return null;
    }

    async Task Listen(WebSocket socket)
    {
        var buffer = new byte[1 << 16];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
        {
            var got = await socket.ReceiveAsync(buffer, _stop.Token);
            if (got.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, got.Count);
            if (!got.EndOfMessage) continue;
            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            try { Handle(JsonDocument.Parse(json).RootElement.Clone()); }
            catch (JsonException) { }
        }
    }

    void Handle(JsonElement message)
    {
        if (message.TryGetProperty("hello", out _))
        {
            var agent = message.TryGetProperty("agent", out var a) ? a.GetString() ?? "" : "";
            Browser = BrowserName(agent);
            Log.Info($"Browser extension connected ({Browser})");
            ConnectionChanged?.Invoke();
            return;
        }
        if (message.TryGetProperty("event", out var kind))
        {
            Event?.Invoke(kind.GetString() ?? "", message);
            return;
        }
        if (message.TryGetProperty("id", out var id) && _waiting.TryRemove(id.GetInt32(), out var wait))
        {
            if (message.TryGetProperty("error", out var error)) wait.TrySetException(new BrowserException(error.GetString() ?? "The browser said no."));
            else wait.TrySetResult(message.TryGetProperty("result", out var result) ? result : default);
        }
    }

    /// <summary>One request to the extension ("tabs", "open", "attach", "cdp"…); its answer.</summary>
    public async Task<JsonElement> Call(string op, JsonObject? args = null, CancellationToken ct = default, int seconds = 30)
    {
        if (!Connected) throw new BrowserException("The browser isn't connected.");
        int id = Interlocked.Increment(ref _next);
        var message = args ?? new JsonObject();
        message["id"] = id;
        message["op"] = op;
        var wait = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = wait;
        try
        {
            await SendRaw(message);
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

    /// <summary>A Chrome DevTools command to one tab.</summary>
    public Task<JsonElement> Cdp(int tabId, string method, JsonObject? parameters = null, CancellationToken ct = default, int seconds = 30) =>
        Call("cdp", new JsonObject { ["tabId"] = tabId, ["method"] = method, ["params"] = parameters ?? new JsonObject() }, ct, seconds);

    async Task SendRaw(JsonObject message)
    {
        var socket = _socket;
        if (socket is not { State: WebSocketState.Open }) return;
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sending.WaitAsync();
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token); }
        catch (Exception) { }
        finally { _sending.Release(); }
    }

    static string BrowserName(string agent)
    {
        string Version(string marker)
        {
            int at = agent.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return "";
            var rest = agent[(at + marker.Length)..];
            int dot = rest.IndexOf('.');
            return dot > 0 ? " " + rest[..dot] : "";
        }
        if (agent.Contains("Edg/")) return "Edge" + Version("Edg/");
        if (agent.Contains("OPR/")) return "Opera" + Version("OPR/");
        if (agent.Contains("Brave")) return "Brave";
        if (agent.Contains("Chrome/")) return "Chrome" + Version("Chrome/");
        return "your browser";
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); } catch (Exception) { }
        try { _socket?.Abort(); } catch (Exception) { }
    }
}

public sealed class BrowserException(string message) : Exception(message);
