import { Composition, Still } from "remotion";
import { ShikiAd } from "./ShikiAd";
import { Ad } from "./v2/Ad";
import { Mesh } from "./v2/parts";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition id="ShikiAdV2" component={Ad} durationInFrames={2100} fps={60} width={1920} height={1080} />
      <Composition id="ShikiAdVertical" component={Ad} durationInFrames={2100} fps={60} width={1080} height={1920} defaultProps={{ vertical: true }} />
      {/* the cream backdrop put behind Shiki's glass wheel while recording it */}
      <Still id="CreamBackdrop" component={() => <Mesh mode="cream" />} width={1920} height={1080} />
      <Composition id="ShikiAd" component={ShikiAd} durationInFrames={1008} fps={30} width={1920} height={1080} />
    </>
  );
};
