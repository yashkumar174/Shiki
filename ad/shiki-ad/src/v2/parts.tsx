import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { backOut, c, easeIn, easeOut, pixel, sans, serif, tween } from "../theme";
import { lines } from "./lines";

export const FPS = 60;

/** The frame being made: 16:9 (1920x1080) or 9:16 for Reels and Shorts (1080x1920). Scenes lay out from it. */
export type Layout = { W: number; H: number; v: boolean };
export const LayoutCtx = React.createContext<Layout>({ W: 1920, H: 1080, v: false });
export const useLayout = () => React.useContext(LayoutCtx);
/** Pick the 16:9 or the 9:16 value. */
export const pick = <T,>(L: Layout, wide: T, tall: T) => (L.v ? tall : wide);
/** Seconds to frames at 60 fps. */
export const s = (sec: number) => Math.round(sec * FPS);
export const smooth = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

// The recordings, as processed by tools/process.py (frames, frame rate, size in image pixels).
export const CLIPS = {
  cat: { frames: 519, fps: 30, w: 62, h: 74 },
  "shrink-cat": { frames: 302, fps: 30, w: 62, h: 74 },
  chat: { frames: 575, fps: 60, w: 745, h: 602 },
  "shrink-win1": { frames: 630, fps: 60, w: 465, h: 220 },
  "shrink-win2": { frames: 230, fps: 60, w: 332, h: 74 },
  wheel: { frames: 9, fps: 30, w: 550, h: 550 },
  wheel2: { frames: 15, fps: 30, w: 550, h: 550 },
} as const;
export type ClipName = keyof typeof CLIPS;

/**
 * A real recording of one of Shiki's windows. `at` says which recorded frame shows at each
 * frame of the scene (so a clip can be sped up, held or jumped).
 */
export const Clip: React.FC<{ name: ClipName; at: (f: number) => number; scale?: number; pixelated?: boolean; style?: React.CSSProperties }> = ({
  name,
  at,
  scale = 1,
  pixelated,
  style,
}) => {
  const frame = useCurrentFrame();
  const clip = CLIPS[name];
  const i = Math.max(0, Math.min(clip.frames - 1, Math.round(at(frame))));
  return (
    <Img
      src={staticFile(`rec/${name}/f${String(i).padStart(4, "0")}.png`)}
      style={{ width: clip.w * scale, height: clip.h * scale, display: "block", imageRendering: pixelated ? "pixelated" : "auto", ...style }}
    />
  );
};

/** Clip time from scene time: piecewise-linear through [sceneFrame, clipFrame] points. */
export const timeMap = (points: [number, number][]) => (f: number) => {
  if (f <= points[0][0]) return points[0][1];
  for (let k = 1; k < points.length; k++) {
    const [a, x] = points[k - 1];
    const [b, y] = points[k];
    if (f <= b) return x + ((f - a) / (b - a || 1)) * (y - x);
  }
  return points[points.length - 1][1];
};

