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

Automatic sending is off by default. Codex weekly and recovery are the default
selections; the other windows and low-quota alerts are independently optional.
Settings are also available directly in the submenu. Changing settings
establishes a quiet baseline.

Windows are chosen per source: **Codex weekly**, **Codex 5-hour**, **Claude weekly**,
**Claude 5-hour** and **Claude model weekly**. The recovery and low choices apply to
every selected window. A source only alerts while it has a tray number, because a
source with no tray number is not read. Alert titles name the source, for example
`Claude 5-hour: quota below 10%`. A selected 5-hour window can send a recovery
alert after each reset that follows use, which may be several a day.

**Claude model weekly** watches the weekly limits that apply to one model, which
can run out well before the all-models weekly limit. Each model is tracked on its
own and named in the title, for example `Claude Fable weekly: quota below 10%`.
Use it in place of **Claude 5-hour** on an account that has no five-hour limit: a
window the account does not report never alerts. The tray menu's **Claude plan has
a 5-hour limit** tick switches the **Claude 5-hour** alert on and off together with
its tray number, and leaves **Claude model weekly** as it is.

## Alert rules

- **Recovery:** a fresh reading crosses from 90% or less to **above 90%**.
  A confirmed new reset cycle at above 90% also qualifies, even when the previous
  observation was high. For example, 25% to 96% does not require seeing 100%.
- **Low:** a fresh reading crosses from 10% or more to **below 10%**.
  A confirmed new cycle already below 10% also qualifies.
- Exactly 90% and 10% do not trigger. These are remaining-quota percentages.
- Each alert is suppressed after successful delivery within its reset cycle,
  independently per source and window. State survives restarts. First launch/enabling at
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
quota alert text goes to ntfy, never credentials or account details.

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

Run `CodexUsageTray.exe --self-test-push` for 54 deterministic checks without
sending any real notifications or accessing production settings. The existing
`--self-test` suite is separate and includes a live Codex usage read.

Validated on Windows with .NET SDK 10.0.401: Release build (zero warnings/errors),
all 54 push checks, and the existing self-test suite.

On September 17, 2026, the maintainer confirmed receiving the explicit test
notification on their phone from the installed self-contained build at commit
`8a2c040`. The installed executable, startup registration, and existing widget feed
were verified. This confirms the configured PC-to-phone test-delivery path;
automatic threshold crossings are covered by the deterministic tests, not by an
observed natural reset or depletion event. Fresh installations still need their
own subscription and delivery test.

Claude windows were added on November 21, 2026. They use the same rules and are
covered by the deterministic tests. Upgrading re-establishes a quiet baseline once,
because the saved alert state belongs to the previous set of choices.

The per-model weekly choice was added on November 23, 2026, after Claude stopped
reporting a five-hour limit on the maintainer's account. It uses the same rules,
is covered by nine of the deterministic checks, and re-establishes a quiet baseline
once on upgrade for the same reason. No per-model alert had fired naturally when
this was written.
