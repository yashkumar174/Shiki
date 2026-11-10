import React from "react";
import { AbsoluteFill, OffthreadVideo, Sequence, interpolate, staticFile, useCurrentFrame } from "remotion";
import { Audio } from "@remotion/media";
import { C, Cat, Ground, Icon, IconName, PIXEL, POP, Pose, Ring, SANS, Sprite, clamp01, cubic, inCubic, mix, outCubic, outQuint, spr, tw } from "./kit";
import { End } from "./End";
import { Opener } from "./Opener";
import { Bento, Levels, Phone, Pills } from "./Product";
import { Span } from "./timing";

// Shiki ad V3, the fast cut: 21 s at 120 BPM, so a beat is 30 frames and every cut sits on
// one. Three flashes of the old way, a hard drop as the cat lands, the product in quick
// shots joined by whips and zoom-throughs, nine cats on the beat, and the logo on a hit.

const B = 30;
export const HYPE_FRAMES = 1260;

/** Where each shot starts, in frames. */
const T = {
  era1: 0,
  era2: B,
  era3: 2 * B,
  suck: 3 * B,
  drop: 4 * B,
  hub: 8 * B,
  pill: 12 * B,
  card: 12 * B + 45,
  done: 15 * B,
  bento: 16 * B,
  pills: 20 * B,
  cats: 22 * B,
  faces: 24 * B,
  rename: 28 * B,
  levels: 30 * B,
  phone: 32 * B,
  end: 36 * B,
};

/** A span that plays a scene's own choreography over `dur` frames of this shot. */
const sp = (dur: number, auth: number): Span => ({ from: 0, dur, auth });
/** A slice of the opener: its choreography from `u0`, at `rate` choreography frames a frame. */
const slice = (u0: number, rate: number): Span => ({ from: -u0 / rate, dur: 360 / rate, auth: 360 });

// ---------------------------------------------------------------- sound

const Sfx: React.FC<{ at: number; name: string; v?: number; dir?: "sfx" | "sfx2" }> = ({ at, name, v = 0.4, dir = "sfx2" }) => (
  <Sequence from={at} durationInFrames={130} layout="none" name={`sfx ${name}`}>
    <Audio src={staticFile(`${dir}/${name}.wav`)} volume={v} />
  </Sequence>
);

// ---------------------------------------------------------------- shots and the cuts between them

type Tr = "cut" | "punch" | "whipL" | "whipR" | "whipU" | "whipD" | "zoom";
const EN = 10; // frames an entrance takes
const EX = 6; // frames an exit takes

/**
 * One shot. It arrives and leaves by its own transition, with blur along the direction of
 * travel, so two shots that share a cut read as one camera move: a whip, or a push through.
 */
const Shot: React.FC<{ from: number; dur: number; name: string; enter?: Tr; exit?: Tr; children: React.ReactNode }> = ({ from, dur, name, enter = "cut", exit = "cut", children }) => (
  <Sequence from={from} durationInFrames={dur} name={name}>
    <ShotBody dur={dur} id={`hype-${from}`} enter={enter} exit={exit}>
      {children}
    </ShotBody>
  </Sequence>
);

const ShotBody: React.FC<{ dur: number; id: string; enter: Tr; exit: Tr; children: React.ReactNode }> = ({ dur, id, enter, exit, children }) => {
  const f = useCurrentFrame();
  let tx = 0;
  let ty = 0;
  let sc = 1;
  let bx = 0;
  let by = 0;
  let op = 1;
  if (f < EN && enter !== "cut") {
    const q = 1 - outQuint(clamp01((f + 1.5) / EN));
    if (enter === "whipL") (tx += q * 1700), (bx += q * 70);
    if (enter === "whipR") (tx -= q * 1700), (bx += q * 70);
    if (enter === "whipU") (ty += q * 1000), (by += q * 55);
    if (enter === "whipD") (ty -= q * 1000), (by += q * 55);
    if (enter === "zoom") (sc *= 1 - 0.5 * q), (bx += q * 14), (by += q * 14), (op *= Math.min(1, ((f + 1.5) / EN) * 4));
    if (enter === "punch") sc *= 1 + 0.14 * q;
  }
  const x = clamp01((f - (dur - EX)) / EX);
  if (x > 0 && exit !== "cut" && exit !== "punch") {
    const q = x * x;
    if (exit === "whipL") (tx -= q * 1700), (bx += q * 70);
    if (exit === "whipR") (tx += q * 1700), (bx += q * 70);
    if (exit === "whipU") (ty -= q * 1000), (by += q * 55);
    if (exit === "whipD") (ty += q * 1000), (by += q * 55);
    if (exit === "zoom") (sc *= 1 + 3 * q), (bx += q * 16), (by += q * 16), (op *= 1 - x * x * x);
  }
  const blurred = bx > 0.5 || by > 0.5;
  return (
    <AbsoluteFill style={{ transform: `translate(${tx}px, ${ty}px) scale(${sc})`, opacity: op, filter: blurred ? `url(#${id})` : undefined }}>
      {blurred ? (
        <svg width={0} height={0} style={{ position: "absolute" }}>
          <filter id={id} x="-25%" y="-25%" width="150%" height="150%">
            <feGaussianBlur stdDeviation={`${bx.toFixed(1)} ${by.toFixed(1)}`} />
          </filter>
        </svg>
      ) : null}
      {children}
    </AbsoluteFill>
  );
};

