# Install or update

Requires Windows x64 and a signed-in Codex CLI on the same Windows account. The app checks for the desktop-bundled CLI, then falls back to `codex` on PATH.

## Install

1. Get the complete Windows x64 ZIP and its checksum from [Releases](https://github.com/arussin/codex-usage/releases). Check the repository README for current download availability.
2. Use **Extract All** to unpack the ZIP into a stable directory, such as `%LOCALAPPDATA%\Programs\CodexUsageTray`. Open that extracted folder before running the app. No separate .NET installation is required.
3. Run `CodexUsageTray.exe`. Left-click the tray number to view your usage.
4. To start the app automatically at sign-in, right-click the tray and select **Start with Windows**.

### Package layouts

The **single-EXE** package bundles the app and .NET runtime in
`CodexUsageTray.exe`. Accompanying files provide the license, instructions and
build information. Windows automatically extracts native runtime components to
the per-user .NET temporary cache on first launch; keep that cache writable.

The earlier **folder** package has many DLLs beside the EXE. Keep all of them
together. Running that EXE inside the ZIP can leave required files such as
`CodexUsageTray.dll` missing. Always extract the complete folder package first.

## Enable JSON export

Right-click → **JSON export → Enable export**. Export starts off. **Choose output file…** selects a destination without enabling it.

The default file is `%LOCALAPPDATA%\CodexUsagePhone\usage.json`. After a successful **Refresh**, check that the file's `refreshedAt` timestamp advances. See the [JSON guide](JSON_EXPORT.md) for fields and error behavior, or the [Android widget setup](https://github.com/arussin/kwgt-cyberdeck-status/blob/main/docs/CODEX_SETUP.md) to connect a phone.

## Update

1. Note your installation folder, startup setting, and export destination.
2. Right-click the running tray → **Exit**.
3. Back up the complete installation folder, then replace its contents with the complete new build. When switching from the folder package to the single-EXE package, use the new package's contents without carrying over old runtime DLLs. Never replace files while the app is running.
4. Launch the app and check its readings and export settings. If you moved the installation, update **Start with Windows** to use the new location.

## Roll back

Exit the app, restore the complete backup to its previous location, then launch it. Check the restored version's export behavior and startup setting. Older versions may use different export defaults.

Disabling export does not delete a previously written file or stop a sharing service. Readers should continue to check the timestamp for freshness.
