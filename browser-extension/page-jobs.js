// The page jobs Shiki can ask for (read the page, find an element, get a box ready for typing…).
// Each one is complete on its own: it's sent to the page as text and run there. The extension
// loads this file, and the Shiki app reads the same file for its own browser window, so both
// ways of working in a browser run exactly the same code.

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
