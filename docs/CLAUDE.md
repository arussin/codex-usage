# Claude quota

The tray can show Claude plan limits beside Codex limits, or instead of them. Claude is off until you tick one of its tray numbers.

## Requirements

- A Claude subscription plan. API-key and gateway sign-ins have no plan limits to show.
- The Claude Code CLI, signed in on this Windows account. Run `claude auth login` once and complete the browser sign-in.

The app looks for `claude.exe` in this order: `%USERPROFILE%\.local\bin`, the folders on PATH, then the newest copy bundled with the Claude desktop app under `%APPDATA%\Claude\claude-code`. The desktop app's copy is signed in only while the desktop app runs it, so it still needs `claude auth login` once. An npm `claude.cmd` shim is not used.

## Turn it on

Right-click any tray number → **Tray numbers**, then tick what you want to watch.

| Tray number | Shows | Bar under the digits |
| --- | --- | --- |
| Codex weekly | Weekly Codex allowance remaining | None (the original icon) |
| Codex 5-hour | Five-hour Codex allowance remaining | Grey, half width |
| Claude 5-hour | Current Claude session window remaining | Orange, half width |
| Claude weekly | Weekly Claude allowance, all models | Orange, full width |
| Claude model weekly | The per-model weekly limit closest to running out | Orange, split |

Hover over a number to read its name and value. Left-click a number to open that source's popup. The Claude popup lists every per-model weekly limit on its own line.

Weekly digits are green while you are on or ahead of an even daily pace and red when behind it. Five-hour digits turn red at 20% remaining or less. Grey digits mean there is no current reading.

Windows places new tray icons in the overflow area. Drag a number onto the taskbar to keep it visible.

## Switching a source off

A source is polled only while at least one of its tray numbers is ticked. Untick all Codex numbers and the app stops calling the Codex CLI, stops updating `usage.json`, and removes the Codex icons. The same applies to Claude. One number always stays, because the menu is only reachable through an icon.

Each source has its own reader, history, alerts and export file. A failed read of one source never changes the other's numbers.

## How the numbers are read

Every five minutes, and on **Refresh** or when a popup opens, the app runs:

```text
claude -p /usage --output-format stream-json --verbose --no-session-persistence --strict-mcp-config --setting-sources ""
```

`/usage` is a local Claude Code command. It makes no model request and uses none of your allowance. The app reads the structured `usage_report` in the output: the `session`, `weekly_all` and `weekly_scoped` rows, each with a percentage used and a reset time. The flags keep the poll from saving a session, running your hooks or plugins, or starting MCP servers.

The app never reads Claude's stored credentials. It only runs the CLI, the same way it runs the Codex CLI.

Claude Code marks the `usage_report` format as experimental. If a Claude Code update changes it, the Claude numbers show `--` and the popup explains why; Codex is unaffected.

## JSON export

With **JSON export** enabled, Claude's readings are written to `claude.json` in the same folder as the configured output file. It uses the [same contract](JSON_EXPORT.md) as `usage.json`: `fiveHour`, `weekly` (all models) and `refreshedAt`. Per-model weekly limits are not exported.

An existing private file-serving setup for that folder serves `claude.json` at the same address with the new file name.

## What the states mean

| You see | Meaning |
| --- | --- |
| `--` and "Claude Code reported no plan usage" | The CLI is signed out or signed in with an API key. Run `claude auth login`. |
| `--` and "could not fetch plan limits right now" | Claude Code's own usage request failed or was throttled. The next poll retries. |
| `--` and "Claude Code was not found" | No `claude.exe` in the places listed above. |
| A number with a red digit colour | Low or behind pace, as described above. |
