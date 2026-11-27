using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Shiki.Core.Phone;

/// <summary>
/// Everything a photo or video says about itself, the way a phone's info panel shows it: when it
/// was taken, on what, with which settings, how big, and where. Read from Windows' own file
/// properties, and for videos also from the QuickTime metadata iPhones write (make, model, place).
/// </summary>
public sealed record MediaInfo
{
    public required string Path { get; init; }
    public required long Bytes { get; init; }
    public required bool Video { get; init; }
    /// <summary>JPEG, HEIF, PNG, HEVC, H.264…: what you'd call the file.</summary>
    public required string Format { get; init; }

    public DateTime? Taken { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Lens { get; init; }

    // Photo settings.
    public double? FNumber { get; init; }
    public double? Exposure { get; init; }
    public int? Iso { get; init; }
    public double? FocalLength { get; init; }
    public int? Focal35 { get; init; }
    public double? ExposureBias { get; init; }
    public bool? FlashFired { get; init; }

    /// <summary>As seen (turned the way the picture is meant to be looked at).</summary>
    public int? Width { get; init; }
    public int? Height { get; init; }

    // Video.
    public TimeSpan? Duration { get; init; }
    public double? FrameRate { get; init; }
    public long? Bitrate { get; init; }
    public string? Codec { get; init; }
    public string? Audio { get; init; }

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Altitude { get; init; }

    public bool HasPlace => Latitude is { } lat && Longitude is { } lon && !(Math.Abs(lat) < 1e-6 && Math.Abs(lon) < 1e-6);

    /// <summary>"Apple iPhone 14", or just the model when it already names the maker.</summary>
    public string? Camera => Model == null ? Make
        : Make == null || Model.StartsWith(Make, StringComparison.OrdinalIgnoreCase) ? Model
        : $"{Make} {Model}";

    static readonly string[] Keys =
    [
        "System.Photo.DateTaken", "System.Photo.CameraManufacturer", "System.Photo.CameraModel", "System.Photo.LensModel",
        "System.Photo.FNumber", "System.Photo.ExposureTime", "System.Photo.ISOSpeed", "System.Photo.FocalLength",
        "System.Photo.FocalLengthInFilm", "System.Photo.ExposureBias", "System.Photo.Flash", "System.Photo.Orientation",
        "System.Image.HorizontalSize", "System.Image.VerticalSize",
        "System.GPS.Latitude", "System.GPS.LatitudeRef", "System.GPS.Longitude", "System.GPS.LongitudeRef", "System.GPS.Altitude",
        "System.Media.Duration", "System.Media.DateEncoded", "System.Video.FrameWidth", "System.Video.FrameHeight",
        "System.Video.FrameRate", "System.Video.TotalBitrate", "System.Video.FourCC", "System.Audio.Format",
    ];

    static readonly HashSet<string> VideoTypes = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".3gp", ".mkv", ".webm", ".m4v" };