/** Words that hit: they land big and sharp from a blur, and leave fast. */
const Slam: React.FC<{ text: string; at: number; out?: number; size: number; x?: number; y: number; color?: string; accentFrom?: number; accent?: string; shadow?: string; weight?: number }> = ({
  text,
  at,
  out,
  size,
  x,
  y,
  color = C.text,
  accentFrom,
  accent = C.moon,
  shadow,
  weight = 700,
}) => {
  const f = useCurrentFrame();
  if (f < at || (out !== undefined && f >= out + 5)) return null;
  const s = tw(f, at, at + 9, 1.85, 1, outQuint) * (out !== undefined ? tw(f, out, out + 5, 1, 0.9, inCubic) : 1);
  const blur = tw(f, at, at + 6, 22, 0, outCubic);
  const op = tw(f, at, at + 3, 0, 1, outCubic) * (out !== undefined ? tw(f, out, out + 5, 1, 0, inCubic) : 1);
  const cut = accentFrom ?? text.length;
  return (
    <div
      style={{
        position: "absolute",
        left: x ?? 0,
        right: x === undefined ? 0 : undefined,
        top: y,
        display: "flex",
        justifyContent: x === undefined ? "center" : "flex-start",
        opacity: op,
        transform: `scale(${s})`,
        transformOrigin: x === undefined ? "center" : "left center",
        filter: blur > 0.4 ? `blur(${blur}px)` : undefined,
      }}
    >
      <div style={{ fontFamily: PIXEL, fontWeight: weight, fontSize: size, lineHeight: 1, color, whiteSpace: "pre", textShadow: shadow, letterSpacing: `${tw(f, at, at + 10, 0.06, 0, outQuint)}em` }}>
        {text.slice(0, cut)}
        {cut < text.length ? <span style={{ color: accent }}>{text.slice(cut)}</span> : null}
      </div>
    </div>
  );
};

// ---------------------------------------------------------------- the camera: hits, shake, flashes

/** [frame, strength]: where the edit lands a hit. */
const FACE_CUTS = [0, 15, 30, 45, 60, 70, 80, 90, 100];
const FACE_DURS = [15, 15, 15, 15, 10, 10, 10, 10, 20];
const HITS: [number, number][] = [
  [T.drop, 1],
  [T.drop + B, 0.5],
  [T.drop + 2 * B, 0.55],
  [T.done, 0.6],
  [T.bento, 0.85],
  ...FACE_CUTS.map((c): [number, number] => [T.faces + c, 0.28]),
  [T.rename, 0.4],
  [T.end, 1],
];
/** Hard cuts that tear for a few frames (the colour channels split). */
const TEARS = [T.era1, T.era2, T.era3, ...FACE_CUTS.map((c) => T.faces + c), T.rename];

const Camera: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const f = useCurrentFrame();
  let dx = 0;
  let dy = 0;
  let rot = 0;
  let env = 0;
  for (const [at, a] of HITS) {
    const t = f - at;
    if (t < 0 || t > 26) continue;
    const e = a * Math.exp(-t / 4.5);
    dx += e * 26 * Math.sin(t * 2.3 + at);
    dy += e * 18 * Math.cos(t * 3.1 + at);
    rot += e * 0.5 * Math.sin(t * 1.9 + at * 0.7);
    env = Math.max(env, e);
  }
  let tear = 0;
  for (const at of TEARS) {
    const t = f - at;
    if (t >= 0 && t < 5) tear = Math.max(tear, 18 * (1 - t / 5));
  }
  return (
    <AbsoluteFill style={{ background: C.ink }}>
      {tear > 0.5 ? (
        <svg width={0} height={0} style={{ position: "absolute" }}>
          <filter id="hype-tear" x="0" y="0" width="100%" height="100%" colorInterpolationFilters="sRGB">
            <feColorMatrix in="SourceGraphic" type="matrix" values="1 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 1 0" result="r" />
            <feOffset in="r" dx={tear} dy={0} result="r2" />
            <feColorMatrix in="SourceGraphic" type="matrix" values="0 0 0 0 0  0 1 0 0 0  0 0 0 0 0  0 0 0 1 0" result="g" />
            <feColorMatrix in="SourceGraphic" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 1 0 0  0 0 0 1 0" result="b" />
            <feOffset in="b" dx={-tear} dy={0} result="b2" />
            <feBlend in="r2" in2="g" mode="screen" result="rg" />
            <feBlend in="rg" in2="b2" mode="screen" />
          </filter>
        </svg>
      ) : null}
      <AbsoluteFill style={{ transform: `translate(${dx}px, ${dy}px) rotate(${rot}deg) scale(${1 + 0.05 * env})`, filter: tear > 0.5 ? "url(#hype-tear)" : undefined }}>{children}</AbsoluteFill>
    </AbsoluteFill>
  );
};

