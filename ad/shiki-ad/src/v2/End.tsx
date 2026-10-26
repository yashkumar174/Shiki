import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { backOut, c, easeIn, easeOut, sans, tween } from "../theme";
import { Burst, Chip, Clip, Confetti, Layout, Mesh, pick, shake, Shine, Slam, useLayout, wordTimes } from "./parts";

export const TAGLINE = 36;
// Paw prints stamp across the last montage card; each one opens a hole into the end card.
const STAMPS_WIDE = [
  { x: 330, y: 800, at: 0, r: -14 },
  { x: 760, y: 470, at: 5, r: 10 },
  { x: 1200, y: 760, at: 10, r: -8 },
  { x: 1620, y: 380, at: 15, r: 14 },
];
const STAMPS_TALL = [
  { x: 280, y: 1480, at: 0, r: -14 },
  { x: 780, y: 1160, at: 5, r: 10 },
  { x: 300, y: 760, at: 10, r: -8 },
  { x: 790, y: 420, at: 15, r: 14 },
];
export const STAMPS = STAMPS_WIDE; // same timing in both shapes
const stamps = (L: Layout) => (L.v ? STAMPS_TALL : STAMPS_WIDE);
const OPEN = 42; // after this the end card is fully shown

const Stamps: React.FC = () => {
  const frame = useCurrentFrame();
  const L = useLayout();
  if (frame > OPEN) return null;
  return (
    <>
      {stamps(L).map((st, i) => {
        const f = frame - st.at;
        if (f < 0) return null;
        return (
          <Img
            key={i}
            src={staticFile("cat/kuro-pawprint.png")}
            style={{
              position: "absolute",
              left: st.x - 65,
              top: st.y - 60,
              width: 130,
              height: 120,
              imageRendering: "pixelated",
              rotate: `${st.r}deg`,
              scale: `${tween(f, 0, 8, 1.6, 1, backOut) * (1 + tween(f, 0, 6, 0.15, 0))} ${tween(f, 0, 8, 1.6, 1, backOut) * (1 - tween(f, 0, 6, 0.15, 0))}`,
              opacity: tween(f, 0, 3, 0, 1) * tween(frame, OPEN - 12, OPEN, 1, 0),
              filter: "drop-shadow(0 10px 18px rgba(0,0,0,0.35))",
            }}
          />
        );
      })}
    </>
  );
};

/** Little prints walking along the bottom, left to right. */
const Walk: React.FC<{ from: number }> = ({ from }) => {
  const frame = useCurrentFrame();
  const L = useLayout();
  return (
    <>
      {Array.from({ length: 9 }, (_, k) => {
        const at = from + k * 7;
        const f = frame - at;
        if (f < 0) return null;
        return (
          <Img
            key={k}
            src={staticFile("cat/kuro-pawprint.png")}
            style={{
              position: "absolute",
              left: pick(L, 300 + k * 160, 90 + k * 110),
              top: pick(L, 985, 1700) + (k % 2 ? -26 : 0),
              width: 52,
              height: 48,
              imageRendering: "pixelated",
              rotate: "90deg",
              opacity: tween(f, 0, 4, 0, 0.85) * tween(f, 40, 70, 1, 0),
              scale: tween(f, 0, 8, 1.5, 1, backOut),
            }}
          />
        );
      })}
    </>
  );
};

export const End: React.FC = () => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const CATZ = pick(L, 7, 8);
  const words = wordTimes("sky-tagline", "Just say it. It's done.");
  const doneAt = TAGLINE + words[3].at;
  // The holes the paw prints punch through to this card, growing until they cover it.
  const mask =
    frame < OPEN
      ? stamps(L).map((st) => {
          const r = tween(frame, st.at + 2, st.at + 30, 0, 1500, easeIn);
          return `radial-gradient(circle at ${st.x}px ${st.y}px, #000 ${r}px, transparent ${r + 1}px)`;
        }).join(", ")
      : undefined;
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ overflow: "hidden", WebkitMaskImage: mask, maskImage: mask }}>
        <Mesh mode="night" intensity={1.3} />
        <AbsoluteFill style={{ translate: shake(frame, 16, 20), scale: tween(frame, 0, 300, 1, 1.05, (t) => t) }}>
          <div
            style={{
              position: "absolute",
              left: L.W / 2 - 31 * CATZ,
              top: pick(L, 30, 280),
              scale: tween(frame, 12, 30, 0.2, 1, backOut),
              transformOrigin: "50% 100%",
              filter: "drop-shadow(0 30px 60px rgba(0,0,0,0.6))",
            }}
          >
            {/* the real cat, mid-party */}
            <Clip name="cat" at={(f) => 84 + (f / 2) % 90} scale={CATZ} pixelated />
          </div>
          <Burst at={14} x={L.W / 2} y={pick(L, 300, 600)} />
          <Confetti at={18} x={L.W / 2} y={pick(L, 250, 540)} count={16} px={10} />
          <AbsoluteFill style={{ alignItems: "center", top: pick(L, 560, 900) }}>
            <Slam at={18} size={250} color={c.text}>
              <Shine at={70} base={c.text} dur={46}>
                Shiki
              </Shine>
            </Slam>
            <div style={{ display: "flex", flexDirection: L.v ? "column" : "row", alignItems: "center", gap: pick(L, 26, 6), marginTop: 10 }}>
              <Slam at={TAGLINE + words[0].at} size={pick(L, 96, 120)} color={c.text}>
                Just say it.
              </Slam>
              <Slam at={doneAt} size={pick(L, 96, 120)} italic color={c.amber}>
                It's done.
              </Slam>
            </div>
            <div style={{ marginTop: 44, opacity: tween(frame, 110, 130, 0, 1), translate: `0 ${tween(frame, 110, 135, 20, 0, easeOut)}px`, display: "flex", flexDirection: L.v ? "column" : "row", gap: 16, alignItems: "center" }}>
              <Chip>for Windows</Chip>
              <div style={{ fontFamily: sans, fontSize: 30, color: c.muted }}>Converts files right on your PC.</div>
            </div>
          </AbsoluteFill>
          <Walk from={140} />
        </AbsoluteFill>
      </AbsoluteFill>
      <Stamps />
    </AbsoluteFill>
  );
};
