using System;
using System.IO;
using System.Linq;

namespace Shiki.Core;

/// <summary>
/// What's probably wanted with a new download, worked out on this PC (no AI): a HEIC photo that
/// won't open everywhere, a huge video, an archive to unpack. One suggestion at most, or none.
/// </summary>
public static class Suggestions
{
    public static (BroAction Action, string Why)? ForDownload(string path)
    {
        if (Habits.For(path) != null) return null; // done by itself already
        var ext = Path.GetExtension(path).ToLowerInvariant();
        long size;
        try { size = new FileInfo(path).Length; } catch (IOException) { return null; }
        (string Id, string Why)? pick = ext switch
        {
            ".heic" or ".heif" => ("image-jpg", "HEIC photos don't open everywhere. A JPG copy does."),
            ".avif" => ("image-jpg", "AVIF pictures don't open everywhere. A JPG copy does."),
            ".webp" when size > 0 => ("image-jpg", "WEBP pictures from the web don't open in every app. A JPG copy does."),
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".tgz" => ("archive-extract", "It's an archive: unpack it to see what's inside."),
            _ when FileKinds.Of(path) == FileKind.Video && size > 300L * 1024 * 1024 => ("video-compress", $"It's a big video ({FileChores.Size(size)}). A compressed copy is much smaller and looks the same."),
            _ when FileKinds.Of(path) == FileKind.Image && size > 10L * 1024 * 1024 => ("image-compress", $"It's a big picture ({FileChores.Size(size)}). A compressed copy is easier to send."),
            _ => null,
        };
        if (pick is not { } p || ActionCatalog.Find(p.Id) is not { } action) return null;
        if (!(action.Available?.Invoke() ?? true) || !action.Accepts(path)) return null;
        return (action, p.Why);
    }
}
