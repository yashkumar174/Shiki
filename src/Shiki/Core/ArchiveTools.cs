using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Shiki.Core;

/// <summary>Zips files and folders, and extracts zip, 7z, rar and tar archives.</summary>
public static class ArchiveTools
{
    /// <summary>
    /// Puts everything into one zip next to the first item. Folders keep their name inside the zip;
    /// files that sit inside a selected folder aren't added twice.
    /// </summary>
    public static string Zip(IReadOnlyList<string> items, IProgress<double> progress, CancellationToken ct)
    {
        var folders = items.Where(Directory.Exists).Select(f => Path.GetFullPath(f).TrimEnd('\\') + "\\").ToList();
        var roots = items.Where(i => Directory.Exists(i) || !folders.Any(f => Path.GetFullPath(i).StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roots.Count == 0) throw new InvalidOperationException("Nothing to zip.");

        // (file on disk, name inside the zip)
        var entries = new List<(string File, string Name)>();
        var emptyFolders = new List<string>();
        foreach (var root in roots)
        {
            if (File.Exists(root))
            {
                entries.Add((root, Path.GetFileName(root)));
                continue;
            }
            var parent = Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd('\\'))!;
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
            {
                var files = Directory.GetFiles(dir);
                if (files.Length == 0 && Directory.GetDirectories(dir).Length == 0) emptyFolders.Add(Path.GetRelativePath(parent, dir).Replace('\\', '/') + "/");
                entries.AddRange(files.Select(f => (f, Path.GetRelativePath(parent, f).Replace('\\', '/'))));
            }
        }

        var first = roots[0];
        var full = Path.GetFullPath(first).TrimEnd('\\');
        var stem = Directory.Exists(first) ? Path.GetFileName(full) : Path.GetFileNameWithoutExtension(full);
        var output = OutputPaths.Next(Path.Combine(Path.GetDirectoryName(full)!, stem + ".zip"), ".zip");

        long total = Math.Max(1, entries.Sum(e => new FileInfo(e.File).Length)), done = 0;
        var part = output + ".part";
        try
        {
            using (var zip = ZipFile.Open(part, ZipArchiveMode.Create))
            {
                foreach (var (file, name) in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    // Already-compressed files (photos, videos, archives) only get slower to zip.
                    var level = IsCompressed(file) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    var entry = zip.CreateEntry(name, level);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using var source = File.OpenRead(file);
                    using var target = entry.Open();
                    var buffer = new byte[1 << 16];
                    int read;
                    while ((read = source.Read(buffer)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, read);
                        done += read;
                        progress.Report((double)done / total);
                    }
                }
                foreach (var folder in emptyFolders) zip.CreateEntry(folder);
            }
            File.Move(part, output);
        }
        catch
        {
            File.Delete(part);
            throw;
        }
        int count = entries.Count;
        JobNotes.Say($"Zipped {count} {(count == 1 ? "file" : "files")} into {Path.GetFileName(output)}");
        return output;
    }

    static readonly HashSet<string> Compressed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".heic", ".avif", ".gif", ".mp4", ".mkv", ".mov", ".webm", ".mp3", ".m4a", ".aac", ".opus", ".ogg",
        ".zip", ".7z", ".rar", ".gz", ".xz", ".bz2", ".docx", ".xlsx", ".pptx",
    };

    static bool IsCompressed(string file) => Compressed.Contains(Path.GetExtension(file));

    /// <summary>
    /// Extracts next to the archive (or into the results folder). A single top-level folder inside comes out as itself; anything
    /// else goes into a folder named after the archive. Nothing is overwritten.
    /// </summary>
    public static string Extract(string archivePath, IProgress<double> progress, CancellationToken ct)
    {
        var dir = OutputPaths.ExtractDirectory(archivePath);
        var stem = StemOf(archivePath);
        var staging = Path.Combine(dir, $".{stem}.extracting");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        try
        {
            var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            string? Target(string? key)
            {
                var name = (key ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                if (name.Length == 0) name = stem;
                var target = Path.GetFullPath(Path.Combine(staging, name));
                // Zip slip: an entry like "..\..\Windows\x" must not land outside the folder.
                if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return target;
                Log.Info($"Skipped an archive entry that points outside the folder: {key}");
                return null;
            }
            void Write(string? key, DateTime? time, Stream source)
            {
                if (Target(key) is not { } target) return;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var output = File.Create(target)) source.CopyTo(output);
                if (time is { } t) File.SetLastWriteTime(target, t);
            }

            if (TryOpenArchive(archivePath) is { } archive)
            {
                using (archive)
                {
                    // Empty folders come back too.
                    foreach (var folder in archive.Entries.Where(e => e.IsDirectory))
                        if (Target(folder.Key) is { } path) Directory.CreateDirectory(path);
                    var files = archive.Entries.Where(e => !e.IsDirectory).ToList();
                    if (files.Any(e => e.IsEncrypted)) throw new InvalidOperationException(PasswordMessage);
                    long total = Math.Max(1, files.Sum(e => e.Size)), done = 0;
                    foreach (var entry in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        using (var source = entry.OpenEntryStream()) Write(entry.Key, entry.LastModifiedTime, source);
                        done += entry.Size;
                        progress.Report((double)done / total);
                    }
                }
            }
            else
            {
                // Compressed tarballs (.tar.gz, .tar.xz…) only open as a stream.
                using var file = File.OpenRead(archivePath);
                using var reader = OpenReader(file);
                while (reader.MoveToNextEntry())
                {
                    ct.ThrowIfCancellationRequested();
                    if (reader.Entry.IsDirectory) continue;
                    if (reader.Entry.IsEncrypted) throw new InvalidOperationException(PasswordMessage);
                    using (var source = reader.OpenEntryStream()) Write(reader.Entry.Key, reader.Entry.LastModifiedTime, source);
                    progress.Report((double)file.Position / Math.Max(1, file.Length));
                }
            }

            var top = Directory.GetFileSystemEntries(staging);
            string result;
            if (top.Length == 1 && Directory.Exists(top[0]))
            {
                result = UniqueFolder(dir, Path.GetFileName(top[0]));
                Directory.Move(top[0], result);
                Directory.Delete(staging);
            }
            else
            {
                result = UniqueFolder(dir, stem);
                Directory.Move(staging, result);
            }
            int count = Directory.EnumerateFiles(result, "*", SearchOption.AllDirectories).Count();
            JobNotes.Say($"Extracted {count} {(count == 1 ? "file" : "files")} to {Path.GetFileName(result)}");
            return result;
        }
        catch
        {
            try { Directory.Delete(staging, true); } catch (IOException) { }
            throw;
        }
    }

    const string PasswordMessage = "This archive has a password. Open it with 7-Zip or WinRAR.";

    static IArchive? TryOpenArchive(string path)
    {
        try
        {
            return ArchiveFactory.OpenArchive(path);
        }
        catch (Exception ex)
        {
            Log.Info($"Not openable as an archive, trying as a stream: {ex.Message}");
            return null;
        }
    }

    static IReader OpenReader(Stream stream)
    {
        try
        {
            return ReaderFactory.OpenReader(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("This doesn't look like an archive I can open, or it's damaged.", ex);
        }
    }

    /// <summary>"photos.tar.gz" → "photos".</summary>
    static string StemOf(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var double_ in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (name.EndsWith(double_, StringComparison.OrdinalIgnoreCase)) return name[..^double_.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    static string UniqueFolder(string dir, string name)
    {
        var candidate = Path.Combine(dir, name);
        for (int i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{name} ({i})");
        return candidate;
    }
}
