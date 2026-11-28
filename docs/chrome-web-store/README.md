# Putting Shiki's extension in the Chrome Web Store

Once it's in the store, people add it with one click from Shiki ("Add Shiki to Chrome"). This folder has everything the submission asks for; the steps below say where each piece goes.

## What you need first (only you can do these)

1. A Chrome Web Store developer account: open https://chrome.google.com/webstore/devconsole, sign in with the Google account you want the listing under, accept the developer agreement and pay the one-time US$5 registration fee.
2. In the console's Account page, add a contact email and verify it (Google won't publish without it).

## The package

Run from the repo (it leaves out the `key` that only the hand-loaded copy uses; the store gives the extension its own id):

```
python build/pack-extension.py
```

It writes `Releases/Shiki-extension-1.0.0.zip`. That zip is what you upload.

## Step by step in the developer console

1. Click **New item** and upload `Releases/Shiki-extension-1.0.0.zip`.
2. **Store listing** tab: fill in the fields from "Store listing" below, and upload:
   - Store icon: `browser-extension/icon128.png`
   - Screenshots (1280×800): `screenshot-1-answers.png`, `screenshot-2-how.png`
   - Small promo tile (440×280): `promo-tile-440x280.png`
3. **Privacy** tab: fill in the single purpose, the permission justifications, the remote-code answer and the data-use answers from "Privacy" below. Privacy policy URL: https://github.com/yashkumar174/Shiki/blob/main/docs/PRIVACY.md
4. **Distribution** tab: Public; all regions (or the ones you want).
5. Click **Submit for review**. The `debugger` permission gets a closer look, so expect a few days. Google emails you when it's done.
6. When it's published, copy its id (the 32 letters at the end of the listing's address, `https://chromewebstore.google.com/detail/<name>/<id>`) and send it to Claude: it goes in `BrowserBridge.ChromeStoreId` in `src/Shiki/Shell/BrowserBridge.cs`, and from that release on Shiki's setup is one button.

Edge (optional, free): Microsoft Partner Center at https://partner.microsoft.com/dashboard/microsoftedge, the same zip and texts. Its id goes in `BrowserBridge.EdgeStoreId`. Without it, Edge users are sent to the Chrome Web Store, which Edge also accepts (Edge asks once to allow extensions from other stores).

## Store listing

**Name:** Shiki

**Summary** (132 characters at most):
> Lets Shiki, the cat on your Windows PC, do tasks on websites for you in your own browser. Asks you before anything that counts.

**Category:** Productivity → Workflow & Planning

**Language:** English

**Description:**
> Shiki is a desktop assistant for Windows: a small pixel cat on your screen that converts files, answers questions and does chores when you ask. This extension lets the Shiki app do things on websites for you, in your own browser, where you're already signed in.
>
> Ask Shiki (by typing or by voice) for something on the web: "fill in the leave form on the HR portal", "find the cheapest bus to Goa on the 12th", "download my last electricity bill". Shiki opens a new tab and works there: it reads the page, clicks and types like you would, and tells you what it did or found. Your own mouse and keyboard stay free while it works.
>
> It asks first, always. Anything that sends, submits, books, buys or deletes waits for your yes in Shiki.
> It never types your secrets. Passwords, one-time codes and card numbers: Shiki hands those parts to you.
> It talks only to Shiki on your PC. The extension has no server and connects only to the Shiki app on the same computer. It does nothing on its own: only while Shiki is doing a task you asked for.
>
> While Shiki works in a tab, the browser shows "Shiki started debugging this browser". That's how the extension clicks and reads the page; the bar goes away when the task is done.
>
> You need the Shiki app for Windows: https://github.com/yashkumar174/Shiki

**Homepage URL:** https://github.com/yashkumar174/Shiki

**Support URL:** https://github.com/yashkumar174/Shiki/issues

## Privacy

**Single purpose:**
> Lets the Shiki desktop app on the same computer carry out web tasks the user asks it for, in a tab of the user's own browser.

**Permission justifications:**

- `debugger`:
  > Shiki carries out web tasks the user asks for (filling in a form, searching a site) in a tab of the user's browser. Through chrome.debugger it attaches only to the tab it opened for that task, sends real mouse and keyboard input there (Input.dispatchMouseEvent, Input.dispatchKeyEvent, Input.insertText), opens addresses (Page.navigate), takes a picture of the tab when needed (Page.captureScreenshot), and runs the page-reading functions written in this extension (Runtime.evaluate) to list the page's links, buttons and boxes. Only those commands are allowed (see the ALLOWED list in background.js). It attaches when a task starts and detaches when it ends, and the browser shows its "started debugging" bar meanwhile.
- `tabs`:
  > To open a new tab for a task the user asked for, bring it to the front, follow a link that opens in a new tab, and list the open tabs when the user asks Shiki to work in one of them.
- `alarms`:
  > To reconnect to the Shiki desktop app (a WebSocket on 127.0.0.1, the same computer) when the service worker wakes up, so the extension is ready when the user asks Shiki for something.

**Host permissions:** none.

**Are you using remote code?** No.
> All JavaScript that runs, in the extension and in pages, is in the package (the page jobs are the PAGE functions in background.js). The Shiki app on the same computer only names which job to run and with which values (for example "click element 12"); it never sends code.

**Data usage** (tick what applies; this is what Shiki does):
- Collects **Website content** (text of the page in the tab Shiki is working in, and a picture of it when needed): yes, only during a task the user asked for. It's passed to the Shiki app on the same computer, which sends it to Anthropic's Claude (with the user's own API key) to decide the next step. Nothing else is collected: no personal info, history, location, authentication info, communications, financial info or user activity.
- Certify: not sold to third parties; not used or transferred for purposes unrelated to the item's single purpose; not used or transferred to determine creditworthiness or for lending.

## Notes for the reviewer (the "Additional information" box, if asked)

> The extension is the browser half of the Shiki desktop app (open source: https://github.com/yashkumar174/Shiki). To try it: install Shiki for Windows, add a Claude API key in its settings, add this extension, then ask Shiki "go to wikipedia.org and search for Jamshedpur". The app connects to the extension over ws://127.0.0.1:47820 and only accepts connections whose Origin is this extension.
