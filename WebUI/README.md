# LogFileCollector dashboard

The collector's web UI. `WebServer.cs` serves this folder from disk at runtime —
the whole folder is copied next to the executable on build.

Built from the WATS `wats-frontend` skill (`templates/dashboard.html`).

## Run

Not standalone: start the collector and open its dashboard URL
(`http://localhost:8787/` by default). Opening `index.html` from the file system
shows the shell with an "Offline" badge, because every number comes from the
collector's own `/api/*` endpoints.

## Rules

- Colours, fonts and spacing come from `assets/tokens.css`. No raw hex.
- Components are in the skill's `references/component-catalog.md`.
- Check before shipping: `node <skill>/scripts/check-tokens.mjs index.html`,
  then look at the page in **both** themes.
- There is no mock-data path — the page is live or it says it is offline.

## Layout

| Path | What it is |
| --- | --- |
| `index.html` | The whole dashboard: markup, page CSS, and the polling client |
| `assets/` | Design system — `tokens.css`, `wats.css`, `wats.js`, brand SVGs. Copied from the skill; update by re-copying, not by editing |
| `vendor/echarts.min.js` | ECharts 5.5.0, vendored so the charts work offline |

## API the page consumes

| Endpoint | Purpose |
| --- | --- |
| `GET /api/status` | Live status, watch configuration, session totals |
| `GET /api/files?limit=&q=` | Copy history from SQLite, newest first |
| `GET /api/activity?days=` | Files copied per UTC day, zero-filled |
| `GET /api/log?lines=` | Tail of the current Serilog file |
| `GET /api/config` | Raw `appsettings.json` and whether it may be edited |
| `PUT /api/config` | Validate and write `appsettings.json` (loopback only) |
| `POST /api/rescan` | Start a full rescan in the background (loopback only) |
