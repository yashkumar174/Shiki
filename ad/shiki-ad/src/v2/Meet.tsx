import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { backOut, c, easeIn, easeOut, sans, tween } from "../theme";
import { Burst, Chip, Clip, Confetti, Layout, Mesh, s, shake, Slam, useLayout, Whip } from "./parts";

// Where things go in 16:9, and stacked for 9:16.
const lay = (L: Layout) =>
  L.v
    ? {
        cat: { x: 540, y: 690, z: 9 },
        word: { left: 0, right: 0, top: 1040, justifyContent: "center" },
        tag: { left: 0, right: 0, top: 1390, textAlign: "center" as const },
        chip: { left: 0, right: 0, top: 1490, display: "flex", justifyContent: "center", transformOrigin: "50% 50%" },
        sparkles: [[230, 1060, 0], [840, 1030, 14], [880, 1300, 28], [170, 1300, 40]],
      }
    : {
        cat: { x: 560, y: 560, z: 9 },
        word: { left: 1000, top: 330 },
        tag: { left: 1010, top: 680 },
        chip: { left: 1010, top: 780, transformOrigin: "0 50%" },
        sparkles: [[990, 330, 0], [1640, 300, 14], [1700, 560, 28], [960, 600, 40]],
      };

export const Meet: React.FC = () => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const P = lay(L);
  const CAT = P.cat;
  const out = tween(frame, s(3.55), s(4), 0, 1, easeIn);
  const pop = tween(frame, 0, 18, 0.15, 1, backOut);
  return (
    <AbsoluteFill style={{ overflow: "hidden" }}>
      <Mesh mode="night" />
      <Whip amount={out * 70}>
        <AbsoluteFill style={{ translate: `${-out * (L.W + 380)}px 0`, rotate: `${-out * 3}deg` }}>
          <AbsoluteFill style={{ translate: shake(frame, 0, 26) }}>
            {/* The real cat, recorded on the desktop: startled, then a party. */}
            <div
              style={{
                position: "absolute",
                left: CAT.x - (62 * CAT.z) / 2,
                top: CAT.y - (74 * CAT.z) / 2,
                scale: `${pop * (1 + tween(frame, 18, 30, 0.06, 0))} ${pop * (1 - tween(frame, 18, 30, 0.06, 0))}`,
                transformOrigin: "50% 100%",
                filter: "drop-shadow(0 40px 60px rgba(0,0,0,0.5))",
              }}
            >
              <Clip name="cat" at={(f) => 40 + f / 2} scale={CAT.z} pixelated />
            </div>
            <Burst at={0} x={CAT.x} y={CAT.y} />
            {/* sparkles twinkling around the wordmark */}
            {P.sparkles.map(([x, y, d], i) => {
              const t = (frame - s(1.5) - d) % 56;
              if (frame < s(1.5) + d) return null;
              const k = Math.sin((Math.max(0, t) / 56) * Math.PI);
              return (
                <Img
                  key={i}
                  src={staticFile("cat/kuro-float-Sparkle.png")}
                  style={{ position: "absolute", left: x, top: y, width: 3 * 14, height: 3 * 14, imageRendering: "pixelated", scale: k, rotate: `${t * 2}deg`, opacity: k }}
                />
              );
            })}
            <Confetti at={s(1.4)} x={CAT.x} y={CAT.y - 120} count={12} px={9} />
            <div style={{ position: "absolute", display: "flex", ...P.word }}>
              {"Shiki".split("").map((ch, i) => (
                <Slam key={i} at={s(1) + Math.round(i * 7.5)} size={300} color={c.text}>
                  {ch}
                </Slam>
              ))}
            </div>
            <div
              style={{
                position: "absolute",
                ...P.tag,
                fontFamily: sans,
                fontSize: 52,
                fontWeight: 500,
                color: c.muted,
                opacity: tween(frame, s(1.9), s(2.2), 0, 1),
                translate: `0 ${tween(frame, s(1.9), s(2.3), 30, 0, easeOut)}px`,
              }}
            >
              A tiny cat that{" "}
              <span style={{ color: c.amber, position: "relative", display: "inline-block" }}>
                runs your PC.
                {/* a hand-drawn swoosh underneath, drawing itself in */}
                <svg width="100%" height="30" viewBox="0 0 300 30" preserveAspectRatio="none" style={{ position: "absolute", left: 0, top: "100%", overflow: "visible" }}>
                  <path
                    d="M4 18 C 70 6, 150 4, 296 14"
                    fill="none"
                    stroke={c.amber}
                    strokeWidth={5}
                    strokeLinecap="round"
                    pathLength={1}
                    strokeDasharray={1}
                    strokeDashoffset={tween(frame, s(2.25), s(2.6), 1, 0, easeOut)}
                  />
                </svg>
              </span>
            </div>
            <div style={{ position: "absolute", ...P.chip, opacity: tween(frame, s(2.4), s(2.6), 0, 1), scale: tween(frame, s(2.4), s(2.7), 0.6, 1, backOut) }}>
              <Chip>for Windows</Chip>
            </div>
          </AbsoluteFill>
        </AbsoluteFill>
      </Whip>
    </AbsoluteFill>
  );
};
