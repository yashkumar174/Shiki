import * as MediaLibrary from 'expo-media-library/legacy';
import { Sendable } from './link';

/** The iPhone's photos and videos, always as the original files (HEIC stays HEIC). */

async function sendable(asset: MediaLibrary.Asset): Promise<Sendable | null> {
  try {
    // Photos kept only in iCloud are downloaded first, at full quality.
    const info = await MediaLibrary.getAssetInfoAsync(asset, { shouldDownloadFromNetwork: true });
    if (!info.localUri) return null;
    return {
      uri: info.localUri,
      name: asset.filename,
      taken: asset.creationTime || asset.modificationTime || Date.now(),
      id: `${asset.id}:${asset.modificationTime}`,
      // A photo's size as the phone has it: the PC checks the file it gets has the same.
      ...(asset.mediaType === 'photo' && info.width > 0 && info.height > 0 ? { w: info.width, h: info.height } : {}),
    };
  } catch {
    return null;
  }
}

const isScreenshot = (a: MediaLibrary.Asset) => (a.mediaSubtypes ?? []).includes('screenshot');

/** The newest camera photos, videos or screenshots, newest first. */
export async function recent(count: number, kind: string, today: boolean): Promise<Sendable[]> {
  const midnight = new Date();
  midnight.setHours(0, 0, 0, 0);
  const mediaType: MediaLibrary.MediaTypeValue[] =
    kind === 'video' ? ['video'] : kind === 'any' ? ['photo', 'video'] : ['photo'];
  const page = await MediaLibrary.getAssetsAsync({
    first: Math.min(400, count * 4 + 20),
    mediaType,
    sortBy: [[MediaLibrary.SortBy.creationTime, false]],
    createdAfter: today ? midnight.getTime() : undefined,
  });
  const picked = page.assets
    .filter((a) => (kind === 'screenshot' ? isScreenshot(a) : !isScreenshot(a)))
    .slice(0, count);
  const items = await Promise.all(picked.map(sendable));
  return items.filter((i): i is Sendable => i !== null);
}

/** Camera photos and videos taken after a moment (ms), oldest first: the ones to send by themselves. */
export async function newSince(since: number): Promise<{ items: Sendable[]; newest: number }> {
  const page = await MediaLibrary.getAssetsAsync({
    first: 50,
    mediaType: ['photo', 'video'],
    sortBy: [[MediaLibrary.SortBy.creationTime, true]],
    createdAfter: since,
  });
  const fresh = page.assets.filter((a) => !isScreenshot(a) && a.creationTime > since);
  const items = (await Promise.all(fresh.map(sendable))).filter((i): i is Sendable => i !== null);
  const newest = fresh.reduce((m, a) => Math.max(m, a.creationTime), since);
  return { items, newest };
}

/** Picked in the photo picker: the original from the library when it has an id. */
export async function fromLibrary(assetId: string): Promise<Sendable | null> {
  try {
    const asset = await MediaLibrary.getAssetInfoAsync(assetId, { shouldDownloadFromNetwork: true });
    return sendable(asset);
  } catch {
    return null;
  }
}

export const isMedia = (name: string) => /\.(jpe?g|png|heic|heif|gif|webp|dng|mov|mp4|m4v|3gp)$/i.test(name);
