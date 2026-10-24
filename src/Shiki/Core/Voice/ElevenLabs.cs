using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shiki.Core.Ai;

namespace Shiki.Core.Voice;

/// <summary>
/// ElevenLabs voices for the cat, with the person's own key: their voice list (kept in the data
/// folder so Settings opens instantly) and speech streamed as raw 24 kHz sound, the same shape
/// as Deepgram's Aura, so it plays through the same speakers. Only for speaking: listening stays
/// with Deepgram (or Whisper).
/// </summary>
public static class ElevenLabs
{
    public static readonly SecretStore Key = new("Shiki/ElevenLabsApiKey");
    public static bool HasKey => Key.Exists;

    /// <summary>Flash v2.5: about 75 ms to the first sound, and it speaks Hindi too.</summary>
    public const string Model = "eleven_flash_v2_5";

    const string Api = "https://api.elevenlabs.io/";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static string CacheFile => Path.Combine(Log.DataDir, "elevenlabs-voices.json");

    public sealed record VoiceInfo(string Id, string Name, string Note);

    /// <summary>Characters spoken so far (for the running count in Settings).</summary>
    public static event Action<int>? Sent;

    static List<VoiceInfo>? _voices;

    /// <summary>The person's voices, as last fetched (empty until the key is saved).</summary>
    public static IReadOnlyList<VoiceInfo> Voices
    {
        get
        {
            if (_voices != null) return _voices;
            try
            {
                _voices = File.Exists(CacheFile) ? JsonSerializer.Deserialize<List<VoiceInfo>>(File.ReadAllText(CacheFile)) ?? [] : [];
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the ElevenLabs voices", ex);
                _voices = [];
            }
            return _voices;
        }
    }

    /// <summary>Checks the key by fetching the voice list (and keeps the list). Null when it works.</summary>
    public static async Task<string?> TestKey(string key)
    {
        try
        {
            await FetchVoices(key, CancellationToken.None);
            return null;
        }
        catch (ElevenLabsException ex)
        {
            return ex.Message;
        }
        catch (HttpRequestException)
        {
            return "I can't reach ElevenLabs. Check the internet connection.";
        }
        catch (TaskCanceledException)
        {
            return "ElevenLabs took too long to answer.";
        }
    }

    /// <summary>Fetches the voice list again (new voices added on the website), quietly.</summary>
    public static async Task Refresh()
    {
        if (Key.Read() is not { } key) return;
        try
        {
            await FetchVoices(key, CancellationToken.None);
        }
        catch (Exception ex) when (ex is ElevenLabsException or HttpRequestException or TaskCanceledException)
        {
            Log.Error("Could not refresh the ElevenLabs voices", ex);
        }
    }

    static async Task FetchVoices(string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + "v2/voices?page_size=100&sort=name&sort_direction=asc");
        request.Headers.Add("xi-api-key", key);
        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new ElevenLabsException((int)response.StatusCode, body);
        using var json = JsonDocument.Parse(body);
        var list = new List<VoiceInfo>();
        foreach (var v in json.RootElement.GetProperty("voices").EnumerateArray())
        {
            var id = v.GetProperty("voice_id").GetString();
            var name = v.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
            // A short note from its labels: "female, Indian" and the like.
            var note = new List<string>();
            if (v.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
                foreach (var label in new[] { "gender", "accent" })
                    if (labels.TryGetProperty(label, out var value) && value.GetString() is { Length: > 0 } text) note.Add(text);
            // Names like "Sarah - Mature, Reassuring" keep only the name.
            list.Add(new VoiceInfo(id, name.Split(" - ")[0].Trim(), string.Join(", ", note)));
        }
        _voices = list;
        try
        {
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(list));
        }
        catch (IOException ex)
        {
            Log.Error("Could not keep the ElevenLabs voices", ex);
        }
    }

    /// <summary>Starts the speech for this text: the response streams raw 16-bit sound at 24 kHz.</summary>
    internal static async Task<(HttpRequestMessage Request, HttpResponseMessage Response)> Stream(string text, string voiceId, CancellationToken ct)
    {
        var key = Key.Read() ?? throw new InvalidOperationException("No ElevenLabs key.");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}v1/text-to-speech/{Uri.EscapeDataString(voiceId)}/stream?output_format=pcm_24000")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { text, model_id = Model }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("xi-api-key", key);
        var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            request.Dispose();
            throw new ElevenLabsException((int)response.StatusCode, body);
        }
        Sent?.Invoke(text.Length);
        return (request, response);
    }

    /// <summary>The whole speech as a WAV (previews and tests).</summary>
    public static async Task<byte[]> Wav(string text, string voiceId, CancellationToken ct)
    {
        var (request, response) = await Stream(text, voiceId, ct);
        using (request)
        using (response)
        {
            var pcm = await response.Content.ReadAsByteArrayAsync(ct);
            using var wav = new MemoryStream();
            using (var writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(24000, 16, 1)))
                writer.Write(pcm, 0, pcm.Length);
            return wav.ToArray();
        }
    }
}

/// <summary>ElevenLabs said no: the reason in plain words (a wrong key, no credit left, a missing voice).</summary>
public sealed class ElevenLabsException(int status, string body) : Exception(Explain(status, body))
{
    public int Status { get; } = status;

    static string Explain(int status, string body)
    {
        string detail = "";
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("detail", out var d))
                detail = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("message", out var m) ? m.GetString() ?? "" : d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString();
        }
        catch (JsonException)
        {
            detail = body.Length > 200 ? body[..200] : body;
        }
        return status switch
        {
            401 when detail.Contains("permission", StringComparison.OrdinalIgnoreCase) => $"That ElevenLabs key isn't allowed to do this: give it Text to Speech and Voices (read) access on elevenlabs.io. ({detail})",
            401 => "ElevenLabs didn't accept that key. Copy it again from elevenlabs.io (Developers, API keys).",
            402 or 429 when detail.Contains("quota", StringComparison.OrdinalIgnoreCase) || detail.Contains("credit", StringComparison.OrdinalIgnoreCase) => "Your ElevenLabs credit has run out for now.",
            429 => "ElevenLabs is busy (too many requests). Try again in a moment.",
            404 => "ElevenLabs doesn't have that voice any more. Pick another in Settings.",
            _ => $"ElevenLabs said: {(detail.Length > 0 ? detail : $"error {status}")}",
        };
    }
}
