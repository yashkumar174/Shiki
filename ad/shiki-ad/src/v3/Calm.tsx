import React from "react";
import { AbsoluteFill } from "remotion";
import { C, Cat, Dot, Fx, Icon, IconName, POP, Pose, SANS, SNAP, Sprite, Typed, cubic, inCubic, inOut, inQuart, outQuint, spr, tw, useU, walkPose } from "./kit";
import { Span } from "./timing";

// The calm after the collapse, as one continuous shot: the point opens into a thin line, the
// cat drops onto it, the promise is typed, and then everything the cat can reach fans out.

const CX = 960;
const TEXT_X = 710;
const GROUND = 640; // where the line rests once the cat's weight is on it

type Node = { ax: number; ay: number; side: "l" | "r"; icon: IconName; label: string; sub?: string; bend?: number };
const NODES: Node[] = [
  { ax: 670, ay: 280, side: "l", icon: "chat", label: "WhatsApp Mummy", sub: "“on my way”" },
  { ax: 1250, ay: 250, side: "r", icon: "note", label: "play Seedhe Maut" },
  { ax: 1310, ay: 548, side: "r", icon: "photo", label: "3 photos from my iPhone", bend: -70 },
  { ax: 1240, ay: 840, side: "r", icon: "bell", label: "remind me at 5" },
  { ax: 650, ay: 800, side: "l", icon: "mail", label: "email the invoice to Tausif" },
];
const nodeAt = (i: number) => 26 + i * 10;
const curveOf = (n: Node) => {
  const dx = n.ax - CX;
  const p0 = [CX, 540];
  const p1 = [CX + dx * 0.6, 540 + (n.bend ?? 0)];
  const p2 = [n.ax - dx * 0.5, n.ay - (n.bend ?? 0) * 0.4];
  const p3 = [n.ax, n.ay];
  return { p0, p1, p2, p3, d: `M${p0[0]} ${p0[1]} C${p1[0]} ${p1[1]} ${p2[0]} ${p2[1]} ${p3[0]} ${p3[1]}` };
};

