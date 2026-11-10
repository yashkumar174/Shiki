# Shiki ad V3: style sheet from the two reference reels

Both references are landscape **1920×1080**, so V3 is 16:9 too (a 9:16 cut can come later).

## Reel 1: "Upgrade your SaaS" (zae.vf), 10 s

- **Pace:** very fast, a new beat every 0.7–1 s.
- **Ground:** near-black with a dim dot-matrix / glyph field; a glowing gradient orb (lime into violet) breathes in the centre.
- **Type:** one word at a time, typed with a blinking block cursor ("SaaS.", "Built", "This isn't…"), white, medium weight, centred.
- **Product:** small screenshots and UI cards orbit the word in a ring, then sweep in and out.
- **The turn:** a hard cut to a bright photographic backdrop (green hills, sky), where a **bento grid of UI cards assembles** tile by tile.
- **Accents:** neon lime bars and magenta sparkles, used sparingly.
- **Ending:** a ">" chevron and two lime light bars over a field of pixel glyphs.

## Reel 2: OpenAI motion concept (anmoldoesmotion), 31 s

- **Pace:** calm, a beat every 2–3 s, lots of empty space.
- **Opening:** one thin horizontal line, with a tiny label in its middle ("‹ this means signals ›").
- **The metaphor:** a glowing blue dot draws a squiggle, then **many thin curves fan out from it**, each ending in a small labelled node ("12 new messages", "research_final", "meeting at 5 pm"). One hub, everything it touches.
- **Transition:** blue concentric rings pulse outward, turn white, with the word "connected." in the middle.
- **Brand:** a clean white field, the name in one word.
- **Product close-up:** the chat composer pill, with its mic and a blue round send arrow, filling the frame.
- **Type:** a single word with an accent dot ("Seconds.").
- **The emotional beat:** a photographic nature backdrop (white flowers, blue sky) with "One place to **Build**", the last word in the accent colour.
- **Ending:** logo on the backdrop, then back to the thin line.

## What V3 takes from them

1. **One idea per beat**, big negative space, a single word or short line on screen at a time.
2. **Typed words with a blinking cursor**, set in Shiki's own pixel face (Pixelify Sans) instead of a neutral sans.
3. **The hub metaphor from reel 2, made Shiki's:** curves fan out from **the cat** to labelled task nodes ("WhatsApp Mummy", "play Seedhe Maut", "email the invoice", "3 photos from iPhone"). The cat is the hub.
4. **Product as floating cards** (real Shiki windows, crisp), orbiting or sweeping like reel 1.
5. **The turn to a backdrop:** instead of hills or flowers, **Shiki's own night window**: a deep night sky with pixel stars and a crescent moon. The Home board's tiles assemble over it like reel 1's bento.
6. **Accents:** moonlight blue `#5AB9FC` as the one accent word or dot; cat-eye amber `#F2C14E` only for the cat's own moments. No lime, no magenta, no violet.
7. **Ground:** ink `#0A0D14` with a dim pixel-glyph field (Shiki's pixel cat language) in place of the dot matrix.
8. **Ending:** the thin line returns; the cat walks along it; logo.

## Brand kit

- **Colours:** night `#0A0D14`, panel `#0E121B`, card `#141A27`, line `#222B3C`, text `#E9EDF3`, muted `#9AA5B8`, moonlight `#5AB9FC`, deep blue `#1971C2`, cat-eye amber `#F2C14E`.
  - Cat card colours: Mochi `#F0913A`, Latte `#C9A27A`, Sky `#9CC6F2`, Kuro `#1E2433`, Ginger `#E8562A`, Ash `#4F7A6B`, Sumi `#E9E4D8`, Maple `#7A3E22`, Biscuit `#F2C14E`.
- **Type:** Pixelify Sans (headlines, typed words, numbers); Manrope (anything small).
  - Both are in `src/Shiki/Assets/Fonts`.
- **Pixel art** stays crisp: nearest-neighbour scaling, whole-number scale factors only.
