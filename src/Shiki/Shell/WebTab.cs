using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Shiki.Shell;

/// <summary>
/// One tab in the person's browser that Shiki works in, through its extension: open a page, read
/// what's on it as a numbered list (links, buttons, boxes and the text around them), click, type,
/// pick, press keys, scroll, look. Clicks and typing are real browser input, so sites behave as
/// if the person did it, but the person's own mouse and keyboard stay free.
/// </summary>
public sealed class WebTab
{
    readonly BrowserBridge _bridge;
    public int Id { get; private set; }

    WebTab(BrowserBridge bridge, int id)
    {
        _bridge = bridge;
        Id = id;
    }

    /// <summary>A new tab (in front) at this address, ready to work in.</summary>
    public static async Task<WebTab> Open(BrowserBridge bridge, string url, CancellationToken ct)
    {
        var opened = await bridge.Call("open", new JsonObject { ["url"] = url }, ct);
        var tab = new WebTab(bridge, opened.GetProperty("id").GetInt32());
        await tab.Attach(ct);
        await tab.Settle(ct);
        return tab;
    }

    /// <summary>Works in another tab from now on (one the page opened).</summary>
    public async Task SwitchTo(int tabId, CancellationToken ct)
    {
        Id = tabId;
        await _bridge.Call("activate", new JsonObject { ["tabId"] = tabId }, ct);
        await Attach(ct);
        await Settle(ct);
    }

    async Task Attach(CancellationToken ct)
    {
        await _bridge.Call("attach", new JsonObject { ["tabId"] = Id }, ct);
        // Downloads go to the Downloads folder without asking where.
        try { await _bridge.Cdp(Id, "Page.enable", null, ct); } catch (Exception) { }
    }

    /// <summary>Lets go of the tab (the "Shiki is working in this browser" bar goes away).</summary>
    public async Task Release()
    {
        try { await _bridge.Call("detach", new JsonObject { ["tabId"] = Id }); } catch (Exception) { }
    }

    public async Task<IReadOnlyList<(int Id, string Url, string Title)>> Tabs(CancellationToken ct)
    {
        var list = await _bridge.Call("tabs", null, ct);
        var tabs = new List<(int, string, string)>();
        foreach (var t in list.EnumerateArray()) tabs.Add((t.GetProperty("id").GetInt32(), t.GetProperty("url").GetString() ?? "", t.GetProperty("title").GetString() ?? ""));
        return tabs;
    }

    public async Task GoTo(string url, CancellationToken ct)
    {
        await _bridge.Cdp(Id, "Page.navigate", new JsonObject { ["url"] = url }, ct);
        await Task.Delay(400, ct);
        await Settle(ct);
    }

    public async Task Back(CancellationToken ct)
    {
        await Eval("history.back()", ct);
        await Task.Delay(500, ct);
        await Settle(ct);
    }

    /// <summary>Waits for the page to finish loading (at most 20 seconds), then a moment for it to draw.</summary>
    public async Task Settle(CancellationToken ct)
    {
        for (int i = 0; i < 40; i++)
        {
            try
            {
                var state = await Eval("document.readyState", ct, seconds: 5);
                if (state.ValueKind == JsonValueKind.String && state.GetString() == "complete") break;
            }
            catch (BrowserException) { }
            await Task.Delay(500, ct);
        }
        await Task.Delay(500, ct);
    }

