using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ConvertedBro.Core;

public static class FileList
{
    const int MaxFolderFiles = 500;

    /// <summary>Turns dropped paths into files: folders contribute their direct children.</summary>
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
}

/// <summary>Sorts like Explorer does: "page 2" before "page 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y) => StrCmpLogicalW(Path.GetFileName(x ?? ""), Path.GetFileName(y ?? ""));
}
