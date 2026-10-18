# Converted Bro

A little pixel cat for Windows that converts files without opening a browser.
Shift + drag a file anywhere and drop it on a format, drop it on the cat, or right-click it in Explorer.
Everything runs locally: nothing is uploaded anywhere.

Inspired by [Tangerine](https://x.com/th2049) for macOS.

## What it does

The wheel has up to three pages: **formats** (what to convert to), **tools** (what to do to the file) and **size limit** (make it fit an upload limit).

| Files | Formats | Tools | Size limit |
|---|---|---|---|
| Images (png, jpg, webp, heic, avif, gif, bmp, tiff, ico, svg) | PNG, JPG, WEBP, ICO, PDF (all images into one A4 PDF) | Scan, Copy text, Compress, Half size, Rotate, Remove metadata (camera, date, GPS) | 100 KB, 200 KB, 1 MB, 2 MB |
| Video (mp4, mov, mkv, avi, webm, …) | MP4, WEBM, GIF, MP3 | Compress (up to 1080p), Half size, Rotate, Mute | |
| Audio (mp3, wav, m4a, flac, ogg, …) | MP3, M4A, WAV, FLAC, OGG | Even out volume | |
| PDF | Pages to PNG, Pages to JPG | Copy text, Split into pages, Merge (PDFs and images, in name order) | 100 KB, 200 KB, 1 MB, 2 MB |

- **Size limit** finds the highest quality that fits (binary search on JPEG/WEBP quality, then a smaller picture if even the lowest quality is too big). PDFs are re-rendered page by page, so their pages become pictures.
- **Copy text** uses the OCR engine built into Windows (offline) and puts the text on the clipboard. It reads the languages installed in Windows.
- **Scan** opens an editor: auto-detect places four corners on the page (paper is bright and colourless: Otsu threshold, biggest blob, extreme corners), you drag them where needed (a loupe magnifies the corner), pick *Clean colour*, *Black and white* or *Original*, rotate, preview the result, and save one PDF or one image per photo. Cleanup straightens the page with a perspective warp and divides by a blurred copy of itself, which evens out shadows and turns the paper white.

Outputs go next to the original and never overwrite anything (`photo (1).jpg`, `clip (small).mp4`, `report (pages)\page-01.png`).

## Ways to use it

- **Shift + drag.** Start dragging a file anywhere and hold Shift: the wheel opens right under the cursor. Drop the file on a wedge. To reach the tools, rest on the centre for half a second while dragging.
- **Drop on the cat.** Drag files onto the cat (even while it's hiding) and the wheel opens next to it. Click a wedge or press its number; click the centre, scroll, or press Tab to switch pages.
- **Right-click in Explorer.** Right-click a file → *Show more options* (or Shift + right-click) → **Convert with Bro**. Select many files and they're handled as one batch (a multi-select "Merge" makes one PDF).
- **Double-click the cat**, or *Convert files…* in its menu, to pick files from a dialog.
- **Right-click the cat** or the tray icon for the menu: pick a cat, pick a look, Explorer menu, Shift + drag, start with Windows, quit.

## The cat

- Lives in the bottom-left corner, sitting on the taskbar edge. Drag it sideways to move it along the edge.
- When there's nothing to do it ducks behind the edge, leaving only its ears and paws peeking over. It pops up when you hover it, Shift + drag a file, drop something on it, or a conversion is running.
- While converting it taps its paws; when a file is done it does a happy hop, then ducks again.
- Three cats to choose from in the menu: **Mochi** (calico), **Latte** (Siamese) and **Sky** (white and pale blue).
- The art is drawn as text grids in `UI/CatSprites.cs` (one character per pixel, poses are small patches on top). `ConvertedBro.exe --export-sprites <folder>` writes every frame as PNG.

## Looks

Pick one in the menu:

- **Arcade** (default): pixel wheel with stepped, bevelled wedges and hard shadows, Pixelify Sans, square menus and toasts with a block progress bar. Everything is drawn on whole screen pixels so it stays sharp.
- **Glass**: Apple-style liquid glass. Each wedge, the hub, the menu and the toasts are separate glass pieces that bend what's behind them at their rims, with a rim light, slight frost, a saturation lift and a soft shadow. Segoe UI Variable. Follows the Windows light/dark setting.

### How the liquid glass works

`UI/LiquidGlass.cs` does on the CPU what the open-source web versions do with SVG displacement maps or WebGL shaders ([kube.io's write-up](https://kube.io/blog/liquid-glass-css-svg/), [liquidGL](https://github.com/naughtyduk/liquidGL), [liquid-glass-react](https://github.com/rdev/liquid-glass-react)):

1. Take a snapshot of the screen behind the popup (before it appears).
2. Describe each shape as a signed distance field (`GlassShapes`: circles, rounded rectangles, ring slices).
3. Give the rim a convex squircle profile, `f(x) = (1 − (1 − x)⁴)^¼`, and bend each ray by Snell's law (air 1.0 → glass 1.5). The sideways offset says where to sample the snapshot, so the rims bend the desktop and the middle stays clear. Red and blue are sampled slightly apart for a hint of colour fringing.
4. Frost a little, lift saturation, tint, then add a specular rim light from the upper left (and a softer one on the far side) plus a soft drop shadow.

**Live glass.** The glass follows what's behind it while a popup is open (a video playing, a window moving). Each glass window is excluded from screen capture (`SetWindowDisplayAffinity`, it stays visible on screen), so `LiveGlass` can keep grabbing the area behind it on a background thread. Everything that depends only on the shape (refraction offsets, rim light, shadow) is worked out once in a `GlassPlan`; each frame then only halves and frosts the snapshot and resamples it, and only when the background actually changed. Roughly 7 ms of maths plus about 15 ms for the Windows screen grab per frame, so 25–40 fps depending on size.

Side effect: while live glass is on, the wheel, menu and toasts don't appear in screenshots or screen recordings. Turn off *Glass follows what's behind* in the menu to get a still snapshot instead (and show up in recordings). For a frame dump while tuning, set `CONVERTEDBRO_GLASS_DUMP` to a folder.

The hovered wedge is lit in the colour of the file type: tangerine for images, iris for video, mint for audio, coral for PDF. Design tokens live in `UI/Theme.cs`.

## Build and run

Needs the .NET 8 SDK. On this machine it's installed at `%USERPROFILE%\.dotnet` (not on PATH).

```bash
~/.dotnet/dotnet.exe build src/ConvertedBro/ConvertedBro.csproj
```

```bash
~/.dotnet/dotnet.exe publish src/ConvertedBro/ConvertedBro.csproj -c Release -o publish/ConvertedBro
```

FFmpeg is not in the repo. Put `ffmpeg.exe` and its DLLs in `tools/ffmpeg/` (the BtbN `win64-gpl-shared` build) and the build copies them next to the exe. The app also finds `ffmpeg.exe` on PATH.

Command line (what the Explorer menu calls):

```
ConvertedBro.exe                          start the bro + tray icon
ConvertedBro.exe run <action-id> <files>  run one action, e.g. run video-mp4 "a.mov"
ConvertedBro.exe menu <files>             show the format ring at the cursor
ConvertedBro.exe --register               add the Explorer right-click menu (current user, no admin)
ConvertedBro.exe --unregister             remove it
```

Only one ConvertedBro runs per user. Extra launches (Explorer starts one per selected file) pass their files to it over a named pipe, and they're batched for 0.7 s. When started from Explorer without the bro running, it works quietly and exits when done.

Logs and settings: `%LOCALAPPDATA%\ConvertedBro\`.

## Project layout

```
src/ConvertedBro/
  Core/   FileKind, ActionCatalog (every action lives here), ImageTools (Magick.NET),
          MediaTools (FFmpeg), PdfTools (PDFium + PDFsharp), SizeTools, TextTools (Windows OCR),
          ScanTools, JobRunner, OutputPaths
  Shell/  SingleInstance (mutex + pipe), ContextMenuRegistrar, GlobalMouseHook, StartupRegistration
  UI/     CatWindow, CatSprites, ConvertWheel, ScanWindow, BroMenu, ToastWindow, TrayIcon, Theme, Glass, LiquidGlass, PixelShape, WedgeGeometry, BroArt
  Assets/ Fonts/PixelifySans.ttf (SIL Open Font License, see OFL.txt)
```

To add an action, add a `BroAction` in `ActionCatalog.Build()`, with `Group = ActionGroup.Tool` and a Fluent `Glyph` for tools. The wheel and the Explorer menu pick it up automatically (re-run `--register` for Explorer).

## Licensing notes

- Magick.NET: Apache-2.0. Docnet.Core / PDFium: MIT / BSD. Pixelify Sans: SIL Open Font License 1.1.
- The cats are original pixel art made for this project.
- **PDFsharp is pinned to 6.2.4 on purpose.** 6.5.0 switched to AGPL/commercial and refuses to run without a license.
- The bundled FFmpeg build is **GPL** (needed for libx264). That's fine for personal use; to distribute the app under a non-GPL license, switch to an LGPL build and use a different H.264 encoder.

## Known limits / next up

- Windows 11 shows the entry under *Show more options*. Getting it into the new top-level menu needs an `IExplorerCommand` handler plus a signed sparse package.
- Not supported yet: Word/Excel/PowerPoint documents (needs Office or LibreOffice).
- The build output folder is `bin/<config>/net8.0-windows10.0.19041.0/win-x64/` (the Windows SDK target is needed for OCR).
- Ideas: compress to a target size ("under 2 MB"), trim video, remove background, OCR (built into Windows), QR codes, rename in bulk.
