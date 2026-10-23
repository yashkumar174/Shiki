using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shiki.Core;

/// <summary>
/// What a file is, in facts the cat can explain: its kind, size and dates, and what Windows knows
/// about it (when a photo was taken and with what, a video's length, a song's artist, a program's
/// publisher and whether it's signed). Read-only.
/// </summary>
public static class FileFacts
{
    public static async Task<string> Describe(string path)
    {
        var facts = new StringBuilder();
        if (Directory.Exists(path))
        {
            var dir = new DirectoryInfo(path);
            int files = 0, folders = 0;
            long bytes = 0;
            foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Take(50_000))
            {
                if (entry is FileInfo f) { files++; bytes += f.Length; }
                else folders++;
            }
            return $"A folder: {files:N0} files in {folders:N0} subfolders, {FileChores.Size(bytes)}. Made {dir.CreationTime:d MMM yyyy}, changed {dir.LastWriteTime:d MMM yyyy}.";
        }
        var info = new FileInfo(path);
        facts.AppendLine($"Name: {info.Name}");
        facts.AppendLine($"Kind: {KindName(info.Extension)}");
        facts.AppendLine($"Size: {FileChores.Size(info.Length)}");
        facts.AppendLine($"Made on this PC: {info.CreationTime:d MMM yyyy HH:mm}; last changed: {info.LastWriteTime:d MMM yyyy HH:mm}");
        if (info.Attributes.HasFlag(FileAttributes.ReadOnly)) facts.AppendLine("Read-only.");

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            switch (FileKinds.Of(path))
            {
                case FileKind.Image:
                {
                    var p = await file.Properties.GetImagePropertiesAsync();
                    if (p.Width > 0) facts.AppendLine($"Picture: {p.Width} x {p.Height} pixels");
                    if (p.DateTaken.Year > 1900) facts.AppendLine($"Taken: {p.DateTaken.LocalDateTime:d MMM yyyy HH:mm}");
                    var camera = $"{p.CameraManufacturer} {p.CameraModel}".Trim();
                    if (camera.Length > 0) facts.AppendLine($"Camera: {camera}");
                    if (p.Latitude is { } lat && p.Longitude is { } lon) facts.AppendLine($"Location stored in the photo: {lat:0.####}, {lon:0.####}");
                    break;
                }
                case FileKind.Video:
                {
                    var p = await file.Properties.GetVideoPropertiesAsync();
                    if (p.Duration > TimeSpan.Zero) facts.AppendLine($"Length: {Length(p.Duration)}");
                    if (p.Width > 0) facts.AppendLine($"Video: {p.Width} x {p.Height}");
                    if (p.Latitude is { } lat && p.Longitude is { } lon) facts.AppendLine($"Location stored in the video: {lat:0.####}, {lon:0.####}");
                    break;
                }
                case FileKind.Audio:
                {
                    var p = await file.Properties.GetMusicPropertiesAsync();
                    if (p.Duration > TimeSpan.Zero) facts.AppendLine($"Length: {Length(p.Duration)}");
                    if (p.Title.Length > 0) facts.AppendLine($"Title: {p.Title}");
                    if (p.Artist.Length > 0) facts.AppendLine($"Artist: {p.Artist}");
                    if (p.Album.Length > 0) facts.AppendLine($"Album: {p.Album}");
                    break;
                }
                case FileKind.Pdf or FileKind.Document:
                {
                    var p = await file.Properties.GetDocumentPropertiesAsync();
                    if (p.Title.Length > 0) facts.AppendLine($"Title: {p.Title}");
                    if (p.Author.Count > 0) facts.AppendLine($"Author: {string.Join(", ", p.Author)}");
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Windows has nothing more to say about it.
        }

        if (info.Extension.ToLowerInvariant() is ".pdf")
        {
            try
            {
                using var doc = Docnet.Core.DocLib.Instance.GetDocReader(path, new Docnet.Core.Models.PageDimensions(1.0));
                facts.AppendLine($"Pages: {doc.GetPageCount()}");
            }
            catch (Exception)
            {
                facts.AppendLine("It's locked with a password (or damaged), so its pages can't be read.");
            }
        }
        if (info.Extension.ToLowerInvariant() is ".exe" or ".msi" or ".dll")
        {
            var version = FileVersionInfo.GetVersionInfo(path);
            if (version.CompanyName is { Length: > 0 } company) facts.AppendLine($"Publisher (as the file says): {company}");
            if (version.FileDescription is { Length: > 0 } description) facts.AppendLine($"Description: {description}");
            if (version.ProductVersion is { Length: > 0 } product) facts.AppendLine($"Version: {product}");
            var (status, signer) = Signature(path);
            facts.AppendLine(status switch
            {
                "Valid" => $"Digitally signed{(signer != null ? $" by {signer}" : "")}, and the signature checks out.",
                "NotSigned" => "It is NOT digitally signed: nobody vouches for it, so be careful running it.",
                "Unknown" => "Its signature couldn't be checked.",
                _ => $"Its signature is NOT valid ({status}): it may have been changed since it was signed. Don't run it.",
            });
        }
        if (Zone(path) is { } from) facts.AppendLine($"Downloaded from: {from}");
        return facts.ToString().TrimEnd();
    }

    static string Length(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    static string KindName(string ext) => ext.ToLowerInvariant() switch
    {
        ".exe" => "a program (runs when opened)",
        ".msi" or ".msix" or ".appx" => "an installer",
        ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js" => "a script (runs commands when opened)",
        ".lnk" => "a shortcut",
        ".iso" => "a disc image",
        ".torrent" => "a torrent file",
        ".ics" => "a calendar invite",
        ".vcf" => "a contact card",
        ".srt" => "subtitles",
        ".apk" => "an Android app",
        "" => "no extension",
        _ => FileKinds.Of(ext) is var kind && kind != FileKind.Unknown ? $"{kind.ToString().ToLowerInvariant()} ({ext})" : ext,
    };

    /// <summary>Where a download came from, as Windows noted it (the Mark of the Web).</summary>
    static string? Zone(string path)
    {
        try
        {
            var zone = File.ReadAllText(path + ":Zone.Identifier");
            var host = zone.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("HostUrl=") || l.StartsWith("ReferrerUrl="));
            return host != null && Uri.TryCreate(host[(host.IndexOf('=') + 1)..], UriKind.Absolute, out var uri) ? uri.Host : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Windows' own signature check (files signed inside them and Windows' own files, which are
    /// signed in its catalogs): the status ("Valid", "NotSigned"…) and who signed it.
    /// </summary>
    static (string Status, string? Signer) Signature(string path)
    {
        try
        {
            var script = $"$s = Get-AuthenticodeSignature -LiteralPath '{path.Replace("'", "''")}'; $s.Status; $s.SignerCertificate.Subject";
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi)!;
            // PowerShell's first-run progress goes to stderr: read it so it can't fill up, and drop it.
            _ = process.StandardError.ReadToEndAsync();
            var lines = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            process.WaitForExit(15000);
            var signer = lines.Length > 1 && System.Text.RegularExpressions.Regex.Match(lines[1], @"CN=(""[^""]+""|[^,]+)") is { Success: true } cn ? cn.Groups[1].Value.Trim('"') : null;
            return (lines.FirstOrDefault() ?? "Unknown", signer);
        }
        catch (Exception)
        {
            return ("Unknown", null);
        }
    }
}
