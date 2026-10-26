import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { Cat, Floater, Glow, RevealLine, Rise, Sprite } from "../kit";
import { backOut, c, easeIn, easeOut, pixel, sans, tween } from "../theme";

// The card the cat lives in, like Shiki's home card: black, with a warm glow.
const CARD = { x: 900, y: 130, w: 860, h: 820 };
const PX = 18;
export const POP = 58; // the cat jumps up out of the peek

const catPose = (f: number) => {
  if (f < POP) return f < 32 ? "peek-Idle" : f < 41 ? "peek-LeftPaw" : f < 50 ? "peek-RightPaw" : "peek-Idle";
  if (f < POP + 16) return "Pop";
  if (f < POP + 22) return "Startled";
  if (f < POP + 52) return "Happy";
  if (f >= 128 && f < 146) return "LookLeft";
  return "Idle";
};

export const Meet: React.FC = () => {
  const frame = useCurrentFrame();
  const pose = catPose(frame);
  const peeking = pose.startsWith("peek-");
  // The jump: up past the resting spot, then a soft landing with a squash.
  const jump =
    frame < POP
      ? 0
      : frame < POP + 12
        ? tween(frame, POP, POP + 12, 380, -70, easeOut)
        : tween(frame, POP + 12, POP + 22, -70, 0, easeIn);
  const squash = frame >= POP + 22 ? tween(frame, POP + 22, POP + 34, 0.12, 0, backOut) : frame >= POP ? tween(frame, POP, POP + 8, 0, -0.08) : 0;
  const card = tween(frame, 0, 24, 0, 1);

  return (
    <AbsoluteFill style={{ background: c.paper }}>
      {/* Words on the left */}
      <div style={{ position: "absolute", left: 150, top: 330, width: 700 }}>
        <RevealLine at={POP - 22} size={190} color={c.ink} parts={[{ t: "Meet" }, { t: "Shiki.", italic: true, color: c.amberEdge }]} />
        <Rise at={POP + 6} style={{ marginTop: 34, fontFamily: sans, fontSize: 44, lineHeight: 1.3, color: c.inkMuted }}>
          A tiny cat that lives on your PC
          <br />
          and actually gets things done.
        </Rise>
      </div>

      {/* The card */}
      <div
        style={{
          position: "absolute",
          left: CARD.x,
          top: CARD.y,
          width: CARD.w,
          height: CARD.h,
          borderRadius: 52,
          background: c.hero,
          overflow: "hidden",
          boxShadow: "0 50px 120px rgba(40,25,10,0.28), 0 8px 24px rgba(40,25,10,0.12)",
          opacity: card,
          scale: tween(frame, 0, 26, 0.92, 1),
          translate: `0 ${tween(frame, 0, 26, 60, 0)}px`,
        }}
      >
        <Glow x="62%" y="78%" size={900} opacity={0.55} />
        <Glow x="62%" y="85%" size={420} opacity={0.35} color="#FFD08A" />
        {peeking ? (
          <div
            style={{
              position: "absolute",
              left: (CARD.w - 26 * PX) / 2,
              bottom: 0,
              translate: `0 ${tween(frame, 12, 30, 7 * PX + 10, 0, easeOut)}px`,
            }}
          >
            <Sprite name={pose} px={PX} />
          </div>
        ) : (
          <div style={{ position: "absolute", left: (CARD.w - 20 * PX) / 2, bottom: 40, translate: `0 ${jump}px` }}>
            <Cat pose={pose} px={PX} squash={squash} />
          </div>
        )}
        <Floater name="float-Heart" px={9} at={POP + 26} x={CARD.w / 2 + 120} y={CARD.h - 420} drift={30} />
        <Floater name="float-Heart" px={7} at={POP + 36} x={CARD.w / 2 - 170} y={CARD.h - 380} drift={-24} />
        <Floater name="float-Sparkle" px={10} at={POP + 30} x={CARD.w / 2 - 60} y={CARD.h - 470} drift={10} rise={110} />
        {/* "hi!" in the cat's pixel voice */}
        <div
          style={{
            position: "absolute",
            left: CARD.w / 2 + 150,
            top: 170,
            fontFamily: pixel,
            fontSize: 46,
            color: c.ink,
            background: c.paper,
            padding: "10px 24px",
            borderRadius: 18,
            opacity: tween(frame, POP + 40, POP + 46, 0, 1) * tween(frame, 136, 142, 1, 0, easeIn),
            scale: tween(frame, POP + 40, POP + 50, 0.4, 1, backOut),
            transformOrigin: "0% 100%",
          }}
        >
          hi!
        </div>
      </div>
    </AbsoluteFill>
  );
};

/** Where the cat sits on screen, for the circle wipe that grows out of it. */
export const CAT_CENTER = { x: CARD.x + CARD.w / 2, y: CARD.y + CARD.h - 40 - 10 * PX };
