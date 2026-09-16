# Windows acceptance results

Reviewed on 2026-09-16 from the tester's supplied report. The exact draft passed
the exercised launch, layout, export, persistence and startup checks on a
separate Windows x64 PC. Update/rollback was not tested. Phone integration was
outside this test's scope.

## Build and environment

- Draft: `v0.1.0-json-export-preview.1`.
- Source: `ade5f362abcab194e90678a15bd14f56c9bd29cf`.
- Archive: `CodexUsageTray-JSON-export-win-x64-preview.zip`.
- SHA-256: `47496a9ea1b2095d2357e460467ce82c5b13e8d8e6798451833ed78be39da086`.
- Reported environment: Windows 25H2, build 26200.9457, x64; 150% display scaling.
- The reported archive hash matches the draft release asset and the retained
  acceptance kit. The test report records 273 extracted files with no mismatch.

## Results

| Check | Result | Evidence recorded in the supplied report |
|---|---|---|
| Normal launch and usage | PASS | One responding process from the extracted folder; tester confirmed readings. |
| Popup layout | PASS | Tester confirmed text and buttons fit at 150% scaling. |
| Export off by default | PASS | Tester confirmed Enable export was initially unchecked. |
| Enable and default destination | PASS | A successful refresh produced JSON with `refreshedAt`. |
| Custom destination | PASS | Valid JSON was read from the selected test destination. |
| Manual refresh | PASS | `refreshedAt` and file modification time advanced after Refresh. |
| Previous destination | PASS | Tester confirmed its timestamp stopped advancing after changing paths. |
| Automatic refresh | PASS | A six-minute observation captured a five-minute timestamp advance. |
| Enabled preference/path after restart | PASS | Export remained enabled and the selected path received a startup reading. |
| Disabled preference after refresh/restart | PASS | Export remained disabled; file contents' timestamp and modification time stayed at the disabled baseline. |
| Start with Windows after sign-in | PASS | Tester confirmed startup; one responding process was observed and disabled export stayed unchanged. |
| Update/rollback | NOT TESTED | No older installation was established on the test machine. |

The tester performed tray-menu actions and confirmed layout; automated checks
on that machine inspected accessible files and processes. Native automation did
not expose the popup, and application-data access was restricted. The layout
result is tester-confirmed and covers only the reported display scaling.

## Installation finding

Launching directly inside the ZIP failed because the temporary extraction lacked
`CodexUsageTray.dll`. Running the EXE after extracting the complete archive
resolved it. The documented installation path is **Extract All**, then run the
EXE from the extracted folder. These observations did not establish a defect in
the fully extracted build.

## Cleanup and remaining scope

The tester confirmed Start with Windows was turned off after the test. Export
remained enabled at the custom test destination for the final manual-refresh
check. Cleanup of that remaining test instance is not claimed.

This test did not replace the existing deployment or configure a phone feed.
Candidate-to-phone reads across file replacements, Android synthetic data states
and update/rollback remain unverified. Download releases remain drafts pending
the [remaining acceptance checks](RELEASE_CHECKS.md#remaining-acceptance-checks).

Device names, private paths, personal readings and exact reading timestamps from
the original report are omitted here.
