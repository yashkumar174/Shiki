using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core.Mail;

public sealed record MailSummary(string Id, string ThreadId, string From, string Subject, DateTime? Date, string Snippet, bool Unread);

public sealed record MailMessage(string Id, string ThreadId, string From, string To, string Subject, DateTime? Date, string Body, string MessageIdHeader, IReadOnlyList<string> Attachments);

/// <summary>
/// Gmail through Google's own API: search, read, draft and send in a moment, without opening the
/// browser. Signing in happens on Google's page in the browser (Shiki never sees the password);
/// what Google hands back stays in Windows Credential Manager, and Disconnect takes it back.
/// The OAuth client is the person's own, made once in Google Cloud (see the steps in Settings).
/// </summary>
public static class Gmail
{
    static readonly SecretStore Client = new("Shiki/GmailClient");   // "client id\nclient secret"
    static readonly SecretStore Refresh = new("Shiki/GmailToken");   // the refresh token
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    const string Scope = "https://www.googleapis.com/auth/gmail.modify";
    const string Api = "https://gmail.googleapis.com/gmail/v1/users/me/";

    static string? _access;
    static DateTime _accessUntil;

    public static bool HasClient => Client.Exists;
    public static bool Connected => Refresh.Exists && Client.Exists;

    /// <summary>Saves the person's OAuth client; empty forgets it (and the sign-in made with it).</summary>
    public static void SaveClient(string id, string secret)
    {
        if (id.Trim().Length == 0)
        {
            Client.Remove();
            Refresh.Remove();
            _access = null;
            return;
        }
        Client.Save($"{id.Trim()}\n{secret.Trim()}");
    }

    static (string Id, string Secret)? ClientPair() =>
        Client.Read() is { } both && both.Split('\n') is [var id, var secret] ? (id, secret) : null;

