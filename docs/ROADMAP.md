# Shiki roadmap

What's built is in the README. This file is what comes next, in order. Every feature keeps the
same rules: anything that changes files or the PC asks first (a plan card or a spoken yes), the
cat never types passwords or pays for anything, and nothing is deleted without asking.

## The wow batch (decided 23 October 2026, built the same night)

Built in this order unless we decide otherwise.

### 1. Paw to ask (built, v2.1)
Hold a shortcut (or say "Hey Sky, look at this") and the pointer becomes the cat's pixel paw.
Swipe the paw around anything on screen: it leaves a trail of little paw prints around what you
picked. Then ask about it, by voice or typing: "what is this?", "make this a table", "copy the
text", "what does this chart say?". The picked area goes to Claude (vision) with your question.
- The paw trail closes into the area you meant; a plain drag works too.
- Results come back in the bubble, with buttons where they make sense (Copy, Save as Excel, Open link).

### 2. "Hey Sky, what's this error?" (built, v2.1)
The cat looks at the window in front (or what you pawed), reads the error, explains it in plain
words and suggests the fix. Shares the screen capture and Claude vision of item 1.
- Offers the fix as a step-by-step card; anything it would change on the PC asks first.
- Never reads or sends password fields.

### 3. Meeting buddy (built, v2.2)
"Hey Sky, take notes" when a meeting starts; "Hey Sky, stop notes" (or the call ends) and it
gives you the summary.
- Records the call's sound (what you hear, through Windows) and your microphone, and writes it
  down with Deepgram, speakers labelled ("Speaker 1", renamed by you later).
- Summary: what was discussed, decisions, open questions.
- **Tasks**: anything you're asked to do becomes a task in a new Tasks page, with who asked and
  the due date if one was said ("send the deck by Friday"). A reminder pops up from the cat when
  it's due.
- Live notes in a side panel while it runs; "Hey Sky, mark that" pins the moment.
- Ask the meeting afterwards: "what did Rahul say about the budget?"
- Draft a follow-up email with the summary and tasks (copied to the clipboard; sending comes
  with the email agent later).
- Spots a meeting starting (Zoom, Teams, Meet or any call using the mic) and quietly offers to
  take notes, once.
- Meetings page: every meeting with its summary, tasks and transcript, searchable.
- Privacy: everything stays on the PC except the audio sent to Deepgram while notes are on; a
  reminder to let others know you're taking notes; an option to keep only the notes and drop
  the recording.

### 4. The cat reacts to your PC (and grows) (built, v2.3)
- Moods from the PC: sleepy and slow when the battery is low, fanning itself when the processor
  is hot, a little cheer when a download finishes, dozing when you're away.
- Levels: it grows with use (files handled, requests, meetings), and new levels unlock skins and
  accessories (hats, scarves, a laptop for the meeting buddy).

### 5. Only your voice (built, v2.4)
The cat learns your voice when you teach it its name, so "Hey Sky" from a video, a call or a
friend doesn't wake it. Checked on this PC.

### 6. Alive, without the distraction (built, v2.5)
The cat moves, but never pulls you away from work:
- It stays in its lane along the bottom edge: a few steps now and then, stretches, yawns, looks
  toward the pointer or the window you're in.
- Ears perk up when you speak; it settles when you're typing.
- When it finds a file, it turns and points (a small paw toward that side) instead of running
  across the screen.
- Still during full-screen apps, games, videos and presentations, and while you're typing.
- A Calm setting: Lively, Calm (the default) or Still.

### 7. Dictation anywhere (built, v2.6)
Hold a key and talk; your words are typed wherever the cursor is (WhatsApp, Word, email, the
browser), with punctuation. Uses the same voice engine as commands (Deepgram now, Wispr Flow
later, Whisper offline). "Hey Sky, type…" works too.

## After the wow batch

In no fixed order yet; we pick as we go.

- **Voice for the PC** (built, v2.7): volume, mute, brightness, dark mode, Do Not Disturb, open and close apps,
  lock and sleep (asks first), reminders, switching the sound output.
- **Routines** (built, v2.9): "start work mode", "done for the day", and your own phrases ("when I say meeting,
  mute notifications and open Zoom").
- **Files by voice** (built, v2.10): tidy Downloads into folders, find, compress and copy a file, the disk-space
  helper ("what's eating my disk?"), backup of Documents to another drive, a passport photo from
  the newest photo.
- **Screen by voice** (built, v2.11): start and stop recording, copy the text on screen, the colour under the
  pointer.
- **The cat talks back** (built, v2.8): spoken replies (Windows voices or Deepgram), and it never hears itself.
- **Documents** (built, v2.12): ask a PDF a question, summarise a Word file, read aloud.
- **What is this?** for files (built, v2.13), **Smart rename** (batch, with undo), **Translate** (documents,
  pictures, screen text).
- **Live captions and translation** for anything playing on the PC.
- **Habits** (built, v2.14): "you always turn HEIC into JPG, want me to do it automatically?"; **preferences** it
  remembers ("my PDFs under 2 MB").
- **Clipboard magic** (built, v2.14): "clean this up", "make this a table", "turn this address into a QR code".
- Earlier ideas: local suggestions, a privacy guard, data extraction, personality quips.
- **Wispr Flow** for commands once their API is open to us.

## Much later

- **Agents**: email replies (asks before sending), a browser agent, computer use. Never
  passwords or payments.
- **Phone link**: files between phone and PC by voice or QR, after the desktop is polished.
