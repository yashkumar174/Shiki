import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { backOut, c, easeIn, easeOut, sans, tween } from "../theme";
import { cam, Caption, Clip, Layout, Pill, s, Slam, smooth, useLayout, Whip } from "./parts";

// When the lines start, in this scene's frames.
export const MUMMY = 15;
export const TWO = 252;
export const RAHUL = 408;
export const SKY_RAHUL = 555;
const CHAT_IN = 238;
export const TAP = 384; // the paw presses "Do it"
const END = s(12);
const DIVE = END - 24; // the camera dives into Rahul's number field

// Where things were on the real screen (Windows' 1536x864 at 125% = this 1920x1080 frame).
const CHAT = { x: 1187.5, y: 367.5 };
const CAT = { x: 1842.5, y: 927.5 };
const PILL_WIDE = { x: 1130, y: 836, w: 690, h: 112 };
const PILL_TALL = { x: 1220, y: 836, w: 600, h: 150 }; // narrower, so it fits the 9:16 frame with the cat
const DOIT = { x: CHAT.x + 144, y: CHAT.y + 444 };
const FIELD = { x: CHAT.x + 391, y: CHAT.y + 355 };

// The real chat window, cut into layers (public/slices) so it can build itself up.
const SLICES = {
  m: { user: [150, 87, 562, 79], sky: [30, 178, 682, 28], card: [76, 210, 636, 286], title: [88, 238, 610, 24], item1: [88, 274, 610, 50], item2: [88, 332, 610, 76], buttons: [88, 416, 610, 58] },
  r: { user: [150, 111, 562, 55], sky: [30, 178, 682, 28], card: [76, 210, 636, 286], title: [88, 238, 610, 52], label: [88, 302, 610, 26], field: [88, 328, 610, 54], hint: [88, 385, 610, 23], buttons: [88, 416, 610, 58] },
} as const;
const ORDER = { m: ["user", "sky", "card", "title", "item1", "item2", "buttons"], r: ["user", "sky", "card", "title", "label", "field", "hint", "buttons"] } as const;

/** A warm desktop: wallpaper and a taskbar, where Shiki's real windows sit. */
const Desktop: React.FC<{ tall: boolean }> = ({ tall }) => (
  <AbsoluteFill>
    <div style={{ position: "absolute", left: 0, right: 0, top: tall ? -900 : 0, bottom: 0, background: "linear-gradient(155deg, #22161F 0%, #4A2433 38%, #9C4A3A 72%, #E39553 100%)" }} />
    <div style={{ position: "absolute", left: -200, top: 380, width: 1500, height: 900, borderRadius: "50%", background: "radial-gradient(closest-side, rgba(255,190,120,0.35), transparent)", rotate: "-18deg" }} />
    <div style={{ position: "absolute", left: 900, top: -300, width: 1300, height: 800, borderRadius: "50%", background: "radial-gradient(closest-side, rgba(255,120,110,0.22), transparent)", rotate: "24deg" }} />
    <div
      style={{
        position: "absolute",
        left: 0,
        right: 0,
        bottom: 0,
        height: 60,
        background: "rgba(26,22,21,0.88)",
        borderTop: "1px solid rgba(255,255,255,0.08)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        gap: 10,
      }}
    >
      {[
        <div key="s" style={{ display: "grid", gridTemplateColumns: "repeat(2, 11px)", gap: 3 }}>
          {[0, 1, 2, 3].map((k) => (
            <div key={k} style={{ width: 11, height: 11, borderRadius: 2, background: c.text }} />
          ))}
        </div>,
        <div key="q" style={{ width: 22, height: 22, borderRadius: "50%", border: `3px solid ${c.text}` }} />,
        <div key="f" style={{ width: 28, height: 22, borderRadius: 4, background: "#F2C14E" }} />,
        <div key="b" style={{ width: 28, height: 28, borderRadius: "50%", background: "conic-gradient(#E85A3C, #F6A945, #7FB685, #E85A3C)" }} />,
        <div key="m" style={{ width: 28, height: 22, borderRadius: 4, background: "#D9D4CC" }} />,
        <div key="n" style={{ width: 26, height: 26, borderRadius: 7, background: "#7FB685" }} />,
      ].map((icon, i) => (
        <div key={i} style={{ width: 48, height: 48, borderRadius: 8, display: "flex", alignItems: "center", justifyContent: "center", background: i === 0 ? "rgba(255,255,255,0.06)" : undefined }}>
          {icon}
        </div>
      ))}
      <div style={{ position: "absolute", right: 22, textAlign: "right", fontFamily: sans, color: c.text, fontSize: 17, lineHeight: 1.3 }}>
        2:41 PM
        <br />
        26-10-2026
      </div>
    </div>
  </AbsoluteFill>
);