/** A white-blue flash on each hit, over everything. */
const Flashes: React.FC = () => {
  const f = useCurrentFrame();
  let o = 0;
  for (const [at, a] of HITS) {
    const t = f - at;
    if (t >= 0 && t < 14) o = Math.max(o, a * 0.62 * Math.exp(-t / 2.4));
  }
  return o > 0.004 ? <AbsoluteFill style={{ background: "radial-gradient(ellipse at center, #FFFFFF 0%, #CFE9FF 60%, #9CCBFA 100%)", opacity: o }} /> : null;
};

// ---------------------------------------------------------------- 0: the old way, in three flashes

const EraWords: React.FC<{ text: string; light: boolean }> = ({ text, light }) => (
  <Slam text={text} at={3} size={132} x={84} y={840} color={light ? "#fff" : "#2C3E50"} shadow={light ? "6px 6px 0 rgba(10,13,20,0.55)" : undefined} />
);

/** The cat, already falling, as the old way is pulled into its point. */
const FallingCat: React.FC<{ landY: number }> = ({ landY }) => {
  const f = useCurrentFrame();
  if (f < 17) return null;
  const p = clamp01((f - 17) / 13);
  const y = -260 + (landY + 260) * p * p;
  return (
    <>
      {[3, 2, 1].map((k) => (
        <Cat key={k} pose="Startled" x={960} y={y - k * 70 * p} scale={12} opacity={0.14 * (4 - k) * p} />
      ))}
      <Cat pose="Startled" x={960} y={y} scale={12} />
    </>
  );
};

// ---------------------------------------------------------------- 1: the drop

const LAND_Y = 440;

const Drop: React.FC = () => {
  const f = useCurrentFrame();
  const half = tw(f, 0, 12, 0, 850, outQuint);
  const sag = 26 * Math.exp(-f / 9) * Math.cos(f / 3.2);
  let pose: Pose = "Idle";
  let y = LAND_Y + sag;
  if (f < 7) (pose = "Pop"), (y += 8);
  else if (f >= 20 && f < 26) pose = "Blink";
  else if (f >= 30 && f < 60) pose = Math.floor(f / 5) % 2 === 0 ? "Talk" : "Idle";
  else if (f >= 60 && f < 106) pose = "Happy";
  else if (f >= 106) (pose = "Pop"), (y += 8);
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 960 - 520, top: LAND_Y - 520, width: 1040, height: 1040, borderRadius: "50%", background: "radial-gradient(circle, rgba(90,185,252,0.34) 0%, rgba(90,185,252,0.08) 42%, transparent 68%)", opacity: tw(f, 0, 8, 1, 0.45, outCubic) }} />
      {[0, 1, 2].map((i) => (
        <Ring key={i} x={960} y={LAND_Y} r={tw(f, i * 5, i * 5 + 34, 40, 1150, outQuint)} width={i === 0 ? 5 : 3} color={i === 1 ? "#FFFFFF" : C.moon} opacity={tw(f, i * 5, i * 5 + 34, 0.95, 0, outCubic)} />
      ))}
      {half > 1 ? (
        <div style={{ position: "absolute", left: 960 - half, top: LAND_Y - 2 + (f < 30 ? sag * 0.2 : 0), width: half * 2, height: 4, background: `linear-gradient(90deg, transparent, ${C.text} 14%, ${C.text} 86%, transparent)`, boxShadow: "0 0 26px rgba(90,185,252,0.7)" }} />
      ) : null}
      {f < 24
        ? [-1, 1].map((dir) =>
            [0, 1, 2, 3].map((k) => {
              const p = tw(f, 0, 14 + k * 3, 0, 1, outQuint);
              return <div key={`${dir}${k}`} style={{ position: "absolute", left: Math.round(960 + dir * (120 + p * (60 + k * 60)) - 7), top: Math.round(LAND_Y - 18 - Math.sin(p * Math.PI) * (26 + k * 16)), width: 14, height: 14, background: C.text, opacity: (1 - p) * 0.85 }} />;
            }),
          )
        : null}
      <Slam text="Say it." at={30} out={56} size={330} y={520} />
      <Slam text="It happens." at={60} size={236} y={548} accentFrom={10} />
      {f >= 60 && f < 96
        ? [0, 1, 2].map((i) => (
            <Sprite key={i} name="mochi-float-Sparkle" w={3} h={3} scale={10} x={960 + [-250, 210, 280][i]} y={LAND_Y - [250, 300, 130][i] - tw(f, 60 + i * 4, 96, 0, 46, outQuint)} opacity={tw(f, 60 + i * 4, 66 + i * 4, 0, 1, outCubic) * tw(f, 84, 96, 1, 0, inCubic)} />
          ))
        : null}
      <Cat pose={pose} x={960} y={y} scale={12} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 2: the hub

