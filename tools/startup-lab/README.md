# Isolated Windows startup lab

This diagnostic workflow runs the existing draft downloads in disposable GitHub
Windows machines. It uses synthetic Codex responses, with no account sign-in or
personal data. It cannot run on a personal PC or a self-hosted runner.

The matrix compares the folder package and single EXE on Windows Server x64 and
Windows 11 Arm (x64 emulation). ZIP and candidate hashes identify the actual
release files; the application is neither rebuilt nor instrumented.

Checks cover the existing self-tests against a synthetic CLI, the full normal
entry point, export off by default, opt-in export, duplicate-instance handling,
the real five-minute timer, preservation of old readings on error, and recovery
after relaunch. Only a compact synthetic result is uploaded. No release is
created or published, and no repository write permission is granted to the job.

This does **not** perform Windows sign-in, inspect the Surface, validate visual
layout, or test a phone. Hosted runners have a different security configuration.
A pass does not resolve the recorded Surface startup failure. Test processes
are terminated by the harness after observations.

The workflow runs only on the diagnostic branch or by explicit dispatch. Its
report includes the actual OS build and architecture, so differences remain
visible. Each job has a 12-minute limit and report retention is 14 days.
