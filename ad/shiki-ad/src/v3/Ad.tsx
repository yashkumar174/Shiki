import React from "react";
import { AbsoluteFill, Sequence, interpolate, staticFile, useCurrentFrame } from "remotion";
import { Audio } from "@remotion/media";
import { Calm } from "./Calm";
import { End } from "./End";
import { Ground } from "./kit";
import { Opener } from "./Opener";
import { Bento, Cats, ChatDone, Levels, Phone, Pill, Pills } from "./Product";
import { CUTS, CutName, FPS, sceneOf } from "./timing";

/** Shiki ad V3, the night shift. `cut` picks the 39 s main cut or the 15 s showreel; the scenes are the same. */
export const AdV3: React.FC<{ cut?: CutName }> = ({ cut = "main" }) => {
  const frame = useCurrentFrame();
  const reel = cut === "reel";
  const total = CUTS[cut].seconds * FPS;
  const musicFrom = Math.round(CUTS[cut].musicFrom * FPS);

  const opener = sceneOf(cut, ["opener"]);
  const calm = sceneOf(cut, ["line", "drop", "type", "hub"]);
  const pill = sceneOf(cut, ["pill"]);
  const chat = sceneOf(cut, ["chat", "done"]);
  const bento = sceneOf(cut, ["bento"]);
  const pills = sceneOf(cut, ["pills"]);
  const cats = sceneOf(cut, ["cats"]);
  const levels = sceneOf(cut, ["levels"]);
  const phone = sceneOf(cut, ["phone"]);
  const end = sceneOf(cut, ["end"]);

  // the music comes in as the old way collapses, and fades under the logo
  const musicLen = total - musicFrom;
  const music = (f: number) => 0.5 * interpolate(f, [0, 30, musicLen - 100, musicLen - 4], [0, 1, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });

  return (
    <AbsoluteFill style={{ background: "#0A0D14" }}>
      <Ground field={frame < opener.dur - 40 ? 0 : 1} />
      <Sequence from={opener.from} durationInFrames={opener.dur} name="0 How it evolved">
        <Opener span={opener.spans.opener} reel={reel} />
      </Sequence>
      <Sequence from={calm.from} durationInFrames={calm.dur} name="1-4 Line, cat, promise, hub">
        <Calm spans={calm.spans} reel={reel} />
      </Sequence>
      <Sequence from={pill.from} durationInFrames={pill.dur} name="5 Voice pill">
        <Pill span={pill.spans.pill} reel={reel} />
      </Sequence>
      <Sequence from={chat.from} durationInFrames={chat.dur} name="6-7 Chat card, done.">
        <ChatDone spans={chat.spans} reel={reel} />
      </Sequence>
      <Sequence from={bento.from} durationInFrames={bento.dur} name="8 Home assembles">
        <Bento span={bento.spans.bento} reel={reel} />
      </Sequence>
      <Sequence from={pills.from} durationInFrames={pills.dur} name="9 Pills">
        <Pills span={pills.spans.pills} reel={reel} />
      </Sequence>
      <Sequence from={cats.from} durationInFrames={cats.dur} name="10 Cats and a name">
        <Cats span={cats.spans.cats} reel={reel} />
      </Sequence>
      <Sequence from={levels.from} durationInFrames={levels.dur} name="11 Levels walk">
        <Levels span={levels.spans.levels} exitX={phone.dur > 0 ? 560 : 960} reel={reel} />
      </Sequence>
      {phone.dur > 0 ? (
        <Sequence from={phone.from} durationInFrames={phone.dur} name="12 iPhone">
          <Phone span={phone.spans.phone} />
        </Sequence>
      ) : null}
      <Sequence from={end.from} durationInFrames={end.dur} name="13 Logo">
        <End span={end.spans.end} reel={reel} />
      </Sequence>

      <Sequence from={musicFrom} layout="none" name="music">
        <Audio src={staticFile("audio/music.wav")} volume={music} />
      </Sequence>
    </AbsoluteFill>
  );
};
