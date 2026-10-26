// Renders chosen frames of a composition to out/stills/<id>-f<frame>.png, sharing one bundle (for reviewing motion).
// node scripts/stills.mjs [CompositionId] <frame> <frame> ...
import { bundle } from "@remotion/bundler";
import { renderStill, selectComposition } from "@remotion/renderer";
import path from "node:path";

const args = process.argv.slice(2);
const id = isNaN(Number(args[0])) ? args.shift() : "ShikiAd";
const serveUrl = await bundle({ entryPoint: path.resolve("src/index.ts") });
const composition = await selectComposition({ serveUrl, id });
for (const frame of args.map(Number)) {
  await renderStill({ serveUrl, composition, frame, output: `out/stills/${id}-f${frame}.png`, scale: 0.5 });
  console.log("frame", frame);
}
