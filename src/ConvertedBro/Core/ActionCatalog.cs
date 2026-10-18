using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConvertedBro.Core;

public delegate Task<IReadOnlyList<string>> ActionRunner(IReadOnlyList<string> inputs, IProgress<double> progress, CancellationToken ct);

/// <summary>Which page of the wheel an action sits on.</summary>
public enum ActionGroup { Format, Tool, Size }

/// <summary>One thing the bro can do to files, e.g. "to MP4".</summary>
public sealed class BroAction
{
    public required string Id { get; init; }
    /// <summary>Text in the Explorer menu, e.g. "to MP4".</summary>
    public required string Label { get; init; }
    /// <summary>Text on the wheel, e.g. "MP4" or "Compress".</summary>
    public required string Short { get; init; }
    public required string Hint { get; init; }
    public required FileKind Kind { get; init; }
    public ActionGroup Group { get; init; } = ActionGroup.Format;
    /// <summary>Segoe Fluent Icons glyph for tools; formats are text only.</summary>
    public string? Glyph { get; init; }
    /// <summary>Which source extensions this action is offered for.</summary>
    public required Func<string, bool> AcceptsExtension { get; init; }
    /// <summary>True when all files go into a single run (merge); false runs once per file.</summary>
    public bool Combine { get; init; }
    public int MinFiles { get; init; } = 1;
    /// <summary>Extra check on the whole selection before offering this on the wheel.</summary>
    public Func<IReadOnlyList<string>, bool>? OfferFor { get; init; }
    public required ActionRunner Run { get; init; }
    /// <summary>Opens an editor window first (the scanner) instead of starting a job straight away.</summary>
    public bool OpensEditor { get; init; }

    public bool Accepts(string path) => AcceptsExtension(Path.GetExtension(path));
}

public static class ActionCatalog
{
    // Declared before All: static fields initialise in order, and Build() reads this.
    static readonly (long Bytes, string Id)[] SizeLimits =
        [(100 * 1024, "100kb"), (200 * 1024, "200kb"), (1024 * 1024, "1mb"), (2 * 1024 * 1024, "2mb")];

    public static IReadOnlyList<BroAction> All { get; } = Build();

