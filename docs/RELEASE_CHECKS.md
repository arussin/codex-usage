# Release verification

Status on 2026-09-16: the Windows download remains a **draft prerelease**.
Its source commit is `ade5f362abcab194e90678a15bd14f56c9bd29cf`.
Later changes on `main` update documentation only.

## Verified

- GitHub identifies this repository as a direct fork of
  [Tooblippe/codex-usage](https://github.com/Tooblippe/codex-usage).
  Upstream history and license are retained.
- The self-contained Windows x64 build, self-tests and formatting checks passed
  with .NET SDK 10.0.401. The packaged runtime is 10.0.12.
- Downloaded draft assets matched their SHA-256 checksums and passed ZIP integrity
  checks. The Windows ZIP contains the complete runtime and license.
- The exact draft passed the exercised acceptance checks on a separate Windows
  x64 PC at 150% scaling: normal launch and usage, popup layout, default-off and
  enabled export, custom destination, manual/five-minute refresh, enabled and
  disabled preferences across restart, and startup after real sign-in.
  [Detailed results and evidence limits](WINDOWS_ACCEPTANCE.md).
- An isolated test app exercised the production tray menu and export controller
  with synthetic readings and separate files:
  - Export starts off and creates no snapshot until enabled.
  - Enabling export writes a newly fetched reading.
  - The file picker saves a custom destination and subsequent readings use it.
  - Manual refresh advances `refreshedAt`.
  - The unchanged five-minute timer fetched again after 300.002 seconds.
  - A simulated fetch failure preserves the previous file byte-for-byte.
  - The enabled preference and custom path survive restart.
  - Disabling export preserves the old file; later reads and restart do not
    rewrite it or advance its timestamp.
- The popup was visually inspected with synthetic available readings. Main
  labels, bars, buttons and footer fit at the tested display scaling.
- A user imported the public Phosphor Classic Android preset, configured Source,
  and confirmed it displayed live usage. This used the existing deployed
  exporter; it is not an end-to-end test of the draft Windows binary.

## Limits of the isolated test

The test app redirects application data, replaces quota reads and startup
registration with fixtures, and uses a separate entry point. Production menu,
popup rendering and export-controller source are unchanged. Test-only window
ownership, taskbar visibility and placement allow inspection of the popup.

The isolated checks establish behavior of that source in isolation. Normal
launch, usage and startup of the exact binary were separately exercised in the
[Windows acceptance test](WINDOWS_ACCEPTANCE.md). Neither test covers every
Windows configuration or display scale.

## Remaining acceptance checks

- [ ] Verify the documented update/rollback procedure in a separate environment
  with an older installation. The tested fresh installation had no older version;
  rollback remains NOT TESTED.
- [ ] Connect a phone to that candidate export through its ordinary private
  hostname and confirm reads across real file replacements.
- [ ] Confirm Android stale, no-data, unavailable, low, exhausted and FULL states
  using a separate synthetic feed; preserve the working feed.
- [ ] Record acceptance and approve publication before making the draft public.

See [installation and rollback](INSTALL.md), [JSON behavior](JSON_EXPORT.md),
and [widget checks](https://github.com/arussin/kwgt-cyberdeck-status/blob/main/docs/RELEASE_CHECKLIST.md).
An upstream PR is a separate decision; the complete candidate also contains
earlier CLI-discovery and DPI changes.
