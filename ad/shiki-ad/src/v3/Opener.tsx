import React from "react";
import { AbsoluteFill } from "remotion";
import { Arrow, C, Dot, Fx, Icon, IconName, PIXEL, Ring, SANS, Typed, inCubic, inOut, inQuart, outCubic, outQuint, rand, spr, tw, useU, POP, SNAP } from "./kit";
import { Span } from "./timing";

// How it used to be done: 2006, 2014, 2021, each moving the way its time moved, then all of
// it pulled into one point. The UIs are generic mock-ups: no real marks, no copied chrome.

const T2014 = 92;
const T2021 = 184;
const TCOLLAPSE = 264;

// ---------------------------------------------------------------- 2006

const OLD = "Tahoma, Verdana, 'Segoe UI', sans-serif";
const GREY = "#C6C9CE";
const up: React.CSSProperties = { background: GREY, boxShadow: "inset 2px 2px 0 #FFFFFF, inset -2px -2px 0 #70747C, 0 0 0 1px #17181B" };
const down: React.CSSProperties = { background: "#FFFFFF", boxShadow: "inset 2px 2px 0 #70747C, inset -2px -2px 0 #FFFFFF" };
const TITLE = "#2F3E7A";

const OldTitle: React.FC<{ text: string; h?: number }> = ({ text, h = 38 }) => (
  <div style={{ position: "absolute", left: 4, top: 4, right: 4, height: h, background: TITLE, color: "#fff", fontFamily: OLD, fontWeight: 700, fontSize: h * 0.5, display: "flex", alignItems: "center", paddingLeft: 10 }}>
    {text}
    <div style={{ marginLeft: "auto", display: "flex", gap: 3, paddingRight: 5 }}>
      {["_", "□", "x"].map((s) => (
        <div key={s} style={{ ...up, width: h - 12, height: h - 12, color: "#111", fontSize: h * 0.42, display: "flex", alignItems: "center", justifyContent: "center", lineHeight: 1 }}>
          {s}
        </div>
      ))}
    </div>
  </div>
);
const OldButton: React.FC<{ text: string; x: number; y: number; w?: number; pressed?: boolean }> = ({ text, x, y, w = 110, pressed }) => (
  <div style={{ ...(pressed ? { ...down, background: GREY } : up), position: "absolute", left: x, top: y, width: w, height: 36, fontFamily: OLD, fontSize: 19, color: "#111", display: "flex", alignItems: "center", justifyContent: "center" }}>
    {text}
  </div>
);

