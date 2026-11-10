using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core.Phone;

/// <summary>A phone paired with this PC (its key is in Credential Manager).</summary>
public sealed class PairedPhone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Paired { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? LastIp { get; set; }
    /// <summary>"android" (Shiki for Android, over TLS) or "iphone" (the Expo app, over the web link).</summary>
    public string Kind { get; set; } = "android";
}

/// <summary>A phone asking to pair: the person checks the code is the same on both screens.</summary>
public sealed class PairRequest(string name, string code, string ip)
{
    public string Name { get; } = name;
    public string Code { get; } = code;
    public string Ip { get; } = ip;
    internal readonly TaskCompletionSource<bool> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Accept() => Answer.TrySetResult(true);
    public void Decline() => Answer.TrySetResult(false);
}

/// <summary>Files coming from (or going to) the phone as one batch: a share, a "get my photos", new photos.</summary>
public sealed class PhoneJob(string id, string phone, bool quiet)
{
    public string Id { get; } = id;
    public string Phone { get; } = phone;
    /// <summary>Asked for from the PC (the cat says how it went itself): no pop-up.</summary>
    public bool Quiet { get; } = quiet;
    public int Total { get; internal set; } = -1;
    public List<string> Saved { get; } = [];
    public int Failed { get; internal set; }
    public string? Note { get; internal set; }
    internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsDone => Finished.Task.IsCompleted;
}

/// <summary>
/// Shiki on the phone talks to the PC over the home network: it finds the PC (a broadcast), pairs
/// once (the same six-digit code on both screens), then keeps asking "anything for me?" so the PC
/// can ask it for photos. Every file goes byte for byte over TLS (the phone pins this PC's
/// certificate) and is checked with SHA-256 before it's kept. Only devices on the local network.
/// </summary>
public sealed partial class PhoneLink : IDisposable
{
    public const int FindPort = 47811, LinkPort = 47812, AppPort = 47813;
    const int Version = 1;

    public static PhoneLink? Current { get; private set; }

    /// <summary>A phone wants to pair. Raised off the UI thread.</summary>
    public event Action<PairRequest>? PairRequested;
    /// <summary>A batch of files has finished arriving. Raised off the UI thread.</summary>
    public event Action<PhoneJob>? JobFinished;
    /// <summary>A phone was paired (an iPhone pairs by scanning the QR code, without a code to compare). Raised off the UI thread.</summary>
    public event Action<PairedPhone>? PhonePaired;
    /// <summary>Phones paired, removed, connected or gone. Raised off the UI thread.</summary>
    public event Action? Changed;

    sealed class State
    {
        public string PcId { get; set; } = "";
        public List<PairedPhone> Phones { get; set; } = [];
    }

    sealed class Session
    {
        public readonly ConcurrentQueue<JsonObject> Commands = new();
        public TaskCompletionSource<bool>? Waiter;
        public long LastPoll;
        public string? Ip;
    }

    readonly Settings _settings;
    readonly State _state;
    readonly X509Certificate2 _cert;
    readonly CancellationTokenSource _stop = new();
    readonly ConcurrentDictionary<string, Session> _sessions = new();
    readonly ConcurrentDictionary<string, PhoneJob> _jobs = new();
    readonly ConcurrentDictionary<string, (string Path, DateTime Until)> _outgoing = new();
    readonly ConcurrentDictionary<string, long> _pairTries = new();
    readonly Dictionary<string, string> _received = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    TcpListener? _link, _app;
    UdpClient? _find;
    bool _listening;
    long _lastSave;

    public string Fingerprint { get; }
    public string PcName => Environment.MachineName;
    public IReadOnlyList<PairedPhone> Phones { get { lock (_gate) return _state.Phones.ToList(); } }
    public bool Listening => _listening;

    static string Dir => Directory.CreateDirectory(Path.Combine(Log.DataDir, "phone")).FullName;
    static string StatePath => Path.Combine(Dir, "phone-link.json");
    static string ReceivedPath => Path.Combine(Dir, "received.txt");
    static string PartsDir => Directory.CreateDirectory(Path.Combine(Dir, "parts")).FullName;
    static readonly SecretStore CertPassword = new("Shiki/PhoneLinkCertificate");
    static SecretStore KeyFor(string phone) => new($"Shiki/Phone/{phone}");

