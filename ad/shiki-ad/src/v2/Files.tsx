import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { backOut, c, easeIn, easeInOut, easeOut, sans, serif, tween } from "../theme";
import { Burst, Caption, Clip, Confetti, Mesh, Odometer, pick, Sparks, shake, Slam, timeMap, useLayout } from "./parts";
import heads from "../../public/rec/shrink-win1/heads.json";

export const PDF_LINE = 18;
const WHEEL = 34;
const PROGRESS = 138;
const SAVED = 188;
export const SKY_SAVED = 210;
const END = 360;

// The real wheel (recorded over the ad's own cream, so its frosted glass shows that), cut into its
// three wedges and the hub along the real gaps (measured at 89.5, 209.5 and 330.5 degrees), so it
// can fly together. Hub: 78 px, its shadow ring to about 100 px, wedges from 102 px (of 550).
const WEDGES = [
  { from: 209.5, to: 330.5 }, // PNG, top
  { from: -29.5, to: 89.5 }, // JPG, right
  { from: 89.5, to: 209.5 }, // Word, left
];
const sector = (a0: number, a1: number) => {
  const pts = ["50% 50%"];
  for (let a = a0; a <= a1 + 0.01; a += (a1 - a0) / 12) pts.push(`${50 + 80 * Math.cos((a * Math.PI) / 180)}% ${50 + 80 * Math.sin((a * Math.PI) / 180)}%`);
  return `polygon(${pts.join(", ")})`;
};
export const HOVER = 50; // after it lands, the JPG wedge lights up as the real hover did

const Wheel: React.FC<{ at: number; size: number }> = ({ at, size }) => {
  const frame = useCurrentFrame();
  const f = frame - at;
  if (f < 0) return null;
  const k = size / 550;
  // the plain wheel while it assembles, then the real hover fading in
  const shown = f < HOVER ? 0 : Math.min(14, 3 + (f - HOVER) / 2);
  const img = (style: React.CSSProperties) => <Clip name="wheel2" at={() => shown} scale={k} style={{ position: "absolute", left: 0, top: 0, ...style }} />;
  // wedges only: no hub, and none of the soft shadow outside the wheel (the whole wheel casts one)
  const ring = `radial-gradient(circle at 50% 50%, transparent ${91 * k}px, #000 ${92 * k}px, #000 ${193 * k}px, transparent ${196 * k}px)`;
  return (
    <div style={{ position: "relative", width: size, height: size }}>
      {WEDGES.map((w, i) => {
        const g = f - 6 - i * 4;
        const mid = (((w.from + w.to) / 2) * Math.PI) / 180;
        const p = tween(g, 0, 18, 0, 1, backOut);
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              inset: 0,
              opacity: tween(g, 0, 5, 0, 1),
              translate: `${-Math.cos(mid) * 140 * (1 - p)}px ${-Math.sin(mid) * 140 * (1 - p)}px`,
              rotate: `${(1 - p) * -40}deg`,
              scale: 0.6 + 0.4 * p,
            }}
          >
            {img({ clipPath: sector(w.from, w.to), WebkitMaskImage: ring, maskImage: ring })}
          </div>
        );
      })}
      {/* the hub, with the file's name in it */}
      <div style={{ position: "absolute", inset: 0, scale: tween(f, 0, 14, 0.2, 1, backOut), opacity: tween(f, 0, 4, 0, 1) }}>{img({ clipPath: `circle(${91 * k}px at 50% 50%)` })}</div>
    </div>
  );
};

const FileCard: React.FC<{ style?: React.CSSProperties }> = ({ style }) => (
  <div
    style={{
      position: "absolute",
      display: "flex",
      alignItems: "center",
      gap: 20,
      padding: "20px 28px 20px 20px",
      background: "#fff",
      borderRadius: 24,
      boxShadow: "0 30px 70px rgba(90,50,20,0.22)",
      fontFamily: sans,
      ...style,
    }}
  >
    <div style={{ width: 62, height: 74, borderRadius: 12, background: c.coral, color: "#fff", fontSize: 18, fontWeight: 600, display: "flex", alignItems: "flex-end", justifyContent: "center", paddingBottom: 10 }}>PDF</div>
    <div>
      <div style={{ fontSize: 32, fontWeight: 600, color: c.ink, whiteSpace: "nowrap" }}>Project Report.pdf</div>
      <div style={{ fontSize: 26, color: c.inkMuted }}>20.6 MB</div>
    </div>
  </div>
);

