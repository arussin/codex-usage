# Single-EXE Windows acceptance

Reviewed on 2026-09-16 from a supplied separate-PC test report. The exact
single-EXE preview passed launch, layout, export and rollback checks. **Startup
acceptance remains open.** The download remains a draft.

## Package identity

- Source: `14f8e8f22605e35464cac6d2cf614cbbccc57d68`.
- ZIP SHA-256: `cd1096c1c02d64aede3c67e96028ddb26e485e076951d3f8355a0b575cdbbf58`.
- EXE SHA-256: `5b46e1cf80690e57699c7dfc3a2277103a5fc855a90ed37e004016956e26740a`.
- The report verifies the downloaded ZIP checksum, four extracted files and the
  EXE checksum against bundled provenance. The earlier folder package was
  preserved and backed up in full.

## Results reported from the test PC

| Check | Result |
| --- | --- |
| Normal launch, readings and popup layout | PASS; candidate process verified and user confirmed the UI |
| Existing export preference and custom path | PASS; normal-account settings read and export update agreed |
| Manual refresh | PASS; valid reading and file modification time advanced |
| Export disabled, followed by refresh | PASS; file hash and modification time were unchanged |
| Automatic launch after sign-in | INCONCLUSIVE for acceptance; second attempt launched the correct binary once, but from a temporary ZIP extraction instead of the registered stable location |
| Return to preserved folder build | PASS; backed-up files matched, original build ran once and updated the same destination; user confirmed readings |
| Final test-account settings | PASS; final report records the original folder build, original export preference/path and startup off |

The report contains intermediate statements that the candidate was still
running. Its later rollback verification and final user confirmation establish
the reported final state above. An initial sandboxed process could not read
settings and was stopped; it is excluded from normal-launch acceptance.

## Startup findings

The first sign-in required manual launch. The second started a single instance
automatically; its EXE matched the candidate checksum. Its process path was a
temporary ZIP extraction, while the inspected Run entry targeted the permanent
candidate folder. Explorer was the parent. The inspected Run/RunOnce and named
Startup-folder locations contained no additional matching entry. No matching
recent application crash event was found in the queried events.

These observations do not establish the cause of the first miss or the launch
mechanism of the temporary copy. In particular, app restoration is unproven.
The temporary EXE location must not be confused with native runtime extraction
inside the .NET cache.

After the attempted UI cleanup, the stable candidate's Run entry remained.
The test agent removed that exact entry and verified startup off. Therefore
successful cleanup through the temporary copy's UI was not established.

Source review shows that the startup checkmark compares the registered command
with the currently running EXE's path. A different copy can therefore appear
unchecked while another location remains registered. This explains a possible
misleading checkmark; it does not establish which menu actions occurred or why
the temporary copy launched. Disabling through `SetEnabled(false)` removes the
named entry. No application code was changed for this review.

## Remaining startup check

Use only the separate test account. Preserve the existing test build and record
its settings before proceeding.

1. Exit the test tray. Verify no tray process remains. Run the candidate from
   its fully extracted, permanent folder and verify the process path and hash.
2. Enable **Start with Windows** from that verified process. Confirm that its
   Run entry points to the same permanent EXE.
3. Exit the tray before signing out. After sign-in, inspect the process before
   manually launching anything. Confirm exactly one candidate at the permanent
   path, with current readings and export.
4. Turn startup off from that verified process and confirm the entry is absent.
   Restore the recorded test-account build and settings. Record any discrepancy
   without treating a temporary-copy launch as a pass.

## Evidence limits

The separate-PC results above are reported observations, not a new execution by
this reviewing task. Independently, both exact packages passed 59 isolated
assertions covering settings compatibility, upgrade/rollback, disabled/error
snapshot preservation and the generated startup command. Those diagnostics
used synthetic files and exited before normal tray startup; they cannot settle
the sign-in anomaly.

The report states that the main deployment, phone feed and Tailscale were
unchanged. No private hostnames, endpoints, readings, screenshots or raw logs
are included here. Candidate-to-phone and Android state checks remain separate
items in the [release checklist](RELEASE_CHECKS.md).
