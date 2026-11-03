# Shiki: complete project handoff

*Written 24 October 2026 at version 2.38.0; updated 29 October 2026 at version **2.42.0** (new-person address card, quick sends and reminders, one request one go, habits from corrections, morning briefing). Read this first, then the README (every feature, in detail) and docs/ROADMAP.md. Both are included after this section in the PDF.*

---

## 1. What Shiki is

Shiki (formerly "Converted Bro") is a **Windows desktop assistant shaped like a pixel cat** that lives at the bottom of the screen. It started as a local file converter (images, video, audio, PDF, documents, archives) and grew into a **voice-driven PC assistant**:

- Say **"Hey Sky"** (the cat's name; skins: Kuro/black is the user's, plus Mochi, Latte, Ginger, Sky) and talk to it in English, Hindi or Hinglish.
- It converts and tidies files, controls the PC (volume, apps, dark mode, lock), reads and sends **Gmail**, manages **Google Calendar**, finds **contacts**, sends **WhatsApp** messages (text and files), plays **Spotify**, takes **meeting notes**, sets **reminders**, does **dictation** anywhere, answers questions with **web search**, reads documents, and as a last resort uses a **computer-use agent** (mouse and keyboard).
- It talks back (ElevenLabs, Deepgram Aura or Windows voices), shows a small voice pill with a waveform, and asks before anything that changes things.

Owner: **Yash** (GitHub `yashkumar174`, Git user `yashkumar172`, you@example.com). Repo: **https://github.com/yashkumar174/Shiki** (branch `main`). The local folder is still `D:\projects\converted bro`.

---

## 2. Picking this up in a new account: the essentials

| Thing | Where / what |
|---|---|
| Code | `D:\projects\converted bro\src\Shiki` (project `Shiki.csproj`, exe `Shiki.exe`, namespace `Shiki`) |
| Language / framework | C# 12, **.NET 8**, WPF (+ a little WinForms), target `net8.0-windows10.0.19041.0` (needed for Windows OCR and WinRT media APIs) |
| .NET SDK | `%USERPROFILE%\.dotnet\dotnet.exe` (**not on PATH**; there is no winget) |
| Build (check only) | `"$USERPROFILE/.dotnet/dotnet.exe" build -c Debug -v q -nologo -o "$TEMP/shikicheck"` from `src/Shiki` |
| Build (to run the dev copy) | stop Shiki first (`taskkill //IM Shiki.exe //F`), then `dotnet build -c Debug`; the exe is `src/Shiki/bin/Debug/net8.0-windows10.0.19041.0/win-x64/Shiki.exe` |
| Installer | `powershell -ExecutionPolicy Bypass -File build/make-installer.ps1` → `Releases\ShikiApp-win-Setup.exe` (Velopack, packId **ShikiApp**, takes several minutes; run it in the background) |
| Install / update the user's copy | stop Shiki, run `Releases\ShikiApp-win-Setup.exe --silent` **from PowerShell** (not the Bash tool, see section 4), then start `%LOCALAPPDATA%\ShikiApp\current\Shiki.exe` |
| Installed app | `%LOCALAPPDATA%\ShikiApp\current\` (version in `sq.version`) |
| App data | `%LOCALAPPDATA%\Shiki\` (settings, logs, memory, tasks, models, index) |
| FFmpeg | not in the repo; `tools\ffmpeg\` (BtbN win64-gpl-shared), copied next to the exe by the build |

### The release routine used for every change

1. Make the change; build to the check folder until **0 errors**.
2. Test it (section 5): dev verbs, snapshots, logs. Never simulate mouse/keyboard input on the user's PC.
3. Bump `<Version>` in `src/Shiki/Shiki.csproj` (semver: features bump the minor, fixes the patch).
4. Add one bullet to the **README** feature list, right before the line that starts with `- **Log**: everything the cat was asked`, in the style `- **Short title** (v2.NN): what it does, in plain words.`
5. Escape private-use icon characters in `.cs` files as `\uE8A7` (Segoe Fluent glyphs written literally get mangled) and keep `.cs` line endings LF.
6. Stage **only the files you changed** (`git add <paths>`, never `git add -A`; see section 4), check `git status --short`, then commit with a message like the existing ones (title "What it does, version 2.NN.0", a short body, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`), and `git push origin main`.
7. Build the installer, install silently, restart the installed app, confirm `sq.version`.

