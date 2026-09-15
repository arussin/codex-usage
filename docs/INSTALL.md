# Install or update

**Release candidate: final interactive checks are pending.**

1. Download the complete Windows x64 ZIP and checksum from this fork's release. Keep all runtime files together; do not copy only the EXE.
2. New install: extract to a stable directory, such as `%LOCALAPPDATA%\Programs\CodexUsageTray`.
   Updating: note the existing location/startup setting, explicitly exit the old tray, and back up the whole old installation before replacing anything. Never overwrite a running app.
3. Run `CodexUsageTray.exe`. The selected Codex CLI must already be authenticated. Right-click → **JSON export → Enable export**.
4. Confirm `%LOCALAPPDATA%\CodexUsagePhone\usage.json` exists and its timestamp advances after a successful Refresh. Export is off by default; choosing a file path does not enable it.
5. Connect a consumer with the [widget setup guide](https://github.com/arussin/kwgt-cyberdeck-status/blob/main/docs/CODEX_SETUP.md). Reuse existing Tailscale mappings; never reset another service.

For startup, use the tray's **Start with Windows** control after verifying the stable installation path. No release script changes startup registration or installs the new app automatically.

**Rollback:** exit the new build, restore the complete old installation, and restore startup only if you changed it. The recovered old build exports unconditionally and ignores the new opt-in preference. Disabling the new build does not delete a previously served file; consumers must check its age.
