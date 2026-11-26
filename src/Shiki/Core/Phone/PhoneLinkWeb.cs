using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core.Phone;

/// <summary>
/// The same link for the iPhone app (Expo), which can only make plain web requests: it pairs by
/// scanning a QR code with a one-time token, asks "anything for me?" by long polling, and sends
/// each file whole with its MD5 (worked out on the phone), which is checked here along with the
/// size before the file is kept. Home network only, a secret key on every request.
/// </summary>
public sealed partial class PhoneLink
{
    public const int WebPort = 47814;

    TcpListener? _web;
    string? _pairToken;
    DateTime _pairTokenUntil;
    // The same pairing, typed instead of scanned: six digits, five tries.
    string _pairPin = "";
    int _pinTries;

    /// <summary>While pairing is open: this PC's address and the six digits to type in the app (when the camera can't scan).</summary>
    public (string Host, string Pin)? PairingByHand { get; private set; }
    // The phone's own name for a file it sent (its photo id and date) → where it is here.
    readonly Dictionary<string, string> _receivedIds = new(StringComparer.Ordinal);
    bool _idsLoaded;
    static string ReceivedIdsPath => Path.Combine(Dir, "received-ids.txt");

    void ListenWeb()
    {
        if (_web != null) return;
        try
        {
            _web = new TcpListener(IPAddress.Any, WebPort);
            _web.Start();
            _ = AcceptLoop(_web, ServeWeb);
            Log.Info($"Phone web link listening on {WebPort}");
        }
        catch (Exception ex)
        {
            Log.Error("Phone web link couldn't listen", ex);
            _web = null;
        }
    }

    /// <summary>
    /// What the iPhone app scans to pair: this PC's address and a token that works once, for 15
    /// minutes. Null when this PC isn't on a home network.
    /// </summary>
    public string? StartIphonePairing()
    {
        Listen();
        var ip = LocalAddress();
        if (ip == null) return null;
        _pairToken = Base64Url(RandomNumberGenerator.GetBytes(16));
        _pairTokenUntil = DateTime.Now.AddMinutes(15);
        _pairPin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _pinTries = 0;
        PairingByHand = (ip, _pairPin);
        return $"shiki://pair?h={ip}&p={WebPort}&t={_pairToken}&id={_state.PcId}&n={Uri.EscapeDataString(PcName)}";
    }

    public void StopIphonePairing()
    {
        _pairToken = null;
        PairingByHand = null;
    }

    sealed class WebRequest
    {
        public string Method = "", Path = "";
        public Dictionary<string, string> Query = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
        public long Length;
        public bool Chunked;
        public string Q(string name) => Query.TryGetValue(name, out var v) ? v : "";
    }

