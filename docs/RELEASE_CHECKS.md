# Release verification

Status on 2026-09-16: Windows downloads remain **draft prereleases**.

- Folder preview `.1`: source `ade5f362abcab194e90678a15bd14f56c9bd29cf`;
  the separate-PC Windows acceptance below applies to this package.
- Single-EXE preview `.2`: source `14f8e8f22605e35464cac6d2cf614cbbccc57d68`;
  [packaging checks](SINGLE_EXE_CHECKS.md) and separate-PC launch, layout,
  export and rollback checks passed. **Controlled Windows startup failed**
  despite a verified permanent-path EXE and matching Run entry. The cause is
  unknown. Disabling startup through the menu passed.
  [Detailed results](SINGLE_EXE_ACCEPTANCE.md). The application C# source is
  unchanged from preview `.1`.

Both packages retain their own checksums and acceptance scope. The folder
preview remains available to its owner while the new packaging is checked.

## Verified

- GitHub identifies this repository as a direct fork of
  [Tooblippe/codex-usage](https://github.com/Tooblippe/codex-usage).
  Upstream history and license are retained.
- The self-contained Windows x64 build, self-tests and formatting checks passed
  with .NET SDK 10.0.401. The packaged runtime is 10.0.12.
- Downloaded draft assets matched their SHA-256 checksums and passed ZIP integrity
  checks. The Windows ZIP contains the complete runtime and license.
- The exact folder preview passed the exercised acceptance checks on a separate Windows
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

- [x] Verify the exact single-EXE preview's normal launch, popup/usage, retained
  export settings, manual refresh and disabled-export behavior on a separate PC.
- [x] Switch from the preserved folder package to the single EXE and back; verify
  readings and the saved export destination. This used separate package folders.
- [x] Verify that disabling startup through the permanent-path candidate's
  menu removes its Run entry; restore the original test-account settings.
- [ ] Diagnose and resolve the failed controlled single-EXE startup, then verify
  automatic launch at sign-in. The completed test failed; it is no longer an
  unperformed check. See [startup findings](SINGLE_EXE_ACCEPTANCE.md).
- [ ] Connect a phone to that candidate export through its ordinary private
  hostname and confirm reads across real file replacements.
- [ ] Confirm Android stale, no-data, unavailable, low, exhausted and FULL states
  using a separate synthetic feed; preserve the working feed.
- [ ] Record acceptance and approve publication before making the draft public.

See [installation and rollback](INSTALL.md), [JSON behavior](JSON_EXPORT.md),
and [widget checks](https://github.com/arussin/kwgt-cyberdeck-status/blob/main/docs/RELEASE_CHECKLIST.md).
Maintainers can use the [isolated build instructions](BUILD.md).
An upstream PR is a separate decision; the complete candidate also contains
earlier CLI-discovery and DPI changes.
