# Brief: build Shiki ad V3 in Remotion

You are the motion designer on this. Read these first, in order:

1. `ad/v3/STYLE.md`: what the two reference reels do, what V3 takes from them, and the brand kit (colours, fonts, pixel rules).
2. `ad/v3/SCRIPT.md`: the beats with timings, the 15 s showreel cut, and what footage exists.
3. `ad/shiki-ad/src/v2/` (skim): how the last ad was put together in this project (parts, sprites, sound), so V3 can reuse what's good. V3 should not look like V2: V2 was cream and warm; V3 is the night shift.

## What Shiki is (for the words and the feel)

Shiki is a pixel cat that lives on your Windows desktop. You hold a key and say what you want ("WhatsApp Mummy I'm on my way and email these to me"), and it does it: messages, music, email, files, reminders, notes from meetings. The app just got a full redesign called **Night shift**, and the ad exists to show it off:

- **Home** is a bento board. Your cat's card, a night window with a crescent moon where you drop files, your level, Ask, quick tools, and numbers.
- **A folder-tab bar of pills** that swell and open as the mouse passes.
- **Cats:** nine cats with their own personalities (Mochi, Latte, Sky, Kuro, Ginger, Ash, Sumi, Maple, Biscuit). You can rename your cat; this user's Mochi is called **melloo**, and the name shows everywhere.
- **Levels:** a winding paw-print walk of 12 levels. Each level has small tasks, and finishing them unlocks cats and things to wear (party hat, headphones, crown).
- **The iPhone link:** photos go from the phone to the PC at full quality over Wi-Fi, with no cables.

## What to make

In `ad/shiki-ad/src/v3/`, register two compositions in `src/Root.tsx` (keep the existing ones):

- `ShikiAdV3`: 1920×1080, 60 fps, 39 s (2340 frames). The main cut.
- `ShikiAdV3Reel`: 1920×1080, 60 fps, 15 s (900 frames). The showreel cut.

Both share scene components; the reel passes faster timings. Put the timings in one table in code, so beats can be nudged without touching the scenes.

## Assets (all in `ad/shiki-ad/public/v3/`)

- `rec/*.mp4`: real dashboard recordings, 1550×1026 on ink `#0A0D14`.
- `rec/chat.webm`: the chat window, transparent.
  - Use `OffthreadVideo` (or `Video` from `@remotion/media`), trim with start/end, and play them faster or slower if a beat needs it.
- `shots/*.png`: crisp stills. `levels-tall.png` (1250×4713) is the whole Levels walk, for a camera pan; `home.png` can be sliced into its tiles for the bento assembly.
- `cat/*.png`: 20×20 pixel sprites for every cat and pose.
  - Scale by whole numbers only, with `imageRendering: "pixelated"`.
  - Animate the cat by swapping poses (Idle, Blink, Happy, Listen, Talk, TapLeft/TapRight and so on), not by squashing the art.
- `fonts/`: load Pixelify Sans and Manrope with `@remotion/fonts` / `staticFile` (or `@remotion/google-fonts`, which has both).
- Sound: music in `public/audio/music.wav`; effects in `public/sfx` and `public/sfx2` (whoosh, pop, tick, click, riser, impact, sparkle, meow). Put a sound on every cut and type-on, mixed under the music. Fade the music out at the end.

## The bar

This should look like the work of a top motion designer: a showreel-grade piece, not a slideshow of screenshots.

- **One idea per beat.** Use big negative space and keep one line of words on screen at a time.
- **Motion with weight.** Use springs and eased curves, never linear.
  - Every entrance has anticipation or overshoot, and every exit is quicker than its entrance.
  - Stagger groups (the hub nodes, the bento tiles) so they read as one motion, not a pile of motions.
- **Match cuts and continuity.** Something carries each beat into the next: the thin line, the moonlight dot, the cat's eye, a tile that becomes the next frame.
- **Camera.** Slow pushes and pans on the product shots; screens tilt in gentle 3D (perspective, small rotateX/Y) and settle flat.
- **The opener (how it evolved)** must feel like each era.
  - Make 2006 deliberately clunky: stepped frames and a bevelled grey UI.
  - Make 2014 flat and cluttered, and 2021 slick but frantic.
  - Then everything collapses into one point, and the calm starts.
  - Build the old UIs as generic mock-ups: no real logos, no copied OS chrome.
- **Brand logos.** None anywhere. Where a node needs an icon, draw a simple pictogram.
- **Privacy.** Home's "Lately" tile shows real file names, so blur it, crop it out, or cover it with a made-up tile.
- **Colour.** Stay on the night-shift palette. Moonlight blue is the one accent; amber only for the cat's own moments.
- **Type.** Pixelify Sans for headlines, typed words and numbers. Manrope for anything small. Typed words get a blinking block cursor.

## How to work

- Build and preview by rendering stills of key frames, for example `npx remotion still ShikiAdV3 --frame=600 out/v3-check-600.png`.
  - Look at them and fix what looks off: crops, overlaps, text running off the frame, and pixel art that isn't crisp.
  - Check every beat at least once in both cuts.
- Run `npx tsc --noEmit` and make sure it passes.
- Don't render the full videos; that happens after you hand back.
- Touch only `src/v3/`, `src/Root.tsx` and new files under `public/v3/`.
  - Don't delete anything.
  - Don't use git.
  - Don't change the Shiki app.

When you're done, report back with:
- the files you made;
- each beat with its frame range in both cuts;
- what you'd still improve;
- anything in the footage that held you back.
