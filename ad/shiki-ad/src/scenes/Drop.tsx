import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { Caption, Cat, Floater, Glow, RevealLine } from "../kit";
import { backOut, c, easeIn, easeInOut, easeOut, sans, tween } from "../theme";
import { voice } from "../voice-data";

export const LAND = 46; // the PDF lands on the cat
export const ASK = 56; // "Make this PDF under two MB."
export const PICK = 104; // "Under 2 MB" is chosen on the wheel
export const READY = 150; // the smaller file comes out

const PX = 16;
const CAT = { x: 960, y: 820 }; // middle of the cat's feet
const HEAD = { x: CAT.x, y: CAT.y - 13 * PX };

const FileCard: React.FC<{ name: string; size: string; badge?: React.ReactNode; style?: React.CSSProperties }> = ({ name, size, badge, style }) => (
  <div
    style={{
      position: "absolute",
      display: "flex",
      alignItems: "center",
      gap: 20,
      width: 400,
      padding: "22px 26px",
      background: c.paperCard,
      border: `1px solid ${c.paperLine}`,
      borderRadius: 26,
      boxShadow: "0 30px 70px rgba(60,40,20,0.16), 0 3px 8px rgba(60,40,20,0.06)",
      fontFamily: sans,
      ...style,
    }}
  >
    <div style={{ width: 64, height: 76, borderRadius: 12, background: c.coral, color: "#fff", fontSize: 19, fontWeight: 600, display: "flex", alignItems: "flex-end", justifyContent: "center", paddingBottom: 10 }}>
      PDF
    </div>
    <div style={{ flex: 1 }}>
      <div style={{ fontSize: 32, fontWeight: 600, color: c.ink }}>{name}</div>
      <div style={{ fontSize: 26, color: c.inkMuted, marginTop: 4 }}>{size}</div>
    </div>
    {badge}
  </div>
);

// Shiki's wheel: what can be done with the file, around the cat.
const CHIPS = [
  { t: "To Word", x: -470, y: -330 },
  { t: "To JPG", x: -560, y: -150 },
  { t: "Merge", x: -470, y: 30 },
  { t: "Under 2 MB", x: 300, y: -330, pick: true },
  { t: "Sign", x: 400, y: -150 },
  { t: "Lock", x: 320, y: 30 },
];

const Chip: React.FC<{ i: number }> = ({ i }) => {
  const frame = useCurrentFrame();
  const chip = CHIPS[i];
  const f = frame - (62 + i * 4);
  if (f < 0) return null;
  const picked = chip.pick && frame >= PICK;
  // the others go back into the cat once one is chosen
  const back = chip.pick ? tween(frame, PICK + 26, PICK + 36, 0, 1, easeIn) : tween(frame, PICK + 2, PICK + 12, 0, 1, easeIn);
  const k = tween(f, 0, 16, 0, 1, backOut) * (1 - back);
  return (
    <div
      style={{
        position: "absolute",
        left: HEAD.x,
        top: HEAD.y,
        translate: `${chip.x * k}px ${chip.y * k}px`,
        scale: (0.4 + 0.6 * Math.min(1, k)) * (picked ? tween(frame, PICK, PICK + 10, 1.14, 1.06, backOut) : 1),
        opacity: Math.min(1, k * 1.5),
        padding: "18px 30px",
        borderRadius: 40,
        fontFamily: sans,
        fontSize: 32,
        fontWeight: 600,
        whiteSpace: "nowrap",
        color: c.ink,
        background: picked ? c.amber : c.paperCard,
        border: `1.5px solid ${picked ? c.amberEdge : c.paperLine}`,
        boxShadow: picked ? "0 20px 50px rgba(217,138,43,0.35)" : "0 16px 40px rgba(60,40,20,0.12)",
        display: "flex",
        alignItems: "center",
        gap: 12,
      }}
    >
      <div style={{ width: 12, height: 12, borderRadius: 3, background: picked ? c.ink : c.amber }} />
      {chip.t}
    </div>
  );
};

const Progress: React.FC = () => {
  const frame = useCurrentFrame();
  const p = tween(frame, PICK + 8, READY - 2, 0, 1, easeInOut);
  const r = 250;
  const len = 2 * Math.PI * r;
  const o = tween(frame, PICK + 6, PICK + 14, 0, 1) * tween(frame, READY, READY + 10, 1, 0, easeIn);
  return (
    <svg width={600} height={600} style={{ position: "absolute", left: HEAD.x - 300, top: HEAD.y - 260, rotate: "-90deg", opacity: o }}>
      <circle cx={300} cy={300} r={r} fill="none" stroke={c.paperLine} strokeWidth={10} />
      <circle cx={300} cy={300} r={r} fill="none" stroke={c.amber} strokeWidth={10} strokeLinecap="round" strokeDasharray={len} strokeDashoffset={len * (1 - p)} />
    </svg>
  );
};

