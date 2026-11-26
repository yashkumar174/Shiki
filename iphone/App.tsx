import { useCallback, useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Alert, AppState, KeyboardAvoidingView, Pressable, ScrollView, StyleSheet, Switch, Text, TextInput, View } from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as DocumentPicker from 'expo-document-picker';
import * as ImagePicker from 'expo-image-picker';
import * as MediaLibrary from 'expo-media-library/legacy';
import * as Sharing from 'expo-sharing';
import { activateKeepAwakeAsync, deactivateKeepAwake } from 'expo-keep-awake';
import * as Store from './src/store';
import * as Link from './src/link';
import * as Media from './src/media';

/**
 * Shiki on the iPhone: scan the code on the PC once, then send photos, videos and files to it at
 * full quality, and let the PC ask for them ("get my last photo") while this is open.
 */
export default function App() {
  const [pairing, setPairing] = useState<Store.Pairing | null | undefined>(undefined);
  const [status, setStatus] = useState('');
  const [connected, setConnected] = useState(false);
  const [scanning, setScanning] = useState(false);
  // What the camera last read that wasn't Shiki's code (so you can tell it's seeing something).
  const [seen, setSeen] = useState('');
  // Typing the PC's address and six digits, when the camera can't scan.
  const [typing, setTyping] = useState(false);
  const [address, setAddress] = useState('');
  const [pin, setPin] = useState('');
  const [busy, setBusy] = useState(0);
  const [events, setEvents] = useState<string[]>([]);
  const [fromPc, setFromPc] = useState<{ name: string; uri: string }[]>([]);
  const [camera, askCamera] = useCameraPermissions();

  const pairingRef = useRef<Store.Pairing | null>(null);
  const queue = useRef<Promise<void>>(Promise.resolve());
  const scanned = useRef(false);
  const lastRead = useRef('');
  const loopGeneration = useRef(0);

  const note = useCallback((line: string) => setEvents((e) => [line, ...e].slice(0, 8)), []);
  const remember = useCallback(async (p: Store.Pairing | null) => {
    pairingRef.current = p;
    setPairing(p);
    if (p) await Store.save(p);
    else await Store.forget();
  }, []);

  useEffect(() => {
    Store.load().then((p) => { pairingRef.current = p; setPairing(p); });
    Link.received().then(setFromPc);
  }, []);

  useEffect(() => {
    if (busy > 0) activateKeepAwakeAsync('shiki').catch(() => {});
    else deactivateKeepAwake('shiki').catch(() => {});
  }, [busy]);

  // ---- The work, one batch at a time ----

  const later = useCallback((task: () => Promise<void>) => {
    queue.current = queue.current.then(async () => {
      setBusy((b) => b + 1);
      try { await task(); } catch (e) {
        if (e instanceof Link.Unpaired) { await remember(null); setStatus('Removed on the PC. Scan its code again to connect.'); }
        else note(`Couldn't finish: ${String((e as Error)?.message ?? e)}`);
      } finally { setBusy((b) => b - 1); }
    });
  }, [note, remember]);

  const sendBatch = useCallback(async (job: string, items: Link.Sendable[], label: string) => {
    const p = pairingRef.current;
    if (!p) return;
    await Link.report(p, job, { total: items.length });
    let failed = 0;
    for (let i = 0; i < items.length; i++) {
      setStatus(`${label} ${items.length === 1 ? items[i].name : `${i + 1} of ${items.length}`} to ${p.pcName}…`);
      if (!(await Link.upload(p, items[i], job))) failed++;
    }
    await Link.report(p, job, { done: true, failed, note: failed ? `${failed} didn't go through` : undefined });
    const sent = items.length - failed;
    note(failed ? `Sent ${sent} of ${items.length} to ${p.pcName}` : `Sent ${items.length === 1 ? items[0].name : `${items.length} files`} to ${p.pcName}, checked`);
    setStatus(`Connected to ${p.pcName}`);
  }, [note]);

  const handle = useCallback(async (command: Link.Command) => {
    const p = pairingRef.current;
    if (!p) return;
    if (command.cmd === 'send-recent') {
      const allowed = (await MediaLibrary.requestPermissionsAsync()).granted;
      const items = allowed ? await Media.recent(command.count, command.kind, command.today) : [];
      if (items.length === 0) {
        await Link.report(p, command.job, {
          total: 0, done: true,
          note: allowed ? `There's nothing like that on the iPhone${command.today ? ' from today' : ''}.` : 'Shiki on the iPhone isn\'t allowed to see your photos: open it and allow Photos.',
        });
        return;
      }
      await sendBatch(command.job, items, 'Sending');
    } else if (command.cmd === 'get') {
      await Link.report(p, command.job, { total: command.files.length });
      let failed = 0;
      for (const file of command.files) {
        setStatus(`Getting ${file.name} from ${p.pcName}…`);
        const path = await Link.download(p, file.token, file.name, file.md5);
        if (!path) { failed++; continue; }
        // Photos and videos go into Photos; anything else stays here, to share or save to Files.
        if (Media.isMedia(file.name) && (await MediaLibrary.requestPermissionsAsync(true)).granted) {
          try { await MediaLibrary.saveToLibraryAsync(path); } catch { /* kept in the app */ }
        }
        note(`Got ${file.name} from ${p.pcName}`);
      }
      await Link.report(p, command.job, { done: true, failed, note: failed ? 'the iPhone couldn\'t save them' : undefined });
      setFromPc(await Link.received());
      setStatus(`Connected to ${p.pcName}`);
    }
  }, [note, sendBatch]);

  // ---- Asking the PC "anything for me?" while the app is open ----

  useEffect(() => {
    if (!pairing) return;
    let alive = true;
    const generation = ++loopGeneration.current;
    let lastAuto = 0;
    const run = async () => {
      let backoff = 2000;
      let lastSearch = 0;
      let checked = false;
      while (alive && generation === loopGeneration.current) {
        if (AppState.currentState !== 'active') { await sleep(1000); checked = false; continue; }
        let p = pairingRef.current;
        if (!p) return;
        // Coming back (opening the app, or after losing the PC): a quick hello first, so "Connected"
        // shows at once; and if the PC isn't at its old address, find it at its new one.
        if (!checked) {
          if (await Link.hello(p)) {
            checked = true;
            setConnected(true);
            setStatus(`Connected to ${p.pcName}`);
          } else if (Date.now() - lastSearch > 20000) {
            lastSearch = Date.now();
            setStatus(`Looking for ${p.pcName}…`);
            const found = await Link.findPc(p);
            if (found && generation === loopGeneration.current) {
              p = { ...p, host: found };
              await remember(p);
              console.log(`[shiki] found ${p.pcName} at its new address ${found}`);
              checked = true;
              setConnected(true);
              setStatus(`Connected to ${p.pcName}`);
            }
          }
        }
        try {
          const command = await Link.wait(p);
          setConnected(true);
          setStatus((s) => (s.startsWith('Looking') || s === '' ? `Connected to ${p.pcName}` : s));
          if (command.cmd !== 'none') later(() => handle(command));
          backoff = 2000;
          // New camera photos and videos, about once a minute.
          if (p.autoSend && Date.now() - lastAuto > 60000) {
            lastAuto = Date.now();
            const allowed = (await MediaLibrary.getPermissionsAsync()).granted;
            if (allowed) {
              const { items, newest } = await Media.newSince(p.autoSince);
              if (items.length > 0) {
                const next = { ...p, autoSince: newest };
                await remember(next);
                later(() => sendBatch(`auto-${Date.now().toString(36)}`, items, 'Sending new'));
              }
            }
          }
        } catch (e) {
          if (e instanceof Link.Unpaired) { await remember(null); setStatus('Removed on the PC. Scan its code again to connect.'); return; }
          setConnected(false);
          checked = false;
          setStatus(`Looking for ${p.pcName}… (same Wi-Fi, or this iPhone's hotspot)`);
          await sleep(backoff);
          backoff = Math.min(backoff * 2, 15000);
        }
      }
    };
    run();
    return () => { alive = false; };
  }, [pairing?.phone, handle, later, remember, sendBatch]);

  // ---- Pairing ----

  const onScan = useCallback(async ({ data }: { data: string }) => {
    if (scanned.current) return;
    // The camera reports the same code many times a second: note it once.
    if (lastRead.current !== data) { lastRead.current = data; console.log('[shiki] camera read:', data.slice(0, 120)); }
    const code = Store.parseQr(data);
    if (!code) {
      setSeen(data.length > 60 ? data.slice(0, 60) + '…' : data);
      return;
    }
    scanned.current = true;
    setScanning(false);
    const current = pairingRef.current;
    if (current && current.pcId === code.pcId) {
      // The same PC at a new address.
      await remember({ ...current, host: code.host, port: code.port });
      setStatus(`Connected to ${current.pcName}`);
      return;
    }
    setStatus(`Connecting to ${code.pcName}…`);
    try {
      const p = await Link.pair(code, 'iPhone');
      await remember(p);
      setStatus(`Connected to ${p.pcName}`);
      await MediaLibrary.requestPermissionsAsync();
    } catch (e) {
      setStatus(`Couldn't connect: ${String((e as Error)?.message ?? e)}. Is this iPhone on the same Wi-Fi as the PC?`);
    }
  }, [remember]);

  /** Pairing with the address and six digits the PC shows under its code. */
  const pairByHand = useCallback(async () => {
    const host = address.trim().replace(/^https?:\/\//, '').split(/[/:]/)[0];
    const digits = pin.replace(/\D/g, '');
    if (!/^\d{1,3}(\.\d{1,3}){3}$/.test(host)) { setStatus('Type the address the PC shows, like 192.168.1.5.'); return; }
    if (digits.length !== 6) { setStatus('The code is six digits.'); return; }
    setStatus('Connecting…');
    try {
      const p = await Link.pair({ host, port: 47814, pin: digits }, 'iPhone');
      await remember(p);
      setTyping(false);
      setPin('');
      setStatus(`Connected to ${p.pcName}`);
      await MediaLibrary.requestPermissionsAsync();
    } catch (e) {
      setStatus(`Couldn't connect: ${String((e as Error)?.message ?? e)}`);
    }
  }, [address, pin, remember]);

  const startScan = useCallback(async () => {
    if (!camera?.granted && !(await askCamera()).granted) {
      Alert.alert('Camera needed', 'Shiki uses the camera only to scan the code on your PC.');
      return;
    }
    scanned.current = false;
    setSeen('');
    setScanning(true);
  }, [camera, askCamera]);

  // ---- Sending by hand ----

  const pickPhotos = useCallback(async () => {
    await MediaLibrary.requestPermissionsAsync();
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ['images', 'videos'],
      allowsMultipleSelection: true,
      selectionLimit: 0,
      quality: 1,
      // As they are: HEIC stays HEIC, videos aren't re-encoded.
      preferredAssetRepresentationMode: ImagePicker.UIImagePickerPreferredAssetRepresentationMode.Current,
      videoExportPreset: ImagePicker.VideoExportPreset.Passthrough,
    });
    if (result.canceled) return;
    const items = (await Promise.all(result.assets.map(async (a) =>
      (a.assetId ? await Media.fromLibrary(a.assetId) : null) ?? {
        uri: a.uri, name: a.fileName ?? 'photo', taken: Date.now(), id: `pick:${a.fileName}:${a.fileSize}`,
        ...(a.type === 'image' && a.width > 0 && a.height > 0 ? { w: a.width, h: a.height } : {}),
      }))) as Link.Sendable[];
    later(() => sendBatch(`share-${Date.now().toString(36)}`, items, 'Sending'));
  }, [later, sendBatch]);

  const pickFiles = useCallback(async () => {
    const result = await DocumentPicker.getDocumentAsync({ multiple: true, copyToCacheDirectory: true });
    if (result.canceled) return;
    const items = result.assets.map((a) => ({ uri: a.uri, name: a.name, taken: Date.now(), id: `doc:${a.name}:${a.size}` }));
    later(() => sendBatch(`share-${Date.now().toString(36)}`, items, 'Sending'));
  }, [later, sendBatch]);

  const disconnect = useCallback(() => {
    Alert.alert('Disconnect?', `This iPhone stops sending to ${pairing?.pcName}. Scan the code on the PC to connect again.`, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Disconnect', style: 'destructive', onPress: () => { remember(null); setConnected(false); setStatus(''); } },
    ]);
  }, [pairing, remember]);

  // ---- Screens ----

  if (pairing === undefined) return <View style={styles.center}><ActivityIndicator /></View>;

  if (scanning) {
    return (
      <View style={styles.scanner}>
        <CameraView
          style={StyleSheet.absoluteFill}
          facing="back"
          barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
          onBarcodeScanned={onScan}
          onCameraReady={() => console.log('[shiki] camera ready')}
          onMountError={(e) => { console.log('[shiki] camera error:', e.message); setSeen(`The camera couldn't start: ${e.message}`); }}
        />
        <View style={styles.scanHint}>
          <Text style={styles.scanText}>Point at the code in Shiki on your PC</Text>
          <Text style={styles.scanSub}>Settings → Accounts → Your phone → Connect a phone → iPhone</Text>
        </View>
        <View style={styles.scanFoot}>
          {seen ? <Text style={styles.scanSeen}>Seen: {seen}{seen.startsWith('The camera') ? '' : ' (not Shiki\'s code)'}</Text> : null}
          <Pressable style={[styles.button, styles.primary]} onPress={() => { setScanning(false); setTyping(true); setStatus(''); }}>
            <Text style={[styles.buttonText, styles.primaryText]}>Type the code instead</Text>
          </Pressable>
          <Pressable style={styles.button} onPress={() => setScanning(false)}>
            <Text style={styles.buttonText}>Cancel</Text>
          </Pressable>
        </View>
        <StatusBar style="light" />
      </View>
    );
  }

  if (typing) {
    return (
      <KeyboardAvoidingView style={styles.page} behavior="padding">
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.title}>Type the code</Text>
          <Text style={styles.body}>On the PC, under the QR code in Connect a phone (iPhone), there's an address and six digits. Type them here.</Text>
          <Text style={styles.label}>The PC's address</Text>
          <TextInput style={styles.input} value={address} onChangeText={setAddress} placeholder="192.168.1.5" placeholderTextColor="#9AA3AF"
            keyboardType="numbers-and-punctuation" autoCapitalize="none" autoCorrect={false} />
          <Text style={styles.label}>The six digits</Text>
          <TextInput style={[styles.input, styles.pin]} value={pin} onChangeText={setPin} placeholder="123 456" placeholderTextColor="#9AA3AF"
            keyboardType="number-pad" maxLength={7} />
          <Pressable style={[styles.button, styles.primary]} onPress={pairByHand}>
            <Text style={[styles.buttonText, styles.primaryText]}>Connect</Text>
          </Pressable>
          <Pressable style={styles.button} onPress={() => { setTyping(false); setStatus(''); }}>
            <Text style={styles.buttonText}>Back</Text>
          </Pressable>
          {status ? <Text style={styles.status}>{status}</Text> : null}
        </ScrollView>
      </KeyboardAvoidingView>
    );
  }

  return (
    <ScrollView style={styles.page} contentContainerStyle={styles.content}>
      <Text style={styles.title}>Shiki</Text>
      {!pairing ? (
        <>
          <Text style={styles.body}>Sends your photos, videos and files to your PC at full quality, over your Wi-Fi. On the PC, open Connect a phone (Settings → Phone), choose iPhone, then scan its code.</Text>
          <Pressable style={[styles.button, styles.primary]} onPress={startScan}>
            <Text style={[styles.buttonText, styles.primaryText]}>Scan the code on your PC</Text>
          </Pressable>
          <Pressable style={styles.button} onPress={() => { setTyping(true); setStatus(''); }}>
            <Text style={styles.buttonText}>Type the code instead</Text>
          </Pressable>
          {status ? <Text style={styles.status}>{status}</Text> : null}
        </>
      ) : (
        <>
          <View style={styles.statusRow}>
            <View style={[styles.dot, { backgroundColor: connected ? '#3FA34D' : '#C9C3BA' }]} />
            <Text style={styles.statusText}>{status || `Connecting to ${pairing.pcName}…`}</Text>
            {busy > 0 ? <ActivityIndicator style={{ marginLeft: 8 }} /> : null}
          </View>
          <Pressable style={[styles.button, styles.primary]} onPress={pickPhotos}>
            <Text style={[styles.buttonText, styles.primaryText]}>Send photos and videos</Text>
          </Pressable>
          <Pressable style={styles.button} onPress={pickFiles}>
            <Text style={styles.buttonText}>Send files</Text>
          </Pressable>
          <Text style={styles.hint}>On the PC you can also just ask: "get my last 3 photos from my phone". It works while Shiki is open here.</Text>

          <View style={styles.switchRow}>
            <Text style={styles.switchText}>Send new photos and videos by themselves while Shiki is open</Text>
            <Switch value={pairing.autoSend} onValueChange={(on) => remember({ ...pairing, autoSend: on, autoSince: on ? Date.now() : pairing.autoSince })} />
          </View>

          {events.length > 0 ? (
            <View style={styles.section}>
              {events.map((e, i) => <Text key={i} style={styles.event}>{e}</Text>)}
            </View>
          ) : null}

          {fromPc.length > 0 ? (
            <View style={styles.section}>
              <Text style={styles.heading}>From your PC</Text>
              {fromPc.slice(0, 12).map((f) => (
                <Pressable key={f.uri} style={styles.fileRow} onPress={() => Sharing.shareAsync(f.uri)}>
                  <Text style={styles.fileName} numberOfLines={1}>{f.name}</Text>
                  <Text style={styles.share}>Share</Text>
                </Pressable>
              ))}
            </View>
          ) : null}

          <Pressable style={[styles.button, styles.quiet]} onPress={startScan}>
            <Text style={styles.buttonText}>Scan the PC's code again</Text>
          </Pressable>
          <Text style={styles.hint}>If the PC's address changes (a new Wi-Fi), scanning its code again fixes it without connecting again.</Text>
          <Pressable style={[styles.button, styles.quiet]} onPress={disconnect}>
            <Text style={styles.buttonText}>Disconnect from {pairing.pcName}</Text>
          </Pressable>
        </>
      )}
      <StatusBar style="dark" />
    </ScrollView>
  );
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

