# Changelog

All notable changes to LogFileCollector are recorded here.

## [1.5.0] — 2026-09-30

### Added

- **More than one source folder.** `SourceFolders` takes a list, and the settings
  form has add/remove rows for it. Each folder gets its own watcher, so one
  unreachable share does not stop the others being collected — and each is
  retried and reported separately, rather than hiding behind a single
  "reachable" flag. An existing `SourceFolder` is read as a one-item list, so a
  1.4 configuration keeps working untouched.
- **The dashboard opens in an application window**, not just a browser tab. The
  tray hosts the same page in WebView2, so on Windows it behaves like an
  installed application while the HTTP server — which is what makes the headless
  service, remote viewing and the Linux build possible — stays exactly as it was.
  *Open in browser* is still in the tray menu, and the window falls back to the
  browser when the WebView2 runtime is missing.

### Changed

- **The tray menu is WATS dark.** It was rendering in the default Windows
  grey-on-grey, which looked nothing like the dashboard beside it. It now uses
  the same token values as `tokens.css` — near-black surfaces, the yellow accent
  bar on the selected item, readable muted text on the status line.

## [1.4.0] — 2026-09-30

### Added

- **Settings are edited in a form, not a JSON file.** The dashboard's gear opens a
  proper settings dialog covering everything an operator sets: source and target
  folders, file filter, subfolders, settle delay, periodic rescan, collision
  strategy, log level and rotation, and the dashboard address and permissions.
  It validates before saving — a missing folder, or a source and target that are
  the same, is caught in the form rather than by the collector at next startup —
  and any key the form does not model is preserved byte for byte. The raw JSON is
  still visible, read-only, under *Advanced*.
- **Tray: Settings...** opens that form, and **Restart service** applies a
  configuration change without a trip to `services.msc`.

### Fixed

- **The tray icon did not appear after installing.** The Startup shortcut only
  fires at the next logon, and nothing launched the tray at the end of the
  install — so 1.3.0 finished with a running service, no window, and no icon,
  which looked exactly like nothing had happened. The finish page now offers to
  show the icon straight away, ticked by default.
- The installer no longer offers to open `appsettings.json` in Notepad, and the
  Start Menu entry for it is replaced by a **Settings** link to the form.

## [1.3.0] — 2026-09-30

How the collector runs changed in this release. 1.2 was still a console process
you had to start yourself; 1.3 installs a service that starts at boot and a tray
icon that talks to it.

### Added

- **Runs as a Windows service** (`WATSLogFileCollector`, automatic start,
  LocalSystem, restart-on-failure). Collection starts at boot and survives
  logoff, which the 1.1 scheduled task did not do reliably. The installer removes
  that task so the same folder is not collected twice. `--service` selects the
  mode; the SCM passes it.
- **System-tray application** (`LogFileCollectorTray.exe`), started with Windows
  per user. Live status plus open dashboard, rescan now, edit configuration,
  start/stop service and a *Start with Windows* toggle. It holds no collector
  logic — it polls the service's dashboard API over loopback — which is how a
  service isolated in session 0 can still have a UI.
- **WATS branding.** The official `WATSBee.ico` as the application, tray and
  Add/Remove Programs icon, and installer wizard artwork rasterised from the
  official `WatsLogoYellow.svg` (the yellow hexagon with the bee).

### Fixed

- **A missing or unreachable source folder no longer kills the process.** 1.2 and
  earlier threw out of `FileSystemWatcher` and exited. As a service that is much
  worse than it sounds: the SCM restarts it straight back into the same crash,
  forever — and the shipped default, `C:\Logs\Input`, does not exist on a fresh
  machine, so that was the out-of-the-box experience. The folder is now polled
  every 30 seconds, the watcher attaches when it appears, and the folder is swept
  once on attach so files that arrived in the meantime are not missed. The same
  recovery handles a watcher that dies mid-run on buffer overflow or a dropped
  network share.

### Changed

- Startup is now one code path (`CollectorHost`) shared by console mode, the
  service and the scheduled task, rather than three.

## [1.2] — 2026-09-29

### Added

- **Built-in web dashboard.** The collector now serves its own UI on
  `http://localhost:8787/` — live status, a files-per-day chart, the copy
  history, the log tail, a one-click rescan and a validated `appsettings.json`
  editor. Built on the WATS Portal design system, dark and light themes.
  Disable it with `"Dashboard": { "Enabled": false }`.
- **`--config <path>`** command-line option, to run against a configuration file
  outside `ProgramData` — for testing, or for more than one collector per host.
- **.NET 8 target.** The same collector now runs on Linux and macOS. The Windows
  installer and scheduled task continue to deploy the .NET Framework 4.8 build.
- **MSI installer** (`WATS-LogFileCollector-Setup.msi`, WiX 5), for GPO/Intune
  deployment and silent install, with an install wizard. Upgrades in place and
  leaves `appsettings.json`, the dedup database and the logs alone. The Inno
  Setup script is kept for the 1.1 release path but is now secondary.

### Changed

- The project is now SDK-style and multi-targets `net48;net8.0`. Framework
  output moved from `bin\Release\` to `bin\Release\net48\`, and the installer
  script follows it. Build with `dotnet build -c Release -f net48`.
- The tracking database gained `TargetPath` and `CopiedAtUtc` columns, added by
  in-place migration — a `copied.db` written by 1.1 is upgraded on first run and
  keeps all its existing rows.
- A full rescan can no longer run twice concurrently.

### Security

- The dashboard has no authentication and binds loopback only by default. Every
  state-changing request (rescan, config write) is refused unless it arrives from
  a loopback address, so widening `UrlPrefix` for remote monitoring exposes a
  read-only view rather than a config editor. `"AllowConfigEdit": false` disables
  config writing outright.

## [1.1] — 2025

- First tested release: folder watching with optional recursion, SQLite-backed
  duplicate prevention, rename strategies, Serilog logging, `--rescan` and
  `--reset`, Inno Setup installer with an optional boot-time scheduled task.
