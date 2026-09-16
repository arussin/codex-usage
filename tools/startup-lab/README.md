# Isolated Windows startup lab

This diagnostic workflow rebuilds the published source in disposable GitHub
Windows machines, using the original .NET SDK and runtime versions. It uses
synthetic Codex responses, with no account sign-in or personal data. The scripts
refuse to run on a personal PC or a self-hosted runner.

The matrix compares the folder package and single EXE on Windows Server x64 and
Windows 11 Arm (x64 emulation). Reports identify source commits, source-file and
executable hashes, SDK, runtime and actual OS. The single-EXE hash is compared
with the draft download; a rebuild is not assumed to be byte-for-byte identical.

Checks cover the existing self-tests against a synthetic CLI, the full normal
entry point, export off by default, opt-in export, duplicate-instance handling,
the real five-minute timer, preservation of old readings on error, and recovery
after relaunch. Only build metadata and the synthetic result are uploaded.

A separate experiment registers two temporary entries inside the disposable VM
and calls Windows' RemoteApp startup dispatcher. One entry is a harmless control;
the other starts the candidate. The report distinguishes a dispatcher no-op
from candidate launch and removes both entries afterward. This is not a sign-in
test or a proposed installation workaround.

This does **not** perform Windows sign-in, inspect the Surface, validate visual
layout, or test a phone. Hosted runners have a different security configuration.
A pass does not resolve the recorded Surface startup failure. Test processes
are terminated by the harness after observations.

The workflow has read-only repository permission and does not access draft
assets, upload binaries, create releases or publish downloads. It runs on the
diagnostic branch or by explicit dispatch. Each job has a 15-minute limit and
report retention is 14 days.