    async Task ServeWeb(TcpClient client)
    {
        using (client)
        {
            var ip = RemoteIp(client);
            if (!IsLocal(ip)) return;
            client.NoDelay = true;
            var stream = client.GetStream();
            WebRequest request;
            using (var read = new CancellationTokenSource(15000))
                request = await ReadRequest(stream, read.Token);
            if (request.Headers.TryGetValue("Expect", out var expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"));

            switch (request.Method, request.Path)
            {
                case ("GET", "/v1/hello"):
                    await Json(stream, 200, new JsonObject { ["ok"] = true, ["id"] = _state.PcId, ["name"] = PcName, ["v"] = Version });
                    return;
                case ("POST", "/v1/pair"):
                    await WebPair(stream, request, ip);
                    return;
            }

            var phone = Authorised(new JsonObject
            {
                ["phone"] = request.Headers.GetValueOrDefault("X-Shiki-Phone"),
                ["key"] = request.Headers.GetValueOrDefault("X-Shiki-Key"),
            });
            if (phone == null)
            {
                await Json(stream, 401, new JsonObject { ["ok"] = false, ["error"] = "unpaired" });
                return;
            }
            Seen(phone, ip);
            switch (request.Method, request.Path)
            {
                case ("POST", "/v1/wait"): await WebWait(stream, phone); break;
                case ("GET", "/v1/check"): await WebCheck(stream, request); break;
                case ("PUT", "/v1/put"): await WebPut(stream, request, phone); break;
                case ("POST", "/v1/job"):
                    ApplyJob(await ReadJson(stream, request), phone);
                    await Json(stream, 200, new JsonObject { ["ok"] = true });
                    break;
                case ("GET", "/v1/get"): await WebGet(stream, request); break;
                default: await Json(stream, 404, new JsonObject { ["ok"] = false, ["error"] = "unknown" }); break;
            }
        }
    }

    async Task WebPair(Stream stream, WebRequest request, string ip)
    {
        var body = await ReadJson(stream, request);
        var token = Str(body, "token") ?? "";
        var pin = (Str(body, "pin") ?? "").Trim();
        var wanted = _pairToken;
        bool open = wanted != null && DateTime.Now < _pairTokenUntil;
        bool scanned = open && token.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(wanted!));
        // Typed: right six digits, and fewer than five wrong tries (after that the code is spent).
        bool typed = open && !scanned && pin.Length == 6 && _pinTries < 5 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pin), Encoding.UTF8.GetBytes(_pairPin));
        if (open && !scanned && pin.Length > 0 && !typed) _pinTries++;
        if (!scanned && !typed)
        {
            var why = !open ? "That code has expired. Open Connect a phone on the PC and use the new one."
                : pin.Length > 0 && _pinTries >= 5 ? "Too many wrong codes. Close Connect a phone on the PC and open it again for a new one."
                : pin.Length > 0 ? "That code isn't right. Check the six digits on the PC."
                : "That code has expired. Open Connect a phone on the PC and use the new one.";
            Log.Info($"iPhone pairing from {ip} refused ({(pin.Length > 0 ? "typed code" : "scanned code")})");
            await Json(stream, 403, new JsonObject { ["ok"] = false, ["error"] = why });
            return;
        }
        // It works once: scanning it (or typing it) means being in front of this PC's screen.
        _pairToken = null;
        PairingByHand = null;
        var phone = new PairedPhone
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Name = Clean(Str(body, "name") ?? "iPhone", 40),
            Kind = "iphone",
            Paired = DateTime.Now,
            LastSeen = DateTime.Now,
            LastIp = ip,
        };
        var key = Base64Url(RandomNumberGenerator.GetBytes(32));
        KeyFor(phone.Id).Save(key);
        lock (_gate) _state.Phones.Add(phone);
        SaveState();
        AiLog.Write("phone", $"paired {phone.Name} (iPhone)");
        Log.Info($"iPhone \"{phone.Name}\" at {ip} paired");
        await Json(stream, 200, new JsonObject { ["ok"] = true, ["phone"] = phone.Id, ["key"] = key, ["pc"] = _state.PcId, ["name"] = PcName });
        PhonePaired?.Invoke(phone);
        Changed?.Invoke();
    }

    async Task WebWait(Stream stream, PairedPhone phone)
    {
        var session = _sessions.GetOrAdd(phone.Id, _ => new Session());
        var mine = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref session.Waiter, mine)?.TrySetResult(false);
        if (session.Commands.IsEmpty) await Task.WhenAny(mine.Task, Task.Delay(25000));
        JsonObject? command = null;
        if (session.Waiter == mine && session.Commands.TryDequeue(out var next)) command = next;
        Interlocked.CompareExchange(ref session.Waiter, null, mine);
        try
        {
            await Json(stream, 200, command ?? new JsonObject { ["ok"] = true, ["cmd"] = "none" });
            session.LastPoll = Environment.TickCount64;
        }
        catch (Exception) when (command != null)
        {
            session.Commands.Enqueue(command);
            throw;
        }
    }

    async Task WebCheck(Stream stream, WebRequest request)
    {
        LoadIds();
        var id = FileId(request.Q("id"));
        string? had = null;
        if (id.Length > 0) lock (_gate) _receivedIds.TryGetValue(id, out had);
        if (had != null && File.Exists(had))
        {
            if (request.Q("job") is { Length: > 0 } jobId && _jobs.TryGetValue(Clean(jobId, 40), out var job))
                lock (_gate) if (!job.Saved.Contains(had)) job.Saved.Add(had);
            await Json(stream, 200, new JsonObject { ["ok"] = true, ["done"] = true, ["saved"] = Path.GetFileName(had) });
            return;
        }
        await Json(stream, 200, new JsonObject { ["ok"] = true, ["done"] = false });
    }

    /// <summary>A whole file in the body: kept if its size (and MD5, when the phone gives one) match.</summary>
    async Task WebPut(Stream stream, WebRequest request, PairedPhone phone)
    {
        long.TryParse(request.Q("size"), out var size);
        long.TryParse(request.Q("taken"), out var taken);
        var name = SafeName(request.Q("name") is { Length: > 0 } n ? n : "file");
        var md5 = request.Q("md5").ToLowerInvariant();
        var jobId = request.Q("job");
        if (size <= 0 || size > 64L << 30 || (!request.Chunked && request.Length != size))
        {
            await Json(stream, 400, new JsonObject { ["ok"] = false, ["error"] = "bad size" });
            return;
        }
        var job = JobFor(jobId, phone.Id);
        var partPath = Path.Combine(PartsDir, "web-" + Guid.NewGuid().ToString("N") + ".part");
        long got = 0;
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5Hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        try
        {
            await using (var part = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.Asynchronous))
            {
                await foreach (var chunk in Body(stream, request))
                {
                    await part.WriteAsync(chunk);
                    sha256.AppendData(chunk.Span);
                    md5Hash.AppendData(chunk.Span);
                    got += chunk.Length;
                    if (got > size) throw new IOException("More than was said");
                }
            }
        }
        catch (Exception)
        {
            try { File.Delete(partPath); } catch (Exception) { }
            throw;
        }
        var gotMd5 = Convert.ToHexString(md5Hash.GetHashAndReset()).ToLowerInvariant();
        var sha = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
        if (got != size || (md5.Length == 32 && gotMd5 != md5))
        {
            File.Delete(partPath);
            Log.Info($"iPhone file {name} didn't arrive whole ({got:N0} of {size:N0} bytes, md5 {(md5.Length == 32 ? gotMd5 == md5 ? "ok" : "differs" : "not given")})");
            await Json(stream, 400, new JsonObject { ["ok"] = false, ["error"] = "checksum" });
            return;
        }
        var target = Keep(partPath, name, sha, taken, job, phone, size);
        // A photo: the same number of pixels as on the phone? (Turned photos count: 3024×4032 is 4032×3024.)
        int.TryParse(request.Q("w"), out var phoneW);
        int.TryParse(request.Q("h"), out var phoneH);
        string? pixels = null;
        bool? same = null;
        if (phoneW > 0 && phoneH > 0 && Pixels(target) is { } here)
        {
            pixels = $"{here.W}×{here.H}";
            same = Math.Max(here.W, here.H) >= Math.Max(phoneW, phoneH) && Math.Min(here.W, here.H) >= Math.Min(phoneW, phoneH);
            lock (_gate)
            {
                if (same == true) { job.SameSize++; job.LastPixels = pixels; }
                else job.Smaller.Add($"{Path.GetFileName(target)}: {pixels} here, {phoneW}×{phoneH} on the phone");
            }
            Log.Info($"iPhone photo {Path.GetFileName(target)}: {pixels} here, {phoneW}×{phoneH} on the phone{(same == true ? "" : " (SMALLER)")}");
        }
        var id = FileId(request.Q("id"));
        if (id.Length > 0)
        {
            LoadIds();
            lock (_gate) _receivedIds[id] = target;
            try { File.AppendAllText(ReceivedIdsPath, $"{id}\t{target}\n"); } catch (Exception ex) { Log.Error("Noting an iPhone file", ex); }
        }
        await Json(stream, 200, new JsonObject { ["ok"] = true, ["saved"] = Path.GetFileName(target), ["sha"] = sha, ["checked"] = md5.Length == 32, ["pixels"] = pixels, ["sameSize"] = same });
        if (string.IsNullOrWhiteSpace(jobId)) FinishSingle(job);
    }

    /// <summary>A picture's width and height, read from its header (HEIC too), or null if it isn't one.</summary>
    internal static (int W, int H)? Pixels(string path)
    {
        try
        {
            var info = new ImageMagick.MagickImageInfo(path);
            return info.Width > 0 && info.Height > 0 ? ((int)info.Width, (int)info.Height) : null;
        }
        catch (Exception) { return null; }
    }

    async Task WebGet(Stream stream, WebRequest request)
    {
        var token = request.Q("file");
        if (!_outgoing.TryGetValue(token, out var offer) || offer.Until < DateTime.Now || !File.Exists(offer.Path))
        {
            await Json(stream, 404, new JsonObject { ["ok"] = false, ["error"] = "gone" });
            return;
        }
        var file = new FileInfo(offer.Path);
        var head = $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {file.Length}\r\n" +
                   $"X-Shiki-Name: {Uri.EscapeDataString(file.Name)}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(stream, 1 << 20);
    }

    // ---- Plain HTTP, just enough of it ----

    static async Task<WebRequest> ReadRequest(Stream stream, CancellationToken ct)
    {
        var first = await ReadLine(stream, ct, 16 * 1024);
        var parts = first.Split(' ');
        if (parts.Length < 2) throw new IOException("Not HTTP");
        var request = new WebRequest { Method = parts[0].ToUpperInvariant() };
        var target = parts[1];
        int q = target.IndexOf('?');
        request.Path = q >= 0 ? target[..q] : target;
        if (q >= 0)
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                var key = Uri.UnescapeDataString(eq >= 0 ? pair[..eq] : pair);
                var value = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')) : "";
                request.Query[key] = value;
            }
        for (int i = 0; i < 100; i++)
        {
            var line = await ReadLine(stream, ct, 16 * 1024);
            if (line.Length == 0) break;
            int colon = line.IndexOf(':');
            if (colon > 0) request.Headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        if (request.Headers.TryGetValue("Content-Length", out var length)) long.TryParse(length, out request.Length);
        request.Chunked = request.Headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        return request;
    }

    /// <summary>The request's body, piece by piece (with a 30-second limit on silence).</summary>
    static async IAsyncEnumerable<ReadOnlyMemory<byte>> Body(Stream stream, WebRequest request)
    {
        var buffer = new byte[1 << 20];
        using var idle = new CancellationTokenSource();
        if (!request.Chunked)
        {
            long left = request.Length;
            while (left > 0)
            {
                idle.CancelAfter(30000);
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), idle.Token);
                if (read == 0) throw new IOException("The phone stopped sending");
                left -= read;
                yield return buffer.AsMemory(0, read);
            }
            yield break;
        }
        while (true)
        {
            idle.CancelAfter(30000);
            var sizeLine = await ReadLine(stream, idle.Token, 1024);
            var hex = sizeLine.Split(';')[0].Trim();
            long chunk = Convert.ToInt64(hex.Length == 0 ? "0" : hex, 16);
            if (chunk == 0)
            {
                // Trailer lines, then the end.
                while ((await ReadLine(stream, idle.Token, 8192)).Length > 0) { }
                yield break;
            }
            long left = chunk;
            while (left > 0)
            {
                idle.CancelAfter(30000);
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), idle.Token);
                if (read == 0) throw new IOException("The phone stopped sending");
                left -= read;
                yield return buffer.AsMemory(0, read);
            }
            await ReadLine(stream, idle.Token, 16);
        }
    }

    static async Task<JsonObject> ReadJson(Stream stream, WebRequest request)
    {
        if (request.Length > 64 * 1024) throw new IOException("Too big");
        using var memory = new MemoryStream();
        await foreach (var chunk in Body(stream, request))
        {
            memory.Write(chunk.Span);
            if (memory.Length > 64 * 1024) throw new IOException("Too big");
        }
        return memory.Length == 0 ? new JsonObject() : JsonNode.Parse(memory.ToArray()) as JsonObject ?? new JsonObject();
    }

    static async Task Json(Stream stream, int status, JsonObject body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", _ => "OK" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    static string FileId(string id) => Clean(id.Replace('\t', ' ').Replace('\n', ' '), 200);

    void LoadIds()
    {
        lock (_gate)
        {
            if (_idsLoaded) return;
            _idsLoaded = true;
            try
            {
                if (File.Exists(ReceivedIdsPath))
                    foreach (var line in File.ReadAllLines(ReceivedIdsPath))
                        if (line.Split('\t') is [var id, var path]) _receivedIds[id] = path;
            }
            catch (Exception ex) { Log.Error("Reading the iPhone's received list", ex); }
        }
    }
}
