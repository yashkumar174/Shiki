using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConvertedBro.UI;

public enum CatPose { Idle, LookLeft, LookRight, Blink, Pop, Happy, TapLeft, TapRight }
public enum PeekPose { Idle, LeftPaw, RightPaw }

/// <summary>A cat's colours and markings. All cats share one body shape.</summary>
public sealed class CatSkin
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>
    /// k outline, b fur, w chest, f paws, p patches/ears/tail, e ear inside and toe beans,
    /// E eyes, W eye shine, P nose, s soft shade, B second patch colour, m/M open mouth.
    /// </summary>
    public required Dictionary<char, Color> Palette { get; init; }
    public (int Row, int Col, string Pixels)[] Markings { get; init; } = [];
}

/// <summary>
/// Pixel-art cats, drawn as text grids (one character per pixel) and turned into tiny bitmaps.
/// Poses are small patches laid over the base drawing. Bitmaps are 1 px per art pixel;
/// the UI scales them up with nearest-neighbour sampling so they stay crisp.
/// </summary>
public static class CatSprites
{
    public const int Width = 20, Height = 20, PeekWidth = 26, PeekHeight = 7;

    static readonly string[] Body =
    [
        "..k..............k..",
        ".kpk............kpk.",
        ".kepk..........kpek.",
        ".keepkkkkkkkkkkpeek.",
        "kbepbbbbbbbbbbbbpebk",
        "kbbbbbbbbbbbbbbbbbbk",
        "kbbbbbbbbbbbbbbbbbbk",
        "kbbbEWbbbbbbbbEWbbbk",
        "kbbbEEbbbbbbbbEEbbbk",
        "kbbebbbbbPPbbbbbebbk",
        "kbbbbbbbkbbkbbbbbbbk",
        "kbbbbbbbbkkbbbbbbbbk",
        ".kbbbbbbbbbbbbbbbbk.",
        "..kkbbbbbbbbbbbbkk..",
        "...kbbbwwwwwwbbbk.k.",
        "...kbbwwwwwwwwbbkkpk",
        "...kbbwwwwwwwwbbkpk.",
        "...kbbbwwwwwwbbbkpk.",
        "...kfffkbbbbkfffkk..",
        "...kkkkkkkkkkkkkk...",
    ];

    // Patches are (row, column, pixels); '.' leaves the pixel underneath alone.
    static readonly Dictionary<CatPose, (int, int, string)[]> Poses = new()
    {
        [CatPose.Idle] = [],
        [CatPose.Blink] = [(7, 4, "bb"), (8, 4, "kk"), (7, 14, "bb"), (8, 14, "kk")],
        [CatPose.LookLeft] = [(7, 3, "EWb"), (8, 3, "EEb"), (7, 13, "EWb"), (8, 13, "EEb")],
        [CatPose.LookRight] = [(7, 4, "bEW"), (8, 4, "bEE"), (7, 14, "bEW"), (8, 14, "bEE")],
        [CatPose.Pop] = [(10, 7, ".kkkk."), (11, 7, "kmmmmk"), (12, 7, "kmMMmk"), (13, 7, ".kkkk.")],
        [CatPose.Happy] =
        [
            (7, 3, "bkb"), (8, 3, "kbk"), (7, 13, "bkb"), (8, 13, "kbk"),
            (10, 7, ".kkkk."), (11, 7, "kmmmmk"), (12, 7, "kmMMmk"), (13, 7, ".kkkk."),
        ],
        // Bongo paws while working; the eyes follow each tap so it reads at a small size.
        [CatPose.TapLeft] = [(17, 4, "fff"), (18, 4, "kkk"), (7, 3, "EWb"), (8, 3, "EEb"), (7, 13, "EWb"), (8, 13, "EEb")],
        [CatPose.TapRight] = [(17, 13, "fff"), (18, 13, "kkk"), (7, 4, "bEW"), (8, 4, "bEE"), (7, 14, "bEW"), (8, 14, "bEE")],
    };

    static readonly string[] Paw = [".kkkk.", "kffffk", "kfefek", "kffffk"];

    public static IReadOnlyList<CatSkin> Skins { get; } =
    [
        new CatSkin
        {
            Id = "mochi",
            Name = "Mochi",
            Description = "Calico, loud when excited",
            Palette = Colors(k: 0x1B1525, b: 0xFFFFFF, w: 0xFFFFFF, f: 0xFFFFFF, p: 0xF0913A, e: 0xFFB3C7,
                E: 0x1B1525, W: 0x1B1525, P: 0xFF8FAB, s: 0xEDE7F2, B: 0x3A3040),
            Markings =
            [
                (4, 4, "ppp"), (5, 1, "ppppp"), (6, 1, "pppp"), (7, 1, "pp"),
                (4, 13, "BBB"), (5, 14, "BB"),
                (15, 14, "pp"), (16, 14, "pp"), (17, 13, "pp"),
            ],
        },
        new CatSkin
        {
            Id = "latte",
            Name = "Latte",
            Description = "Siamese, blue eyes, brown socks",
            Palette = Colors(k: 0x2A1A14, b: 0xF6E7D3, w: 0xFFF6EA, f: 0x6B4532, p: 0x6B4532, e: 0xE8A9A0,
                E: 0x2F6FD6, W: 0x8EC2FF, P: 0xC97B78, s: 0xE9D3B8, B: 0xD9B593),
            Markings =
            [
                (8, 8, "BBBB"), (9, 7, "BB..BB"), (10, 7, "B.BB.B"), (11, 8, "B..B"),
                (5, 1, "s"), (6, 1, "s"), (5, 18, "s"), (6, 18, "s"),
            ],
        },
        new CatSkin
        {
            Id = "sky",
            Name = "Sky",
            Description = "Snow white with pale blue",
            Palette = Colors(k: 0x1B2A44, b: 0xFFFFFF, w: 0xFFFFFF, f: 0xFFFFFF, p: 0xA9CBF2, e: 0xFFB3C7,
                E: 0x1B2A44, W: 0x1B2A44, P: 0xFF9FB6, s: 0xCFE2FA, B: 0xA9CBF2),
            Markings =
            [
                (4, 8, "pppp"), (5, 9, "pp"),
                (5, 1, "s"), (6, 1, "s"), (7, 1, "s"), (8, 1, "s"), (5, 18, "s"), (6, 18, "s"), (7, 18, "s"), (8, 18, "s"),
                (12, 2, "ssssssssssssssss"), (17, 4, "ss"), (17, 14, "ss"),
            ],
        },
    ];

