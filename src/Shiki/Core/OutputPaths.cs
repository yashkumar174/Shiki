using System;
using System.IO;

namespace Shiki.Core;

/// <summary>
/// Picks where results go, never overwriting anything: next to the original, or all in one
/// folder of the person's choosing (optionally sorted into Images, Videos, PDFs…).
/// </summary>
public static class OutputPaths
{
    /// <summary>Where results go; null keeps them next to the original. Set from settings at startup.</summary>
    public static string? Folder { get; set; }

    /// <summary>With a folder: put each result in a subfolder for its kind.</summary>
    public static bool SortByType { get; set; }

    /// <summary>The suggested folder: in the user's home, not under OneDrive, so big videos don't get synced.</summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Shiki");

    /// <param name="suffix">Optional tag added to the name, e.g. "small" gives "photo (small).jpg".</param>
    public static string Next(string input, string newExtension, string? suffix = null)
    {
        var dir = DirectoryFor(input, TypeFolder(newExtension));
        var stem = Path.GetFileNameWithoutExtension(input);
        if (suffix != null) stem = $"{stem} ({suffix})";
        return Unique(dir, stem, newExtension);
    }

    /// <summary>A new folder for many results from one file (PDF pages, say).</summary>
    public static string NextFolder(string input, string suffix, string kindFolder = "Images")
    {
        var baseName = $"{Path.GetFileNameWithoutExtension(input)} ({suffix})";
        return UniqueFolder(DirectoryFor(input, kindFolder), baseName);
    }

    /// <summary>Where an archive's contents are unpacked.</summary>
    public static string ExtractDirectory(string archive) => DirectoryFor(Path.GetFullPath(archive), "Extracted");

    /// <summary>
    /// Where things the app makes from scratch go (snips, pasted pictures, QR codes): the results
    /// folder when there is one, otherwise Pictures\Shiki.
    /// </summary>
    public static string CreatedFolder
    {
        get
        {
            var dir = Folder == null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shiki")
                : SortByType ? Path.Combine(Folder, "Images") : Folder;
            return Directory.CreateDirectory(dir).FullName;
        }
    }

    /// <summary>Screen recordings: the results folder (its Videos part when sorting), else Videos\Shiki.</summary>
    public static string RecordingsFolder
    {
        get
        {
            var dir = Folder == null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Shiki")
                : SortByType ? Path.Combine(Folder, "Videos") : Folder;
            return Directory.CreateDirectory(dir).FullName;
        }
    }

    public static string UniqueFolder(string dir, string name)
    {
        var candidate = Path.Combine(dir, name);
        for (int i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{name} ({i})");
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    static string DirectoryFor(string input, string kindFolder)
    {
        if (Folder == null) return Path.GetDirectoryName(Path.GetFullPath(input))!;
        var dir = SortByType ? Path.Combine(Folder, kindFolder) : Folder;
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string TypeFolder(string extension) => FileKinds.OfExtension(extension) switch
    {
        FileKind.Image => "Images",
        FileKind.Video => "Videos",
        FileKind.Audio => "Audio",
        FileKind.Pdf => "PDFs",
        FileKind.Archive => "Archives",
        _ => "Other",
    };

    static string Unique(string dir, string stem, string ext)
    {
        var candidate = Path.Combine(dir, stem + ext);
        for (int i = 1; File.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
        return candidate;
    }
}
