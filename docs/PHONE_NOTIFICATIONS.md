# Phone quota notifications

Optional ntfy push alerts, using the tray's existing refresh. No phone polling,
new background process, or changes to the JSON export are required.

## Setup

1. Install the ntfy phone app and allow notifications.
2. In the tray menu, open **Phone notifications > Configure ntfy**. Keep the
   hosted server or enter your HTTPS ntfy server. A random topic is generated.
3. Subscribe to that server/topic in the phone app. Enable phone notifications
   and choose the alerts and quota windows, then save.
4. Use **Send test notification** and confirm it appears on your phone.
   Server acceptance alone does not confirm phone delivery.

Automatic sending is off by default. Weekly/recovery are the default selections;
five-hour and low-quota alerts are independently optional. Settings are also
available directly in the submenu. Changing settings establishes a quiet baseline.

## Alert rules

- **Recovery:** a fresh reading crosses from 90% or less to **above 90%**.
  A confirmed new reset cycle at above 90% also qualifies, even when the previous
  observation was high. For example, 25% to 96% does not require seeing 100%.
- **Low:** a fresh reading crosses from 10% or more to **below 10%**.
  A confirmed new cycle already below 10% also qualifies.
- Exactly 90% and 10% do not trigger. These are remaining-quota percentages.
- Each alert is suppressed after successful delivery within its reset cycle,
  independently per window. State survives restarts. First launch/enabling at
  a high or low value does not itself generate an alert.
- Missing, failed, expired, stale or out-of-order readings do not trigger alerts.
  Reset timestamp jitter before the stored reset deadline does not rearm alerts.

Detection normally occurs on the next successful five-minute tray refresh while
Windows and the tray are running, plus push delivery delay. A sleeping/offline PC
cannot send until it resumes. A reset consumed below 91% before the next read can
still be missed: this is a threshold notification, not a complete reset-event log.
Existing native Windows low-quota warnings are unchanged.

## Delivery and privacy

Requests are asynchronous with a ten-second timeout. Transient failures have at
most three total attempts per alert/cycle, retried only on subsequent fresh reads
still in the relevant region. Leaving that region cancels a pending alert.
Permanent HTTP errors stop retries. Attempts are saved before sending; successful
server acceptance is saved afterward. If the response is lost or the app crashes
between acceptance and saving, a duplicate is possible; this is not an exactly-once
protocol. Push errors never interrupt the quota display, history or JSON export.

Settings and state are separate files in `%LOCALAPPDATA%\CodexUsageTray`:
`phone-notifications.json` and `phone-notification-state.json`. The topic stays out
of the exported widget feed, error text and notification-state file. Only short
quota alert text goes to ntfy, never Codex credentials or account details.

On an open server, anyone with the topic name can subscribe or publish. Keep the
random topic private; do not commit your settings. This version supports HTTPS
origins with an open/random topic, not bearer-token or password authentication.
Redirects are not followed. Saving a disabled preference failure disables this
session and warns that the preference must be checked before the next launch.

For Google Play ntfy with the hosted service, disabling Instant delivery uses the
app's Firebase delivery path instead of a foreground subscription service. Delivery
may be delayed while idle; other app builds/server setups behave differently.
See [publishing](https://docs.ntfy.sh/publish/) and
[Android delivery](https://docs.ntfy.sh/subscribe/phone/) for ntfy's current behavior.

## Test

Run `CodexUsageTray.exe --self-test-push` for 40 deterministic checks without
sending any real notifications or accessing production settings. The existing
`--self-test` suite is separate and includes a live Codex usage read.

Validated on Windows with .NET SDK 10.0.401: Release build (zero warnings/errors),
all 40 push checks, and the existing self-test suite. Phone delivery and interactive
UI acceptance still require setup/testing on the actual device.
