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
/// if the person did it, but the person's own mouse and keyboard stay free. Everything that runs
/// inside the page is written in the extension; Shiki only names the job ("read", "click 12").
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
        try { await _bridge.Cdp(Id, "Page.enable", null, ct); } catch (Exception) { }
    }

    /// <summary>Lets go of the tab (the "Shiki started debugging this browser" bar goes away).</summary>
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

    /// <summary>One of the extension's page jobs (read, describe, locate, prepare, choose, scroll, formButtons, text, ready, back); its answer.</summary>
    async Task<JsonElement> Page(string action, CancellationToken ct, int seconds = 20, params object?[] args)
    {
        var list = new JsonArray();
        foreach (var a in args) list.Add(JsonValue.Create(a));
        var answer = await _bridge.Call("page", new JsonObject { ["tabId"] = Id, ["action"] = action, ["args"] = list }, ct, seconds);
        return answer.TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    public async Task GoTo(string url, CancellationToken ct)
    {
        await _bridge.Cdp(Id, "Page.navigate", new JsonObject { ["url"] = url }, ct);
        await Task.Delay(400, ct);
        await Settle(ct);
    }

    public async Task Back(CancellationToken ct)
    {
        await Page("back", ct);
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
                var state = await Page("ready", ct, seconds: 5);
                if (state.ValueKind == JsonValueKind.String && state.GetString() == "complete") break;
            }
            catch (BrowserException) { }
            await Task.Delay(500, ct);
        }
        await Task.Delay(500, ct);
    }

    /// <summary>The page as Shiki reads it: title, address, and a numbered list of what can be used, with the text around it.</summary>
    public async Task<string> Read(CancellationToken ct)
    {
        var page = await Page("read", ct);
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
        var info = await Page("describe", ct, 20, number);
        if (info.ValueKind != JsonValueKind.Object) return null;
        return (info.GetProperty("kind").GetString() ?? "", info.GetProperty("label").GetString() ?? "", info.GetProperty("sensitive").GetBoolean(), info.GetProperty("buttons").GetString() ?? "");
    }

    /// <summary>Clicks element number n (scrolled into view first) with a real mouse click.</summary>
    public async Task Click(int number, CancellationToken ct)
    {
        var at = await Page("locate", ct, 20, number);
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
        var ready = await Page("prepare", ct, 20, number, replace);
        var said = ready.ValueKind == JsonValueKind.String ? ready.GetString() : "gone";
        if (said == "gone") throw new BrowserException($"There's no element {number} any more: read the page again.");
        if (said == "sensitive") throw new BrowserException("That box is for a password, a card or a one-time code: Shiki never types those. Ask the person to fill it in themselves (your_turn).");
        await _bridge.Cdp(Id, "Input.insertText", new JsonObject { ["text"] = text }, ct);
        await Task.Delay(250, ct);
    }

    /// <summary>Picks an option in a list box (by its text or value).</summary>
    public async Task<string> Choose(int number, string option, CancellationToken ct)
    {
        var result = await Page("choose", ct, 20, number, option);
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

    /// <summary>The buttons of the form the keyboard is in (so Enter would press one of them).</summary>
    public async Task<string> FocusedFormButtons(CancellationToken ct)
    {
        var buttons = await Page("formButtons", ct);
        return buttons.ValueKind == JsonValueKind.String ? buttons.GetString()! : "";
    }

    public async Task Scroll(bool down, int? number, CancellationToken ct)
    {
        await Page("scroll", ct, 20, down, number);
        await Task.Delay(400, ct);
    }

    /// <summary>The page's words, plain (for reading results, articles, prices), from this character on.</summary>
    public async Task<string> Text(int from, CancellationToken ct)
    {
        var text = await Page("text", ct);
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
}
