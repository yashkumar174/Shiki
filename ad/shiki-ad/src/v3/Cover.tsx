import React from "react";
import { AbsoluteFill, Img, staticFile } from "remotion";
import { C, Cat, PIXEL, Ring, SANS, Sprite, rand } from "./kit";

// The Instagram cover for the fast cut: 1080x1920. Everything that matters sits inside the
// middle 1080x1350, the part the profile grid shows.

const W = 1080;
const H = 1920;
const LINE_Y = 1020;

const Sky: React.FC = () => {
  const stars = Array.from({ length: 150 }, (_, i) => ({
    x: Math.round(rand(i * 3 + 1) * 270) * 4,
    y: Math.round(rand(i * 3 + 2) * 480) * 4,
    big: rand(i * 3 + 3) < 0.12,
    o: 0.35 + 0.6 * rand(i + 900),
  }));
  return (
    <AbsoluteFill style={{ background: "linear-gradient(180deg, #08142E 0%, #0B1838 30%, #0B1326 62%, #0A0D14 100%)", overflow: "hidden" }}>
      <div style={{ position: "absolute", left: W / 2 - 760, top: LINE_Y - 860, width: 1520, height: 1520, borderRadius: "50%", background: "radial-gradient(circle, rgba(90,185,252,0.30) 0%, rgba(90,185,252,0.10) 34%, transparent 64%)" }} />
      {stars.map((s, i) =>
        s.big ? (
          <React.Fragment key={i}>
            <div style={{ position: "absolute", left: s.x - 4, top: s.y, width: 12, height: 4, background: "#DCEBFF", opacity: s.o }} />
            <div style={{ position: "absolute", left: s.x, top: s.y - 4, width: 4, height: 12, background: "#DCEBFF", opacity: s.o }} />
          </React.Fragment>
        ) : (
          <div key={i} style={{ position: "absolute", left: s.x, top: s.y, width: 4, height: 4, background: i % 5 === 0 ? C.moon : "#C9D8F0", opacity: s.o * 0.75 }} />
        ),
      )}
    </AbsoluteFill>
  );
};

/** The pixel crescent from Shiki's night window. */
const Moon: React.FC<{ x: number; y: number; cell: number }> = ({ x, y, cell }) => {
  const cells: number[][] = [];
  for (let j = 0; j < 18; j++)
    for (let i = 0; i < 18; i++) {
      const a = Math.hypot(i - 8.5, j - 8.5) <= 8.6;
      const b = Math.hypot(i - 4.6, j - 6.4) <= 7.7;
      if (a && !b) cells.push([i, j, rand(i * 31 + j * 7) < 0.14 ? 1 : 0]);
    }
  return (
    <div style={{ position: "absolute", left: x, top: y, width: cell * 18, height: cell * 18, filter: "drop-shadow(0 0 40px rgba(244,233,200,0.45))" }}>
      {cells.map(([i, j, d], k) => (
        <div key={k} style={{ position: "absolute", left: i * cell, top: j * cell, width: cell, height: cell, background: d ? "#E3D6AC" : "#F4EBC8" }} />
      ))}
    </div>
  );
};

export const CoverV3: React.FC = () => (
  <AbsoluteFill style={{ background: C.ink, width: W, height: H }}>
    <Sky />
    <Moon x={820} y={330} cell={8} />

    {/* the headline */}
    <div style={{ position: "absolute", left: 80, top: 318, fontFamily: PIXEL, fontWeight: 700, lineHeight: 1, color: C.text, textShadow: "0 10px 40px rgba(0,0,0,0.5)" }}>
      <div style={{ fontSize: 168 }}>Say it.</div>
      <div style={{ fontSize: 128, marginTop: 12 }}>
        It happens<span style={{ color: C.moon }}>.</span>
      </div>
    </div>

    {/* Home's tiles behind the cat (the greeting is left out), tilted back like a screen on a desk */}
    <div style={{ position: "absolute", left: 0, top: LINE_Y + 30, width: W, height: 420, perspective: 1300, perspectiveOrigin: "50% 0%" }}>
      <div
        style={{
          position: "absolute",
          left: W / 2 - 500,
          top: 0,
          width: 1000,
          height: 534,
          transformOrigin: "50% 0%",
          transform: "rotateX(42deg)",
          borderRadius: 26,
          overflow: "hidden",
          maskImage: "linear-gradient(180deg, #000 20%, transparent 78%)",
          WebkitMaskImage: "linear-gradient(180deg, #000 20%, transparent 78%)",
        }}
      >
        <Img src={staticFile("v3/shots/home.png")} style={{ position: "absolute", left: -69 * 0.718, top: -264 * 0.718, width: 1550 * 0.718, height: 1025 * 0.718, maxWidth: "none" }} />
      </div>
    </div>

    {/* the cat lands on the line, with rings */}
    {[0, 1, 2].map((i) => (
      <Ring key={i} x={W / 2} y={LINE_Y - 150} r={230 + i * 110} width={i === 0 ? 4 : 2} color={i === 1 ? "#FFFFFF" : C.moon} opacity={0.5 - i * 0.14} />
    ))}
    <div style={{ position: "absolute", left: 60, right: 60, top: LINE_Y - 2, height: 4, background: `linear-gradient(90deg, transparent, ${C.text} 16%, ${C.text} 84%, transparent)`, boxShadow: "0 0 30px rgba(90,185,252,0.8)" }} />
    <div style={{ position: "absolute", left: W / 2 - 190, top: LINE_Y - 22, width: 380, height: 34, borderRadius: "50%", background: "rgba(0,0,0,0.45)", filter: "blur(10px)" }} />
    <Cat pose="Happy" x={W / 2} y={LINE_Y} scale={16} />
    <Sprite name="mochi-float-Sparkle" w={3} h={3} scale={12} x={W / 2 + 200} y={LINE_Y - 360} />
    <Sprite name="mochi-float-Sparkle" w={3} h={3} scale={9} x={W / 2 - 270} y={LINE_Y - 260} />
    <Sprite name="mochi-float-Heart" w={5} h={5} scale={8} x={W / 2 + 230} y={LINE_Y - 200} opacity={0.9} />

    {/* the name and the date */}
    <div style={{ position: "absolute", left: 0, right: 0, top: 1390, display: "flex", flexDirection: "column", alignItems: "center", gap: 24 }}>
      <div style={{ fontFamily: PIXEL, fontWeight: 700, fontSize: 132, lineHeight: 1, color: C.text, textShadow: "0 8px 40px rgba(0,0,0,0.7)" }}>Shiki</div>
      <div style={{ fontFamily: SANS, fontWeight: 700, fontSize: 34, letterSpacing: "0.04em", color: C.ink, background: C.moon, borderRadius: 40, padding: "14px 38px", boxShadow: "0 0 50px rgba(90,185,252,0.5)" }}>Coming soon to Windows</div>
    </div>
  </AbsoluteFill>
);
