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
- Releases: Actions > Make release publishes the version in `LoupedeckPackage.yaml` as a `.lplug4`, then bumps that file for next time. Pull afterwards.
- GitHub builds skip the Logi link and reload step (`CI` is true there).

## Installing for a user
1. Check the basics: macOS, the Claude desktop app, and Logi Options+ signed in to a Logi account (plugins can't be added without one) with the keypad connected. If one is missing, stop and tell the user.
2. `dotnet --version` must be 10 or later. If not: `brew install --cask dotnet-sdk`.
3. If `LogiPluginTool` is missing: `dotnet tool install --global LogiPluginTool`. Make sure `~/.dotnet/tools` is on `PATH`.
4. Build from `ClaudeKeysPlugin/`: `dotnet build`. It doesn't copy the plugin: it links Logi's Plugins folder to this repo's `bin/` and reloads it. So the repo must stay where it is.
5. Check the log (path under Build) for a clean load. If keys draw but do nothing in Claude, ask the user to check that Logi Plugin Service is allowed under Privacy & Security > Accessibility.
6. You can't place keys. Walk the user through "2. Place the keys" in the README.
7. Offer to add the session-name tip from the README to `~/.claude/CLAUDE.md`. Ask first: it's the user's file.
8. To uninstall: `dotnet clean` in `ClaudeKeysPlugin/` removes the link.

## Code map (`ClaudeKeysPlugin/src/`)
- `ClaudeSessionStore.cs`: reads every session's status every 2 seconds. All Claude file knowledge stays here.
- `CodexThreads.cs`: the same for Codex. All Codex file knowledge stays here.
- `ClaudeTranscripts.cs`: step count (2/4) for busy sessions, from their task list.
- `ClaudeUsage.cs`: usage and pace for the Usage key. All usage knowledge stays here.
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
  Bridge IDs exist only with Remote Control on (it can be disabled by policy), so without one the plugin opens
  `claude://code/continue?session=<local_id>&source=desktop_action`.
- Usage: `GET https://api.anthropic.com/api/oauth/usage` with the CLI's token (Keychain item `Claude Code-credentials`, `claudeAiOauth.accessToken`)
  and header `anthropic-beta: oauth-2025-04-20`. Spend-limit accounts fill `spend` (`limit` and `used` in `amount_minor` with `exponent`,
  month resets 00:00 UTC on the 1st). Plans fill `limits[]` (`percent`, `severity`, `resets_at`, `is_active`). `claude -p /usage` refreshes an
  expired token without calling a model; `claude auth status` doesn't. Never print or log the token.
- Shortcuts: Dictate `Cmd+D`, Accept `Cmd+Return`, Fork `Cmd+Opt+O`, Archive `Cmd+Opt+A`. `/btw <text>` opens a side chat, and dictation doesn't work inside one.

## Codex (undocumented)
- Threads: `~/.codex/state_5.sqlite`. Logs: `~/Library/Logs/com.openai.codex`.
- Open a thread: `codex://threads/<id>`.
- Dictation outside Claude: ChatGPT's single-tap shortcut, which the user sets to `Ctrl+Opt+Cmd+D`. Never hold a modifier key down from the plugin: a lost key-up left Control stuck system-wide (2026-11-15).
- ChatGPT's dictation records as the process `Codex (Aperitif)`. `Microphone.cs` asks Core Audio whether it's recording (macOS 14.2 or later), to choose the start or stop sound and to make Return stop dictation.

## Logi SDK gotchas
- Keys are 116×116. `IsWidget = true` removes the caption so the image fills the key.
- One-colour SVGs turn white, so icons carry an invisible second colour.
- LongPress fires at about 0.5 seconds. Act on Release when a key also has a hold.
- Dynamic folders can't be opened from code, so the hold pages redraw our own keys instead.
