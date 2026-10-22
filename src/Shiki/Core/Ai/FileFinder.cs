using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Shiki.Core.Ai;

/// <summary>One file (or folder) the cat found, as it's described to Claude and shown in the chat.</summary>
public sealed record FoundFile(string Path, long Bytes, DateTime Modified, bool IsFolder)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>
/// Looks for files in the places people keep them (Downloads, Desktop, Documents…), by kind,
/// words in the name and age. Read-only: it never opens or changes what it finds.
/// </summary>
public static class FileFinder
{
    const int MaxDepth = 3, MaxScanned = 40_000;

    /// <summary>The names Claude can use for places; "explorer" is the folder open in File Explorer.</summary>
    public static readonly string[] Places = ["downloads", "desktop", "documents", "pictures", "videos", "music", "results", "explorer", "everywhere"];

    public static string? PlacePath(string place, string? explorerFolder) => place.ToLowerInvariant() switch
    {
        "downloads" => Downloads(),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "results" => OutputPaths.Folder,
        "explorer" => explorerFolder,
        _ => Directory.Exists(place) ? place : null,
    };

    public sealed record Query(string Where, string Kind, string? NameContains, double? WithinDays, int Limit);

    public static List<FoundFile> Find(Query query, string? explorerFolder)
    {
        var roots = query.Where.Equals("everywhere", StringComparison.OrdinalIgnoreCase)
            ? Places.Where(p => p is not ("everywhere" or "explorer")).Select(p => PlacePath(p, null)).Append(explorerFolder)
            : [PlacePath(query.Where, explorerFolder)];
        var folders = roots.Where(r => r != null && Directory.Exists(r)).Select(r => Path.GetFullPath(r!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Nested roots (Pictures inside OneDrive inside the home folder…) would be listed twice.
        folders = folders.Where(f => !folders.Any(o => o != f && f.StartsWith(o.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))).ToList();

        var words = (query.NameContains ?? "").Split([' ', ',', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DateTime? since = query.WithinDays is { } days ? DateTime.Now.AddDays(-days) : null;
        bool wantFolders = query.Kind.Equals("folder", StringComparison.OrdinalIgnoreCase);
        FileKind? kind = Enum.TryParse<FileKind>(query.Kind, true, out var k) && k != FileKind.Unknown ? k : null;

        var found = new List<FoundFile>();
        int scanned = 0;
        foreach (var root in folders) Walk(root, 0);
        return found.OrderByDescending(f => f.Modified).Take(Math.Clamp(query.Limit, 1, 50)).ToList();

        void Walk(string dir, int depth)
        {
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return;
            }
            foreach (var entry in entries)
            {
                if (++scanned > MaxScanned) return;
                if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                bool isFolder = entry is DirectoryInfo;
                if (isFolder && (entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                bool matches = isFolder ? wantFolders : !wantFolders && (kind == null ? FileKinds.Of(entry.FullName) != FileKind.Unknown : FileKinds.Of(entry.FullName) == kind);
                if (matches && (since == null || entry.LastWriteTime >= since)
                    && words.All(w => entry.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    found.Add(new FoundFile(entry.FullName, entry is FileInfo f ? f.Length : 0, entry.LastWriteTime, isFolder));
                if (isFolder && depth < MaxDepth && !Skip(entry.Name)) Walk(entry.FullName, depth + 1);
            }
        }
    }

    /// <summary>Folders full of code or app data, not things people mean.</summary>
    static bool Skip(string name) => name.StartsWith('.') || name is "node_modules" or "bin" or "obj" or "__pycache__" or "venv" or "My Music" or "My Pictures" or "My Videos";

    public static string Describe(FoundFile f) =>
        f.IsFolder ? $"{f.Path}  (folder, changed {Age(f.Modified)})" : $"{f.Path}  ({Size(f.Bytes)}, changed {Age(f.Modified)})";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    public static string Age(DateTime when)
    {
        var ago = DateTime.Now - when;
        if (when.Date == DateTime.Today) return $"today {when:HH:mm}";
        if (when.Date == DateTime.Today.AddDays(-1)) return $"yesterday {when:HH:mm}";
        return ago.TotalDays < 7 ? $"{when:dddd} ({when:yyyy-MM-dd})" : when.ToString("yyyy-MM-dd");
    }

    static string? Downloads()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
