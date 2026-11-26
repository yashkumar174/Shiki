// Every beat of both cuts, in one place. Nudge a beat here; the scenes follow.
//
// Each scene is choreographed once, against the frame count in AUTHORED (the main cut's
// pace). A cut gives each beat a start and an end in seconds; the scene then plays its
// choreography faster or slower to fit, so the reel is the same parts at a higher speed.

export const FPS = 60;

export type BeatId =
  | "opener"
  | "line"
  | "drop"
  | "type"
  | "hub"
  | "pill"
  | "chat"
  | "done"
  | "bento"
  | "pills"
  | "cats"
  | "levels"
  | "phone"
  | "end";

/** Frames each beat was choreographed for (the main cut's lengths). */
export const AUTHORED: Record<BeatId, number> = {
  opener: 360,
  line: 120,
  drop: 96,
  type: 168,
  hub: 216,
  pill: 156,
  chat: 144,
  done: 72,
  bento: 228,
  pills: 120,
  cats: 180,
  levels: 180,
  phone: 144,
  end: 156,
};

type Table = Record<BeatId, [start: number, end: number]>;

/** The main cut: 39 s. Seconds from the start of the cut. */
const MAIN: Table = {
  opener: [0.0, 6.0],
  line: [6.0, 8.0],
  drop: [8.0, 9.6],
  type: [9.6, 12.4],
  hub: [12.4, 16.0],
  pill: [16.0, 18.6],
  chat: [18.6, 21.0],
  done: [21.0, 22.2],
  bento: [22.2, 26.0],
  pills: [26.0, 28.0],
  cats: [28.0, 31.0],
  levels: [31.0, 34.0],
  phone: [34.0, 36.4],
  end: [36.4, 39.0],
};

/** The showreel cut: 15 s, about one beat a second. A beat with no length is left out. */
const REEL: Table = {
  opener: [0.0, 1.9],
  line: [1.9, 2.2],
  drop: [2.2, 3.0],
  type: [3.0, 4.5],
  hub: [4.5, 6.5],
  pill: [6.5, 7.35],
  chat: [7.35, 8.0],
  done: [8.0, 8.4],
  bento: [8.4, 10.0],
  pills: [10.0, 11.0],
  cats: [11.0, 12.5],
  levels: [12.5, 13.5],
  phone: [13.5, 13.5],
  end: [13.5, 15.0],
};

export type CutName = "main" | "reel";

export const CUTS: Record<CutName, { seconds: number; table: Table; musicFrom: number }> = {
  // the music starts where the old way collapses: the calm starts with it
  main: { seconds: 39, table: MAIN, musicFrom: 4.6 },
  reel: { seconds: 15, table: REEL, musicFrom: 1.45 },
};

/** A beat inside its scene: where it starts (frames from the scene's start), how long it runs, and the length it was choreographed for. */
export type Span = { from: number; dur: number; auth: number };

const f = (sec: number) => Math.round(sec * FPS);

/** The frames a scene made of these beats covers, and each beat's span inside it. */
export const sceneOf = <K extends BeatId>(cut: CutName, ids: K[]) => {
  const t = CUTS[cut].table;
  const from = f(t[ids[0]][0]);
  const to = f(t[ids[ids.length - 1]][1]);
  const spans = {} as Record<K, Span>;
  for (const id of ids) {
    spans[id] = { from: f(t[id][0]) - from, dur: f(t[id][1]) - f(t[id][0]), auth: AUTHORED[id] };
  }
  return { from, dur: to - from, spans };
};

/** Choreography time for a beat: 0 at its start, `auth` at its end, running on past both. */
export const uOf = (frame: number, s: Span) => (s.dur > 0 ? ((frame - s.from) * s.auth) / s.dur : s.auth + 1);
