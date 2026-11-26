import { Composition, Still } from "remotion";
import { ShikiAd } from "./ShikiAd";
import { Ad } from "./v2/Ad";
import { Mesh } from "./v2/parts";
import { AdV3 } from "./v3/Ad";
import { AdV3Hype, HYPE_FRAMES } from "./v3/Hype";
import { CoverV3 } from "./v3/Cover";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      {/* V3, the night shift: the 39 s main cut and the 15 s showreel cut, from the same scenes */}
      <Composition id="ShikiAdV3" component={AdV3} durationInFrames={2340} fps={60} width={1920} height={1080} defaultProps={{ cut: "main" as const }} />
      {/* the Instagram cover for the fast cut (the grid shows the middle 1080x1350) */}
      <Still id="ShikiCoverV3" component={CoverV3} width={1080} height={1920} />
      {/* V3, the fast cut: 21 s on a 120 BPM grid, every cut on a beat */}
      <Composition id="ShikiAdV3Hype" component={AdV3Hype} durationInFrames={HYPE_FRAMES} fps={60} width={1920} height={1080} />
      <Composition id="ShikiAdV3Reel" component={AdV3} durationInFrames={900} fps={60} width={1920} height={1080} defaultProps={{ cut: "reel" as const }} />
      <Composition id="ShikiAdV2" component={Ad} durationInFrames={2100} fps={60} width={1920} height={1080} />
      <Composition id="ShikiAdVertical" component={Ad} durationInFrames={2100} fps={60} width={1080} height={1920} defaultProps={{ vertical: true }} />
      {/* the cream backdrop put behind Shiki's glass wheel while recording it */}
      <Still id="CreamBackdrop" component={() => <Mesh mode="cream" />} width={1920} height={1080} />
      <Composition id="ShikiAd" component={ShikiAd} durationInFrames={1008} fps={30} width={1920} height={1080} />
    </>
  );
};
