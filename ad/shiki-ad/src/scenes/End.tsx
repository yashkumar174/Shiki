import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { Cat, Floater, Glow, RevealLine, Rise } from "../kit";
import { backOut, c, pixel, tween } from "../theme";

const PX = 15;

const catPose = (f: number) => (f < 34 ? "Happy" : f < 80 ? "Purr" : "Idle");

export const End: React.FC = () => {
  const frame = useCurrentFrame();
  const pop = tween(frame, 2, 18, 0, 1, backOut);
  return (
    <AbsoluteFill style={{ background: c.night, opacity: tween(frame, 0, 10, 0, 1) }}>
      <Glow x="50%" y="36%" size={1100} opacity={0.4} />
      <Glow x="50%" y="40%" size={420} opacity={0.25} color="#FFD08A" />
      <div style={{ position: "absolute", left: 960 - 10 * PX, top: 120, scale: pop, transformOrigin: "50% 100%" }}>
        <Cat pose={catPose(frame)} px={PX} />
      </div>
      <Floater name="float-Heart" px={8} at={12} x={1080} y={150} drift={30} />
      <Floater name="float-Heart" px={6} at={20} x={800} y={170} drift={-26} />
      <Floater name="float-Sparkle" px={9} at={28} x={1110} y={260} drift={14} rise={110} />
      <AbsoluteFill style={{ alignItems: "center", top: 450 }}>
        <RevealLine at={10} size={250} color={c.text} parts={[{ t: "Shiki" }]} />
        <RevealLine
          at={30}
          size={84}
          color={c.text}
          stagger={5}
          style={{ marginTop: 6 }}
          parts={[{ t: "Say it." }, { t: "It's done.", italic: true, color: c.amber }]}
        />
        <Rise at={58} style={{ marginTop: 44, fontFamily: pixel, fontSize: 34, color: c.muted, letterSpacing: "0.04em" }}>
          your little desktop cat · for Windows
        </Rise>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
