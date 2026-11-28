// Shiki's helper in the browser. It keeps a connection to Shiki on this PC (127.0.0.1 only) and,
// when Shiki is doing a task you asked for, opens or works in a tab for it: reading the page,
// clicking and typing through Chrome's debugger. It does nothing on its own.
//
// Shiki sends plain commands ("read the page", "click element 12"); everything that runs in a page
// is written here, in the extension. Only a fixed list of debugger commands can be used.

const PORT = 47820;
let socket = null;
let wait = 2000;
const attached = new Set();

// The only debugger commands Shiki may send: open a page, real mouse and key input, a picture.
const ALLOWED = new Set([
  "Page.enable",
  "Page.navigate",
  "Page.captureScreenshot",
  "Input.dispatchMouseEvent",
  "Input.dispatchKeyEvent",
  "Input.insertText",
]);

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
    case "cdp": {
      if (!ALLOWED.has(m.method)) throw new Error("Shiki's extension doesn't allow " + m.method);
      if (!attached.has(m.tabId)) throw new Error("Shiki isn't working in that tab.");
      return await chrome.debugger.sendCommand({ tabId: m.tabId }, m.method, m.params || {});
    }
    case "page": {
      // One of the page jobs below, run in the tab Shiki is working in.
      const job = PAGE[m.action];
      if (!job) throw new Error("Unknown page job: " + m.action);
      if (!attached.has(m.tabId)) throw new Error("Shiki isn't working in that tab.");
      const args = JSON.stringify(m.args || []);
      const answer = await chrome.debugger.sendCommand({ tabId: m.tabId }, "Runtime.evaluate", {
        expression: `(${job.toString()})(...${args})`,
        returnByValue: true,
        awaitPromise: true,
        userGesture: true,
      });
      if (answer.exceptionDetails) {
        const ex = answer.exceptionDetails.exception;
        throw new Error((ex && ex.description) || answer.exceptionDetails.text || "The page's script failed.");
      }
      return { value: answer.result ? answer.result.value ?? null : null };
    }
    case "close":
      await chrome.tabs.remove(m.tabId);
      return {};
  }
  throw new Error("Unknown request: " + m.op);
}

// ---- Jobs that run inside the page (each one complete on its own: it's sent to the page as text) ----

