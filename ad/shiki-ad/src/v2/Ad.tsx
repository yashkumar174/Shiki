import { Audio } from "@remotion/media";
import React from "react";
import { AbsoluteFill, interpolate, Sequence, staticFile } from "remotion";
import { Grain } from "../kit";
import { End, STAMPS, TAGLINE } from "./End";
import { Files, HOVER, PDF_LINE, SKY_SAVED } from "./Files";
import { Hook, PAW, tabAt, UGH } from "./Hook";
import { Meet } from "./Meet";
import { CUT, CUTS, Montage } from "./Montage";
import { Leak, LayoutCtx, s } from "./parts";
import { MUMMY, RAHUL, SKY_RAHUL, TAP, TWO, Voice } from "./Voice";
import { lines } from "./lines";

// Scenes, in 60 fps frames. 120 BPM: a beat is 30 frames, a bar 120; every cut is on the grid.
const HOOK = 0;
const MEET = s(4);
const VOICE = s(8);
const FILES = s(20);
const MONTAGE = s(26);
const END = s(30);
export const TOTAL = s(35);

type LineName = keyof typeof lines;
const SPOKEN: [LineName, number][] = [
  ["you-mummy", VOICE + MUMMY],
  ["sky-twothings", VOICE + TWO],
  ["you-rahul", VOICE + RAHUL],
  ["sky-rahul", VOICE + SKY_RAHUL],
  ["you-pdf", FILES + PDF_LINE],
  ["sky-saved", FILES + SKY_SAVED],
  ["sky-tagline", END + TAGLINE],
];

// The music steps back while anyone talks.
const musicVolume = (f: number) => {
  const duck = Math.max(
    ...SPOKEN.map(([n, at]) =>
      interpolate(
        f,
        [at - 8, at + 2, at + lines[n].frames, at + lines[n].frames + 14],
        [0, 1, 1, 0],
        { extrapolateLeft: "clamp", extrapolateRight: "clamp" },
      ),
    ),
  );
  return 0.62 * (1 - 0.55 * duck);
};

const Fx: React.FC<{ at: number; name: string; v?: number }> = ({
  at,
  name,
  v = 0.5,
}) => (
  <Sequence
    from={Math.max(0, at)}
    durationInFrames={s(2.2)}
    layout="none"
    name={`sfx ${name}`}
  >
    <Audio src={staticFile(`sfx2/${name}.wav`)} volume={v} />
  </Sequence>
);

