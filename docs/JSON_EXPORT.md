# JSON export

Export is off on a fresh installation or when export preferences are missing, invalid, or unreadable. An existing `usage.json` is not evidence of consent and is not touched while disabled.

## Controls

Right-click the tray → **JSON export**:

- **Enable export**: toggles persistent consent. Enabling requests a refresh through the existing refresh method; it does not publish an old cached snapshot with a new timestamp.
- **Choose output file…**: selects a full destination filename, with an overwrite confirmation for an existing file. It works while disabled and does not enable export. If enabled, a successful change requests a refresh. Changing destinations leaves the old file untouched and may require updating the consumer's Serve mapping.

The default remains `%LOCALAPPDATA%\CodexUsagePhone\usage.json` for compatibility with existing consumers. The generic C# writer is named `JsonSnapshotStore`; the directory name does not limit who can consume the data.

Preferences are stored locally in `%LOCALAPPDATA%\CodexUsageTray\json-export-settings.json`. They contain the enabled flag and full output path, so they must not be served or published. Preference writes use a temporary file and same-directory replacement. Export cannot become enabled or switch an enabled destination if preferences fail to save. Disabling always stops export in the current session; if persistence fails, the UI warns that the previous on-disk preference may apply on the next launch.

## Contract

The document contains only `fiveHour`, `weekly`, and `refreshedAt`. Each limit contains boolean `available`, nullable integer `remaining`, and nullable offset-bearing ISO timestamp `resetsAt`. Remaining is clamped to 0–100 using the popup's rules. Missing limits use false/null/null; an available limit can have a null reset. An exhausted limit has available true and remaining zero.

`refreshedAt` is the PC clock time when the existing reader parsed a successful Codex response, not a server timestamp, file-copy time, or widget-tap time. All values come from the already-normalized `UsageSnapshot`. No raw response, error text, account identifiers, credentials, or path is serialized.

## Cadence and failure behavior

The existing five-minute timer, initial refresh, manual Refresh, and popup-open refresh feed the exporter. There is no separate export frequency or selectable field list. Those settings would add freshness/schema variants without a demonstrated need.

Successful enabled export writes a complete sibling `.tmp` file, closes it, and replaces/moves it into the destination. Readers of the destination see complete old or new documents. Filesystem/access errors are contained using the project's auxiliary-persistence convention; export retries after a later successful refresh. There is no power-loss durability promise.

Fetch failure, export failure, disabling, changing destinations, and startup do not delete the previous snapshot or relabel it as fresh. A successful fetch reporting unavailable data does publish a fresh unavailable snapshot. Compare `refreshedAt` with the consumer's clock; availability alone says nothing about freshness.

No HTTP server or network configuration is added. An existing private file-serving mechanism can expose the chosen file. The application does not configure Tailscale or require a phone.

## Tests

Package-free `--self-test` coverage includes the JSON allowlist, availability/nulls, timestamps, last-good preservation, replacement readers, filesystem failures/recovery, default-off behavior, persistent opt-in/destination, disabling/restart, missing/corrupt/unreadable preferences, failed enable/path saves, and session disabling after persistence failure. The full suite includes one live authenticated Codex read.