/** Camera shots: [from, to, x, y, zoom at start, zoom at end]. Each glides in from the last one. */
type Shot = [number, number, number, number, number, number];
const SHOTS_WIDE: Shot[] = [
  [0, 150, 1480, 820, 1.55, 1.7],
  [150, CHAT_IN, 1460, 880, 2.2, 2.35],
  [CHAT_IN, 330, 1560, 640, 1.45, 1.6],
  [330, RAHUL - 8, 1360, 780, 2.3, 2.45],
  [RAHUL - 8, SKY_RAHUL, 1480, 860, 1.6, 1.72],
  [SKY_RAHUL, DIVE, 1560, 700, 1.6, 1.78],
];
// 9:16 sees a tall, narrow slice: the pill and the cat, then the whole window, then up close.
const SHOTS_TALL: Shot[] = [
  [0, 150, 1530, 380, 1.4, 1.5],
  [150, CHAT_IN, 1520, 520, 1.75, 1.85],
  [CHAT_IN, 330, 1560, 420, 1.45, 1.5],
  [330, RAHUL - 8, 1360, 700, 2.3, 2.45],
  [RAHUL - 8, SKY_RAHUL, 1540, 470, 1.6, 1.65],
  [SKY_RAHUL, DIVE, 1560, 430, 1.5, 1.6],
];
const GLIDE = 14;

/** Keep the frame on the desktop (in 9:16 it may also look up into the wallpaper above the screen). */
const clampCam = (L: Layout, x: number, y: number, z: number) =>
  [Math.min(1920 - L.W / 2 / z, Math.max(L.W / 2 / z, x)), Math.min(1080 - L.H / 2 / z, Math.max(L.H / 2 / z - (L.v ? 900 : 0), y)), z] as const;

const shotAt = (L: Layout, i: number, k: number) => {
  const [, , x, y, z0, z1] = (L.v ? SHOTS_TALL : SHOTS_WIDE)[i];
  return clampCam(L, x, y, z0 + (z1 - z0) * smooth(k));
};

/** Where the camera is: drifting within a shot, gliding between shots, diving at the end. */
const camera = (L: Layout, f: number): readonly [number, number, number] => {
  const SHOTS = L.v ? SHOTS_TALL : SHOTS_WIDE;
  if (f >= DIVE) {
    const [x0, y0, z0] = shotAt(L, SHOTS.length - 1, 1);
    const k = tween(f, DIVE, END, 0, 1, easeIn);
    return [x0 + (FIELD.x - x0) * Math.min(1, k * 2), y0 + (FIELD.y - y0) * Math.min(1, k * 2), z0 * Math.pow(14 / z0, k)];
  }
  const i = Math.max(0, SHOTS.findIndex(([a, b]) => f >= a && f < b));
  const [a, b] = SHOTS[i];
  const here = shotAt(L, i, (f - a) / (b - a));
  if (i === 0 || f - a >= GLIDE) return here;
  const prev = shotAt(L, i - 1, 1);
  const g = smooth((f - a) / GLIDE);
  return [prev[0] + (here[0] - prev[0]) * g, prev[1] + (here[1] - prev[1]) * g, prev[2] + (here[2] - prev[2]) * g];
};