    /// <summary>Reads it all. Slow-ish (a file system round trip): call it off the UI thread.</summary>
    public static async Task<MediaInfo> ReadAsync(string path)
    {
        var file = new FileInfo(path);
        var ext = file.Extension.ToLowerInvariant();
        bool video = VideoTypes.Contains(ext);
        IDictionary<string, object> p = new Dictionary<string, object>();
        try
        {
            var item = await Windows.Storage.StorageFile.GetFileFromPathAsync(file.FullName);
            p = await item.Properties.RetrievePropertiesAsync(Keys);
        }
        catch (Exception ex) { Log.Error($"Reading the details of {file.Name}", ex); }

        var qt = video ? QuickTime.Read(file.FullName) : new Dictionary<string, string>();

        T? Get<T>(string key) where T : struct
        {
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            try { return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); }
            catch { return null; }
        }
        string? Text(string key) => p.TryGetValue(key, out var v) && v is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        string? Qt(string key) => qt.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        // When: the camera's own time, else the video's, else the time the phone gave the file.
        DateTime? taken = p.TryGetValue("System.Photo.DateTaken", out var dt) && dt is DateTimeOffset d ? d.LocalDateTime : null;
        if (taken == null && Qt("com.apple.quicktime.creationdate") is { } created && DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.None, out var c)) taken = c.LocalDateTime;
        if (taken == null && p.TryGetValue("System.Media.DateEncoded", out var de) && de is DateTimeOffset e) taken = e.LocalDateTime;
        taken ??= file.LastWriteTime;

        int? width, height;
        if (video) { width = Get<int>("System.Video.FrameWidth"); height = Get<int>("System.Video.FrameHeight"); }
        else
        {
            width = Get<int>("System.Image.HorizontalSize");
            height = Get<int>("System.Image.VerticalSize");
            // EXIF orientations 5–8 are turned a quarter: the picture is seen the other way round.
            if (Get<int>("System.Photo.Orientation") is >= 5 and <= 8) (width, height) = (height, width);
        }

        double? lat = Coordinate(p, "System.GPS.Latitude", "System.GPS.LatitudeRef", "S");
        double? lon = Coordinate(p, "System.GPS.Longitude", "System.GPS.LongitudeRef", "W");
        double? alt = Get<double>("System.GPS.Altitude");
        if (lat == null && Qt("com.apple.quicktime.location.ISO6709") is { } iso && Iso6709(iso) is { } place)
            (lat, lon, alt) = place;

        string? codec = Get<uint>("System.Video.FourCC") is { } fourCc ? CodecName(fourCc) : null;
        string? audio = Text("System.Audio.Format") is { } af && af.StartsWith("{00001610", StringComparison.OrdinalIgnoreCase) ? "AAC" : null;

        int? flash = Get<int>("System.Photo.Flash");
        return new MediaInfo
        {
            Path = file.FullName,
            Bytes = file.Length,
            Video = video,
            Format = video ? codec ?? ext.TrimStart('.').ToUpperInvariant() : FormatName(ext),
            Taken = taken,
            Make = Text("System.Photo.CameraManufacturer") ?? Qt("com.apple.quicktime.make"),
            Model = Text("System.Photo.CameraModel") ?? Qt("com.apple.quicktime.model"),
            Lens = LensName(Text("System.Photo.LensModel") ?? Qt("com.apple.quicktime.camera.lens_model"), Text("System.Photo.CameraModel") ?? Qt("com.apple.quicktime.model")),
            FNumber = Get<double>("System.Photo.FNumber"),
            Exposure = Get<double>("System.Photo.ExposureTime"),
            Iso = Get<int>("System.Photo.ISOSpeed"),
            FocalLength = Get<double>("System.Photo.FocalLength"),
            Focal35 = Get<int>("System.Photo.FocalLengthInFilm") ?? (int.TryParse(Qt("com.apple.quicktime.camera.focal_length.35mm_equivalent"), out var f35) ? f35 : null),
            ExposureBias = Get<double>("System.Photo.ExposureBias"),
            FlashFired = flash == null ? null : (flash & 1) == 1,
            Width = width,
            Height = height,
            Duration = Get<ulong>("System.Media.Duration") is { } ticks ? TimeSpan.FromTicks((long)ticks) : null,
            FrameRate = Get<uint>("System.Video.FrameRate") is { } fr and > 0 ? fr / 1000.0 : null,
            Bitrate = Get<uint>("System.Video.TotalBitrate") is { } br and > 0 ? br : null,
            Codec = codec,
            Audio = audio,
            Latitude = lat,
            Longitude = lon,
            Altitude = alt,
        };
    }

    static string FormatName(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => "JPEG",
        ".heic" or ".heif" => "HEIF",
        ".png" => "PNG",
        ".gif" => "GIF",
        ".webp" => "WebP",
        ".dng" => "RAW",
        _ => ext.TrimStart('.').ToUpperInvariant(),
    };

    static string CodecName(uint fourCc)
    {
        var text = Encoding.ASCII.GetString(BitConverter.GetBytes(fourCc)).Trim('\0', ' ').ToUpperInvariant();
        return text switch
        {
            "HEVC" or "HVC1" or "HEV1" or "H265" => "HEVC",
            "H264" or "AVC1" or "X264" => "H.264",
            "AV01" => "AV1",
            _ => text,
        };
    }

    /// <summary>"iPhone 14 back dual wide camera 5.7mm f/1.5" → "Back dual wide camera" (the rest is shown on its own).</summary>
    static string? LensName(string? lens, string? model)
    {
        if (string.IsNullOrWhiteSpace(lens)) return null;
        var name = lens.Trim();
        if (model != null && name.StartsWith(model, StringComparison.OrdinalIgnoreCase)) name = name[model.Length..].Trim();
        name = Regex.Replace(name, @"\s+[\d.]+\s*mm\b.*$", "", RegexOptions.IgnoreCase).Trim();
        if (name.Length == 0) return null;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    static double? Coordinate(IDictionary<string, object> p, string key, string refKey, string negative)
    {
        if (!p.TryGetValue(key, out var v) || v is not double[] { Length: >= 1 } dms) return null;
        double value = dms[0] + (dms.Length > 1 ? dms[1] / 60 : 0) + (dms.Length > 2 ? dms[2] / 3600 : 0);
        if (p.TryGetValue(refKey, out var r) && r is string s && s.Trim().Equals(negative, StringComparison.OrdinalIgnoreCase)) value = -value;
        return value;
    }

    /// <summary>"+22.8038+086.1592+135.696/" → latitude, longitude, height.</summary>
    static (double, double, double?)? Iso6709(string text)
    {
        var m = Regex.Match(text, @"^([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)?");
        if (!m.Success) return null;
        double lat = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        double lon = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        double? alt = m.Groups[3].Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null;
        return (lat, lon, alt);
    }

    /// <summary>
    /// The 'mdta' metadata in a QuickTime/MP4 file (moov → meta → keys + ilst): what iPhones write
    /// about a video, such as com.apple.quicktime.model and .location.ISO6709.
    /// </summary>
    static class QuickTime
    {
        public static Dictionary<string, string> Read(string path)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var stream = File.OpenRead(path);
                var moov = TopLevel(stream, "moov");
                if (moov == null) return found;
                var meta = Child(moov, 0, moov.Length, "meta");
                if (meta == null) return found;
                var (start, end) = meta.Value;
                // QuickTime's meta has no version field; MP4's does (then hdlr isn't where we look first).
                if (end - start >= 8 && Ascii(moov, start + 4) != "hdlr") start += 4;
                var keys = Child(moov, start, end, "keys");
                var ilst = Child(moov, start, end, "ilst");
                if (keys == null || ilst == null) return found;

                var names = new List<string>();
                int k = keys.Value.Start + 8; // version/flags, then the count
                while (k + 8 <= keys.Value.End)
                {
                    int size = Int(moov, k);
                    if (size < 8 || k + size > keys.Value.End) break;
                    names.Add(Encoding.UTF8.GetString(moov, k + 8, size - 8));
                    k += size;
                }

                int i = ilst.Value.Start;
                while (i + 8 <= ilst.Value.End)
                {
                    int size = Int(moov, i);
                    if (size < 8 || i + size > ilst.Value.End) break;
                    int index = Int(moov, i + 4);
                    var data = Child(moov, i + 8, i + size, "data");
                    if (data is { } dv && index >= 1 && index <= names.Count && dv.End - dv.Start >= 8)
                    {
                        int type = Int(moov, dv.Start) & 0xFFFFFF;
                        if (type == 1) found[names[index - 1]] = Encoding.UTF8.GetString(moov, dv.Start + 8, dv.End - dv.Start - 8);
                        else if (type is 21 or 22 && dv.End - dv.Start >= 9) found[names[index - 1]] = BigInteger(moov, dv.Start + 8, dv.End - dv.Start - 8).ToString(CultureInfo.InvariantCulture);
                    }
                    i += size;
                }
            }
            catch (Exception ex) { Log.Error($"Reading the video details of {System.IO.Path.GetFileName(path)}", ex); }
            return found;
        }

        /// <summary>The body of a top-level box (skipping over the big media data), or null.</summary>
        static byte[]? TopLevel(Stream stream, string type)
        {
            var head = new byte[16];
            long at = 0;
            while (at + 8 <= stream.Length)
            {
                stream.Position = at;
                if (stream.Read(head, 0, 8) < 8) return null;
                long size = (uint)Int(head, 0);
                int header = 8;
                if (size == 1)
                {
                    if (stream.Read(head, 8, 8) < 8) return null;
                    size = ((long)(uint)Int(head, 8) << 32) | (uint)Int(head, 12);
                    header = 16;
                }
                else if (size == 0) size = stream.Length - at;
                if (size < header) return null;
                if (Ascii(head, 4) == type)
                {
                    long body = size - header;
                    if (body > 64 << 20) return null; // a moov this big isn't one we want to read whole
                    var bytes = new byte[body];
                    stream.Position = at + header;
                    int read = 0;
                    while (read < body) { int n = stream.Read(bytes, read, (int)body - read); if (n <= 0) break; read += n; }
                    return bytes;
                }
                at += size;
            }
            return null;
        }

        static (int Start, int End)? Child(byte[] b, int start, int end, string type)
        {
            int i = start;
            while (i + 8 <= end)
            {
                int size = Int(b, i);
                if (size < 8 || i + size > end) return null;
                if (Ascii(b, i + 4) == type) return (i + 8, i + size);
                i += size;
            }
            return null;
        }

        static int Int(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
        static string Ascii(byte[] b, int at) => Encoding.ASCII.GetString(b, at, 4);
        static long BigInteger(byte[] b, int at, int length)
        {
            long v = 0;
            for (int j = 0; j < Math.Min(length, 8); j++) v = (v << 8) | b[at + j];
            return v;
        }
    }
}
