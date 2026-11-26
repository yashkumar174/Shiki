import React from "react";
import { AbsoluteFill, Easing, Img, Sequence, continueRender, delayRender, interpolate, staticFile, useCurrentFrame } from "remotion";
import { Audio } from "@remotion/media";
import { Span, uOf } from "./timing";

// ---------------------------------------------------------------- brand kit

export const C = {
  ink: "#0A0D14",
  panel: "#0E121B",
  card: "#141A27",
  line: "#222B3C",
  text: "#E9EDF3",
  muted: "#9AA5B8",
  moon: "#5AB9FC",
  deep: "#1971C2",
  amber: "#F2C14E",
};

export const PIXEL = "ShikiPixel";
export const SANS = "ShikiManrope";

// Shiki's own font files (public/v3/fonts), loaded once; rendering waits for them.
let fontsAsked = false;
const loadFonts = () => {
  if (fontsAsked || typeof document === "undefined") return;
  fontsAsked = true;
  const faces: [string, string, string][] = [
    [PIXEL, "v3/fonts/PixelifySans.ttf", "400 700"],
    [SANS, "v3/fonts/Manrope-Regular.ttf", "400"],
    [SANS, "v3/fonts/Manrope-Medium.ttf", "500"],
    [SANS, "v3/fonts/Manrope-SemiBold.ttf", "600"],
    [SANS, "v3/fonts/Manrope-Bold.ttf", "700"],
  ];
  for (const [family, file, weight] of faces) {
    const handle = delayRender(`font ${file}`);
    const face = new FontFace(family, `url(${staticFile(file)})`, { weight });
    face
      .load()
      .then(() => {
        document.fonts.add(face);
        continueRender(handle);
      })
      .catch(() => continueRender(handle));
  }
};
loadFonts();

// ---------------------------------------------------------------- motion

export const outQuint = Easing.bezier(0.16, 1, 0.3, 1);
export const outCubic = Easing.bezier(0.33, 1, 0.68, 1);
export const inCubic = Easing.bezier(0.55, 0, 0.9, 0.3);
export const inQuart = Easing.bezier(0.7, 0, 0.84, 0);
export const inOut = Easing.bezier(0.65, 0, 0.35, 1);
export const backOut = Easing.bezier(0.34, 1.56, 0.64, 1);
export const backIn = Easing.bezier(0.36, 0, 0.66, -0.56);

/** u in [a, b] maps to [from, to], clamped and eased. */
export const tw = (u: number, a: number, b: number, from: number, to: number, ease: (t: number) => number = outQuint) =>
  interpolate(u, [a, b], [from, to], { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing: ease });

type SpringCfg = { stiffness?: number; damping?: number; mass?: number };
/**
 * A spring from 0 to 1, started at `at` (in choreography frames, 60 to the second). Worked out
 * in closed form, so it is exact at any speed the beat is played at.
 */
export const spr = (u: number, at: number, cfg: SpringCfg = {}) => {
  const { stiffness = 170, damping = 16, mass = 1 } = cfg;
  const t = (u - at) / 60;
  if (t <= 0) return 0;
  const w0 = Math.sqrt(stiffness / mass);
  const z = damping / (2 * Math.sqrt(stiffness * mass));
  if (z < 1) {
    const wd = w0 * Math.sqrt(1 - z * z);
    return 1 - Math.exp(-z * w0 * t) * (Math.cos(wd * t) + ((z * w0) / wd) * Math.sin(wd * t));
  }
  return 1 - Math.exp(-w0 * t) * (1 + w0 * t);
};
/** Spring presets: a firm settle, a soft pop with overshoot, a heavy landing. */
export const SNAP: SpringCfg = { stiffness: 260, damping: 26 };
export const POP: SpringCfg = { stiffness: 240, damping: 15 };
export const HEAVY: SpringCfg = { stiffness: 120, damping: 13 };

export const mix = (a: number, b: number, t: number) => a + (b - a) * t;
export const clamp01 = (t: number) => Math.max(0, Math.min(1, t));

