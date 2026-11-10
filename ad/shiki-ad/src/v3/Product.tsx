import React from "react";
import { AbsoluteFill, Img, OffthreadVideo, Sequence, staticFile } from "remotion";
import { C, Cat, Dot, Fx, HEAVY, PIXEL, POP, Pose, Ring, SANS, Sprite, Typed, clamp01, inCubic, inOut, inQuart, mix, outCubic, outQuint, rand, spr, tw, useU } from "./kit";
import { Span } from "./timing";

// The product beats: the voice pill, the chat card, "done.", the Home board assembling over
// the night window, the pills, the cats, the levels walk and the iPhone.

const speedOf = (s: Span) => s.auth / Math.max(1, s.dur);

// ---------------------------------------------------------------- 5: the voice pill

export const Pill: React.FC<{ span: Span; reel?: boolean }> = ({ span, reel }) => {
  const u = useU(span);
  const W = 1100;
  const H = 220;
  const grow = spr(u, 0, POP);
  const open = spr(u, 5, { stiffness: 150, damping: 15 });
  const w = H + (W - H) * open;
  const left = 960 - w / 2;
  const out = tw(u, 138, 156, 0, 1, inQuart);

  // the cat drops in to listen
  const LAND = 40;
  const t = u - LAND;
  const dip = t > 0 ? 9 * Math.exp(-t / 8) * Math.cos(t / 3) : 0;
  let pose: Pose = "Listen";
  let catY = 540 - H / 2 + dip;
  if (u < LAND) {
    const p = Math.max(0, (u - 22) / (LAND - 22));
    catY = -30 + (540 - H / 2 + 30) * p * p;
    pose = "Startled";
  } else if (u >= 112) pose = u >= 126 && u < 132 ? "Blink" : "Happy";

  const heard = u >= 112;
  const amp = heard ? tw(u, 112, 126, 1, 0.3, outCubic) : 1;
  const listening = tw(u, 16, 26, 0, 1, outQuint) * tw(u, 44, 50, 1, 0, inCubic);
  const r = 20 + 55 * grow;

  return (
    <AbsoluteFill style={{ transform: `translateY(${out * 330}px) scale(${(1 + (0.07 * u) / 156) * (1 - 0.22 * out)})`, opacity: tw(u, 142, 156, 1, 0, inCubic) }}>
      {/* it is listening: rings from the mic */}
      {[0, 1, 2].map((i) => {
        const p = clamp01((u - 8 - i * 34) / 44);
        return heard || p <= 0 || p >= 1 ? null : <Ring key={i} x={left + H / 2} y={540 + dip} r={r + p * 150} width={2} opacity={(1 - p) * 0.5} />;
      })}
      <div
        style={{
          position: "absolute",
          left,
          top: 540 - H / 2 + dip,
          width: w,
          height: H,
          borderRadius: H / 2,
          background: C.card,
          border: `2px solid #2A3550`,
          boxSizing: "border-box",
          boxShadow: "0 40px 110px rgba(0,0,0,0.6), 0 0 80px rgba(90,185,252,0.10)",
          opacity: tw(u, 2, 9, 0, 1, outQuint),
          overflow: "hidden",
        }}
      >
        <div style={{ position: "absolute", left: 230, top: 0, height: H - 4, display: "flex", alignItems: "center" }}>
          <div style={{ position: "absolute", fontFamily: SANS, fontWeight: 500, fontSize: 62, color: C.muted, opacity: listening, whiteSpace: "nowrap" }}>Listening…</div>
          <Typed text="play Seedhe Maut for me" u={u} at={52} per={2.5} size={62} font={SANS} weight={500} cursor={u >= 50} cursorUntil={132} />
        </div>
        <svg width={44} height={44} viewBox="0 0 24 24" style={{ position: "absolute", right: 56, top: H / 2 - 24, opacity: 0.5 * open }}>
          <path d="M10 5H6a1 1 0 0 0-1 1v12a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-4M14 5h5v5M19 5l-8 8" fill="none" stroke={C.muted} strokeWidth={1.7} strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </div>
      {/* the mic: the dot the cat left behind */}
      <div style={{ position: "absolute", left: left + H / 2 - r, top: 540 + dip - r, width: r * 2, height: r * 2, borderRadius: "50%", background: C.moon, boxShadow: "0 0 50px rgba(90,185,252,0.4)", display: "flex", alignItems: "center", justifyContent: "center", gap: 10 }}>
        {[0, 1, 2, 3].map((i) => {
          const h = (22 + 58 * amp * (0.5 + 0.5 * Math.sin(u * 0.42 + i * 1.3)) * (0.6 + 0.4 * Math.sin(u * 0.17 + i * 2.1))) * grow;
          return <div key={i} style={{ width: 12, height: h, borderRadius: 6, background: C.ink }} />;
        })}
      </div>
      {u >= 22 ? <Cat pose={pose} x={960 + W / 2 - 190} y={catY} scale={6} /> : null}

      <Fx span={span} at={0} name="pop" v={0.34} />
      <Fx span={span} at={LAND - 1} name="click" v={0.25} />
      {(reel ? [52, 84] : [52, 62, 72, 82, 92, 102]).map((a) => (
        <Fx key={a} span={span} at={a} name="tick" v={0.2} />
      ))}
      <Fx span={span} at={112} name="ding" v={0.22} />
      <Fx span={span} at={138} name="whoosh2" v={0.26} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 6 + 7: the chat card, then "done."

export const ChatDone: React.FC<{ spans: { chat: Span; done: Span }; reel?: boolean }> = ({ spans }) => {
  const uC = useU(spans.chat);
  const uD = useU(spans.done);
  const speed = speedOf(spans.chat);
  const inS = spr(uC, 0, { stiffness: 130, damping: 17 });
  const away = tw(uD, 0, 14, 0, 1, inQuart);
  const TOP = 170;
  const SCALE = 1.25 * (1 + (0.035 * Math.min(uC, 144)) / 144);
  // the recording: the two-things card arrives as the window lands; then on to the number card
  const cut = Math.round(84 / speed);
  const video: React.CSSProperties = { width: 900, height: 1100, display: "block" };
  // The recording is the ask window at the top of a taller black canvas, and the window grows
  // with each card. Only the window is shown: the crop follows it down as it unfolds.
  const FAST = { stiffness: 420, damping: 36 };
  const bottom = 157 + (584 - 157) * spr(uC, 26, FAST) + (718 - 584) * spr(uC, 102, FAST);
  return (
    <AbsoluteFill>
      {away < 1 ? (
        <div
          style={{
            position: "absolute",
            left: 960 - 372,
            top: TOP,
            width: 900,
            height: 1100,
            transformOrigin: "372px 370px",
            transform: `perspective(1800px) translateY(${(1 - inS) * 460}px) rotateX(${(1 - inS) * 26 + away * 30}deg) rotateY(${(1 - inS) * -14}deg) scale(${SCALE * (1 - away)})`,
            opacity: tw(uC, 0, 8, 0, 1, outQuint) * tw(uD, 8, 14, 1, 0, inCubic),
            filter: "drop-shadow(0 40px 70px rgba(0,0,0,0.6)) drop-shadow(0 0 50px rgba(90,185,252,0.08))",
          }}
        >
         <div style={{ position: "absolute", inset: 0, clipPath: `inset(22px 178px ${1100 - bottom}px 22px round 16px)` }}>
          <Sequence from={0} durationInFrames={cut} layout="none" name="chat: two things">
            <OffthreadVideo src={staticFile("v3/rec/chat.webm")} muted trimBefore={40} playbackRate={speed} style={video} />
          </Sequence>
          <Sequence from={cut} layout="none" name="chat: the number card">
            <OffthreadVideo src={staticFile("v3/rec/chat.webm")} muted trimBefore={300} playbackRate={speed} style={video} />
          </Sequence>
         </div>
        </div>
      ) : null}

      {/* moonlight rings pulse out from where the card was */}
      {uD > 0
        ? [0, 1, 2, 3].map((i) => {
            const a = 6 + i * 9;
            return <Ring key={i} x={960} y={540} r={tw(uD, a, a + 54, 0, 1060, outQuint)} width={3} color={i < 2 ? C.moon : "#FFFFFF"} opacity={tw(uD, a, a + 54, 0.9, 0, outCubic)} />;
          })
        : null}
      {uD > 8 ? (
        <div style={{ position: "absolute", left: 0, right: 0, top: 540 - 96, display: "flex", justifyContent: "center", opacity: tw(uD, 60, 70, 1, 0, inCubic), transform: `scale(${tw(uD, 60, 70, 1, 0.86, inCubic)})` }}>
          <Typed text="done." u={uD} at={14} per={3} size={168} accentFrom={4} cursorUntil={56} />
        </div>
      ) : null}

      <Fx span={spans.chat} at={0} name="whoosh2" v={0.28} />
      <Fx span={spans.chat} at={26} name="pop" v={0.3} />
      <Fx span={spans.chat} at={102} name="pop" v={0.3} />
      <Fx span={spans.done} at={0} name="whoosh-fast" v={0.3} />
      <Fx span={spans.done} at={12} name="ding" v={0.3} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- the night window

const MOON_CELLS: number[][] = (() => {
  const out: number[][] = [];
  for (let y = 0; y < 18; y++)
    for (let x = 0; x < 18; x++) {
      const a = Math.hypot(x - 8.5, y - 8.5) <= 8.6;
      const b = Math.hypot(x - 4.6, y - 6.4) <= 7.7;
      if (a && !b) out.push([x, y, rand(x * 31 + y * 7) < 0.14 ? 1 : 0]);
    }
  return out;
})();

/** Shiki's own backdrop: a deep night sky, pixel stars, a crescent moon. */
export const NightSky: React.FC<{ u: number }> = ({ u }) => {
  const stars = React.useMemo(
    () =>
      Array.from({ length: 120 }, (_, i) => ({
        x: Math.round(rand(i * 3 + 1) * 480) * 4,
        y: Math.round(rand(i * 3 + 2) * rand(i * 3 + 2) * 270) * 4,
        big: rand(i * 3 + 3) < 0.14,
        s: rand(i + 500),
      })),
    [],
  );
  const cell = 8;
  return (
    <AbsoluteFill style={{ background: "linear-gradient(180deg, #0A1836 0%, #0C1A3A 34%, #0B1326 72%, #0A0D14 100%)", overflow: "hidden" }}>
      <div style={{ position: "absolute", left: 1220, top: -260, width: 1000, height: 1000, borderRadius: "50%", background: "radial-gradient(circle, rgba(90,185,252,0.20) 0%, rgba(90,185,252,0.06) 35%, transparent 62%)" }} />
      {stars.map((s, i) => {
        const tw1 = 0.45 + 0.55 * Math.abs(Math.sin(u * (0.02 + s.s * 0.05) + s.s * 20));
        return s.big ? (
          <React.Fragment key={i}>
            <div style={{ position: "absolute", left: s.x - 4, top: s.y, width: 12, height: 4, background: "#DCEBFF", opacity: tw1 }} />
            <div style={{ position: "absolute", left: s.x, top: s.y - 4, width: 4, height: 12, background: "#DCEBFF", opacity: tw1 }} />
          </React.Fragment>
        ) : (
          <div key={i} style={{ position: "absolute", left: s.x, top: s.y, width: 4, height: 4, background: i % 5 === 0 ? C.moon : "#C9D8F0", opacity: tw1 * 0.8 }} />
        );
      })}
      <div style={{ position: "absolute", left: 1600, top: 96 + Math.round(Math.sin(u * 0.02) * 1.5) * 4, width: cell * 18, height: cell * 18, filter: "drop-shadow(0 0 40px rgba(244,233,200,0.45))" }}>
        {MOON_CELLS.map(([x, y, d], i) => (
          <div key={i} style={{ position: "absolute", left: x * cell, top: y * cell, width: cell, height: cell, background: d ? "#E3D6AC" : "#F4EBC8" }} />
        ))}
      </div>
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 8: the Home board assembles

/** Where the Home window sits when it is whole: the bento board and the next beat's recording share it. */
export const BOARD = { s: 0.8, x: 340, y: 130 };

// The six tiles of Home, as they sit in shots/home.png (the "Lately" tile, which shows real
// file names, is further down the page and never appears).
const TILES: { x: number; y: number; w: number; h: number; from: number[]; turn: number[]; at: number }[] = [
  { x: 428, y: 264, w: 636, h: 424, from: [0, -120], turn: [38, 0, 0], at: 30 }, // the night window
  { x: 69, y: 264, w: 338, h: 742, from: [-760, 60], turn: [0, 50, -8], at: 40 }, // your cat
  { x: 1085, y: 264, w: 375, h: 424, from: [820, -260], turn: [-20, -50, 10], at: 50 }, // level
  { x: 428, y: 709, w: 308, h: 297, from: [-300, 620], turn: [-45, 12, -10], at: 60 }, // ask
  { x: 757, y: 709, w: 307, h: 297, from: [80, 700], turn: [-50, 0, 6], at: 69 }, // quick tools
  { x: 1085, y: 709, w: 375, h: 297, from: [780, 520], turn: [-30, -40, 12], at: 78 }, // numbers
];

export const Bento: React.FC<{ span: Span; reel?: boolean; noHead?: boolean }> = ({ span, reel, noHead }) => {
  const u = useU(span);
  const R = tw(u, 0, 40, 0, 1180, outCubic);
  const skyOut = tw(u, 198, 222, 0, 1, inOut);
  const rx = tw(u, 30, 150, 14, 0, inOut);
  const ry = tw(u, 30, 150, -12, 0, inOut);
  const { s: S, x: OX, y: OY } = BOARD;
  return (
    <AbsoluteFill>
      {/* the turn: the last ring opens onto the night window */}
      <AbsoluteFill style={{ clipPath: R < 1170 ? `circle(${R}px at 960px 540px)` : undefined }}>
        <AbsoluteFill style={{ background: C.ink }} />
        <AbsoluteFill style={{ opacity: 1 - skyOut, transform: `scale(${1.1 - 0.1 * tw(u, 0, 228, 0, 1, outCubic)})` }}>
          <NightSky u={u} />
        </AbsoluteFill>
      </AbsoluteFill>
      <Ring x={960} y={540} r={R} width={3} opacity={tw(u, 0, 40, 0.9, 0, outCubic)} />

      {/* the headline sits where Home's greeting will be */}
      {noHead ? null : (
      <div style={{ position: "absolute", left: 0, right: 0, top: 176, display: "flex", justifyContent: "center", opacity: tw(u, 192, 204, 1, 0, inCubic), transform: `translateY(${tw(u, 192, 204, 0, -18, inCubic)}px)` }}>
        <Typed text="Everything, one place." u={u} at={98} per={2.4} size={92} accentFrom={12} cursor={u > 92} cursorUntil={190} />
      </div>
      )}

      {/* the whole window fades in around the tiles: the next beat is the same window, moving */}
      {skyOut > 0 ? <Img src={staticFile("v3/shots/home.png")} style={{ position: "absolute", left: OX, top: OY, width: 1550 * S, height: 1025 * S, opacity: skyOut }} /> : null}

      <AbsoluteFill style={{ transform: `perspective(2200px) rotateX(${rx}deg) rotateY(${ry}deg)` }}>
        {TILES.map((t, i) => {
          const p = spr(u, t.at, { stiffness: 135, damping: 15 });
          if (p <= 0.001) return null;
          const q = 1 - p;
          const sheen = tw(u, 128 + (t.x / 1550) * 26, 166 + (t.x / 1550) * 26, 0, 1, inOut);
          return (
            <div
              key={i}
              style={{
                position: "absolute",
                left: OX + t.x * S,
                top: OY + t.y * S,
                width: t.w * S,
                height: t.h * S,
                borderRadius: 30 * S,
                overflow: "hidden",
                transform: `perspective(1400px) translate(${t.from[0] * q}px, ${t.from[1] * q}px) rotateX(${t.turn[0] * q}deg) rotateY(${t.turn[1] * q}deg) rotateZ(${t.turn[2] * q}deg) scale(${1 + 0.55 * q})`,
                opacity: Math.min(1, p * 3.2),
                boxShadow: `0 ${30 * (1 - skyOut)}px ${70 * (1 - skyOut)}px rgba(0,0,0,${0.5 * (1 - skyOut)})`,
              }}
            >
              <Img src={staticFile("v3/shots/home.png")} style={{ position: "absolute", left: -t.x * S, top: -t.y * S, width: 1550 * S, height: 1025 * S, maxWidth: "none" }} />
              {sheen > 0 && sheen < 1 ? <div style={{ position: "absolute", top: -20, bottom: -20, left: `${-70 + 240 * sheen}%`, width: "46%", transform: "skewX(-18deg)", background: "linear-gradient(90deg, transparent, rgba(255,255,255,0.28), transparent)" }} /> : null}
            </div>
          );
        })}
      </AbsoluteFill>

      <Fx span={span} at={0} name="whoosh3" v={0.4} />
      {TILES.filter((_, i) => !reel || i % 2 === 0).map((t) => (
        <Fx key={t.at} span={span} at={t.at + 12} name="pop" v={0.26} />
      ))}
      {reel || noHead ? null : [100, 116, 132].map((a) => <Fx key={a} span={span} at={a} name="tick" v={0.2} />)}
      <Fx span={span} at={134} name="sparkle" v={0.2} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- the window recordings

type Cam = { s: number; x: number; y: number };
/** A camera that puts the recording's point (fx, fy) at the screen point (sx, sy), at scale s. */
const look = (s: number, fx: number, fy: number, sx = 960, sy = 540): Cam => ({ s, x: sx - fx * s, y: sy - fy * s });
const camMix = (a: Cam, b: Cam, t: number): Cam => {
  // scale eases in ratio, so a push feels even from start to end
  const s = a.s * Math.pow(b.s / a.s, t);
  const k = Math.abs(b.s - a.s) < 1e-6 ? t : (s - a.s) / (b.s - a.s);
  return { s, x: mix(a.x, b.x, k), y: mix(a.y, b.y, k) };
};
const camCss = (c: Cam): React.CSSProperties => ({ position: "absolute", left: 0, top: 0, width: 1550, height: 1026, transformOrigin: "0 0", transform: `translate(${c.x}px, ${c.y}px) scale(${c.s})`, clipPath: "inset(4px 2px 2px 2px)" }); // the recordings carry a pale line along the top edge

const TABS = look(1.75, 560, 150, 960, 430);
const TABS_END = look(1.75, 640, 150, 960, 430);

// ---------------------------------------------------------------- 9: the folder tab and the pills

const PILLS_RATE = 2.6;

export const Pills: React.FC<{ span: Span; reel?: boolean }> = ({ span, reel }) => {
  const u = useU(span);
  const speed = speedOf(span);
  const p = tw(u, 0, 44, 0, 1, inOut);
  const start: Cam = { s: BOARD.s, x: BOARD.x, y: BOARD.y };
  const cam = camMix(camMix(start, TABS, p), TABS_END, tw(u, 36, 120, 0, 1, inOut));
  // the "mouse" is the cat's paw: it runs along the pills as each one swells in the recording
  const tc = (u * PILLS_RATE) / 60;
  const k = Math.max(-1.2, Math.min(8.15, (tc - 0.2) / 0.6));
  const px = cam.x + (412 + 60.7 * k + 18) * cam.s;
  const py = cam.y + 72 * cam.s;
  return (
    <AbsoluteFill style={{ background: C.ink }}>
      <AbsoluteFill style={{ transform: `perspective(2400px) rotateX(${Math.sin(p * Math.PI) * 5}deg)` }}>
        <div style={camCss(cam)}>
          <OffthreadVideo src={staticFile("v3/rec/pills.mp4")} muted playbackRate={PILLS_RATE * speed} style={{ width: 1550, height: 1026, display: "block" }} />
        </div>
      </AbsoluteFill>
      <Sprite name="mochi-pawpointer" w={12} h={14} scale={4} x={px - 24} y={py + (1 - tw(u, 6, 22, 0, 1, outQuint)) * 120} opacity={tw(u, 6, 16, 0, 1, outQuint)} style={{ filter: "drop-shadow(0 8px 10px rgba(0,0,0,0.5))" }} />
      <Fx span={span} at={2} name="whoosh2" v={0.26} />
      {Array.from({ length: 9 }, (_, i) => i)
        .filter((i) => !reel || i % 3 === 1)
        .map((i) => (
          <Fx key={i} span={span} at={((0.2 + 0.6 * i) * 60) / PILLS_RATE} name="tick" v={0.24} />
        ))}
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 10: the cats, and a name typed

const LATTE = look(2.0, 755, 640, 960, 470);
const WHOLE = look(0.94, 775, 513, 960, 520);
const CATS_RATE = 2.4;

export const Cats: React.FC<{ span: Span; reel?: boolean }> = ({ span, reel }) => {
  const u = useU(span);
  const speed = speedOf(span);
  const p2 = tw(u, 80, 130, 0, 1, inOut);
  const cam = camMix(camMix(TABS_END, LATTE, tw(u, 0, 38, 0, 1, inOut)), WHOLE, p2);
  const cut = Math.round(84 / speed);
  const whip = tw(u, 164, 180, 0, 1, inQuart);
  const video: React.CSSProperties = { width: 1550, height: 1026, display: "block" };
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ background: C.ink, opacity: 1 - whip }} />
      <AbsoluteFill style={{ transform: `translateY(${-whip * 1300}px) perspective(2400px) rotateX(${Math.sin(p2 * Math.PI) * 4}deg) rotateY(${Math.sin(p2 * Math.PI) * -6}deg)`, filter: whip > 0.05 ? `blur(${whip * 10}px)` : undefined }}>
        <div style={camCss(cam)}>
          {/* the pencil opens on Latte and a name is typed */}
          <Sequence from={0} durationInFrames={cut} layout="none" name="rename">
            <OffthreadVideo src={staticFile("v3/rec/rename.mp4")} muted trimBefore={30} playbackRate={speed} style={video} />
          </Sequence>
          {/* then the page glides past all nine */}
          <Sequence from={cut} layout="none" name="cats glide">
            <OffthreadVideo src={staticFile("v3/rec/cats.mp4")} muted playbackRate={CATS_RATE * speed} style={video} />
          </Sequence>
        </div>
      </AbsoluteFill>
      <AbsoluteFill style={{ background: "linear-gradient(180deg, transparent 54%, rgba(10,13,20,0.9) 78%, rgba(10,13,20,0.98) 90%)", opacity: tw(u, 16, 36, 0, 1, outQuint) * tw(u, 150, 164, 1, 0, inCubic) }} />
      <div style={{ position: "absolute", left: 0, right: 0, top: 930, display: "flex", justifyContent: "center", opacity: tw(u, 150, 162, 1, 0, inCubic), transform: `translateY(${tw(u, 150, 162, 0, 20, inCubic)}px)` }}>
        <Typed text="Name your cat." u={u} at={30} per={3} size={84} accentFrom={13} cursor={u > 24} cursorUntil={148} />
      </div>
      <Fx span={span} at={0} name="click" v={0.34} />
      {(reel ? [24, 48] : [24, 33, 42, 51, 60]).map((a) => (
        <Fx key={a} span={span} at={a} name="tick" v={0.24} />
      ))}
      <Fx span={span} at={80} name="whoosh2" v={0.3} />
      <Fx span={span} at={162} name="whoosh-fast" v={0.34} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 11: the levels walk

const LEVEL_Y = [331, 708, 1084, 1461, 1837, 2214];
const HERE = { x: 759, y: 2132 }; // the cat at "You are here", in levels-tall.png

export const Levels: React.FC<{ span: Span; exitX?: number; reel?: boolean }> = ({ span, exitX = 960, reel }) => {
  const u = useU(span);
  const X = 600;
  const off = tw(u, 6, 142, 0, HERE.y - 540, inOut);
  const rise = tw(u, 0, 28, 1150, 0, outQuint);
  const out = tw(u, 166, 180, 0, 1, inQuart);
  const rx = tw(u, 0, 142, 22, 0, inOut);
  const ry = tw(u, 0, 142, -10, 0, inOut);
  // which level the camera has reached, with a small kick as each one passes
  let level = 1;
  let kick = 0;
  LEVEL_Y.forEach((y, i) => {
    const d = 650 - (y - off);
    if (d >= 0 && i + 1 >= level) {
      level = i + 1;
      kick = Math.exp(-d / 46);
    }
  });
  const arrive = u - 140;
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ transform: `translate(${(exitX - (X + 625)) * out}px, 0px) scale(${1 - 0.8 * out})`, opacity: 1 - tw(u, 170, 180, 0, 1, inCubic) }}>
        <div
          style={{
            position: "absolute",
            left: X,
            top: 0,
            width: 1250,
            height: 1080,
            transform: `translateY(${rise}px) perspective(1700px) rotateX(${rx}deg) rotateY(${ry}deg)`,
            maskImage: "linear-gradient(180deg, transparent 0%, #000 16%, #000 82%, transparent 100%)",
            WebkitMaskImage: "linear-gradient(180deg, transparent 0%, #000 16%, #000 82%, transparent 100%)",
          }}
        >
          <div style={{ position: "absolute", inset: 0, overflow: "hidden", maskImage: "linear-gradient(90deg, transparent 0%, #000 7%, #000 93%, transparent 100%)", WebkitMaskImage: "linear-gradient(90deg, transparent 0%, #000 7%, #000 93%, transparent 100%)" }}>
            <Img src={staticFile("v3/shots/levels-tall.png")} style={{ position: "absolute", left: 0, top: -off, width: 1250, height: 4713, maxWidth: "none" }} />
          </div>
          {/* the cat's own moment: amber rings where it stands */}
          {arrive > 0
            ? [0, 1].map((i) => <Ring key={i} x={HERE.x} y={540} r={tw(arrive, i * 9, i * 9 + 34, 40, 190, outQuint)} width={3} color={C.amber} opacity={tw(arrive, i * 9, i * 9 + 34, 0.9, 0, outCubic)} />)
            : null}
        </div>
      </AbsoluteFill>

      <div style={{ position: "absolute", left: 110, top: 286, opacity: tw(u, 8, 26, 0, 1, outQuint) * tw(u, 158, 170, 1, 0, inCubic), transform: `translateX(${tw(u, 8, 30, -40, 0, outQuint) - tw(u, 158, 170, 0, 40, inCubic)}px)` }}>
        <div style={{ fontFamily: SANS, fontWeight: 600, fontSize: 24, letterSpacing: "0.32em", color: C.muted }}>LEVEL</div>
        <div style={{ fontFamily: PIXEL, fontWeight: 700, fontSize: 250, lineHeight: 1, color: level >= 6 ? C.amber : C.text, transform: `scale(${1 + 0.1 * kick})`, transformOrigin: "left bottom", marginTop: -6 }}>0{level}</div>
        <div style={{ marginTop: 26 }}>
          <Typed text={"Level up\ntogether."} u={u} at={34} per={3} size={82} accentFrom={17} cursor={u > 28} cursorUntil={156} style={{ lineHeight: 1.12 }} />
        </div>
      </div>

      {out > 0.5 ? <Dot x={exitX} y={540} r={6 * tw(u, 172, 180, 0, 1, outQuint)} /> : null}

      <Fx span={span} at={0} name="whoosh2" v={0.26} />
      {(reel ? [60, 110] : [34, 58, 78, 96, 114]).map((a) => (
        <Fx key={a} span={span} at={a} name="tick" v={0.24} />
      ))}
      <Fx span={span} at={138} name="ding" v={0.3} />
      <Fx span={span} at={164} name="whoosh-fast" v={0.3} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 12: the iPhone

/** A stand-in photo: the night sky again, so nothing off-palette comes in. */
const Photo: React.FC<{ seed: number; style?: React.CSSProperties }> = ({ seed, style }) => {
  const tones = [
    ["#0C1A3A", "#1971C2"],
    ["#141A27", "#5AB9FC"],
    ["#1B2A4A", "#9CC6F2"],
    ["#0E121B", "#F2C14E"],
    ["#12233F", "#C9A27A"],
  ];
  const [a, b] = tones[seed % tones.length];
  return (
    <div style={{ background: `linear-gradient(${150 + seed * 23}deg, ${a} 20%, ${b} 130%)`, overflow: "hidden", ...style }}>
      <div style={{ position: "absolute", left: `${18 + ((seed * 29) % 50)}%`, top: `${16 + ((seed * 13) % 30)}%`, width: "20%", aspectRatio: "1", borderRadius: "50%", background: "#F4EBC8", opacity: 0.9 }} />
      <div style={{ position: "absolute", left: "-10%", right: "-10%", bottom: "-30%", height: "62%", borderRadius: "50%", background: "#0A0D14", opacity: 0.75 }} />
    </div>
  );
};

export const Phone: React.FC<{ span: Span }> = ({ span }) => {
  const u = useU(span);
  const inS = spr(u, 0, HEAVY);
  const out = tw(u, 130, 144, 0, 1, inQuart);
  const PX = 560;
  const PY = 480;
  const PW = 330;
  const PH = 670;
  const press = u >= 30 && u < 36 ? 0.94 : 1;
  const fly = tw(u, 38, 80, 0, 1, inOut);
  const toast = spr(u, 60, POP);
  const TX = 1010;
  const TY = 400;
  // the photo's flight: up and over, from the phone to the toast
  const m = 1 - fly;
  const fx = m * m * PX + 2 * m * fly * 900 + fly * fly * (TX + 610);
  const fy = m * m * (PY - 40) + 2 * m * fly * 120 + fly * fly * (TY + 80);
  const fs = mix(86, 112, fly) + 210 * Math.sin(fly * Math.PI);
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ transform: `scale(${1 - out})`, opacity: 1 - tw(u, 136, 144, 0, 1, inCubic) }}>
        {/* the phone: a plain slab, no marks */}
        <div
          style={{
            position: "absolute",
            left: PX - PW / 2,
            top: PY - PH / 2,
            width: PW,
            height: PH,
            borderRadius: 56,
            background: "#1A2030",
            border: "2px solid #2E3850",
            boxSizing: "border-box",
            boxShadow: "0 50px 120px rgba(0,0,0,0.6), 0 0 90px rgba(90,185,252,0.10)",
            transform: `perspective(1600px) translateY(${(1 - inS) * 560}px) rotateY(${14 - 4 * inS}deg) rotateX(${(1 - inS) * 20 + 3}deg) rotateZ(${(1 - inS) * -8}deg)`,
            opacity: Math.min(1, inS * 3),
          }}
        >
          <div style={{ position: "absolute", inset: 12, borderRadius: 44, background: C.panel, overflow: "hidden" }}>
            <div style={{ position: "absolute", left: "50%", top: 14, width: 84, height: 24, marginLeft: -42, borderRadius: 12, background: "#05070B" }} />
            <div style={{ position: "absolute", left: 24, top: 62, fontFamily: PIXEL, fontWeight: 600, fontSize: 34, color: C.text }}>Shiki</div>
            <div style={{ position: "absolute", left: 24, top: 104, fontFamily: SANS, fontWeight: 500, fontSize: 17, color: C.muted }}>Send to your PC, full quality</div>
            {Array.from({ length: 9 }, (_, i) => {
              const sel = i === 4;
              return (
                <div key={i} style={{ position: "absolute", left: 20 + (i % 3) * 91, top: 150 + Math.floor(i / 3) * 91, width: 84, height: 84 }}>
                  <Photo seed={i + 1} style={{ position: "absolute", inset: 0, borderRadius: 14, opacity: sel && fly > 0.02 ? 0.25 : 1 }} />
                  {sel ? <div style={{ position: "absolute", inset: -3, borderRadius: 17, border: `3px solid ${C.moon}`, opacity: spr(u, 16, POP) }} /> : null}
                </div>
              );
            })}
            <div style={{ position: "absolute", left: 20, right: 20, bottom: 30, height: 62, borderRadius: 31, background: C.moon, color: C.ink, fontFamily: SANS, fontWeight: 700, fontSize: 22, display: "flex", alignItems: "center", justifyContent: "center", transform: `scale(${press})` }}>
              {u < 80 ? "Send 1 photo" : "Sent"}
            </div>
          </div>
        </div>

        {/* over the air: no cable in the picture */}
        <svg width={1920} height={1080} style={{ position: "absolute" }}>
          {[0, 1, 2].map((i) => {
            const p = clamp01((u - 34 - i * 7) / 40);
            return p <= 0 || p >= 1 ? null : <path key={i} d={`M ${780 + i * 34} ${250 - i * 4} A ${70 + i * 40} ${70 + i * 40} 0 0 1 ${780 + i * 34} ${390 + i * 40}`} fill="none" stroke={C.moon} strokeWidth={3} strokeLinecap="round" opacity={Math.sin(p * Math.PI) * 0.8} />;
          })}
        </svg>

        {/* the PC's toast */}
        <div
          style={{
            position: "absolute",
            left: TX,
            top: TY,
            width: 690,
            height: 160,
            borderRadius: 30,
            background: C.card,
            border: `1.5px solid ${C.line}`,
            boxSizing: "border-box",
            boxShadow: "0 30px 80px rgba(0,0,0,0.55)",
            transform: `translateX(${(1 - toast) * 260}px)`,
            opacity: Math.min(1, toast * 3),
            overflow: "hidden",
          }}
        >
          <div style={{ position: "absolute", left: 150, top: 34, fontFamily: SANS, fontWeight: 700, fontSize: 36, color: C.text }}>1 photo from iPhone</div>
          <div style={{ position: "absolute", left: 150, top: 88, fontFamily: SANS, fontWeight: 500, fontSize: 22, color: C.muted }}>4032 × 3024 · 5.8 MB · full quality</div>
          <div style={{ position: "absolute", left: 0, bottom: 0, height: 4, width: `${tw(u, 62, 86, 0, 100, inOut)}%`, background: C.moon }} />
        </div>
        <Sprite name="mochi-head" w={20} h={20} scale={5} x={TX + 26 + (1 - toast) * 260} y={TY + 26} opacity={Math.min(1, toast * 3)} />

        {fly > 0 ? (
          <div style={{ position: "absolute", left: fx - fs / 2, top: fy - fs / 2, width: fs, height: fs, transform: `rotate(${Math.sin(fly * Math.PI) * -9}deg)`, filter: `drop-shadow(0 ${20 * Math.sin(fly * Math.PI) + 4}px 30px rgba(0,0,0,0.5))` }}>
            <Photo seed={5} style={{ position: "absolute", inset: 0, borderRadius: fs * 0.16, border: fly >= 1 ? undefined : `3px solid ${C.moon}`, boxSizing: "border-box" }} />
          </div>
        ) : null}

        <div style={{ position: "absolute", left: 0, right: 0, top: 880, display: "flex", justifyContent: "center", opacity: tw(u, 122, 132, 1, 0, inCubic) }}>
          <Typed text="Full quality. No cables." u={u} at={50} per={2.2} size={84} accentFrom={23} cursor={u > 44} cursorUntil={120} />
        </div>
      </AbsoluteFill>
      {out > 0.5 ? <Dot x={960} y={540} r={6 * tw(u, 136, 144, 0, 1, outQuint)} /> : null}

      <Fx span={span} at={0} name="pop" v={0.3} />
      <Fx span={span} at={30} name="click" v={0.34} />
      <Fx span={span} at={38} name="whoosh2" v={0.3} />
      <Fx span={span} at={62} name="ding" dir="sfx" v={0.3} />
      {[50, 66, 82, 98].map((a) => (
        <Fx key={a} span={span} at={a} name="tick" v={0.18} />
      ))}
      <Fx span={span} at={128} name="whoosh-fast" v={0.3} />
    </AbsoluteFill>
  );
};