type Node = { ax: number; ay: number; side: "l" | "r"; icon: IconName; label: string; sub?: string; bend?: number };
const NODES: Node[] = [
  { ax: 720, ay: 270, side: "l", icon: "chat", label: "WhatsApp Mummy", sub: "“on my way”" },
  { ax: 1240, ay: 240, side: "r", icon: "note", label: "play Seedhe Maut" },
  { ax: 1270, ay: 548, side: "r", icon: "photo", label: "3 photos from my iPhone", bend: -70 },
  { ax: 1230, ay: 850, side: "r", icon: "bell", label: "remind me at 5" },
  { ax: 700, ay: 810, side: "l", icon: "mail", label: "email the invoice to Tausif" },
];
const curveOf = (n: Node) => {
  const dx = n.ax - 960;
  const p0 = [960, 540];
  const p1 = [960 + dx * 0.6, 540 + (n.bend ?? 0)];
  const p2 = [n.ax - dx * 0.5, n.ay - (n.bend ?? 0) * 0.4];
  const p3 = [n.ax, n.ay];
  return { p0, p1, p2, p3, d: `M${p0[0]} ${p0[1]} C${p1[0]} ${p1[1]} ${p2[0]} ${p2[1]} ${p3[0]} ${p3[1]}` };
};
const nodeAt = (i: number) => 5 + i * 8;

const Hub: React.FC = () => {
  const f = useCurrentFrame();
  const push = 1 + 0.045 * (f / 120);
  const core = spr(f, 0, { stiffness: 140, damping: 16 });
  const k = (f - 5) % 8;
  const pose: Pose = f < 4 ? "Pop" : f < 50 ? (k < 4 ? "Talk" : "Listen") : f >= 78 && f < 84 ? "Blink" : "Happy";
  return (
    <AbsoluteFill style={{ transform: `scale(${push}) rotate(${mix(-1.6, 0.4, f / 120)}deg)` }}>
      <div style={{ position: "absolute", left: 960 - 380, top: 540 - 380, width: 760, height: 760, borderRadius: "50%", background: "radial-gradient(circle, rgba(90,185,252,0.34) 0%, rgba(90,185,252,0.09) 40%, transparent 68%)", transform: `scale(${0.5 + 0.5 * core + 0.04 * Math.sin(f * 0.2)})` }} />
      <svg width={1920} height={1080} style={{ position: "absolute" }}>
        {NODES.map((n, i) => {
          const c = curveOf(n);
          const p = tw(f, nodeAt(i), nodeAt(i) + 14, 0, 1, outQuint);
          if (p <= 0.001) return null;
          const head = cubic(c.p0, c.p1, c.p2, c.p3, p);
          const run = f > nodeAt(i) + 16 ? ((f - nodeAt(i) - 16) / 30 + i * 0.37) % 1 : -1;
          const pulse = run >= 0 ? cubic(c.p0, c.p1, c.p2, c.p3, run) : null;
          return (
            <g key={i}>
              <path d={c.d} pathLength={1} strokeDasharray="1 1" strokeDashoffset={1 - p} stroke={C.moon} strokeOpacity={0.85} strokeWidth={3} fill="none" strokeLinecap="round" />
              <circle cx={p < 0.995 ? head[0] : n.ax} cy={p < 0.995 ? head[1] : n.ay} r={7} fill={C.moon} />
              {pulse ? <circle cx={pulse[0]} cy={pulse[1]} r={5} fill="#fff" opacity={Math.sin(run * Math.PI) * 0.95} /> : null}
            </g>
          );
        })}
      </svg>
      <Cat pose={pose} x={960} y={640 - (f < 4 ? -8 : 0)} scale={10} />
      {NODES.map((n, i) => {
        const pop = spr(f, nodeAt(i) + 9, POP);
        if (pop <= 0.001) return null;
        const bob = Math.sin(f * 0.09 + i * 1.9) * 6;
        const left = n.side === "l";
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              ...(left ? { right: 1920 - n.ax + 16 } : { left: n.ax + 16 }),
              top: n.ay - 48,
              height: 96,
              transform: `translateY(${bob}px) scale(${0.6 + 0.4 * pop})`,
              transformOrigin: left ? "right center" : "left center",
              opacity: Math.min(1, pop * 2.5),
              display: "flex",
              alignItems: "center",
              gap: 18,
              padding: "0 36px 0 16px",
              borderRadius: 48,
              background: C.card,
              border: `2px solid #2C3A55`,
              boxShadow: "0 22px 60px rgba(0,0,0,0.55), 0 0 44px rgba(90,185,252,0.16)",
              whiteSpace: "nowrap",
            }}
          >
            <div style={{ width: 64, height: 64, borderRadius: 32, background: "rgba(90,185,252,0.16)", display: "flex", alignItems: "center", justifyContent: "center" }}>
              <Icon name={n.icon} size={36} color={C.moon} stroke={2} />
            </div>
            <div style={{ fontFamily: SANS, fontWeight: 700, fontSize: 32, color: C.text }}>
              {n.label}
              {n.sub ? <span style={{ color: C.muted, fontWeight: 500 }}>&nbsp; {n.sub}</span> : null}
            </div>
          </div>
        );
      })}
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 3: the voice pill

