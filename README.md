# Codex Usage Tray

Keep your Codex quota visible in the Windows system tray. Open the popup for five-hour and weekly limits, reset times, and a weekly usage graph. Export a small JSON feed for widgets and other tools.

## Get started

Requires **Windows x64** and a **signed-in Codex CLI** on the same Windows account. The app finds the desktop-bundled CLI or uses `codex` on PATH.

**Windows downloads are not yet publicly available.** Check [Releases](https://github.com/arussin/codex-usage/releases) for availability. Builds include the .NET runtime; no SDK or compilation is needed.

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

Forked from [Tooblippe/codex-usage](https://github.com/Tooblippe/codex-usage), with optional JSON export, CLI discovery improvements, and display-scaling fixes. Licensed under [MIT](LICENSE).
