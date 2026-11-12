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

`https://utilities.<home domain>` on the home server: systemd `utilities` on `127.0.0.1:5082`, database `Utilities`
on the shared SQL Server, files in `deploy/utilities/` (`HOST-SETUP.md` has every step).
`deploy/utilities/deploy.sh` really deploys: never run it to test it; check it with `bash -n` and by reading.

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
  ViewModels/  *Input classes for the forms: ReadingInput, MeterInput
  Services/    ReadingCalculations (each reading's units, days and units a day, and which meter a date belongs to;
               pure, unit tested), UtilityReadings (loads a utility's meters and readings, worked out: for its page
               and the dashboard), UtilityUnits (unit symbols, icons, and how readings and daily figures are shown:
               water in litres a day), UtilityRouteConstraint ({utility:utility} in a page's route),
               Import/ (ReadingsWorkbook reads the spreadsheet; ReadingsImporter plans and imports it; the
               reading and planning are pure, unit tested),
               PolynomialTrend (a least-squares polynomial fit; pure, unit tested), UtilityChart (a utility's chart
               data as JSON: units a day by date and the order 4 trendline)
  Data/        AppDbContext (per-user query filters; AuditedDbContext from Common.Web), DesignTimeDbContextFactory, Migrations/
  Monitoring/  AppHealth (database probes; the heartbeat source), UtilitiesAlertMonitor (database, and Common's
               BackupMonitor for backups and off-site copies), UtilitiesAlertOptions
  Pages/       Index (dashboard: each utility's latest reading and daily figure), UtilityPageModel (base for a
               utility's pages, at /{utility}/…: /Electricity, /Gas, /Water), Readings/* (Index is the utility's
               page: the add form, the readings table and the meters; Edit, Delete; ReadingRules), Meters/* (Create
               at /{utility}/Meters/New, Edit with its Delete handler; MeterRules), Import/ (at /Import), Account/* (sign in/out),
               Shared/ (layout)
  wwwroot/     css/site.css, js/color-mode.js (light/dark/system, as Vehicle Manager), js/tooltips.js (every
               title="…" becomes a Bootstrap tooltip: pages just write title), js/utility-chart.js (Chart.js
               4.4.4 from jsDelivr, as the other web apps: the utility page's chart, a tick a year), favicon.svg/.ico + icons/ +
               site.webmanifest (made by ../make-icons.py from Bootstrap Icons' speedometer2 on the navbar's dark
               grey; re-run it to change them: needs rsvg-convert, brew install librsvg)
```

## Data rules

- **Meters** belong to the signed-in user (`IUserOwned`); readings are filtered through their meter.
- A utility has one meter at a time. A replacement has an **installed on** date and a **start reading** (usually
  0); a utility's first meter has neither, and its first reading is only where counting starts.
- **Readings:** one per meter and date (unique index), `decimal(12,3)`. A meter with readings can't be deleted (FK
  `Restrict`). A reading belongs to the latest meter installed on or before its date
  (`ReadingCalculations.MeterFor`); the forms never ask for the meter, and an edit that changes the date can move it.
  A utility's first reading adds its first meter. A meter only counts up: a reading can't be lower than the one before
  it on its meter (or the meter's start reading) or higher than the one after (`ReadingRules`).
- **Units** are the reading less the one before on the same meter (a replacement's first: less its start reading;
  none for a utility's very first reading). **Days** are since the utility's reading before, even on the meter before,
  as the spreadsheet had it.
- **A replacement meter** needs its installed date and start reading; adding or editing one must leave every reading
  on the meter it was taken from (`MeterRules`), so it's installed after the last reading of the meter before and no
  later than its own first. A utility's first meter has only a serial number to edit.
- Utility and unit are enums stored as text, so order and group them in memory, not in SQL.
- **Figures are worked out, never stored** (units, days, units a day, the trendline): see the design notes.
- **The chart** plots units a day at each reading's date (water in litres), and the trendline is fitted against the
  date (`DateOnly.DayNumber`, scaled to -1..1 before fitting: day numbers to the 8th power would swamp a double). No
  chart, and no Chart.js on the page, with fewer than two daily figures.
- Format text outside a request with en-GB explicitly: CI runs in US English.
- **Backups, alerts, heartbeat** as the other web apps', through Common (`AddBackups<SqlServerBackupSource>`,
  `AddAlerting`, `AddHeartbeat`) with `CollectorIdentity("Utilities")`: the backup's database is
  `ConnectionStrings:Utilities`, its file `utilities-latest.bak`, the off-site prefix `utilities`. All off until
  configured; backups at 04:00. No scheduled jobs of its own, so the monitor only checks the database and backups.
  The tests turn the monitor on (`AppFactory`), with nothing to send to.
- **Import** (`Services/Import/`, `Pages/Import/`): upload, preview, import; nothing is kept between the steps but
  the workbook itself, base64 in the preview's form (limit 2 MB), so the import reads exactly what was previewed. A
  utility's block is a title starting with its name above the headings Date and Reading (found in the first ten
  rows); the first block per utility counts. Cells are read as Excel saved them (`CachedValue`), or worked out if
  never saved (the tests' workbooks); readings are rounded to three places. A utility with no meters gets them from
  the sheet (a new one, at 0, where the reading falls); one with meters only gains readings on new dates that fit the
  meter their date belongs to. The preview compares units a day with the sheet's own Units/Day. It's all one
  `SaveChanges`. Log counts only.
- **Never commit the real spreadsheet or a bill,** or put their figures in tests: `TestWorkbook` builds made-up
  sheets in code.