    public static BroAction? Find(string id) => All.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The kind most of the selection is; its actions are the ones offered.</summary>
    public static FileKind DominantKind(IReadOnlyList<string> files) =>
        files.GroupBy(FileKinds.Of).Where(g => g.Key != FileKind.Unknown)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    /// <summary>Actions that make sense for a dropped/selected set of files, in catalog order.</summary>
    public static IReadOnlyList<BroAction> For(IReadOnlyList<string> files)
    {
        var kind = DominantKind(files);
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
        ImageTo(".jpg", "JPG", "Small photo that opens anywhere", ".jpg", ".jpeg", ".jfif");
        ImageTo(".webp", "WEBP", "Modern web image, tiny files", ".webp");
        ImageTo(".ico", "ICO", "Windows icon in every size", ".ico");
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
            Short = "Compress",
            Hint = "Same look, smaller file",
            Glyph = "îœ¿",
            Group = ActionGroup.Tool,
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico"),
            Run = PerFile(ImageTools.Compress),
        });
        list.Add(new BroAction
        {
            Id = "image-half",
            Label = "Resize to 50%",
            Short = "Half size",
            Hint = "Half the width and height",
            Glyph = "îœŸ",
            Group = ActionGroup.Tool,
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico"),
            Run = PerFile(input => ImageTools.Resize(input, 50)),
        });
        list.Add(new BroAction
        {
            Id = "image-rotate",
            Label = "Rotate right",
            Short = "Rotate",
            Hint = "Turn 90Â° clockwise",
            Glyph = "îž­",
            Group = ActionGroup.Tool,
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico"),
            Run = PerFile(ImageTools.RotateRight),
        });
        list.Add(new BroAction
        {
            Id = "image-strip",
            Label = "Remove metadata",
            Short = "Metadata",
            Hint = "Strip camera, date and location info",
            Glyph = "î£¬",
            Group = ActionGroup.Tool,
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico", ".gif"),
            Run = PerFile(ImageTools.StripMetadata),
        });

        list.Add(new BroAction
        {
            Id = "image-scan",
            Label = "Scan documentâ€¦",
            Short = "Scan",
            Hint = "Crop, straighten and clean up a photo of paper",
            Glyph = "\uE7A8",
            Group = ActionGroup.Tool,
            Kind = FileKind.Image,
            AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico", ".gif"),
            Combine = true,
            OpensEditor = true,
            Run = (_, _, _) => throw new InvalidOperationException("The scanner opens its own window."),
        });
        list.Add(CopyText(FileKind.Image, Kind(FileKind.Image, ".svg", ".ico")));

        // Size limits for forms and uploads, on their own page.
        foreach (var (bytes, id) in SizeLimits)
            list.Add(new BroAction
            {
                Id = $"image-under-{id}",
                Label = $"Shrink under {SizeTools.Label(bytes)}",
                Short = SizeTools.Label(bytes),
                Hint = $"Smallest change that fits under {SizeTools.Label(bytes)}",
                Group = ActionGroup.Size,
                Kind = FileKind.Image,
                AcceptsExtension = Kind(FileKind.Image, ".svg", ".ico", ".gif"),
                Run = PerFile(input => SizeTools.FitImage(input, bytes)),
            });

        // ---- Video ----
        void Video(string id, string label, string shortName, string hint, string ext, string[] args,
            string? suffix = null, Func<string, bool>? accepts = null, ActionGroup group = ActionGroup.Format, string? glyph = null) =>
            list.Add(new BroAction
            {
                Id = id,
                Label = label,
                Short = shortName,
                Hint = hint,
                Glyph = glyph,
                Group = group,
                Kind = FileKind.Video,
                AcceptsExtension = accepts ?? Kind(FileKind.Video),
                Run = PerFileAsync((input, progress, ct) =>
                {
                    var output = OutputPaths.Next(input, ext, suffix);
                    return (output, MediaTools.RunAsync(input, output, args, progress, ct));
                }),
            });

        Video("video-mp4", "to MP4", "MP4", "H.264, plays everywhere", ".mp4", MediaTools.ToMp4,
            accepts: ext => Kind(FileKind.Video, ".mp4")(ext) || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase));
        Video("video-webm", "to WEBM", "WEBM", "VP9 for the web", ".webm", MediaTools.ToWebm, accepts: Kind(FileKind.Video, ".webm"));
        Video("video-gif", "to GIF", "GIF", "Looping GIF, 480 px wide", ".gif", MediaTools.ToGif);
        Video("video-mp3", "Extract audio (MP3)", "MP3", "Just the sound", ".mp3", MediaTools.ToMp3);
        Video("video-compress", "Compress", "Compress", "Smaller MP4, up to 1080p", ".mp4", MediaTools.CompressVideo, "small",
            group: ActionGroup.Tool, glyph: "îœ¿");
        Video("video-half", "Resize to 50%", "Half size", "Half the width and height", ".mp4", MediaTools.HalfSize, "50%",
            group: ActionGroup.Tool, glyph: "îœŸ");
        Video("video-rotate", "Rotate right", "Rotate", "Turn 90Â° clockwise", ".mp4", MediaTools.RotateRight, "rotated",
            group: ActionGroup.Tool, glyph: "îž­");
        list.Add(new BroAction
        {
            Id = "video-mute",
            Label = "Remove audio",
            Short = "Mute",
            Hint = "Same video, no sound",
            Glyph = "î",
            Group = ActionGroup.Tool,
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
        AudioTo(".m4a", "M4A", "AAC, great on phones", MediaTools.ToM4a);
        AudioTo(".wav", "WAV", "Uncompressed, for editing", MediaTools.ToWav);
        AudioTo(".flac", "FLAC", "Lossless, smaller than WAV", MediaTools.ToFlac);
        AudioTo(".ogg", "OGG", "Open format, Vorbis", MediaTools.ToOgg);
        list.Add(new BroAction
        {
            Id = "audio-normalize",
            Label = "Even out volume",
            Short = "Loudness",
            Hint = "Quiet and loud parts end up evenly loud",
            Glyph = "î§",
            Group = ActionGroup.Tool,
            Kind = FileKind.Audio,
            AcceptsExtension = Kind(FileKind.Audio),
            Run = PerFileAsync((input, progress, ct) =>
            {
                var output = OutputPaths.Next(input, Path.GetExtension(input), "leveled");
                return (output, MediaTools.RunAsync(input, output, MediaTools.Normalize, progress, ct));
            }),
        });

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
            Hint = "Every page as a lighter image",
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
            Glyph = "î£†",
            Group = ActionGroup.Tool,
            Kind = FileKind.Pdf,
            AcceptsExtension = Kind(FileKind.Pdf),
            Run = PerFileMany(PdfTools.Split),
        });
        list.Add(new BroAction
        {
            Id = "pdf-merge",
            Label = "Merge",
            Short = "Merge",
            Hint = "Join PDFs and images in name order",
            Glyph = "îœ›",
            Group = ActionGroup.Tool,
            Kind = FileKind.Pdf,
            AcceptsExtension = ext => FileKinds.OfExtension(ext) is FileKind.Pdf or FileKind.Image,
            Combine = true,
            MinFiles = 2,
            OfferFor = files => files.Any(f => FileKinds.Of(f) == FileKind.Pdf),
            Run = (inputs, progress, ct) => Task.Run<IReadOnlyList<string>>(() => [PdfTools.Combine(inputs, progress)], ct),
        });

        list.Add(CopyText(FileKind.Pdf, Kind(FileKind.Pdf)));
        foreach (var (bytes, id) in SizeLimits)
            list.Add(new BroAction
            {
                Id = $"pdf-under-{id}",
                Label = $"Shrink under {SizeTools.Label(bytes)}",
                Short = SizeTools.Label(bytes),
                Hint = "Fits the limit; pages become images",
                Group = ActionGroup.Size,
                Kind = FileKind.Pdf,
                AcceptsExtension = Kind(FileKind.Pdf),
                Run = PerFileMany((input, progress) => [SizeTools.FitPdf(input, bytes, progress)]),
            });

        return list;
    }

    static BroAction CopyText(FileKind kind, Func<string, bool> accepts) => new()
    {
        Id = kind == FileKind.Pdf ? "pdf-text" : "image-text",
        Label = "Copy text",
        Short = "Copy text",
        Hint = "Reads the text and puts it on the clipboard",
        Glyph = "\uE8C8",
        Group = ActionGroup.Tool,
        Kind = kind,
        AcceptsExtension = accepts,
        Combine = true,
        Run = TextTools.CopyText,
    };

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