    /// <summary>The app's copy of Shiki for Android, offered to the phone while connecting.</summary>
    public static string ApkPath => Path.Combine(AppContext.BaseDirectory, "phone", "Shiki-phone.apk");

    PhoneLink(Settings settings)
    {
        _settings = settings;
        _state = LoadState();
        if (_state.PcId.Length == 0) { _state.PcId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(); SaveState(); }
        _cert = LoadCertificate();
        Fingerprint = Convert.ToHexString(SHA256.HashData(_cert.RawData)).ToLowerInvariant();
        try
        {
            if (File.Exists(ReceivedPath))
                foreach (var line in File.ReadAllLines(ReceivedPath))
                    if (line.Split('\t') is [var sha, var path]) _received[sha] = path;
        }
        catch (Exception ex) { Log.Error("Reading the phone's received list", ex); }
    }

    /// <summary>Starts the link; it listens straight away if a phone is already paired.</summary>
    public static PhoneLink Start(Settings settings)
    {
        Current ??= new PhoneLink(settings);
        if (Current.Phones.Count > 0) Current.Listen();
        return Current;
    }

    /// <summary>Listens for phones (Windows may ask once to let Shiki on the private network).</summary>
    public void Listen()
    {
        lock (_gate)
        {
            if (_listening) return;
            _listening = true;
        }
        try
        {
            _link = new TcpListener(IPAddress.Any, LinkPort);
            _link.Start();
            _ = AcceptLoop(_link, client => Serve(client));
            _find = new UdpClient(new IPEndPoint(IPAddress.Any, FindPort)) { EnableBroadcast = true };
            _ = FindLoop(_find);
            ListenWeb();
            Log.Info($"Phone link listening on {LinkPort} (find on {FindPort}), certificate {Fingerprint[..12]}…");
        }
        catch (Exception ex)
        {
            Log.Error("Phone link couldn't listen", ex);
            lock (_gate) _listening = false;
        }
    }

    // ---- Getting the app onto the phone (plain web, only while the connect window is open) ----

    /// <summary>The web address the phone opens to download Shiki for Android, or null if it can't be offered.</summary>
    public string? StartAppDownload()
    {
        var ip = LocalAddress();
        if (ip == null || !File.Exists(ApkPath)) return null;
        try
        {
            if (_app == null)
            {
                _app = new TcpListener(IPAddress.Any, AppPort);
                _app.Start();
                _ = AcceptLoop(_app, ServeApp);
            }
            return $"http://{ip}:{AppPort}/";
        }
        catch (Exception ex)
        {
            Log.Error("Phone app download couldn't start", ex);
            return null;
        }
    }

    public void StopAppDownload()
    {
        try { _app?.Stop(); } catch (Exception) { }
        _app = null;
    }

    async Task ServeApp(TcpClient client)
    {
        using (client)
        {
            if (!IsLocal(RemoteIp(client))) return;
            var stream = client.GetStream();
            using var timeout = new CancellationTokenSource(15000);
            string request;
            try
            {
                request = await ReadLine(stream, timeout.Token, 8192);
                // The rest of the headers aren't needed, but they're read: closing with them unread resets the connection.
                for (int i = 0; i < 100 && (await ReadLine(stream, timeout.Token, 8192)).Length > 0; i++) { }
            }
            catch (Exception) { return; }
            var path = request.Split(' ') is [_, var p, ..] ? p : "/";
            if (path.StartsWith("/shiki.apk"))
            {
                var apk = new FileInfo(ApkPath);
                var head = $"HTTP/1.1 200 OK\r\nContent-Type: application/vnd.android.package-archive\r\nContent-Length: {apk.Length}\r\nContent-Disposition: attachment; filename=\"Shiki.apk\"\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await using var file = apk.OpenRead();
                await file.CopyToAsync(stream);
                Log.Info($"Phone app downloaded by {RemoteIp(client)}");
                return;
            }
            var html = $$"""
                <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>Shiki for Android</title>
                <style>body{font-family:system-ui,sans-serif;margin:0;padding:32px 22px;background:#faf7f2;color:#222}
                h1{font-size:26px;margin:0 0 10px}p{font-size:16px;line-height:1.5;color:#555}
                a{display:block;text-align:center;background:#222;color:#fff;text-decoration:none;font-size:18px;padding:16px;border-radius:14px;margin:26px 0 18px}
                ol{padding-left:20px;color:#555;line-height:1.6}</style></head><body>
                <h1>Shiki for your phone</h1>
                <p>Sends your photos, videos and files to {{WebUtility.HtmlEncode(PcName)}} at full quality, over your Wi-Fi.</p>
                <a href="/shiki.apk">Download Shiki</a>
                <ol><li>Open the downloaded file and tap Install (allow your browser to install apps if asked).</li>
                <li>Open Shiki and tap your PC's name.</li><li>Check the code matches the one on your PC.</li></ol>
                </body></html>
                """;
            var body = Encoding.UTF8.GetBytes(html);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(body);
        }
    }

