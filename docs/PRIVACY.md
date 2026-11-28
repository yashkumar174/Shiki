# Shiki privacy policy

Last updated: 28 November 2026

Shiki is a Windows desktop assistant: a small cat on your screen that converts and tidies files, answers questions, and helps with email when you ask it to. This page explains what Shiki reads, where it goes, and what stays on your PC.

## The short version

- Shiki runs on your own PC. It has no servers of its own, no accounts, no ads and no analytics. The developer never receives your files, voice, email or anything else.
- When you ask for something that needs AI, speech recognition or email, Shiki sends only what that request needs, directly from your PC, to the service you set up with your own key or sign-in: Anthropic (Claude), Deepgram, and Google (Gmail).
- Keys and sign-ins are kept in Windows Credential Manager on your PC. You can remove any of them in Settings at any time.

## What stays on your PC

- Your files. Conversions, compression, PDF tools, renaming, tidying and backups all happen on your PC.
- Settings, history, tasks, routines, meeting notes and the cat's log, in your Windows user folder (`%LOCALAPPDATA%\Shiki`).
- Your voiceprint for "only my voice wakes the cat", made and compared on your PC.
- Speech recognition with Whisper, when you choose it instead of Deepgram.

## What is sent, and to whom

**Anthropic (Claude)**, with your own Claude API key, when you ask the cat something:
- the words of your request, and the cat's earlier replies in the same conversation;
- names and paths of files involved, and the text inside a document when you ask about that document;
- a picture of part of your screen when you point at it, or a screenshot of the window in front when you ask "what's this error?" or ask the cat to work on screen;
- the text or picture you copied, when you ask it to work on your clipboard;
- the content of an email, when you ask about that email;
- the text you selected, when you use writing help (fix, shorten, translate, reply);
- the text of web pages (and, when needed, a picture of the tab), while Shiki does a task you asked for on a website in your browser (see "Shiki's browser extension" below);
- the name of the town your PC is in, when "Know my town" is on (see "Location" below);
- a meeting's transcript, to write its summary and tasks when you asked for meeting notes;
- caption lines, when you ask for live captions to be translated.

Before text from documents, the clipboard or email is sent, Shiki's privacy guard hides card numbers, Aadhaar and PAN numbers, passwords and API keys it finds, on your PC. Anthropic's API terms apply to what is sent; under them, API inputs aren't used to train models.

**Deepgram**, with your own Deepgram key, if you choose Deepgram for speech:
- short recordings of what you say to the cat (and, for the wake word, short bursts of speech the PC heard);
- the sound of a meeting while you have asked for meeting notes, and what the PC plays while live captions are on;
- the text of replies the cat reads aloud, when you choose a Deepgram voice.

**Google (Gmail)**, only after you connect Gmail in Settings:
- Shiki uses the Gmail API with your permission to search, read, draft, send, archive and mark emails, only when you ask it to. An email is sent only after you see it in full and press "Do it".
- Google's sign-in happens on Google's own page; Shiki never sees your Google password. The access Google gives back is stored in Windows Credential Manager on your PC and is used only from your PC.
- Email content is shown to you and, when you ask the cat about it, sent to Anthropic's Claude to answer your request. It is not stored by Shiki beyond the conversation, not sold, not used for advertising, and not used to train AI models. It is never transferred to the developer or anyone else.

Shiki's use and transfer of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements.

**Web search.** When you ask about something on the web, Claude searches and reads web pages through Anthropic's web search; the search words come from your request.

## Shiki's browser extension

Shiki's extension for Chrome, Edge and other Chromium browsers lets the Shiki app on your PC do tasks you ask for on websites, in your own browser.

- The extension connects only to the Shiki app on the same computer (127.0.0.1). It has no server and sends nothing anywhere else. The Shiki app accepts connections only from Shiki's own extension.
- It does nothing on its own. Only while Shiki is doing a task you asked for does it open a tab (or use the one you point it to) and, through the browser's debugger, read that page's text, click, type and scroll there. The browser shows "Shiki started debugging this browser" while it does; when the task ends, it lets go.
- To work out the next step, the Shiki app sends the page's text (and, when needed, a picture of the tab) to Anthropic's Claude with your own key, as described above. Nothing is stored after the task, beyond a line in the cat's log on your PC saying what was done.
- It never types passwords, one-time codes, card or bank details or ID numbers: when a page needs those, Shiki asks you to type them yourself. Anything that sends, submits, books, buys or deletes waits for your yes.
- It doesn't read your history, bookmarks, cookies or saved passwords, and doesn't track the sites you visit.
- You can remove the extension at any time from your browser's extensions page.

## Location

When "Know my town" is on (Settings), Shiki asks Windows where the PC is (Windows' own location setting, the one Weather and Maps use) and finds the nearest town in a list stored on your PC. Only the town's name is sent to Claude, so that answers about travel, weather or "near me" start from where you are. Your position itself never leaves your PC. You can turn this off, or set your town yourself ("I live in…").

## Your phone

When you connect a phone, photos and files go directly between the phone and your PC over your own Wi-Fi (or the phone's hotspot). They don't pass through any server.

## Your choices

- Turn off the wake word, voice features, talking back, the privacy guard, suggestions and the cat's remarks in Settings.
- Remove your Claude or Deepgram key in Settings at any time.
- Disconnect Gmail in Settings, which also asks Google to revoke Shiki's access. You can also remove it from your Google Account at myaccount.google.com/permissions.
- Uninstalling Shiki removes the app; your settings folder (`%LOCALAPPDATA%\Shiki`) can be deleted to remove everything it kept.

## Children

Shiki is not directed at children under 13.

## Changes

If this policy changes, the date at the top changes with it.

## Contact

Questions about this policy: write to the support email shown on Google's sign-in screen for Shiki.
