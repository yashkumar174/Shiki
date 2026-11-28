// Shiki's helper in the browser. It keeps a connection to Shiki on this PC (127.0.0.1 only) and,
// when Shiki is doing a task you asked for, opens or works in a tab for it: reading the page,
// clicking and typing through Chrome's debugger. It does nothing on its own.

const PORT = 47820;
let socket = null;
let wait = 2000;
const attached = new Set();

function connect() {
  if (socket) return;
  let ws;
  try {
    ws = new WebSocket(`ws://127.0.0.1:${PORT}/shiki`);
  } catch (e) {
    later();
    return;
  }
  socket = ws;
  ws.onopen = () => {
    wait = 2000;
    send({ hello: "shiki-extension", version: chrome.runtime.getManifest().version, agent: navigator.userAgent });
  };
  ws.onmessage = async (event) => {
    let message;
    try { message = JSON.parse(event.data); } catch (e) { return; }
    if (message.ping) { send({ pong: message.ping }); return; }
    try {
      const result = await handle(message);
      send({ id: message.id, result: result ?? {} });
    } catch (e) {
      send({ id: message.id, error: String((e && e.message) || e) });
    }
  };
  ws.onclose = () => {
    if (socket === ws) socket = null;
    // Shiki isn't running (or restarted): try again, less often the longer it's away.
    later();
  };
  ws.onerror = () => {};
}

function later() {
  setTimeout(connect, wait);
  wait = Math.min(wait * 2, 30000);
}

function send(message) {
  if (socket && socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(message));
}

async function handle(m) {
  switch (m.op) {
    case "tabs": {
      const tabs = await chrome.tabs.query({});
      return tabs.map((t) => ({ id: t.id, url: t.url || t.pendingUrl || "", title: t.title || "", active: t.active, windowId: t.windowId }));
    }
    case "open": {
      const tab = await chrome.tabs.create({ url: m.url || "about:blank", active: true });
      await chrome.windows.update(tab.windowId, { focused: true });
      return { id: tab.id };
    }
    case "activate": {
      const tab = await chrome.tabs.update(m.tabId, { active: true });
      await chrome.windows.update(tab.windowId, { focused: true });
      return {};
    }
    case "attach": {
      if (!attached.has(m.tabId)) {
        await chrome.debugger.attach({ tabId: m.tabId }, "1.3");
        attached.add(m.tabId);
      }
      return {};
    }
    case "detach": {
      if (attached.has(m.tabId)) {
        attached.delete(m.tabId);
        try { await chrome.debugger.detach({ tabId: m.tabId }); } catch (e) {}
      }
      return {};
    }
    case "cdp":
      return await chrome.debugger.sendCommand({ tabId: m.tabId }, m.method, m.params || {});
    case "close":
      await chrome.tabs.remove(m.tabId);
      return {};
  }
  throw new Error("Unknown request: " + m.op);
}

chrome.debugger.onDetach.addListener((source, reason) => {
  attached.delete(source.tabId);
  send({ event: "detached", tabId: source.tabId, reason });
});

chrome.tabs.onCreated.addListener((tab) => {
  send({ event: "tabCreated", tabId: tab.id, openerTabId: tab.openerTabId ?? null, url: tab.pendingUrl || tab.url || "" });
});

chrome.tabs.onRemoved.addListener((tabId) => {
  attached.delete(tabId);
  send({ event: "tabClosed", tabId });
});

// The worker sleeps when idle: an alarm wakes it to reconnect.
chrome.alarms.create("shiki-keepalive", { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(() => connect());
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
chrome.action.onClicked.addListener(() => connect());
connect();