    public static CatSkin Skin(string? id) => Skins.FirstOrDefault(s => s.Id == id) ?? Skins[0];

    static readonly Dictionary<string, BitmapSource> Cache = [];

    public static BitmapSource Frame(CatSkin skin, CatPose pose) => Cached($"{skin.Id}/{pose}", () =>
    {
        var grid = Body.Select(r => r.ToCharArray()).ToArray();
        Apply(grid, skin.Markings);
        Apply(grid, Poses[pose]);
        return Render(grid, skin);
    });

    /// <summary>
    /// Hiding pose: the top of the head with its ears, and two paws gripping the screen edge in
    /// front of it. The eyes stay below the edge.
    /// </summary>
    public static BitmapSource Peek(CatSkin skin, PeekPose pose) => Cached($"{skin.Id}/peek/{pose}", () =>
    {
        var full = Body.Select(r => r.ToCharArray()).ToArray();
        Apply(full, skin.Markings);
        var grid = Enumerable.Range(0, PeekHeight).Select(_ => new string('.', PeekWidth).ToCharArray()).ToArray();
        for (int r = 0; r < PeekHeight; r++)
            for (int c = 0; c < Width; c++)
                if (full[r][c] != '.') grid[r][c + 3] = full[r][c];
        Blit(grid, Paw, 0, 3 - (pose == PeekPose.LeftPaw ? 1 : 0));
        Blit(grid, Paw, 20, 3 - (pose == PeekPose.RightPaw ? 1 : 0));
        return Render(grid, skin);
    });

    /// <summary>The head on a square canvas, for icons and the cat picker.</summary>
    public static BitmapSource Head(CatSkin skin) => Cached($"{skin.Id}/head", () =>
    {
        var full = Body.Select(r => r.ToCharArray()).ToArray();
        Apply(full, skin.Markings);
        var grid = Enumerable.Range(0, Width).Select(_ => new string('.', Width).ToCharArray()).ToArray();
        for (int r = 0; r <= 12; r++) grid[r + 4] = full[r];
        return Render(grid, skin);
    });

    /// <summary>Dev aid: writes every frame of every cat as PNG (1 px per art pixel).</summary>
    public static void Export(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        void Save(BitmapSource bitmap, string name)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png"));
            encoder.Save(file);
        }
        foreach (var skin in Skins)
        {
            foreach (var pose in Enum.GetValues<CatPose>()) Save(Frame(skin, pose), $"{skin.Id}-{pose}");
            foreach (var pose in Enum.GetValues<PeekPose>()) Save(Peek(skin, pose), $"{skin.Id}-peek-{pose}");
            Save(Head(skin), $"{skin.Id}-head");
        }
    }

    static BitmapSource Cached(string key, Func<BitmapSource> make)
    {
        if (!Cache.TryGetValue(key, out var bitmap)) Cache[key] = bitmap = make();
        return bitmap;
    }

    static void Apply(char[][] grid, (int Row, int Col, string Pixels)[] patches)
    {
        foreach (var (row, col, pixels) in patches)
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i] != '.') grid[row][col + i] = pixels[i];
    }

    static void Blit(char[][] grid, string[] art, int col, int topRow)
    {
        for (int r = 0; r < art.Length; r++)
        {
            int row = topRow + r;
            if (row < 0 || row >= grid.Length) continue;
            for (int c = 0; c < art[r].Length; c++)
                if (art[r][c] != '.') grid[row][col + c] = art[r][c];
        }
    }

    static BitmapSource Render(char[][] grid, CatSkin skin)
    {
        int h = grid.Length, w = grid[0].Length;
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char ch = grid[y][x];
                if (ch == '.' || !skin.Palette.TryGetValue(ch, out var color)) continue;
                int i = (y * w + x) * 4;
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bitmap.Freeze();
        return bitmap;
    }

    static Dictionary<char, Color> Colors(int k, int b, int w, int f, int p, int e, int E, int W, int P, int s, int B)
    {
        static Color C(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return new()
        {
            ['k'] = C(k), ['K'] = C(k), ['b'] = C(b), ['w'] = C(w), ['f'] = C(f), ['p'] = C(p), ['e'] = C(e),
            ['E'] = C(E), ['W'] = C(W), ['P'] = C(P), ['s'] = C(s), ['B'] = C(B),
            ['m'] = C(0xC8283A), ['M'] = C(0xF26B7A),
        };
    }
}
