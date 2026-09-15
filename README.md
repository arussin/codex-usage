# Codex Usage Tray + JSON export

**Windows tray quota monitor with an optional status feed for other tools.**

This is a direct fork of **[Tooblippe/codex-usage](https://github.com/Tooblippe/codex-usage)**. Upstream history and its MIT license are retained. Adam's additions provide default-off JSON export, CLI-discovery improvements, and display-scaling fixes. [Detailed tray documentation](docs/TRAY_DETAILS.md).

## Use it

The self-contained Windows x64 build is being prepared under [Releases](https://github.com/arussin/codex-usage/releases). **The first exporter build remains a draft until the final Windows and phone checks pass.** Do not use an unrelated upstream download expecting this exporter.

After a release is available:

1. Extract the entire ZIP to a stable folder and run `CodexUsageTray.exe`. A working, signed-in Codex CLI is required; no .NET SDK is needed to run the release.
2. Right-click the tray → **JSON export → Enable export**.
3. Read `%LOCALAPPDATA%\CodexUsagePhone\usage.json`, or choose another dedicated status destination.

For my Android widgets: **[Cyberdeck Status for KWGT](https://github.com/arussin/kwgt-cyberdeck-status)** → [three-step setup](https://github.com/arussin/kwgt-cyberdeck-status/blob/main/docs/CODEX_SETUP.md).

The tray uses its existing refresh cycle. Export failures preserve the old file and timestamp. Disabling export does not delete that file or stop any sharing service.

[JSON contract](docs/JSON_EXPORT.md) · [Safe update/rollback](docs/INSTALL.md) · [Release checks](docs/RELEASE_CHECKS.md) · [MIT license](LICENSE)
