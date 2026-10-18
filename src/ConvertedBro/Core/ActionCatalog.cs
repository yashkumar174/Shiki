using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConvertedBro.Core;

public delegate Task<IReadOnlyList<string>> ActionRunner(IReadOnlyList<string> inputs, IProgress<double> progress, CancellationToken ct);

/// <summary>One thing the bro can do to files, e.g. "to MP4".</summary>
public sealed class BroAction
{
    public required string Id { get; init; }
    /// <summary>Text in the Explorer menu, e.g. "to MP4".</summary>
    public required string Label { get; init; }
    /// <summary>Text on the radial bubble, e.g. "MP4".</summary>
    public required string Short { get; init; }
    public required string Hint { get; init; }
    public required FileKind Kind { get; init; }
    /// <summary>Which source extensions this action is offered for.</summary>
    public required Func<string, bool> AcceptsExtension { get; init; }
    /// <summary>True when all files go into a single run (merge); false runs once per file.</summary>
    public bool Combine { get; init; }
    public int MinFiles { get; init; } = 1;
    /// <summary>Extra check on the whole selection before offering this in the radial menu.</summary>
    public Func<IReadOnlyList<string>, bool>? OfferFor { get; init; }
    public required ActionRunner Run { get; init; }

    public bool Accepts(string path) => AcceptsExtension(Path.GetExtension(path));
}

public static class ActionCatalog
{
    public static IReadOnlyList<BroAction> All { get; } = Build();

    public static BroAction? Find(string id) => All.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Actions that make sense for a dropped/selected set of files, best first.</summary>
    public static IReadOnlyList<BroAction> For(IReadOnlyList<string> files)
    {
        // Offer the actions of the most common kind in the selection.
        var kind = files.GroupBy(FileKinds.Of).Where(g => g.Key != FileKind.Unknown)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (kind == FileKind.Unknown) return [];

        return All.Where(a =>
        {
            var accepted = files.Where(a.Accepts).ToList();
            return accepted.Any(f => FileKinds.Of(f) == kind) && accepted.Count >= a.MinFiles && (a.OfferFor?.Invoke(files) ?? true);
        }).ToList();
    }

    /// <summary>Actions listed in the Explorer right-click menu for one extension.</summary>
    public static IReadOnlyList<BroAction> ForExtension(string ext) =>
        All.Where(a => a.MinFiles == 1 && a.AcceptsExtension(ext)).ToList();

    static List<BroAction> Build()
    {
        static Func<string, bool> Kind(FileKind kind, params string[] except) =>
            ext => FileKinds.OfExtension(ext) == kind && !except.Contains(ext, StringComparer.OrdinalIgnoreCase);

        var list = new List<BroAction>();

        // ---- Images ----
        void ImageTo(string ext, string name, string hint, params string[] alreadyThat) => list.Add(new BroAction
        {
            Id = $"image-{ext.TrimStart('.')}",
            Label = $"to {name}",
            Short = name,
            Hint = hint,
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, alreadyThat),
            Run = PerFile(input =>
            {
                var output = OutputPaths.Next(input, ext);
                ImageTools.Convert(input, output);
                return output;
            }),
        });

        ImageTo(".png", "PNG", "Lossless, keeps transparency", ".png");
        ImageTo(".jpg", "JPG", "Small photo, works everywhere", ".jpg", ".jpeg", ".jfif");
        ImageTo(".webp", "WEBP", "Modern web image, tiny files", ".webp");
        ImageTo(".ico", "ICO", "Windows icon, all sizes", ".ico");

