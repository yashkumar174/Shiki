import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { backOut, c, pixel, sans, tween } from "../theme";
import { Layout, Mesh, pick, s, shake, Slam, smooth, useLayout } from "./parts";

// One tab slams in on every beat: the converter-site mess Shiki replaces.
// Positions are [x, y] in 16:9, then [x, y] in 9:16.
const TABS = [
  { t: "Compress PDF – Free", fav: c.coral, at: [960, 560, 540, 1080], r: -3 },
  { t: "JPG to PNG Converter", fav: "#5BA88F", at: [600, 420, 380, 860], r: 5 },
  { t: "Remove Background", fav: "#D4A017", at: [1330, 400, 700, 720], r: -6 },
  { t: "PDF to Word Online", fav: "#7E8CE0", at: [640, 730, 360, 1300], r: -5 },
  { t: "Merge PDF – Free", fav: "#C77DBA", at: [1290, 740, 720, 1450], r: 4 },
  { t: "Image to Text (OCR)", fav: "#4F9DA6", at: [960, 300, 560, 590], r: 2 },
];
const tabXY = (L: Layout, i: number) => (L.v ? [TABS[i].at[2], TABS[i].at[3]] : [TABS[i].at[0], TABS[i].at[1]]);
export const tabAt = (i: number) => s(i * 0.5);
export const UGH = s(3);
// The cat's paw swats everything off screen, right to left, and leaves the night behind it.
export const PAW = 204;
const PAW_END = 244;
export const pawX = (frame: number, W = 1920) => W + 380 - (W + 1280) * smooth(Math.min(1, Math.max(0, (frame - PAW) / (PAW_END - PAW))));
const hitFrame = (x: number, W: number) => {
  for (let f = PAW; f <= PAW_END; f++) if (pawX(f, W) < x + 120) return f;
  return PAW_END;
};

const Tab: React.FC<{ i: number }> = ({ i }) => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const tab = TABS[i];
  const [x, y] = tabXY(L, i);
  const f = frame - tabAt(i);
  if (f < 0) return null;
  const hit = hitFrame(x, L.W);
  const knock = tween(frame, hit, hit + 18, 0, 1, (t) => t);
  return (
    <div
      style={{
        position: "absolute",
        left: x - knock * 1900,
        top: y - knock * (300 + i * 60) + knock * knock * 900,
        translate: "-50% -50%",
        rotate: `${tab.r + Math.sin((frame + i * 40) / 18) * 1.2 - knock * (220 + i * 40)}deg`,
        scale: tween(f, 0, 14, 1.6, 1, backOut),
        opacity: tween(f, 0, 4, 0, 1),
        filter: `blur(${tween(f, 0, 8, 10, 0)}px)`,
        display: "flex",
        alignItems: "center",
        gap: 18,
        width: 500,
        height: 88,
        padding: "0 26px",
        background: "#fff",
        borderRadius: "22px 22px 12px 12px",
        boxShadow: "0 30px 60px rgba(90,50,20,0.18), 0 3px 8px rgba(90,50,20,0.1)",
        fontFamily: sans,
        fontSize: 31,
        fontWeight: 500,
        color: c.ink,
      }}
    >
      <div style={{ width: 32, height: 32, borderRadius: 9, background: tab.fav, flexShrink: 0 }} />
      <div style={{ flex: 1, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{tab.t}</div>
      <div style={{ color: c.inkMuted, fontSize: 30 }}>×</div>
    </div>
  );
};

export const Hook: React.FC = () => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const count = TABS.filter((_, i) => frame >= tabAt(i)).length;
  const last = count ? tabAt(count - 1) : 0;
  // Each slam pushes the camera in a little.
  const push = 1 + count * 0.025;
  const paw = pawX(frame, L.W);
  return (
    <AbsoluteFill style={{ overflow: "hidden" }}>
      <Mesh mode="cream" />
      <AbsoluteFill
        style={{
          scale: push,
          translate: frame >= PAW ? shake(frame, PAW + 6, 18) : frame >= UGH ? shake(frame, UGH, 22) : shake(frame, last, 10),
          filter: frame > UGH ? `saturate(${tween(frame, UGH, UGH + 10, 1, 0.25)})` : undefined,
        }}
      >
        {TABS.map((_, i) => (
          <Tab key={i} i={i} />
        ))}
      </AbsoluteFill>
      <AbsoluteFill style={{ alignItems: "center", paddingTop: pick(L, 70, 250), translate: `${Math.min(0, paw - (L.W - 220))}px 0` }}>
        <div
          style={{
            fontFamily: pixel,
            fontSize: 40,
            color: c.ink,
            background: "#fff",
            border: `3px solid ${c.ink}`,
            borderRadius: 16,
            padding: "6px 22px",
            scale: count ? tween(frame, last, last + 10, 1.3, 1, backOut) : 0,
          }}
        >
          {count} {count === 1 ? "tab" : "tabs"} open
        </div>
      </AbsoluteFill>
      {frame >= UGH && (
        <AbsoluteFill style={{ background: "rgba(246,240,231,0.55)", opacity: tween(frame, UGH, UGH + 6, 0, 1) }} />
      )}
      <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", translate: `${Math.min(0, paw - (L.W - 420)) * 0.9}px 0`, rotate: `${Math.min(0, paw - (L.W - 420)) * -0.01}deg` }}>
        <Slam
          at={UGH}
          size={pick(L, 330, 300)}
          italic
          color={c.amberEdge}
          style={{
            // a little RGB split as it lands
            textShadow: frame - UGH < 14 ? `${tween(frame, UGH, UGH + 14, 14, 0)}px 0 rgba(232,90,60,0.7), ${tween(frame, UGH, UGH + 14, -14, 0)}px 0 rgba(90,160,200,0.6)` : undefined,
          }}
        >
          Ugh.
        </Slam>
      </AbsoluteFill>
      {/* the night the paw leaves behind, then the paw itself */}
      {frame >= PAW && (
        <>
          <div style={{ position: "absolute", left: paw + 120, top: 0, right: -40, bottom: 0, background: "#0D0B0A" }} />
          <Img
            src={staticFile("cat/kuro-pawpointer.png")}
            style={{
              position: "absolute",
              left: paw - 240,
              top: L.H / 2 - 204,
              width: 12 * 34,
              height: 14 * 34,
              imageRendering: "pixelated",
              rotate: `${-90 + Math.sin((frame - PAW) / 3) * 4}deg`,
              filter: "drop-shadow(0 30px 40px rgba(0,0,0,0.45))",
            }}
          />
        </>
      )}
    </AbsoluteFill>
  );
};