/** A steady random number in [0, 1) from a seed. */
export const rand = (seed: number) => {
  const x = Math.sin(seed * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
};

/** Choreography time of a beat for the current frame. */
export const useU = (span: Span) => uOf(useCurrentFrame(), span);

// ---------------------------------------------------------------- sound

/** A sound effect at a point of a beat's choreography. It lands on the same move at any speed. */
export const Fx: React.FC<{ span: Span; at: number; name: string; v?: number; dir?: "sfx" | "sfx2" }> = ({ span, at, name, v = 0.4, dir = "sfx2" }) => {
  if (span.dur <= 0) return null;
  const from = Math.max(0, Math.round(span.from + (at * span.dur) / span.auth));
  return (
    <Sequence from={from} durationInFrames={130} layout="none" name={`sfx ${name}`}>
      <Audio src={staticFile(`${dir}/${name}.wav`)} volume={v} />
    </Sequence>
  );
};

// ---------------------------------------------------------------- the ground

const GLYPHS: number[][][] = [
  // each glyph: [x, y] cells on a 5x5 grid: an ear, a paw, a plus, a dot pair, a whisker
  [[0, 2], [1, 1], [2, 0], [3, 1], [4, 2]],
  [[1, 0], [3, 0], [0, 2], [4, 2], [2, 2], [1, 3], [2, 3], [3, 3]],
  [[2, 1], [1, 2], [2, 2], [3, 2], [2, 3]],
  [[1, 2], [3, 2]],
  [[0, 1], [1, 2], [2, 2], [3, 2], [4, 3]],
  [[2, 2]],
];

/** Ink, a dim field of pixel glyphs (the cat's own alphabet) and a slow moonlight glow. */
export const Ground: React.FC<{ field?: number; glow?: number }> = ({ field = 1, glow = 1 }) => {
  const frame = useCurrentFrame();
  const t = frame / 60;
  const cell = 64;
  const px = 4;
  const cols = 31;
  const rows = 18;
  const glyphs = React.useMemo(() => {
    const out: { x: number; y: number; g: number; seed: number }[] = [];
    for (let r = 0; r < rows; r++)
      for (let c = 0; c < cols; c++) {
        const seed = r * 97 + c * 13;
        if (rand(seed) < 0.3) continue;
        out.push({ x: c * cell - 22, y: r * cell - 30, g: Math.floor(rand(seed + 5) * GLYPHS.length), seed });
      }
    return out;
  }, []);
  return (
    <AbsoluteFill style={{ background: C.ink, overflow: "hidden" }}>
      <div
        style={{
          position: "absolute",
          left: 960 + Math.sin(t * 0.35) * 90 - 800,
          top: 540 + Math.cos(t * 0.28) * 50 - 800,
          width: 1600,
          height: 1600,
          borderRadius: "50%",
          background: `radial-gradient(circle, rgba(90,185,252,0.13) 0%, rgba(25,113,194,0.06) 34%, transparent 62%)`,
          opacity: glow * (0.85 + 0.15 * Math.sin(t * 0.9)),
        }}
      />
      {field > 0.01 ? (
        <svg
          width={1920}
          height={1080}
          style={{
            position: "absolute",
            opacity: field,
            maskImage: "radial-gradient(ellipse 75% 75% at 50% 50%, #000 20%, transparent 100%)",
            WebkitMaskImage: "radial-gradient(ellipse 75% 75% at 50% 50%, #000 20%, transparent 100%)",
          }}
        >
          {glyphs.map((g, i) => {
            const tw1 = 0.5 + 0.5 * Math.sin(t * (0.5 + rand(g.seed + 1) * 1.1) + rand(g.seed + 2) * 6.28);
            const o = 0.05 + 0.11 * tw1 * tw1;
            return (
              <g key={i} transform={`translate(${g.x} ${g.y})`} fill={rand(g.seed + 3) < 0.16 ? C.moon : C.muted} opacity={o}>
                {GLYPHS[g.g].map(([cx, cy], k) => (
                  <rect key={k} x={cx * px} y={cy * px} width={px} height={px} />
                ))}
              </g>
            );
          })}
        </svg>
      ) : null}
      <AbsoluteFill style={{ background: "radial-gradient(ellipse at center, transparent 50%, rgba(4,6,10,0.7) 100%)" }} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- the cat

export type Pose =
  | "Idle"
  | "Blink"
  | "Happy"
  | "Listen"
  | "Talk"
  | "TapLeft"
  | "TapRight"
  | "LookLeft"
  | "LookRight"
  | "Pop"
  | "Startled"
  | "Purr"
  | "Yawn";

/**
 * A pixel cat. `x` is the middle of its feet and `y` is the ground it stands on. The art is 20x20,
 * drawn at a whole-number scale on whole pixels, so it stays crisp.
 */
export const Cat: React.FC<{ pose: Pose; x: number; y: number; scale?: number; cat?: string; opacity?: number; flip?: boolean }> = ({
  pose,
  x,
  y,
  scale = 10,
  cat = "mochi",
  opacity = 1,
  flip,
}) => (
  <Img
    src={staticFile(`v3/cat/${cat}-${pose}.png`)}
    style={{
      position: "absolute",
      left: Math.round(x - 10 * scale),
      top: Math.round(y - 20 * scale),
      width: 20 * scale,
      height: 20 * scale,
      imageRendering: "pixelated",
      opacity,
      transform: flip ? "scaleX(-1)" : undefined,
    }}
  />
);

/** Any other sprite from the cat sheet (head, paw pointer, sparkle), at a whole-number scale. */
export const Sprite: React.FC<{ name: string; w: number; h: number; x: number; y: number; scale: number; opacity?: number; style?: React.CSSProperties }> = ({
  name,
  w,
  h,
  x,
  y,
  scale,
  opacity = 1,
  style,
}) => (
  <Img
    src={staticFile(`v3/cat/${name}.png`)}
    style={{ position: "absolute", left: Math.round(x), top: Math.round(y), width: w * scale, height: h * scale, imageRendering: "pixelated", opacity, ...style }}
  />
);

/** The pose for a cat that is walking: paws alternate every `every` frames. */
export const walkPose = (u: number, every = 7): Pose => (Math.floor(u / every) % 2 === 0 ? "TapLeft" : "TapRight");

// ---------------------------------------------------------------- type

/**
 * Words typed on with a blinking block cursor. The whole line is laid out from the first
 * frame (untyped letters are invisible), so centred lines don't slide as they are typed.
 * `accentFrom` colours the letters from that index on (moonlight by default).
 */
export const Typed: React.FC<{
  text: string;
  u: number;
  at: number;
  per?: number;
  size: number;
  color?: string;
  font?: string;
  weight?: number;
  accentFrom?: number;
  accent?: string;
  cursor?: boolean;
  cursorUntil?: number;
  spacing?: string;
  style?: React.CSSProperties;
}> = ({ text, u, at, per = 3, size, color = C.text, font = PIXEL, weight = 600, accentFrom, accent = C.moon, cursor = true, cursorUntil, spacing, style }) => {
  const n = Math.max(0, Math.min(text.length, Math.floor((u - at) / per)));
  const typing = u >= at && n < text.length;
  const blinkOn = Math.floor((u - at) / 16) % 2 === 0;
  const showCursor = cursor && u >= at - 8 && (typing || blinkOn) && (cursorUntil === undefined || u < cursorUntil);
  const cut = accentFrom ?? text.length;
  const part = (from: number, to: number, visible: boolean) => {
    if (to <= from) return null;
    const plain = text.slice(from, Math.min(to, Math.max(from, cut)));
    const hot = text.slice(Math.max(from, Math.min(to, cut)), to);
    return (
      <span style={{ opacity: visible ? 1 : 0 }}>
        {plain}
        {hot ? <span style={{ color: accent }}>{hot}</span> : null}
      </span>
    );
  };
  return (
    <div style={{ fontFamily: font, fontWeight: weight, fontSize: size, lineHeight: 1.05, color, whiteSpace: "pre", letterSpacing: spacing, ...style }}>
      {part(0, n, true)}
      <span style={{ display: "inline-block", width: 0, height: size * 0.72, position: "relative", verticalAlign: "baseline" }}>
        <span
          style={{
            position: "absolute",
            left: size * 0.06,
            bottom: -size * 0.02,
            width: size * 0.42,
            height: size * 0.74,
            background: accentFrom !== undefined && n >= cut ? accent : color,
            opacity: showCursor ? 1 : 0,
          }}
        />
      </span>
      {part(n, text.length, false)}
    </div>
  );
};

// ---------------------------------------------------------------- small shapes

/** Rings pulsing out from a point. `p` is each ring's own 0..1 progress. */
export const Ring: React.FC<{ x: number; y: number; r: number; width?: number; color?: string; opacity?: number }> = ({ x, y, r, width = 2, color = C.moon, opacity = 1 }) =>
  r <= 0 || opacity <= 0.003 ? null : (
    <div
      style={{
        position: "absolute",
        left: x - r,
        top: y - r,
        width: r * 2,
        height: r * 2,
        borderRadius: "50%",
        border: `${width}px solid ${color}`,
        boxSizing: "border-box",
        opacity,
      }}
    />
  );

/** A soft-glowing dot: the moonlight point that carries from beat to beat. */
export const Dot: React.FC<{ x: number; y: number; r: number; color?: string; opacity?: number; glow?: number }> = ({ x, y, r, color = C.moon, opacity = 1, glow = 1 }) =>
  r <= 0 || opacity <= 0.003 ? null : (
    <div
      style={{
        position: "absolute",
        left: x - r,
        top: y - r,
        width: r * 2,
        height: r * 2,
        borderRadius: "50%",
        background: color,
        opacity,
        boxShadow: glow > 0 ? `0 0 ${r * 2.5 * glow}px ${r * 0.6 * glow}px rgba(90,185,252,0.45)` : undefined,
      }}
    />
  );

export type IconName = "chat" | "note" | "mail" | "photo" | "bell" | "cloud" | "folder" | "cable" | "clip";

/** Plain pictograms, drawn here: no brand marks anywhere. */
export const Icon: React.FC<{ name: IconName; size: number; color: string; stroke?: number }> = ({ name, size, color, stroke = 2 }) => {
  const p = { fill: "none", stroke: color, strokeWidth: stroke, strokeLinecap: "round" as const, strokeLinejoin: "round" as const };
  let body: React.ReactNode = null;
  if (name === "chat")
    body = (
      <>
        <path {...p} d="M4 6.5A2.5 2.5 0 0 1 6.5 4h11A2.5 2.5 0 0 1 20 6.5v7a2.5 2.5 0 0 1-2.5 2.5H11l-4.5 4v-4A2.5 2.5 0 0 1 4 13.5z" />
        <path {...p} d="M8.5 10h7" />
      </>
    );
  if (name === "note")
    body = (
      <>
        <path {...p} d="M9 17.5V6l10-2v11.5" />
        <circle {...p} cx="6.5" cy="17.5" r="2.5" />
        <circle {...p} cx="16.5" cy="15.5" r="2.5" />
      </>
    );
  if (name === "mail")
    body = (
      <>
        <rect {...p} x="3.5" y="5.5" width="17" height="13" rx="2.5" />
        <path {...p} d="M4 7.5l8 6 8-6" />
      </>
    );
  if (name === "photo")
    body = (
      <>
        <rect {...p} x="3.5" y="4.5" width="17" height="15" rx="2.5" />
        <circle {...p} cx="9" cy="10" r="1.6" />
        <path {...p} d="M4.5 17l4.5-4 3.5 3 3-2.5 4 3.5" />
      </>
    );
  if (name === "bell")
    body = (
      <>
        <path {...p} d="M6 16.5v-5a6 6 0 0 1 12 0v5l1.5 1.5h-15z" />
        <path {...p} d="M10 20.5a2.2 2.2 0 0 0 4 0" />
      </>
    );
  if (name === "cloud") body = <path {...p} d="M7 18.5a4 4 0 0 1-.6-7.95A5.5 5.5 0 0 1 17 9.6a4.5 4.5 0 0 1 .5 8.9z" />;
  if (name === "folder") body = <path {...p} d="M3.5 7A2 2 0 0 1 5.5 5h4l2 2.5h7a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" />;
  if (name === "cable")
    body = (
      <>
        <rect {...p} x="8" y="3.5" width="8" height="6" rx="1" />
        <path {...p} d="M10 9.5v3a2 2 0 0 0 4 0v-3M12 14.5V17a3 3 0 0 1-3 3H6" />
      </>
    );
  if (name === "clip") body = <path {...p} d="M19 11.5l-7.2 7.2a4.2 4.2 0 0 1-6-6l7.6-7.6a2.8 2.8 0 0 1 4 4l-7.5 7.5a1.4 1.4 0 0 1-2-2l6.8-6.8" />;
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" style={{ display: "block", flexShrink: 0 }}>
      {body}
    </svg>
  );
};

/** A plain arrow cursor for the mock UIs. */
export const Arrow: React.FC<{ x: number; y: number; size?: number; dark?: boolean }> = ({ x, y, size = 34, dark }) => (
  <svg width={size} height={size * 1.4} viewBox="0 0 20 28" style={{ position: "absolute", left: x, top: y }}>
    <path d="M2 2v20l5.2-4.6 3.4 8 3.2-1.4-3.4-7.8H18z" fill={dark ? "#111" : "#fff"} stroke={dark ? "#fff" : "#111"} strokeWidth={1.6} strokeLinejoin="round" />
  </svg>
);

/** A point on a cubic curve. */
export const cubic = (p0: number[], p1: number[], p2: number[], p3: number[], t: number) => {
  const m = 1 - t;
  return [
    m * m * m * p0[0] + 3 * m * m * t * p1[0] + 3 * m * t * t * p2[0] + t * t * t * p3[0],
    m * m * m * p0[1] + 3 * m * m * t * p1[1] + 3 * m * t * t * p2[1] + t * t * t * p3[1],
  ];
};
