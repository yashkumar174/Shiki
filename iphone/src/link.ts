import * as FileSystem from 'expo-file-system/legacy';
import { Pairing, Scanned } from './store';

/**
 * Talking to Shiki on the PC over the home network (plain web requests: Expo Go can't pin a
 * certificate). Every request but pairing carries this phone's secret key. Files go whole, with
 * their MD5 worked out here, which the PC checks along with the size before keeping them.
 */

export class Unpaired extends Error {}

export type Command =
  | { cmd: 'none' }
  | { cmd: 'send-recent'; job: string; count: number; kind: 'photo' | 'video' | 'screenshot' | 'any'; today: boolean }
  | { cmd: 'get'; job: string; files: { token: string; name: string; size: number; sha: string }[] };

const base = (p: { host: string; port: number }) => `http://${p.host}:${p.port}`;
const auth = (p: Pairing) => ({ 'X-Shiki-Phone': p.phone, 'X-Shiki-Key': p.key });

async function withTimeout(url: string, init: RequestInit, ms: number): Promise<Response> {
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), ms);
  try {
    return await fetch(url, { ...init, signal: abort.signal });
  } finally {
    clearTimeout(timer);
  }
}

/** The way to the PC for pairing: from its QR code (a token), or typed in (its address and six digits). */
export type PairTarget = Pick<Scanned, 'host' | 'port'> & { token?: string; pin?: string; pcName?: string };

/** Pairs with the PC; the token (or the six digits) works once. */
export async function pair(target: PairTarget, phoneName: string): Promise<Pairing> {
  const r = await withTimeout(`${base(target)}/v1/pair`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token: target.token ?? '', pin: target.pin ?? '', name: phoneName }),
  }, 15000);
  const json = await r.json();
  if (!json.ok) throw new Error(json.error || 'The PC said no.');
  return {
    pcId: json.pc,
    pcName: json.name || target.pcName || 'your PC',
    host: target.host,
    port: target.port,
    phone: json.phone,
    key: json.key,
    autoSince: Date.now(),
    autoSend: true,
  };
}

/** "Anything for me?": the PC answers at once with a command, or with none after 25 seconds. */
export async function wait(p: Pairing): Promise<Command> {
  const r = await withTimeout(`${base(p)}/v1/wait`, { method: 'POST', headers: auth(p) }, 40000);
  if (r.status === 401) throw new Unpaired();
  return (await r.json()) as Command;
}

export async function report(p: Pairing, job: string, info: { total?: number; done?: boolean; failed?: number; note?: string }) {
  try {
    await withTimeout(`${base(p)}/v1/job`, {
      method: 'POST',
      headers: { ...auth(p), 'Content-Type': 'application/json' },
      body: JSON.stringify({ job, ...info }),
    }, 15000);
  } catch {
    // The PC times the batch out on its own.
  }
}

export type Sendable = { uri: string; name: string; taken: number; id: string };

const query = (values: Record<string, string | number | undefined>) =>
  Object.entries(values)
    .filter(([, v]) => v !== undefined && v !== '')
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    .join('&');

/** Sends one file whole; true once the PC has it, checked. Tries three times. */
export async function upload(p: Pairing, item: Sendable, job?: string): Promise<boolean> {
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      const check = await withTimeout(`${base(p)}/v1/check?${query({ id: item.id, job })}`, { headers: auth(p) }, 15000);
      if (check.status === 401) throw new Unpaired();
      if ((await check.json()).done) return true;
      const status = await sendFile(p, item, job);
      if (status === 401) throw new Unpaired();
      if (status === 200) return true;
      console.log(`[shiki] ${item.name}: the PC answered ${status}`);
    } catch (e) {
      if (e instanceof Unpaired) throw e;
      console.log(`[shiki] ${item.name} (try ${attempt}): ${String((e as Error)?.message ?? e)}`);
      if (attempt === 3) return false;
      await new Promise((r) => setTimeout(r, 1500 * attempt));
    }
  }
  return false;
}

/**
 * One try at sending a file. Files in the app's own folders go straight from disk with their MD5;
 * photos in the library (which Expo's file module isn't allowed to read) are read by React
 * Native's own request instead, and checked by size on the PC.
 */
async function sendFile(p: Pairing, item: Sendable, job?: string): Promise<number> {
  let info: FileSystem.FileInfo | null = null;
  try {
    info = await FileSystem.getInfoAsync(item.uri, { md5: true });
  } catch (e) {
    console.log(`[shiki] ${item.name}: not readable from disk (${String((e as Error)?.message ?? e)}), sending it another way`);
  }
  if (info?.exists) {
    const url = `${base(p)}/v1/put?${query({ id: item.id, name: item.name, size: info.size, taken: item.taken, md5: info.md5, job })}`;
    try {
      // In the foreground: iOS background transfers don't reach a plain-http address on the home network.
      const result = await FileSystem.uploadAsync(url, item.uri, {
        httpMethod: 'PUT',
        uploadType: FileSystem.FileSystemUploadType.BINARY_CONTENT,
        sessionType: FileSystem.FileSystemSessionType.FOREGROUND,
        headers: { ...auth(p), 'Content-Type': 'application/octet-stream' },
      });
      return result.status;
    } catch (e) {
      console.log(`[shiki] ${item.name}: upload from disk failed (${String((e as Error)?.message ?? e)}), sending it another way`);
    }
  }
  const blob = await (await fetch(item.uri)).blob();
  if (!blob.size) throw new Error('the photo came out empty');
  const url = `${base(p)}/v1/put?${query({ id: item.id, name: item.name, size: blob.size, taken: item.taken, job })}`;
  const r = await withTimeout(url, { method: 'PUT', headers: { ...auth(p), 'Content-Type': 'application/octet-stream' }, body: blob }, 15 * 60 * 1000);
  return r.status;
}

/** Fetches a file the PC offered into the app's own folder; its path, or null. */
export async function download(p: Pairing, token: string, name: string): Promise<string | null> {
  const folder = `${FileSystem.documentDirectory}From PC/`;
  await FileSystem.makeDirectoryAsync(folder, { intermediates: true }).catch(() => {});
  const safe = name.replace(/[\/\\:]/g, '_');
  let target = folder + safe;
  if ((await FileSystem.getInfoAsync(target)).exists) {
    const dot = safe.lastIndexOf('.');
    target = folder + (dot > 0 ? `${safe.slice(0, dot)} ${Date.now()}${safe.slice(dot)}` : `${safe} ${Date.now()}`);
  }
  const result = await FileSystem.downloadAsync(`${base(p)}/v1/get?${query({ file: token })}`, target, { headers: auth(p) });
  if (result.status !== 200) {
    await FileSystem.deleteAsync(target, { idempotent: true });
    return null;
  }
  return target;
}

/** Files the PC sent, newest first. */
export async function received(): Promise<{ name: string; uri: string }[]> {
  const folder = `${FileSystem.documentDirectory}From PC/`;
  try {
    const names = await FileSystem.readDirectoryAsync(folder);
    const items = await Promise.all(names.map(async (name) => {
      const info = await FileSystem.getInfoAsync(folder + name);
      return { name, uri: folder + name, time: info.exists ? info.modificationTime ?? 0 : 0 };
    }));
    return items.sort((a, b) => b.time - a.time).map(({ name, uri }) => ({ name, uri }));
  } catch {
    return [];
  }
}
