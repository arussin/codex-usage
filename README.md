# Codex Usage Tray

A fork of **[Tooblippe’s Codex Usage Tray](https://github.com/Tooblippe/codex-usage)**. The original Windows tray app, usage popup, five-hour and weekly limits, reset times, and graph come from upstream.

**This fork adds optional JSON snapshot export** for widgets and other tools. It also includes CLI discovery and display-scaling fixes, plus a single-EXE download with the .NET runtime included.

## Get started

Requires **Windows x64** and a **signed-in Codex CLI** on the same Windows account. The app finds the desktop-bundled CLI or uses `codex` on PATH.

**[Download the Windows single-EXE preview](https://github.com/arussin/codex-usage/releases/tag/v0.1.0-json-export-preview.2).** The .NET runtime is included; no SDK or compilation is needed.

1. Extract the complete release ZIP into a stable folder.
2. Run `CodexUsageTray.exe`.
3. Left-click the tray number for details; right-click for **Refresh**, settings, or **Exit**.

[Installation and updates](docs/INSTALL.md) · [Tray controls](docs/TRAY_DETAILS.md)

## Optional JSON export

Export is **off by default**. Right-click → **JSON export → Enable export**. Use **Choose output file…** to change the destination.

Default file: `%LOCALAPPDATA%\CodexUsagePhone\usage.json`

The feed contains quota availability, remaining percentages, reset times, and a refresh timestamp. It updates with the tray's five-minute polling and manual refreshes. Failed refreshes leave the previous file and timestamp intact; consumers should check its age.

[JSON fields and settings](docs/JSON_EXPORT.md) · [Android widgets and setup](https://github.com/arussin/kwgt-cyberdeck-status)

## Credits

Original application by [Tooblippe](https://github.com/Tooblippe/codex-usage). Upstream history and the [MIT license](LICENSE) are retained.