export const Calm: React.FC<{ spans: { line: Span; drop: Span; type: Span; hub: Span }; reel?: boolean }> = ({ spans, reel }) => {
  const uL = useU(spans.line);
  const uD = useU(spans.drop);
  const uT = useU(spans.type);
  const uH = useU(spans.hub);

  // ---- the line: it grows from the point, sinks under the cat, and is drawn back in for the hub
  const LAND = 40;
  const sink = spr(uD, LAND, { stiffness: 150, damping: 11 });
  const lineY = 540 + (GROUND - 540) * sink;
  const t = uD - LAND;
  const sag = t > 0 ? 30 * Math.exp(-t / 11) * Math.cos(t / 3.4) : 0;
  const half = tw(uL, 0, 58, 0, 800, outQuint) * tw(uH, 0, 22, 1, 0, inQuart);
  const gap = reel ? 0 : 232 * spr(uL, 28, SNAP) * tw(uD, 2, 16, 1, 0, inCubic);
  const labelO = reel ? 0 : tw(uL, 38, 62, 0, 1, outQuint) * tw(uD, 0, 9, 1, 0, inCubic);

  // ---- the cat
  let catX = CX;
  let catY = lineY + sag;
  let pose: Pose = "Idle";
  let catOn = uD >= 14;
  if (uD < LAND) {
    const p = Math.max(0, (uD - 14) / (LAND - 14));
    catY = -40 + (540 + 40) * p * p;
    pose = "Startled";
  } else if (uT < 0) {
    pose = uD < LAND + 9 ? "Pop" : uD >= 72 && uD < 79 ? "Blink" : "Idle";
  } else if (uH < 0) {
    catX = tw(uT, 0, 30, CX, TEXT_X - 190, inOut);
    if (uT < 30) {
      pose = walkPose(uT, 6);
      catY -= Math.abs(Math.sin((uT / 6) * Math.PI)) * 8;
    } else if (uT < 68) pose = "Listen";
    else if (uT < 106) pose = Math.floor(uT / 7) % 2 === 0 ? "Talk" : "Idle";
    else if (uT < 150) pose = "Happy";
    else pose = "Idle";
  } else {
    catX = tw(uH, 0, 26, TEXT_X - 190, CX, inOut);
    if (uH < 26) {
      pose = walkPose(uH, 6);
      catY -= Math.abs(Math.sin((uH / 6) * Math.PI)) * 8;
    } else if (uH < 96) {
      // a word for every task that lands
      const k = (uH - 42) % 10;
      pose = uH > 42 && k < 5 ? "Talk" : "Listen";
    } else if (uH >= 138 && uH < 145) pose = "Blink";
    else if (uH < 188) pose = "Happy";
    else if (uH < 197) {
      pose = "Pop";
      catY += 6; // gathers itself
    } else {
      pose = "Pop";
      catY -= tw(uH, 197, 214, 0, 960, inQuart);
    }
    catOn = uH < 215;
  }

  const y = lineY;
  const sagX = catX;
  const x0 = CX - half;
  const x1 = CX + half;
  const linePath = (a: number, b: number) => {
    if (b - a < 1) return "";
    // the line bends where the cat stands
    if (Math.abs(sag) > 0.3 && a < sagX - 170 && b > sagX + 170) return `M${a} ${y} L${sagX - 170} ${y} Q${sagX} ${y + sag * 2} ${sagX + 170} ${y} L${b} ${y}`;
    return `M${a} ${y} L${b} ${y}`;
  };

  const glint = tw(uL, 62, 116, 0, 1, inOut);
  const hubOn = uH > 0;
  const core = spr(uH, 22, { stiffness: 90, damping: 16 });

  return (
    <AbsoluteFill>
      {/* the glow the curves grow out of */}
      {hubOn ? (
        <div style={{ position: "absolute", left: CX - 340, top: 540 - 340, width: 680, height: 680, borderRadius: "50%", background: "radial-gradient(circle, rgba(90,185,252,0.3) 0%, rgba(90,185,252,0.08) 40%, transparent 68%)", opacity: core * tw(uH, 196, 212, 1, 0.4, inCubic), transform: `scale(${0.6 + 0.4 * core + 0.03 * Math.sin(uH * 0.08)})` }} />
      ) : null}

      <svg width={1920} height={1080} style={{ position: "absolute" }}>
        <defs>
          <linearGradient id="v3-line" gradientUnits="userSpaceOnUse" x1={160} x2={1760} y1={0} y2={0}>
            <stop offset="0" stopColor={C.text} stopOpacity="0" />
            <stop offset="0.12" stopColor={C.text} stopOpacity="0.62" />
            <stop offset="0.88" stopColor={C.text} stopOpacity="0.62" />
            <stop offset="1" stopColor={C.text} stopOpacity="0" />
          </linearGradient>
        </defs>
        {half > 1 ? (
          gap > 2 ? (
            <>
              <path d={linePath(x0, CX - gap)} stroke="url(#v3-line)" strokeWidth={2} fill="none" />
              <path d={linePath(CX + gap, x1)} stroke="url(#v3-line)" strokeWidth={2} fill="none" />
            </>
          ) : (
            <path d={linePath(x0, x1)} stroke={half > 700 ? "url(#v3-line)" : C.text} strokeOpacity={half > 700 ? 1 : 0.62} strokeWidth={2} fill="none" />
          )
        ) : null}

        {/* the hub: thin curves from the cat to everything it can do */}
        {hubOn
          ? NODES.map((n, i) => {
              const c = curveOf(n);
              const out = tw(uH, 174 + (4 - i) * 3, 190 + (4 - i) * 3, 0, 1, inQuart);
              const p = tw(uH, nodeAt(i), nodeAt(i) + 30, 0, 1, outQuint) * (1 - out);
              if (p <= 0.001) return null;
              const head = cubic(c.p0, c.p1, c.p2, c.p3, p);
              const run = uH > nodeAt(i) + 34 && out === 0 ? ((uH - nodeAt(i) - 34) / 56 + i * 0.37) % 1 : -1;
              const pulse = run >= 0 ? cubic(c.p0, c.p1, c.p2, c.p3, run) : null;
              return (
                <g key={i}>
                  <path d={c.d} pathLength={1} strokeDasharray="1 1" strokeDashoffset={1 - p} stroke={C.moon} strokeOpacity={0.75} strokeWidth={2} fill="none" strokeLinecap="round" />
                  {p < 0.995 ? <circle cx={head[0]} cy={head[1]} r={5} fill={C.moon} /> : <circle cx={n.ax} cy={n.ay} r={5} fill={C.moon} />}
                  {pulse ? <circle cx={pulse[0]} cy={pulse[1]} r={3.5} fill="#fff" opacity={Math.sin(run * Math.PI) * 0.9} /> : null}
                </g>
              );
            })
          : null}
      </svg>

      {/* the tiny label that sits in the line */}
      {labelO > 0.01 ? (
        <div style={{ position: "absolute", left: 0, right: 0, top: lineY - 21, textAlign: "center", fontFamily: SANS, fontWeight: 500, fontSize: 30, color: "#B9C3D4", opacity: labelO, letterSpacing: `${0.06 + 0.22 * (1 - tw(uL, 38, 74, 0, 1, outQuint))}em` }}>
          <span style={{ color: C.moon }}>‹</span>&nbsp; your desktop has a cat &nbsp;<span style={{ color: C.moon }}>›</span>
        </div>
      ) : null}

      {/* the point the old way fell into, opening into the line; then a glint of moonlight along it */}
      <Dot x={CX} y={540} r={6} opacity={tw(uL, 4, 26, 1, 0, inCubic)} />
      {glint > 0 && glint < 1 && half > 700 ? <Dot x={160 + 1600 * glint} y={lineY} r={4} opacity={Math.sin(glint * Math.PI)} glow={2} /> : null}

      {/* landing dust */}
      {t > 0 && t < 26
        ? [-1, 1].map((dir) =>
            [0, 1, 2].map((k) => {
              const p = tw(t, 0, 16 + k * 4, 0, 1, outQuint);
              return <div key={`${dir}${k}`} style={{ position: "absolute", left: Math.round(CX + dir * (96 + p * (40 + k * 34)) - 5), top: Math.round(catY - 14 - Math.sin(p * Math.PI) * (16 + k * 10)), width: 10, height: 10, background: C.text, opacity: (1 - p) * 0.7 }} />;
            }),
          )
        : null}

      {/* the promise */}
      {uT > 20 && uH < 0 ? (
        <div style={{ position: "absolute", left: TEXT_X, top: GROUND - 34 - 268 }}>
          <div style={{ opacity: tw(uT, 150, 160, 1, 0, inCubic), transform: `translateX(${tw(uT, 150, 160, 0, -50, inCubic)}px)` }}>
            <Typed text="Say it." u={uT} at={30} per={4} size={128} cursorUntil={66} />
          </div>
          <div style={{ opacity: tw(uT, 153, 163, 1, 0, inCubic), transform: `translateX(${tw(uT, 153, 163, 0, -50, inCubic)}px)` }}>
            <Typed text="It happens." u={uT} at={68} per={3.4} size={128} accentFrom={10} cursor={uT >= 62} cursorUntil={150} />
          </div>
        </div>
      ) : null}

      {/* the dot the cat leaves behind: it becomes the voice pill's mic */}
      {uH > 186 ? <Dot x={CX} y={540} r={20 * spr(uH, 190, POP)} /> : null}

      {catOn ? <Cat pose={pose} x={catX} y={catY} scale={10} /> : null}
      {uT > 106 && uT < 140 && uH < 0 ? (
        <Sprite name="mochi-float-Sparkle" w={3} h={3} scale={8} x={catX + 96} y={catY - 232 - tw(uT, 106, 140, 0, 30, outQuint)} opacity={tw(uT, 124, 140, 1, 0, inCubic)} />
      ) : null}

      {/* the task nodes */}
      {hubOn
        ? NODES.map((n, i) => {
            const pop = spr(uH, nodeAt(i) + 18, POP);
            const out = tw(uH, 174 + (4 - i) * 3, 188 + (4 - i) * 3, 0, 1, inQuart);
            if (pop <= 0.001 || out >= 1) return null;
            const bob = Math.sin(uH * 0.045 + i * 1.9) * 5;
            const dx = (CX - n.ax) * 0.55 * out;
            const dy = (540 - n.ay) * 0.55 * out;
            const left = n.side === "l";
            return (
              <div
                key={i}
                style={{
                  position: "absolute",
                  ...(left ? { right: 1920 - n.ax + 16 } : { left: n.ax + 16 }),
                  top: n.ay - 42,
                  height: 84,
                  transform: `translate(${dx}px, ${dy + bob}px) scale(${(0.7 + 0.3 * pop) * (1 - 0.5 * out)})`,
                  transformOrigin: left ? "right center" : "left center",
                  opacity: Math.min(1, pop * 2.5) * (1 - out),
                  display: "flex",
                  alignItems: "center",
                  gap: 16,
                  padding: "0 32px 0 14px",
                  borderRadius: 42,
                  background: C.card,
                  border: `1.5px solid ${C.line}`,
                  boxShadow: "0 18px 50px rgba(0,0,0,0.5), 0 0 34px rgba(90,185,252,0.08)",
                  whiteSpace: "nowrap",
                }}
              >
                <div style={{ width: 56, height: 56, borderRadius: 28, background: "rgba(90,185,252,0.14)", display: "flex", alignItems: "center", justifyContent: "center" }}>
                  <Icon name={n.icon} size={32} color={C.moon} stroke={1.9} />
                </div>
                <div style={{ fontFamily: SANS, fontWeight: 600, fontSize: 30, color: C.text }}>
                  {n.label}
                  {n.sub ? <span style={{ color: C.muted, fontWeight: 500 }}>&nbsp; {n.sub}</span> : null}
                </div>
              </div>
            );
          })
        : null}

      {/* sound */}
      <Fx span={spans.line} at={2} name="whoosh2" v={0.22} />
      <Fx span={spans.drop} at={14} name="whoosh-fast" v={0.3} />
      <Fx span={spans.drop} at={LAND - 1} name="impact" v={0.3} />
      <Fx span={spans.drop} at={LAND + 12} name="meow" v={0.42} />
      {(reel ? [30, 68, 92] : [30, 38, 46, 54, 68, 76, 84, 92, 100]).map((a) => (
        <Fx key={a} span={spans.type} at={a} name="tick" v={0.22} />
      ))}
      <Fx span={spans.type} at={106} name="sparkle" v={0.2} />
      <Fx span={spans.hub} at={22} name="whoosh2" v={0.25} />
      {NODES.map((_, i) => (
        <Fx key={i} span={spans.hub} at={nodeAt(i) + 18} name="pop" v={0.28} />
      ))}
      <Fx span={spans.hub} at={176} name="whoosh-fast" v={0.32} />
    </AbsoluteFill>
  );
};