/** A soft amber outline that pulses around a part of a real window. */
const Glow: React.FC<{ x: number; y: number; w: number; h: number; from: number; to: number; r?: number }> = ({ x, y, w, h, from, to, r = 14 }) => {
  const frame = useCurrentFrame();
  if (frame < from || frame > to) return null;
  const pulse = 0.5 + 0.5 * Math.sin(((frame - from) / 22) * Math.PI);
  return (
    <div
      style={{
        position: "absolute",
        left: x - 6,
        top: y - 6,
        width: w + 12,
        height: h + 12,
        borderRadius: r + 6,
        border: `3px solid ${c.amber}`,
        boxShadow: `0 0 ${18 + pulse * 26}px rgba(246,169,69,${0.45 + pulse * 0.35})`,
        opacity: tween(frame, from, from + 8, 0, 1) * tween(frame, to - 8, to, 1, 0),
        scale: tween(frame, from, from + 12, 1.15, 1, backOut),
      }}
    />
  );
};

/** One layer of the real chat window, flying into its place. */
const Layer: React.FC<{ v: "m" | "r"; name: string; at: number; outAt?: number; i: number }> = ({ v, name, at, outAt, i }) => {
  const frame = useCurrentFrame();
  const [x, y, w, h] = (SLICES[v] as Record<string, readonly number[]>)[name];
  const f = frame - at;
  if (f < 0) return null;
  const out = outAt === undefined ? 0 : tween(frame, outAt, outAt + 8, 0, 1, easeIn);
  if (out >= 1) return null;
  const isCard = name === "card";
  return (
    <Img
      src={staticFile(`slices/${v}-${name}.png`)}
      style={{
        position: "absolute",
        left: x,
        top: y,
        width: w,
        height: h,
        opacity: tween(f, 0, 7, 0, 1) * (1 - out),
        translate: `${name === "user" ? tween(f, 0, 14, 40, 0, easeOut) : 0}px ${(isCard ? 0 : tween(f, 0, 14, 22, 0, backOut)) - out * 26}px`,
        scale: isCard ? `1 ${tween(f, 0, 14, 0.82, 1, backOut)}` : 1,
        transformOrigin: "50% 0%",
        filter: `blur(${tween(f, 0, 6, 4, 0) + out * 4}px)`,
        zIndex: i,
      }}
    />
  );
};

/** The real Ask Sky window: grows out of the voice pill, builds row by row, then swaps to Rahul's card. */
const ChatBuild: React.FC = () => {
  const frame = useCurrentFrame();
  const PILL = useLayout().v ? PILL_TALL : PILL_WIDE;
  if (frame < CHAT_IN - 2) return null;
  // the pill's dark capsule stretches into the window's shape
  const m = tween(frame, CHAT_IN - 2, CHAT_IN + 12, 0, 1, easeOut);
  const morph = {
    x: PILL.x + (CHAT.x + 22 - PILL.x) * m,
    y: PILL.y + (CHAT.y + 22 - PILL.y) * m,
    w: PILL.w + (700 - PILL.w) * m,
    h: PILL.h + (556 - PILL.h) * m,
    r: 56 + (30 - 56) * m,
  };
  const shell = tween(frame, CHAT_IN + 8, CHAT_IN + 14, 0, 1);
  const toRahul = tween(frame, SKY_RAHUL - 2, SKY_RAHUL + 6, 0, 1);
  const dim = frame >= RAHUL - 8 && frame < SKY_RAHUL ? tween(frame, RAHUL - 8, RAHUL, 1, 0.45) : 1;
  const press = frame < TAP ? 1 : frame < TAP + 4 ? 0.92 : tween(frame, TAP + 4, TAP + 12, 0.92, 1, backOut);
  const caretOn = Math.floor((frame - SKY_RAHUL) / 32) % 2 === 0;
  return (
    <>
      {shell < 1 && (
        <div style={{ position: "absolute", left: morph.x, top: morph.y, width: morph.w, height: morph.h, borderRadius: morph.r, background: "#211F1D", boxShadow: "0 40px 90px rgba(0,0,0,0.45)" }} />
      )}
      <div style={{ position: "absolute", left: CHAT.x, top: CHAT.y, width: 745, height: 602, opacity: dim, isolation: "isolate" }}>
        <Img src={staticFile("slices/m-shell.png")} style={{ position: "absolute", left: 0, top: 0, width: 745, height: 602, opacity: shell * (1 - toRahul) }} />
        <Img src={staticFile("slices/r-shell.png")} style={{ position: "absolute", left: 0, top: 0, width: 745, height: 602, opacity: shell * toRahul }} />
        {ORDER.m.map((n, i) => (
          <Layer key={`m${n}`} v="m" name={n} i={i} at={CHAT_IN + 12 + i * 4 + (i > 2 ? 4 : 0)} outAt={SKY_RAHUL - 6 + (ORDER.m.length - i)} />
        ))}
        {ORDER.r.map((n, i) => (
          <Layer key={`r${n}`} v="r" name={n} i={i} at={SKY_RAHUL + 4 + i * 4 + (i > 2 ? 4 : 0)} />
        ))}
        {/* "Do it", pressed by the paw */}
        {frame >= CHAT_IN + 62 && frame < SKY_RAHUL - 6 && (
          <div style={{ position: "absolute", left: 100, top: 419, width: 88, height: 51, scale: press, zIndex: 20 }}>
            <Img src={staticFile("slices/m-doit.png")} style={{ width: 88, height: 51 }} />
            {frame >= TAP + 4 && (
              <div
                style={{
                  position: "absolute",
                  inset: 2,
                  borderRadius: 12,
                  background: c.amber,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  opacity: tween(frame, TAP + 4, TAP + 8, 0, 1),
                }}
              >
                <svg width={28} height={28} viewBox="0 0 24 24" style={{ scale: tween(frame, TAP + 4, TAP + 14, 0.3, 1, backOut) }}>
                  <path d="M5 12.5l4.5 4.5L19 7.5" fill="none" stroke={c.ink} strokeWidth={3.2} strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              </div>
            )}
          </div>
        )}
        {/* the caret blinks in Rahul's number field */}
        {frame >= SKY_RAHUL + 30 && !caretOn && <div style={{ position: "absolute", left: 150, top: 340, width: 8, height: 30, background: "rgb(37,35,32)", zIndex: 30 }} />}
      </div>
    </>
  );
};

