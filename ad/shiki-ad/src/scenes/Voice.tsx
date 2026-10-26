import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { Caption, Cat, Floater, Glow, level, RevealLine } from "../kit";
import { backOut, c, easeIn, easeOut, sans, tween } from "../theme";
import { voice } from "../voice-data";

// When each voice line starts, in this scene's frames (ShikiAd plays the audio at the same moments).
export const ASK = 14;
export const SENDING = 150;
export const SENT = 230;
export const DONE = 240;

const ask = voice["you-ask"];
const sending = voice["sky-sending"];
const done = voice["sky-done"];

// The words of "Hey Sky, WhatsApp Rahul, I'm on my way" appear as they're heard.
const WORDS = ["Hey Sky,", "WhatsApp", "Rahul,", "I'm on my way."];

const PX = 20;

/** The amber mic circle from Shiki's voice pill, its bars moving with the voice. */
const MicDot: React.FC<{ loud: number }> = ({ loud }) => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        width: 96,
        height: 96,
        borderRadius: "50%",
        background: c.amber,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        gap: 7,
        flexShrink: 0,
        boxShadow: `0 0 ${20 + loud * 50}px rgba(246,169,69,${0.25 + loud * 0.4})`,
      }}
    >
      {[0, 1, 2, 3].map((b) => {
        const idle = 0.28 + 0.1 * Math.sin(frame / 6 + b * 1.7);
        const h = Math.max(idle, loud * (0.55 + 0.45 * Math.abs(Math.sin(frame / 2.3 + b * 2.1))));
        return <div key={b} style={{ width: 8, height: 12 + h * 40, borderRadius: 4, background: c.ink }} />;
      })}
    </div>
  );
};

/** The countdown before a message really goes: say "stop" and it doesn't. */
const Countdown: React.FC = () => {
  const frame = useCurrentFrame();
  const sent = frame >= SENT;
  const p = tween(frame, SENDING + 4, SENT, 0, 1, (t) => t);
  const r = 30;
  const len = 2 * Math.PI * r;
  return (
    <div style={{ width: 80, height: 80, position: "relative", flexShrink: 0, opacity: tween(frame, SENDING, SENDING + 8, 0, 1) }}>
      {!sent ? (
        <svg width={80} height={80} style={{ rotate: "-90deg" }}>
          <circle cx={40} cy={40} r={r} fill="none" stroke={c.nightLine} strokeWidth={7} />
          <circle cx={40} cy={40} r={r} fill="none" stroke={c.amber} strokeWidth={7} strokeLinecap="round" strokeDasharray={len} strokeDashoffset={len * p} />
        </svg>
      ) : (
        <div
          style={{
            width: 80,
            height: 80,
            borderRadius: "50%",
            background: c.sage,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            scale: tween(frame, SENT, SENT + 10, 0.3, 1, backOut),
          }}
        >
          <svg width={40} height={40} viewBox="0 0 24 24">
            <path d="M5 12.5l4.5 4.5L19 7.5" fill="none" stroke={c.hero} strokeWidth={3} strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </div>
      )}
    </div>
  );
};

const Pill: React.FC = () => {
  const frame = useCurrentFrame();
  const heard = WORDS.filter((_, i) => frame >= ASK + ask.runs[i][0]);
  const loud = Math.max(level(ask.env, frame - ASK), level(sending.env, frame - SENDING) * 0.8, level(done.env, frame - DONE) * 0.8);
  const second = frame >= SENDING;
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 30,
        width: 980,
        padding: "26px 36px 26px 28px",
        borderRadius: 80,
        background: "#1E1C1A",
        border: `1.5px solid ${c.nightLine}`,
        boxShadow: "0 40px 90px rgba(0,0,0,0.5)",
        opacity: tween(frame, 8, 18, 0, 1),
        scale: tween(frame, 8, 22, 0.86, 1, backOut),
      }}
    >
      <MicDot loud={loud} />
      <div style={{ flex: 1, fontFamily: sans, minHeight: 96, display: "flex", flexDirection: "column", justifyContent: "center" }}>
        <div style={{ fontSize: 40, fontWeight: 500, color: heard.length ? c.text : c.muted, display: "flex", flexWrap: "wrap", columnGap: 12 }}>
          {heard.length === 0
            ? "Listening…"
            : heard.map((w, i) => {
                const f = frame - ASK - ask.runs[i][0];
                return (
                  <span key={i} style={{ opacity: tween(f, 0, 8, 0, 1), filter: `blur(${tween(f, 0, 8, 6, 0)}px)`, translate: `0 ${tween(f, 0, 10, 8, 0)}px` }}>
                    {w}
                  </span>
                );
              })}
        </div>
        {second && (
          <div
            style={{
              fontSize: 32,
              color: frame >= SENT ? c.sage : c.muted,
              marginTop: 8,
              opacity: tween(frame, SENDING, SENDING + 10, 0, 1),
              translate: `0 ${tween(frame, SENDING, SENDING + 12, 10, 0)}px`,
            }}
          >
            {frame >= SENT ? "Sent to Rahul." : "Sending to Rahul… say stop to cancel."}
          </div>
        )}
      </div>
      {second && <Countdown />}
    </div>
  );
};

