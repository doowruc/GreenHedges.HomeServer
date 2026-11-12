# Utilities

Personal .NET 10 Razor Pages app for my home's electricity, gas and water meter readings (and later the bills). Part
of GreenHedges.HomeServer: the repo-wide rules (Git, privacy, the server) are in the root `CLAUDE.md`; this file is
only about this app. The plan and build order are in `docs/utilities/DESIGN.md`; setup is in
`docs/utilities/README.md`.

It's built the same way as Vehicle Manager and Pay & Pensions, and shares their plumbing through
`GreenHedges.HomeServer.Common.Web` (sign-in, audit columns, concurrency, migrations, `/health`, SQL Server backups,
OpenTelemetry). Its telemetry goes to Grafana as `service_name` `Utilities` when `OTEL_EXPORTER_OTLP_ENDPOINT` is
set; never log readings, serial numbers or account details. Where this file says nothing, follow
`src/VehicleManager/CLAUDE.md`'s conventions: pages never use entity classes (`*Input` and projections), edits use
`TrySaveEditAsync`, deletes use `ExecuteDeleteAsync`, use `FirstOrDefaultAsync` rather than `FindAsync`, and every
schema change is an EF Core migration.

## Commands

Run from `GreenHedges.HomeServer.Utilities/` (the project folder, next to this file):

```bash
dotnet watch                 # run with hot reload → http://localhost:5082
dotnet watch -lp https       # same over https → https://localhost:7082 (needed for sign-in in Safari)
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

From the repo root:

```bash
dotnet test tests/Utilities/GreenHedges.HomeServer.Utilities.Tests   # this app's tests; must all pass
docker compose up -d --wait  # local SQL Server (compose.yaml), shared with the other web apps
```

## Server

Not deployed yet (build step 5). It will be `https://utilities.<home domain>`: systemd `utilities` on
`127.0.0.1:5082`, database `Utilities` on the shared SQL Server, files in `deploy/utilities/`.

## Tests

`GreenHedges.HomeServer.Utilities.Tests` (in `tests/Utilities/`, xUnit), laid out like the other web apps': `Data/`
(EF Core against a throwaway database), `Pages/` (the app in memory through `Infrastructure/AppFactory` and
`PageClient`, with a test sign-in) and `Unit/` (pure logic). The throwaway database is created on the SQL Server in
`ConnectionStrings:UtilitiesTests` (`Utilities_Tests_<time>_<id>`); without it, database and page tests are skipped.
Derive database tests from `DatabaseTest` and mark them `[DatabaseFact]`/`[DatabaseTheory]`. Never point tests at
the real database. **Test data is made up** (`Infrastructure/TestData`): never real readings, serial numbers or
bills, and never the real spreadsheet.

## Layout

```
GreenHedges.HomeServer.Utilities/
  Models/      EF Core entities: Meter (the user-owned root; + UtilityType, MeterUnit), Reading
  Data/        AppDbContext (per-user query filters; AuditedDbContext from Common.Web), DesignTimeDbContextFactory, Migrations/
  Monitoring/  AppHealth (database probes; the heartbeat source), UtilitiesAlertMonitor (database, and Common's
               BackupMonitor for backups and off-site copies), UtilitiesAlertOptions
  Pages/       Index (dashboard: each utility's latest reading), Units (unit symbols and utility icons),
               Account/* (sign in/out), Shared/ (layout)
  wwwroot/     css/site.css, js/color-mode.js (light/dark/system, as Vehicle Manager), js/tooltips.js (every
               title="…" becomes a Bootstrap tooltip: pages just write title), favicon.svg/.ico + icons/ +
               site.webmanifest (made by ../make-icons.py from Bootstrap Icons' speedometer2 on the navbar's dark
               grey; re-run it to change them: needs rsvg-convert, brew install librsvg)
```

## Data rules

- **Meters** belong to the signed-in user (`IUserOwned`); readings are filtered through their meter.
- A utility has one meter at a time. A replacement has an **installed on** date and a **start reading** (usually
  0); a utility's first meter has neither, and its first reading is only where counting starts.
- **Readings:** one per meter and date (unique index), `decimal(12,3)`. A meter with readings can't be deleted (FK
  `Restrict`).
- Utility and unit are enums stored as text, so order and group them in memory, not in SQL.
- **Figures are worked out, never stored** (units, days, units a day, the trendline): see the design notes.
- Format text outside a request with en-GB explicitly: CI runs in US English.
- **Backups, alerts, heartbeat** as the other web apps', through Common (`AddBackups<SqlServerBackupSource>`,
  `AddAlerting`, `AddHeartbeat`) with `CollectorIdentity("Utilities")`: the backup's database is
  `ConnectionStrings:Utilities`, its file `utilities-latest.bak`, the off-site prefix `utilities`. All off until
  configured; backups at 04:00. No scheduled jobs of its own, so the monitor only checks the database and backups.
  The tests turn the monitor on (`AppFactory`), with nothing to send to.
- **Never commit the real spreadsheet or a bill,** or put their figures in tests.
