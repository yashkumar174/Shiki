import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { c, easeIn, easeOut, sans, serif, tween } from "./theme";

// Sprite sizes in cat pixels, as Shiki draws them (CatSprites.Export).
const SIZE: Record<string, [number, number]> = {
  "float-Heart": [7, 6],
  "float-Sparkle": [3, 3],
  "float-Sweat": [3, 4],
  "float-Z": [4, 4],
  "float-Alert": [3, 7],
  pawprint: [13, 12],
  pawpointer: [12, 14],
};
const size = (name: string): [number, number] => SIZE[name] ?? (name.startsWith("peek-") ? [26, 7] : [20, 20]);

/** One of Shiki's own sprites (the black cat, Kuro), blown up with hard pixel edges. */
export const Sprite: React.FC<{ name: string; px: number; style?: React.CSSProperties }> = ({ name, px, style }) => {
  const [w, h] = size(name);
  return (
    <Img
      src={staticFile(`cat/kuro-${name}.png`)}
      style={{ width: w * px, height: h * px, imageRendering: "pixelated", display: "block", ...style }}
    />
  );
};

/** Open-eyed poses blink every few seconds, like the real cat. */
export const blinking = (frame: number, pose: string, offset = 0) =>
  (pose === "Idle" || pose === "Listen" || pose === "LookLeft" || pose === "LookRight") && (frame + offset) % 78 < 4
    ? "Blink"
    : pose;

/** The cat, breathing gently from its feet. */
export const Cat: React.FC<{ pose: string; px: number; style?: React.CSSProperties; squash?: number }> = ({
  pose,
  px,
  style,
  squash = 0,
}) => {
  const frame = useCurrentFrame();
  const breath = Math.sin((frame / 30) * Math.PI * 2 * 0.55) * 0.012;
  return (
    <div style={{ transformOrigin: "50% 100%", scale: `${1 + squash * 0.7} ${1 + breath - squash}`, ...style }}>
      <Sprite name={blinking(frame, pose)} px={px} />
    </div>
  );
};

/** A little sprite that floats up from the cat (heart, sparkle, sweat) and fades. */
export const Floater: React.FC<{ name: string; px: number; at: number; x: number; y: number; drift?: number; rise?: number }> = ({
  name,
  px,
  at,
  x,
  y,
  drift = 0,
  rise = 150,
}) => {
  const frame = useCurrentFrame();
  const f = frame - at;
  if (f < 0 || f > 46) return null;
  return (
    <div
      style={{
        position: "absolute",
        left: x,
        top: y,
        translate: `${tween(f, 0, 46, 0, drift)}px ${tween(f, 0, 46, 0, -rise)}px`,
        scale: tween(f, 0, 8, 0.3, 1, easeOut),
        opacity: tween(f, 30, 46, 1, 0, easeIn),
      }}
    >
      <Sprite name={name} px={px} />
    </div>
  );
};

/** Fine film grain over everything: the difference between flat and expensive. */
export const Grain: React.FC<{ opacity?: number }> = ({ opacity = 0.07 }) => {
  const frame = useCurrentFrame();
  const seed = Math.floor(frame / 2) % 9;
  return (
    <AbsoluteFill style={{ pointerEvents: "none", mixBlendMode: "overlay", opacity }}>
      <svg width="100%" height="100%">
        <filter id={`grain${seed}`}>
          <feTurbulence type="fractalNoise" baseFrequency="0.85" numOctaves="2" seed={seed} stitchTiles="stitch" />
        </filter>
        <rect width="100%" height="100%" filter={`url(#grain${seed})`} />
      </svg>
    </AbsoluteFill>
  );
};

/** A soft warm light, like the glow behind the cat on Shiki's home card. */
export const Glow: React.FC<{ x: string; y: string; size: number; opacity?: number; color?: string }> = ({
  x,
  y,
  size: s,
  opacity = 0.5,
  color = c.amber,
}) => (
  <div
    style={{
      position: "absolute",
      left: x,
      top: y,
      width: s,
      height: s,
      translate: "-50% -50%",
      borderRadius: "50%",
      background: `radial-gradient(circle, ${color} 0%, transparent 65%)`,
      opacity,
      filter: "blur(30px)",
    }}
  />
);

