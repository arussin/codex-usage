# Single-EXE Windows acceptance

Reviewed on 2026-09-16 from the separate-PC acceptance and controlled trace
reports. **Windows did not create the candidate process during the observed
startup window; the reason remains unknown.** Normal launch, layout, export,
rollback and menu cleanup passed. The download remains a draft and has not
passed full acceptance.

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

Those initial checks could not distinguish an omitted or delayed launch from
a short-lived process. The later controlled trace below resolves that question
for its captured window, while leaving the reason for non-launch unresolved.

The candidate was then launched manually solely for cleanup. The user disabled
startup through that verified copy's menu, and the test agent confirmed the Run
entry was absent without editing the registry. That manual launch is not an
automatic-startup pass. The original build and settings were restored afterward.

## Controlled process trace

The separate-PC report records these results using the unchanged preview.2 EXE:

- Candidate checksum and exact quoted permanent-path registration verified;
  no tray instances running before sign-out.
- Capture/decode control passed: a harmless process's creation, executable
  identity and exit code 37 were decoded successfully.
- Continuous coverage from approximately 74 seconds before the new Explorer
  start through 11 minutes 20 seconds afterward; old Explorer exit and new
  Explorer creation both captured. Zero lost events or buffers reported.
- No candidate creation, candidate rundown or other tray copy observed before
  the labeled manual control. Interactive process checks also found no tray
  through 10 minutes 9 seconds after Explorer start.
- Manual creation at approximately 10 minutes 52 seconds after Explorer start
  was captured from a normal-user PowerShell parent. The full command path
  identified the permanent candidate. It remained present at the ending
  rundown approximately 28 seconds later. No actual candidate exit was captured;
  the rundown status field is not an exit code.
- The user then successfully used the candidate menu to disable startup and
  exit. Original build, export preferences and startup-off state were restored.
  Diagnostic recording was saved, decoded and confirmed stopped.

This supports **no automatic candidate launch observed during the verified
window**, rather than a demonstrated crash or duplicate-instance exit. It does
not explain Windows' selection or delay, exclude a launch after the window, or
establish that the startup registration will work reliably. Manual launch is a
positive control, not an automatic-startup pass. The next analysis concerns
Windows' startup processing; no corrective app change is justified yet.

The local review of earlier logs also identified three .NET Runtime 1023 errors
as missing `CodexUsageTray.dll` in temporary extraction of the old folder-build
ZIP. Both earlier Shell-Core launch pairs predated the controlled test and
contained basename-only commands. None establishes a failure of this candidate
during the controlled sign-in.

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

Inspect the already captured Shell-Core startup stages to determine whether
Windows enumerated the relevant Run list, progressed through its delayed work,
or recorded an execution failure. Compare relevant startup policy and account
context without changing them. The available report has not established a
specific policy, delay condition or registration defect.

Preserve the successful launch/export/layout/rollback results. Request another
sign-in only when a specific hypothesis or corrective change warrants it; repeat
affected app checks if a corrective build changes their behavior.

## Evidence limits

These are reported separate-PC observations, not a new execution by this
reviewing task. Raw traces remain local to the test PC; only the sanitized
results were supplied. Independently, both exact packages passed 59 isolated assertions
covering settings compatibility, upgrade/rollback, disabled/error snapshot
preservation and the generated startup command. Those diagnostics exited before
normal tray startup and do not override the failed sign-in check.

The report states that the main deployment, phone feed and Tailscale were
unchanged. No private hostnames, endpoints, readings, screenshots or raw logs
are included here. Candidate-to-phone and Android state checks remain separate
items in the [release checklist](RELEASE_CHECKS.md).
