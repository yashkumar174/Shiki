# Shiki ad V3: script and shot list

Two cuts from the same parts, both **1920×1080, 60 fps**:

- **Main cut, 39 s:** a 6 s opener on how this used to be done, then a calm open like reel 2, a faster middle like reel 1, and a calm close.
- **Showreel cut, 15 s:** the same parts at double speed, one beat a second. It shows off the motion work (the "showreel for a resume" idea), still with Shiki's look.

There's no voiceover; the words on screen carry it. Under it: a soft beat, with sound effects on the cuts.

Footage sources:
- **G**: made in Remotion (type, lines, sprites, mock UI).
- **S**: crisp stills from Shiki's snapshot tools (`public/v3/shots`).
- **R**: real recordings of Shiki's windows, played by Shiki's own demo commands with no mouse or keys touched (`public/v3/rec`).
- **P**: the iPhone. It's built in Remotion for now; a real screen recording from Yash can replace it later.

## Main cut

### Opener: how it evolved (0.0–6.0, G)

The same task done the old way, getting a little less bad each era, until it collapses into one sentence. Each era looks like its own time, with mock UI built in Remotion. Use no real logos or trademarked chrome; generic shapes say it.

| # | Time | On screen | Words on screen |
|---|---|---|---|
| 0a | 0.0–1.6 | **2006.** A grey bevelled desktop window. A "Save as…" dialog, then a format dropdown, then "Export…". A cursor hunts through menus; a click counter in the corner climbs to 12. Hard, jerky, low-frame-rate motion (step the animation at 12 fps on purpose). | `2006` / It took twelve clicks. |
| 0b | 1.6–3.2 | **2014.** A flat browser window: a free online converter with fake ad boxes. "Uploading… 43%", stuck. Beside it, an email to yourself: "photo.jpg (compressed to 1.2 MB)". Smoother but cluttered. | `2014` / Then a website and an email to yourself. |
| 0c | 3.2–4.6 | **2021.** Six rounded app tiles in a row; the cursor ping-pongs between them; a cable icon; a chat thread with a typing indicator. Motion is fast and busy, with too many things moving at once. | `2021` / Then six apps. |
| 0d | 4.6–6.0 | Everything is sucked into one point in the centre (scale toward the point, motion blur, a whoosh). The point becomes the thin line of beat 1. | **Now, one sentence.** |

The year stamp is big, in the pixel face, top left, and ticks over like an odometer between eras. The caption sits bottom centre, in Manrope.

### The rest (each time is from the start of the cut)

| # | Time | On screen | Words on screen | Source |
|---|---|---|---|---|
| 1 | 6.0–8.0 | A thin line across the dark field, a tiny label in its middle | ‹ your desktop has a cat › | G |
| 2 | 8.0–9.6 | The pixel cat (Mochi, named **melloo**) drops onto the line, lands, blinks | | G (`v3/cat/mochi-*.png`) |
| 3 | 9.6–12.4 | Typed with a block cursor, one under the other | **Say it.** / It happens**.** (moonlight dot) | G |
| 4 | 12.4–16.0 | Curves fan out from the cat to five labelled nodes, each with a small pictogram (no brand logos) | WhatsApp Mummy "on my way" · play Seedhe Maut · email the invoice to Tausif · 3 photos from my iPhone · remind me at 5 | G |
| 5 | 16.0–18.6 | The voice pill, close: the waveform moves, then the words type in | play Seedhe Maut for me | S `pill-words.png`, `pill-listening.png`, animated in G |
| 6 | 18.6–21.0 | The chat card: "Two things, one go", then the WhatsApp number card | | R `chat.webm` |
| 7 | 21.0–22.2 | Moonlight rings pulse out from the card | done. | G |
| 8 | 22.2–26.0 | The turn: Shiki's night window (stars, moon) fills the frame; Home's tiles fly in and lock into the board, one by one | Everything, **one place**. | S `home.png` sliced into tiles, plus G |
| 9 | 26.0–28.0 | The folder tab and the pills: each pill swells and opens as the "mouse" passes | | R `pills.mp4` |
| 10 | 28.0–31.0 | Cats: the cards slide past; the pencil opens and a name is typed | Name your cat. | R `cats.mp4`, `rename.mp4` |
| 11 | 31.0–34.0 | Levels: the camera glides down the paw-print walk to the cat at "You are here" | Level up together. | S `levels-tall.png` with a G pan, or R `levels.mp4` |
| 12 | 34.0–36.4 | The iPhone sends a photo; on the PC, a toast: "1 photo from iPhone" | Full quality. No cables. | P (G) |
| 13 | 36.4–39.0 | The thin line returns; the cat walks along it; logo | **Shiki** / Say it. It happens. / Free for Windows | G |

## Showreel cut (15 s)

The same parts, re-timed for density: 0–2 the evolution flash (three years in 0.6 s each, then the collapse), 2–3 the cat drops, 3–4.5 "Say it." typed, 4.5–6.5 the hub fan, 6.5–8 the pill and the card, 8–10 the bento assembles, 10–11 the pills, 11–12.5 the cats and the rename, 12.5–13.5 the levels walk, 13.5–15 the logo.
Faster cuts and more kinetic type. Use match cuts: shapes carry over from one beat to the next, and the line, the dot and the cat's eye are the through-line.

## What's recorded (in `ad/shiki-ad/public/v3`)

- `rec/pills.mp4` (5.5 s): Home, each pill swelling in turn. Window 1550×1026 on ink.
- `rec/home.mp4` (6.1 s): Home gliding top to bottom.
- `rec/cats.mp4` (6.3 s): Cats gliding top to bottom.
- `rec/rename.mp4` (2.1 s): Latte's pencil opens, "Simba" is typed, it goes back to Latte.
- `rec/levels.mp4` (7.7 s): Levels gliding down the walk.
- `rec/chat.webm` (9.6 s, transparent): the ask window, the two-things card, then the number card.
- `shots/home.png`, `home-tall.png`, `cats-tall.png`, `levels-tall.png`, `settings.png`, `pill-words.png`, `pill-listening.png`.
- `cat/`: every cat × every pose, plus heads, paw prints, floaters and accessories (20×20 pixel art; scale by whole numbers with nearest-neighbour only).
- `fonts/`: Pixelify Sans and Manrope.

**Privacy:** Home's "Lately" tile shows real file names. Crop it out, blur it, or cover it with a made-up tile; don't show it as-is.
