using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Shiki.Core;

public sealed record ContentHit(string Path, string Snippet, DateTime Modified);

/// <summary>
/// A private search index of what's inside the person's documents (PDFs, Word, PowerPoint, Excel,
/// text), kept in one file on this PC (index.db) and never sent anywhere. It fills itself in the
/// background, a few files at a time, and follows changes in the folders it covers.
/// </summary>
public sealed class ContentIndex : IDisposable
{
    static string DbPath => Path.Combine(Log.DataDir, "index.db");
    const long MaxFileBytes = 60L * 1024 * 1024;

    readonly object _gate = new();
    readonly SqliteConnection _db;
    readonly List<FileSystemWatcher> _watchers = [];
    readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? _work;
    Timer? _settle;

    /// <summary>Documents in the index, and whether it's going through the folders now.</summary>
    public int Count { get; private set; }
    public bool Indexing { get; private set; }
    public event Action? Changed;

    public ContentIndex()
    {
        _db = new SqliteConnection($"Data Source={DbPath}");
        _db.Open();
        Exec("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS files (path TEXT PRIMARY KEY, modified INTEGER, size INTEGER);
            CREATE VIRTUAL TABLE IF NOT EXISTS texts USING fts5(path UNINDEXED, name, body, tokenize = 'unicode61 remove_diacritics 2');
            """);
        Count = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM files"));
    }

    /// <summary>The folders covered: the person's usual ones.</summary>
    public static IReadOnlyList<string> Folders => new[] { "documents", "desktop", "downloads" }
        .Select(p => Ai.FileFinder.PlacePath(p, null)).Where(p => p != null && Directory.Exists(p)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    static bool Wanted(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!Documents.Readable.Contains(ext) || ext is ".json" or ".log" or ".xml") return false;
        var name = Path.GetFileName(path);
        return !name.StartsWith("~$") && !name.StartsWith('.');
    }

    /// <summary>Goes through the folders (only new and changed files are read), then follows changes.</summary>
    public void Start()
    {
        Stop();
        var work = _work = new CancellationTokenSource();
        foreach (var folder in Folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                watcher.Created += (_, e) => Touched(e.FullPath);
                watcher.Changed += (_, e) => Touched(e.FullPath);
                watcher.Deleted += (_, e) => Touched(e.FullPath);
                watcher.Renamed += (_, e) => { Touched(e.OldFullPath); Touched(e.FullPath); };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Log.Error($"Can't follow changes in {folder}", ex);
            }
        }
        // Low priority, after the PC has settled: it reads a lot of files the first time.
        var thread = new Thread(() => Sweep(work.Token)) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "Content index" };
        thread.Start();
    }

    public void Stop()
    {
        _work?.Cancel();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    void Touched(string path)
    {
        if (!Wanted(path)) return;
        lock (_changed) _changed.Add(path);
        // Wait for a quiet moment: a file being saved fires several times.
        _settle ??= new Timer(_ => Catch());
        _settle.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
    }

    void Catch()
    {
        List<string> paths;
        lock (_changed)
        {
            paths = _changed.ToList();
            _changed.Clear();
        }
        foreach (var path in paths)
        {
            if (_work?.IsCancellationRequested ?? true) return;
            if (File.Exists(path)) Add(path);
            else Forget(path);
        }
        Changed?.Invoke();
    }

    void Sweep(CancellationToken ct)
    {
        try
        {
            Thread.Sleep(TimeSpan.FromSeconds(20));
            Indexing = true;
            Changed?.Invoke();
            var known = new Dictionary<string, (long Modified, long Size)>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT path, modified, size FROM files";
                using var r = cmd.ExecuteReader();
                while (r.Read()) known[r.GetString(0)] = (r.GetInt64(1), r.GetInt64(2));
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in Folders)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var file in files)
                {
                    if (ct.IsCancellationRequested) return;
                    if (!Wanted(file)) continue;
                    seen.Add(file);
                    FileInfo info;
                    try { info = new FileInfo(file); } catch (Exception) { continue; }
                    if (known.TryGetValue(file, out var was) && was.Modified == info.LastWriteTimeUtc.Ticks && was.Size == info.Length) continue;
                    Add(file);
                    // Gentle on the PC: a short rest between files.
                    Thread.Sleep(30);
                }
            }
            // Files that are gone.
            foreach (var gone in known.Keys.Where(k => !seen.Contains(k) && !File.Exists(k)).ToList()) Forget(gone);
            Ai.AiLog.Write("indexed", $"{Count} documents");
        }
        catch (Exception ex)
        {
            Log.Error("The content index stopped", ex);
        }
        finally
        {
            Indexing = false;
            Changed?.Invoke();
        }
    }

    void Add(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return;
            string text;
            try
            {
                var contents = Documents.Read(path, 200_000);
                // A scan has no words in it; the name still counts.
                text = contents.ScannedPdf != null ? "" : Regex.Replace(contents.Text, @"--- (Page|Slide) \d+ ---", " ");
            }
            catch (Exception)
            {
                // Locked, damaged or password protected: the name alone.
                text = "";
            }
            lock (_gate)
            {
                Exec("DELETE FROM texts WHERE path = $p", ("$p", path));
                Exec("INSERT INTO texts (path, name, body) VALUES ($p, $n, $b)", ("$p", path), ("$n", Path.GetFileNameWithoutExtension(path)), ("$b", text));
                Exec("INSERT OR REPLACE INTO files (path, modified, size) VALUES ($p, $m, $s)", ("$p", path), ("$m", info.LastWriteTimeUtc.Ticks), ("$s", info.Length));
                Count = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM files"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            Log.Error($"Couldn't index {path}", ex);
        }
    }

    void Forget(string path)
    {
        lock (_gate)
        {
            Exec("DELETE FROM texts WHERE path = $p", ("$p", path));
            Exec("DELETE FROM files WHERE path = $p", ("$p", path));
            Count = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM files"));
        }
    }

    /// <summary>Throws the index away and builds it again.</summary>
    public void Rebuild()
    {
        lock (_gate)
        {
            Exec("DELETE FROM texts");
            Exec("DELETE FROM files");
            Count = 0;
        }
        Start();
    }

    /// <summary>
    /// Documents containing all the words (or exact phrases in quotes), best first, with a bit of
    /// text around where they matched. Numbers like 28,990 match however they're written.
    /// </summary>
    public List<ContentHit> Search(string query, int max = 10)
    {
        var match = MatchExpression(query);
        if (match.Length == 0) return [];
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT t.path, snippet(texts, 2, '[', ']', '…', 16), f.modified
                FROM texts t JOIN files f ON f.path = t.path
                WHERE texts MATCH $q ORDER BY bm25(texts, 0, 4.0, 1.0) LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$q", match);
            cmd.Parameters.AddWithValue("$n", max);
            var hits = new List<ContentHit>();
            try
            {
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var path = r.GetString(0);
                    if (!File.Exists(path)) continue;
                    hits.Add(new ContentHit(path, r.IsDBNull(1) ? "" : r.GetString(1), new DateTime(r.GetInt64(2), DateTimeKind.Utc).ToLocalTime()));
                }
            }
            catch (SqliteException ex)
            {
                Log.Error($"Index search \"{query}\" failed", ex);
            }
            return hits;
        }
    }

    /// <summary>The words as an FTS5 query: every word (or "quoted phrase") must appear; 28,990 is the phrase "28 990".</summary>
    static string MatchExpression(string query)
    {
        var parts = new List<string>();
        foreach (Match m in Regex.Matches(query, "\"([^\"]+)\"|(\\S+)"))
        {
            var piece = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var words = Regex.Matches(piece, @"[\p{L}\p{N}]+").Select(w => w.Value).ToList();
            if (words.Count == 0) continue;
            parts.Add("\"" + string.Join(" ", words) + "\"");
        }
        return string.Join(" AND ", parts);
    }

    void Exec(string sql, params (string Name, object Value)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    object Scalar(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() ?? 0;
    }

    public void Dispose()
    {
        Stop();
        _settle?.Dispose();
        lock (_gate) _db.Dispose();
    }
}