export const Files: React.FC = () => {
  const frame = useCurrentFrame();
  const L = useLayout();
  // 16:9 puts things side by side; 9:16 stacks them.
  const WH = pick(L, { x: 700, y: 490, size: 760 }, { x: 540, y: 690, size: 820 });
  const TOAST = pick(L, { x: 960 - (465 * 2.3) / 2, y: 100, z: 2.3 }, { x: (1080 - 465 * 2.1) / 2, y: 330, z: 2.1 });
  const CATP = pick(L, { x: 520, y: 560, z: 6 }, { x: 540, y: 600, z: 7 });
  const iris = tween(frame, 0, 20, 0, 1400, easeOut);
  const leave = tween(frame, END - 22, END, 0, 1, easeIn);
  const fly = tween(frame, 2, WHEEL, 0, 1, easeInOut);
  const size = tween(frame, PROGRESS + 2, SAVED, 20.6, 1.9, easeInOut);
  const toastAt = timeMap([[PROGRESS, 0], [SAVED, 150], [SAVED + 30, 182]]);
  return (
    <AbsoluteFill style={{ overflow: "hidden", background: "rgb(37,35,32)" }}>
      <AbsoluteFill style={{ clipPath: frame < 20 ? `circle(${iris}px at 50% 50%)` : undefined }}>
        <Mesh mode="cream" />
        <AbsoluteFill style={{ scale: 1 + leave * 2.2, opacity: 1 - leave }}>
          {/* 1: the file lands, the real wheel opens around it */}
          {frame < PROGRESS && (
            <AbsoluteFill style={{ translate: shake(frame, WHEEL, 16) }}>
              {frame >= WHEEL && (
                <div
                  style={{
                    position: "absolute",
                    left: WH.x - WH.size / 2,
                    top: WH.y - WH.size / 2,
                    width: WH.size,
                    height: WH.size,
                    scale: tween(frame, WHEEL, PROGRESS, 1, 1.06),
                    filter: "drop-shadow(0 24px 40px rgba(60,30,10,0.28))",
                  }}
                >
                  <Wheel at={WHEEL} size={WH.size} />
                </div>
              )}
              <Burst at={WHEEL} x={WH.x} y={WH.y} />
              {frame < WHEEL + 4 && (
                <FileCard
                  style={{
                    left: pick(L, 120, 60) + (WH.x - 230 - pick(L, 120, 60)) * fly,
                    top: pick(L, 860, 1500) + (WH.y - 40 - pick(L, 860, 1500)) * fly - Math.sin(fly * Math.PI) * 160,
                    rotate: `${tween(frame, 2, WHEEL, -14, 0)}deg`,
                    scale: tween(frame, WHEEL - 10, WHEEL + 2, 1, 0.3, easeIn),
                    opacity: tween(frame, WHEEL - 2, WHEEL + 3, 1, 0),
                  }}
                />
              )}
              <div style={L.v ? { position: "absolute", left: 0, right: 0, top: 1150, display: "flex", justifyContent: "center", alignItems: "baseline", gap: 30 } : { position: "absolute", left: 1190, top: 300 }}>
                <Slam at={66} size={130} color={c.ink}>
                  under
                </Slam>
                <Slam at={78} size={pick(L, 240, 180)} italic color={c.amberEdge}>
                  2 MB.
                </Slam>
              </div>
            </AbsoluteFill>
          )}

          {/* 2: the real progress, fast-forwarded, and the size falling */}
          {frame >= PROGRESS && frame < SKY_SAVED && (
            <AbsoluteFill style={{ translate: shake(frame, SAVED, 18) }}>
              <div style={{ position: "absolute", left: TOAST.x, top: TOAST.y, filter: "drop-shadow(0 50px 80px rgba(60,30,10,0.35))", scale: tween(frame, PROGRESS, PROGRESS + 10, 1.12, 1, easeOut) }}>
                <Clip name="shrink-win1" at={toastAt} scale={TOAST.z} />
              </div>
              {/* sparks fly off the head of the real progress bar */}
              <Sparks
                from={PROGRESS}
                to={SAVED - 6}
                at={(f) => {
                  const h = heads[Math.round(toastAt(f))] ?? -1;
                  return h > 0 && h < 426 ? [TOAST.x + h * TOAST.z, TOAST.y + 115.5 * TOAST.z] : null;
                }}
              />
              <Burst at={SAVED} x={TOAST.x + 45 * TOAST.z} y={TOAST.y + 60 * TOAST.z} />
              <div style={{ position: "absolute", left: 0, right: 0, top: pick(L, 610, 860), display: "flex", flexDirection: L.v ? "column" : "row", justifyContent: "center", alignItems: "center", gap: pick(L, 30, 10), fontFamily: serif, lineHeight: 1 }}>
                <span style={{ fontSize: 120, color: c.inkMuted, textDecoration: frame >= SAVED ? "line-through" : "none" }}>20.6 MB</span>
                <span style={{ fontSize: 90, color: c.inkMuted, rotate: L.v ? "90deg" : undefined }}>→</span>
                <span
                  style={{
                    fontSize: 190,
                    color: frame >= SAVED ? c.amberEdge : c.ink,
                    fontStyle: frame >= SAVED ? "italic" : "normal",
                    scale: frame >= SAVED ? tween(frame, SAVED, SAVED + 12, 1.35, 1, backOut) : 1,
                    display: "inline-block",
                    minWidth: L.v ? undefined : 420,
                  }}
                >
                  <Odometer value={size} size={190} color={frame >= SAVED ? c.amberEdge : c.ink} italic={frame >= SAVED} unit="MB" />
                </span>
              </div>
            </AbsoluteFill>
          )}

          {/* 3: the real cat, pleased, and its real quip */}
          {frame >= SKY_SAVED && (
            <AbsoluteFill>
              <div style={{ position: "absolute", left: CATP.x - 31 * CATP.z, top: CATP.y - 37 * CATP.z, scale: tween(frame, SKY_SAVED, SKY_SAVED + 16, 0.4, 1, backOut), transformOrigin: "50% 100%", filter: "drop-shadow(0 40px 50px rgba(60,30,10,0.3))" }}>
                <Clip name="shrink-cat" at={(f) => 130 + (f - SKY_SAVED) / 2} scale={CATP.z} pixelated />
              </div>
              <Confetti at={SKY_SAVED + 6} x={CATP.x} y={CATP.y - 180} count={14} px={9} />
              <div
                style={{
                  position: "absolute",
                  left: pick(L, 860, (1080 - 332 * 2.6) / 2),
                  top: pick(L, 330, 900),
                  transformOrigin: L.v ? "50% 0%" : "0% 100%",
                  scale: tween(frame, SKY_SAVED + 8, SKY_SAVED + 24, 0.3, 1, backOut),
                  opacity: tween(frame, SKY_SAVED + 8, SKY_SAVED + 14, 0, 1),
                  filter: "drop-shadow(0 30px 50px rgba(60,30,10,0.3))",
                }}
              >
                <Clip name="shrink-win2" at={(f) => Math.min(200, f - SKY_SAVED)} scale={2.6} />
              </div>
              <div style={L.v ? { position: "absolute", left: 0, right: 0, top: 1150, display: "flex", justifyContent: "center" } : { position: "absolute", left: 880, top: 600 }}>
                <Slam at={SKY_SAVED + 40} size={pick(L, 150, 118)} color={c.ink}>
                  Light as a <span style={{ fontStyle: "italic", color: c.amberEdge }}>feather.</span>
                </Slam>
              </div>
            </AbsoluteFill>
          )}
          <Caption line="you-pdf" at={PDF_LINE} who="You" text="Make this PDF under two MB." hot={["two", "MB."]} light />
          <Caption line="sky-saved" at={SKY_SAVED} who="Sky" text="Done! That saved nineteen MB." hot={["nineteen"]} light />
        </AbsoluteFill>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
