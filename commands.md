# Build the installer (single-file EXE)

From the repo root:

```cmd
dotnet publish .\InOfficeTime.Installer\InOfficeTime.Installer.csproj -c Release -o .\dist
```

Output:

```
.\dist\InOfficeTimeSetup.exe
```

That EXE contains the self-contained service bundled as an embedded ZIP. Ship
just this one file.

# Install (on the target machine)

Double-click `InOfficeTimeSetup.exe`, accept the UAC prompt.

The installer will:
- stop and remove any previous `InOfficeTime` service,
- extract the service into `C:\Program Files\InOfficeTime\`,
- register the `InOfficeTime` Windows service (auto-start),
- start it.

After install:
- API:
  - `GET http://localhost:11000/log?month=yyyy-mm`  — raw log text (defaults to current month)
  - `GET http://localhost:11000/time?month=yyyy-mm` — per-day sessions + month totals
  - `GET http://localhost:11000/day?date=yyyy-mm-dd` — single day's sessions + totals (defaults to today)
- Logs: `C:\ProgramData\InOfficeTime\<yyyy-MM>.txt`
- Config: `C:\Program Files\InOfficeTime\appsettings.json`

# Configure the office host

Office detection pings a known office-only host. Change the host (or timeout) by
editing `C:\Program Files\InOfficeTime\appsettings.json` (requires admin):

```json
{
  "OfficeDetection": {
    "Host": "10.41.11.71",
    "TimeoutMs": 1000
  }
}
```

Then restart the service so it picks up the new value:

```cmd
sc.exe stop InOfficeTime
sc.exe start InOfficeTime
```

Reinstalling/upgrading with `InOfficeTimeSetup.exe` preserves this file, so your
custom host won't be reset on upgrade.

# Reinstall (upgrade to a new build)

Just run the new `InOfficeTimeSetup.exe` — it auto-stops and removes the old
service before installing the new one. No separate uninstall step is required.

```cmd
"C:\path\to\new\InOfficeTimeSetup.exe"
```

Existing log files in `C:\ProgramData\InOfficeTime` are untouched.

# Uninstall

```cmd
"C:\path\to\InOfficeTimeSetup.exe" /u
```

Stops the service, deletes it, and removes `C:\Program Files\InOfficeTime`.
Log files in `C:\ProgramData\InOfficeTime` are preserved.

# Troubleshooting

Check service state:

```cmd
sc.exe query InOfficeTime
sc.exe qc InOfficeTime
```

View .NET / service errors:

```cmd
wevtutil qe Application /c:20 /rd:true /f:text /q:"*[System[Provider[@Name='.NET Runtime' or @Name='Application Error']]]"
```
