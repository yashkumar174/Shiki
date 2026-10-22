using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Shiki.Core;

/// <summary>
/// The file chores the cat does by voice: tidying a folder into subfolders (with undo), finding
/// what fills the disk, backing a folder up to another drive, and copying files somewhere. Nothing
/// is ever deleted; every move is written down first so it can be put back.
/// </summary>
public static class FileChores
{
    // ---- Tidy ----

    public sealed record Move(string From, string To);

    /// <summary>What a tidy did: the moves, and the folders it made (undo removes those if empty).</summary>
    public sealed record UndoList(List<Move> Moves, List<string> Made);

    static readonly string[] Unfinished = [".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload"];
    static readonly string[] Installers = [".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle"];

    static string? FolderFor(string file)
    {
        var ext = Path.GetExtension(file);
        if (Installers.Contains(ext, StringComparer.OrdinalIgnoreCase)) return "Installers";
        if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)) return "Documents";
        return FileKinds.Of(file) switch
        {
            FileKind.Image => "Images",
            FileKind.Video => "Videos",
            FileKind.Audio => "Music",
            FileKind.Pdf => "PDFs",
            FileKind.Document => "Documents",
            FileKind.Archive => "Archives",
            _ => null,
        };
    }

    /// <summary>
    /// What tidying the folder would do: each loose file (not ones still downloading or just
    /// changed) into a subfolder for its kind. Files of unknown kinds stay where they are.
    /// </summary>
    public static List<Move> PlanTidy(string folder)
    {
        var moves = new List<Move>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System)) continue;
            if (Unfinished.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) continue;
            if (DateTime.Now - info.LastWriteTime < TimeSpan.FromMinutes(2)) continue;
            if (FolderFor(file) is not { } sub) continue;
            var target = Free(Path.Combine(folder, sub, info.Name), taken);
            taken.Add(target);
            moves.Add(new Move(file, target));
        }
        return moves;
    }

    /// <summary>"143 files: 60 images, 20 PDFs…" for the card.</summary>
    public static string Describe(List<Move> moves) =>
        string.Join(", ", moves.GroupBy(m => Path.GetFileName(Path.GetDirectoryName(m.To))).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} to {g.Key}"));

    /// <summary>Moves the files, writing the list down first so Undo can put them back.</summary>
    public static (string Outcome, string UndoFile) Tidy(string folder, List<Move> moves)
    {
        var undo = UndoFile("tidy");
        var made = new List<string>();
        File.WriteAllText(undo, JsonSerializer.Serialize(new UndoList(moves, made)));
        int moved = 0, skipped = 0;
        var done = new List<Move>();
        foreach (var move in moves)
        {
            try
            {
                if (!File.Exists(move.From)) { skipped++; continue; }
                var into = Path.GetDirectoryName(move.To)!;
                if (!Directory.Exists(into))
                {
                    Directory.CreateDirectory(into);
                    made.Add(into);
                }
                // The name may have been taken since the plan was made.
                var to = File.Exists(move.To) ? Free(move.To, []) : move.To;
                File.Move(move.From, to);
                done.Add(move with { To = to });
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Open in another app: leave it.
                skipped++;
            }
        }
        File.WriteAllText(undo, JsonSerializer.Serialize(new UndoList(done, made)));
        Ai.AiLog.Write("tidied", $"{folder}: {moved} moved, {skipped} left");
        return ($"Tidied {moved} file{(moved == 1 ? "" : "s")} into folders{(skipped > 0 ? $"; {skipped} were in use and stayed" : "")}.", undo);
    }

    /// <summary>Puts moved files back where they were (unless something else is there now).</summary>
    public static string Undo(string undoFile)
    {
        if (!File.Exists(undoFile)) return "There's nothing to undo.";
        var list = JsonSerializer.Deserialize<UndoList>(File.ReadAllText(undoFile)) ?? new UndoList([], []);
        int back = 0, stuck = 0;
        foreach (var move in Enumerable.Reverse(list.Moves))
        {
            try
            {
                if (File.Exists(move.To) && !File.Exists(move.From))
                {
                    File.Move(move.To, move.From);
                    back++;
                }
                else stuck++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stuck++;
            }
        }
        // Folders the tidy made, if they're empty again.
        foreach (var folder in list.Made)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (IOException)
            {
                // In use: leave it.
            }
        }
        File.Delete(undoFile);
        Ai.AiLog.Write("undone", $"{back} back, {stuck} not");
        return stuck == 0 ? $"Put {back} file{(back == 1 ? "" : "s")} back." : $"Put {back} back; {stuck} had moved again or were in use.";
    }

    static string UndoFile(string what)
    {
        var folder = Path.Combine(Log.DataDir, "undo");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{what} {DateTime.Now:yyyy-MM-dd HH.mm.ss}.json");
    }

    /// <summary>The newest undo list, for "undo that" after the card is gone.</summary>
    public static string? LastUndo()
    {
        var folder = Path.Combine(Log.DataDir, "undo");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null;
    }

    static string Free(string path, HashSet<string> taken)
    {
        if (!File.Exists(path) && !taken.Contains(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var next = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(next) && !taken.Contains(next)) return next;
        }
    }

    // ---- Disk space ----

    /// <summary>
    /// What fills the disk: each drive's space, the biggest of the usual folders, the biggest
    /// files under the user's folders, and the Recycle Bin. Read-only; stops after about 20 seconds.
    /// </summary>
    public static string DiskReport(CancellationToken ct)
    {
        var report = new StringBuilder();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
            report.AppendLine($"Drive {drive.Name.TrimEnd('\\')} {(drive.VolumeLabel is { Length: > 0 } l ? $"({l}) " : "")}: {Size(drive.AvailableFreeSpace)} free of {Size(drive.TotalSize)}{(drive.DriveType == DriveType.Removable ? ", removable" : "")}");

        var deadline = DateTime.Now.AddSeconds(20);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = new (string Name, string Path)[]
        {
            ("Downloads", Path.Combine(home, "Downloads")),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Temporary files", Path.GetTempPath()),
        };
        var big = new List<(string Path, long Bytes)>();
        var sizes = new List<(string Name, string Path, long Bytes, bool Partial)>();
        foreach (var (name, path) in places.Where(p => Directory.Exists(p.Path)).DistinctBy(p => p.Path.TrimEnd('\\').ToLowerInvariant()))
        {
            long total = 0;
            bool partial = false;
            foreach (var file in Walk(path))
            {
                if (ct.IsCancellationRequested || DateTime.Now > deadline) { partial = true; break; }
                try
                {
                    var length = new FileInfo(file).Length;
                    total += length;
                    if (length > 200L * 1024 * 1024) big.Add((file, length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gone or locked: skip.
                }
            }
            sizes.Add((name, path, total, partial));
        }
        report.AppendLine().AppendLine("Your folders, biggest first:");
        foreach (var s in sizes.OrderByDescending(s => s.Bytes))
            report.AppendLine($"- {s.Name}: {(s.Partial ? "over " : "")}{Size(s.Bytes)} ({s.Path})");
        if (RecycleBin() is { } bin && bin.Bytes > 0) report.AppendLine($"- Recycle Bin: {Size(bin.Bytes)} in {bin.Items:N0} items");
        if (big.Count > 0)
        {
            report.AppendLine().AppendLine("Biggest files (over 200 MB):");
            foreach (var (path, bytes) in big.OrderByDescending(b => b.Bytes).Take(15))
                report.AppendLine($"- {Size(bytes)}: {path} (changed {File.GetLastWriteTime(path):d MMM yyyy})");
        }
        return report.ToString().TrimEnd();
    }

    /// <summary>Every file under the folder, skipping folders it can't read and links.</summary>
    static IEnumerable<string> Walk(string root) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        });

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };

    static (long Bytes, long Items)? RecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : null;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    // ---- Backup and copy ----

    /// <summary>Other drives a backup can go to (not the one the folder is on).</summary>
    public static List<DriveInfo> BackupDrives(string folder)
    {
        var source = Path.GetPathRoot(Path.GetFullPath(folder));
        return DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network
            && !string.Equals(d.RootDirectory.FullName, source, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Where the backup of the folder goes on that drive: "E:\Shiki backups\Documents".</summary>
    public static string BackupTarget(string folder, string drive) =>
        Path.Combine(drive, "Shiki backups", Path.GetFileName(Path.GetFullPath(folder).TrimEnd('\\')));

    /// <summary>
    /// Copies what's new or changed since the last backup (a file already there with the same
    /// size and date is left alone). Never deletes anything in the backup.
    /// </summary>
    public static string Backup(string folder, string target, IProgress<double>? progress, CancellationToken ct)
    {
        var files = Walk(folder).ToList();
        long copied = 0, bytes = 0, same = 0, failed = 0;
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            var to = Path.Combine(target, Path.GetRelativePath(folder, file));
            try
            {
                var from = new FileInfo(file);
                var there = new FileInfo(to);
                if (there.Exists && there.Length == from.Length && Math.Abs((there.LastWriteTimeUtc - from.LastWriteTimeUtc).TotalSeconds) < 2)
                {
                    same++;
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    from.CopyTo(to, overwrite: true);
                    copied++;
                    bytes += from.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
            if (i % 20 == 0) progress?.Report((i + 1) / (double)files.Count);
        }
        Ai.AiLog.Write("backed up", $"{folder} to {target}: {copied} copied ({Size(bytes)}), {same} unchanged, {failed} failed");
        var outcome = copied == 0 ? $"Backup is up to date ({same:N0} files already there)." : $"Backed up {copied:N0} file{(copied == 1 ? "" : "s")} ({Size(bytes)}){(same > 0 ? $"; {same:N0} were already there" : "")}.";
        return failed > 0 ? outcome + $" {failed} couldn't be read (open in another app?)." : outcome;
    }

    /// <summary>Copies files (and folders) into a folder, never over an existing file.</summary>
    public static (string Outcome, List<string> Made) Copy(IReadOnlyList<string> paths, string folder)
    {
        Directory.CreateDirectory(folder);
        var made = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var to = Free(Path.Combine(folder, Path.GetFileName(path)), []);
                File.Copy(path, to);
                made.Add(to);
            }
            else if (Directory.Exists(path))
            {
                var to = Path.Combine(folder, Path.GetFileName(path.TrimEnd('\\')));
                for (int i = 2; Directory.Exists(to); i++) to = Path.Combine(folder, $"{Path.GetFileName(path.TrimEnd('\\'))} ({i})");
                foreach (var file in Walk(path))
                {
                    var target = Path.Combine(to, Path.GetRelativePath(path, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
                made.Add(to);
            }
        }
        Ai.AiLog.Write("copied", $"{made.Count} to {folder}");
        return ($"Copied {made.Count} item{(made.Count == 1 ? "" : "s")} to {Path.GetFileName(folder.TrimEnd('\\'))}.", made);
    }
}
