# First exporter release: acceptance checklist

The publication script runs an isolated Windows build, the complete self-test suite, and formatting checks before pushing source or creating a draft binary release. A passing automated run is **not** an interactive Windows/Android acceptance test.

Before making the binary draft public:

- [ ] New Windows profile/install: authenticated CLI discovery, normal launch, popup and sizing.
- [ ] Export starts off; Enable export, custom file picker, disable and restart work as described.
- [ ] Manual and five-minute refresh advance the snapshot timestamp; failed/disabled export leaves old data visibly old.
- [ ] A phone reaches the ordinary private hostname and reads updated files after replacements.
- [ ] A sanitized public preset imports, displays and refreshes; stale/no-data/low/FULL remain distinguishable.
- [ ] Installation, startup and rollback instructions work; do not change Adam's live setup without explicit direction.

Keep the Windows release a **draft** until these checks are recorded. The widgets' source and `.kwgt` files can be public independently. Update the short setup guide with the exact verified binary-release URL after passing the checks.

Do not submit an upstream PR automatically. The complete candidate also has prior CLI-discovery/DPI work; split it from an exporter-only contribution and re-test that exact branch first.
