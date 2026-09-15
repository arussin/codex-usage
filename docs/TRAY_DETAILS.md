# Codex Usage Tray

A lightweight Windows tray app for Codex quotas, with an optional JSON feed for widgets and other consumers.

Based on [Tooblippe/codex-usage](https://github.com/Tooblippe/codex-usage). The proposed `arussin/codex-usage` repository must be created as a real GitHub fork, preserving upstream history and its [MIT license](LICENSE).

## Get started

**Release draft:** this candidate is built and tested locally but has not been published. Once approved, download the complete self-contained Windows x64 ZIP from the fork's verified release. Users do not need Git, an SDK, or compilation.

1. Extract the entire build into a stable folder and run `CodexUsageTray.exe`.
2. Have a working Codex CLI session signed in on this Windows account. The app prefers the desktop-bundled CLI and otherwise uses `codex` on PATH.
3. Left-click the tray number for details; right-click for Refresh and settings.

## Optional JSON export

Export is **off by default**. Right-click → **JSON export → Enable export**. Choose **Choose output file…** to change the destination.

The compatibility default is `%LOCALAPPDATA%\CodexUsagePhone\usage.json`. Export follows the existing five-minute refresh, including startup and manual refreshes. No extra polling or server is added. Old files retain their timestamps after failures or disabling; consumers must check freshness.

[Export contract and settings](JSON_EXPORT.md) · [Upstream usage and maintainer build guide](UPSTREAM_GUIDE.md)

The independent `arussin/kwgt-cyberdeck-status` project holds the companion widgets and guides. Tray source, patches, and binaries stay in this fork.
