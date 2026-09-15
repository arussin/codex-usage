# Tray controls

The tray number shows the percentage of your weekly Codex allowance remaining. Open the tray overflow area if the icon is hidden.

## View your usage

Left-click the number to open the popup. It shows the five-hour and weekly limits, their reset times, and a graph of recorded weekly usage. Opening the popup also requests a fresh reading.

A missing allowance is shown as unavailable. An available allowance with zero remaining is exhausted; it is different from unavailable data.

The app refreshes at startup and every five minutes while running. Right-click → **Refresh** requests an update immediately. The selected Codex CLI must be signed in on your Windows account.

## Tray menu

| Control | Action |
| --- | --- |
| Refresh | Request current quota readings. |
| JSON export → Enable export | Start or stop writing the JSON feed. Off by default. |
| JSON export → Choose output file… | Select the destination file. Selecting a file does not enable export. |
| Start with Windows | Enable or disable automatic startup for this Windows account. |
| Exit | Close the tray app and stop polling. |

## Use the JSON feed

The default destination is `%LOCALAPPDATA%\CodexUsagePhone\usage.json`. It contains the same normalized quota readings used by the tray.

Disabling export leaves the last file in place. Changing the destination also leaves the old file intact. Fetch or write failures preserve the previous timestamp, so downstream tools should check `refreshedAt` before presenting a reading as current.

[JSON contract and settings](JSON_EXPORT.md) · [Installation and rollback](INSTALL.md) · [Usage graphs and technical reference](UPSTREAM_GUIDE.md)