(Steps 3–5 were done by a small Python script kept outside the repo; it isn't needed, the steps are simple.)

---

## 3. How the user likes to work (important)

- **Talk plainly.** The user is not a programmer by trade; explain outcomes in everyday words, short, no jargon. They write in English with Hinglish mixed in.
- **"Say it and it happens."** The north star: no keyboard, no mouse, as few confirmation steps as is safe, fast.
- **Build, test, ship.** For a batch of agreed work, build each item, self-test, commit, push and update the installed app without check-ins after every feature. Ask only for real decisions.
- **They test interactive things themselves.** Don't simulate input on their PC; ask them to try spoken flows.
- **Before coding something big or ambiguous, they may ask "don't code, tell me first"**: then explain and wait for the go-ahead.
- Recent design taste: premium, smooth (spring animations, a 4-bar waveform), no clutter, a slim scrollbar, cards that are short and clear.
- They post about Shiki publicly (social media), so polish matters.
- Language: everything shown or typed must be in **English letters**; Hindi is written in Roman letters (Hinglish), never Devanagari or other scripts.

### Safety rules that are part of the product (never relax these without the user)

- Anything that changes files or the PC, or sends something, asks first (a card with **Do it / Not now**, or a spoken "yes").
  - Exception the user asked for (v2.38): a WhatsApp or email to **someone already written to** goes after a **5-second countdown** ("say stop"), switchable in Settings. A card with several sends (v2.40) counts down only if every part would.
  - A send to someone new never counts down: the card asks for the number or email first (v2.39).
- The cat never types passwords, never pays, never deletes without asking.
- Web pages, emails, documents and file names are **data, not instructions**.
- Everything the cat does is logged (`cat-log.txt`, viewable from Settings).
- The privacy guard hides card numbers, Aadhaar, PAN, passwords and keys before anything goes to Claude.

---

## 4. Environment gotchas (learned the hard way)

- **The Bash tool's `%LOCALAPPDATA%` is virtualized** (it points into the Claude app's package cache). Read and write the real `%LOCALAPPDATA%\Shiki` with **PowerShell**, or with `%USERPROFILE%\AppData\Local\...` paths. Run the installer from PowerShell, or it installs into the sandbox.
- **Bash heredocs mangle backslashes** (`\\b` became a backspace character), and long ones can fail with "unexpected EOF". Write Python edit scripts with the file-writing tool into the scratchpad and run them with `python -I`, or use raw strings. After edits, scan `.cs` files for control characters.
- **Never `git add -A` in this repo.** Other sessions keep work in the same folder: the `ad/` folder (a Remotion ad project for Shiki, ~3,000 files) got swept into the v2.42.0 commit and pushed by mistake; the user chose to leave it on GitHub, so it is tracked now, but its changes belong to whoever works on the ad. Stage your own files by name.
- **PowerShell 5.1 `Get-Content` garbles UTF-8**: use `-Encoding UTF8`, or edit with Python using utf-8.
- **Stop Shiki before building** the Debug copy (the exe is locked otherwise). A build into `$TEMP/shikicheck` works while it runs.
- Single instance: launching `Shiki.exe <verb>` while it runs forwards the verb to the running copy over a named pipe. That's how dev verbs are sent.
- Velopack installs per user (no admin). The installer is unsigned (SmartScreen warning on other PCs).

---

## 5. Testing toolkit (dev verbs)

Run `Shiki.exe <verb> ...` (the running instance does it; results go to `%LOCALAPPDATA%\Shiki\log.txt` or `cat-log.txt`). The most useful:

