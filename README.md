# InOffice Time

A lightweight Windows service that tracks your work session time and tells you
how much of it was spent **in the office** vs **remote** — entirely offline, on
your own machine, with no cloud, accounts, or telemetry.

Sessions are detected from Windows **lock / unlock / logon / logoff** events,
and each event is tagged with a location based on whether a configurable
office-only host responds to a ping. A local HTTP API on `http://localhost:11000`
renders daily and monthly reports as HTML (or JSON).

---

<img width="985" height="829" alt="image" src="https://github.com/user-attachments/assets/2bfd377d-01bc-4d68-998b-be6e924c3c38" />

---

## Table of contents

- [How it works](#how-it-works)
- [Project layout](#project-layout)
- [Requirements](#requirements)
- [Build from source](#build-from-source)
- [Install](#install)
- [Using the API](#using-the-api)
- [Log format](#log-format)
- [Configuration](#configuration)
- [Upgrade / reinstall](#upgrade--reinstall)
- [Uninstall](#uninstall)
- [Troubleshooting](#troubleshooting)
- [Development](#development)

---

## How it works

1. On install, an `InOfficeTime` Windows service is registered and started as
   **Local System** so it can receive session notifications for *all* sessions
   on the machine (`NOTIFY_FOR_ALL_SESSIONS`).
2. A hidden message-only window subscribes to
   [WTS session notifications](https://learn.microsoft.com/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification)
   and forwards logon / logoff / lock / unlock events to a log writer.
3. For every event, `LocationDetector` pings a configurable office-only host
   (default `10.41.11.71`, timeout `1000 ms`). A successful reply tags the event
   as `office`; anything else is `remote`.
4. Each event is appended as a tab-separated line to the current month's log
   file at `C:\ProgramData\InOfficeTime\<yyyy-MM>.txt`.
5. When you open `http://localhost:11000/time` (or `/day`, `/log`), the service
   parses that file, pairs resume events with pause events into sessions, clips
   them to the requested window, and renders the totals.

An ongoing (still-active) session is shown as "Ongoing" and counted up to the
current instant.

---

## Project layout

```
InOffice/
├── InOffice.slnx                    Solution file (both projects)
├── InOfficeTime/                    The Windows service / HTTP API
│   ├── Program.cs                   Minimal-API host + routes (/, /log, /time, /week, /day, /off)
│   ├── SessionMonitorWorker.cs      STA thread + WTS notification plumbing
│   ├── WtsMessageWindow.cs          Hidden message-only window
│   ├── WtsNative.cs                 P/Invoke declarations
│   ├── LocationDetector.cs          Ping-based office/remote detection
│   ├── Location.cs                  "office" / "remote" constants
│   ├── WorkTimeLogWriter.cs         Appends TSV lines to the monthly log
│   ├── WorkTimeLogPaths.cs          Resolves C:\ProgramData\InOfficeTime\*
│   ├── WorkTimeAnalytics.cs         Turns the log file into daily / monthly reports
│   ├── HtmlRenderer.cs              Renders the HTML views
│   ├── appsettings.json             Shipped next to the exe; editable post-install
│   └── InOfficeTime.csproj          net10.0-windows, self-contained, win-x64
│
├── InOfficeTime.Installer/          Single-file setup EXE
│   ├── Program.cs                   Installs / uninstalls the service and payload
│   ├── app.manifest                 Requests UAC elevation (requireAdministrator)
│   └── InOfficeTime.Installer.csproj
│         - publishes the service project,
│         - zips the publish output,
│         - embeds the zip as "payload.zip",
│         - produces a single-file, self-contained setup EXE
│
├── commands.md                      Quick command cheat-sheet
└── dist/                            (generated) InOfficeTimeSetup.exe
```

The installer project has an MSBuild target `BuildServicePayload` that runs
before compilation. It publishes the service (`dotnet publish … -c Release -r
win-x64 --self-contained true`), zips the output and embeds the resulting
`payload.zip` as a managed resource. The final installer is a single,
self-contained `InOfficeTimeSetup.exe` — no separate .NET runtime required on
the target machine.

---

## Requirements

### To build

- Windows 10 / 11 (x64)
- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or newer
- PowerShell or `cmd.exe`

### To install (on the target machine)

- Windows 10 / 11 (x64)
- Administrator privileges (the installer requests UAC elevation)
- No .NET runtime needed — the installer is self-contained

---

## Build from source

Clone the repo and, from the repo root, run:

```cmd
dotnet publish .\InOfficeTime.Installer\InOfficeTime.Installer.csproj -c Release -o .\dist
```

Output:

```
.\dist\InOfficeTimeSetup.exe
```

That single EXE is everything you need to ship. It contains the self-contained
service bundled as an embedded ZIP.

> Tip: a build cheat sheet is also kept in [`commands.md`](./commands.md).

---

## Install

On the target machine, double-click `InOfficeTimeSetup.exe` and accept the UAC
prompt.

The installer will:

- stop and remove any previous `InOfficeTime` service,
- extract the service into `C:\Program Files\InOfficeTime\`,
- register the `InOfficeTime` Windows service with `start= auto`,
- start it.

Once installed:

- **API**: `http://localhost:11000`
- **Install dir**: `C:\Program Files\InOfficeTime\`
- **Logs**: `C:\ProgramData\InOfficeTime\<yyyy-MM>.txt`
- **Config**: `C:\Program Files\InOfficeTime\appsettings.json` (admin-editable)

Lock and unlock your PC once to generate the first session entry.

---

## Using the API

All endpoints are served on `http://localhost:11000`. They do content
negotiation: a browser gets an HTML page; anything that sends
`Accept: application/json` or `Content-Type: application/json` gets JSON back.

| Endpoint                         | Description                                                                  |
| -------------------------------- | ---------------------------------------------------------------------------- |
| `GET /`                          | Redirects to `/time` (or returns `{endpoints: […]}` for JSON clients).       |
| `GET /log?month=yyyy-mm`         | Raw log text for the given month (defaults to current month).                |
| `GET /time?month=yyyy-mm`        | Month report — sessions per day + monthly totals (office / remote / total).  |
| `GET /week?week=yyyy-Www`        | Week report — sessions per day + weekly totals (ISO 8601 week).              |
| `GET /day?date=yyyy-mm-dd`       | Single day's sessions + daily totals (defaults to today).                    |
| `POST /off`                      | Mark a day as out of office (`type=full|half`, `date=yyyy-mm-dd`).           |

### Quick examples

Browser (HTML):

```
http://localhost:11000/time
http://localhost:11000/time?month=2026-04
http://localhost:11000/day?date=2026-04-17
http://localhost:11000/log?month=2026-04
```

JSON:

```cmd
curl -H "Accept: application/json" http://localhost:11000/time
curl -H "Accept: application/json" http://localhost:11000/day?date=2026-04-17
```

Mark today as a full day off (form-encoded POST):

```cmd
curl -X POST -d "type=full" http://localhost:11000/off
```

Mark a specific date as a half day off (JSON response):

```cmd
curl -X POST -H "Accept: application/json" -d "type=half&date=2026-04-21" http://localhost:11000/off
```

The browser UI also exposes a **Day off** quick-control next to the filters on
the `/time`, `/week`, and `/day` pages that POSTs to the same endpoint.

### JSON response shape (`/time`)

```json
{
  "Month": "2026-04",
  "MonthTotalSeconds": 123456,
  "MonthOfficeSeconds": 80000,
  "MonthRemoteSeconds": 43456,
  "MonthTotalHours": 34.29,
  "MonthOfficeHours": 22.22,
  "MonthRemoteHours": 12.07,
  "DaysOff": 1.5,
  "Days": [
    {
      "Date": "2026-04-17",
      "DayTotalSeconds": 27000,
      "DayOfficeSeconds": 18000,
      "DayRemoteSeconds": 9000,
      "DayOffType": null,
      "Sessions": [
        {
          "Start": "2026-04-17T08:30:00+02:00",
          "End":   "2026-04-17T12:00:00+02:00",
          "TotalSeconds": 12600,
          "Location": "office",
          "Ongoing": false
        }
      ]
    },
    {
      "Date": "2026-04-21",
      "DayTotalSeconds": 0,
      "DayOfficeSeconds": 0,
      "DayRemoteSeconds": 0,
      "DayOffType": "full",
      "Sessions": []
    }
  ]
}
```

---

## Log format

One tab-separated line per Windows session event, appended to
`C:\ProgramData\InOfficeTime\<yyyy-MM>.txt`:

```
<ISO-8601 timestamp>\t<EventName>\t<SessionId>\t<Location>
```

Example:

```
2026-04-17T08:30:12.1234567+02:00	SessionUnlock	2	office
2026-04-17T12:00:04.9876543+02:00	SessionLock	2	office
```

- `EventName` is one of `SessionLogon`, `SessionLogoff`, `SessionLock`,
  `SessionUnlock` (resume vs. pause pairs are decided in
  `WorkTimeAnalytics.cs`), or `OutOfOffice` for entries written via `POST /off`.
- `Location` is `office` or `remote` for session events (computed at the moment
  the event fires via an ICMP ping to the configured host). For `OutOfOffice`
  rows the same column carries `full` or `half` to indicate the day-off kind.
  Day-off entries use a timestamp pinned to local noon of the target date and
  a session id of `0`.

Log files are plain text and safe to archive, diff, or back up.

---

## Configuration

Office detection is configured in `appsettings.json`, which ships next to the
service exe and can be edited post-install:

**File:** `C:\Program Files\InOfficeTime\appsettings.json` (requires admin to
edit)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "OfficeDetection": {
    "Host": "10.41.11.71",
    "TimeoutMs": 1000
  }
}
```

Settings:

| Key                           | Default        | Description                                                                       |
| ----------------------------- | -------------- | --------------------------------------------------------------------------------- |
| `OfficeDetection.Host`        | `10.41.11.71`  | Host (IP or DNS name) pinged to detect office presence. Must be office-only.      |
| `OfficeDetection.TimeoutMs`   | `1000`         | Ping timeout in milliseconds. A successful reply within the timeout ⇒ `office`.   |
| `Logging.LogLevel.Default`    | `Information`  | Standard ASP.NET Core logging configuration. Use `Debug` for more verbose output. |

After editing, restart the service so it picks up the new values:

```cmd
sc.exe stop InOfficeTime
sc.exe start InOfficeTime
```

> Reinstalling / upgrading with `InOfficeTimeSetup.exe` **preserves
> `appsettings.json`**, so your custom host won't be reset on upgrade.

---

## Upgrade / reinstall

Just run the new `InOfficeTimeSetup.exe` — it auto-stops and removes the old
service before installing the new one. No separate uninstall step is required.

```cmd
"C:\path\to\new\InOfficeTimeSetup.exe"
```

- `appsettings.json` in the install folder is preserved.
- Existing log files in `C:\ProgramData\InOfficeTime` are untouched.

---

## Uninstall

Run the installer with `/u` (or `-u`, `--uninstall`) from an elevated prompt:

```cmd
"C:\path\to\InOfficeTimeSetup.exe" /u
```

This will:

- stop the `InOfficeTime` service,
- delete the service registration,
- remove `C:\Program Files\InOfficeTime\`.

**Kept intentionally**: log files in `C:\ProgramData\InOfficeTime\`. Delete
them manually if you no longer want your historical data:

```cmd
rmdir /s /q "C:\ProgramData\InOfficeTime"
```

If you just want to stop the service without removing it:

```cmd
sc.exe stop InOfficeTime
```

To remove the service by hand (without the installer):

```cmd
sc.exe stop InOfficeTime
sc.exe delete InOfficeTime
rmdir /s /q "C:\Program Files\InOfficeTime"
```

---

## Troubleshooting

Check service state:

```cmd
sc.exe query InOfficeTime
sc.exe qc InOfficeTime
```

View recent .NET / service errors in the Application event log:

```cmd
wevtutil qe Application /c:20 /rd:true /f:text /q:"*[System[Provider[@Name='.NET Runtime' or @Name='Application Error']]]"
```

Common issues:

- **Port 11000 already in use** — another process is bound to
  `localhost:11000`. Stop it, or change `ASPNETCORE_URLS` for the service.
- **Everything shows as `remote`** — the configured `OfficeDetection.Host`
  isn't responding to ICMP from your machine. Try pinging it manually; pick a
  different office-only host (or open ICMP on the firewall) and update
  `appsettings.json`.
- **"This installer must run as Administrator"** — right-click
  `InOfficeTimeSetup.exe` → *Run as administrator*.
- **`WTSRegisterSessionNotification` failed** in the service log — the service
  isn't running as `LocalSystem`. Check `sc qc InOfficeTime` and reinstall via
  the installer (which registers it correctly).

---

## Development

Run the service directly (as a console app) for quick iteration. From the repo
root:

```cmd
dotnet run --project .\InOfficeTime\InOfficeTime.csproj
```

It listens on `http://localhost:11000` the same way as the installed service,
but logs to a local `C:\ProgramData\InOfficeTime\<yyyy-MM>.txt` and reads
configuration from `InOfficeTime\appsettings.Development.json` /
`appsettings.json`.

When not running under Service Control Manager, WTS notifications for *other*
sessions may not be delivered — use the installed service for real session
tracking.

Tech stack:

- .NET 10 (`net10.0-windows`), ASP.NET Core Minimal APIs
- `Microsoft.Extensions.Hosting.WindowsServices` for service hosting
- Windows Forms message loop (STA) for the WTS notification window
- Self-contained, single-file publish for the installer

---

## License

No license specified yet — treat this repository as **all rights reserved**
until a license file is added.
