using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Shiki.Core.Voice;

/// <summary>
/// The last 30 things said to the cat, kept as sound on this PC (with what was heard), so the
/// speech engines can be compared on the person's own voice. Never sent anywhere by itself;
/// a setting turns it off, and the folder can simply be deleted.
/// </summary>
public static class VoiceClips
{
    public const int Most = 30;
    public static string Folder => Path.Combine(Log.DataDir, "voice-clips");

    /// <summary>Set from the setting.</summary>
    public static bool On { get; set; } = true;

    /// <summary>Keeps a clip (16 kHz mono) and what was heard, then trims to the newest 30.</summary>
    public static void Keep(float[] samples, string heard)
    {
        if (!On || samples.Length < 16000 / 2) return;
        var copy = samples.ToArray();
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var name = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                File.WriteAllBytes(name + ".wav", Deepgram.Wav(copy));
                File.WriteAllText(name + ".txt", heard);
                foreach (var old in Directory.GetFiles(Folder, "*.wav").OrderByDescending(f => f).Skip(Most))
                {
                    File.Delete(old);
                    File.Delete(Path.ChangeExtension(old, ".txt"));
                }
            }
            catch (IOException ex)
            {
                Log.Error("Could not keep the voice clip", ex);
            }
        });
    }

    /// <summary>The kept clips, newest first: the sound file and what was heard live.</summary>
    public static List<(string Wav, string Heard)> All() =>
        Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.wav").OrderByDescending(f => f)
                .Select(f => (f, File.Exists(Path.ChangeExtension(f, ".txt")) ? File.ReadAllText(Path.ChangeExtension(f, ".txt")) : "")).ToList()
            : [];

    /// <summary>The samples of a kept clip (16 kHz mono, 16-bit WAV as written by <see cref="Keep"/>).</summary>
    public static float[] Read(string wav)
    {
        var bytes = File.ReadAllBytes(wav);
        int count = (bytes.Length - 44) / 2;
        var samples = new float[Math.Max(0, count)];
        for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f;
        return samples;
    }
}
