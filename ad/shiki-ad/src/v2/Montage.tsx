import React from "react";
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from "remotion";
import { backOut, c, easeOut, tween } from "../theme";
import { Letters, Mesh, smooth, useLayout, Whip } from "./parts";

// Half a second each, on the beat: a word, and the real tile from Shiki's Tools page.
export const CUTS = [
  { word: "Paw to ask.", tile: "paw", night: true },
  { word: "Snip.", tile: "snip", night: false },
  { word: "Copy text.", tile: "copytext", night: true },
  { word: "Cut out.", tile: "cutout", night: false },
  { word: "Sign.", tile: "sign", night: true },
  { word: "Merge.", tile: "merge", night: false },
  { word: "Unlock.", tile: "unlock", night: true },
  { word: "Record.", tile: "record", night: false },
];
export const CUT = 30;
const T = 9; // each change takes this many frames, ending on the beat
const KINDS = ["push", "flip", "zoom", "rise", "push", "flip", "zoom"] as const;

const TILE_W = 780; // tile images are 278 px wide, the icon square at 32,32 and 47 across

const Card: React.FC<{ i: number }> = ({ i }) => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const f = frame - i * CUT;
  const cut = CUTS[i];
  const left = i % 2 === 0;
  const tileW = L.v ? 900 : TILE_W;
  const k = tileW / 278;
  const icon = { x: 32 * k, y: 32 * k, s: 47 * k };
  const pop = tween(f, 6, 14, 0, 1, backOut) * (1 - tween(f, 14, 24, 0, 1, easeOut));
  const sweep = tween(f, 4, 24, -40, 140, (t) => t);
  return (
    <AbsoluteFill style={{ overflow: "hidden" }}>
      <Mesh mode={cut.night ? "night" : "cream"} intensity={1.2} />
      <div style={{ position: "absolute", left: L.v ? (1080 - tileW) / 2 : left ? 1010 : 130, top: L.v ? 520 : 250, perspective: 1600 }}>
        <div
          style={{
            position: "relative",
            rotate: `y ${(left ? -1 : 1) * tween(f, 0, CUT, 16, 8)}deg`,
            translate: `${(left ? 1 : -1) * tween(f, 0, CUT, 60, 0, easeOut)}px ${tween(f, 0, 12, 40, 0, backOut)}px`,
            scale: tween(f, 0, 10, 0.85, 1, backOut),
          }}
        >
          <Img
            src={staticFile(`tiles/${cut.tile}.png`)}
            style={{ width: tileW, display: "block", borderRadius: 34, boxShadow: cut.night ? "0 60px 120px rgba(0,0,0,0.6), 0 0 0 1.5px rgba(255,255,255,0.08)" : "0 60px 120px rgba(90,50,20,0.3)" }}
          />
          {/* a band of light passes over the tile */}
          <div style={{ position: "absolute", inset: 0, borderRadius: 34, overflow: "hidden", mixBlendMode: "screen" }}>
            <div style={{ position: "absolute", inset: 0, background: `linear-gradient(115deg, transparent ${sweep - 14}%, rgba(255,214,160,0.28) ${sweep}%, transparent ${sweep + 14}%)` }} />
          </div>
          {/* the real icon pops out of the tile */}
          <div
            style={{
              position: "absolute",
              left: icon.x,
              top: icon.y,
              width: icon.s,
              height: icon.s,
              borderRadius: 26,
              backgroundImage: `url(${staticFile(`tiles/${cut.tile}.png`)})`,
              backgroundSize: `${tileW}px auto`,
              backgroundPosition: `${-icon.x}px ${-icon.y}px`,
              scale: 1 + pop * 0.45,
              rotate: `${pop * (left ? -10 : 10)}deg`,
              boxShadow: `0 ${10 + pop * 30}px ${20 + pop * 40}px rgba(246,169,69,${0.15 + pop * 0.45})`,
              translate: `0 ${-pop * 18}px`,
            }}
          />
        </div>
      </div>
      <div style={L.v ? { position: "absolute", left: 0, right: 0, top: 1290, display: "flex", justifyContent: "center" } : { position: "absolute", left: left ? 130 : 1010, top: 0, bottom: 0, display: "flex", alignItems: "center" }}>
        <Letters at={i * CUT} size={L.v ? (cut.word.length > 7 ? 140 : 190) : cut.word.length > 7 ? 150 : 200} text={cut.word} color={cut.night ? c.text : c.ink} italic={i % 3 === 1} stagger={1.4} />
      </div>
    </AbsoluteFill>
  );
};

/** One card, or two while one gives way to the next. */
export const Montage: React.FC = () => {
  const frame = useCurrentFrame();
  const L = useLayout();
  const i = Math.min(CUTS.length - 1, Math.floor(frame / CUT));
  const into = (i + 1) * CUT - frame; // frames left before the next card's beat
  if (i >= CUTS.length - 1 || into > T) return <Card i={i} />;
  const p = smooth(1 - into / T);
  const kind = KINDS[i];
  const blur = Math.sin(p * Math.PI) * 50;
  if (kind === "push" || kind === "rise") {
    const [dx, dy] = kind === "push" ? [-L.W, 0] : [0, -L.H];
    return (
      <Whip amount={kind === "push" ? blur : 0}>
        <AbsoluteFill style={{ translate: `${dx * p}px ${dy * p}px`, filter: kind === "rise" ? `blur(${blur / 6}px)` : undefined }}>
          <Card i={i} />
        </AbsoluteFill>
        <AbsoluteFill style={{ translate: `${-dx * (1 - p)}px ${-dy * (1 - p)}px`, filter: kind === "rise" ? `blur(${blur / 6}px)` : undefined }}>
          <Card i={i + 1} />
        </AbsoluteFill>
      </Whip>
    );
  }
  if (kind === "flip") {
    const first = p < 0.5;
    return (
      <AbsoluteFill style={{ perspective: 2200, background: "#0D0B0A" }}>
        <AbsoluteFill style={{ rotate: `y ${first ? -p * 180 : (1 - p) * 180}deg`, scale: 1 - Math.sin(p * Math.PI) * 0.12 }}>
          <Card i={first ? i : i + 1} />
        </AbsoluteFill>
      </AbsoluteFill>
    );
  }
  // zoom: the old card rushes at you, the new one settles in behind it
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ scale: 0.75 + 0.25 * p, opacity: p }}>
        <Card i={i + 1} />
      </AbsoluteFill>
      <AbsoluteFill style={{ scale: 1 + p * 0.8, opacity: Math.pow(1 - p, 2), filter: `blur(${p * 10}px)` }}>
        <Card i={i} />
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
