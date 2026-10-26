import { Audio } from "@remotion/media";
import React from "react";
import { AbsoluteFill, interpolate, Sequence, staticFile, useCurrentFrame } from "remotion";
import { Grain } from "./kit";
import { Drop } from "./scenes/Drop";
import { End } from "./scenes/End";
import { Hook, tabAt } from "./scenes/Hook";
import { CAT_CENTER, Meet, POP } from "./scenes/Meet";
import { More } from "./scenes/More";
import * as V from "./scenes/Voice";
import * as D from "./scenes/Drop";
import { c, easeInOut, tween } from "./theme";
import { voice } from "./voice-data";

// Scene starts, all on the beat (18 frames) so cuts land with the music.
const S1 = 0;
const S2 = 108;
const S3 = 252;
const S4 = 540;
const S5 = 720;
const S6 = 864;
export const TOTAL = 1008;

// When the voices play, so the music can step back for them.
const SPEECH: [number, number][] = [
  [S3 + V.ASK, S3 + V.ASK + voice["you-ask"].frames],
  [S3 + V.SENDING, S3 + V.SENDING + voice["sky-sending"].frames],
  [S3 + V.DONE, S3 + V.DONE + voice["sky-done"].frames],
  [S4 + D.ASK, S4 + D.ASK + voice["you-pdf"].frames],
];
const musicVolume = (f: number) => {
  const duck = Math.max(
    ...SPEECH.map(([a, b]) => interpolate(f, [a - 6, a, b, b + 10], [0, 1, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })),
  );
  const fadeIn = interpolate(f, [0, 6], [0, 1], { extrapolateRight: "clamp" });
  return 0.62 * fadeIn * (1 - 0.55 * duck);
};

const Sfx: React.FC<{ at: number; name: string; volume?: number }> = ({ at, name, volume = 0.5 }) => (
  <Sequence from={at} durationInFrames={60} layout="none" name={`sfx ${name}`}>
    <Audio src={staticFile(`sfx/${name}.wav`)} volume={volume} />
  </Sequence>
);

const Line: React.FC<{ at: number; name: string }> = ({ at, name }) => (
  <Sequence from={at} durationInFrames={voice[name as keyof typeof voice].frames + 10} layout="none" name={`voice ${name}`}>
    <Audio src={staticFile(`audio/${name}.wav`)} volume={1} />
  </Sequence>
);

/** An amber circle grows out of the cat, with the next scene's night right behind it. */
const CatWipe: React.FC = () => {
  const frame = useCurrentFrame();
  const at = `${CAT_CENTER.x}px ${CAT_CENTER.y}px`;
  return (
    <>
      <AbsoluteFill style={{ background: c.amber, clipPath: `circle(${tween(frame, 0, 16, 0, 2300, easeInOut)}px at ${at})` }} />
      <AbsoluteFill style={{ background: c.night, clipPath: `circle(${tween(frame, 4, 18, 0, 2300, easeInOut)}px at ${at})` }} />
    </>
  );
};

export const ShikiAd: React.FC = () => {
  return (
    <AbsoluteFill style={{ background: c.paper }}>
      <Sequence from={S1} durationInFrames={S2 - S1 + 6} name="1 Ten tabs">
        <Hook />
      </Sequence>
      <Sequence from={S2} durationInFrames={S3 - S2} name="2 Meet Shiki">
        <Meet />
      </Sequence>
      <Sequence from={S3 - 18} durationInFrames={18} name="Cat wipe">
        <CatWipe />
      </Sequence>
      <Sequence from={S3} durationInFrames={S4 - S3 + 22} name="3 Just say it">
        <V.Voice />
      </Sequence>
      <Sequence from={S4} durationInFrames={S5 - S4 + 12} name="4 Drop it on the cat">
        <Drop />
      </Sequence>
      <Sequence from={S5} durationInFrames={S6 - S5 + 12} name="5 A whole lot more">
        <More />
      </Sequence>
      <Sequence from={S6} durationInFrames={TOTAL - S6} name="6 End card">
        <End />
      </Sequence>
      <Grain opacity={0.06} />

      {/* Sound */}
      <Audio src={staticFile("audio/music.wav")} volume={musicVolume} />
      {Array.from({ length: 10 }, (_, i) => (
        <Sfx key={i} at={S1 + tabAt(i)} name="mouse-click" volume={0.22 + i * 0.015} />
      ))}
      <Sfx at={S1 + 84} name="whoosh" volume={0.45} />
      <Sfx at={S2 + POP} name="whip" volume={0.3} />
      <Sfx at={S3 - 18} name="whoosh" volume={0.4} />
      <Line at={S3 + V.ASK} name="you-ask" />
      <Line at={S3 + V.SENDING} name="sky-sending" />
      <Sfx at={S3 + V.SENT} name="ding" volume={0.32} />
      <Line at={S3 + V.DONE} name="sky-done" />
      <Sfx at={S4} name="whoosh" volume={0.45} />
      <Sfx at={S4 + D.LAND} name="mouse-click" volume={0.4} />
      <Line at={S4 + D.ASK} name="you-pdf" />
      <Sfx at={S4 + D.PICK} name="switch" volume={0.3} />
      <Sfx at={S4 + D.READY} name="ding" volume={0.32} />
      <Sfx at={S5} name="whoosh" volume={0.4} />
    </AbsoluteFill>
  );
};
