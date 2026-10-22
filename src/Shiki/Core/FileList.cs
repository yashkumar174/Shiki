using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Shiki.Core;

public static class FileList
{
    const int MaxFolderFiles = 500;

    /// <summary>
    /// Turns dropped paths into files: folders contribute their direct children (to convert them),
    /// and the folder itself stays in the list too (to zip it whole).
    /// </summary>
    public static List<string> Expand(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    result.AddRange(Directory.EnumerateFiles(path).Where(f => FileKinds.Of(f) != FileKind.Unknown)
                        .OrderBy(f => f, NaturalComparer.Instance).Take(MaxFolderFiles));
                    result.Add(path);
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not read folder {path}", ex);
                }
            }
            else if (File.Exists(path))
            {
                result.Add(path);
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>What the person actually picked: folders, and files that aren't inside one of them.</summary>
    public static List<string> TopLevel(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        var folders = list.Where(Directory.Exists).Select(f => Path.GetFullPath(f).TrimEnd('\\') + "\\").ToList();
        return list.Where(p => Directory.Exists(p) || !folders.Any(f => Path.GetFullPath(p).StartsWith(f, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

/// <summary>Sorts like Explorer does: "page 2" before "page 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y) => StrCmpLogicalW(Path.GetFileName(x ?? ""), Path.GetFileName(y ?? ""));
}
