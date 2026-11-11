# Claude Keys

<img src="keypad.svg" width="400" alt="The keypad: a Claude session working on step 3 of 5, one waiting for you, one finished, a Codex thread working, an idle session, an empty key, and Dictate, Return and Cancel">

A Logitech MX Keypad plugin for the Claude desktop app on macOS. It shows your Claude sessions on the keypad and drives them.
Codex threads can go on the session keys too, for the times you use both.

## Keys

| Key | Tap | Hold |
|---|---|---|
| Session 1–6 | Open that session (Claude or Codex). Empty: pin the session on screen | Pin here, Unpin, Archive (Claude only) |
| Dictate | Start or stop Claude's mic | — |
| Return | Press Return: sends, finishes dictation, or queues while Claude works | New (session), BTW (side chat), Fork, Yes, Commit |
| Cancel | Press Escape: cancels dictation or interrupts Claude | Archive |

Session key colours: blue dots = working, amber = waiting for you, green = finished, grey = idle.
Codex bubbles point right.

## Hold pages

Holding Return, Cancel or a session key turns the keypad into a page of buttons. The Cancel key closes it,
and it closes by itself after 10 seconds.

- **Return** acts on the session on screen. New, BTW and Fork take what you've typed or dictated and send it to a
  new session, a side chat (`/btw`) or a fork of this one. Yes presses the main button of Claude's next-step box,
  or sends your text when there's no box. Commit sends "Let's do a commit".
- **Cancel** archives the session on screen.
- **A session key**: Pin here puts the session on screen on that key, Unpin empties the key, and Archive
  archives that key's session.

## Install

1. `brew install --cask dotnet-sdk`
2. `dotnet tool install --global LogiPluginTool`
3. `cd ClaudeKeysPlugin && dotnet build`
4. In Logi Options+, drag the Claude Keys actions onto your keypad: Session 1–6, Dictate, Return and Cancel.
   Put them wherever you like; the picture above is one layout.

## Tip: short session names

Session names are long by default. Add this to `~/.claude/CLAUDE.md` so they fit on a key:

    ## Session names
    - Rename the session with the session title tool at the start of a new session, and again right after a fork.
    - 1–3 words, under 16 characters, naming the task: "Login bug", "Tax receipts".
    - A fork gets a name for what it will do next, never "fork", "copy", "v2" or a number.
    - No filler words, dates or emoji.

## Limits

- Codex support covers the session keys only: they show a thread's status and switch to it.
  Dictate, Return and Cancel act on Claude only, and Archive on a held session key is Claude only.
- No ready-made layout. Logi plugins can ship a preset profile only for a specific app, and Claude Keys
  works in every app, so you place the keys yourself.
- Mac only. It reads Claude's and Codex's own files and shortcuts, which are undocumented,
  so an app update can break a key.