const SAID = "WhatsApp Mummy I’m on my way";

const VoicePill: React.FC = () => {
  const f = useCurrentFrame();
  const W = 1320;
  const H = 240;
  const left = 960 - W / 2;
  const top = 560 - H / 2;
  const n = Math.max(0, Math.min(SAID.length, Math.floor((f - 6) / 1.05)));
  const typing = n < SAID.length;
  return (
    <AbsoluteFill style={{ transform: `scale(${1 + 0.05 * (f / 45)})` }}>
      {[0, 1].map((i) => {
        const p = clamp01((f - i * 14) / 30);
        return p <= 0 || p >= 1 ? null : <Ring key={i} x={left + H / 2} y={560} r={90 + p * 190} width={3} opacity={(1 - p) * 0.6} />;
      })}
      <div style={{ position: "absolute", left, top, width: W, height: H, borderRadius: H / 2, background: C.card, border: "2px solid #2A3550", boxSizing: "border-box", boxShadow: "0 50px 130px rgba(0,0,0,0.65), 0 0 110px rgba(90,185,252,0.18)" }} />
      <div style={{ position: "absolute", left: left + 30, top: top + 30, width: H - 60, height: H - 60, borderRadius: "50%", background: C.moon, display: "flex", alignItems: "center", justifyContent: "center", gap: 9, boxShadow: "0 0 60px rgba(90,185,252,0.55)" }}>
        {[0, 1, 2, 3, 4].map((i) => {
          const a = typing ? 0.35 + 0.65 * Math.abs(Math.sin(f * 0.62 + i * 1.7) * Math.cos(f * 0.21 + i)) : 0.3;
          return <div key={i} style={{ width: 13, height: 26 + 86 * a, borderRadius: 7, background: C.ink }} />;
        })}
      </div>
      <div style={{ position: "absolute", left: left + H + 14, top, height: H, display: "flex", alignItems: "center", fontFamily: SANS, fontWeight: 600, fontSize: 66, color: C.text, whiteSpace: "pre" }}>
        {SAID.slice(0, n)}
        <span style={{ display: "inline-block", width: 5, height: 70, marginLeft: 6, background: C.moon, opacity: typing || Math.floor(f / 8) % 2 === 0 ? 1 : 0 }} />
      </div>
      <Cat pose={typing ? "Listen" : "Happy"} x={left + W - 170} y={top + 4} scale={8} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 4: the chat card, then "done."

// The ask window sits at the top left of the recording's canvas: x 22-722, y 22-584 once the
// two-things card is open.
const CARD = { cx: 372, cy: 303, bottom: 584 };
const DO_IT = { x: 150, y: 456 };

const ChatCard: React.FC = () => {
  const f = useCurrentFrame();
  const p = clamp01(f / 45);
  const S = 1.42 * (1 + 0.05 * p);
  const tapP = clamp01((f - 14) / 12);
  const tap = f >= 26 && f < 31 ? 6 : 0;
  // the paw comes in to press "Do it"
  const px = mix(520, DO_IT.x - 22, outQuint(tapP));
  const py = mix(760, DO_IT.y - 8, outQuint(tapP)) + tap;
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 960 - 520, top: 520 - 420, width: 1040, height: 840, borderRadius: "50%", background: "radial-gradient(ellipse, rgba(90,185,252,0.22) 0%, transparent 66%)" }} />
      <div
        style={{
          position: "absolute",
          left: 960 - CARD.cx,
          top: 520 - CARD.cy,
          width: 900,
          height: 1100,
          transformOrigin: `${CARD.cx}px ${CARD.cy}px`,
          transform: `perspective(1800px) rotateY(${mix(-20, -5, outQuint(p))}deg) rotateX(${mix(12, 3, outQuint(p))}deg) scale(${S})`,
          filter: "drop-shadow(0 50px 80px rgba(0,0,0,0.65)) drop-shadow(0 0 60px rgba(90,185,252,0.16))",
        }}
      >
        <div style={{ position: "absolute", inset: 0, clipPath: `inset(22px 178px ${1100 - CARD.bottom}px 22px round 16px)` }}>
          <OffthreadVideo src={staticFile("v3/rec/chat.webm")} muted trimBefore={110} playbackRate={0.6} style={{ width: 900, height: 1100, display: "block" }} />
        </div>
        {f >= 26 ? <Ring x={DO_IT.x} y={DO_IT.y} r={tw(f, 26, 44, 20, 130, outQuint)} width={3} opacity={tw(f, 26, 44, 0.9, 0, outCubic)} /> : null}
        <Sprite name="mochi-pawpointer" w={12} h={14} scale={5} x={px} y={py} opacity={tw(f, 14, 20, 0, 1, outCubic)} style={{ filter: "drop-shadow(0 8px 10px rgba(0,0,0,0.5))" }} />
      </div>
    </AbsoluteFill>
  );
};