    /// <summary>Runs a bit of JavaScript in the page; its result.</summary>
    public async Task<JsonElement> Eval(string expression, CancellationToken ct, int seconds = 20)
    {
        var answer = await _bridge.Cdp(Id, "Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = true,
            ["userGesture"] = true,
        }, ct, seconds);
        if (answer.TryGetProperty("exceptionDetails", out var problem))
        {
            var text = problem.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var d) ? d.GetString() : problem.GetProperty("text").GetString();
            throw new BrowserException(text ?? "The page's script failed.");
        }
        return answer.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    /// <summary>The page as Shiki reads it: title, address, and a numbered list of what can be used, with the text around it.</summary>
    public async Task<string> Read(CancellationToken ct)
    {
        var page = await Eval(Snapshot, ct);
        if (page.ValueKind != JsonValueKind.Object) return "(The page couldn't be read.)";
        var title = page.GetProperty("title").GetString();
        var url = page.GetProperty("url").GetString();
        var body = page.GetProperty("text").GetString();
        var cut = page.GetProperty("cut").GetBoolean();
        var at = page.GetProperty("at").GetString();
        return $"Page: {title}\nAddress: {url}\nScrolled: {at}\n\n{body}{(cut ? "\n(…the page goes on: read_text or scroll for more.)" : "")}";
    }

    /// <summary>What an element is (for the safety check before clicking it).</summary>
    public async Task<(string Kind, string Label, bool Sensitive, string FormButtons)?> Describe(int number, CancellationToken ct)
    {
        var info = await Eval($$"""
            (() => {
              const el = document.querySelector('[data-shiki="{{number}}"]');
              if (!el) return null;
              const label = (el.getAttribute('aria-label') || el.innerText || el.value || el.getAttribute('title') || el.getAttribute('placeholder') || '').replace(/\s+/g, ' ').trim().slice(0, 120);
              const form = el.form || el.closest('form');
              const buttons = form ? [...form.querySelectorAll('button,input[type=submit]')].map(b => (b.innerText || b.value || '').trim()).join(' | ').slice(0, 200) : '';
              return { kind: (el.getAttribute('type') || el.getAttribute('role') || el.tagName).toLowerCase(), label, sensitive: {{SensitiveCheck}}, buttons };
            })()
            """, ct);
        if (info.ValueKind != JsonValueKind.Object) return null;
        return (info.GetProperty("kind").GetString() ?? "", info.GetProperty("label").GetString() ?? "", info.GetProperty("sensitive").GetBoolean(), info.GetProperty("buttons").GetString() ?? "");
    }

    /// <summary>Clicks element number n (scrolled into view first) with a real mouse click.</summary>
    public async Task Click(int number, CancellationToken ct)
    {
        var at = await Eval($$"""
            (() => {
              const el = document.querySelector('[data-shiki="{{number}}"]');
              if (!el) return null;
              el.scrollIntoView({ block: 'center', inline: 'center' });
              const r = el.getBoundingClientRect();
              return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
            })()
            """, ct);
        if (at.ValueKind != JsonValueKind.Object) throw new BrowserException($"There's no element {number} any more: read the page again.");
        double x = at.GetProperty("x").GetDouble(), y = at.GetProperty("y").GetDouble();
        await Task.Delay(120, ct);
        await Mouse("mouseMoved", x, y, ct);
        await Mouse("mousePressed", x, y, ct);
        await Mouse("mouseReleased", x, y, ct);
        await Task.Delay(700, ct);
        await Settle(ct);
    }

    Task Mouse(string type, double x, double y, CancellationToken ct) =>
        _bridge.Cdp(Id, "Input.dispatchMouseEvent", new JsonObject
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = y,
            ["button"] = type == "mouseMoved" ? "none" : "left",
            ["clickCount"] = type == "mouseMoved" ? 0 : 1,
        }, ct);

    /// <summary>Types into element n (replacing what's in it unless told to add). Refuses password, card and code boxes.</summary>
    public async Task Type(int number, string text, bool replace, CancellationToken ct)
    {
        var ready = await Eval($$"""
            (() => {
              const el = document.querySelector('[data-shiki="{{number}}"]');
              if (!el) return 'gone';
              if ({{SensitiveCheck}}) return 'sensitive';
              el.scrollIntoView({ block: 'center' });
              el.focus();
              if ({{(replace ? "true" : "false")}}) {
                if ('select' in el && typeof el.select === 'function') el.select();
                else { const range = document.createRange(); range.selectNodeContents(el); const s = getSelection(); s.removeAllRanges(); s.addRange(range); }
              }
              return 'ok';
            })()
            """, ct);
        var said = ready.ValueKind == JsonValueKind.String ? ready.GetString() : "gone";
        if (said == "gone") throw new BrowserException($"There's no element {number} any more: read the page again.");
        if (said == "sensitive") throw new BrowserException("That box is for a password, a card or a one-time code: Shiki never types those. Ask the person to fill it in themselves (your_turn).");
        await _bridge.Cdp(Id, "Input.insertText", new JsonObject { ["text"] = text }, ct);
        await Task.Delay(250, ct);
    }

    /// <summary>Picks an option in a list box (by its text or value).</summary>
    public async Task<string> Choose(int number, string option, CancellationToken ct)
    {
        var result = await Eval($$"""
            (() => {
              const el = document.querySelector('[data-shiki="{{number}}"]');
              if (!el || el.tagName !== 'SELECT') return 'not a list';
              const want = {{JsonSerializer.Serialize(option)}}.toLowerCase().trim();
              const o = [...el.options].find(o => o.text.toLowerCase().trim() === want || o.value.toLowerCase() === want)
                     || [...el.options].find(o => o.text.toLowerCase().includes(want));
              if (!o) return 'no such option; there are: ' + [...el.options].map(o => o.text.trim()).slice(0, 30).join(' | ');
              el.value = o.value;
              el.dispatchEvent(new Event('input', { bubbles: true }));
              el.dispatchEvent(new Event('change', { bubbles: true }));
              return 'picked ' + o.text.trim();
            })()
            """, ct);
        await Task.Delay(300, ct);
        return result.ValueKind == JsonValueKind.String ? result.GetString()! : "done";
    }

    static readonly Dictionary<string, (string Code, int Vk)> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = ("Enter", 13), ["Tab"] = ("Tab", 9), ["Escape"] = ("Escape", 27), ["Backspace"] = ("Backspace", 8),
        ["Delete"] = ("Delete", 46), ["ArrowDown"] = ("ArrowDown", 40), ["ArrowUp"] = ("ArrowUp", 38), ["ArrowLeft"] = ("ArrowLeft", 37),
        ["ArrowRight"] = ("ArrowRight", 39), ["PageDown"] = ("PageDown", 34), ["PageUp"] = ("PageUp", 33), ["Home"] = ("Home", 36),
        ["End"] = ("End", 35), [" "] = ("Space", 32), ["Space"] = ("Space", 32),
    };

    public static bool KnownKey(string key) => Keys.ContainsKey(key);

    /// <summary>One key press in the page (Enter, Tab, Escape, the arrows…).</summary>
    public async Task Press(string key, CancellationToken ct)
    {
        if (!Keys.TryGetValue(key, out var k)) throw new BrowserException($"Shiki can press Enter, Tab, Escape, Backspace, Delete, the arrows, PageUp/PageDown, Home, End and Space, not \"{key}\".");
        var name = key == "Space" ? " " : key;
        var down = new JsonObject { ["type"] = "keyDown", ["key"] = name, ["code"] = k.Code, ["windowsVirtualKeyCode"] = k.Vk };
        if (k.Vk == 13) down["text"] = "\r";
        if (k.Vk == 32) down["text"] = " ";
        await _bridge.Cdp(Id, "Input.dispatchKeyEvent", down, ct);
        await _bridge.Cdp(Id, "Input.dispatchKeyEvent", new JsonObject { ["type"] = "keyUp", ["key"] = name, ["code"] = k.Code, ["windowsVirtualKeyCode"] = k.Vk }, ct);
        await Task.Delay(k.Vk == 13 ? 800 : 200, ct);
        if (k.Vk == 13) await Settle(ct);
    }

    /// <summary>Is the box that has the keyboard part of a form whose buttons send, pay or book (so Enter would do that)?</summary>
    public async Task<string> FocusedFormButtons(CancellationToken ct)
    {
        var buttons = await Eval("""
            (() => {
              const el = document.activeElement;
              const form = el && (el.form || el.closest('form'));
              return form ? [...form.querySelectorAll('button,input[type=submit]')].map(b => (b.innerText || b.value || '').trim()).join(' | ').slice(0, 200) : '';
            })()
            """, ct);
        return buttons.ValueKind == JsonValueKind.String ? buttons.GetString()! : "";
    }

    public async Task Scroll(bool down, int? number, CancellationToken ct)
    {
        var target = number is { } n ? $"document.querySelector('[data-shiki=\"{n}\"]')" : "null";
        await Eval($"(() => {{ const el = {target}; const by = (el ? el.clientHeight : innerHeight) * 0.8 * {(down ? 1 : -1)}; if (el) el.scrollBy(0, by); else scrollBy(0, by); }})()", ct);
        await Task.Delay(400, ct);
    }

    /// <summary>The page's words, plain (for reading results, articles, prices), from this character on.</summary>
    public async Task<string> Text(int from, CancellationToken ct)
    {
        var text = await Eval("document.body ? document.body.innerText : ''", ct);
        var all = text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
        all = System.Text.RegularExpressions.Regex.Replace(all, @"\n{3,}", "\n\n");
        if (from >= all.Length) return "(That's the end of the page.)";
        const int Most = 12000;
        var piece = all.Substring(from, Math.Min(Most, all.Length - from));
        return from + piece.Length < all.Length ? piece + $"\n(…more from character {from + piece.Length} of {all.Length})" : piece;
    }

    /// <summary>A picture of what the tab shows (JPEG).</summary>
    public async Task<byte[]> Picture(CancellationToken ct)
    {
        var shot = await _bridge.Cdp(Id, "Page.captureScreenshot", new JsonObject { ["format"] = "jpeg", ["quality"] = 60 }, ct);
        return Convert.FromBase64String(shot.GetProperty("data").GetString()!);
    }

    /// <summary>The script that decides if an element (el) is a password, card or one-time-code box.</summary>
    const string SensitiveCheck = """
        ((el) => {
          const t = (el.getAttribute('type') || '').toLowerCase();
          const ac = (el.getAttribute('autocomplete') || '').toLowerCase();
          const words = ((el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('placeholder') || '')).toLowerCase();
          return t === 'password' || ac.startsWith('cc-') || ac === 'one-time-code' || ac.includes('password')
            || /\b(otp|cvv|cvc|card ?number|cardnumber|ccnum|upi ?pin|pin code|passcode|aadhaar|pan number)\b/.test(words);
        })(el)
        """;

    /// <summary>
    /// Numbers everything that can be used (links, buttons, boxes, lists, ticks) with data-shiki,
    /// and writes the page as lines: "[12] button "Search"", "# Heading", and the text between.
    /// </summary>
    const string Snapshot = """
        (() => {
          const MAX = 15000;
          document.querySelectorAll('[data-shiki]').forEach(e => e.removeAttribute('data-shiki'));
          const clean = (t, max = 90) => { t = (t || '').replace(/\s+/g, ' ').trim(); return t.length > max ? t.slice(0, max - 1) + '…' : t; };
          const shown = (el) => {
            const r = el.getBoundingClientRect();
            if (r.width < 2 || r.height < 2) return false;
            const s = getComputedStyle(el);
            return s.visibility !== 'hidden' && s.display !== 'none' && +s.opacity > 0.05;
          };
          const usable = 'a[href],button,input:not([type=hidden]),textarea,select,summary,[role=button],[role=link],[role=checkbox],[role=radio],[role=tab],[role=menuitem],[role=menuitemcheckbox],[role=option],[role=combobox],[role=switch],[role=textbox],[role=searchbox],[role=slider],[contenteditable=""],[contenteditable=true],[onclick]';
          const name = (el) => {
            let t = el.getAttribute('aria-label');
            const by = el.getAttribute('aria-labelledby');
            if (!t && by) t = by.split(/\s+/).map(id => (document.getElementById(id) || {}).innerText || '').join(' ');
            if (!t && el.id) { const l = document.querySelector('label[for="' + CSS.escape(el.id) + '"]'); if (l) t = l.innerText; }
            if (!t && ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName) && el.closest('label')) t = el.closest('label').innerText;
            if (!t) t = el.getAttribute('placeholder') || el.getAttribute('alt') || el.getAttribute('title');
            if (!t && !['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName)) t = el.innerText;
            if (!t) { const img = el.querySelector && el.querySelector('img[alt]'); if (img) t = img.alt; }
            if (!t) t = el.getAttribute('name') || (el.tagName === 'INPUT' && ['submit', 'button'].includes(el.type) ? el.value : '');
            return clean(t);
          };
          const role = (el) => {
            const r = el.getAttribute('role');
            if (r) return r;
            const tag = el.tagName;
            if (tag === 'A') return 'link';
            if (tag === 'BUTTON' || tag === 'SUMMARY') return 'button';
            if (tag === 'SELECT') return 'list';
            if (tag === 'TEXTAREA') return 'textbox';
            if (tag === 'INPUT') {
              const t = (el.type || 'text').toLowerCase();
              if (['submit', 'button', 'reset', 'image'].includes(t)) return 'button';
              if (t === 'checkbox' || t === 'radio') return t;
              if (t === 'password') return 'password box';
              return t === 'text' ? 'textbox' : t + ' box';
            }
            if (el.isContentEditable) return 'textbox';
            return 'clickable';
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
              const h = /^H[1-3]$/.test(parent.tagName) ? '# ' : '';
              add(h + t);
              return;
            }
            if (node.nodeType !== Node.ELEMENT_NODE) return;
            const el = node;
            const tag = el.tagName;
            if (['SCRIPT', 'STYLE', 'NOSCRIPT', 'SVG', 'TEMPLATE', 'IFRAME'].includes(tag)) return;
            if (el.getAttribute('aria-hidden') === 'true') return;
            let now = inside;
            if (!inside && el.matches(usable) && shown(el)) {
              n++;
              el.setAttribute('data-shiki', String(n));
              let line = '[' + n + '] ' + role(el) + ' "' + name(el) + '"';
              if (tag === 'INPUT' || tag === 'TEXTAREA') {
                if (el.type === 'password') line += el.value ? ' (filled)' : '';
                else if (el.type === 'checkbox' || el.type === 'radio') line += el.checked ? ' (ticked)' : ' (not ticked)';
                else if (el.value) line += ' = "' + clean(el.value, 60) + '"';
              } else if (tag === 'SELECT') {
                const o = el.options[el.selectedIndex];
                line += ' = "' + clean(o ? o.text : '', 40) + '" (options: ' + [...el.options].slice(0, 12).map(o => clean(o.text, 30)).join(' | ') + (el.options.length > 12 ? ' | …' : '') + ')';
              } else if (el.getAttribute('aria-checked')) line += el.getAttribute('aria-checked') === 'true' ? ' (on)' : ' (off)';
              else if (el.getAttribute('aria-expanded')) line += el.getAttribute('aria-expanded') === 'true' ? ' (open)' : ' (closed)';
              if (el.disabled || el.getAttribute('aria-disabled') === 'true') line += ' (disabled)';
              add(line);
              now = true;
            }
            if (el.shadowRoot) for (const c of el.shadowRoot.childNodes) walk(c, now);
            for (const c of el.childNodes) walk(c, now);
          };
          walk(document.body, false);
          const y = Math.round(scrollY), h = Math.max(document.documentElement.scrollHeight - innerHeight, 0);
          const at = h === 0 ? 'the whole page fits' : y <= 5 ? 'at the top' : y >= h - 5 ? 'at the bottom' : Math.round(100 * y / h) + '% down';
          return { title: document.title, url: location.href, text: lines.join('\n'), cut, at };
        })()
        """;
}
