# Shiki ad

Two cuts, both in the Remotion project `shiki-ad/`:

- **ShikiAdV2** (the current one): 35 s, 1920×1080, 60 fps. Real recordings of Shiki's windows, Indian English voices, a 120 BPM track, about 25 cuts. Scenes are in `src/v2/`, the timeline and sound in `src/v2/Ad.tsx`.
- **ShikiAd** (the first try): 33 s, 30 fps, sprites only. In `src/scenes/`.

## Making the material again

- `tools/record.py <shots>` records Shiki's own windows one at a time (ffmpeg `gfxcapture`), so nothing else on screen gets in. Toasts and the wheel only show up in recordings with Settings › LiveGlass off; turn it back on afterwards.
- `tools/process.py <name> <fps> [cat]` turns a recording into a PNG sequence in `shiki-ad/public/rec/`. The cat is resampled to Windows' unscaled pixels so its pixel art stays crisp.
- `tools/eleven.py` uses the ElevenLabs key Shiki keeps in Credential Manager: voice lines (`tts`, library voices Bunty for "you", Anika for Sky, model `eleven_multilingual_v2`) and sound effects (`sfx`). The key can't make music, so `tools/music2.py` builds the track from ElevenLabs one-shots.
- Check how a line is heard with `Shiki.exe hear-file <wav>` (it only transcribes). Write "Whats-app" so it isn't heard as "what's up".

```bash
cd shiki-ad
npm run dev                     # Studio preview
npx remotion render ShikiAdV2 out/shiki-ad-v2.mp4 --crf=17
```

Remotion is free for individuals and companies of up to 3 people; bigger companies need a licence.
