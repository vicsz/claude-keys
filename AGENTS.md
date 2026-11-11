# Claude Keys: agent notes

C# Logi Actions plugin for the MX Keypad (3×3 keys, 2 page buttons). Read this before changing code.

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

## Code map (`ClaudeKeysPlugin/src/`)
- `ClaudeSessionStore.cs`: reads every session's status every 2 seconds. All Claude file knowledge stays here.
- `CodexThreads.cs`: the same for Codex. All Codex file knowledge stays here.
- `ClaudeTranscripts.cs`: step count (2/4) for busy sessions, from their task list.
- `ClaudeApp.cs`: everything that acts on the Claude window (links, shortcuts, paste, palette).
- `ReplyMode.cs`: the hold pages for Go, Stop and Session keys.
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
