import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { Cat, Glow, RevealLine, Rise, Sprite } from "../kit";
import { c, easeInOut, easeOut, sans, tween } from "../theme";

const LINES = [
  "Copy text from anything on screen",
  "Cut out backgrounds",
  "Sign, lock and merge PDFs",
  "Meeting notes and live captions",
  "Reminders that talk back",
];

const W = 820;

/** Shiki's real Tools page, in its window, gently scrolling. */
const ToolsWindow: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        position: "absolute",
        left: 1000,
        top: 210,
        perspective: 2400,
      }}
    >
      <div
        style={{
          rotate: `x ${tween(frame, 0, 152, 10, 6)}deg`,
          transformStyle: "preserve-3d",
        }}
      >
        <div
          style={{
            rotate: `y ${tween(frame, 0, 152, -20, -13, easeInOut)}deg`,
            translate: `0 ${tween(frame, 0, 24, 80, 0, easeOut)}px`,
            opacity: tween(frame, 0, 14, 0, 1),
            position: "relative",
          }}
        >
          {/* the cat sits on top of the window */}
          <div style={{ position: "absolute", left: W - 210, top: -20 * 7 + 2 }}>
            <Cat pose={frame > 60 && frame < 100 ? "LookLeft" : "Idle"} px={7} />
          </div>
          <div
            style={{
              width: W,
              height: 780,
              borderRadius: 30,
              overflow: "hidden",
              background: c.nightPanel,
              border: `1.5px solid ${c.nightLine}`,
              boxShadow: "0 80px 160px rgba(0,0,0,0.6), 0 0 120px rgba(246,169,69,0.12)",
            }}
          >
            <div style={{ height: 58, display: "flex", alignItems: "center", gap: 12, padding: "0 22px", borderBottom: `1px solid ${c.nightLine}`, fontFamily: sans, fontSize: 22, fontWeight: 600, color: c.text }}>
              <Sprite name="head" px={2} />
              Shiki
              <div style={{ flex: 1 }} />
              <div style={{ color: c.muted, fontSize: 20, letterSpacing: 10 }}>– ▢ ✕</div>
            </div>
            <Img
              src={staticFile("shots/dash-tools.png")}
              style={{ width: W, display: "block", translate: `0 ${tween(frame, 18, 152, 0, -1450, easeInOut)}px` }}
            />
          </div>
        </div>
      </div>
    </div>
  );
};

export const More: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <AbsoluteFill style={{ background: c.night, overflow: "hidden", opacity: tween(frame, 0, 10, 0, 1), scale: tween(frame, 0, 18, 1.05, 1) }}>
      <Glow x="72%" y="50%" size={1300} opacity={0.18} />
      <div style={{ position: "absolute", left: 140, top: 190, width: 820 }}>
        <RevealLine at={6} size={124} color={c.text} style={{ maxWidth: 640 }} parts={[{ t: "And a whole" }, { t: "lot more.", italic: true, color: c.amber }]} />
        <div style={{ marginTop: 50, display: "flex", flexDirection: "column", gap: 26 }}>
          {LINES.map((l, i) => (
            <Rise key={l} at={30 + i * 7} dist={18} style={{ display: "flex", alignItems: "center", gap: 22, fontFamily: sans, fontSize: 40, color: c.text }}>
              <Sprite name="float-Sparkle" px={7} />
              {l}
            </Rise>
          ))}
        </div>
      </div>
      <ToolsWindow />
    </AbsoluteFill>
  );
};
