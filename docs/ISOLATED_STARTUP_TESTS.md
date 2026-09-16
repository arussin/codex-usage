# Independent Windows startup investigation

## Result — 2026-09-16

An unattended Windows test lab is now available on the diagnostic branch. The
single EXE was independently rebuilt on two hosted architectures and **matches
the existing preview.2 executable byte for byte**. No release was published.
The separate test PC's real sign-in failure remains unresolved.

[Completed run and reports](https://github.com/arussin/codex-usage/actions/runs/35117763781)
 · [Lab scripts](../tools/startup-lab/README.md)
 · [Separate-PC acceptance evidence](SINGLE_EXE_ACCEPTANCE.md)

| Hosted environment | Package | Normal app and refresh checks | Windows dispatcher experiment |
| --- | --- | --- | --- |
| Windows 11 Enterprise 26200.9168 Arm64; x64 emulation | Single EXE | Passed | Candidate and control launched |
| Same Windows 11 image | Folder build | Passed | Candidate and control launched |
| Windows Server Datacenter 26100.33296 x64 | Single EXE | Passed | Inconclusive: neither control nor candidate observed |
| Same Windows Server image | Folder build | Passed | Inconclusive: neither control nor candidate observed |

## What ran

The functional checks used the app's normal entry point and a local synthetic
Codex CLI. They covered export off by default, opted-in export, a second instance
exiting without disturbing the first, a real five-minute timer refresh error,
unchanged snapshot bytes/reading time/file timestamp on that error, and a fresh
reading after relaunch. All four runs passed their 19 harness checks (including
preconditions) and the application's existing self-test suite. Timer errors were
observed 300.7–301.9 seconds after launch. No real account or phone was used.

The separate dispatcher experiment registered a quoted candidate command and
a harmless control in the disposable user's Run key. It manually invoked
`runonce.exe /AlternateShellStartup`, a command documented by Microsoft for
[RemoteApp sessions](https://learn.microsoft.com/en-us/troubleshoot/windows-server/remote/application-not-start-in-remoteapp-session).
This was an experiment in an already signed-in ordinary session, **not a new
Windows sign-in** and not a proposed installation workaround.

On both Windows 11 jobs, the exact candidate path was observed after about two
seconds, one process-creation event was received, the control also ran, and the
candidate remained alive at least 15 seconds afterward. On Windows Server,
neither entry ran during 90 seconds. That failed control makes the Server
experiment inconclusive. A zero dispatcher exit code was not counted as an
application pass. Both temporary registry values were removed in every job.

## Build identity

- Single-EXE source: `14f8e8f22605e35464cac6d2cf614cbbccc57d68`.
- Folder source: `ade5f362abcab194e90678a15bd14f56c9bd29cf`.
- SDK 10.0.401, runtime 10.0.12, self-contained `win-x64`, original publish flags.
- Single EXE SHA-256 on both hosts:
  `5b46e1cf80690e57699c7dfc3a2277103a5fc855a90ed37e004016956e26740a`.
- The ZIP container was not rebuilt or claimed byte-identical. The folder
  executable alone matches; that is not an attestation for every folder file.

The first source-build run passed, but its single EXE differed because hosted
checkout used CRLF while the original build used LF. All 15 input files matched
after line-ending normalization. Pinning LF in the disposable build copy then
reproduced the original EXE hash. Application logic was unchanged.

## Interpretation and limits

The exact unsigned EXE can run normally and through the tested Windows 11 Run
dispatcher path. This does not exclude a trust decision, policy, account-context
difference, delay or path-availability problem during the separate PC's sign-in.
The lab uses administrator privileges with UAC disabled, a different patch level
and, for Windows 11, a different CPU architecture. Its startup command was 51
characters; the reported separate-PC command was 128. No sign-out, reboot,
security-policy change, visual acceptance or fresh phone check was performed.

Static reads of the original package binaries found identical Windows manifests:
`asInvoker`, `uiAccess=false`, x64 GUI subsystem and identical DLL characteristics.
Both are unsigned. This rules out a changed elevation requirement in the
single-EXE manifest; it does not establish a security-blocking cause.

The existing separate-PC trace is still the direct evidence for that incident:
Windows created no candidate process in the observed sign-in window. Further
analysis of its startup enumeration/delay stages and exact-window enforcement
events remains pending; no repeat manual acceptance checklist is justified.

The lab has read-only GitHub permissions. Attempts to read draft assets with that
permission stopped before app execution; an automatic approval review rejected
broader write access. The completed route rebuilt public source instead. No
personal credentials, readings, endpoints, screenshots, raw traces or binaries
were uploaded. Existing installed files, processes, startup state and networking
were unchanged; download releases remain drafts.
