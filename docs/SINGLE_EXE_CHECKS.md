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

## Separate-PC acceptance

The exact package passed normal launch, user-confirmed readings/layout, retained
export settings and custom path, manual refresh, disabled-export preservation,
and return to the preserved folder build. The test account was restored to the
original folder version with startup off.

**Startup acceptance remains open.** One automatic launch used a temporary ZIP
copy while the Run entry targeted the permanent candidate. The first missed
startup and UI cleanup behavior remain unresolved. See the
[separate-PC report and focused retest](SINGLE_EXE_ACCEPTANCE.md).

Additional isolated diagnostics exercised the exact folder and single-EXE
packages with synthetic files: 59 assertions passed across upgrade, rollback
and reopening. Both generated the correct startup command for their running
EXE. These diagnostics exited before normal tray startup and do not resolve
the separate-PC sign-in findings.

Candidate-to-phone and Android synthetic-state acceptance remain separate
checks in the [release checklist](RELEASE_CHECKS.md).