/** The message as it'll arrive: Rahul's chat, with Shiki's warm bubble. */
const Message: React.FC = () => {
  const frame = useCurrentFrame();
  const f = frame - SENDING - 6;
  const sent = frame >= SENT;
  return (
    <div
      style={{
        width: 620,
        borderRadius: 36,
        background: c.nightCard,
        border: `1.5px solid ${c.nightLine}`,
        padding: 30,
        fontFamily: sans,
        opacity: tween(f, 0, 12, 0, 1),
        translate: `0 ${tween(f, 0, 18, 40, 0)}px`,
        boxShadow: "0 40px 90px rgba(0,0,0,0.45)",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 18, paddingBottom: 22, borderBottom: `1px solid ${c.nightLine}` }}>
        <div style={{ width: 62, height: 62, borderRadius: "50%", background: c.sage, color: c.hero, fontSize: 30, fontWeight: 600, display: "flex", alignItems: "center", justifyContent: "center" }}>
          R
        </div>
        <div>
          <div style={{ color: c.text, fontSize: 32, fontWeight: 600 }}>Rahul</div>
          <div style={{ color: c.muted, fontSize: 24 }}>WhatsApp</div>
        </div>
      </div>
      <div style={{ display: "flex", justifyContent: "flex-end", paddingTop: 26 }}>
        <div
          style={{
            background: c.amber,
            color: c.ink,
            fontSize: 34,
            fontWeight: 500,
            padding: "16px 22px 12px",
            borderRadius: "26px 26px 8px 26px",
            opacity: sent ? 1 : 0.55,
            scale: sent ? tween(frame, SENT, SENT + 10, 0.94, 1, backOut) : 1,
          }}
        >
          I'm on my way.
          <div style={{ fontSize: 20, textAlign: "right", marginTop: 4, color: "rgba(28,27,25,0.65)" }}>
            2:41 pm {sent ? "✓✓" : "·"}
          </div>
        </div>
      </div>
    </div>
  );
};

const catPose = (f: number) => {
  const talk = (env: readonly number[], at: number) => level(env, f - at) > 0.28;
  if (f >= DONE && f < DONE + done.frames) return talk(done.env, DONE) ? "Talk" : "Happy";
  if (f >= SENT && f < DONE) return "Happy";
  if (f >= SENDING && f < SENDING + sending.frames) return talk(sending.env, SENDING) ? "Talk" : "Idle";
  if (f >= ASK - 4 && f < SENDING) return "Listen";
  return "Idle";
};

export const Voice: React.FC = () => {
  const frame = useCurrentFrame();
  const pop = frame >= SENT ? tween(frame, SENT, SENT + 12, 0.1, 0, backOut) : 0;
  return (
    <AbsoluteFill style={{ background: c.night, overflow: "hidden", scale: tween(frame, 0, 300, 1, 1.04, (t) => t) }}>
      <Glow x="20%" y="80%" size={1100} opacity={0.32} />
      <Glow x="70%" y="30%" size={1200} opacity={0.08} />
      <div style={{ position: "absolute", left: 140, top: 110 }}>
        <RevealLine at={2} size={120} color={c.text} parts={[{ t: "Just" }, { t: "say it.", italic: true, color: c.amber }]} />
      </div>
      {/* the cat, listening, on a soft shadow */}
      <div style={{ position: "absolute", left: 175, top: 905, width: 370, height: 40, borderRadius: "50%", background: "rgba(0,0,0,0.55)", filter: "blur(12px)" }} />
      <div style={{ position: "absolute", left: 160, top: 925 - 20 * PX, opacity: tween(frame, 0, 10, 0, 1) }}>
        <Cat pose={catPose(frame)} px={PX} squash={pop} />
      </div>
      <Floater name="float-Alert" px={9} at={ASK} x={500} y={470} rise={70} />
      <Floater name="float-Sparkle" px={11} at={SENT + 2} x={470} y={520} drift={40} />
      <Floater name="float-Sparkle" px={8} at={SENT + 8} x={230} y={540} drift={-30} />
      <Floater name="float-Heart" px={9} at={SENT + 14} x={360} y={480} drift={10} />

      <div style={{ position: "absolute", left: 760, top: 300 }}>
        <Pill />
      </div>
      <div style={{ position: "absolute", left: 1120, top: 520 }}>
        <Message />
      </div>

      <Caption who="You" text="Hey Sky, WhatsApp Rahul, I'm on my way." from={ASK + 4} to={SENDING - 6} />
      <Caption who="Sky" text="Sending to Rahul. Say stop to cancel." from={SENDING} to={SENDING + sending.frames} />
      <Caption who="Sky" text="Done. Anything else?" from={DONE} to={DONE + done.frames + 6} />
      <ExitDim />
    </AbsoluteFill>
  );
};

// Darken a touch as the next scene slides over.
const ExitDim: React.FC = () => {
  const frame = useCurrentFrame();
  return <AbsoluteFill style={{ background: "#000", opacity: tween(frame, 288, 304, 0, 0.5, easeOut) * tween(frame, 288, 289, 0, 1, easeIn) }} />;
};
