using System.IO;

namespace ConvertedBro.Core;

/// <summary>Picks where converted files go: next to the source, never overwriting anything.</summary>
public static class OutputPaths
{
    /// <param name="suffix">Optional tag added to the name, e.g. "small" gives "photo (small).jpg".</param>
    public static string Next(string input, string newExtension, string? suffix = null)
    {
        var dir = Path.GetDirectoryName(input)!;
        var stem = Path.GetFileNameWithoutExtension(input);
        if (suffix != null) stem = $"{stem} ({suffix})";
        return Unique(dir, stem, newExtension);
    }

    public static string NextFolder(string input, string suffix)
    {
        var dir = Path.GetDirectoryName(input)!;
        var baseName = $"{Path.GetFileNameWithoutExtension(input)} ({suffix})";
        var candidate = Path.Combine(dir, baseName);
        for (int i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{baseName} {i}");
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    static string Unique(string dir, string stem, string ext)
    {
        var candidate = Path.Combine(dir, stem + ext);
        for (int i = 1; File.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
        return candidate;
    }
}
