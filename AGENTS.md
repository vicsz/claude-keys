# Claude Keys: agent notes

C# Logi Actions plugin for the MX Creative Keypad (3×3 keys, 2 page buttons). Read this before changing code.

## Rules
- Keep it minimal: no generated, optional or sample files.
- Commit only when asked.
- You can't press the keypad. Build, then ask the user to test on hardware.
- Never print or log `~/.claude/sessions/*.key`. They hold tokens.

## Build
- Needs the .NET 10 SDK and `LogiPluginTool`. Logitech's docs say .NET 8, but Plugin Service 6.4 runs .NET 10.
- Build from `ClaudeKeysPlugin/` only: `dotnet build`. It reloads the plugin itself. Expect 0 warnings.
- Never build from `src/`. It creates `src/obj` and breaks the next build. Delete `src/obj` to fix it.
- Log: `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/ClaudeKeys.log`

## Installing for a user
1. Check the basics: macOS, the Claude desktop app, and Logi Options+ with the keypad connected. If one is missing, stop and tell the user.
2. `dotnet --version` must be 10 or later. If not: `brew install --cask dotnet-sdk`.
3. If `LogiPluginTool` is missing: `dotnet tool install --global LogiPluginTool`. Make sure `~/.dotnet/tools` is on `PATH`.
4. Build from `ClaudeKeysPlugin/`: `dotnet build`. It doesn't copy the plugin: it links Logi's Plugins folder to this repo's `bin/` and reloads it. So the repo must stay where it is.
5. Check the log (path under Build) for a clean load. If keys draw but do nothing in Claude, ask the user to check that Logi Plugin Service is allowed under Privacy & Security > Accessibility.
6. You can't place keys. Tell the user to drag Session 1–6, Dictate, Return and Cancel onto the keypad in Logi Options+, in any layout.
7. Offer to add the session-name tip from the README to `~/.claude/CLAUDE.md`. Ask first: it's the user's file.
8. To uninstall: `dotnet clean` in `ClaudeKeysPlugin/` removes the link.

## Code map (`ClaudeKeysPlugin/src/`)
- `ClaudeSessionStore.cs`: reads every session's status every 2 seconds. All Claude file knowledge stays here.
- `CodexThreads.cs`: the same for Codex. All Codex file knowledge stays here.
- `ClaudeTranscripts.cs`: step count (2/4) for busy sessions, from their task list.
- `ClaudeApp.cs`: everything that acts on the Claude window (links, shortcuts, paste, palette).
- `ReplyMode.cs`: the hold pages for Return, Cancel and Session keys.
- `KeyArt.cs`: draws every key.
- `SessionKeyLocks.cs`: saves which session is on which key.
- `Actions/`: one file per key.

## Claude (undocumented, can change with any update)
- `~/.claude/sessions/<pid>.json`: live sessions: `status` (busy, idle, waiting), `waitingFor`, `pid`.
- `~/Library/Application Support/Claude/claude-code-sessions/*/*/local_*.json`: `title`, `isStarred`, `isArchived`, `bridgeSessionIds`, `lastFocusedAt`. `btwLoweredFrom` marks a side chat.
- `~/Library/Logs/Claude/main.log`: `setFocusedSession` shows the open session instantly. The JSON files lag about a second.
- Links: `claude://claude.ai/code/<bridgeSessionId>` opens a session, `claude://code/new?source=desktop_action` starts one.
- Shortcuts: Dictate `Cmd+D`, Accept `Cmd+Return`, Fork `Cmd+Opt+O`, Archive `Cmd+Opt+A`. `/btw <text>` opens a side chat, and dictation doesn't work inside one.

## Codex (undocumented)
- Threads: `~/.codex/state_5.sqlite`. Logs: `~/Library/Logs/com.openai.codex`.
- Open a thread: `codex://threads/<id>`.

## Logi SDK gotchas
- Keys are 116×116. `IsWidget = true` removes the caption so the image fills the key.
- One-colour SVGs turn white, so icons carry an invisible second colour.
- LongPress fires at about 0.5 seconds. Act on Release when a key also has a hold.
- Dynamic folders can't be opened from code, so the hold pages redraw our own keys instead.