/** Drifting soft light, warm: the backdrop for every scene. */
export const Mesh: React.FC<{ mode: "night" | "cream"; intensity?: number }> = ({ mode, intensity = 1 }) => {
  const frame = useCurrentFrame();
  const t = frame / FPS;
  const night = mode === "night";
  const blobs = night
    ? [
        { color: c.amber, x: 30, y: 70, r: 1100, o: 0.34, sx: 6, sy: 4, p: 0 },
        { color: c.coral, x: 78, y: 28, r: 900, o: 0.2, sx: 5, sy: 7, p: 2 },
        { color: "#8A4B2A", x: 60, y: 90, r: 1200, o: 0.35, sx: 7, sy: 3, p: 4 },
      ]
    : [
        { color: "#FFD3A1", x: 25, y: 30, r: 1200, o: 0.8, sx: 6, sy: 5, p: 0 },
        { color: "#F9B9A6", x: 80, y: 75, r: 1100, o: 0.55, sx: 5, sy: 6, p: 3 },
        { color: c.amber, x: 70, y: 20, r: 800, o: 0.3, sx: 7, sy: 4, p: 5 },
      ];
  return (
    <AbsoluteFill style={{ background: night ? "#0D0B0A" : "#F6F0E7", overflow: "hidden" }}>
      {blobs.map((b, i) => (
        <div
          key={i}
          style={{
            position: "absolute",
            left: `${b.x + Math.sin(t * 0.5 + b.p) * b.sx}%`,
            top: `${b.y + Math.cos(t * 0.4 + b.p) * b.sy}%`,
            width: b.r,
            height: b.r,
            translate: "-50% -50%",
            borderRadius: "50%",
            background: `radial-gradient(circle, ${b.color} 0%, transparent 62%)`,
            opacity: b.o * intensity,
          }}
        />
      ))}
      <AbsoluteFill style={{ background: night ? "radial-gradient(ellipse at center, transparent 45%, rgba(0,0,0,0.55) 100%)" : "radial-gradient(ellipse at center, transparent 55%, rgba(120,80,40,0.12) 100%)" }} />
    </AbsoluteFill>
  );
};

/** Horizontal motion blur for whip pans (an SVG blur that only smears sideways). */
export const Whip: React.FC<{ amount: number; children: React.ReactNode; style?: React.CSSProperties }> = ({ amount, children, style }) => {
  const frame = useCurrentFrame();
  const id = `whip-${frame}-${Math.round(amount)}`;
  if (amount < 0.5) return <AbsoluteFill style={style}>{children}</AbsoluteFill>;
  return (
    <AbsoluteFill style={{ filter: `url(#${id})`, ...style }}>
      <svg width={0} height={0} style={{ position: "absolute" }}>
        <filter id={id} x="-20%" y="0" width="140%" height="100%">
          <feGaussianBlur stdDeviation={`${amount} 0`} />
        </filter>
      </svg>
      {children}
    </AbsoluteFill>
  );
};

/** A kick of camera shake that settles over a third of a second. */
export const shake = (frame: number, at: number, strength = 14) => {
  const f = frame - at;
  if (f < 0 || f > 20) return "0px 0px";
  const k = strength * Math.pow(1 - f / 20, 2);
  return `${Math.sin(f * 2.7) * k}px ${Math.cos(f * 3.3) * k}px`;
};

/** Camera: put scene point (x, y) of a 1920x1080 scene in the middle of a W x H frame at scale `z`. */
export const cam = (x: number, y: number, z: number, L: Layout, extra?: React.CSSProperties): React.CSSProperties => ({
  position: "absolute",
  left: 0,
  top: 0,
  width: 1920,
  height: 1080,
  transformOrigin: "0 0",
  translate: `${L.W / 2 - x * z}px ${L.H / 2 - y * z}px`,
  scale: z,
  ...extra,
});

/** Big words that land hard: in from oversized and blurred, a little overshoot. */
export const Slam: React.FC<{
  at: number;
  size: number;
  children: React.ReactNode;
  color?: string;
  italic?: boolean;
  font?: string;
  outAt?: number;
  style?: React.CSSProperties;
}> = ({ at, size, children, color = c.text, italic, font = serif, outAt, style }) => {
  const frame = useCurrentFrame();
  const f = frame - at;
  if (f < 0) return null;
  const out = outAt === undefined ? 0 : tween(frame, outAt, outAt + 8, 0, 1, easeIn);
  return (
    <div
      style={{
        fontFamily: font,
        fontStyle: italic ? "italic" : "normal",
        fontSize: size,
        lineHeight: 1,
        color,
        letterSpacing: font === serif ? "-0.015em" : "-0.01em",
        whiteSpace: "nowrap",
        scale: tween(f, 0, 14, 1.45, 1, backOut) * (1 - out * 0.12),
        opacity: tween(f, 0, 5, 0, 1) * (1 - out),
        filter: `blur(${tween(f, 0, 9, 14, 0)}px)`,
        ...style,
      }}
    >
      {children}
    </div>
  );
};

