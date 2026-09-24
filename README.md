# Gmail Calendar Notifier

[![build](https://github.com/lianglee123/google-calendar-notifier/actions/workflows/build.yml/badge.svg)](https://github.com/lianglee123/google-calendar-notifier/actions/workflows/build.yml)

**⬇️ Download** — a single `.exe` (requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)):
[latest stable release](https://github.com/lianglee123/google-calendar-notifier/releases/latest) ·
[newest dev build](https://github.com/lianglee123/google-calendar-notifier/releases/download/latest/GmailCalendarNotifier.exe)

This project was born out of a simple need: I kept getting meeting times wrong. If you
struggle with the same problem, I hope it helps you too.

An **Outlook-style reminder app for Google Calendar on Windows**. It lives in the system
tray, quietly polls your calendar, and pops an always-on-top reminder window at each
event's reminder time — with **Snooze** and **Dismiss**, just like Outlook's classic
reminder popup.

It's built for the case where you use Google Calendar on Windows but don't run the Google
Calendar web app all day (or your organization has locked down the "secret iCal address",
so simple `.ics` subscriptions don't work).

## About this fork

This is a fork of [khensler/gcal-notifier](https://github.com/khensler/gcal-notifier) (MIT),
created because I kept getting meeting times wrong. Changes made in this fork, on top of the
original project:

- **Multi-monitor reminders** — the popup now appears on *every* monitor (toggleable in
  Settings via **Show on all monitors**, on by default). Windows are created per monitor on
  each popup, so hot-plugged displays and setting changes are picked up automatically.
- **Modal overlay** — a full-screen, semi-transparent overlay on each monitor blocks input
  to other apps until all reminders are snoozed or dismissed. Clicking an overlay
  re-activates its reminder window, and closing the popup is no longer possible while
  reminders are pending.
- **Correct placement on scaled displays** — popups no longer end up off-screen on monitors
  using 125%/150% scaling (physical pixels vs. WPF DIPs are converted via `GetDpiForMonitor`,
  see the new `ScreenDpi.cs` helper).
- **Snooze default** — the default snooze interval is now **1 minute** (was 5).

## Features

- 🔔 Always-on-top reminder popup, centered on your primary screen, with a sound.
- ⏰ Honors each event's own reminder time, or a default lead time you choose.
- 💤 Snooze (1 min → 1 day) and Dismiss / Dismiss All, per reminder or all at once.
- 🔗 **Open in Calendar** button on each reminder, plus a **Join meeting** link when the
  event has a Meet/Zoom/Teams URL (in the location or description).
- 🔁 Recurring events handled correctly.
- 🚀 Optional launch at Windows startup.
- 🔒 Two sign-in methods: a built-in client (no setup) or your own Google OAuth client.

## Requirements

- Windows 10 or 11 (x64).
- A Google Calendar account.
- The [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Desktop, x64) —
  needed to run the download and to build from source. Install it with
  `winget install Microsoft.DotNet.DesktopRuntime.8`, or from the link above. If it's missing,
  the app shows a dialog with a download link on first launch.

---

## Install (for users)

1. Make sure the **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**
   is installed (`winget install Microsoft.DotNet.DesktopRuntime.8`).
2. **[Download `GmailCalendarNotifier.exe`](https://github.com/lianglee123/google-calendar-notifier/releases/download/latest/GmailCalendarNotifier.exe)**
   — a ~1 MB single file, rebuilt automatically from every push to `main`. Or build it yourself
   (see [Build from source](#build-from-source)).
3. Double-click it. Because the app is not code-signed, **Windows SmartScreen may warn**
   ("Windows protected your PC") — click **More info → Run anyway**. Some antivirus may also
   prompt.
4. On first launch the **Settings** window opens. Click **Sign in with Google** and approve
   access in your browser. (See [Sign-in methods](#sign-in-methods) for the options.)
5. Click **Save**. The app minimizes to the system tray and starts reminding you. Tick
   **Start automatically when Windows starts** if you want it always running.

There's no installer — it's a single portable `.exe`. To "uninstall", just delete it and the
`%APPDATA%\GmailCalendarNotifier` folder.

---

## Build from source

Requires the **.NET 8 SDK**.

```powershell
git clone <your-repo-url>
cd gmail-notifier
dotnet build
dotnet run          # run it directly
```

### Produce a distributable .exe

**Self-contained** — one file, ~155 MB, runs on any Windows 10/11 x64 machine with **no .NET
install required**. This is the easiest thing to hand to someone else:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

**Framework-dependent** — one file, ~1 MB, but the target machine needs the **.NET 8 Desktop
Runtime** installed:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -o publish-fd
```

The exe lands in the output folder (`publish\` or `publish-fd\`). Distribute just that one
`.exe` — not the `.pdb`.

### Releases (maintainers)

Two channels are produced by GitHub Actions:

- **Newest dev build** — every push to `main` updates the rolling `latest` prerelease
  ([`build` workflow](.github/workflows/build.yml)).
- **Stable versioned release** — pushing a `vX.Y.Z` tag builds and publishes a normal release
  with that version stamped into the exe ([`release` workflow](.github/workflows/release.yml)):

  ```powershell
  git tag v1.0.0
  git push origin v1.0.0
  ```

  The **latest stable release** link above resolves to the newest of these.

---

## Usage

The app runs from the **system tray**. Right-click the tray icon for:

- **Show reminders** — bring the reminder window up now (or tell you what's next).
- **Refresh now** — re-sync with Google immediately.
- **Settings…** — sign in/out and change options.
- **Exit**.

### Reminder window

- Reminders appear at their reminder time, centered and on top, with a sound.
- Select one or more reminders (Ctrl/Shift-click), pick a snooze interval, and **Snooze** —
  or **Dismiss** them. With nothing selected, the buttons act on all shown reminders.
- **Dismiss All** clears everything.
- **Open in Calendar** opens that event in the Google Calendar web UI. **Join meeting** opens
  the meeting link in your browser when the event has one.
- Closing the window (X) just hides it; pending reminders stay scheduled.

---

## Sign-in methods

Choose in **Settings → Sign-in method**. Both use Google's OAuth with a PKCE + loopback flow
and store only a refresh token (see [Privacy](#privacy--how-it-works)).

| Method | Setup required | Reads calendar via |
|---|---|---|
| **Built-in client** (default) | None | CalDAV |
| **My own Google OAuth client** | Create a Desktop OAuth client (below) | Calendar REST API |

**Built-in client** signs in exactly the way Mozilla Thunderbird does, using Thunderbird's
public installed-app credential. Pick this if your organization already permits Thunderbird —
no Google Cloud access needed.

**My own Google OAuth client** is for when you'd rather not rely on a shared credential, or the
built-in client isn't permitted in your environment. To set one up:

1. Go to the [Google Cloud Console](https://console.cloud.google.com/) and create (or pick) a project.
2. **APIs & Services → Library** → enable **Google Calendar API**.
3. **APIs & Services → OAuth consent screen** → configure it (External is fine; add your own
   Google account as a Test user).
4. **APIs & Services → Credentials → Create credentials → OAuth client ID** → Application
   type **Desktop app**.
5. Copy the **Client ID** and **Client secret** into the app's Settings, select **My own
   Google OAuth client**, and **Save**.

Switching methods (or editing the client id/secret) signs you out so you can re-authorize with
the new client.

---

## Settings

Stored at `%APPDATA%\GmailCalendarNotifier\settings.json` (editable in the app; the table is
for reference).

| Setting | Meaning |
|---|---|
| `DefaultReminderMinutes` | Minutes before start to remind, when an event has no reminder of its own. |
| `PollIntervalMinutes` | How often to re-sync with Google (minimum 1). |
| `UseEventAlarms` | Honor each event's own reminder time when present; otherwise always use the default. |
| `PlaySound` | Play a sound when a reminder pops. |
| `RunAtStartup` | Launch at Windows login (per-user `Run` registry key). |
| `AccountEmail` | Display only — the signed-in account. |
| `UseCustomOAuthClient` | `false` = built-in client + CalDAV; `true` = your own client + REST API. |
| `OAuthClientId` / `OAuthClientSecret` | Your desktop OAuth client's credentials (used when `UseCustomOAuthClient` is true). |

---

## Privacy & how it works

- Sign-in uses Google OAuth with **PKCE + a loopback redirect** to a temporary `localhost`
  port — no client secret is ever exposed to the browser.
- Only a **refresh token** is stored, **encrypted with Windows DPAPI** (readable only by your
  Windows account on this machine), at `%APPDATA%\GmailCalendarNotifier\token.dat`. It's never
  written to `settings.json`. **Sign out** in Settings deletes it.
- The app requests calendar access and **only ever reads** — it never modifies your calendar.
- **Built-in mode** reads over **CalDAV** (`apidata.googleusercontent.com/caldav/v2`), the same
  transport Thunderbird uses, and expands recurrences/reminders locally.
  **Custom mode** reads via the **Google Calendar REST API**.
- No data leaves your machine except the direct requests to Google.

> **About the built-in credential:** the OAuth client id/secret compiled into the app is
> Thunderbird's public *installed-app* credential, published in Mozilla's open-source tree and
> shipped in every Thunderbird build. For an installed app this "secret" is not confidential
> (PKCE is what secures the flow), so it is safe to include in source. This project is not
> affiliated with or endorsed by Mozilla or Google.

---

## Notes & limitations

- **Built-in (CalDAV) mode** reads your **primary** calendar. **Custom (REST) mode** reads all
  calendars you have visible ("selected") in Google Calendar.
- All-day events are intentionally skipped (a timed popup doesn't map cleanly to them).
- Reminders honor per-event overrides; when an event uses the calendar's *default* reminder,
  the app's `DefaultReminderMinutes` is used instead.
- Times are shown in your local time zone.

## Troubleshooting

- **"Access blocked" / a Google error page during sign-in** — your organization doesn't permit
  the built-in (Thunderbird) client. Switch to **My own Google OAuth client** in Settings.
- **App won't start (small build)** — install the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0), or use the
  self-contained build.
- **Nothing pops but a meeting is soon** — reminders fire at the reminder time (e.g. 10 min
  before), not immediately. Right-click the tray icon → **Show reminders** to see what's next.
- **Logs** — `%APPDATA%\GmailCalendarNotifier\log.txt` records sign-in and sync activity, and
  any unexpected errors. **Open log** is also on the Settings window.

## Project structure

| File | Responsibility |
|---|---|
| `App.xaml(.cs)` | Startup, tray icon, wiring, global error logging. |
| `OAuthService.cs` | Google OAuth (PKCE + loopback), token refresh. |
| `TokenStore.cs` | DPAPI-encrypted refresh-token storage. |
| `CaldavClient.cs` | CalDAV discovery + event query (built-in mode). |
| `CalendarService.cs` | Fetch + parse events (CalDAV and REST paths). |
| `ReminderManager.cs` | Poll timer, reminder scheduling, snooze/dismiss state. |
| `ReminderWindow.xaml(.cs)` | The reminder popup. |
| `SettingsWindow.xaml(.cs)` | Sign-in and options UI. |
| `AppSettings.cs` | Settings model + JSON persistence. |

## License

[MIT](LICENSE) © 2026 Kenyon Hensler & joy.li
