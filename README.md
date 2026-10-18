# Converted Bro

A little desktop buddy for Windows that converts files without opening a browser.
Drop files on the bro (or right-click them in Explorer) and pick a format from the ring.
Everything runs locally: nothing is uploaded anywhere.

Inspired by [Tangerine](https://x.com/th2049) for macOS.

## What it does (v1)

| Files | Actions |
|---|---|
| Images (png, jpg, webp, heic, avif, gif, bmp, tiff, ico, svg) | PNG · JPG · WEBP · ICO · PDF (all images into one A4 PDF) · Compress · Resize 50% |
| Video (mp4, mov, mkv, avi, webm, …) | MP4 · WEBM · GIF · Extract MP3 · Compress (max 1080p) · Remove audio |
| Audio (mp3, wav, m4a, flac, ogg, …) | MP3 · M4A · WAV · FLAC · OGG |
| PDF | Pages → PNG · Pages → JPG · Split into pages · Merge (PDFs and images, in name order) |

Outputs go next to the original and never overwrite anything (`photo (1).jpg`, `clip (small).mp4`, `report (pages)\page-01.png`).

## Ways to use it

- **Drop on the bro.** Drag files onto the bro and a ring of formats pops up. Click one, or press its number key.
- **Shift + drag.** Start dragging a file anywhere and hold Shift; the bro hops next to your cursor. Drop on him. If you don't, he goes back home.
- **Right-click in Explorer.** Right-click a file → *Show more options* (or Shift + right-click) → **Convert with Bro**. Select many files and they're handled as one batch (a multi-select "Merge" makes one PDF).
- **Double-click the bro**, or use *Convert files…* in the tray menu, to pick files from a dialog.
- **Right-click the bro** or the tray icon for settings: Explorer menu on/off, Shift + drag on/off, start with Windows, quit.

Drag the bro anywhere to give him a new home; he remembers it.

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
          MediaTools (FFmpeg), PdfTools (PDFium + PDFsharp), JobRunner, OutputPaths
  Shell/  SingleInstance (mutex + pipe), ContextMenuRegistrar, GlobalMouseHook, StartupRegistration
  UI/     BuddyWindow, RadialMenuWindow, ToastWindow, TrayIcon, BroArt (icon drawing)
```

To add an action, add a `BroAction` in `ActionCatalog.Build()`. The radial menu and the Explorer menu pick it up automatically (re-run `--register` for Explorer).

## Licensing notes

- Magick.NET: Apache-2.0. Docnet.Core / PDFium: MIT / BSD.
- **PDFsharp is pinned to 6.2.4 on purpose.** 6.5.0 switched to AGPL/commercial and refuses to run without a license.
- The bundled FFmpeg build is **GPL** (needed for libx264). That's fine for personal use; to distribute the app under a non-GPL license, switch to an LGPL build and use a different H.264 encoder.

## Known limits / next up

- Windows 11 shows the entry under *Show more options*. Getting it into the new top-level menu needs an `IExplorerCommand` handler plus a signed sparse package.
- Not supported yet: Word/Excel/PowerPoint documents (needs LibreOffice), PDF compression (needs Ghostscript).
- Ideas: compress to a target size ("under 2 MB"), trim video, remove background, OCR (built into Windows), QR codes, rename in bulk, strip photo location data.