/** The ad, 16:9 or, with `vertical`, 9:16 for Reels and Shorts (same timing and sound). */
export const Ad: React.FC<{ vertical?: boolean }> = ({ vertical = false }) => (
  <LayoutCtx.Provider
    value={
      vertical ? { W: 1080, H: 1920, v: true } : { W: 1920, H: 1080, v: false }
    }
  >
    <AbsoluteFill style={{ background: "#0D0B0A" }}>
      <Sequence
        from={HOOK}
        durationInFrames={MEET - HOOK}
        name="1 Ten tabs. Ugh."
      >
        <Hook />
      </Sequence>
      <Sequence from={MEET} durationInFrames={VOICE - MEET} name="2 Meet Shiki">
        <Meet />
      </Sequence>
      <Sequence
        from={VOICE}
        durationInFrames={FILES - VOICE}
        name="3 Say it (Mummy, Rahul)"
      >
        <Voice />
      </Sequence>
      <Sequence
        from={FILES}
        durationInFrames={MONTAGE - FILES}
        name="4 PDF under 2 MB"
      >
        <Files />
      </Sequence>
      <Sequence
        from={MONTAGE}
        durationInFrames={END - MONTAGE + 44}
        name="5 Montage"
      >
        <Montage />
      </Sequence>
      <Sequence from={END} durationInFrames={TOTAL - END} name="6 End card">
        <End />
      </Sequence>
      {/* warm light leaks over the big changes */}
      <Leak at={MEET - 4} />
      <Leak at={VOICE - 14} flip />
      <Leak at={FILES + 6} />
      <Leak at={MONTAGE - 10} flip />
      <Leak at={END + 10} dur={34} />
      <Grain opacity={0.07} />

      <Audio src={staticFile("v2/music.wav")} volume={musicVolume} />
      {SPOKEN.map(([n, at]) => (
        <Sequence
          key={n}
          from={at}
          durationInFrames={lines[n].frames + 10}
          layout="none"
          name={`voice ${n}`}
        >
          <Audio src={staticFile(`v2/${n}.wav`)} volume={0.85} />
        </Sequence>
      ))}

      {/* hook */}
      {Array.from({ length: 6 }, (_, i) => (
        <React.Fragment key={i}>
          <Fx at={HOOK + tabAt(i)} name="pop" v={0.4} />
          <Fx at={HOOK + tabAt(i)} name="click" v={0.35} />
        </React.Fragment>
      ))}
      <Fx at={HOOK + UGH} name="impact" v={0.4} />
      <Fx at={HOOK + PAW} name="whoosh3" v={0.45} />
      <Fx at={HOOK + PAW + 10} name="pop" v={0.3} />
      <Fx at={HOOK + PAW + 16} name="pop" v={0.25} />
      <Fx at={MEET - 80} name="revcym" v={0.45} />
      {/* the cat */}
      <Fx at={MEET} name="impact" v={0.4} />
      <Fx at={MEET + 10} name="meow" v={0.45} />
      <Fx at={MEET + s(1.4)} name="sparkle" v={0.45} />
      {Array.from({ length: 5 }, (_, i) => (
        <Fx
          key={i}
          at={MEET + s(1) + Math.round(i * 7.5)}
          name="click"
          v={0.3}
        />
      ))}
      <Fx at={MEET + s(3.55) - 10} name="whoosh-fast" v={0.5} />
      {/* voice */}
      <Fx at={VOICE + 6} name="pop" v={0.35} />
      <Fx at={VOICE + 238} name="pop" v={0.5} />
      <Fx at={VOICE + 238} name="whoosh2" v={0.3} />
      <Fx at={VOICE + TWO + 40} name="sparkle" v={0.3} />
      <Fx at={VOICE + TAP - 26} name="whoosh2" v={0.25} />
      <Fx at={VOICE + TAP} name="pop" v={0.5} />
      <Fx at={VOICE + TAP + 6} name="sparkle" v={0.35} />
      <Fx at={VOICE + RAHUL - 8} name="pop" v={0.35} />
      <Fx at={VOICE + SKY_RAHUL} name="click" v={0.4} />
      <Fx at={FILES - 40} name="whoosh3" v={0.4} />
      {/* files */}
      <Fx at={FILES + 34} name="impact" v={0.28} />
      <Fx at={FILES + 34} name="sparkle" v={0.45} />
      {[0, 1, 2].map((k) => (
        <Fx key={k} at={FILES + 40 + k * 4} name="click" v={0.3} />
      ))}
      <Fx at={FILES + 34 + HOVER} name="pop" v={0.3} />
      <Fx at={FILES + 78} name="click" v={0.35} />
      {Array.from({ length: 8 }, (_, i) => (
        <Fx key={i} at={FILES + 140 + i * 6} name="tick" v={0.3} />
      ))}
      <Fx at={FILES + 188} name="ding" v={0.55} />
      <Fx at={FILES + SKY_SAVED} name="meow" v={0.35} />
      <Fx at={FILES + SKY_SAVED + 6} name="sparkle" v={0.4} />
      <Fx at={FILES + SKY_SAVED + 40} name="click" v={0.3} />
      <Fx at={MONTAGE - 30} name="whoosh-fast" v={0.45} />
      {/* montage */}
      {CUTS.map((_, i) => (
        <React.Fragment key={i}>
          {i > 0 && <Fx at={MONTAGE + i * CUT - 9} name="whoosh2" v={0.3} />}
          <Fx at={MONTAGE + i * CUT} name="click" v={0.35} />
        </React.Fragment>
      ))}
      <Fx at={END - 80} name="revcym" v={0.45} />
      {/* end */}
      {STAMPS.map((st, i) => (
        <Fx key={i} at={END + st.at} name="pop" v={0.45} />
      ))}
      <Fx at={END + 14} name="impact" v={0.4} />
      <Fx at={END + 18} name="sparkle" v={0.5} />
      <Fx at={END + 70} name="sparkle" v={0.3} />
    </AbsoluteFill>
  </LayoutCtx.Provider>
);
