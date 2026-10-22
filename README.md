# Converted Bro

A little pixel cat for Windows that converts files without opening a browser.
Select a file and press Ctrl+Alt+X, Shift + drag it anywhere and drop it on a format, drop it on the cat, or right-click it in Explorer.
Everything runs locally: nothing is uploaded anywhere.

Inspired by [Tangerine](https://x.com/th2049) for macOS.

## What it does

The wheel has up to five pages: **formats** (what to convert to), **tools** (what to do to the file), **size limit** (make it fit an upload limit), **form photos** (exact sizes for exam, passport and visa forms) and **files** (zip anything, folders included).

| Files | Formats | Tools | Size limit |
|---|---|---|---|
| Images (png, jpg, webp, heic, avif, gif, bmp, tiff, ico, svg) | PNG, JPG, WEBP, ICO, PDF (all images into one A4 PDF) | Remove background, Scan, Copy text, Read QR, Copy image, Compress, Half size, Rotate, Remove metadata (camera, date, GPS) | 100 KB, 200 KB, 1 MB, 2 MB; form photos: Exam photo, Passport, Visa, Signature |
| Video (mp4, mov, mkv, avi, webm, …) | MP4, WEBM, GIF, MP3 | Compress (up to 1080p), Half size, Rotate, Mute | |
| Audio (mp3, wav, m4a, flac, ogg, …) | MP3, M4A, WAV, FLAC, OGG | Even out volume | |
| PDF | Pages to PNG, Pages to JPG, Word (with Word 2013+) | Copy text, Edit pages, Sign, Unlock, Lock, Page numbers, Split into pages, Merge (PDFs and images, in name order) | 100 KB, 200 KB, 1 MB, 2 MB |
| Documents (doc, docx, xls, xlsx, csv, ppt, pptx, odt, ods, odp, rtf) | PDF | | |
| Archives (zip, 7z, rar, tar, tar.gz, …) | | Extract here | |
| Anything, folders too | | Zip (on the Files page) | |

- **Size limit** finds the highest quality that fits (binary search on JPEG/WEBP quality, then a smaller picture if even the lowest quality is too big). PDFs are re-rendered page by page, so their pages become pictures.
- **Copy text** uses the OCR engine built into Windows (offline) and puts the text on the clipboard. It reads the languages installed in Windows.
- **Snip and convert** (Ctrl+Shift+Q, changeable in Settings): the screen freezes, drag a box, and the wheel opens on the snip. Snips are saved in `Pictures\Converted Bro`. Copy text on a snip reads text from anything on screen.
- **Edit pages**: thumbnails of every page (of one or several PDFs); drag to reorder, turn or remove, then save one PDF.
- **Sign**: draw your signature once (or use a photo of it on paper; the paper is removed), then click to place it on any page, drag to move, drag the corner to resize. It's a visual signature, not a certificate.
- **Unlock / Lock**: remove a known password (the dialog checks it straight away), or add one (AES-256). Unusual encryption that PDFsharp can't rewrite is unlocked by re-rendering the pages.
- **Page numbers**: "3 / 10" centred at the bottom of each page.
- **Remove background**: runs offline on your own PC with BiRefNet (MIT). The model (115 MB) downloads the first time you use it, to `%LOCALAPPDATA%\ConvertedBro\models`. One photo opens an editor: the cut-out on a checkerboard, a background swatch row (transparent, white, black, grey, blue, red, or any colour), and a touch-up brush (Erase / Restore, S–XL, `[` `]` to resize, Ctrl+Z to undo, scroll to zoom, right-drag to pan, "Hold to compare" for the original). Save gives `name (no background).png`, or a JPG on a colour; Copy puts it on the clipboard. Several photos at once are cut out straight to transparent PNGs. The mask is refined at full resolution (guided filter) and the old background's colour is cleaned out of soft edges, so hair and fur don't get a halo. It uses the graphics card through DirectML when it can (about 4 s a photo on a GTX 1650; the first one after a while takes longer while the card warms up) and the processor otherwise.
- **Form photos**: Exam photo (3.5 × 4.5 cm, 20–50 KB), Passport (2 × 2 in), Visa (35 × 45 mm), Signature (3.5 × 1.5 cm, 10–20 KB). The crop starts centred on the face (Windows' built-in face detector, offline) or on the signature's ink; drag or scroll to adjust. The JPG gets the right pixel size and DPI, and the highest quality inside the size range (a tiny file is padded with an empty comment to reach a minimum). **White background** swaps whatever is behind you for plain white.
- **Paste onto the cat** (*Paste from clipboard* in its menu): copied files open the wheel, a copied picture (a screenshot, an image from a browser) is saved in `Pictures\Converted Bro` and opens the wheel, and copied text opens the QR maker. Every finished job's card has **Copy**: one picture pastes as a picture, anything else as files.
- **Record the screen** (Ctrl+Alt+R, changeable in Settings; also in the cat's menu and Tools): the screen dims (live, not frozen); drag a box, or press Enter for the whole screen. A dashed frame marks the area and a small bar offers **microphone** and **system sound** toggles (remembered) and **Record**. After a 3-2-1 countdown the frame turns red and the bar shows the time and **Stop** (or press Ctrl+Alt+R again). The frame and bar never appear in the video. It's saved as `Recording <date>.mp4` (results folder, or `Videos\Converted Bro`), goes into History, and the wheel opens on it, ready for GIF, compress and the rest. Capture uses Windows' GPU desktop duplication through FFmpeg (`ddagrab`) and the graphics card's encoder (NVENC) when there is one, falling back to GDI capture and x264. Sound is captured with WASAPI (NAudio) and mixed in at the end, with silent stretches filled so it stays in sync.
- **Office documents to PDF**: Word, Excel and PowerPoint files (and their OpenDocument cousins, RTF, CSV) through the Office on the PC, run hidden; LibreOffice is used when Office isn't installed. Apps that were already open with your documents are left running. **PDF to Word** needs Word 2013 or newer; on a PC with an old Office next to a new one the newest Word is used, an unlicensed Word gets a clear message instead of a hang, and Word's own "convert this PDF?" prompt is switched off only for the conversion and put back afterwards.
- **Pick a colour** (Ctrl+Alt+C, changeable in Settings): the screen freezes with a magnifier under the cursor; click to copy the hex (`#1E90FF`), and the card offers RGB and HSL too. Arrow keys nudge one pixel (Shift for ten), Enter picks, Esc cancels. It isn't Ctrl+Shift+C because terminals copy and browsers inspect with that.
- **QR codes**: *Make a QR code* in the cat's menu turns a link or any text into a QR code as you type (save as PNG or copy). **Read QR** on any picture or snip reads QR codes and barcodes (light-on-dark too), copies what they say and offers *Open link* for a web address.
- **Zip / Extract**: Zip packs files and whole folders into one `.zip` named after the first item (photos and videos are stored without recompressing, which is faster and no bigger). Extract unpacks zip, 7z, rar, tar and compressed tarballs next to the archive: a single folder inside comes out as itself, anything else goes into a folder named after the archive. Entries that try to escape the folder (`..\..\`) are skipped. Password-protected archives aren't supported.
- **Scan** opens an editor: auto-detect places four corners on the page (paper is bright and colourless: Otsu threshold, biggest blob, extreme corners), you drag them where needed (a loupe magnifies the corner), pick *Clean colour*, *Black and white* or *Original*, rotate, preview the result, and save one PDF or one image per photo. Cleanup straightens the page with a perspective warp and divides by a blurred copy of itself, which evens out shadows and turns the paper white.

Results never overwrite anything (`photo (1).jpg`, `clip (small).mp4`, `report (pages)\page-01.png`). Where they go is up to you, in Settings → **Where results go**:

- **One folder** (the default): everything lands in `C:\Users\<you>\Converted Bro` (outside OneDrive, so big videos don't get synced), or any folder you pick with *Change…*. Turn on **Sort by type** to get `Images`, `Videos`, `Audio`, `PDFs`, `Archives` and `Extracted` subfolders. Snips, pasted pictures and QR codes go there too. *Open results folder* is in the cat's menu.
- **Next to the original**: each result sits beside the file it came from (snips then go to `Pictures\Converted Bro`).

## The dashboard

Open it by launching ConvertedBro.exe, clicking the tray icon, or with *Open Converted Bro* in the cat's menu. It's styled after Wispr Flow: a warm paper canvas or deep charcoal (the moon in the title bar switches; it starts out following Windows), white cards, Instrument Serif for the big moments, and one warm orange accent.

- **Home**: a greeting, the black card (drop files on it, choose some, or snip the screen), the cat's numbers (files converted, space saved, day streak), one tip for whatever isn't set up yet, and today's results with Open, Copy, Folder and "Do it again" on hover.
- **Tools**: every tool by kind of file. "Convert to" and "Shrink under" chips, then tiles; pick one and choose the files. Capture tools (snip, colour picker, QR, paste) are at the top.
- **Player**: videos and sound play right inside the dashboard: click a recording or any video/audio result in Home or History (or its play button). Play/pause, a seek bar you can click or drag, time, volume and mute, speed (1x, 1.5x, 2x, 0.5x) and full screen; Open (in your usual player), Copy, Show in folder and Convert (GIF, compress and the rest) at the top. Keys: Space or K, Left/Right 5 s, J/L 10 s, Up/Down volume, M mute, F full screen, Esc back. Step to another page and it pauses and waits in the sidebar. It uses Windows' own media engine, so it plays what Windows plays; anything else gets a message and an Open button. `ConvertedBro.exe play <file>` opens it directly.
- **History**: every job ever, grouped by day, with search (Ctrl+F) and filters. Removing a line only forgets it; the files stay. Kept in `%LOCALAPPDATA%\ConvertedBro\history.json` (the newest 2,000).
- **Set up Converted Bro**: a checklist in the sidebar (Explorer menu, the Ctrl+Alt+X shortcut, results folder, start with Windows) that checks the real state and does each step when clicked. It goes away when everything's done.
- **Editors inside**: Pages, Sign, Remove background, Scan, Form photos, Lock/Unlock and the QR maker open as pages in the dashboard (in its look, with a back arrow), wherever you start them: the wheel, the cat, Explorer or Tools. Step to another page and the editor waits in the sidebar; opening a different editor replaces it. Esc goes back.
- **Settings** is a page too. Peeking at it never closes an open editor. Click the cat at the bottom of the sidebar to switch cats.

*Start with Windows* starts quietly (`--background`): just the cat, no window. `ConvertedBro.exe dashboard [home|tools|history]` opens a page.

## The cat's moods

The cat hides below the taskbar edge with its ears and paws showing, and comes up when there's something to do. It also has moods:

- **Sleep.** After 5 quiet minutes (2 at night, 10 pm to 6 am) it slumps, lets go of the edge and Z's drift up. Hover to wake it: it pops up with a yawn. Any drop, job or shortcut wakes it too.
- **Big files.** Drop or start something over 1 GB and it's startled: wide eyes, a "!" and a hop.
- **Hard work.** It drums its paws while converting; past 8 seconds it squeezes its eyes shut and sweats, and a long job ends with a double hop and sparkles.
- **Petting.** Rub the mouse back and forth over its head: happy eyes and hearts. Six seconds straight is too much: a frown and a tail flick, and no petting for a moment.
- **Counter.** Its menu shows the tally under its name ("142 files converted, 1.3 GB saved"; saved counts only results that came out smaller). At 10, 25, 50, 100, 250, 500, 1,000 files and so on it throws a little party.

Dev aid: `ConvertedBro.exe cat <sleep|startle|purr|grumpy|strain|party>` acts out a mood; `ConvertedBro.exe catmenu` opens its menu at the mouse.

## Ways to use it

- **Select and press Ctrl+Alt+X.** Select files in File Explorer (any tab) or on the desktop, press Ctrl+Alt+X, and the wheel opens at the mouse. Press a wedge's number or click it; Tab switches pages. Change the shortcut in Settings.
- **Shift + drag.** Start dragging a file anywhere and hold Shift: the wheel opens right under the cursor. Drop the file on a wedge. To reach the tools, rest on the centre for half a second while dragging.
- **Drop on the cat.** Drag files onto the cat (even while it's hiding) and the wheel opens next to it. Click a wedge or press its number; click the centre, scroll, or press Tab to switch pages.
- **Right-click in Explorer.** Right-click a file → *Show more options* (or Shift + right-click) → **Convert with Bro**. Select many files and they're handled as one batch (a multi-select "Merge" makes one PDF).
- **Double-click the cat**, or *Convert files…* in its menu, to pick files from a dialog.
- **Right-click the cat** or the tray icon for the menu: paste, snip, pick a colour, make a QR code, pick a cat, pick a look, Explorer menu, Shift + drag, start with Windows, quit.

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

### Installer

```bash
powershell -ExecutionPolicy Bypass -File build/make-installer.ps1
```

This publishes the app and packs it with [Velopack](https://velopack.io) (MIT; `dotnet tool install -g vpk`) into `Releases/ConvertedBroApp-win-Setup.exe` (about 120 MB, most of it FFmpeg and the AI runtimes). The version comes from `<Version>` in the project file, or `-Version 1.0.1`.

What Setup.exe does:

- Installs for the current user, no admin prompt, to `%LOCALAPPDATA%\ConvertedBroApp` (the app's own data stays in `%LOCALAPPDATA%\ConvertedBro`), shows a splash with the cat, and installs the .NET 8 Desktop Runtime if it's missing.
- Adds **Converted Bro** to the Start menu, turns on *Start with Windows* (`--background`: just the cat at sign-in) and adds the Explorer right-click menu, then opens the dashboard.
- Registers an uninstaller in Settings → Apps. Uninstalling removes the app, the shortcut, the startup entry and the Explorer menu; settings, history, signatures and the background model stay, in case it comes back (delete `%LOCALAPPDATA%\ConvertedBro` to remove them too).
- The build also writes the files Velopack needs for one-click updates later (`releases.win.json`, the `.nupkg`).

Setup.exe isn't code-signed, so a copy downloaded from the internet gets Windows SmartScreen's "Windows protected your PC" (More info → Run anyway). Signing needs a code-signing certificate (`vpk pack --signParams`). The bundled FFmpeg is the GPL build: fine for personal use; sharing the installer publicly means following the GPL (or switching to an LGPL build).

Command line (what the Explorer menu calls):

```
ConvertedBro.exe                          start the bro + tray icon
ConvertedBro.exe run <action-id> <files>  run one action, e.g. run video-mp4 "a.mov"
ConvertedBro.exe menu <files>             show the format ring at the cursor
ConvertedBro.exe paste                    give the clipboard to the cat
ConvertedBro.exe pick                     pick a colour from the screen
ConvertedBro.exe qr [text]                open the QR maker
ConvertedBro.exe settings                 open Settings
ConvertedBro.exe selected                 the wheel for what's selected in the Explorer window in front
ConvertedBro.exe --register               add the Explorer right-click menu (current user, no admin)
ConvertedBro.exe --unregister             remove it
```

Dev aids (run and exit, results in the log): `--export-sprites <folder>`, `--scan-test <photo>`, `--pdf-test <pdf>`, `--sign-test <pdf> <signature photo>`, `--form-test <exam|passport|visa|signature> <photo> [white]`, `--cutout-test <folder of jpgs>`, `--qr-test <text> <out.png>`, `--desktop-selection-test`, `--record-test <seconds> <out.mp4>`; `ConvertedBro.exe record-demo <seconds>` runs the recorder on a fixed area (set `CB_SHOW_RECORDER=1` to let screenshots see its frame and bar).

Only one ConvertedBro runs per user. Extra launches (Explorer starts one per selected file) pass their files to it over a named pipe, and they're batched for 0.7 s. When started from Explorer without the bro running, it works quietly and exits when done.

Logs and settings: `%LOCALAPPDATA%\ConvertedBro\`.

## Project layout

```
src/ConvertedBro/
  Core/   FileKind, ActionCatalog (every action lives here), ImageTools (Magick.NET),
          MediaTools (FFmpeg), PdfTools (PDFium + PDFsharp), SizeTools, TextTools (Windows OCR),
          ScanTools, Cutout (background removal, ONNX Runtime), FormPhotos, ArchiveTools, QrTools, JobRunner, OutputPaths
  Shell/  SingleInstance (mutex + pipe), ContextMenuRegistrar, GlobalMouseHook, StartupRegistration, Hotkeys, ExplorerSelection
  UI/     CatWindow, CatSprites, ConvertWheel, ScanWindow, CutoutWindow, FormPhotoWindow, ColourPickerWindow, QrWindow, BroMenu, ToastWindow, TrayIcon, Theme, Glass, LiquidGlass, PixelShape, WedgeGeometry, BroArt
  Assets/ Fonts/PixelifySans.ttf (SIL Open Font License, see OFL.txt)
```

To add an action, add a `BroAction` in `ActionCatalog.Build()`, with `Group = ActionGroup.Tool` and a Fluent `Glyph` for tools. The wheel and the Explorer menu pick it up automatically (re-run `--register` for Explorer).

## Licensing notes

- Magick.NET: Apache-2.0. Docnet.Core / PDFium: MIT / BSD. Pixelify Sans and Instrument Serif: SIL Open Font License 1.1 (see the OFL files in Assets/Fonts).
- SharpCompress: MIT. QRCoder: MIT. ZXing.Net: Apache-2.0. NAudio: MIT. Velopack: MIT.
- ONNX Runtime (DirectML): MIT. BiRefNet model weights: MIT (downloaded at first use from the onnx-community conversion on Hugging Face, not bundled).
- The cats are original pixel art made for this project.
- **PDFsharp is pinned to 6.2.4 on purpose.** 6.5.0 switched to AGPL/commercial and refuses to run without a license.
- The bundled FFmpeg build is **GPL** (needed for libx264). That's fine for personal use; to distribute the app under a non-GPL license, switch to an LGPL build and use a different H.264 encoder.

## Known limits / next up

- Windows 11 shows the entry under *Show more options*. Getting it into the new top-level menu needs an `IExplorerCommand` handler plus a signed sparse package.
- Not supported yet: Word/Excel/PowerPoint documents (needs Office or LibreOffice).
- The build output folder is `bin/<config>/net8.0-windows10.0.19041.0/win-x64/` (the Windows SDK target is needed for OCR).
- Ideas: compress to a target size ("under 2 MB"), trim video, remove background, OCR (built into Windows), QR codes, rename in bulk.