/** An expanding ring and a flash, for the big hits. */
export const Burst: React.FC<{ at: number; x: number; y: number; color?: string }> = ({ at, x, y, color = c.amber }) => {
  const frame = useCurrentFrame();
  const f = frame - at;
  if (f < 0 || f > 40) return null;
  const r = tween(f, 0, 36, 40, 1500, easeOut);
  return (
    <>
      <div
        style={{
          position: "absolute",
          left: x - r,
          top: y - r,
          width: r * 2,
          height: r * 2,
          borderRadius: "50%",
          border: `${tween(f, 0, 36, 40, 2)}px solid ${color}`,
          opacity: tween(f, 0, 36, 0.9, 0),
        }}
      />
      <AbsoluteFill style={{ background: "#FFF6E8", opacity: tween(f, 0, 10, 0.55, 0), mixBlendMode: "screen" }} />
    </>
  );
};

/** Pixel hearts and sparkles that pop out of a point (Shiki's own floaters). */
export const Confetti: React.FC<{ at: number; x: number; y: number; count?: number; px?: number }> = ({ at, x, y, count = 10, px = 8 }) => {
  const frame = useCurrentFrame();
  const f = frame - at;
  if (f < 0 || f > 80) return null;
  return (
    <>
      {Array.from({ length: count }, (_, i) => {
        const ang = (i / count) * Math.PI * 2 + i * 0.7;
        const dist = 160 + ((i * 53) % 140);
        const k = tween(f, 0, 40, 0, 1, easeOut);
        const name = i % 3 === 0 ? "float-Sparkle" : "float-Heart";
        const size = name === "float-Heart" ? [7, 6] : [3, 3];
        return (
          <Img
            key={i}
            src={staticFile(`cat/kuro-${name}.png`)}
            style={{
              position: "absolute",
              left: x + Math.cos(ang) * dist * k,
              top: y + Math.sin(ang) * dist * k * 0.8 - f * 1.2,
              width: size[0] * px,
              height: size[1] * px,
              imageRendering: "pixelated",
              opacity: tween(f, 50, 80, 1, 0),
              scale: tween(f, 0, 10, 0.2, 1, backOut),
            }}
          />
        );
      })}
    </>
  );
};

type LineName = keyof typeof lines;

/** Loudness of a voice line, `f` frames after it started (0 outside it). */
export const loud = (name: LineName, f: number) => {
  const env = lines[name].env;
  const i = Math.floor(f / 2);
  return i >= 0 && i < env.length ? env[i] : 0;
};

/** Words of a line, each with the frame it's said (spread over the speech by length). */
export const wordTimes = (name: LineName, text: string) => {
  const { start, end } = lines[name];
  const words = text.split(" ");
  const total = words.reduce((n, w) => n + w.length + 1, 0);
  let acc = 0;
  return words.map((w) => {
    const at = start + ((end - start) * acc) / total;
    acc += w.length + 1;
    return { w, at };
  });
};