const catPose = (f: number) => {
  if (f >= LAND && f < LAND + 10) return "Startled";
  if (f >= PICK + 8 && f < READY) return Math.floor(f / 7) % 2 ? "StrainLeft" : "StrainRight";
  if (f >= READY) return f < READY + 30 ? "Happy" : "Idle";
  if (f >= 62 && f < PICK) return "LookRight";
  if (f >= 18 && f < LAND) return "LookRight";
  return "Idle";
};

export const Drop: React.FC = () => {
  const frame = useCurrentFrame();
  // The whole sheet slides up over the voice scene.
  const sheet = tween(frame, 0, 20, 1, 0, easeOut);
  // The file flies in on an arc and drops into the cat.
  const fly = tween(frame, 20, LAND, 0, 1, easeInOut);
  const fx = 1560 + (HEAD.x - 200 - 1560) * fly;
  const fy = -180 + (HEAD.y - 140 + 180) * fly - Math.sin(fly * Math.PI) * 120;
  const squash =
    frame >= READY ? tween(frame, READY, READY + 12, -0.08, 0, backOut) : frame >= LAND ? tween(frame, LAND, LAND + 14, 0.14, 0, backOut) : 0;
  const pdf = voice["you-pdf"];
  return (
    <AbsoluteFill style={{ translate: `0 ${sheet * 1080}px` }}>
      <AbsoluteFill style={{ background: c.paper, borderRadius: `${sheet * 160 + 0}px ${sheet * 160}px 0 0`, overflow: "hidden" }}>
        <Glow x="50%" y="62%" size={1300} opacity={0.22} />
        <AbsoluteFill style={{ alignItems: "center", paddingTop: 96 }}>
          <RevealLine at={10} size={120} color={c.ink} parts={[{ t: "Drop it on" }, { t: "the cat.", italic: true, color: c.amberEdge }]} />
        </AbsoluteFill>
        <Progress />
        <div style={{ position: "absolute", left: CAT.x - 190, top: CAT.y - 18, width: 380, height: 40, borderRadius: "50%", background: "rgba(60,40,20,0.16)", filter: "blur(10px)" }} />
        <div style={{ position: "absolute", left: CAT.x - 10 * PX, top: CAT.y - 20 * PX }}>
          <Cat pose={catPose(frame)} px={PX} squash={squash} />
        </div>
        {CHIPS.map((_, i) => (
          <Chip key={i} i={i} />
        ))}
        {frame < LAND + 8 && frame >= 20 && (
          <FileCard
            name="Report.pdf"
            size="8.4 MB"
            style={{
              left: fx,
              top: fy,
              rotate: `${tween(frame, 20, LAND, 14, -4)}deg`,
              scale: tween(frame, LAND - 8, LAND + 6, 1, 0.2, easeIn),
              opacity: tween(frame, LAND, LAND + 6, 1, 0),
            }}
          />
        )}
        {frame >= READY && (
          <FileCard
            name="Report.pdf"
            size="8.4 MB  →  1.9 MB"
            badge={
              <div style={{ width: 54, height: 54, borderRadius: "50%", background: c.amber, display: "flex", alignItems: "center", justifyContent: "center" }}>
                <svg width={30} height={30} viewBox="0 0 24 24">
                  <path d="M5 12.5l4.5 4.5L19 7.5" fill="none" stroke={c.ink} strokeWidth={3} strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              </div>
            }
            style={{
              left: HEAD.x + 230,
              top: HEAD.y - 40,
              width: 480,
              translate: `${tween(frame, READY, READY + 16, -260, 0, easeOut)}px ${tween(frame, READY, READY + 16, 60, 0, easeOut)}px`,
              scale: tween(frame, READY, READY + 14, 0.3, 1, backOut),
              opacity: tween(frame, READY, READY + 6, 0, 1),
            }}
          />
        )}
        <Floater name="float-Sweat" px={10} at={PICK + 12} x={HEAD.x + 120} y={HEAD.y - 120} drift={20} rise={80} />
        <Floater name="float-Sweat" px={8} at={PICK + 28} x={HEAD.x - 150} y={HEAD.y - 110} drift={-20} rise={80} />
        <Floater name="float-Heart" px={9} at={READY + 4} x={HEAD.x - 60} y={HEAD.y - 160} drift={-30} />
        <Floater name="float-Heart" px={7} at={READY + 12} x={HEAD.x + 70} y={HEAD.y - 150} drift={30} />
        <Caption who="You" text="Make this PDF under 2 MB." from={ASK + pdf.runs[0][0]} to={ASK + pdf.frames} dark={false} />
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
