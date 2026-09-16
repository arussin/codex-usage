# Single-EXE packaging verification

Checked on 2026-09-16. The new package is a draft; the earlier folder preview
and its [Windows acceptance results](WINDOWS_ACCEPTANCE.md) are retained.

## Package

- Source: `14f8e8f22605e35464cac6d2cf614cbbccc57d68`.
- SDK: .NET 10.0.401; bundled .NET/Windows Desktop runtime: 10.0.12.
- EXE: 51,682,622 bytes; SHA-256
  `5b46e1cf80690e57699c7dfc3a2277103a5fc855a90ed37e004016956e26740a`.
- ZIP: 46,050,670 bytes; SHA-256
  `cd1096c1c02d64aede3c67e96028ddb26e485e076951d3f8355a0b575cdbbf58`.
- Four files: `CodexUsageTray.exe`, `LICENSE`, `README.md`, `PROVENANCE.json`.
  The earlier folder ZIP contained 273 files and was 49,885,155 bytes.
- README: 185 words. No user data, credentials, logs or private endpoints are
  included. Runtime components are bundled without trimming.

## Checks performed

- Application C# and project files match the earlier folder preview. Packaging
  properties are supplied by the build helper; source paths in debug metadata
  are mapped to a generic path.
- Isolated restore, build and publish succeeded with zero build warnings/errors.
- The published EXE passed the existing self-tests and formatting checks.
  Tests cover JSON export/preferences, file replacement/failure behavior,
  programmatic popup sizing/reopening, an isolated temporary startup registry
  value and a live signed-in CLI read.
- The same EXE was copied alone into a separate folder whose name contains
  spaces. It passed the complete self-tests using default native-library cache
  extraction, with no companion DLLs in that folder.
- ZIP integrity and every packaged file's bytes were checked. The packaged EXE
  matches the tested EXE exactly.
- Before/after checks matched the installed tray binaries, running process,
  live startup entry and Tailscale Serve configuration. No normal tray instance
  was started by these packaging tests.

The build uses [single-file settings](BUILD.md). Native runtime files still
extract automatically into the per-user .NET cache; the smaller visible file
count does not remove those runtime dependencies.

## Separate-PC check still needed

On the separate test PC/account, exit the earlier test tray and keep its full
folder as a backup. Extract the new ZIP into another stable folder, then check:

1. Normal launch displays usage and the popup fits.
2. The existing export preference/path is retained; Refresh advances the output
   timestamp. Disabling export keeps the old timestamp unchanged. Restore the
   recorded export preference/path after this check.
3. Start with Windows launches this EXE after sign-in. Turn that test startup
   setting off afterward, or restore its recorded prior value.
4. Exit the new version, launch the saved folder version, and verify readings
   and the recorded export settings. This checks rollback to the tested folder
   preview on that account. Restore the chosen test version/settings afterward.

These checks must not replace the live phone-feed deployment. Record any failure
or skipped item. Candidate-to-phone and Android synthetic-state acceptance remain
separate checks in the [release checklist](RELEASE_CHECKS.md).