export type Part = { t: string; italic?: boolean; color?: string };

/** A serif line whose words rise out of a mask one after another. */
export const RevealLine: React.FC<{
  parts: Part[];
  at: number;
  size: number;
  color: string;
  stagger?: number;
  outAt?: number;
  style?: React.CSSProperties;
}> = ({ parts, at, size: fontSize, color, stagger = 4, outAt, style }) => {
  const frame = useCurrentFrame();
  const words: (Part & { i: number })[] = [];
  parts.forEach((p) => p.t.split(" ").filter(Boolean).forEach((w) => words.push({ ...p, t: w, i: words.length })));
  return (
    <div style={{ fontFamily: serif, fontSize, lineHeight: 1.02, color, letterSpacing: "-0.01em", display: "flex", flexWrap: "wrap", ...style }}>
      {words.map((w) => {
        const f = frame - at - w.i * stagger;
        const out = outAt === undefined ? 0 : tween(frame, outAt + w.i * 2, outAt + w.i * 2 + 10, 0, 1, easeIn);
        return (
          <span key={w.i} style={{ overflow: "hidden", display: "inline-block", paddingBottom: "0.12em", marginBottom: "-0.12em", marginRight: "0.24em" }}>
            <span
              style={{
                display: "inline-block",
                fontStyle: w.italic ? "italic" : "normal",
                color: w.color ?? color,
                translate: `0 ${tween(f, 0, 20, 105, 0) - out * 105}%`,
                rotate: `${tween(f, 0, 20, 4, 0)}deg`,
              }}
            >
              {w.t}
            </span>
          </span>
        );
      })}
    </div>
  );
};

/** Plain text that fades and lifts in. */
export const Rise: React.FC<{ at: number; outAt?: number; children: React.ReactNode; style?: React.CSSProperties; dist?: number }> = ({
  at,
  outAt,
  children,
  style,
  dist = 24,
}) => {
  const frame = useCurrentFrame();
  const out = outAt === undefined ? 0 : tween(frame, outAt, outAt + 9, 0, 1, easeIn);
  return (
    <div
      style={{
        opacity: tween(frame, at, at + 16, 0, 1) * (1 - out),
        translate: `0 ${tween(frame, at, at + 22, dist, 0) - out * 12}px`,
        ...style,
      }}
    >
      {children}
    </div>
  );
};

/** Subtitles for whoever is talking, so the ad works with the sound off too. */
export const Caption: React.FC<{ who: string; text: string; from: number; to: number; dark?: boolean }> = ({ who, text, from, to, dark = true }) => {
  const frame = useCurrentFrame();
  if (frame < from - 2 || frame > to + 10) return null;
  const o = tween(frame, from, from + 8, 0, 1) * tween(frame, to, to + 8, 1, 0, easeIn);
  return (
    <div style={{ position: "absolute", left: 0, right: 0, bottom: 74, display: "flex", justifyContent: "center", opacity: o }}>
      <div
        style={{
          fontFamily: sans,
          fontSize: 36,
          fontWeight: 500,
          color: dark ? c.text : c.ink,
          background: dark ? "rgba(10,9,8,0.72)" : "rgba(255,255,255,0.85)",
          border: `1px solid ${dark ? c.nightLine : c.paperLine}`,
          padding: "14px 26px",
          borderRadius: 18,
          translate: `0 ${tween(frame, from, from + 12, 10, 0)}px`,
        }}
      >
        <span style={{ color: c.amber, fontWeight: 600, marginRight: 14 }}>{who}</span>
        {text}
      </div>
    </div>
  );
};

/** Loudness of a voice line at a video frame (0 before or after it). */
export const level = (env: readonly number[], f: number) => (f >= 0 && f < env.length ? env[Math.floor(f)] : 0);