const Done: React.FC = () => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill>
      {[0, 1, 2].map((i) => (
        <Ring key={i} x={960} y={540} r={tw(f, i * 4, i * 4 + 30, 60, 1100, outQuint)} width={4} color={i === 1 ? "#FFFFFF" : C.moon} opacity={tw(f, i * 4, i * 4 + 30, 0.95, 0, outCubic)} />
      ))}
      <Slam text="done." at={0} size={340} y={370} accentFrom={4} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 5: Home assembles

const BentoShot: React.FC = () => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill style={{ transform: `scale(${1 + 0.035 * (f / 120)})` }}>
      <Bento span={sp(120, 228)} noHead />
      <Slam text="Everything." at={50} out={96} size={92} x={250} y={176} />
      <Slam text="One place." at={64} out={98} size={92} x={1030} y={176} color={C.moon} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 6: the cats

const CatsGlide: React.FC = () => {
  const f = useCurrentFrame();
  const p = f / 60;
  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 500,
          top: 20,
          width: 1550,
          height: 1026,
          transformOrigin: "50% 50%",
          transform: `perspective(2000px) rotateY(${mix(-22, -12, p)}deg) rotateX(${mix(9, 5, p)}deg) scale(${mix(1.08, 1.16, p)})`,
          borderRadius: 22,
          overflow: "hidden",
          boxShadow: "0 60px 140px rgba(0,0,0,0.7), 0 0 90px rgba(90,185,252,0.16)",
          clipPath: "inset(4px 2px 2px 2px round 22px)",
        }}
      >
        <OffthreadVideo src={staticFile("v3/rec/cats.mp4")} muted trimBefore={50} playbackRate={3} style={{ width: 1550, height: 1026, display: "block" }} />
      </div>
      <AbsoluteFill style={{ background: "linear-gradient(90deg, rgba(10,13,20,0.96) 0%, rgba(10,13,20,0.85) 26%, transparent 52%)" }} />
      <Slam text="9" at={8} size={520} x={110} y={210} color={C.amber} />
      <Slam text="cats." at={16} size={190} x={110} y={690} />
    </AbsoluteFill>
  );
};

const FACES: { cat: string; name: string; color: string; light: boolean; pose: string }[] = [
  { cat: "mochi", name: "melloo", color: "#F0913A", light: false, pose: "Happy" },
  { cat: "latte", name: "Latte", color: "#C9A27A", light: false, pose: "Idle" },
  { cat: "sky", name: "Sky", color: "#9CC6F2", light: false, pose: "Purr" },
  { cat: "kuro", name: "Kuro", color: "#1E2433", light: true, pose: "Listen" },
  { cat: "ginger", name: "Ginger", color: "#E8562A", light: true, pose: "Talk" },
  { cat: "ash", name: "Ash", color: "#4F7A6B", light: true, pose: "Idle" },
  { cat: "sumi", name: "Sumi", color: "#E9E4D8", light: false, pose: "Happy" },
  { cat: "maple", name: "Maple", color: "#7A3E22", light: true, pose: "Pop" },
  { cat: "biscuit", name: "Biscuit", color: "#F2C14E", light: false, pose: "crown" },
];

