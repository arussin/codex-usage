# Single-EXE Windows acceptance

Reviewed on 2026-09-16 from the completed separate-PC test report. **Controlled
Windows startup failed; the cause is unknown.** Normal launch, layout, export,
rollback and disabling startup through the menu passed. The download remains a
draft and has not passed full acceptance.

## Package identity

- Source: `14f8e8f22605e35464cac6d2cf614cbbccc57d68`.
- ZIP SHA-256: `cd1096c1c02d64aede3c67e96028ddb26e485e076951d3f8355a0b575cdbbf58`.
- EXE SHA-256: `5b46e1cf80690e57699c7dfc3a2277103a5fc855a90ed37e004016956e26740a`.
- The report verifies the downloaded ZIP and EXE checksums and four extracted
  files. The earlier 273-file folder package was preserved and backed up in
  full. Testing used a separate Windows x64 PC at 150% display scaling.

## Results reported from the test PC

| Check | Result |
| --- | --- |
| Normal launch, readings and popup layout | PASS; candidate process verified and user confirmed the UI |
| Existing export preference and custom path | PASS; retained settings and valid export at the saved destination |
| Manual refresh | PASS; valid reading and file modification time advanced |
| Export disabled, followed by refresh | PASS; file hash and modification time unchanged |
| Controlled startup after sign-in | FAIL; verified permanent-path candidate and matching Run entry, no tray running before sign-out, no tray process or fresh export observed after sign-in |
| Disable startup through the verified candidate menu | PASS; Run entry absent afterward, without registry editing during controlled cleanup |
| Return to preserved folder build | PASS; original files matched backup, one original process exported to the saved destination and user confirmed readings |
| Final test-account settings | PASS; original folder build, original export preference/path and startup off |

An initial sandboxed process could not access normal settings and was excluded
from acceptance. Subsequent acceptance launches used the normal Windows account.

## Controlled startup failure

The extracted candidate's identity and permanent process path were checked.
The Run entry matched that EXE. All tray instances were exited before sign-out.
After sign-in, and again approximately two minutes after Explorer started,
inspection found no tray process and no new export. The Run entry remained
correct. No matching StartupApproved entry or tray-specific Application error
was found in the inspected locations and events.

The missing events do not prove that no other error occurred. The evidence does
not distinguish Windows declining or delaying launch from a process that
started and exited before observation. It also does not establish whether the
cause is the package, app startup code or test-account environment.

The candidate was then launched manually solely for cleanup. The user disabled
startup through that verified copy's menu, and the test agent confirmed the Run
entry was absent without editing the registry. That manual launch is not an
automatic-startup pass. The original build and settings were restored afterward.

## Earlier temporary-copy observation

An earlier attempt missed startup and was followed by a manual launch from the
ZIP. At the next sign-in, a matching candidate appeared automatically from a
temporary ZIP folder while its Run entry targeted the permanent candidate.
The launch mechanism remains unknown. During that earlier cleanup, the test
agent removed the remaining exact candidate Run entry.

The later controlled test supersedes that inconclusive acceptance result.
Cleanup from the permanent-path candidate now passes. The temporary EXE location
is separate from the expected native-runtime extraction inside the .NET cache.

## Remaining startup work

Investigate the failed controlled launch before requesting another acceptance
pass. The next diagnostic needs evidence of whether Windows creates the
candidate process at sign-in and, if it does, where startup exits. The existing
post-sign-in process checks and event queries do not answer that question.
Preserve the successful launch/export/layout/rollback results; repeat affected
checks if a corrective build changes their behavior.

## Evidence limits

These are reported separate-PC observations, not a new execution by this
reviewing task. Independently, both exact packages passed 59 isolated assertions
covering settings compatibility, upgrade/rollback, disabled/error snapshot
preservation and the generated startup command. Those diagnostics exited before
normal tray startup and do not override the failed sign-in check.

The report states that the main deployment, phone feed and Tailscale were
unchanged. No private hostnames, endpoints, readings, screenshots or raw logs
are included here. Candidate-to-phone and Android state checks remain separate
items in the [release checklist](RELEASE_CHECKS.md).
