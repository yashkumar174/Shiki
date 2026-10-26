import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { Glow, RevealLine } from "../kit";
import { backOut, c, easeIn, pixel, sans, tween } from "../theme";

// Ten made-up converter tabs, the "10 browser tabs" Shiki replaces.
const TABS = [
  { t: "Free PDF Converter", fav: c.coral, x: 250, y: 520, r: -6 },
  { t: "Compress PDF Online", fav: "#8E7CC3", x: 1180, y: 470, r: 5 },
  { t: "JPG to PNG – Free", fav: "#5BA88F", x: 640, y: 640, r: 3 },
  { t: "Remove Background", fav: "#D4A017", x: 1320, y: 690, r: -4 },
  { t: "Image to Text (OCR)", fav: "#C77DBA", x: 180, y: 760, r: 4 },
  { t: "Merge PDF – Free", fav: c.coral, x: 860, y: 820, r: -3 },
  { t: "MP4 to MP3", fav: "#4F9DA6", x: 1460, y: 880, r: 7 },
  { t: "Sign PDF Online", fav: "#B5835A", x: 420, y: 920, r: -8 },
  { t: "Resize Image", fav: "#7AA35B", x: 1040, y: 590, r: -6 },
  { t: "Unlock PDF", fav: "#9C6ADE", x: 700, y: 470, r: 6 },
];
export const tabAt = (i: number) => 8 + i * 5;

const Tab: React.FC<{ i: number }> = ({ i }) => {
  const frame = useCurrentFrame();
  const tab = TABS[i];
  const f = frame - tabAt(i);
  if (f < 0) return null;
  // Out: everything is pulled down to where the cat will appear.
  const suck = tween(frame, 80 + i * 1.5, 100 + i * 1.5, 0, 1, easeIn);
  const wobble = Math.sin((frame + i * 13) / 4.5) * 1.6 * (1 - suck);
  return (
    <div
      style={{
        position: "absolute",
        left: tab.x,
        top: tab.y,
        translate: `${(1330 - tab.x - 180) * suck}px ${tween(f, 0, 12, 50, 0) + (1250 - tab.y) * suck}px`,
        rotate: `${tab.r + wobble + suck * 40}deg`,
        scale: tween(f, 0, 12, 0.55, 1, backOut) * (1 - suck * 0.7),
        opacity: tween(f, 0, 6, 0, 1),
        display: "flex",
        alignItems: "center",
        gap: 16,
        width: 380,
        height: 70,
        padding: "0 22px",
        background: c.paperCard,
        borderRadius: "18px 18px 10px 10px",
        border: `1px solid ${c.paperLine}`,
        boxShadow: "0 18px 40px rgba(60,40,20,0.13), 0 2px 6px rgba(60,40,20,0.08)",
        fontFamily: sans,
        fontSize: 25,
        fontWeight: 500,
        color: c.ink,
      }}
    >
      <div style={{ width: 26, height: 26, borderRadius: 7, background: tab.fav, flexShrink: 0 }} />
      <div style={{ flex: 1, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{tab.t}</div>
      <div style={{ color: c.inkMuted, fontSize: 24 }}>×</div>
    </div>
  );
};

export const Hook: React.FC = () => {
  const frame = useCurrentFrame();
  const count = TABS.filter((_, i) => frame >= tabAt(i)).length;
  const out = tween(frame, 84, 96, 0, 1, easeIn);
  return (
    <AbsoluteFill style={{ background: c.paper, overflow: "hidden" }}>
      <Glow x="50%" y="62%" size={1500} opacity={0.18} />
      <AbsoluteFill style={{ alignItems: "center", paddingTop: 120 }}>
        <RevealLine
          at={4}
          outAt={84}
          size={132}
          color={c.ink}
          parts={[{ t: "Ten tabs to fix" }, { t: "one", italic: true, color: c.amberEdge }, { t: "file?" }]}
        />
        <div
          style={{
            marginTop: 26,
            fontFamily: pixel,
            fontSize: 34,
            color: c.ink,
            background: c.paperCard,
            border: `2px solid ${c.ink}`,
            borderRadius: 12,
            padding: "6px 18px",
            opacity: tween(frame, 10, 16, 0, 1) * (1 - out),
            scale: count > 0 ? tween(frame, tabAt(count - 1), tabAt(count - 1) + 5, 1.18, 1, backOut) : 1,
          }}
        >
          {count} {count === 1 ? "tab" : "tabs"} open
        </div>
      </AbsoluteFill>
      {TABS.map((_, i) => (
        <Tab key={i} i={i} />
      ))}
    </AbsoluteFill>
  );
};