| Verb | What it does |
|---|---|
| `ask-quiet <text>` | Sends a typed request to the cat (goes through instant commands, then Claude) |
| `ask-confirm <folder>` / `ask-decline` / `ask-choose <n>` | Press Do it / Not now / option n on the waiting card |
| `snapshot-bubble <png>` | Renders the Ask bubble (from the last thing said) to a PNG |
| `snapshot <page> <png>`, `snapshot-settings <png> [tab]`, `snapshot-pill <png> [words] [answer]` | Render dashboard pages, Settings tabs, the voice pill |
| `pill-demo [words]` | Shows the voice pill, then morphs it into the chat (for screen-frame captures) |
| `instant-test <text>` | Which instant answer a phrase gets (no Claude) |
| `speak-file <out.wav> <voice> <text>` | Renders the cat's voice to a WAV (e.g. `dg:aura-2-thalia-en`, `el:<voiceId>`, `win:`) |
| `stream-file <wav>` | Streams a WAV to Deepgram live, logs partials and timing |
| `wake-file <wav>` | Does this recording wake the cat ("Hey Sky")? |
| `say-test <text>` / `say-with <voice> <text>` | The cat says it out loud (first-sound time in the log) |
| `lookup-test <artist or song>` | The quick Spotify song lookup (no playback) |
| `names-test [learn heard meant]` | Contact names, words given to Deepgram, sounds-like matches, corrections |
| `script-test`, `reminder-answer-test`, `voice-phrases`, `hindi-test` | Language and answer parsing checks |
| `mime-test <out.eml> <files>` / `gmail-draft-test <to> <files>` | Builds the email with attachments (offline) / saves a real Gmail draft and reads it back |
| `google-test <name>`, `gmail-test`, `calendar-day`, `calendar-remove <id>` | Read-only Google checks; remove a test event |
| `reminder-say-test` | A pretend reminder, spoken and listened for |
| `deepgram-warm`, `eleven-warm` | Open those connections (latency tests) |
| `quick-test <text>` | How the quick path reads a request (sends, files, reminders, parts) and who each send would go to; nothing is sent (v2.40) |
| `address-test phone <text>`, `address-test email <text>` | How a typed or spoken number / email is read: cleaned value, the hint shown, the spoken reading (v2.39) |
| `address-card-demo phone <name> [typed]` (or `email`) | A pretend new-person card in the chat, for `snapshot-bubble` (it can't send) |
| `bundle-demo` | A pretend two-things card (WhatsApp + email with files), for `snapshot-bubble` |
| `briefing` | The day's briefing in the chat, as if asked (or `ask-quiet "brief me"`, which `snapshot-bubble` can draw) (v2.42) |
| `task-remove <exact words>` | Takes a test task back off the list |
| `whatsapp-read-test [number]` | Opens WhatsApp Web on that chat and only reads the page (tab, "Use here", message box, Send button); nothing is typed or sent (v2.43) |

Screen captures: toasts and some pills are **excluded from capture** (`WDA_EXCLUDEFROMCAPTURE`), so a screenshot won't show them; check window lists instead. PowerShell `CopyFromScreen` with `SetProcessDPIAware` works for other windows.

---

## 6. Architecture map

**`App.xaml.cs` (~3,600 lines) is the hub**: startup, tray, the cat, hotkeys, the wake word, the voice pipeline wiring, instant commands (`Instant`, `VoiceCommand`), reminders, WhatsApp and Spotify glue, the Ask bubble and voice pill, Settings wiring, and all dev verbs.

| Area | Files |
|---|---|
| Claude chat (tool loop, cards, streaming) | `Core/Ai/CatChat.cs` (model `claude-haiku-5-5`, prompt caching, `ChatItem` types: YouSaid, CatSaid, CatDid, PlanItem, ChoiceItem, TableItem, ChatProblem) |
| System prompt and tool definitions | `Core/Ai/CatPrompt.cs` (~880 lines: rules, ~60 tools incl. web_search/web_fetch server tools) |
| Computer-use agent | `Core/Ai/ComputerAgent.cs` (Claude Sonnet 5.5, `computer` toolset), `UI/AgentPill.cs` (pauses when the person clicks or types) |
| Memory and recent context | `Core/Habits.cs` (`Preferences`: remembered facts, 80 max since v2.41), `Core/Ai/Recent.cs` (what happened lately, injected into the first message) |
| Cards: new people, several things, local cards | `Core/Ai/CatChat.cs`: a `PlanItem` can carry an address box (`Ask`, `Address`, `AddressReady`; `FillAddress`, `RememberAddress`) and `Parts` (`Bundle` joins up to 4 sends/reminders into one card, one yes). `Offer`/`Card`/`Note` let the app make cards itself; the answer then goes back to Claude as a text note instead of tool results. `Core/Addresses.cs`: checks and cleans numbers (+91 by default) and emails, typed or spoken ("nine eight double two…", "name at gmail dot com") |
| Quick path (no Claude) | `Core/QuickPath.cs`: reads "WhatsApp X that…" (incl. Hinglish "X ko WhatsApp karo ki…"), "email/send this to me/X", "remind me at 5 / in 10 minutes / on Friday…", and 2–3 of them joined by and/then/aur/phir; every part must be understood or the whole request goes to Claude. Remembered numbers/emails come first. `App.RunQuick` finds people and files (File Explorer selection, or a learned name for files) and offers the card |
| Habits from corrections | `Core/Learned.cs`: the `learn_habit` tool (WhatsApp Web vs the app, files with or without text, names for files like "the logo photos" → paths), stored as plain sentences in Preferences |
| Morning briefing | `Core/Briefing.cs`: when it's the first wake of the day (after 4 am), "brief me" phrases, and `Gather` (today's calendar, unread mail from the last day without promotions/social/updates/forums, tasks due); Claude writes 3–6 spoken sentences; `App.Brief` runs it |
| People, names, corrections | `Core/People.cs` (contact names, sounds-like matching, `corrections.json`, who's "known" for the countdown) |
| Voice in | `Core/Voice/VoiceInput.cs` (listen, answer cards, follow-ups), `Deepgram.cs` (REST + live websocket, keyterms, endpointing), `WakeWord.cs` (speech bursts, "Hey Sky"), `WhisperLocal.cs` (offline fallback), `Dictation.cs`, `Hinglish.cs` (scripts to Roman, Hinglish commands), `VoicePrint.cs` (only-my-voice), `Microphone.cs`, `PcCommands.cs` |
| Voice out | `Core/Voice/Speaker.cs` (streaming playback, fallback chain), `Core/Voice/ElevenLabs.cs` |
| Google | `Core/Mail/Gmail.cs` (OAuth loopback + PKCE, Gmail API incl. attachments), `Core/Mail/Calendar.cs` (Calendar + Contacts) |
| Music | `Core/Spotify.cs` (open spotify: links, Windows media controls, quick lookup, remembered picks) |
| Files and tools | `Core/ActionCatalog.cs` (every wheel action), ImageTools, MediaTools (FFmpeg), PdfTools, OfficeTools, ArchiveTools, Cutout (background removal, ONNX), FormPhotos, ScanTools, QrTools, FileChores, ContentIndex (SQLite FTS5 "search inside files") |
| Tasks, reminders, meetings, routines | `Core/Todos.cs`, `Core/Meetings/*`, `Core/Routines.cs` |
| UI | `UI/CatWindow.cs` (the cat; keeps itself on top), `UI/AskBubble.cs`, `UI/VoicePill.cs` + `UI/Waveform.cs` + `UI/Spring.cs`, `UI/Dashboard/*` (Home, Ask, Tools, History, Meetings, Tasks, Skills "What Sky can do", Settings host), `UI/SettingsWindow.cs` (tabs), `UI/ToastWindow.cs` |
| Shell | `Shell/*` (single instance, hotkeys, Explorer menu, typing keys, PC control, screen capture) |

NuGet: Anthropic 13.1.0, Whisper.net 1.9.1 (+ Vulkan runtime), NAudio.Wasapi, Microsoft.Data.Sqlite, ONNX Runtime DirectML, Magick.NET, Docnet.Core (PDFium), **PDFsharp pinned to 6.2.4** (6.5+ is AGPL), QRCoder, ZXing.Net, SharpCompress, System.Management, Velopack.

---

## 7. Services, keys and accounts

All keys live in **Windows Credential Manager**, entered by the user in Settings (never paste keys into chat, never write them into files):

| Service | Credential target | Used for |
|---|---|---|
| Anthropic (Claude) | `Shiki/ClaudeApiKey` | The chat (Haiku 5.5), the agent (Sonnet 5.5), web search |
| Deepgram | `Shiki/DeepgramApiKey` | Listening: Nova-3 live (commands), the "Hey Sky" check, meetings, dictation; Aura-2 voices (backup voice) |
| ElevenLabs | `Shiki/ElevenLabsApiKey` | The cat's voice: Flash v2.5 (`eleven_flash_v2_5`), 21 voices, user picked **Adam** |
| Google | `Shiki/GmailClient` (client id + secret), `Shiki/GmailToken` (refresh token) | Gmail (`gmail.modify`), Calendar (`calendar.events`), Contacts (`contacts.readonly`, `contacts.other.readonly`); granted scopes in `google-access.txt` |
| Wispr Flow | `Shiki/WisprApiKey` | Not usable: Wispr Flow said **no API keys** |

Google: the OAuth client is the user's own Google Cloud project (app name "shiki"), with a privacy policy (docs/PRIVACY.md, also published as a GitHub gist) and the user's account as a test user. Public use would need Google verification and building the client into Shiki ("one-click Gmail", parked).

Spotify Web API was ruled out (2026 change: developer mode needs Premium; top-tracks removed). Deezer is region-blocked in India; song.link now needs a key. The quick music lookup uses Apple's iTunes Search API plus one Claude web search.

---

## 8. Data files in `%LOCALAPPDATA%\Shiki`

`settings.json` (all settings), `preferences.json` (remembered facts, e.g. "Tausif's email is tausif@example.com", "Mummy's WhatsApp is 919000000001", "My own email (when I say me or myself) is you@example.com"; since v2.39 numbers and emails typed into a new-person card are saved the same way, your own as "My own WhatsApp (when I say me or myself) is …" (an older "Me's WhatsApp is …" line is still read); since v2.41 learned habits are kept here too, e.g. "WhatsApp: use WhatsApp Web in Chrome, not the app." or `"the logo photos" means: D:\… | D:\…`), `corrections.json` ("torsive" → Tausif), `contact-names.json` (36 names, refreshed daily), `spotify-picks.json` (acdc, seedhe maut, pink floyd… → songs), `elevenlabs-voices.json`, `tasks.json`, `routines.json`, `history.json`, `index.db` (search inside files, ~4,267 docs), `meetings/`, `models/` (Whisper, background-removal model), `undo/`, `google-access.txt`, `log.txt` (app log), `cat-log.txt` (everything the cat did).

The user's current settings worth knowing: VoiceEngine and WakeEngine = **deepgram**, SpeechLanguage = Hindi + English, HindiScript = **roman**, **TalkBack = off** (they turned replies-out-loud off; reminders then don't speak either), TalkVoice = ElevenLabs Adam, FollowUpSeconds 8, VoiceAnswers on, CountdownSends on, wake word on with only-my-voice, **QuickPath on** (v2.40), **BriefingOnWake on** (v2.42; `LastWake` remembers the last "Hey Sky" so only the first of the day brings the briefing). Both new switches are in Settings → Ask the cat.

