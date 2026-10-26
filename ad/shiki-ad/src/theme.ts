import { loadFont as loadInter } from "@remotion/google-fonts/Inter";
import { loadFont as loadSerif } from "@remotion/google-fonts/InstrumentSerif";
import { loadFont as loadPixel } from "@remotion/google-fonts/PixelifySans";
import { Easing, interpolate } from "remotion";

// Shiki's own look: Instrument Serif for the big moments, a pixel face for the cat's
// voice, one warm orange on warm paper or deep charcoal. No blue anywhere.
export const serif = loadSerif("normal", { weights: ["400"], subsets: ["latin"] }).fontFamily;
loadSerif("italic", { weights: ["400"], subsets: ["latin"] });
export const sans = loadInter("normal", { weights: ["400", "500", "600"], subsets: ["latin"] }).fontFamily;
export const pixel = loadPixel("normal", { weights: ["400", "600"], subsets: ["latin"] }).fontFamily;

export const c = {
  paper: "#F2F0EC",
  paperCard: "#FFFFFF",
  paperLine: "#E7E3DD",
  ink: "#1C1B19",
  inkMuted: "#6D6862",
  night: "#121110",
  nightPanel: "#1A1918",
  nightCard: "#211F1D",
  nightLine: "#2E2B28",
  hero: "#0A0908",
  text: "#F2EFEA",
  muted: "#A6A098",
  amber: "#F6A945",
  amberEdge: "#D98A2B",
  coral: "#E85A3C",
  sage: "#7FB685",
};

// Motion vocabulary (from the animate skill): ease-out-quint to enter, ease-in to leave
// (about 75% as long), a touch of back-overshoot for the cute pops.
export const easeOut = Easing.bezier(0.23, 1, 0.32, 1);
export const easeIn = Easing.bezier(0.32, 0, 0.67, 0);
export const easeInOut = Easing.bezier(0.645, 0.045, 0.355, 1);
export const backOut = Easing.bezier(0.34, 1.56, 0.64, 1);

/** frame in [a, b] maps to [from, to], clamped, eased. */
export const tween = (
  frame: number,
  a: number,
  b: number,
  from: number,
  to: number,
  easing: (t: number) => number = easeOut,
) => interpolate(frame, [a, b], [from, to], { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing });

export const FPS = 30;
export const BEAT = 18; // 100 BPM at 30 fps: every cut lands on the music