const ink = '#1E1B18';
const muted = '#6B645C';
const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
  page: { flex: 1, backgroundColor: '#fff' },
  content: { paddingHorizontal: 24, paddingTop: 72, paddingBottom: 48 },
  title: { fontSize: 34, fontWeight: '700', color: ink, marginBottom: 12 },
  body: { fontSize: 16, lineHeight: 23, color: muted, marginBottom: 20 },
  hint: { fontSize: 13.5, lineHeight: 19, color: muted, marginTop: 8 },
  status: { fontSize: 15, color: ink, marginTop: 16, lineHeight: 21 },
  statusRow: { flexDirection: 'row', alignItems: 'center', marginBottom: 18 },
  dot: { width: 10, height: 10, borderRadius: 5, marginRight: 10 },
  statusText: { fontSize: 16, color: ink, flexShrink: 1 },
  button: { borderRadius: 14, paddingVertical: 15, paddingHorizontal: 18, backgroundColor: '#F2EFEB', marginTop: 10 },
  primary: { backgroundColor: ink },
  quiet: { backgroundColor: 'transparent', borderWidth: 1, borderColor: '#E2DDD6', marginTop: 22 },
  buttonText: { fontSize: 16.5, fontWeight: '600', color: ink, textAlign: 'center' },
  primaryText: { color: '#fff' },
  switchRow: { flexDirection: 'row', alignItems: 'center', marginTop: 24, gap: 12 },
  switchText: { flex: 1, fontSize: 15, color: ink, lineHeight: 21 },
  section: { marginTop: 26 },
  heading: { fontSize: 14, fontWeight: '600', color: muted, marginBottom: 6 },
  event: { fontSize: 14, color: ink, paddingVertical: 5 },
  fileRow: { flexDirection: 'row', alignItems: 'center', paddingVertical: 10, borderBottomWidth: StyleSheet.hairlineWidth, borderColor: '#E2DDD6' },
  fileName: { flex: 1, fontSize: 15, color: ink },
  share: { fontSize: 15, color: '#2D6CDF', marginLeft: 12 },
  scanner: { flex: 1, backgroundColor: '#000' },
  scanHint: { position: 'absolute', top: 80, left: 24, right: 24, alignItems: 'center' },
  scanText: { color: '#fff', fontSize: 19, fontWeight: '600', textAlign: 'center' },
  scanSub: { color: '#ddd', fontSize: 14, marginTop: 6, textAlign: 'center' },
  scanFoot: { position: 'absolute', bottom: 48, left: 24, right: 24 },
  scanSeen: { color: '#fff', fontSize: 13, textAlign: 'center', marginBottom: 6, backgroundColor: 'rgba(0,0,0,0.55)', padding: 8, borderRadius: 10, overflow: 'hidden' },
  label: { fontSize: 14, fontWeight: '600', color: ink, marginTop: 16, marginBottom: 6 },
  input: { borderWidth: 1.5, borderColor: '#D9D3CA', borderRadius: 12, paddingHorizontal: 14, paddingVertical: 12, fontSize: 18, color: ink },
  pin: { fontSize: 24, letterSpacing: 4 },
});