const PAGE = {
  ready: function () {
    return document.readyState;
  },

  back: function () {
    history.back();
    return true;
  },

  // The page's plain words.
  text: function () {
    return document.body ? document.body.innerText : "";
  },

  // Numbers everything that can be used (links, buttons, boxes, lists, ticks) with data-shiki, and
  // writes the page as lines: [12] button "Search", "# Heading", and the text between.
  read: function () {
    const MAX = 15000;
    document.querySelectorAll("[data-shiki]").forEach((e) => e.removeAttribute("data-shiki"));
    const clean = (t, max = 90) => { t = (t || "").replace(/\s+/g, " ").trim(); return t.length > max ? t.slice(0, max - 1) + "…" : t; };
    const shown = (el) => {
      const r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) return false;
      const s = getComputedStyle(el);
      return s.visibility !== "hidden" && s.display !== "none" && +s.opacity > 0.05;
    };
    const usable = 'a[href],button,input:not([type=hidden]),textarea,select,summary,[role=button],[role=link],[role=checkbox],[role=radio],[role=tab],[role=menuitem],[role=menuitemcheckbox],[role=option],[role=combobox],[role=switch],[role=textbox],[role=searchbox],[role=slider],[contenteditable=""],[contenteditable=true],[onclick]';
    const name = (el) => {
      let t = el.getAttribute("aria-label");
      const by = el.getAttribute("aria-labelledby");
      if (!t && by) t = by.split(/\s+/).map((id) => (document.getElementById(id) || {}).innerText || "").join(" ");
      if (!t && el.id) { const l = document.querySelector('label[for="' + CSS.escape(el.id) + '"]'); if (l) t = l.innerText; }
      if (!t && ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName) && el.closest("label")) t = el.closest("label").innerText;
      if (!t) t = el.getAttribute("placeholder") || el.getAttribute("alt") || el.getAttribute("title");
      if (!t && !["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName)) t = el.innerText;
      if (!t) { const img = el.querySelector && el.querySelector("img[alt]"); if (img) t = img.alt; }
      if (!t) t = el.getAttribute("name") || (el.tagName === "INPUT" && ["submit", "button"].includes(el.type) ? el.value : "");
      return clean(t);
    };
    const role = (el) => {
      const r = el.getAttribute("role");
      if (r) return r;
      const tag = el.tagName;
      if (tag === "A") return "link";
      if (tag === "BUTTON" || tag === "SUMMARY") return "button";
      if (tag === "SELECT") return "list";
      if (tag === "TEXTAREA") return "textbox";
      if (tag === "INPUT") {
        const t = (el.type || "text").toLowerCase();
        if (["submit", "button", "reset", "image"].includes(t)) return "button";
        if (t === "checkbox" || t === "radio") return t;
        if (t === "password") return "password box";
        return t === "text" ? "textbox" : t + " box";
      }
      if (el.isContentEditable) return "textbox";
      return "clickable";
    };
    const lines = [];
    let size = 0, n = 0, cut = false;
    const add = (line) => { if (size > MAX) { cut = true; return; } lines.push(line); size += line.length + 1; };
    const walk = (node, inside) => {
      if (cut || !node) return;
      if (node.nodeType === Node.TEXT_NODE) {
        if (inside) return;
        const t = clean(node.textContent, 220);
        if (t.length < 2) return;
        const parent = node.parentElement;
        if (!parent || !shown(parent)) return;
        add((/^H[1-3]$/.test(parent.tagName) ? "# " : "") + t);
        return;
      }
      if (node.nodeType !== Node.ELEMENT_NODE) return;
      const el = node;
      const tag = el.tagName;
      if (["SCRIPT", "STYLE", "NOSCRIPT", "SVG", "TEMPLATE", "IFRAME"].includes(tag)) return;
      if (el.getAttribute("aria-hidden") === "true") return;
      let now = inside;
      if (!inside && el.matches(usable) && shown(el)) {
        n++;
        el.setAttribute("data-shiki", String(n));
        let line = "[" + n + "] " + role(el) + ' "' + name(el) + '"';
        if (tag === "INPUT" || tag === "TEXTAREA") {
          if (el.type === "password") line += el.value ? " (filled)" : "";
          else if (el.type === "checkbox" || el.type === "radio") line += el.checked ? " (ticked)" : " (not ticked)";
          else if (el.value) line += ' = "' + clean(el.value, 60) + '"';
        } else if (tag === "SELECT") {
          const o = el.options[el.selectedIndex];
          line += ' = "' + clean(o ? o.text : "", 40) + '" (options: ' + [...el.options].slice(0, 12).map((o) => clean(o.text, 30)).join(" | ") + (el.options.length > 12 ? " | …" : "") + ")";
        } else if (el.getAttribute("aria-checked")) line += el.getAttribute("aria-checked") === "true" ? " (on)" : " (off)";
        else if (el.getAttribute("aria-expanded")) line += el.getAttribute("aria-expanded") === "true" ? " (open)" : " (closed)";
        if (el.disabled || el.getAttribute("aria-disabled") === "true") line += " (disabled)";
        add(line);
        now = true;
      }
      if (el.shadowRoot) for (const c of el.shadowRoot.childNodes) walk(c, now);
      for (const c of el.childNodes) walk(c, now);
    };
    walk(document.body, false);
    const y = Math.round(scrollY), h = Math.max(document.documentElement.scrollHeight - innerHeight, 0);
    const at = h === 0 ? "the whole page fits" : y <= 5 ? "at the top" : y >= h - 5 ? "at the bottom" : Math.round((100 * y) / h) + "% down";
    return { title: document.title, url: location.href, text: lines.join("\n"), cut, at };
  },

  // What element n is, for Shiki's safety check before clicking it.
  describe: function (number) {
    const el = document.querySelector('[data-shiki="' + number + '"]');
    if (!el) return null;
    const t = (el.getAttribute("type") || "").toLowerCase();
    const ac = (el.getAttribute("autocomplete") || "").toLowerCase();
    const words = ((el.name || "") + " " + (el.id || "") + " " + (el.getAttribute("aria-label") || "") + " " + (el.getAttribute("placeholder") || "")).toLowerCase();
    const sensitive = t === "password" || ac.startsWith("cc-") || ac === "one-time-code" || ac.includes("password")
      || /\b(otp|cvv|cvc|card ?number|cardnumber|ccnum|upi ?pin|pin code|passcode|aadhaar|pan number)\b/.test(words);
    const label = (el.getAttribute("aria-label") || el.innerText || el.value || el.getAttribute("title") || el.getAttribute("placeholder") || "").replace(/\s+/g, " ").trim().slice(0, 120);
    const form = el.form || el.closest("form");
    const buttons = form ? [...form.querySelectorAll("button,input[type=submit]")].map((b) => (b.innerText || b.value || "").trim()).join(" | ").slice(0, 200) : "";
    return { kind: (el.getAttribute("type") || el.getAttribute("role") || el.tagName).toLowerCase(), label, sensitive, buttons };
  },

  // Where element n is on screen, after scrolling it into view (for a real mouse click there).
  locate: function (number) {
    const el = document.querySelector('[data-shiki="' + number + '"]');
    if (!el) return null;
    el.scrollIntoView({ block: "center", inline: "center" });
    const r = el.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  },

  // Gets box n ready for typing (focus, and select what's in it to replace it). Never a password, card or code box.
  prepare: function (number, replace) {
    const el = document.querySelector('[data-shiki="' + number + '"]');
    if (!el) return "gone";
    const t = (el.getAttribute("type") || "").toLowerCase();
    const ac = (el.getAttribute("autocomplete") || "").toLowerCase();
    const words = ((el.name || "") + " " + (el.id || "") + " " + (el.getAttribute("aria-label") || "") + " " + (el.getAttribute("placeholder") || "")).toLowerCase();
    if (t === "password" || ac.startsWith("cc-") || ac === "one-time-code" || ac.includes("password")
      || /\b(otp|cvv|cvc|card ?number|cardnumber|ccnum|upi ?pin|pin code|passcode|aadhaar|pan number)\b/.test(words)) return "sensitive";
    el.scrollIntoView({ block: "center" });
    el.focus();
    if (replace) {
      if ("select" in el && typeof el.select === "function") el.select();
      else { const range = document.createRange(); range.selectNodeContents(el); const s = getSelection(); s.removeAllRanges(); s.addRange(range); }
    }
    return "ok";
  },

  // Picks an option in list box n by its text (or value).
  choose: function (number, option) {
    const el = document.querySelector('[data-shiki="' + number + '"]');
    if (!el || el.tagName !== "SELECT") return "not a list";
    const want = String(option).toLowerCase().trim();
    const o = [...el.options].find((o) => o.text.toLowerCase().trim() === want || o.value.toLowerCase() === want)
      || [...el.options].find((o) => o.text.toLowerCase().includes(want));
    if (!o) return "no such option; there are: " + [...el.options].map((o) => o.text.trim()).slice(0, 30).join(" | ");
    el.value = o.value;
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
    return "picked " + o.text.trim();
  },

  // Scrolls the page (or box n) by most of a screen.
  scroll: function (down, number) {
    const el = number ? document.querySelector('[data-shiki="' + number + '"]') : null;
    const by = (el ? el.clientHeight : innerHeight) * 0.8 * (down ? 1 : -1);
    if (el) el.scrollBy(0, by); else scrollBy(0, by);
    return true;
  },

  // The buttons of the form the keyboard is in (so Enter there can be checked like a click).
  formButtons: function () {
    const el = document.activeElement;
    const form = el && (el.form || el.closest("form"));
    return form ? [...form.querySelectorAll("button,input[type=submit]")].map((b) => (b.innerText || b.value || "").trim()).join(" | ").slice(0, 200) : "";
  },
};

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