        list.Add(new BroAction
        {
            Id = "image-pdf",
            Label = "to PDF",
            Short = "PDF",
            Hint = "All images into one PDF, A4 pages",
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image),
            Combine = true,
            Run = (inputs, progress, ct) => Task.Run<IReadOnlyList<string>>(() => [PdfTools.Combine(inputs, progress)], ct),
        });
        list.Add(new BroAction
        {
            Id = "image-compress",
            Label = "Compress",
            Short = "Smaller",
            Hint = "Same look, smaller file",
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico"),
            Run = PerFile(ImageTools.Compress),
        });
        list.Add(new BroAction
        {
            Id = "image-half",
            Label = "Resize to 50%",
            Short = "50%",
            Hint = "Half the width and height",
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico"),
            Run = PerFile(input => ImageTools.Resize(input, 50)),
        });

        // ---- Video ----
        void VideoTo(string id, string label, string shortName, string hint, string ext, string[] args,
            string? suffix = null, Func<string, bool>? accepts = null) => list.Add(new BroAction
        {
            Id = id,
            Label = label,
            Short = shortName,
            Hint = hint,
            Kind = FileKind.Video,
            AcceptsExtension = accepts ?? Kind(FileKind.Video),
            Run = PerFileAsync((input, progress, ct) =>
            {
                var output = OutputPaths.Next(input, ext, suffix);
                return (output, MediaTools.RunAsync(input, output, args, progress, ct));
            }),
        });

        VideoTo("video-mp4", "to MP4", "MP4", "H.264, plays everywhere", ".mp4", MediaTools.ToMp4,
            accepts: ext => Kind(FileKind.Video, ".mp4")(ext) || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase));
        VideoTo("video-webm", "to WEBM", "WEBM", "VP9 for the web", ".webm", MediaTools.ToWebm, accepts: Kind(FileKind.Video, ".webm"));
        VideoTo("video-gif", "to GIF", "GIF", "Looping GIF, 480px wide", ".gif", MediaTools.ToGif);
        VideoTo("video-mp3", "Extract audio (MP3)", "MP3", "Just the sound", ".mp3", MediaTools.ToMp3);
        VideoTo("video-compress", "Compress", "Smaller", "Smaller MP4, max 1080p", ".mp4", MediaTools.CompressVideo, "small");
        list.Add(new BroAction
        {
            Id = "video-mute",
            Label = "Remove audio",
            Short = "Mute",
            Hint = "Same video, no sound",
            Kind = FileKind.Video,
            AcceptsExtension = Kind(FileKind.Video),
            Run = PerFileAsync((input, progress, ct) =>
            {
                var output = OutputPaths.Next(input, Path.GetExtension(input), "muted");
                return (output, MediaTools.RunAsync(input, output, MediaTools.Mute, progress, ct));
            }),
        });

        // ---- Audio ----
        void AudioTo(string ext, string name, string hint, string[] args) => list.Add(new BroAction
        {
            Id = $"audio-{ext.TrimStart('.')}",
            Label = $"to {name}",
            Short = name,
            Hint = hint,
            Kind = FileKind.Audio,
            AcceptsExtension = Kind(FileKind.Audio, ext),
            Run = PerFileAsync((input, progress, ct) =>
            {
                var output = OutputPaths.Next(input, ext);
                return (output, MediaTools.RunAsync(input, output, args, progress, ct));
            }),
        });

        AudioTo(".mp3", "MP3", "Plays on anything", MediaTools.ToMp3);
        AudioTo(".m4a", "M4A", "AAC, great for phones", MediaTools.ToM4a);
        AudioTo(".wav", "WAV", "Uncompressed, for editing", MediaTools.ToWav);
        AudioTo(".flac", "FLAC", "Lossless, smaller than WAV", MediaTools.ToFlac);
        AudioTo(".ogg", "OGG", "Open format, Vorbis", MediaTools.ToOgg);

        // ---- PDF ----
        list.Add(new BroAction
        {
            Id = "pdf-png",
            Label = "Pages to PNG",
            Short = "PNG",
            Hint = "Every page as a sharp image",
            Kind = FileKind.Pdf,
            AcceptsExtension = Kind(FileKind.Pdf),
            Run = PerFileMany((input, progress) => PdfTools.ToImages(input, ".png", progress)),
        });
        list.Add(new BroAction
        {
            Id = "pdf-jpg",
            Label = "Pages to JPG",
            Short = "JPG",
            Hint = "Every page as a photo-sized image",
            Kind = FileKind.Pdf,
            AcceptsExtension = Kind(FileKind.Pdf),
            Run = PerFileMany((input, progress) => PdfTools.ToImages(input, ".jpg", progress)),
        });
        list.Add(new BroAction
        {
            Id = "pdf-split",
            Label = "Split into pages",
            Short = "Split",
            Hint = "One PDF per page",
            Kind = FileKind.Pdf,
            AcceptsExtension = Kind(FileKind.Pdf),
            Run = PerFileMany(PdfTools.Split),
        });
        list.Add(new BroAction
        {
            Id = "pdf-merge",
            Label = "Merge",
            Short = "Merge",
            Hint = "Join PDFs (and images) in order",
            Kind = FileKind.Pdf,
            AcceptsExtension = ext => FileKinds.OfExtension(ext) is FileKind.Pdf or FileKind.Image,
            Combine = true,
            MinFiles = 2,
            OfferFor = files => files.Any(f => FileKinds.Of(f) == FileKind.Pdf),
            Run = (inputs, progress, ct) => Task.Run<IReadOnlyList<string>>(() => [PdfTools.Combine(inputs, progress)], ct),
        });

        return list;
    }

    static ActionRunner PerFile(Func<string, string> convert) =>
        PerFileMany((input, _) => [convert(input)]);

    static ActionRunner PerFileMany(Func<string, IProgress<double>, IReadOnlyList<string>> convert) =>
        (inputs, progress, ct) => Task.Run(() =>
        {
            var outputs = new List<string>();
            for (int i = 0; i < inputs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                int index = i;
                var fileProgress = new SyncProgress(p => progress.Report((index + p) / inputs.Count));
                outputs.AddRange(convert(inputs[i], fileProgress));
                progress.Report((i + 1) / (double)inputs.Count);
            }
            return (IReadOnlyList<string>)outputs;
        }, ct);

    static ActionRunner PerFileAsync(Func<string, IProgress<double>, CancellationToken, (string output, Task work)> convert) =>
        async (inputs, progress, ct) =>
        {
            var outputs = new List<string>();
            for (int i = 0; i < inputs.Count; i++)
            {
                int index = i;
                var fileProgress = new SyncProgress(p => progress.Report((index + p) / inputs.Count));
                var (output, work) = convert(inputs[i], fileProgress, ct);
                await work;
                outputs.Add(output);
            }
            return outputs;
        };

    /// <summary>Progress that reports on the calling thread instead of capturing a context.</summary>
    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