/** Subtitles, word by word as they're said, with who's talking. */
export const Caption: React.FC<{ line: LineName; at: number; who: "You" | "Sky"; text: string; hot?: string[]; light?: boolean; top?: number }> = ({
  line,
  at,
  who,
  text,
  hot = [],
  light,
  top,
}) => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const f = frame - at;
  const { frames } = lines[line];
  if (f < -4 || f > frames + 20) return null;
  const o = tween(f, -4, 6, 0, 1) * tween(f, frames + 4, frames + 18, 1, 0, easeIn);
  return (
    <div style={{ position: "absolute", left: 40, right: 40, ...(top !== undefined ? { top } : { bottom: L.v ? 420 : 96 }), display: "flex", justifyContent: "center", opacity: o }}>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 18,
          padding: "16px 30px 16px 16px",
          borderRadius: 60,
          background: light ? "rgba(255,255,255,0.82)" : "rgba(14,12,11,0.72)",
          border: `1.5px solid ${light ? "rgba(0,0,0,0.06)" : "rgba(255,255,255,0.09)"}`,
          boxShadow: "0 20px 60px rgba(0,0,0,0.25)",
          backdropFilter: "blur(16px)",
          translate: `0 ${tween(f, -4, 10, 16, 0)}px`,
        }}
      >
        <div
          style={{
            fontFamily: sans,
            fontWeight: 600,
            fontSize: 26,
            padding: "8px 18px",
            borderRadius: 40,
            background: who === "Sky" ? c.amber : light ? c.ink : c.text,
            color: who === "Sky" ? c.ink : light ? c.paper : c.ink,
          }}
        >
          {who}
        </div>
        <div style={{ fontFamily: sans, fontSize: 40, fontWeight: 500, color: light ? c.ink : c.text, display: "flex", flexWrap: "wrap", columnGap: 11, maxWidth: L.v ? 800 : undefined }}>
          {wordTimes(line, text).map(({ w, at: wAt }, i) => {
            const g = f - wAt;
            const isHot = hot.some((h) => w.toLowerCase().startsWith(h.toLowerCase()));
            return (
              <span
                key={i}
                style={{
                  opacity: tween(g, -2, 5, 0.18, 1),
                  color: isHot && g > 0 ? (light ? c.amberEdge : c.amber) : undefined,
                  fontWeight: isHot ? 600 : 500,
                }}
              >
                {w}
              </span>
            );
          })}
        </div>
      </div>
    </div>
  );
};

/** Shiki's voice pill, rebuilt: the amber mic with bars that move with the voice, words as heard. */
export const Pill: React.FC<{ line: LineName; at: number; text: string; hot?: string[]; width?: number; appearAt: number }> = ({
  line,
  at,
  text,
  hot = [],
  width = 745,
  appearAt,
}) => {
  const frame = useCurrentFrame();
  const f = frame - at;
  const lvl = loud(line, f);
  const words = wordTimes(line, text).filter((w) => f >= w.at - 2);
  return (
    <div
      style={{
        width,
        display: "flex",
        alignItems: "center",
        gap: 22,
        padding: "18px 26px 18px 18px",
        borderRadius: 60,
        background: "rgba(30,28,26,0.96)",
        border: "1.5px solid #34302C",
        boxShadow: "0 30px 70px rgba(0,0,0,0.5)",
        opacity: tween(frame, appearAt, appearAt + 8, 0, 1),
        scale: tween(frame, appearAt, appearAt + 16, 0.7, 1, backOut),
        transformOrigin: "85% 100%",
      }}
    >
      <div
        style={{
          width: 72,
          height: 72,
          borderRadius: "50%",
          background: c.amber,
          flexShrink: 0,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          gap: 5,
          boxShadow: `0 0 ${16 + lvl * 40}px rgba(246,169,69,${0.3 + lvl * 0.5})`,
        }}
      >
        {[0, 1, 2, 3].map((b) => (
          <div key={b} style={{ width: 6, borderRadius: 3, background: c.ink, height: 10 + 30 * Math.max(0.25 + 0.1 * Math.sin(frame / 7 + b * 1.8), lvl * (0.5 + 0.5 * Math.abs(Math.sin(frame / 3 + b * 2.2)))) }} />
        ))}
      </div>
      <div style={{ fontFamily: sans, fontSize: 30, fontWeight: 500, color: words.length ? c.text : c.muted, display: "flex", flexWrap: "wrap", columnGap: 9, rowGap: 2 }}>
        {words.length === 0
          ? "Listening…"
          : words.map(({ w, at: wAt }, i) => {
              const g = f - wAt;
              const isHot = hot.some((h) => w.toLowerCase().startsWith(h.toLowerCase()));
              return (
                <span key={i} style={{ opacity: tween(g, -2, 6, 0, 1), filter: `blur(${tween(g, -2, 6, 5, 0)}px)`, color: isHot ? c.amber : undefined, position: "relative" }}>
                  {w}
                  {isHot && (
                    <span style={{ position: "absolute", left: 0, bottom: -4, height: 3, borderRadius: 2, background: c.amber, width: `${tween(g, 4, 16, 0, 100)}%` }} />
                  )}
                </span>
              );
            })}
      </div>
    </div>
  );
};

