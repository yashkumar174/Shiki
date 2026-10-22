using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConvertedBro.Core;

public enum FileKind { Unknown, Image, Video, Audio, Pdf, Archive, Document }

public static class FileKinds
{
    static readonly Dictionary<FileKind, string[]> Extensions = new()
    {
        [FileKind.Image] = [".png", ".jpg", ".jpeg", ".jfif", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".avif", ".ico", ".svg"],
        [FileKind.Video] = [".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".3gp", ".mpg", ".mpeg", ".ts"],
        [FileKind.Audio] = [".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".aiff", ".amr"],
        [FileKind.Pdf] = [".pdf"],
        [FileKind.Document] = [".doc", ".docx", ".docm", ".odt", ".rtf", ".xls", ".xlsx", ".xlsm", ".ods", ".csv", ".ppt", ".pptx", ".pptm", ".odp"],
        [FileKind.Archive] = [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"],
    };

    static readonly Dictionary<string, FileKind> ByExtension = Extensions
        .SelectMany(kv => kv.Value.Select(ext => (ext, kind: kv.Key)))
        .ToDictionary(x => x.ext, x => x.kind, StringComparer.OrdinalIgnoreCase);

    public static FileKind Of(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var kind) ? kind : FileKind.Unknown;

    public static FileKind OfExtension(string ext) =>
        ByExtension.TryGetValue(ext, out var kind) ? kind : FileKind.Unknown;

    public static IEnumerable<string> AllExtensions => ByExtension.Keys;

    public static bool IsJpeg(string ext) => ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".jfif", StringComparison.OrdinalIgnoreCase);
}