    /// <summary>
    /// Opens Google's sign-in in the browser and waits for it to come back to this PC (a one-off
    /// local address). Returns the Gmail address.
    /// </summary>
    public static async Task<string> Connect(CancellationToken ct)
    {
        if (ClientPair() is not var (id, secret)) throw new InvalidOperationException("Add the Google OAuth client first.");
        // A free port on this PC for Google to send the person back to.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var redirect = $"http://127.0.0.1:{port}/";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&",
            $"client_id={Uri.EscapeDataString(id)}", $"redirect_uri={Uri.EscapeDataString(redirect)}", "response_type=code",
            $"scope={Uri.EscapeDataString(Scope)}", $"code_challenge={challenge}", "code_challenge_method=S256",
            "access_type=offline", "prompt=consent", $"state={state}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        var contextTask = listener.GetContextAsync();
        if (await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5), ct)) != contextTask)
            throw new TimeoutException("Google's sign-in didn't come back in time.");
        var context = await contextTask;
        var query = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
        bool ok = query["state"] == state && query["code"] is { Length: > 0 };
        var page = Encoding.UTF8.GetBytes(ok
            ? "<html><body style='font-family:Segoe UI;padding:40px'><h2>Gmail is connected to Shiki.</h2><p>You can close this tab.</p></body></html>"
            : "<html><body style='font-family:Segoe UI;padding:40px'><h2>Gmail wasn't connected.</h2><p>Go back to Shiki and try again.</p></body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(page, ct);
        context.Response.Close();
        if (!ok) throw new InvalidOperationException(query["error"] is { } error ? $"Google said: {error}" : "Google's answer didn't match.");

        using var exchange = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = id, ["client_secret"] = secret, ["code"] = query["code"]!, ["code_verifier"] = verifier,
            ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code",
        }), ct);
        using var json = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync(ct));
        if (!exchange.IsSuccessStatusCode) throw new InvalidOperationException($"Google didn't hand over access: {Error(json)}");
        if (!json.RootElement.TryGetProperty("refresh_token", out var refresh)) throw new InvalidOperationException("Google didn't give a lasting sign-in. Try again.");
        Refresh.Save(refresh.GetString()!);
        Remember(json.RootElement);
        var email = await Address(ct);
        AiLog.Write("gmail", $"connected {email}");
        return email;
    }

    /// <summary>Signs out: Google forgets Shiki's access, and so does this PC.</summary>
    public static async Task Disconnect()
    {
        if (Refresh.Read() is { } token)
        {
            try
            {
                using var _ = await Http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
            }
            catch (HttpRequestException)
            {
                // Offline: forgetting it here still disconnects.
            }
        }
        Refresh.Remove();
        _access = null;
        AiLog.Write("gmail", "disconnected");
    }

    static void Remember(JsonElement token)
    {
        _access = token.GetProperty("access_token").GetString();
        _accessUntil = DateTime.UtcNow.AddSeconds(token.TryGetProperty("expires_in", out var e) ? e.GetInt32() - 60 : 3000);
    }

    static string Error(JsonDocument json) =>
        json.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() ?? "" :
        json.RootElement.TryGetProperty("error", out var e) ? e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : e.ToString() : "unknown error";

    static async Task<string> AccessToken(CancellationToken ct)
    {
        if (_access != null && DateTime.UtcNow < _accessUntil) return _access;
        if (ClientPair() is not var (id, secret) || Refresh.Read() is not { } refresh) throw new InvalidOperationException("Gmail isn't connected. Connect it in Settings, under Gmail.");
        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = id, ["client_secret"] = secret, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token",
        }), ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode)
        {
            // Revoked from the Google account, or the client was changed: sign in again.
            if (json.RootElement.TryGetProperty("error", out var e) && e.GetString() == "invalid_grant") Refresh.Remove();
            throw new InvalidOperationException($"Gmail needs connecting again (Settings, Gmail): {Error(json)}");
        }
        Remember(json.RootElement);
        return _access!;
    }

    static async Task<JsonDocument> Call(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, Api + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessToken(ct));
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, ct);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct) is { Length: > 0 } text ? text : "{}");
        if (!response.IsSuccessStatusCode)
        {
            var message = Error(json);
            json.Dispose();
            throw new InvalidOperationException($"Gmail said: {message}");
        }
        return json;
    }

    public static async Task<string> Address(CancellationToken ct)
    {
        using var json = await Call(HttpMethod.Get, "profile", null, ct);
        return json.RootElement.GetProperty("emailAddress").GetString() ?? "";
    }

    /// <summary>Messages matching a Gmail search ("from:rahul newer_than:7d", "is:unread"), newest first.</summary>
    public static async Task<List<MailSummary>> Search(string query, int max, CancellationToken ct)
    {
        using var list = await Call(HttpMethod.Get, $"messages?maxResults={Math.Clamp(max, 1, 25)}&q={Uri.EscapeDataString(query)}", null, ct);
        if (!list.RootElement.TryGetProperty("messages", out var messages)) return [];
        var ids = messages.EnumerateArray().Select(m => m.GetProperty("id").GetString()!).ToList();
        // The headers of each, all at once.
        var found = await Task.WhenAll(ids.Select(async id =>
        {
            using var m = await Call(HttpMethod.Get, $"messages/{id}?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date", null, ct);
            var root = m.RootElement;
            var headers = Headers(root.GetProperty("payload"));
            bool unread = root.TryGetProperty("labelIds", out var labels) && labels.EnumerateArray().Any(l => l.GetString() == "UNREAD");
            return new MailSummary(id, root.GetProperty("threadId").GetString()!, headers.GetValueOrDefault("from", ""), headers.GetValueOrDefault("subject", ""),
                Date(root), WebUtility.HtmlDecode(root.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : ""), unread);
        }));
        return [.. found];
    }

    public static async Task<MailMessage> Read(string id, CancellationToken ct)
    {
        using var m = await Call(HttpMethod.Get, $"messages/{id}?format=full", null, ct);
        var root = m.RootElement;
        var payload = root.GetProperty("payload");
        var headers = Headers(payload);
        var attachments = new List<string>();
        string? plain = null, html = null;
        void Walk(JsonElement part)
        {
            var type = part.TryGetProperty("mimeType", out var t) ? t.GetString() : "";
            var name = part.TryGetProperty("filename", out var f) ? f.GetString() : "";
            if (!string.IsNullOrEmpty(name)) attachments.Add(name);
            else if (part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data))
            {
                var text = Encoding.UTF8.GetString(FromBase64Url(data.GetString()!));
                if (type == "text/plain") plain ??= text;
                else if (type == "text/html") html ??= text;
            }
            if (part.TryGetProperty("parts", out var parts)) foreach (var p in parts.EnumerateArray()) Walk(p);
        }
        Walk(payload);
        var bodyText = plain ?? (html != null ? HtmlToText(html) : "");
        return new MailMessage(id, root.GetProperty("threadId").GetString()!, headers.GetValueOrDefault("from", ""), headers.GetValueOrDefault("to", ""),
            headers.GetValueOrDefault("subject", ""), Date(root), bodyText.Trim(), headers.GetValueOrDefault("message-id", ""), attachments);
    }

    public sealed record Draft(string To, string Subject, string Body, string? ReplyToId);

    /// <summary>The message as Gmail wants it: the whole email, base64url. A reply keeps its thread.</summary>
    static async Task<object> Raw(Draft draft, CancellationToken ct)
    {
        string? threadId = null, inReplyTo = null, subject = draft.Subject;
        if (draft.ReplyToId is { Length: > 0 } replyTo)
        {
            var original = await Read(replyTo, ct);
            threadId = original.ThreadId;
            inReplyTo = original.MessageIdHeader;
            if (string.IsNullOrWhiteSpace(subject)) subject = original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? original.Subject : "Re: " + original.Subject;
        }
        var mime = new StringBuilder();
        mime.Append("To: ").Append(draft.To).Append("\r\n");
        mime.Append("Subject: =?UTF-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(subject ?? ""))).Append("?=\r\n");
        if (inReplyTo is { Length: > 0 }) mime.Append("In-Reply-To: ").Append(inReplyTo).Append("\r\nReferences: ").Append(inReplyTo).Append("\r\n");
        mime.Append("MIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        mime.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(draft.Body.Replace("\r\n", "\n").Replace("\n", "\r\n")), Base64FormattingOptions.InsertLineBreaks));
        var raw = Base64Url(Encoding.UTF8.GetBytes(mime.ToString()));
        return threadId != null ? new { raw, threadId } : new { raw };
    }

    public static async Task<string> Send(Draft draft, CancellationToken ct)
    {
        using var sent = await Call(HttpMethod.Post, "messages/send", await Raw(draft, ct), ct);
        AiLog.Write("gmail sent", $"to {draft.To}: {draft.Subject}");
        return sent.RootElement.GetProperty("id").GetString()!;
    }

    public static async Task<string> SaveDraft(Draft draft, CancellationToken ct)
    {
        using var saved = await Call(HttpMethod.Post, "drafts", new { message = await Raw(draft, ct) }, ct);
        AiLog.Write("gmail draft", $"to {draft.To}: {draft.Subject}");
        return saved.RootElement.GetProperty("id").GetString()!;
    }

    public static async Task MarkRead(string id, CancellationToken ct)
    {
        using var _ = await Call(HttpMethod.Post, $"messages/{id}/modify", new { removeLabelIds = new[] { "UNREAD" } }, ct);
    }

    public static async Task Archive(string id, CancellationToken ct)
    {
        using var _ = await Call(HttpMethod.Post, $"messages/{id}/modify", new { removeLabelIds = new[] { "INBOX" } }, ct);
        AiLog.Write("gmail archived", id);
    }

    static Dictionary<string, string> Headers(JsonElement payload) =>
        payload.TryGetProperty("headers", out var headers)
            ? headers.EnumerateArray().GroupBy(h => h.GetProperty("name").GetString()!.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().GetProperty("value").GetString() ?? "")
            : [];

    static DateTime? Date(JsonElement message) =>
        message.TryGetProperty("internalDate", out var d) && long.TryParse(d.GetString(), out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime : null;

    static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<(script|style)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>|</p>|</div>|</tr>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, @"<[^>]+>", " "));
        return Regex.Replace(Regex.Replace(text, @"[ \t]+", " "), @"\n\s*\n+", "\n\n");
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