    // ---- Finding the PC ----

    async Task FindLoop(UdpClient udp)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var got = await udp.ReceiveAsync(_stop.Token);
                var text = Encoding.UTF8.GetString(got.Buffer);
                if (!text.StartsWith("SHIKI-FIND") || !IsLocal(got.RemoteEndPoint.Address.MapToIPv4().ToString())) continue;
                // "SHIKI-FIND <pc id>" looks for one PC; "SHIKI-FIND *" for any.
                var wanted = text.Length > 11 ? text[11..].Trim() : "*";
                if (wanted != "*" && wanted != _state.PcId) continue;
                var reply = new JsonObject { ["shiki"] = Version, ["id"] = _state.PcId, ["name"] = PcName, ["port"] = LinkPort, ["fp"] = Fingerprint };
                var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString());
                await udp.SendAsync(bytes, got.RemoteEndPoint, _stop.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex) { Log.Error("Phone find", ex); await Task.Delay(500); }
        }
    }

    // ---- The link itself ----

    async Task AcceptLoop(TcpListener listener, Func<TcpClient, Task> serve)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(_stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }
            _ = Task.Run(async () =>
            {
                try { await serve(client); }
                catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException or SocketException) { }
                catch (Exception ex) { Log.Error("Phone link connection", ex); }
            });
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            var ip = RemoteIp(client);
            if (!IsLocal(ip)) return;
            client.NoDelay = true;
            await using var ssl = new SslStream(client.GetStream(), false);
            using (var handshake = new CancellationTokenSource(10000))
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _cert,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false,
                }, handshake.Token);
            JsonObject request;
            using (var read = new CancellationTokenSource(15000))
                request = JsonNode.Parse(await ReadLine(ssl, read.Token, 64 * 1024)) as JsonObject ?? throw new IOException("Not JSON");
            var op = Str(request, "op");
            if (op == "hello") { await Reply(ssl, new JsonObject { ["ok"] = true, ["id"] = _state.PcId, ["name"] = PcName, ["v"] = Version }); return; }
            if (op == "pair") { await Pair(ssl, request, ip); return; }

            if (Authorised(request) is not { } phone)
            {
                await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "unpaired" });
                return;
            }
            Seen(phone, ip);
            switch (op)
            {
                case "wait": await Wait(ssl, phone); break;
                case "check": await Check(ssl, request, phone); break;
                case "put": await Put(ssl, request, phone); break;
                case "job": await JobReport(ssl, request, phone); break;
                case "get": await Get(ssl, request); break;
                case "bye": await Reply(ssl, new JsonObject { ["ok"] = true }); break;
                default: await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "unknown op" }); break;
            }
        }
    }

    async Task Pair(SslStream ssl, JsonObject request, string ip)
    {
        // A request every 15 seconds at most from one address: nobody on the Wi-Fi can flood the PC with cards.
        var now = Environment.TickCount64;
        if (_pairTries.TryGetValue(ip, out var last) && now - last < 15000)
        {
            await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "Wait a few seconds and try again." });
            return;
        }
        _pairTries[ip] = now;
        var name = Clean(Str(request, "name") ?? "Phone", 40);
        var nonce = Str(request, "nonce") ?? "";
        if (nonce.Length is < 16 or > 64)
        {
            await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "bad request" });
            return;
        }
        var pair = new PairRequest(name, PairCode(Fingerprint, nonce), ip);
        Log.Info($"Phone \"{name}\" at {ip} asks to pair (code {pair.Code})");
        PairRequested?.Invoke(pair);
        var answered = await Task.WhenAny(pair.Answer.Task, Task.Delay(TimeSpan.FromMinutes(2)));
        if (answered != pair.Answer.Task || !pair.Answer.Task.Result)
        {
            pair.Decline();
            await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "declined" });
            return;
        }
        var phone = new PairedPhone
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Name = name,
            Paired = DateTime.Now,
            LastSeen = DateTime.Now,
            LastIp = ip,
        };
        var key = Base64Url(RandomNumberGenerator.GetBytes(32));
        KeyFor(phone.Id).Save(key);
        lock (_gate) _state.Phones.Add(phone);
        SaveState();
        AiLog.Write("phone", $"paired {name}");
        await Reply(ssl, new JsonObject { ["ok"] = true, ["phone"] = phone.Id, ["key"] = key, ["pc"] = _state.PcId, ["name"] = PcName });
        PhonePaired?.Invoke(phone);
        Changed?.Invoke();
    }

    /// <summary>Six digits from the certificate and the phone's random number: a different PC in between would show different ones.</summary>
    public static string PairCode(string fingerprint, string nonce)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint + ":" + nonce));
        uint n = BitConverter.ToUInt32(hash, 0) % 1_000_000;
        var digits = n.ToString("D6");
        return digits[..3] + " " + digits[3..];
    }

    PairedPhone? Authorised(JsonObject request)
    {
        var id = Str(request, "phone");
        var key = Str(request, "key");
        if (id == null || key == null) return null;
        PairedPhone? phone;
        lock (_gate) phone = _state.Phones.FirstOrDefault(p => p.Id == id);
        if (phone == null || KeyFor(id).Read() is not { } stored) return null;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(key)) ? phone : null;
    }

    void Seen(PairedPhone phone, string ip)
    {
        var session = _sessions.GetOrAdd(phone.Id, _ => new Session());
        bool wasConnected = Environment.TickCount64 - session.LastPoll < 45000;
        session.LastPoll = Environment.TickCount64;
        session.Ip = ip;
        lock (_gate)
        {
            phone.LastSeen = DateTime.Now;
            phone.LastIp = ip;
        }
        if (Environment.TickCount64 - _lastSave > 5 * 60 * 1000) { _lastSave = Environment.TickCount64; SaveState(); }
        if (!wasConnected) Changed?.Invoke();
    }

    /// <summary>The phone asks "anything for me?": a command when there's one, or "none" after 25 seconds.</summary>
    async Task Wait(SslStream ssl, PairedPhone phone)
    {
        var session = _sessions.GetOrAdd(phone.Id, _ => new Session());
        var mine = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A newer wait (the phone reconnected) takes over; the old one answers "none".
        Interlocked.Exchange(ref session.Waiter, mine)?.TrySetResult(false);
        if (session.Commands.IsEmpty)
            await Task.WhenAny(mine.Task, Task.Delay(25000));
        JsonObject? command = null;
        if (session.Waiter == mine && session.Commands.TryDequeue(out var next)) command = next;
        Interlocked.CompareExchange(ref session.Waiter, null, mine);
        try
        {
            await Reply(ssl, command ?? new JsonObject { ["ok"] = true, ["cmd"] = "none" });
            session.LastPoll = Environment.TickCount64;
        }
        catch (Exception) when (command != null)
        {
            // It didn't get there: the next wait gets it.
            session.Commands.Enqueue(command);
            throw;
        }
    }

    void Send(string phone, JsonObject command)
    {
        command["ok"] = true;
        var session = _sessions.GetOrAdd(phone, _ => new Session());
        session.Commands.Enqueue(command);
        session.Waiter?.TrySetResult(true);
    }

    /// <summary>Before a file is sent: how much of it is here already (to carry on), or that it's here whole.</summary>
    async Task Check(SslStream ssl, JsonObject request, PairedPhone phone)
    {
        var sha = Sha(request);
        if (sha == null) { await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "bad sha" }); return; }
        string? had;
        lock (_gate) _received.TryGetValue(sha, out had);
        if (had != null && File.Exists(had))
        {
            // Already here (sent before): it counts as got, and isn't sent again.
            if (Str(request, "job") is { Length: > 0 } jobId)
            {
                var job = JobFor(jobId, phone.Id);
                lock (_gate) if (!job.Saved.Contains(had)) job.Saved.Add(had);
            }
            await Reply(ssl, new JsonObject { ["ok"] = true, ["done"] = true, ["saved"] = Path.GetFileName(had) });
            return;
        }
        var part = new FileInfo(Path.Combine(PartsDir, sha + ".part"));
        await Reply(ssl, new JsonObject { ["ok"] = true, ["have"] = part.Exists ? part.Length : 0 });
    }

    /// <summary>A file (or the rest of one): kept only if its SHA-256 matches what the phone said.</summary>
    async Task Put(SslStream ssl, JsonObject request, PairedPhone phone)
    {
        var sha = Sha(request);
        long size = Long(request, "size"), offset = Long(request, "offset");
        var name = SafeName(Str(request, "name") ?? "file");
        if (sha == null || size < 0 || size > 64L << 30 || offset < 0 || offset > size)
        {
            await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "bad request" });
            return;
        }
        var jobId = Str(request, "job");
        var job = JobFor(jobId, phone.Id);
        var partPath = Path.Combine(PartsDir, sha + ".part");
        await using (var part = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.Asynchronous))
        {
            // Carrying on from where it stopped: anything past that is dropped.
            if (part.Length < offset) offset = part.Length;
            part.SetLength(offset);
            part.Position = offset;
            var buffer = new byte[1 << 20];
            long left = size - offset;
            using var idle = new CancellationTokenSource();
            while (left > 0)
            {
                idle.CancelAfter(30000);
                int read = await ssl.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), idle.Token);
                if (read == 0) throw new IOException("The phone stopped sending");
                await part.WriteAsync(buffer.AsMemory(0, read));
                left -= read;
            }
        }
        // The whole file, read back, is checked: a resumed file is checked as a whole too.
        string got;
        await using (var check = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan))
            got = Convert.ToHexString(await SHA256.HashDataAsync(check)).ToLowerInvariant();
        if (got != sha)
        {
            File.Delete(partPath);
            // The phone tries again; only what it finally gives up on counts as failed (it says so).
            Log.Info($"Phone file {name} didn't match its checksum; asked again");
            await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "checksum" });
            return;
        }
        var target = Keep(partPath, name, sha, Long(request, "taken"), job, phone, size);
        await Reply(ssl, new JsonObject { ["ok"] = true, ["saved"] = Path.GetFileName(target) });
        if (string.IsNullOrWhiteSpace(jobId)) FinishSingle(job);
    }

    /// <summary>
    /// A received, checked file: kept in its folder with the date it was taken (or, if the same file
    /// came before, the copy that's already here is used and this one dropped).
    /// </summary>
    string Keep(string partPath, string name, string sha, long taken, PhoneJob job, PairedPhone phone, long size)
    {
        string? had;
        lock (_gate) _received.TryGetValue(sha, out had);
        if (had != null && File.Exists(had))
        {
            File.Delete(partPath);
            lock (_gate) if (!job.Saved.Contains(had)) job.Saved.Add(had);
            return had;
        }
        var target = Unique(Path.Combine(FolderFor(name), name));
        File.Move(partPath, target);
        if (taken > 0)
        {
            var when = DateTimeOffset.FromUnixTimeMilliseconds(taken).LocalDateTime;
            // Modified is when it was taken (what photo apps sort by); created stays when it arrived (the gallery's order).
            try { File.SetLastWriteTime(target, when); } catch (Exception) { }
        }
        lock (_gate)
        {
            _received[sha] = target;
            job.Saved.Add(target);
        }
        try { File.AppendAllText(ReceivedPath, $"{sha}\t{target}\n"); } catch (Exception ex) { Log.Error("Noting a phone file", ex); }
        AiLog.Write("phone", $"from {phone.Name}: {Path.GetFileName(target)} ({size:N0} bytes, checked)");
        return target;
    }

    /// <summary>A file on its own (no batch): it's finished as soon as it's here.</summary>
    void FinishSingle(PhoneJob job)
    {
        if (job.Finished.TrySetResult())
        {
            _jobs.TryRemove(job.Id, out _);
            JobFinished?.Invoke(job);
        }
    }

    async Task JobReport(SslStream ssl, JsonObject request, PairedPhone phone)
    {
        ApplyJob(request, phone);
        await Reply(ssl, new JsonObject { ["ok"] = true });
    }

    /// <summary>The phone says how a batch is going: its total, then that it's done (and how many failed).</summary>
    void ApplyJob(JsonObject request, PairedPhone phone)
    {
        var job = JobFor(Str(request, "job"), phone.Id);
        if (request["total"] is JsonValue total && total.TryGetValue<int>(out var n)) job.Total = n;
        if (Str(request, "note") is { Length: > 0 } note) job.Note = Clean(note, 200);
        job.Started.TrySetResult();
        if (request["done"] is JsonValue done && done.TryGetValue<bool>(out var isDone) && isDone)
        {
            if (request["failed"] is JsonValue failed && failed.TryGetValue<int>(out var f)) job.Failed = Math.Max(job.Failed, f);
            if (job.Finished.TrySetResult())
            {
                _jobs.TryRemove(job.Id, out _);
                if (job.Saved.Count > 0 || job.Failed > 0) JobFinished?.Invoke(job);
            }
        }
    }

    PhoneJob JobFor(string? id, string phone)
    {
        id = string.IsNullOrWhiteSpace(id) ? "single-" + Guid.NewGuid().ToString("N")[..8] : Clean(id, 40);
        return _jobs.GetOrAdd(id, _ => new PhoneJob(id, phone, quiet: false));
    }

    /// <summary>The phone fetches a file the PC offered it.</summary>
    async Task Get(SslStream ssl, JsonObject request)
    {
        var token = Str(request, "file") ?? "";
        if (!_outgoing.TryGetValue(token, out var offer) || offer.Until < DateTime.Now || !File.Exists(offer.Path))
        {
            await Reply(ssl, new JsonObject { ["ok"] = false, ["error"] = "gone" });
            return;
        }
        var file = new FileInfo(offer.Path);
        long offset = Math.Clamp(Long(request, "offset"), 0, file.Length);
        await Reply(ssl, new JsonObject { ["ok"] = true, ["name"] = file.Name, ["size"] = file.Length });
        await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = offset;
        await stream.CopyToAsync(ssl, 1 << 20);
        await ssl.FlushAsync();
    }

    // ---- What the cat asks for ----

    /// <summary>The phone that's connected now (the most recently heard from), if any.</summary>
    public PairedPhone? Connected()
    {
        lock (_gate)
            return _state.Phones
                .Select(p => (Phone: p, Poll: _sessions.TryGetValue(p.Id, out var s) ? s.LastPoll : 0))
                .Where(p => p.Poll > 0 && Environment.TickCount64 - p.Poll < 45000)
                .OrderByDescending(p => p.Poll)
                .Select(p => p.Phone)
                .FirstOrDefault();
    }

    public bool IsConnected(PairedPhone phone) =>
        _sessions.TryGetValue(phone.Id, out var s) && s.LastPoll > 0 && Environment.TickCount64 - s.LastPoll < 45000;

    /// <summary>Asks the phone for its newest photos (or videos, screenshots, anything) and waits for them.</summary>
    public async Task<(string Text, IReadOnlyList<string> Saved)> GetRecent(int count, string kind, bool today, Action<string>? progress = null)
    {
        if (await Reachable() is not { } phone) return (NotReachable(), []);
        count = Math.Clamp(count, 1, 100);
        kind = kind is "photo" or "video" or "screenshot" or "any" ? kind : "photo";
        var job = new PhoneJob("get-" + Guid.NewGuid().ToString("N")[..10], phone.Id, quiet: true);
        _jobs[job.Id] = job;
        Send(phone.Id, new JsonObject { ["cmd"] = "send-recent", ["job"] = job.Id, ["count"] = count, ["kind"] = kind, ["today"] = today });
        AiLog.Write("phone", $"asked {phone.Name} for {count} {kind}{(today ? " from today" : "")}");
        if (await Task.WhenAny(job.Started.Task, Task.Delay(15000)) != job.Started.Task)
        {
            _jobs.TryRemove(job.Id, out _);
            return ($"{phone.Name} didn't answer. Open Shiki on the phone once (it may have been put to sleep), then ask again.", []);
        }
        progress?.Invoke(job.Total > 0 ? $"Getting {job.Total} from {phone.Name}…" : $"Getting them from {phone.Name}…");
        // Photos take seconds; a long video can take a few minutes.
        var deadline = DateTime.Now.AddMinutes(10);
        while (!job.IsDone && DateTime.Now < deadline)
        {
            await Task.WhenAny(job.Finished.Task, Task.Delay(1000));
            if (job.Total > 0) progress?.Invoke($"Got {job.Saved.Count} of {job.Total} from {phone.Name}…");
        }
        _jobs.TryRemove(job.Id, out _);
        List<string> saved;
        lock (_gate) saved = job.Saved.ToList();
        return (Summary(job, phone, finished: job.IsDone), saved);
    }

    /// <summary>Offers files to the phone: it saves them in its Downloads (Shiki folder).</summary>
    public async Task<string> SendToPhone(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return "None of those files exist.";
        if (await Reachable() is not { } phone) return NotReachable();
        var job = new PhoneJob("put-" + Guid.NewGuid().ToString("N")[..10], phone.Id, quiet: true);
        _jobs[job.Id] = job;
        var list = new JsonArray();
        foreach (var path in files)
        {
            var info = new FileInfo(path);
            string sha;
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan))
                sha = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            var token = Base64Url(RandomNumberGenerator.GetBytes(18));
            _outgoing[token] = (path, DateTime.Now.AddMinutes(30));
            list.Add(new JsonObject { ["token"] = token, ["name"] = info.Name, ["size"] = info.Length, ["sha"] = sha });
        }
        Send(phone.Id, new JsonObject { ["cmd"] = "get", ["job"] = job.Id, ["files"] = list });
        AiLog.Write("phone", $"sending {files.Count} file(s) to {phone.Name}");
        if (await Task.WhenAny(job.Started.Task, Task.Delay(15000)) != job.Started.Task)
        {
            _jobs.TryRemove(job.Id, out _);
            return $"{phone.Name} didn't answer. Open Shiki on the phone once, then ask again.";
        }
        await Task.WhenAny(job.Finished.Task, Task.Delay(TimeSpan.FromMinutes(10)));
        _jobs.TryRemove(job.Id, out _);
        int failed = job.Failed;
        if (!job.IsDone) return $"Still sending to {phone.Name}; it carries on by itself.";
        return failed == 0
            ? phone.Kind == "iphone"
                ? $"Sent {files.Count} file(s) to {phone.Name}: photos and videos are in its Photos, other files in Shiki on the iPhone (Share there to put them in Files)."
                : $"Sent {files.Count} file(s) to {phone.Name}, checked: they're in the phone's Download/Shiki folder."
            : $"Sent {files.Count - failed} of {files.Count} to {phone.Name}; {failed} didn't make it ({job.Note ?? "try again"}).";
    }

    /// <summary>A connected phone, waiting a few seconds for one that's just reconnecting.</summary>
    async Task<PairedPhone?> Reachable()
    {
        if (Phones.Count == 0) return null;
        Listen();
        for (int i = 0; i < 16 && Connected() == null; i++) await Task.Delay(500);
        return Connected();
    }

    string NotReachable() => Phones.Count == 0
        ? "No phone is connected to Shiki yet. Say \"connect my phone\" (or Settings, Phone) to set it up once."
        : Phones.OrderByDescending(p => p.LastSeen).First() is { Kind: "iphone" } iphone
            ? $"{iphone.Name} isn't connected right now: open Shiki on the iPhone (in Expo Go). It listens only while it's open, on the same Wi-Fi as this PC (or with this PC on its hotspot)."
            : $"{Phones[0].Name} isn't connected right now. It needs to be on the same Wi-Fi as this PC with Shiki open (or running in the background).";

    static string Summary(PhoneJob job, PairedPhone phone, bool finished)
    {
        if (job.Saved.Count == 0)
            return job.Note ?? (job.Failed > 0 ? $"Nothing came through from {phone.Name}; try again." : $"{phone.Name} had nothing like that.");
        var where = Path.GetDirectoryName(job.Saved[0]);
        var names = string.Join(", ", job.Saved.Take(8).Select(Path.GetFileName)) + (job.Saved.Count > 8 ? "…" : "");
        var line = $"Got {job.Saved.Count} file(s) from {phone.Name}, each checked byte for byte (full quality), into {where}: {names}.";
        if (job.Failed > 0) line += $" {job.Failed} didn't come through.";
        if (!finished) line += " More are still coming.";
        return line;
    }

    public void Remove(string phoneId)
    {
        lock (_gate) _state.Phones.RemoveAll(p => p.Id == phoneId);
        KeyFor(phoneId).Remove();
        _sessions.TryRemove(phoneId, out var session);
        session?.Waiter?.TrySetResult(false);
        SaveState();
        AiLog.Write("phone", $"removed {phoneId}");
        Changed?.Invoke();
    }

    // ---- Where files go ----

    static readonly HashSet<string> Media = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".dng", ".raw", ".bmp",
        ".mp4", ".mov", ".3gp", ".mkv", ".webm", ".m4v",
    };

    /// <summary>Photos and videos in Pictures\From phone; anything else in Downloads\From phone.</summary>
    string FolderFor(string name)
    {
        bool media = Media.Contains(Path.GetExtension(name));
        var folder = media
            ? _settings.PhoneFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "From phone")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "From phone");
        return Directory.CreateDirectory(folder).FullName;
    }

    public string PhotoFolder => _settings.PhoneFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "From phone");

    static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var next = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(next)) return next;
        }
    }

    static string SafeName(string name)
    {
        name = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        name = name.Trim().TrimStart('.');
        if (name.Length == 0) name = "file";
        if (name.Length > 120)
        {
            var ext = Path.GetExtension(name);
            name = name[..(120 - ext.Length)] + ext;
        }
        return name;
    }

    // ---- Small pieces ----

    static async Task<string> ReadLine(Stream stream, CancellationToken ct, int max)
    {
        var bytes = new List<byte>(256);
        var one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one, ct);
            if (read == 0) throw new IOException("Closed");
            if (one[0] == (byte)'\n') break;
            if (one[0] != (byte)'\r') bytes.Add(one[0]);
            if (bytes.Count > max) throw new IOException("Line too long");
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    static async Task Reply(Stream stream, JsonObject reply)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(reply.ToJsonString() + "\n"));
        await stream.FlushAsync();
    }

    static string? Str(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static long Long(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;
    static string? Sha(JsonObject o) => Str(o, "sha") is { Length: 64 } sha && sha.All(Uri.IsHexDigit) ? sha.ToLowerInvariant() : null;
    static string Clean(string text, int max)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > max ? clean[..max] : clean;
    }
    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string RemoteIp(TcpClient client) => (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? "";

    /// <summary>Only the home network (and this PC): never anything from the internet.</summary>
    internal static bool IsLocal(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        var b = address.MapToIPv4().GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>This PC's address on the home network (the network with the way out to the internet).</summary>
    public static string? LocalAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => n.GetIPProperties())
                .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .SelectMany(p => p.UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && IsLocal(a.Address.ToString()) && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error("Finding this PC's network address", ex);
            return null;
        }
    }

    static State LoadState()
    {
        try
        {
            if (File.Exists(StatePath)) return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State();
        }
        catch (Exception ex) { Log.Error("Reading the paired phones", ex); }
        return new State();
    }

    void SaveState()
    {
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StatePath, json);
        }
        catch (Exception ex) { Log.Error("Saving the paired phones", ex); }
    }

    /// <summary>This PC's own certificate for the link, made once; the phone remembers its fingerprint.</summary>
    static X509Certificate2 LoadCertificate()
    {
        var path = Path.Combine(Dir, "phone-link.pfx");
        var password = CertPassword.Read();
        if (password != null && File.Exists(path))
        {
            try { return new X509Certificate2(path, password, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet); }
            catch (Exception ex) { Log.Error("Reading the phone link certificate; making a new one", ex); }
        }
        password = Base64Url(RandomNumberGenerator.GetBytes(24));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Shiki on {Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(30));
        File.WriteAllBytes(path, made.Export(X509ContentType.Pfx, password));
        CertPassword.Save(password);
        return new X509Certificate2(path, password, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _link?.Stop(); } catch (Exception) { }
        try { _find?.Dispose(); } catch (Exception) { }
        StopAppDownload();
    }
}