---

## 9. How the voice pipeline works now (numbers matter)

1. **Wake word:** the mic is watched locally; a speech burst (0.3–6 s, ended by 0.33 s of quiet) is sent to Deepgram (REST) to check for "Hey Sky" (Whisper locally if offline). The connection is warmed when speech starts. Check time ~0.3–1.2 s. Only-my-voice compares against a voice print.
2. **The voice pill** springs open above the cat (dot → pill), with a waveform and live words; it grows into the full chat for cards, choices, tables or long answers.
3. **Listening:** Deepgram Nova-3 live websocket, `language=multi` (Hinglish), `smart_format`, **keyterms** (cat name, the user's people from contacts/memory/corrections, routine phrases, app words; cap 90), `endpointing=500`, `utterance_end_ms=1200`. Shiki waits 2.5 s more only after dangling words ("and", "the", "play", "send", "can you") or soft words ("me", "it", "please") without closing punctuation.
4. **Cleaning what was heard:** other Indian scripts → Devanagari → Roman; any other script dropped (all-foreign → "I couldn't make that out"); known corrections applied.
5. **Handled on the PC first (no Claude), in this order:** (a) briefing phrases ("good morning", "brief me", "what's my day like", "aaj ka plan"); on the first "Hey Sky" of the day said on its own, the briefing comes by itself; (b) the **quick path** (v2.40): everyday sends and reminders, alone or 2–3 joined by "and", become a card at once; (c) two-part requests that aren't quick-path go to Claude; (d) **instant commands**: time, date, battery, today's calendar, new mail, open sites, YouTube/Google search, PC controls, routines, remembered music ("play acdc"), and a first-time "play <artist/song>" via the quick lookup.
6. **Claude** (Haiku 5.5, streamed, cached prompt) with tools. Simple actions "settle" without a second Claude call. Cards wait for Do it or a spoken yes; the mic opens by itself for cards and choices. When Claude asks for several cards in one turn they are joined into **one card with numbered parts** (up to 4, one yes). On a new-person card, a spoken number or email fills the box; "yes" before it's filled asks for it.
7. **Speaking:** ElevenLabs Flash v2.5 streamed as pcm_24000 (first sound ~0.16–0.2 s; warmed when you start talking) → Deepgram Aura-2 Thalia if ElevenLabs fails → a Windows voice. The mic stands aside while the cat talks.

---

## 10. Feature inventory (short; the README has every detail)

- **Files:** convert any image/video/audio/PDF/document, size limits, form photos (exam, passport, visa, signature), background removal, scan, OCR copy text, QR read/make, PDF toolkit (edit pages, sign, lock/unlock, page numbers, split, merge), zip/extract, the wheel (Shift+drag), Explorer right-click, snip and convert, screen recorder, colour picker.
- **Cat:** moods from the PC, levels and skins, calm/lively/still, stays on top (re-lifts itself if a window slips over it, v2.28), quips, only-my-voice.
- **Ask the cat (Claude):** chat in the bubble and dashboard, plan cards, choice cards ("Which Yash?"), web search and fetch, documents (ask, summarise, read aloud, translate), clipboard magic, smart rename, file chores (tidy with undo, disk space, backup), search inside files, paw to ask (point at the screen), "what's this error?", memory (preferences, recent context), habits.
- **Google:** Gmail search/read/draft/send with attachments (up to 25 MB, through the API, never the browser), save attachments, Calendar add/move/delete/invite, Contacts with sounds-like matching.
- **Messaging:** WhatsApp text and files (app or WhatsApp Web), countdown for known people, addresses remembered after first use; for someone new, the card has a box for the number (+91 filled in) or email, checked as you type or say it, then **Save and send** (v2.39).
- **"Say it and it happens" (v2.40–v2.42):** quick sends and reminders without waiting for Claude; several things in one sentence on one card with one yes; habits learned from corrections ("no, I always use WhatsApp Web", "just the photos, no text", "the logo photos" = these files); the **morning briefing** (today's meetings, emails that matter, tasks due) on the first "Hey Sky" of the day or "brief me" any time, then it listens for what's next.
- **Music:** Spotify play by song or artist (most popular song), checked through Windows' media controls, remembered for instant replay.
- **Voice:** "Hey Sky", conversation mode, Hindi/Hinglish, dictation anywhere, voice pill with waveform and spring morph, spoken replies, reminders spoken and answered by voice ("done", "later", "tomorrow").
- **Meetings:** notes with speakers, summary, decisions, tasks, ask the meeting, follow-up draft, auto-offer when a call starts.
- **Tasks and reminders**, **routines** ("start work mode"), **live captions and translation**, **download suggestions**, **privacy guard**, **computer-use agent** (last resort; refuses Gmail and Google Calendar while connected).

### Version history of the latest sessions (v2.17 → v2.42)

| Version | What |
|---|---|
| 2.17–2.24 | Agents, Hindi/Hinglish, Gmail API, What-can-Sky-do page, Settings tabs, right-click menu cleanup, voice delay and memory fixes, Calendar and Contacts, search inside files, five speed-ups |
| 2.25 | Answers that read well (Markdown-style formatting), bigger bubble |
| 2.26–2.27 | Waveform, "Hey Sky" voice pill, pill morphs into the chat with springs |
| 2.28 | Cat stays on top; WhatsApp Web in Chrome |
| 2.29–2.31 | Email attachments through Gmail; tidier cards; never the browser for Gmail; mic opens for cards; addresses remembered |
| 2.32 | WhatsApp files without the mouse; spoken reminders |
| 2.33 | Slim scrollbar in the bubble |
| 2.34 | ElevenLabs voices (2.34.1: connection warm-up) |
| 2.35 | "Which Yash?" choice cards; Spotify that really plays; instant commands for typed requests |
| 2.36 | Quicker end-of-speech, quick first-time music lookup, Spotify switching while playing |
| 2.37 | English letters only; two-part requests go to Claude |
| 2.38 | Contact names for the speech engine, sounds-like names, learned corrections, 5-second countdown for known people |
| 2.39 | New-person address card: a box for the number or email on the card, checked as you type or say it, Save and send, remembered |
| 2.40 | Quick sends and reminders without Claude; several things on one card with one yes |
| 2.41 | Habits learned from corrections (WhatsApp Web vs app, text with files, names for files); memory holds 80 facts |
| 2.42 | Morning briefing; sending to yourself ("WhatsApp this to me") remembers your own number |

---

## 11. Known issues and things not yet verified

- **WhatsApp files (fixed in v2.43, the real send not yet tried).** The 26 October failure was because **the WhatsApp app isn't installed** on the user's PC (no `whatsapp:` link handler); the cat opened the app's link, nothing came up, and after 15 s it fell back to the clipboard (whose text it then put back, so the file was gone too; also fixed). Now: no app → WhatsApp Web in Chrome, read with Windows UI Automation (`Shell/WhatsAppWeb.cs`): wait for the message box ("Type a message to +91 …"; up to 150 s while it says "Loading your chats"), "Use here" if needed, paste, wait for the preview (caption box / Send button), caption, press Send. Checked read-only with `whatsapp-read-test 919000000002`: tab in ~1 s, box at ~9 s. Each step is logged as "whatsapp" lines in cat-log.txt. v2.43.1: a "Starting chat" that hangs over 12 s is cancelled and the link opened once more; "Phone number shared via url is invalid" ends the send with a plain message. A fresh tab is opened each time on purpose: reusing an open WhatsApp tab could paste into whichever chat it was showing.
- **The morning briefing hasn't run on a real morning yet** (`LastWake` was still empty on 29 October). With **TalkBack off** it is shown in the chat, not read aloud.
- **Six "Shiki: …" tasks** (from planning, due 24 October) sit in Shiki's own Tasks list and show as overdue in the briefing. Five are built (names into Deepgram, countdown, learning corrections, commands without Claude, the ElevenLabs voice); "Try local speech-to-text (Whisper/Parakeet)" is not. Tick off the five when the user says so.
- **Quick path limits:** it only takes the patterns it knows (English and some Hinglish), at most 3 parts; anything it can't read fully goes to Claude, which is slower but still works. Bare small hours mean pm ("at 5" = 5 pm).
- **WhatsApp Web text send:** presses Enter ~7 s after the tab appears; slow loads may leave the message typed but unsent.
- **Spotify:** album and playlist links may open without playing; the quick lookup's song pick comes from Apple's store (sometimes a less famous song); moods and playlists go through Claude (slower).
- **"Hey Sky" check** still costs ~0.3–1.2 s (Deepgram REST round trip).
- **Deepgram multi** sometimes "hears" other languages in noise (now converted or dropped).
- **Talking back is off** in the user's settings, so they haven't really heard ElevenLabs; reminders are silent while it's off.
- The installer is **unsigned**; FFmpeg is the **GPL** build (fine personally, matters if Shiki is distributed); Google OAuth is in testing mode (only the user's account).
- Business names from "other contacts" (Lenovo, Ola, Unstop…) take a few of the 30 name slots given to Deepgram.

---

## 12. Future plan (in priority order)

*Done since the first version of this handoff: the new-person address card (v2.39), the quick path (v2.40), one request one go (v2.40), learning from corrections (v2.41) and the morning briefing (v2.42).*

**Next up (offered, waiting for the user's go-ahead)**

1. **Confirm WhatsApp files with a real send** (built in v2.43, section 11): the user sends a file to themselves; if it stops, the "whatsapp" lines in cat-log.txt say at which step.
2. Tick off the five finished "Shiki: …" tasks in Shiki's Tasks list.

**The rest of the "say it and it happens" plan**

3. *(Suggestion, not discussed yet)* More quick-path patterns (calendar: "put lunch with Rahul on Friday at 1"; music and PC commands chained with sends).
4. Hold sends a few seconds as an undo window (partly done via the countdown).
5. *(Suggestion)* "Find the photos, email them to Tausif": a find step inside one-go requests (today the files must be selected or have a learned name).

**Voice quality**

6. Test local speech-to-text (Whisper large-v3-turbo on the GPU, NVIDIA Parakeet) against Deepgram on the user's own voice, names and Hinglish; consider a hybrid (local for wake word and short commands).
7. A 50-sentence personal test set (names, Hinglish, "no wait…" corrections, a noisy recording) to compare engines and Wispr Flow objectively.
8. Dictation cleanup pass (Wispr-Flow-style): Claude Haiku rewrite with the names list, removing fillers, handling self-corrections, formatting by app.
9. Optional Eleven v4 Turbo voice model choice in Settings.
10. Faster "Hey Sky": race local Whisper with Deepgram, or stream bursts.

**Bigger features discussed**

11. *(Suggestion)* Briefing extras: weather, the first meeting's link to join by voice, "reply to Rahul" straight from the briefing (it already listens after it).
12. "Anything important?" email triage with voice replies; follow-up reminders ("if Rahul doesn't reply by Friday").
13. Clipboard history ("paste the link I copied an hour ago").
14. Phone handoff (send to phone, OTPs via Phone Link).

**Shipping Shiki to others**

15. Auto-updates (Velopack already writes the files), code signing, a small website, Google verification and a built-in Google client (one-click Gmail), an LGPL FFmpeg build, Mellow's cat skin (needs colours).

---

## 13. People and facts the cat should know

- Mummy (WhatsApp 919000000001), Tausif Ansari (tausif@example.com; another Tausif contact exists), two Yashes in contacts (Yash Choudhary yash.c@example.com and Yash Kumar = the user).
- The user listens through OnePlus Buds 3 at times (16 kHz call-quality mic; the laptop's 48 kHz mic hears names better).
- The user likes AC/DC, Pink Floyd, Seedhe Maut, Arijit Singh / Kesariya.
