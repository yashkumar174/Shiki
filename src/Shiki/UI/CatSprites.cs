using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiki.UI;

public enum CatPose { Idle, LookLeft, LookRight, Blink, Pop, Happy, TapLeft, TapRight, Yawn, Startled, StrainLeft, StrainRight, Purr, Grumpy, GrumpyFlick, Listen, PointLeft, PointRight, Talk }
public enum PeekPose { Idle, LeftPaw, RightPaw, Sleep }

/// <summary>Little sprites that float up from the cat: sleep, love, surprise, effort, joy.</summary>
public enum Floater { Z, Heart, Alert, Sweat, Sparkle, Battery, Bolt, Download }

/// <summary>Something the cat can wear, unlocked as it levels up.</summary>
public sealed record Accessory(string Id, string Name, (int Row, int Col, string Pixels)[] Patches);

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

    // Patches are (row, column, pixels); '.' leaves the pixel underneath alone, 'x' clears it.
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
        // Waking up: eyes shut, a big yawn.
        [CatPose.Yawn] = [(7, 4, "bb"), (8, 4, "kk"), (7, 14, "bb"), (8, 14, "kk"), (10, 7, ".kkkk."), (11, 7, "kmmmmk"), (12, 7, "kmMMmk"), (13, 7, ".kkkk.")],
        // A huge file: wide eyes and a little "o".
        [CatPose.Startled] = [(6, 3, "EEE"), (7, 3, "EWE"), (8, 3, "EEE"), (6, 13, "EEE"), (7, 13, "EWE"), (8, 13, "EEE"), (11, 9, "kk"), (12, 8, "kmmk"), (13, 9, "kk")],
        // A long job: eyes squeezed into > <, paws drumming hard.
        [CatPose.StrainLeft] = [(17, 4, "fff"), (18, 4, "kkk"), (7, 4, "Eb"), (8, 4, "bE"), (7, 14, "bE"), (8, 14, "Eb")],
        [CatPose.StrainRight] = [(17, 13, "fff"), (18, 13, "kkk"), (7, 4, "Eb"), (8, 4, "bE"), (7, 14, "bE"), (8, 14, "Eb")],
        // Being petted: happy closed eyes.
        [CatPose.Purr] = [(7, 3, "bkb"), (8, 3, "kbk"), (7, 13, "bkb"), (8, 13, "kbk")],
        // Petted too long: flat eyes, a frown, and the tail flicks.
        [CatPose.Grumpy] = [(7, 4, "bb"), (8, 4, "EE"), (7, 14, "bb"), (8, 14, "EE"), (10, 8, "bkkb"), (11, 8, "kbbk")],
        // Listening to you: wide, attentive eyes.
        [CatPose.Listen] = [(6, 3, "EEE"), (7, 3, "EWE"), (8, 3, "EEE"), (6, 13, "EEE"), (7, 13, "EWE"), (8, 13, "EEE")],
        // "There it is": a paw raised toward one side, eyes looking that way.
        [CatPose.PointLeft] = [(9, 1, "kkkk"), (10, 1, "kffk"), (11, 1, "kfek"), (12, 1, "kffk"), (13, 1, "kffk"), (7, 3, "EWb"), (8, 3, "EEb"), (7, 13, "EWb"), (8, 13, "EEb")],
        [CatPose.PointRight] = [(9, 15, "kkkk"), (10, 15, "kffk"), (11, 15, "kefk"), (12, 15, "kffk"), (13, 15, "kffk"), (7, 4, "bEW"), (8, 4, "bEE"), (7, 14, "bEW"), (8, 14, "bEE")],
        // Talking: the mouth opens and closes.
        [CatPose.Talk] = [(10, 8, "kmmk"), (11, 9, "kk")],
        [CatPose.GrumpyFlick] = [(7, 4, "bb"), (8, 4, "EE"), (7, 14, "bb"), (8, 14, "EE"), (10, 8, "bkkb"), (11, 8, "kbbk"), (13, 18, "kk"), (14, 17, "kpk"), (15, 19, "x")],
    };

    static readonly Dictionary<Floater, (string[] Art, Color Fill)> Floaters = new()
    {
        // No outline: a mid violet shows on dark taskbars and light wallpapers alike.
        [Floater.Z] = (["WWWW", "..W.", ".W..", "WWWW"], Color.FromRgb(0x9A, 0x8C, 0xFF)),
        [Floater.Heart] = ([".kk.kk.", "kWWkWWk", "kWWWWWk", ".kWWWk.", "..kWk..", "...k..."], Color.FromRgb(0xFF, 0x5C, 0x8A)),
        [Floater.Alert] = (["kkk", "kWk", "kWk", "kWk", "kkk", "kWk", "kkk"], Color.FromRgb(0xFF, 0xD4, 0x47)),
        [Floater.Sweat] = ([".k.", "kWk", "kWk", ".k."], Color.FromRgb(0x7E, 0xC8, 0xFF)),
        [Floater.Sparkle] = ([".W.", "WWW", ".W."], Color.FromRgb(0xFF, 0xD4, 0x47)),
        // The PC's own news: battery low (a nearly empty red battery), plugged in, a download done.
        [Floater.Battery] = (["kkkkkk.", "kWk..kk", "kWk..kk", "kkkkkk."], Color.FromRgb(0xE5, 0x48, 0x4D)),
        [Floater.Bolt] = (["..WW", ".WW.", "WWWW", ".WW.", "WW.."], Color.FromRgb(0xFF, 0xD4, 0x47)),
        [Floater.Download] = (["..W..", "..W..", "W.W.W", ".WWW.", "..W..", "WWWWW"], Color.FromRgb(0x3D, 0xBE, 0x7A)),
    };

    // Accessory colours (the cat's own palette fills in the rest): R red, Y gold, D dark, L light.
    static readonly Dictionary<char, Color> AccessoryColours = new()
    {
        ['R'] = Color.FromRgb(0xE5, 0x48, 0x4D),
        ['Y'] = Color.FromRgb(0xFF, 0xC8, 0x3D),
        ['D'] = Color.FromRgb(0x2B, 0x2F, 0x3A),
        ['L'] = Color.FromRgb(0x8A, 0x93, 0xA6),
        ['G'] = Color.FromRgb(0x3D, 0xBE, 0x7A),
    };

    /// <summary>What the cat can wear. Patches sit on the drawing like markings.</summary>
    public static IReadOnlyList<Accessory> Accessories { get; } =
    [
        new("bow", "Bow tie", [(14, 7, "RRkkRR"), (15, 7, "R.kk.R")]),
        // Over the head and a cup on each side: also worn by itself while taking meeting notes.
        new("headphones", "Headphones", [(1, 6, "DDDDDDDD"), (2, 4, "DD........DD"), (6, 0, "DD"), (7, 0, "DL"), (8, 0, "DL"), (9, 0, "DD"), (6, 18, "DD"), (7, 18, "LD"), (8, 18, "LD"), (9, 18, "DD")]),
        new("party", "Party hat", [(0, 9, "YY"), (1, 8, "kRRk"), (2, 7, "kRYYRk"), (3, 7, "RRRRRR")]),
        new("crown", "Crown", [(0, 6, "Y..YY..Y"), (1, 6, "YY.YY.YY"), (2, 6, "YYYYYYYY"), (3, 6, "YRYYYYRY")]),
    ];

    /// <summary>The cat wearing an accessory: a skin of its own, so every pose and the peek show it.</summary>
    public static CatSkin Dressed(CatSkin skin, string? accessory)
    {
        if (Accessories.FirstOrDefault(a => a.Id == accessory) is not { } wear) return skin;
        var palette = new Dictionary<char, Color>(skin.Palette);
        foreach (var (key, colour) in AccessoryColours) palette.TryAdd(key, colour);
        return new CatSkin
        {
            Id = $"{skin.Id}+{wear.Id}",
            Name = skin.Name,
            Description = skin.Description,
            Palette = palette,
            Markings = [.. skin.Markings, .. wear.Patches],
        };
    }

    static readonly string[] Paw = [".kkkk.", "kffffk", "kfefek", "kffffk"];

    // Paw to ask: a print it leaves on the screen (toe beans and pad), and the paw itself as the pointer.
    static readonly string[] PrintArt =
    [
        "...kk...kk...",
        "..keek.keek..",
        "..keek.keek..",
        "kk.kk...kk.kk",
        "keek.....keek",
        "keek.kkk.keek",
        ".kk.keeek.kk.",
        "...keeeeek...",
        "..keeeeeeek..",
        "..keeeeeeek..",
        "..keeeeeeek..",
        "...kkkkkkk...",
    ];

    static readonly string[] PawArt =
    [
        "..kk.kk.kk..",
        ".kffkffkffk.",
        ".kfekfekfek.",
        "kkfkkfkkfkkk",
        "kffffffffffk",
        "kfffkeekfffk",
        "kffkeeeekffk",
        "kffkeeeekffk",
        "kfffkkkkfffk",
        ".kffffffffk.",
        "..kffffffk..",
        "..kffffffk..",
        "..kffffffk..",
        "..kffffffk..",
    ];

    /// <summary>A paw print (toes up) in the cat's colours, for the trail.</summary>
    public static BitmapSource PawPrint(CatSkin skin) => Cached($"{skin.Id}/print", () => Render(PrintArt.Select(r => r.ToCharArray()).ToArray(), skin));

    /// <summary>The cat's paw reaching up, toes first: the pointer while picking. Its tip is the middle of the top row.</summary>
    public static BitmapSource PawPointer(CatSkin skin) => Cached($"{skin.Id}/pawpointer", () => Render(PawArt.Select(r => r.ToCharArray()).ToArray(), skin));

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
        new CatSkin
        {
            Id = "kuro",
            Name = "Kuro",
            Description = "Black as ink, golden eyes",
            Palette = Colors(k: 0x08070A, b: 0x2A2830, w: 0x3B3943, f: 0x2A2830, p: 0x2A2830, e: 0xF29BB5,
                E: 0xF6C343, W: 0x141218, P: 0xF29BB5, s: 0x3B3943, B: 0x2A2830),
            Markings = [(14, 7, "ssssss")],
        },
        new CatSkin
        {
            Id = "ginger",
            Name = "Ginger",
            Description = "Orange tabby, stripes and a cream chin",
            Palette = Colors(k: 0x3A2210, b: 0xF5A04A, w: 0xFFF1DF, f: 0xFFF1DF, p: 0xD07418, e: 0xFFB3A0,
                E: 0x3A2210, W: 0x3A2210, P: 0xE8786C, s: 0xE58A30, B: 0xD07418),
            Markings = [(4, 7, "p.pp.p"), (5, 8, "p..p"), (9, 1, "pp"), (10, 1, "p"), (9, 17, "pp"), (10, 18, "p")],
        },
        new CatSkin
        {
            Id = "ash",
            Name = "Ash",
            Description = "Soft grey, green eyes",
            Palette = Colors(k: 0x23262E, b: 0xB8BEC9, w: 0xE9ECF1, f: 0xE9ECF1, p: 0x8C93A1, e: 0xF2B8C6,
                E: 0x2E7D5B, W: 0xBFF2D8, P: 0xE08AA0, s: 0xA3AAB7, B: 0x8C93A1),
            Markings = [(4, 8, "pppp"), (5, 9, "pp")],
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
        if (pose == PeekPose.Sleep)
        {
            // Asleep: slumped a pixel lower, paws folded flat on the edge instead of gripping it.
            for (int r = PeekHeight - 1; r > 0; r--) grid[r] = grid[r - 1];
            grid[0] = new string('.', PeekWidth).ToCharArray();
            Blit(grid, ["kkkkkk", "kffffk"], 0, 5);
            Blit(grid, ["kkkkkk", "kffffk"], 20, 5);
            return Render(grid, skin);
        }
        Blit(grid, Paw, 0, 3 - (pose == PeekPose.LeftPaw ? 1 : 0));
        Blit(grid, Paw, 20, 3 - (pose == PeekPose.RightPaw ? 1 : 0));
        return Render(grid, skin);
    });

    /// <summary>A floating sprite; its outline uses the cat's own outline colour.</summary>
    public static BitmapSource Float(CatSkin skin, Floater kind) => Cached($"{skin.Id}/float/{kind}", () =>
    {
        var (art, fill) = Floaters[kind];
        var palette = new Dictionary<char, Color>(skin.Palette) { ['W'] = fill };
        return Render(art.Select(r => r.ToCharArray()).ToArray(), new CatSkin { Id = skin.Id, Name = skin.Name, Description = "", Palette = palette });
    });

    public static (int Width, int Height) FloatSize(Floater kind) => (Floaters[kind].Art[0].Length, Floaters[kind].Art.Length);

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
            foreach (var kind in Enum.GetValues<Floater>()) Save(Float(skin, kind), $"{skin.Id}-float-{kind}");
            Save(Head(skin), $"{skin.Id}-head");
            Save(PawPrint(skin), $"{skin.Id}-pawprint");
            Save(PawPointer(skin), $"{skin.Id}-pawpointer");
            foreach (var wear in Accessories) Save(Frame(Dressed(skin, wear.Id), CatPose.Idle), $"{skin.Id}-{wear.Id}");
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
                if (pixels[i] == 'x') grid[row][col + i] = '.';
                else if (pixels[i] != '.') grid[row][col + i] = pixels[i];
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