/** A small rounded label in the pixel face. */
export const Chip: React.FC<{ children: React.ReactNode; light?: boolean; style?: React.CSSProperties }> = ({ children, light, style }) => (
  <div
    style={{
      fontFamily: pixel,
      fontSize: 30,
      padding: "8px 20px",
      borderRadius: 14,
      color: light ? c.ink : c.text,
      background: light ? "#fff" : "rgba(255,255,255,0.08)",
      border: `2px solid ${light ? c.ink : "rgba(255,255,255,0.18)"}`,
      ...style,
    }}
  >
    {children}
  </div>
);

/** A warm light leak that sweeps across a cut (screen-blended, so it only ever brightens). */
export const Leak: React.FC<{ at: number; dur?: number; flip?: boolean }> = ({ at, dur = 28, flip }) => {
  const frame = useCurrentFrame();
  const f = frame - at;
  if (f < 0 || f > dur) return null;
  const p = f / dur;
  const x = flip ? 110 - p * 120 : -10 + p * 120;
  const o = Math.sin(p * Math.PI);
  return (
    <AbsoluteFill style={{ mixBlendMode: "screen", pointerEvents: "none", opacity: o }}>
      <AbsoluteFill style={{ background: `radial-gradient(ellipse 55% 80% at ${x}% 40%, rgba(255,170,90,0.85), transparent 70%)` }} />
      <AbsoluteFill style={{ background: `radial-gradient(ellipse 35% 60% at ${x + (flip ? -18 : 18)}% 70%, rgba(255,110,120,0.6), transparent 70%)` }} />
      <AbsoluteFill style={{ background: `linear-gradient(${flip ? 250 : 110}deg, transparent ${p * 100 - 30}%, rgba(255,240,210,0.5) ${p * 100}%, transparent ${p * 100 + 20}%)` }} />
    </AbsoluteFill>
  );
};

/** Digits that roll like an odometer to show a value with one decimal, then a unit. */
export const Odometer: React.FC<{ value: number; size: number; color: string; italic?: boolean; unit?: string }> = ({ value, size, color, italic, unit }) => {
  const tenths = Math.round(value * 10 * 100) / 100; // e.g. 20.6 -> 206
  const digits = 3;
  const h = Math.round(size * 1.08);
  const cols = Array.from({ length: digits }, (_, k) => {
    const place = 10 ** (digits - 1 - k);
    const raw = tenths / place;
    // the last column rolls smoothly; higher ones turn over as the one below passes 9
    const below = (tenths % place) / place;
    const v = k === digits - 1 ? raw % 10 : (Math.floor(raw) % 10) + Math.max(0, below * 10 - 9);
    return { v, hide: k === 0 && raw < 1 };
  });
  const cell: React.CSSProperties = { height: h, lineHeight: `${h}px`, display: "block" };
  return (
    <span style={{ display: "inline-flex", height: h, fontStyle: italic ? "italic" : "normal", color, fontSize: size, lineHeight: `${h}px` }}>
      {cols.map(({ v, hide }, k) => (
        <React.Fragment key={k}>
          {k === digits - 1 && <span style={cell}>.</span>}
          {!hide && (
            <span style={{ display: "inline-block", height: h, overflow: "hidden", position: "relative", width: "0.5em", WebkitMaskImage: "linear-gradient(transparent, #000 22%, #000 78%, transparent)", maskImage: "linear-gradient(transparent, #000 22%, #000 78%, transparent)" }}>
              <span style={{ position: "absolute", left: 0, right: 0, top: 0, translate: `0 ${-v * h}px`, textAlign: "center" }}>
                {[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0].map((d, i) => (
                  <span key={i} style={cell}>
                    {d}
                  </span>
                ))}
              </span>
            </span>
          )}
        </React.Fragment>
      ))}
      {unit && <span style={{ ...cell, marginLeft: "0.25em" }}>{unit}</span>}
    </span>
  );
};

