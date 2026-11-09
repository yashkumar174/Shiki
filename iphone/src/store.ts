import * as SecureStore from 'expo-secure-store';

/** What the iPhone remembers about its PC, in the Keychain. */
export type Pairing = {
  pcId: string;
  pcName: string;
  host: string;
  port: number;
  phone: string;
  key: string;
  /** New photos taken after this (ms) haven't been sent by themselves yet. */
  autoSince: number;
  autoSend: boolean;
};

const KEY = 'shiki.pairing';

export async function load(): Promise<Pairing | null> {
  try {
    const text = await SecureStore.getItemAsync(KEY);
    return text ? (JSON.parse(text) as Pairing) : null;
  } catch {
    return null;
  }
}

export async function save(pairing: Pairing): Promise<void> {
  await SecureStore.setItemAsync(KEY, JSON.stringify(pairing));
}

export async function forget(): Promise<void> {
  await SecureStore.deleteItemAsync(KEY);
}

/** The QR code on the PC: shiki://pair?h=<address>&p=<port>&t=<one-time token>&id=<pc id>&n=<pc name> */
export type Scanned = { host: string; port: number; token: string; pcId: string; pcName: string };

export function parseQr(data: string): Scanned | null {
  const match = /^shiki:\/\/pair\?(.*)$/.exec(data.trim());
  if (!match) return null;
  const values: Record<string, string> = {};
  for (const part of match[1].split('&')) {
    const [k, v = ''] = part.split('=');
    values[decodeURIComponent(k)] = decodeURIComponent(v.replace(/\+/g, ' '));
  }
  const port = Number(values.p);
  if (!values.h || !port || !values.t || !values.id) return null;
  return { host: values.h, port, token: values.t, pcId: values.id, pcName: values.n || 'your PC' };
}