/** Shiki's paw pointer comes up from the cat and taps "Do it". */
const PawTap: React.FC = () => {
  const frame = useCurrentFrame();
  const from = TAP - 30;
  if (frame < from || frame > TAP + 30) return null;
  const k = tween(frame, from, TAP, 0, 1, easeOut);
  const back = tween(frame, TAP + 10, TAP + 30, 0, 1, easeIn);
  const px = 7;
  // the fingertip (top middle of the sprite) ends on the button
  const tipX = CAT.x + 30 + (DOIT.x - CAT.x - 30) * k - Math.sin(k * Math.PI) * 60 + back * 260;
  const tipY = CAT.y + (DOIT.y - CAT.y) * k - Math.sin(k * Math.PI) * 140 + back * 320;
  const press = frame >= TAP && frame < TAP + 6 ? 0.88 : 1;
  return (
    <>
      {frame >= TAP && (
        <div
          style={{
            position: "absolute",
            left: DOIT.x - 70,
            top: DOIT.y - 70,
            width: 140,
            height: 140,
            borderRadius: "50%",
            border: `4px solid ${c.amber}`,
            scale: tween(frame, TAP, TAP + 18, 0.2, 1.4, easeOut),
            opacity: tween(frame, TAP, TAP + 18, 1, 0),
          }}
        />
      )}
      <Img
        src={staticFile("cat/kuro-pawpointer.png")}
        style={{
          position: "absolute",
          left: tipX - 6 * px,
          top: tipY - 1 * px,
          width: 12 * px,
          height: 14 * px,
          imageRendering: "pixelated",
          rotate: `${-18 + k * 18 + back * -20}deg`,
          scale: press,
          transformOrigin: "50% 0%",
          opacity: 1 - back,
          filter: "drop-shadow(0 16px 20px rgba(0,0,0,0.5))",
        }}
      />
    </>
  );
};

