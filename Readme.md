# WATS Log File Collector

Collects test-log files from one or more folders into the WATS Client input folder,
without copying anything twice.

It installs as a Windows service that starts at boot, with a system-tray icon and a
dashboard for status and configuration. The same collector also builds for .NET 8 and
runs on Linux and macOS.

*Originally written by Ragnar Engnes for Virinco AS, 2025.*

---

## Contents

- [Why it exists](#why-it-exists)
- [Quick start](#quick-start)
- [How it runs](#how-it-runs)
- [Configuration](#configuration)
- [The dashboard](#the-dashboard)
- [Command line](#command-line)
- [Deployment](#deployment)
- [Security](#security)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Appendix: cloud-storage folders](#appendix-cloud-storage-folders)
- [License](#license)

---

## Why it exists

Custom converters installed in the **WATS Client**
([download.wats.com](https://download.wats.com)) watch a single folder and do not
follow subdirectories. Where test systems write logs into a tree, or into several
places, something has to bring those files to one folder the client is watching.

That is all this does: watch, de-duplicate, copy. A more permanent integration into
WATS Client is planned.

---

## Quick start

1. **Install.** Download `WATS-LogFileCollector-Setup.msi` from
   [Releases](../../releases) and run it. It needs administrator rights, so expect a
   UAC prompt. Leave *Show the collector icon in the notification area* ticked on the
   last page.

   > Windows SmartScreen may warn that the publisher is unknown. **More info → Run
   > anyway.**

2. **Configure.** Right-click the tray icon → **Settings…**. Set at least the
   **source folders** (where your test systems write logs) and the **target folder**
   (the folder your WATS Client converter watches). Save.

3. **Apply.** Right-click the tray icon → **Restart service**. Settings are read at
   startup, so this is what makes them take effect.

4. **Check.** Right-click → **Open dashboard**. The status badge should read
   *Watching*, and files should appear under *Recently collected* as they arrive.

The service starts automatically at boot from now on, whether or not anyone logs in.

---

## How it runs

Three pieces, and it matters which does what:

| Piece | Where it runs | What it does |
| --- | --- | --- |
| **Service** `WATSLogFileCollector` | Session 0, from boot, as LocalSystem | Watches the source folders, copies files, serves the dashboard. Runs with nobody logged in. |
| **Tray app** `LogFileCollectorTray.exe` | The logged-in user's session | Status icon, menu, and the dashboard window. Holds no collector logic — it reads the service's dashboard API over loopback. |
| **Dashboard** | Served by the service | Status and settings. Opens in an application window from the tray, or in any browser. |

A Windows service runs in session 0 and cannot show a user interface. That is why the
tray is a separate process, and why the dashboard is served over HTTP rather than
being a window the service opens. It also means the tray can be closed, restarted, or
never started at all without affecting collection.

### Why HTTP, on a Windows install

The UI is an HTML page served over loopback and shown in a WebView2 window, rather
than a WinForms dialog. One UI then covers three cases that would otherwise need
three: the headless service in session 0, an engineer looking at the machine from
another desk, and the .NET 8 build where there is no WinForms at all. The window is
what makes it feel like an installed application instead of a URL to remember.

### The tray menu

| Item | What it does |
| --- | --- |
| *(status line)* | Watching / Rescanning / Idle / Service not responding, with counts |
| **Open dashboard** | The dashboard in an application window |
| **Settings…** | Straight to the settings form |
| **Rescan now** | Full sweep of every source folder |
| **Open in browser** | The same dashboard in the default browser |
| **Start / Stop service** | Prompts for elevation |
| **Restart service** | Applies a configuration change |
| **Start with Windows** | Per-user; the tray icon only, not the service |
| **Exit** | Closes the icon. Collection continues. |

Double-clicking the icon opens the dashboard.

### Autostart

Two independent things, deliberately:

- **The service** starts at boot, set by the installer. Collection never depends on
  anyone logging in. Change it in `services.msc`, or
  `sc config WATSLogFileCollector start= demand`.
- **The tray icon** starts per-user, via the Startup shortcut the installer creates
  and the *Start with Windows* item in the tray menu (an `HKCU\…\Run` value, no
  elevation needed).

---

## Configuration

**Use the settings form** — the tray's *Settings…*, the dashboard's gear, or the
Start Menu shortcut. It validates before it writes, keeps the previous file as
`appsettings.json.bak`, and preserves any setting it does not itself show.

Settings are read at startup. Apply a change with **Restart service**.

The file lives at:

```text
C:\ProgramData\Virinco\WATS\LogFileCollector\appsettings.json
```

Editing it by hand still works; the form shows the exact JSON it will write under
*Advanced*. The reference below is for scripted deployments.

### Example

```json
{
  "SourceFolders": [
    "C:\\Logs\\Input",
    "\\\\server\\share\\station-2\\logs"
  ],
  "TargetFolder": "C:\\ProgramData\\Virinco\\WATS\\OutputFolder",
  "Filter": "*.log",
  "IncludeSubdirectories": true,
  "FileCreatedDelayMs": 1000,
  "DatabasePath": "copied.db",
  "PeriodicRescanMinutes": 60,
  "RenameStrategy": "counter",
  "Logging": {
    "LogFilePath": "log.txt",
    "LogOutputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
    "LogLevel": "Information",
    "RollingInterval": "Day",
    "RetainedFileCountLimit": 10
  },
  "Dashboard": {
    "Enabled": true,
    "UrlPrefix": "http://localhost:8787/",
    "OpenBrowserOnStart": false,
    "AllowConfigEdit": true
  }
}
```

### Reference

| Setting | Meaning |
| --- | --- |
| `SourceFolders` | Folders to watch. Local paths or UNC shares; read access is enough. Each is watched independently — see below. |
| `SourceFolder` | The single-folder form used before 1.5. Still read, as a one-item list. |
| `TargetFolder` | Where files are copied. Normally the folder your WATS Client converter watches. Needs write access. |
| `Filter` | File pattern, e.g. `*.log`. `*.*` for everything. |
| `IncludeSubdirectories` | Follow subfolders of each source. |
| `FileCreatedDelayMs` | Wait after a file appears before copying it, so half-written files are not picked up. Default 1000. |
| `PeriodicRescanMinutes` | Full sweep on a timer, as a safety net for missed file events. `0` disables it. |
| `RenameStrategy` | On a name collision in the target: `counter` (`file_1.log`), `timestamp` (`file_20250913_103500.log`) or `guid` (`file_a12b34c5.log`). |
| `DatabasePath` | The de-duplication database. Relative paths resolve beside the config file. |
| `Logging.*` | Serilog: level (`Verbose`…`Error`), `RollingInterval`, `RetainedFileCountLimit`, file path and message template. |
| `Dashboard.Enabled` | Serve the dashboard at all. |
| `Dashboard.UrlPrefix` | Where it listens. See [Security](#security) before widening it. |
| `Dashboard.AllowConfigEdit` | Whether settings may be changed from the page. |
| `Dashboard.OpenBrowserOnStart` | Launch a browser when the collector starts in the foreground. Ignored by the service. |

### Several source folders

Each folder gets its own watcher and is reported on its own. A folder that is
unreachable — a share not mounted yet, a path that does not exist — does not stop the
others: it is logged, retried every 30 seconds, and swept once when it appears, so
files that arrived while it was away are still collected.

### How de-duplication works

A file is identified by `(full path, last-write time, size)`, recorded in an SQLite
database. A file already recorded is skipped, across restarts and across rescans. Copy
a file back into the source with a new timestamp and it will be collected again —
identity is not just the name.

---

## The dashboard

`http://localhost:8787/` by default, or the tray's *Open dashboard*.

| Panel | Shows |
| --- | --- |
| Status badge | Watching / Rescanning / Idle / Offline |
| KPI row | Files collected all time, copied this session, skipped as duplicate, errors |
| Files collected per day | Bar chart over the last 7 / 14 / 30 days |
| Watch configuration | Each source folder with its own reachability, plus filter, delay, strategy, last file, runtime |
| Recently collected | Copy history from the database, filterable by path |
| Log | Tail of the current log file, coloured by level |
| ⚙ Settings | The settings form |

The page refreshes every five seconds. Charts are vendored, so they work on a machine
with no internet; only the web fonts come from a CDN and the page falls back to the
system font stack without them.

The HTTP API behind it is documented in [`WebUI/README.md`](WebUI/README.md).

---

## Command line

`LogFileCollector.exe` runs in the foreground with a console — useful for diagnosis.
Stop the service first, or the two will contend for the dashboard port.

| Option | Effect |
| --- | --- |
| *(none)* | Watch and collect until Ctrl+C |
| `--rescan` | Full sweep of every source folder first, then keep watching |
| `--reset` | Delete the de-duplication database before starting. **Everything already collected will be collected again.** |
| `--config <path>` | Use this `appsettings.json` instead of the one in ProgramData. For testing, or more than one collector per host. |
| `--service` | Run under the Service Control Manager. The SCM passes this; there is no reason to type it. |

---

## Deployment

```powershell
# Interactive
msiexec /i WATS-LogFileCollector-Setup.msi

# Silent — from an elevated prompt
msiexec /i WATS-LogFileCollector-Setup.msi /qn

# Uninstall
msiexec /x WATS-LogFileCollector-Setup.msi /qn
```

The MSI is per-machine, upgrades in place, and can be deployed by GPO or Intune.

| | |
| --- | --- |
| Program files | `C:\Program Files\Virinco\LogFileCollector` — service, tray app, `WebUI\` |
| Configuration, database, logs | `C:\ProgramData\Virinco\WATS\LogFileCollector` |
| Service | `WATSLogFileCollector`, automatic start, LocalSystem, restarts on failure |
| Tray | Startup shortcut for all users |
| Start Menu | Run in the foreground, show the tray icon, Settings, Dashboard |

**What an upgrade and an uninstall leave alone:** `appsettings.json`, the
de-duplication database and the logs. An upgrade keeps the operator's settings; a
reinstall resumes where the previous install left off rather than re-copying
everything it already has.

The 1.1 scheduled task (`\Virinco\LogFileCollector`) is removed on install — the
service replaces it, and leaving both would collect the same folder twice.

---

## Security

**The dashboard has no authentication.** The default prefix is loopback only, which is
the intended deployment.

To reach it from another machine, widen the prefix and, on Windows, reserve the URL
for the account the service runs under:

```powershell
netsh http add urlacl url=http://+:8787/ user="NT AUTHORITY\NetworkService"
```

Anything that changes state — a rescan, saving settings — is refused unless the request
comes from a loopback address. Widening the prefix therefore gives the network a
read-only monitor, not a configuration editor. `"AllowConfigEdit": false` disables
settings changes entirely; `"Enabled": false` does not serve the dashboard at all.

### Why the service runs as LocalSystem

`HttpListener` refuses to bind `http://localhost:<port>/` for a non-administrative
account unless a URL ACL has been reserved for it, and the dashboard binding reliably
matters more here than the privilege reduction. To run lower-privileged instead:

```powershell
netsh http add urlacl url=http://localhost:8787/ user="NT AUTHORITY\NetworkService"
sc config WATSLogFileCollector obj= "NT AUTHORITY\NetworkService"
```

The account then needs read access to the source folders and write access to the
target folder.

---

## Troubleshooting

**No tray icon.** The Startup shortcut fires at logon; if the tray was closed, start it
from the Start Menu (*Show tray icon*) or tick *Start with Windows* in its menu. The
service is unaffected either way — check `services.msc` for `WATSLogFileCollector`.

**The tray says "Service not responding".** The service is stopped, or the dashboard
could not bind its port. Start it from the tray, then look at
`C:\ProgramData\Virinco\WATS\LogFileCollector\log*.txt`.

**Nothing is collected.** Open the dashboard and look at *Watch configuration*: a
source folder marked *unreachable* is the usual cause. A path that does not exist yet
is retried every 30 seconds rather than treated as fatal, so the service stays up and
keeps telling you it is missing. Check also that `Filter` matches your files, and that
the account the service runs under can read the folder — a UNC share is the common
case where LocalSystem cannot.

**The dashboard opens in a browser rather than a window.** The WebView2 runtime is
missing. It ships with Windows 11 and with Edge on Windows 10; install the Evergreen
runtime from Microsoft to get the window back. Everything works either way.

**Port 8787 is in use.** Change `Dashboard.UrlPrefix` in the settings form and restart
the service. This also happens when a foreground `LogFileCollector.exe` is running
alongside the service.

**Files were collected twice.** Something changed a file's timestamp or size — identity
is `(path, last-write time, size)`, not the name. Two source folders pointing at the
same directory would also do it, which the settings form now refuses to save.

**Everything was collected again after an upgrade.** The database was deleted, most
likely by `--reset`. An upgrade does not touch it.

---

## Building from source

```powershell
# Collector — both targets
dotnet build src\Tools\LogFileCollector\LogFileCollector.csproj -c Release

# Tray application (Windows only)
dotnet build src\Tools\LogFileCollector\Tray\LogFileCollector.Tray.csproj -c Release

# MSI — needs the net48 collector and the tray built first
dotnet build src\Tools\LogFileCollector\Installer\LogFileCollector.wixproj -c Release
# → Installer\bin\Release\WATS-LogFileCollector-Setup.msi
```

| Target | Use |
| --- | --- |
| `net48` | What the MSI packages, and what the service runs |
| `net8.0` | Cross-platform. `dotnet run -f net8.0 -- --config ./appsettings.json --rescan` |

The `WebUI/` folder is copied next to the executable on build and served from there at
runtime. `Tray/` and `Installer/` are separate projects inside the collector's folder,
and are excluded from its compile globs.

The Inno Setup script (`LogFileCollector.iss`) is kept for the 1.1 release path. New
deployments should use the MSI.

---

## Appendix: cloud-storage folders

Google Drive, OneDrive and Dropbox folders can be used as sources, with one caveat:
**the service runs as LocalSystem and has no access to a drive letter mapped in your
interactive session.**

| Approach | Works as a service? |
| --- | --- |
| UNC path — `\\GoogleDrive\My Drive\Logs` | **Yes.** Preferred. |
| Local profile path — `%USERPROFILE%\OneDrive\...` | Only if the service account can reach it; LocalSystem generally cannot. |
| Mapped drive letter — `G:\My Drive\Logs` | **No.** The letter does not exist outside your session. |

In JSON, escape the backslashes:

```json
"SourceFolders": [ "\\\\GoogleDrive\\My Drive\\Logs" ]
```

If only a drive letter is available, run the service under your own account
(`sc config WATSLogFileCollector obj= "DOMAIN\user" password= "…"`), and reserve the
dashboard URL for that account as described under [Security](#security).

### Permissions

Read access to the source folders is enough — de-duplication is tracked in the local
database, not by writing to the source. Write access is needed only for the target
folder.

---

## License

Copyright © 2025 **Virinco AS**.

- You may use and modify the software internally.
- Redistribution, resale or sublicensing of modified versions is **not permitted**.
- Pull requests are welcome; acceptance is at Virinco's discretion.
- Provided **as is**, without warranty. Use at your own risk.

See [LICENSE.txt](./LICENSE.txt) for the complete terms.