/** One cat, the whole frame in its colour: the fast run through all nine. */
const Face: React.FC<{ i: number }> = ({ i }) => {
  const f = useCurrentFrame();
  const c = FACES[i];
  const right = i % 2 === 0;
  const q = 1 - outQuint(clamp01(f / 8));
  const ink = c.light ? "#F4F1EA" : "#141A27";
  const catX = right ? 1380 : 540;
  return (
    <AbsoluteFill style={{ background: c.color, overflow: "hidden" }}>
      <div style={{ position: "absolute", left: catX - 620, top: 560 - 620, width: 1240, height: 1240, borderRadius: "50%", background: "radial-gradient(circle, rgba(255,255,255,0.3) 0%, rgba(255,255,255,0.08) 45%, transparent 68%)" }} />
      <AbsoluteFill style={{ background: "radial-gradient(ellipse at center, transparent 55%, rgba(0,0,0,0.28) 100%)" }} />
      <div style={{ position: "absolute", left: right ? 110 : undefined, right: right ? undefined : 110, top: 96, fontFamily: PIXEL, fontWeight: 600, fontSize: 54, color: ink, opacity: 0.6 }}>
        0{i + 1} / 09
      </div>
      <div style={{ position: "absolute", left: right ? 100 : undefined, right: right ? undefined : 100, top: 560, fontFamily: PIXEL, fontWeight: 700, fontSize: c.name.length > 6 ? 250 : 300, lineHeight: 1, color: ink, transform: `translateX(${(right ? -1 : 1) * q * 160}px)` }}>{c.name}</div>
      <div style={{ position: "absolute", left: catX - 220, top: 950, width: 440, height: 40, borderRadius: "50%", background: "rgba(0,0,0,0.25)", filter: "blur(10px)" }} />
      <AbsoluteFill style={{ transform: `translate(${(right ? 1 : -1) * q * 220}px, 0px) scale(${1 + 0.12 * q})`, transformOrigin: `${catX}px 960px` }}>
        <Cat pose={c.pose as Pose} cat={c.cat} x={catX} y={960} scale={32} />
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 7: a name, typed

const Rename: React.FC = () => {
  const f = useCurrentFrame();
  const s = 2.0 * (1 + 0.06 * (f / 60));
  // the camera sits on Latte's card, where the pencil opens
  const camX = 960 - 755 * s;
  const camY = 440 - 640 * s;
  return (
    <AbsoluteFill style={{ background: C.ink }}>
      <AbsoluteFill style={{ transform: `perspective(2400px) rotateY(${mix(5, -3, f / 60)}deg)` }}>
        <div style={{ position: "absolute", left: 0, top: 0, width: 1550, height: 1026, transformOrigin: "0 0", transform: `translate(${camX}px, ${camY}px) scale(${s})`, clipPath: "inset(4px 2px 2px 2px)" }}>
          <OffthreadVideo src={staticFile("v3/rec/rename.mp4")} muted trimBefore={24} playbackRate={1.35} style={{ width: 1550, height: 1026, display: "block" }} />
        </div>
      </AbsoluteFill>
      <AbsoluteFill style={{ background: "linear-gradient(180deg, transparent 50%, rgba(10,13,20,0.92) 76%, rgba(10,13,20,0.98) 100%)" }} />
      <Slam text="Name yours." at={14} size={170} y={800} accentFrom={10} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- the cut

export const AdV3Hype: React.FC = () => {
  const f = useCurrentFrame();
  const music = (t: number) => 0.6 * interpolate(t, [0, 4, HYPE_FRAMES - 40, HYPE_FRAMES - 2], [0, 1, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const dark = f >= T.drop;
  return (
    <AbsoluteFill style={{ background: C.ink }}>
      <Camera>
        <Ground field={dark ? 1 : 0} />

        {/* the old way: twelve clicks, a website and an email, six apps */}
        <Shot from={T.era1} dur={B} name="2006" enter="punch">
          <Opener span={slice(30, 2)} mute bare />
          <EraWords text="12 clicks." light />
        </Shot>
        <Shot from={T.era2} dur={B} name="2014" enter="punch">
          <Opener span={slice(112, 2)} mute bare />
          <EraWords text="1 website. 1 email." light={false} />
        </Shot>
        <Shot from={T.era3} dur={B} name="2021" enter="punch">
          <Opener span={slice(196, 2)} mute bare />
          <EraWords text="6 apps." light />
        </Shot>
        <Shot from={T.suck} dur={B} name="it all falls in">
          <Opener span={slice(264, 28 / 30)} mute bare />
          <FallingCat landY={LAND_Y} />
        </Shot>

        <Shot from={T.drop} dur={4 * B} name="the drop: say it, it happens" exit="zoom">
          <Drop />
        </Shot>
        <Shot from={T.hub} dur={4 * B} name="the hub" enter="zoom" exit="zoom">
          <Hub />
        </Shot>
        <Shot from={T.pill} dur={45} name="voice pill" enter="zoom" exit="whipU">
          <VoicePill />
        </Shot>
        <Shot from={T.card} dur={45} name="chat card" enter="whipU" exit="zoom">
          <ChatCard />
        </Shot>
        <Shot from={T.done} dur={B} name="done." enter="punch">
          <Done />
        </Shot>
        <Shot from={T.bento} dur={4 * B} name="Home assembles">
          <BentoShot />
        </Shot>
        <Shot from={T.pills} dur={2 * B} name="pills" exit="whipL">
          <Pills span={sp(60, 120)} />
        </Shot>
        <Shot from={T.cats} dur={2 * B} name="cats glide" enter="whipL">
          <CatsGlide />
        </Shot>
        {FACES.map((c, i) => (
          <Shot key={c.cat} from={T.faces + FACE_CUTS[i]} dur={FACE_DURS[i]} name={`cat ${c.name}`}>
            <Face i={i} />
          </Shot>
        ))}
        <Shot from={T.rename} dur={2 * B} name="name yours" enter="punch" exit="whipU">
          <Rename />
        </Shot>
        <Shot from={T.levels} dur={2 * B} name="levels walk">
          <Levels span={{ from: -2, dur: 60, auth: 180 }} exitX={560} />
        </Shot>
        <Shot from={T.phone} dur={4 * B} name="iPhone">
          <AbsoluteFill style={{ transform: "scale(1.2)", transformOrigin: "1010px 560px" }}>
            <Phone span={sp(120, 144)} />
          </AbsoluteFill>
        </Shot>
        <Shot from={T.end} dur={HYPE_FRAMES - T.end} name="logo" enter="punch">
          <EndGlow />
          <AbsoluteFill style={{ transform: "scale(1.14)", transformOrigin: "960px 600px" }}>
            <End span={sp(120, 156)} />
          </AbsoluteFill>
        </Shot>
      </Camera>
      <Flashes />
      <AbsoluteFill style={{ background: "radial-gradient(ellipse at center, transparent 58%, rgba(4,6,10,0.5) 100%)", pointerEvents: "none" }} />

      {/* sound: the music carries the hits; these are the cuts and the touches */}
      <Audio src={staticFile("audio/v3-hype.wav")} volume={music} />
      {[T.era1, T.era2, T.era3].map((t) => (
        <Sfx key={t} at={t} name="switch" dir="sfx" v={0.5} />
      ))}
      <Sfx at={T.suck + 2} name="whoosh3" v={0.5} />
      <Sfx at={T.drop + 8} name="meow" v={0.45} />
      <Sfx at={T.drop + B - 2} name="whip" dir="sfx" v={0.6} />
      <Sfx at={T.drop + 2 * B - 2} name="whip" dir="sfx" v={0.6} />
      <Sfx at={T.hub - 10} name="whoosh-fast" v={0.4} />
      {NODES.map((_, i) => (
        <Sfx key={i} at={T.hub + nodeAt(i) + 9} name="pop" v={0.3} />
      ))}
      <Sfx at={T.pill - 10} name="whoosh-fast" v={0.4} />
      {[8, 14, 20, 26, 32].map((t) => (
        <Sfx key={t} at={T.pill + t} name="tick" v={0.22} />
      ))}
      <Sfx at={T.card - 6} name="whoosh2" v={0.4} />
      <Sfx at={T.card + 26} name="click" v={0.45} />
      <Sfx at={T.done - 8} name="whoosh-fast" v={0.4} />
      <Sfx at={T.done} name="ding" v={0.35} />
      <Sfx at={T.cats - 8} name="whoosh-fast" v={0.42} />
      <Sfx at={T.cats + 8} name="whip" dir="sfx" v={0.55} />
      {FACE_CUTS.map((c) => (
        <Sfx key={c} at={T.faces + c} name="shutter-modern" dir="sfx" v={0.32} />
      ))}
      {[4, 10, 16, 22, 28].map((t) => (
        <Sfx key={t} at={T.rename + t} name="tick" v={0.24} />
      ))}
      <Sfx at={T.rename + 13} name="whip" dir="sfx" v={0.5} />
      <Sfx at={T.levels - 8} name="whoosh2" v={0.4} />
    </AbsoluteFill>
  );
};

/** Moonlight behind the name as it stands up. */
const EndGlow: React.FC = () => {
  const f = useCurrentFrame();
  return (
    <>
      <div style={{ position: "absolute", left: 960 - 700, top: 500 - 700, width: 1400, height: 1400, borderRadius: "50%", background: "radial-gradient(circle, rgba(90,185,252,0.26) 0%, rgba(90,185,252,0.07) 40%, transparent 66%)", opacity: tw(f, 0, 30, 0.4, 1, outCubic) * (0.9 + 0.1 * Math.sin(f * 0.08)) }} />
      {[0, 1].map((i) => (
        <Ring key={i} x={960} y={540} r={tw(f, i * 6, i * 6 + 40, 30, 1200, outQuint)} width={4} color={i ? "#FFFFFF" : C.moon} opacity={tw(f, i * 6, i * 6 + 40, 0.9, 0, outCubic)} />
      ))}
    </>
  );
};