export const Voice: React.FC = () => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const PILL = L.v ? PILL_TALL : PILL_WIDE;
  const [x, y, z] = camera(L, frame);
  const [px, py, pz] = camera(L, Math.max(0, frame - 1));
  // motion blur from how far the frame moved since the last one
  const moved = Math.hypot(x * z - px * pz, y * z - py * pz);
  const enter = tween(frame, 0, 24, 1, 0, easeOut);
  const speakingSky = (frame >= TWO && frame < TWO + 130) || (frame >= SKY_RAHUL && frame < SKY_RAHUL + 130);
  // The real cat: listening, purring while Sky talks.
  const catAt = (f: number) => (speakingSky ? 180 + ((f / 2) % 70) : 258 + ((f / 2) % 60));
  const dive = tween(frame, DIVE, END, 0, 1, easeIn);
  return (
    <AbsoluteFill style={{ overflow: "hidden", background: "#0D0B0A" }}>
      <Whip amount={enter * 70 + Math.min(40, Math.max(0, moved - 14) * 0.45)}>
        <AbsoluteFill style={{ translate: `${enter * (L.W + 380)}px 0`, filter: dive > 0 ? `blur(${dive * 16}px)` : undefined }}>
          <div style={cam(x, y, z, L)}>
            <Desktop tall={L.v} />
            <ChatBuild />
            <Glow x={CHAT.x + 102} y={CHAT.y + 332} w={578} h={45} from={SKY_RAHUL + 34} to={END} />
            {/* the real cat, on the taskbar */}
            <div style={{ position: "absolute", left: CAT.x, top: CAT.y }}>
              <Clip name="cat" at={catAt} scale={1.25} pixelated />
            </div>
            <PawTap />
            {/* the voice pill, rising from the cat while you talk */}
            {frame < CHAT_IN + 4 && (
              <div style={{ position: "absolute", left: PILL.x, top: PILL.y, opacity: tween(frame, CHAT_IN - 4, CHAT_IN + 2, 1, 0) }}>
                <Pill line="you-mummy" at={MUMMY} appearAt={6} width={PILL.w} text="Hey Sky! WhatsApp Mummy I'm on my way... and email these to me." hot={["Mummy"]} />
              </div>
            )}
            {frame >= RAHUL - 8 && frame < SKY_RAHUL + 4 && (
              <div style={{ position: "absolute", left: PILL.x, top: PILL.y, opacity: tween(frame, SKY_RAHUL - 6, SKY_RAHUL + 4, 1, 0) }}>
                <Pill line="you-rahul" at={RAHUL} appearAt={RAHUL - 8} width={PILL.w} text="And WhatsApp Rahul that I reached home." hot={["Rahul"]} />
              </div>
            )}
          </div>
          {/* 9:16 has room above the desktop: headlines live there */}
          {L.v && (
            <div style={{ position: "absolute", left: 0, right: 0, top: 230, display: "flex", justifyContent: "center" }}>
              <Slam at={20} outAt={TAP - 2} size={150} color={c.text} style={{ textShadow: "0 20px 60px rgba(0,0,0,0.3)" }}>
                Just <span style={{ fontStyle: "italic", color: c.amber }}>say it.</span>
              </Slam>
            </div>
          )}
          {L.v && (
            <div style={{ position: "absolute", left: 0, right: 0, top: 230, display: "flex", justifyContent: "center" }}>
              <Slam at={RAHUL} size={130} color={c.text} style={{ textShadow: "0 20px 60px rgba(0,0,0,0.3)" }}>
                Gets <span style={{ fontStyle: "italic", color: c.amber }}>names</span> right.
              </Slam>
            </div>
          )}
          {/* "Sent." lands on the wallpaper side after the tap */}
          <div style={L.v ? { position: "absolute", left: 0, right: 0, top: 230, display: "flex", justifyContent: "center" } : { position: "absolute", left: 150, top: 330 }}>
            <Slam at={TAP + 6} outAt={RAHUL - 14} size={190} italic color={c.amber} style={{ textShadow: "0 20px 60px rgba(0,0,0,0.35)" }}>
              Sent.
            </Slam>
          </div>
          <Caption line="sky-twothings" at={TWO} who="Sky" text="Two things, one go! Okay to send?" top={L.v ? 470 : undefined} />
          <Caption line="sky-rahul" at={SKY_RAHUL} who="Sky" text="I don't have Rahul's number yet. Just say it!" hot={["Rahul's"]} top={L.v ? 470 : undefined} />
        </AbsoluteFill>
      </Whip>
      {/* the dive lands in the dark of the field */}
      <AbsoluteFill style={{ background: "rgb(37,35,32)", opacity: tween(frame, END - 8, END, 0, 1) }} />
    </AbsoluteFill>
  );
};