/** Little square pixel sparks thrown off a moving point, falling and fading. */
export const Sparks: React.FC<{ from: number; to: number; at: (f: number) => [number, number] | null; color?: string }> = ({ from, to, at, color = c.amber }) => {
  const frame = useCurrentFrame();
  const out: React.ReactNode[] = [];
  for (let born = Math.max(from, frame - 26); born <= Math.min(frame, to); born += 1) {
    const p = at(born);
    if (!p) continue;
    for (let k = 0; k < 2; k++) {
      const seed = Math.sin(born * 12.9898 + k * 78.233) * 43758.5453;
      const r = seed - Math.floor(seed);
      const r2 = (seed * 7.13) % 1;
      const age = frame - born;
      const vx = 2 + r * 7;
      const vy = -5 - r2 * 6;
      const x = p[0] + vx * age;
      const y = p[1] + vy * age + 0.45 * age * age;
      const sz = 5 + Math.round(r * 5);
      out.push(
        <div key={`${born}-${k}`} style={{ position: "absolute", left: x, top: y, width: sz, height: sz, background: k ? "#FFE3A3" : color, opacity: 1 - age / 26, boxShadow: `0 0 10px ${color}` }} />,
      );
    }
  }
  return <>{out}</>;
};

/** Serif words that land letter by letter. */
export const Letters: React.FC<{ at: number; size: number; text: string; color?: string; italic?: boolean; stagger?: number; style?: React.CSSProperties }> = ({
  at,
  size,
  text,
  color = c.text,
  italic,
  stagger = 1.6,
  style,
}) => {
  const frame = useCurrentFrame();
  return (
    <div style={{ display: "flex", fontFamily: serif, fontSize: size, lineHeight: 1, color, fontStyle: italic ? "italic" : "normal", letterSpacing: "-0.015em", whiteSpace: "pre", ...style }}>
      {text.split("").map((ch, i) => {
        const f = frame - at - i * stagger;
        return (
          <span
            key={i}
            style={{
              display: "inline-block",
              opacity: tween(f, 0, 4, 0, 1),
              translate: `0 ${tween(f, 0, 12, size * 0.35, 0, backOut)}px`,
              rotate: `${tween(f, 0, 12, (i % 2 ? 1 : -1) * 14, 0, backOut)}deg`,
              scale: tween(f, 0, 12, 0.4, 1, backOut),
              filter: `blur(${tween(f, 0, 7, 8, 0)}px)`,
            }}
          >
            {ch}
          </span>
        );
      })}
    </div>
  );
};

/** A band of light that sweeps across text once. */
export const Shine: React.FC<{ at: number; dur?: number; children: React.ReactNode; base: string; style?: React.CSSProperties }> = ({ at, dur = 40, children, base, style }) => {
  const frame = useCurrentFrame();
  const p = tween(frame, at, at + dur, -30, 130, (t) => t);
  return (
    <span
      style={{
        backgroundImage: `linear-gradient(105deg, ${base} ${p - 14}%, #FFC277 ${p - 4}%, #FFF4DC ${p}%, #FFC277 ${p + 4}%, ${base} ${p + 14}%)`,
        WebkitBackgroundClip: "text",
        backgroundClip: "text",
        color: "transparent",
        ...style,
      }}
    >
      {children}
    </span>
  );
};