const CLICKS = [10, 20, 30, 35, 40, 45, 50, 60, 65, 75, 80, 88];
const WIN = { x: 500, y: 190, w: 940, h: 600 };
// the cursor's stops (stage pixels): it is read through the stepped clock, so it jumps
const PATH: [number, number, number][] = [
  [0, 1180, 700],
  [10, WIN.x + 34, WIN.y + 62],
  [20, WIN.x + 96, WIN.y + 196],
  [30, WIN.x + 520, WIN.y + 246],
  [35, WIN.x + 716, WIN.y + 304],
  [40, WIN.x + 600, WIN.y + 372],
  [45, WIN.x + 560, WIN.y + 372],
  [50, WIN.x + 560, WIN.y + 432],
  [60, WIN.x + 430, WIN.y + 378],
  [65, WIN.x + 560, WIN.y + 378],
  [75, WIN.x + 700, WIN.y + 330],
  [80, WIN.x + 640, WIN.y + 392],
  [88, WIN.x + 640, WIN.y + 392],
];
const along = (q: number) => {
  for (let k = 1; k < PATH.length; k++) {
    if (q <= PATH[k][0]) {
      const a = PATH[k - 1];
      const b = PATH[k];
      const t = (q - a[0]) / (b[0] - a[0]);
      return [a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
    }
  }
  const z = PATH[PATH.length - 1];
  return [z[1], z[2]];
};

/** The 2006 window with whatever is open at stepped time `q`. */
const Win2006: React.FC<{ q: number }> = ({ q }) => {
  const menu = q >= 10 && q < 25;
  const dialog = q >= 25 && q < 55;
  const list = q >= 35 && q < 50;
  const alert = q >= 55 && q < 70;
  const progress = q >= 70;
  const items = ["New", "Open...", "Save", "Save As...", "Export...", "Exit"];
  const types = ["Bitmap (*.bmp)", "JPEG (*.jpg)", "GIF (*.gif)", "TIFF (*.tif)"];
  return (
    <div style={{ ...up, position: "absolute", left: WIN.x, top: WIN.y, width: WIN.w, height: WIN.h }}>
      <OldTitle text="Picture Editor - holiday.bmp" />
      <div style={{ position: "absolute", left: 6, top: 46, height: 32, display: "flex", fontFamily: OLD, fontSize: 20, color: "#111" }}>
        {["File", "Edit", "View", "Image", "Help"].map((m, i) => (
          <div key={m} style={{ padding: "3px 12px", background: menu && i === 0 ? TITLE : "transparent", color: menu && i === 0 ? "#fff" : "#111" }}>
            {m}
          </div>
        ))}
      </div>
      <div style={{ ...down, position: "absolute", left: 10, top: 84, right: 10, bottom: 34 }}>
        <div style={{ position: "absolute", left: 30, top: 26, width: 560, height: 390, background: "#9CC3EE", overflow: "hidden" }}>
          <div style={{ position: "absolute", left: 400, top: 50, width: 70, height: 70, background: "#F7E27A" }} />
          <div style={{ position: "absolute", left: -40, top: 250, width: 420, height: 200, background: "#5E9A63" }} />
          <div style={{ position: "absolute", left: 250, top: 290, width: 420, height: 200, background: "#4A8552" }} />
          <div style={{ position: "absolute", left: 110, top: 190, width: 90, height: 90, background: "#D9D4C6" }} />
          <div style={{ position: "absolute", left: 95, top: 160, width: 120, height: 34, background: "#9B4B3C" }} />
        </div>
      </div>
      <div style={{ position: "absolute", left: 10, bottom: 6, right: 10, height: 24, fontFamily: OLD, fontSize: 16, color: "#333", display: "flex", alignItems: "center" }}>
        holiday.bmp &nbsp; 2048 x 1536 &nbsp; 9.2 MB
      </div>
      {menu ? (
        <div style={{ ...up, position: "absolute", left: 6, top: 78, width: 230, padding: 3 }}>
          {items.map((m, i) => {
            const hot = q >= 15 && i === 3;
            return (
              <div key={m} style={{ fontFamily: OLD, fontSize: 20, padding: "5px 22px", background: hot ? TITLE : "transparent", color: hot ? "#fff" : "#111" }}>
                {m}
              </div>
            );
          })}
        </div>
      ) : null}
      {dialog ? (
        <div style={{ ...up, position: "absolute", left: 190, top: 150, width: 580, height: 320 }}>
          <OldTitle text="Save As" h={34} />
          <div style={{ position: "absolute", left: 24, top: 68, fontFamily: OLD, fontSize: 19, color: "#111" }}>File name:</div>
          <div style={{ ...down, position: "absolute", left: 150, top: 60, width: 400, height: 36, fontFamily: OLD, fontSize: 19, color: "#111", display: "flex", alignItems: "center", paddingLeft: 8, boxSizing: "border-box" }}>
            <span style={{ background: q >= 30 ? TITLE : "transparent", color: q >= 30 ? "#fff" : "#111" }}>holiday</span>
          </div>
          <div style={{ position: "absolute", left: 24, top: 126, fontFamily: OLD, fontSize: 19, color: "#111" }}>Save as type:</div>
          <div style={{ ...down, position: "absolute", left: 150, top: 118, width: 400, height: 36, fontFamily: OLD, fontSize: 19, color: "#111", display: "flex", alignItems: "center", paddingLeft: 8, boxSizing: "border-box" }}>
            {q >= 45 ? types[1] : types[0]}
            <div style={{ ...up, position: "absolute", right: 2, top: 2, width: 32, height: 32, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 14 }}>▼</div>
          </div>
          {list ? (
            <div style={{ ...down, position: "absolute", left: 150, top: 154, width: 400, boxShadow: "0 0 0 1px #17181B" }}>
              {types.map((t, i) => {
                const hot = q >= 40 && i === 1;
                return (
                  <div key={t} style={{ fontFamily: OLD, fontSize: 19, padding: "4px 8px", background: hot ? TITLE : "#fff", color: hot ? "#fff" : "#111" }}>
                    {t}
                  </div>
                );
              })}
            </div>
          ) : null}
          <OldButton text="Save" x={316} y={262} pressed={q >= 50} />
          <OldButton text="Cancel" x={440} y={262} />
        </div>
      ) : null}
      {alert ? (
        <div style={{ ...up, position: "absolute", left: 230, top: 200, width: 500, height: 230 }}>
          <OldTitle text={q >= 65 ? "Confirm" : "Export"} h={34} />
          <div style={{ position: "absolute", left: 28, top: 64, width: 54, height: 54, background: "#E9C63B", boxShadow: "0 0 0 2px #17181B", fontFamily: OLD, fontWeight: 700, fontSize: 40, display: "flex", alignItems: "center", justifyContent: "center", color: "#111" }}>!</div>
          <div style={{ position: "absolute", left: 104, top: 66, width: 370, fontFamily: OLD, fontSize: 19, lineHeight: 1.3, color: "#111" }}>
            {q >= 65 ? "holiday.jpg already exists. Replace it?" : "Some picture quality may be lost. Do you want to continue?"}
          </div>
          <OldButton text={q >= 65 ? "Yes" : "OK"} x={136} y={170} pressed={q === 60} />
          <OldButton text={q >= 65 ? "No" : "Cancel"} x={262} y={170} />
        </div>
      ) : null}
      {progress ? (
        <div style={{ ...up, position: "absolute", left: 210, top: 220, width: 540, height: 190 }}>
          <OldTitle text="Exporting..." h={34} />
          <div style={{ position: "absolute", left: 26, top: 62, fontFamily: OLD, fontSize: 19, color: "#111" }}>Writing holiday.jpg</div>
          <div style={{ ...down, position: "absolute", left: 26, top: 96, width: 488, height: 34, display: "flex", gap: 4, padding: 5, boxSizing: "border-box" }}>
            {Array.from({ length: Math.min(9, Math.floor((q - 70) / 5) + 1) }, (_, i) => (
              <div key={i} style={{ width: 24, background: TITLE }} />
            ))}
          </div>
          <OldButton text="Cancel" x={404} y={140} />
        </div>
      ) : null}
    </div>
  );
};

const Era2006: React.FC<{ e: number }> = ({ e }) => {
  // twelve pictures a second, on purpose: everything here moves in steps
  const q = Math.floor(e / 5) * 5;
  const [cx, cy] = along(q);
  const count = CLICKS.filter((c) => c <= q).length;
  const pressing = CLICKS.indexOf(q) >= 0;
  return (
    <AbsoluteFill style={{ background: "#4A7C84" }}>
      {["My Files", "Pictures", "Bin"].map((n, i) => (
        <div key={n} style={{ position: "absolute", left: 1730, top: 230 + i * 150, width: 110, textAlign: "center", fontFamily: OLD, fontSize: 18, color: "#fff" }}>
          <div style={{ ...up, width: 62, height: 52, margin: "0 auto 8px", background: i === 2 ? "#AEB4BC" : "#E3C567" }} />
          {n}
        </div>
      ))}
      <Win2006 q={q} />
      <div style={{ ...up, position: "absolute", left: 0, right: 0, bottom: 0, height: 50 }}>
        <div style={{ ...up, position: "absolute", left: 6, top: 6, width: 110, height: 38, fontFamily: OLD, fontWeight: 700, fontSize: 20, display: "flex", alignItems: "center", justifyContent: "center", color: "#111" }}>Menu</div>
        <div style={{ ...down, background: GREY, position: "absolute", left: 130, top: 6, width: 260, height: 38, fontFamily: OLD, fontSize: 18, display: "flex", alignItems: "center", paddingLeft: 12, boxSizing: "border-box", color: "#111" }}>Picture Editor</div>
        <div style={{ ...down, background: GREY, position: "absolute", right: 6, top: 6, width: 100, height: 38, fontFamily: OLD, fontSize: 18, display: "flex", alignItems: "center", justifyContent: "center", color: "#111" }}>10:42</div>
      </div>
      {/* the click counter */}
      <div style={{ ...up, position: "absolute", left: 1466, top: 84, width: 364, height: 96, display: "flex", alignItems: "center", padding: "0 20px", boxSizing: "border-box", gap: 18 }}>
        <div style={{ fontFamily: OLD, fontWeight: 700, fontSize: 28, color: "#111" }}>CLICKS</div>
        <div style={{ ...down, background: "#111", marginLeft: "auto", width: 130, height: 68, fontFamily: PIXEL, fontWeight: 600, fontSize: 60, color: "#7CFF8A", display: "flex", alignItems: "center", justifyContent: "center" }}>
          {count < 10 ? `0${count}` : count}
        </div>
      </div>
      <Arrow x={cx + (pressing ? 2 : 0)} y={cy + (pressing ? 3 : 0)} size={36} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 2014

const FLAT = { bg: "#ECF0F1", dark: "#2C3E50", teal: "#1ABC9C", red: "#E74C3C", blue: "#3498DB", orange: "#F39C12", grey: "#BDC3C7" };

const AdBox: React.FC<{ x: number; y: number; w: number; h: number; text?: string; color?: string }> = ({ x, y, w, h, text = "AD", color = FLAT.grey }) => (
  <div
    style={{
      position: "absolute",
      left: x,
      top: y,
      width: w,
      height: h,
      background: `repeating-linear-gradient(135deg, ${color} 0 14px, #D5DBDE 14px 28px)`,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      fontFamily: SANS,
      fontWeight: 700,
      fontSize: 22,
      color: "#7F8C8D",
      letterSpacing: "0.1em",
    }}
  >
    <span style={{ background: "#ECF0F1", padding: "2px 10px" }}>{text}</span>
  </div>
);

const Browser2014: React.FC<{ e: number }> = ({ e }) => {
  const pct = Math.min(43, Math.round(tw(e, 14, 44, 0, 43, outCubic)));
  const stuck = e > 44;
  return (
    <div style={{ position: "absolute", left: 130, top: 236, width: 980, height: 600, background: "#fff", boxShadow: "0 6px 0 rgba(44,62,80,0.18)" }}>
      <div style={{ height: 46, background: FLAT.dark, display: "flex", alignItems: "flex-end", paddingLeft: 14, gap: 4 }}>
        <div style={{ background: "#fff", height: 34, width: 280, fontFamily: SANS, fontWeight: 600, fontSize: 16, color: FLAT.dark, display: "flex", alignItems: "center", paddingLeft: 14, boxSizing: "border-box" }}>FREE File Converter Onli…</div>
        <div style={{ background: "#3D566E", height: 30, width: 180 }} />
        <div style={{ background: "#3D566E", height: 30, width: 180 }} />
      </div>
      <div style={{ height: 46, background: "#DDE3E6", display: "flex", alignItems: "center", padding: "0 14px" }}>
        <div style={{ background: "#fff", flex: 1, height: 30, fontFamily: SANS, fontSize: 16, color: "#7F8C8D", display: "flex", alignItems: "center", paddingLeft: 12 }}>www.free-file-converter-online.example/upload</div>
      </div>
      <div style={{ position: "absolute", left: 0, top: 92, right: 0, height: 70, background: FLAT.teal, color: "#fff", fontFamily: SANS, fontWeight: 700, fontSize: 30, display: "flex", alignItems: "center", paddingLeft: 26, letterSpacing: "0.02em" }}>
        CONVERT ANY FILE. 100% FREE!!
      </div>
      <AdBox x={26} y={182} w={700} h={84} text="ADVERTISEMENT" />
      <AdBox x={756} y={182} w={198} h={392} />
      <div style={{ position: "absolute", left: 26, top: 290, width: 700, height: 170, background: FLAT.bg }}>
        <div style={{ position: "absolute", left: 24, top: 20, fontFamily: SANS, fontWeight: 700, fontSize: 24, color: FLAT.dark }}>holiday.bmp → JPG</div>
        <div style={{ position: "absolute", left: 24, top: 62, fontFamily: SANS, fontWeight: 500, fontSize: 21, color: stuck ? FLAT.red : "#7F8C8D" }}>
          Uploading… {pct}%{stuck ? "  (still 43%)" : ""}
        </div>
        <div style={{ position: "absolute", left: 24, top: 106, width: 652, height: 30, background: FLAT.grey }}>
          <div style={{ width: `${pct}%`, height: "100%", background: FLAT.blue }} />
        </div>
      </div>
      <div style={{ position: "absolute", left: 26, top: 484, width: 330, height: 90, background: FLAT.orange, color: "#fff", fontFamily: SANS, fontWeight: 700, fontSize: 28, display: "flex", alignItems: "center", justifyContent: "center", boxShadow: `0 6px 0 #C87F0A` }}>DOWNLOAD NOW ▶</div>
      <AdBox x={386} y={484} w={340} h={90} text="SPONSORED" />
    </div>
  );
};

const Mail2014: React.FC<{ e: number }> = ({ e }) => {
  const inX = tw(e, 26, 50, 760, 0, outQuint);
  const chip = spr(e, 54, POP);
  return (
    <div style={{ position: "absolute", left: 1160, top: 300, width: 630, height: 480, background: "#fff", transform: `translateX(${inX}px)`, boxShadow: "0 6px 0 rgba(44,62,80,0.18)" }}>
      <div style={{ height: 58, background: FLAT.red, color: "#fff", fontFamily: SANS, fontWeight: 700, fontSize: 24, display: "flex", alignItems: "center", paddingLeft: 24 }}>New message</div>
      {[
        ["To", "me"],
        ["Subject", "photo (for me)"],
      ].map(([k, v]) => (
        <div key={k} style={{ height: 56, borderBottom: `2px solid ${FLAT.bg}`, display: "flex", alignItems: "center", paddingLeft: 24, fontFamily: SANS, fontSize: 21, color: "#95A5A6" }}>
          {k}:&nbsp;<span style={{ color: FLAT.dark, fontWeight: 600 }}>{v}</span>
        </div>
      ))}
      <div style={{ padding: "22px 24px", fontFamily: SANS, fontSize: 21, color: "#95A5A6" }}>sending this to myself again</div>
      <div
        style={{
          position: "absolute",
          left: 24,
          top: 268,
          height: 64,
          padding: "0 20px 0 14px",
          background: FLAT.bg,
          display: "flex",
          alignItems: "center",
          gap: 10,
          fontFamily: SANS,
          fontWeight: 600,
          fontSize: 21,
          color: FLAT.dark,
          transform: `scale(${chip})`,
          transformOrigin: "left center",
          opacity: Math.min(1, chip * 2),
        }}
      >
        <Icon name="clip" size={30} color={FLAT.dark} stroke={2} />
        photo.jpg <span style={{ color: FLAT.red, fontWeight: 500 }}>(compressed to 1.2 MB)</span>
      </div>
      <div style={{ position: "absolute", left: 24, bottom: 26, width: 150, height: 56, background: FLAT.blue, color: "#fff", fontFamily: SANS, fontWeight: 700, fontSize: 22, display: "flex", alignItems: "center", justifyContent: "center", boxShadow: "0 5px 0 #2378B5" }}>SEND</div>
    </div>
  );
};

const Era2014: React.FC<{ e: number }> = ({ e }) => {
  const cx = tw(e, 8, 26, 700, 420, inOut) + tw(e, 48, 70, 0, 900, inOut);
  const cy = tw(e, 8, 26, 900, 600, inOut) + tw(e, 48, 70, 0, 20, inOut);
  return (
    <AbsoluteFill style={{ background: FLAT.bg }}>
      <Browser2014 e={e} />
      <Mail2014 e={e} />
      <Arrow x={cx} y={cy} size={32} dark />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 2021

const APPS: { icon: IconName; color: string }[] = [
  { icon: "chat", color: "#4FA36C" },
  { icon: "mail", color: "#4C7BD9" },
  { icon: "cloud", color: "#5BA7C9" },
  { icon: "folder", color: "#D9A441" },
  { icon: "photo", color: "#C9566A" },
  { icon: "cable", color: "#7C8698" },
];
const HOPS = [0, 3, 1, 5, 2, 4, 1, 5, 0];
const tileX = (i: number) => 960 + (i - 2.5) * 200;

const Era2021: React.FC<{ e: number }> = ({ e }) => {
  // the cursor ping-pongs: a new app every nine frames
  const k = Math.max(0, Math.min(HOPS.length - 2, Math.floor(e / 9)));
  const hop = spr(e, k * 9, { stiffness: 420, damping: 22 });
  const cx = tileX(HOPS[k]) + (tileX(HOPS[k + 1]) - tileX(HOPS[k])) * hop;
  const active = hop > 0.6 ? HOPS[k + 1] : HOPS[k];
  const toasts = ["Upload failed. Try again?", "Storage almost full", "Cable not recognised"];
  return (
    <AbsoluteFill style={{ background: "linear-gradient(135deg, #1D2438 0%, #2B3A5C 55%, #3B4A6B 100%)" }}>
      <div style={{ position: "absolute", left: 300, top: 120, width: 700, height: 700, borderRadius: "50%", background: "radial-gradient(circle, rgba(120,170,255,0.25), transparent 65%)" }} />
      {APPS.map((a, i) => {
        const inS = spr(e, i * 3, POP);
        const bob = Math.sin(e * 0.22 + i * 1.7) * 12;
        const hot = i === active ? 1.14 : 1;
        const badge = spr(e, 14 + ((i * 17) % 40), POP);
        const n = 1 + Math.floor(Math.max(0, e - 14 - ((i * 17) % 40)) / (6 + i));
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              left: tileX(i) - 78,
              top: 330 + bob,
              width: 156,
              height: 156,
              borderRadius: 44,
              background: `linear-gradient(160deg, ${a.color}, ${a.color}CC)`,
              boxShadow: "0 24px 50px rgba(0,0,0,0.35), inset 0 1px 0 rgba(255,255,255,0.35)",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              transform: `scale(${inS * hot}) rotate(${Math.sin(e * 0.3 + i) * 2.5}deg)`,
            }}
          >
            <Icon name={a.icon} size={84} color="#fff" stroke={1.7} />
            <div
              style={{
                position: "absolute",
                right: -10,
                top: -10,
                minWidth: 46,
                height: 46,
                borderRadius: 23,
                background: "#F0524F",
                color: "#fff",
                fontFamily: SANS,
                fontWeight: 700,
                fontSize: 24,
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                transform: `scale(${badge})`,
                boxShadow: "0 0 0 4px #2B3A5C",
              }}
            >
              {n}
            </div>
          </div>
        );
      })}
      {/* a chat thread that never quite sends */}
      <div style={{ position: "absolute", left: 610, top: 600, width: 700, height: 250, borderRadius: 36, background: "rgba(255,255,255,0.12)", border: "1px solid rgba(255,255,255,0.22)", transform: `translateY(${(1 - spr(e, 6, SNAP)) * 260}px)` }}>
        <div style={{ position: "absolute", right: 28, top: 26, padding: "12px 22px", borderRadius: 26, background: "#4C7BD9", color: "#fff", fontFamily: SANS, fontWeight: 500, fontSize: 24, transform: `scale(${spr(e, 14, POP)})`, transformOrigin: "right center" }}>sending you the pics now</div>
        <div style={{ position: "absolute", right: 28, top: 92, padding: "12px 22px", borderRadius: 26, background: "#4C7BD9", color: "#fff", fontFamily: SANS, fontWeight: 500, fontSize: 24, transform: `scale(${spr(e, 34, POP)})`, transformOrigin: "right center" }}>wait, wrong app</div>
        <div style={{ position: "absolute", left: 28, top: 164, height: 54, padding: "0 22px", borderRadius: 27, background: "rgba(255,255,255,0.2)", display: "flex", alignItems: "center", gap: 9, transform: `scale(${spr(e, 44, POP)})`, transformOrigin: "left center" }}>
          {[0, 1, 2].map((d) => (
            <div key={d} style={{ width: 13, height: 13, borderRadius: 7, background: "#fff", transform: `translateY(${Math.sin(e * 0.5 - d * 0.9) * 6}px)`, opacity: 0.6 + 0.4 * Math.sin(e * 0.5 - d * 0.9) }} />
          ))}
        </div>
      </div>
      {toasts.map((t, i) => {
        const s = spr(e, 20 + i * 16, SNAP);
        return (
          <div key={t} style={{ position: "absolute", right: 70, top: 96 + i * 82, height: 64, padding: "0 26px", borderRadius: 20, background: "rgba(255,255,255,0.94)", color: "#1D2438", fontFamily: SANS, fontWeight: 600, fontSize: 22, display: "flex", alignItems: "center", transform: `translateX(${(1 - s) * 560}px)`, boxShadow: "0 16px 40px rgba(0,0,0,0.3)" }}>
            {t}
          </div>
        );
      })}
      <Arrow x={cx - 6} y={430 + Math.sin(e * 0.7) * 16} size={34} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- the year and the caption

const YEARS = ["2006", "2014", "2021"];
const Year: React.FC<{ u: number }> = ({ u }) => {
  const H = 150;
  const p1 = spr(u, T2014 - 4, { stiffness: 200, damping: 19 });
  const p2 = spr(u, T2021 - 4, { stiffness: 200, damping: 19 });
  const light = u < T2014 + 6 || u >= T2021 + 4;
  return (
    <div style={{ position: "absolute", left: 84, top: 56, height: H, display: "flex", overflow: "hidden", fontFamily: PIXEL, fontWeight: 700, fontSize: 150, lineHeight: `${H}px`, color: light ? "#fff" : FLAT.dark, textShadow: u < T2014 + 6 ? "5px 5px 0 #17181B" : "none" }}>
      {[0, 1, 2, 3].map((i) => {
        const d = YEARS.map((y) => Number(y[i]));
        const s1 = (d[1] - d[0] + 10) % 10;
        const s2 = (d[2] - d[1] + 10) % 10;
        const pos = d[0] + s1 * p1 + s2 * p2;
        return (
          <div key={i} style={{ height: H, width: 88, textAlign: "center", overflow: "hidden" }}>
            <div style={{ transform: `translateY(${-pos * H}px)` }}>
              {Array.from({ length: 30 }, (_, n) => (
                <div key={n} style={{ height: H }}>
                  {n % 10}
                </div>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
};

const CAPTIONS: [number, number, string, boolean][] = [
  [0, T2014, "It took twelve clicks.", true],
  [T2014, T2021, "Then a website and an email to yourself.", false],
  [T2021, TCOLLAPSE + 8, "Then six apps.", true],
];
const Caption: React.FC<{ u: number }> = ({ u }) => (
  <>
    {CAPTIONS.map(([a, b, text, light]) => {
      if (u < a + 4 || u > b + 4) return null;
      const inP = tw(u, a + 8, a + 26, 0, 1, outQuint);
      const outP = tw(u, b - 8, b, 0, 1, inCubic);
      return (
        <div key={text} style={{ position: "absolute", left: 0, right: 0, top: 916, display: "flex", justifyContent: "center", opacity: inP * (1 - outP), transform: `translateY(${(1 - inP) * 26 - outP * 14}px)` }}>
          <div style={{ fontFamily: SANS, fontWeight: 600, fontSize: 42, color: light ? "#fff" : FLAT.dark, background: light ? "rgba(10,13,20,0.55)" : "rgba(255,255,255,0.85)", padding: "12px 34px", borderRadius: 40 }}>{text}</div>
        </div>
      );
    })}
  </>
);

// ---------------------------------------------------------------- the opener

export const Opener: React.FC<{ span: Span; reel?: boolean; mute?: boolean; bare?: boolean }> = ({ span, reel, mute, bare }) => {
  const u = useU(span);
  const v = u - TCOLLAPSE;

  // each era arrives the way its time moved: a hard cut, a flat push, a slick zoom
  const push = tw(u, T2014 - 7, T2014 + 9, 1920, 0, inOut);
  const zoom = tw(u, T2021 - 5, T2021 + 11, 0, 1, outQuint);

  // the collapse: a breath in, then everything falls into one point
  const s = v < 6 ? tw(v, 0, 6, 1, 1.05, outCubic) : tw(v, 6, 28, 1.05, 0, inQuart);
  const rot = tw(v, 6, 28, 0, -14, inQuart);
  const radius = tw(v, 6, 22, 0, 540, inCubic);
  const blur = tw(v, 10, 27, 0, 12, inCubic);
  const hit = 28;

  return (
    <AbsoluteFill>
      {v < hit ? (
        <AbsoluteFill style={{ transform: `scale(${s}) rotate(${rot}deg)`, borderRadius: radius, overflow: "hidden", filter: blur > 0.2 ? `blur(${blur}px)` : undefined }}>
          {u < T2014 + 10 ? <Era2006 e={u} /> : null}
          {u >= T2014 - 8 && u < T2021 + 12 ? (
            <AbsoluteFill style={{ transform: `translateX(${push}px)` }}>
              <Era2014 e={u - T2014} />
            </AbsoluteFill>
          ) : null}
          {u >= T2021 - 6 ? (
            <AbsoluteFill style={{ opacity: Math.min(1, zoom * 3), transform: `scale(${1.5 - 0.5 * zoom})`, filter: zoom < 0.98 ? `blur(${(1 - zoom) * 22}px)` : undefined }}>
              <Era2021 e={Math.min(u, TCOLLAPSE + 4) - T2021} />
            </AbsoluteFill>
          ) : null}
          <Year u={u} />
          {bare ? null : <Caption u={u} />}
        </AbsoluteFill>
      ) : null}

      {/* the older ways are pulled in too: the 2006 window and the 2014 tab, as small cards */}
      {v > 0 && v < hit
        ? [0, 1].map((i) => {
            const p = tw(v, 2 + i * 4, 26 + i * 2, 0, 1, inCubic);
            const from = i === 0 ? [-520, 230] : [2440, 850];
            const ang = (i === 0 ? 1 : -1) * (1 - p) * 1.3;
            const dx = from[0] - 960;
            const dy = from[1] - 540;
            const x = 960 + (dx * Math.cos(ang) - dy * Math.sin(ang)) * (1 - p);
            const y = 540 + (dx * Math.sin(ang) + dy * Math.cos(ang)) * (1 - p);
            return (
              <div key={i} style={{ position: "absolute", left: x - 960, top: y - 540, width: 1920, height: 1080, transform: `scale(${0.5 * (1 - p * p)}) rotate(${(i === 0 ? -1 : 1) * p * 50}deg)`, filter: `blur(${p * 6}px)`, opacity: Math.min(1, p * 8) }}>
                {i === 0 ? <Win2006 q={50} /> : <Browser2014 e={60} />}
              </div>
            );
          })
        : null}

      {/* streaks of light rushing to the point */}
      {v > 4 && v < hit + 4
        ? Array.from({ length: 30 }, (_, i) => {
            const a = rand(i) * Math.PI * 2;
            const start = 6 + rand(i + 40) * 10;
            const p = tw(v, start, hit, 0, 1, inCubic);
            const r = (560 + rand(i + 80) * 620) * (1 - p);
            const len = (60 + rand(i + 9) * 150) * (0.3 + p) * (1 - p * 0.6);
            return (
              <div
                key={i}
                style={{
                  position: "absolute",
                  left: 960 + Math.cos(a) * r,
                  top: 540 + Math.sin(a) * r - 1,
                  width: len,
                  height: 2,
                  background: `linear-gradient(90deg, ${i % 3 === 0 ? C.moon : "#fff"}, transparent)`,
                  transformOrigin: "0 50%",
                  transform: `rotate(${a}rad)`,
                  opacity: Math.sin(Math.PI * Math.min(1, p * 1.05)) * 0.8,
                }}
              />
            );
          })
        : null}

      {/* one point */}
      {v >= hit - 2 ? (
        <>
          <div style={{ position: "absolute", left: 960 - 500, top: 540 - 500, width: 1000, height: 1000, borderRadius: "50%", background: "radial-gradient(circle, rgba(255,255,255,0.9) 0%, rgba(90,185,252,0.35) 18%, transparent 55%)", opacity: tw(v, hit - 2, hit + 16, 0.75, 0, outCubic), transform: `scale(${tw(v, hit - 2, hit + 16, 0.3, 1.2, outQuint)})` }} />
          <Ring x={960} y={540} r={tw(v, hit, hit + 40, 0, 420, outQuint)} width={2} opacity={tw(v, hit, hit + 40, 0.8, 0, outCubic)} />
          <Ring x={960} y={540} r={tw(v, hit + 5, hit + 50, 0, 250, outQuint)} width={2} color="#fff" opacity={tw(v, hit + 5, hit + 50, 0.6, 0, outCubic)} />
          <Dot x={960} y={540} r={6 + 10 * (1 - spr(v, hit, POP))} />
          <div style={{ position: "absolute", left: 0, right: 0, top: 330, display: "flex", justifyContent: "center", opacity: tw(v, 84, 94, 1, 0, inCubic), transform: `translateY(${tw(v, 84, 94, 0, -22, inCubic)}px)` }}>
            <Typed text="Now, one sentence." u={v} at={hit + 4} per={1.5} size={112} cursorUntil={84} />
          </div>
        </>
      ) : null}

      {/* sound (a cut that slices the opener brings its own) */}
      {mute ? null : (
        <>
      {CLICKS.filter((_, i) => !reel || i % 4 === 0).map((c) => (
        <Fx key={c} span={span} at={c} name="mouse-click" dir="sfx" v={0.34} />
      ))}
      <Fx span={span} at={T2014 - 6} name="switch" dir="sfx" v={0.45} />
      <Fx span={span} at={T2014 - 4} name="tick" v={0.3} />
      {reel ? null : <Fx span={span} at={T2014 + 54} name="pop" v={0.22} />}
      <Fx span={span} at={T2021 - 5} name="whoosh2" v={0.3} />
      {(reel ? [14, 44] : [6, 18, 30, 42, 54, 66]).map((t) => (
        <Fx key={t} span={span} at={T2021 + t} name="pop" v={0.2} />
      ))}
      <Fx span={span} at={TCOLLAPSE + hit - 118} name="riser" v={0.26} />
      <Fx span={span} at={TCOLLAPSE + 4} name="whoosh3" v={0.5} />
      <Fx span={span} at={TCOLLAPSE + hit - 1} name="impact" v={0.55} />
      {reel ? null : [0, 9, 18].map((t) => <Fx key={t} span={span} at={TCOLLAPSE + hit + 5 + t} name="tick" v={0.2} />)}
        </>
      )}
    </AbsoluteFill>
  );
};
