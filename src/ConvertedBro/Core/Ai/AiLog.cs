using System;
using System.IO;

namespace ConvertedBro.Core.Ai;

/// <summary>
/// Everything the cat was asked and did, in plain words: what was said, what it searched,
/// which plans it offered, and what ran. Never the API key.
/// </summary>
public static class AiLog
{
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(Log.DataDir, "cat-log.txt");

    public static void Write(string what, string detail)
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 2_000_000) File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {what,-9} {detail.Replace(Environment.NewLine, " / ").Replace("\n", " / ")}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not write the cat's log", ex);
        }
    }
}
