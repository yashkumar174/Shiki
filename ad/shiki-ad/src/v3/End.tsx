import React from "react";
import { AbsoluteFill } from "remotion";
import { C, Cat, Dot, Fx, PIXEL, POP, Pose, SANS, Sprite, Typed, inCubic, inOut, outQuint, spr, tw, useU, walkPose } from "./kit";
import { Span } from "./timing";

// The close: the dot opens into the thin line again, the cat walks along it, and the name
// stands up letter by letter behind it.

const LINE_Y = 640;
const WORD = "Shiki";
const WORD_X = 600;
const WORD_W = 540; // about how wide the name sets at this size
const SIZE = 236;
const STOP_X = WORD_X + WORD_W + 130;

export const End: React.FC<{ span: Span; reel?: boolean }> = ({ span, reel }) => {
  const u = useU(span);
  const dotY = tw(u, 0, 18, 540, LINE_Y, inOut);
  const half = tw(u, 6, 56, 0, 780, outQuint);
  const catX = tw(u, 4, 78, -130, STOP_X, (t) => 1 - Math.pow(1 - t, 1.6));
  const walking = u < 78;
  let pose: Pose = walkPose(u, 6);
  if (!walking) pose = u < 86 ? "Idle" : u >= 112 && u < 118 ? "Blink" : "Happy";
  const hop = walking ? Math.abs(Math.sin((u / 6) * Math.PI)) * 7 : 0;
  return (
    <AbsoluteFill>
      <svg width={1920} height={1080} style={{ position: "absolute" }}>
        <defs>
          <linearGradient id="v3-endline" x1="0" x2="1" y1="0" y2="0">
            <stop offset="0" stopColor={C.text} stopOpacity="0" />
            <stop offset="0.14" stopColor={C.text} stopOpacity="0.62" />
            <stop offset="0.86" stopColor={C.text} stopOpacity="0.62" />
            <stop offset="1" stopColor={C.text} stopOpacity="0" />
          </linearGradient>
        </defs>
        {half > 1 ? <rect x={960 - half} y={LINE_Y - 1} width={half * 2} height={2} fill="url(#v3-endline)" /> : null}
      </svg>
      <Dot x={960} y={dotY} r={6} opacity={tw(u, 12, 30, 1, 0, inCubic)} />

      {/* the name: each letter stands up out of the line once the cat has passed it */}
      <div style={{ position: "absolute", left: WORD_X, top: LINE_Y - SIZE - 12, height: SIZE + 4, width: WORD_W + 80, overflow: "hidden", display: "flex" }}>
        {WORD.split("").map((ch, i) => {
          const p = spr(u, 42 + i * 7, POP);
          return (
            <div key={i} style={{ fontFamily: PIXEL, fontWeight: 700, fontSize: SIZE, lineHeight: `${SIZE}px`, color: C.text, transform: `translateY(${(1 - p) * (SIZE + 20)}px)` }}>
              {ch}
            </div>
          );
        })}
      </div>

      <Cat pose={pose} x={catX} y={LINE_Y - hop} scale={8} />
      {u > 88 && u < 124 ? <Sprite name="mochi-float-Sparkle" w={3} h={3} scale={8} x={STOP_X + 78} y={LINE_Y - 190 - tw(u, 88, 124, 0, 30, outQuint)} opacity={tw(u, 108, 124, 1, 0, inCubic)} /> : null}

      <div style={{ position: "absolute", left: 0, right: 0, top: LINE_Y + 44, display: "flex", justifyContent: "center" }}>
        <Typed text="Say it. It happens." u={u} at={86} per={2} size={58} weight={500} accentFrom={18} cursor={u > 80} />
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: LINE_Y + 150, display: "flex", justifyContent: "center", opacity: tw(u, 114, 132, 0, 1, outQuint), transform: `translateY(${tw(u, 114, 136, 18, 0, outQuint)}px)` }}>
        <div style={{ fontFamily: SANS, fontWeight: 600, fontSize: 27, color: C.muted, letterSpacing: "0.04em", border: `1.5px solid ${C.line}`, borderRadius: 30, padding: "12px 30px", background: "rgba(20,26,39,0.6)" }}>Free for Windows</div>
      </div>

      <Fx span={span} at={4} name="whoosh2" v={0.24} />
      {WORD.split("")
        .map((_, i) => i)
        .filter((i) => !reel || i % 2 === 0)
        .map((i) => (
          <Fx key={i} span={span} at={42 + i * 7} name="pop" v={0.24} />
        ))}
      <Fx span={span} at={84} name="meow" v={0.36} />
      <Fx span={span} at={90} name="sparkle" v={0.2} />
    </AbsoluteFill>
  );
};
