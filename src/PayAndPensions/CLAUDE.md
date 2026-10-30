# Pay & Pensions

Personal .NET 10 Razor Pages app for my household's payslip history (and later pension pots). Part of
GreenHedges.HomeServer: the repo-wide rules (Git, privacy, the server) are in the root `CLAUDE.md`; this file is only
about this app. The plan and build order are in `docs/pay-and-pensions/DESIGN.md`; setup is in
`docs/pay-and-pensions/README.md`.

It's built the same way as Vehicle Manager, and shares its plumbing through `GreenHedges.HomeServer.Common.Web`
(sign-in, audit columns, concurrency, migrations, `/health`, SQL Server backups, OpenTelemetry). Its telemetry goes to
Grafana as `service_name` `PayAndPensions` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; never log names, employers or
amounts. Where this file says nothing, follow
`src/VehicleManager/CLAUDE.md`'s conventions: pages never use entity classes (`*Input` and projections), edits use
`TrySaveEditAsync`, deletes use `ExecuteDeleteAsync`, use `FirstOrDefaultAsync` rather than `FindAsync`, and every
schema change is an EF Core migration.

## Commands

Run from `GreenHedges.HomeServer.PayAndPensions/` (the project folder, next to this file):

```bash
dotnet watch                 # run with hot reload → http://localhost:5081
dotnet watch -lp https       # same over https → https://localhost:7081 (needed for sign-in in Safari)
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

From the repo root:

```bash
dotnet test tests/PayAndPensions/GreenHedges.HomeServer.PayAndPensions.Tests   # this app's tests; must all pass
docker compose up -d --wait  # local SQL Server (compose.yaml), shared with Vehicle Manager
```

## Server

`https://pay.<home domain>` on the home server: systemd `payandpensions` on `127.0.0.1:5081`, database
`PayAndPensions` on the shared SQL Server, files in `deploy/pay-and-pensions/` (`HOST-SETUP.md` has every step).
`deploy/pay-and-pensions/deploy.sh` really deploys: never run it to test it; check it with `bash -n` and by reading.

## Tests

`GreenHedges.HomeServer.PayAndPensions.Tests` (in `tests/PayAndPensions/`, xUnit), laid out like Vehicle Manager's:
`Data/` (EF Core against a throwaway database), `Pages/` (the app in memory through `Infrastructure/AppFactory` and
`PageClient`, with a test sign-in) and `Unit/` (pure logic). The throwaway database is created on the SQL Server in
`ConnectionStrings:PayAndPensionsTests` (`PayAndPensions_Tests_<time>_<id>`); without it, database and page tests are
skipped. Derive database tests from `DatabaseTest` and mark them `[DatabaseFact]`/`[DatabaseTheory]`. Never point
tests at the real database. **Test data is made up** (`Infrastructure/TestData`): never real names, employers or
figures, and never a real spreadsheet.

## Layout

```
GreenHedges.HomeServer.PayAndPensions/
  Models/      EF Core entities: Person (the user-owned root), Employer, PayElement (+ PayElementKind, PayCategory),
               Payslip, PayslipLine
  ViewModels/  *Input classes for the forms (PayslipInput with a PayslipLineInput per pay element)
  Services/    TaxYears (6 April to 5 April; labels "2026-27", tax months), PayCalculations (each payslip's figures and
               each tax year's totals, as the spreadsheets worked them out; pure, unit tested),
               Import/ (PayslipWorkbook reads a spreadsheet; PayslipImporter plans and imports it),
               PayHistory (a person's payslips and tax years, worked out: for the dashboard and Years)
  Data/        AppDbContext (per-user query filters; AuditedDbContext from Common.Web), DesignTimeDbContextFactory, Migrations/
  Monitoring/  AppHealth (database probes; the heartbeat source), PayAndPensionsAlertMonitor (database, and Common's
               BackupMonitor for backups and off-site copies), PayAndPensionsAlertOptions
  Pages/       Index (dashboard: one person at a time, ?person=), PayCharts (the charts' JSON), People/*, Employers/*,
               PayElements/*, Payslips/*, Import/ and Years/ (tax years, at /People/{id}/TaxYears; the folder isn't
               TaxYears, which would hide the TaxYears class) (per person, under
               /People/{personId}/…, through PersonPageModel; Payslips' add and edit share PayslipFormModel),
               Account/* (sign in/out), Shared/ (layout; PersonSwitcherViewComponent, the navbar's people switcher:
               the same page for another person), Names (name tidying)
  wwwroot/     css/site.css, js/color-mode.js (light/dark/system, as Vehicle Manager), js/payslip-totals.js (live totals
               on the payslip form), js/pay-charts.js (Chart.js 4.4.4 from jsDelivr, as Vehicle Manager), js/tooltips.js (every
               title="…" becomes a Bootstrap tooltip: pages just write title), favicon.svg/.ico + icons/ + site.webmanifest
               (made by ../make-icons.py from Bootstrap Icons' cash-stack on the navbar's dark grey; re-run it to change
               them: needs rsvg-convert, brew install librsvg)
```

## Data rules

- **People** belong to the signed-in user (`IUserOwned`); everything else is filtered through its person. Names of
  people, and of a person's employers and pay elements, are unique ignoring case, with spaces tidied (`Names`).
- **Pay history is never deleted along with something else.** Deleting a person takes their employers and pay
  elements (FK cascade), but not while they have payslips; an employer with payslips, or a pay element on a payslip,
  can't be deleted either (FK `Restrict`, and the Delete pages say so). Unused pay elements are hidden instead.
- **Payslips:** one per person and tax month (unique index; a check constraint keeps `TaxMonth` 1-12). `TaxYear` is the
  year the tax year starts (2026 = 2026-27). Amounts are `decimal(19,2)` and can be negative.
- A pay element's **kind** says whether it adds to gross, comes off it, or is the employer's (in neither); its
  **category** drives the derived figures (annual salary, pension %, bonus %). Both enums are stored as text, so order
  them in memory, not in SQL.
- **Figures are worked out, never stored** (`PayCalculations`): gross, deductions, net, annual salary (Salary × 12)
  and rises, pension % of salary, take-home %, year to date and the predicted year (12 ÷ tax month × year to date, from
  recorded payslips only), bonus % of annual salary; per tax year, totals, the change on the year just before, and
  higher-rate pension relief (20% of employee pension). Pages pass payslips oldest first, with the year before's for
  the first rise. A year's change is on the **same tax months** of the year before, so a year in progress compares like
  for like.
- Format text outside a request (services, the import's messages) with en-GB explicitly: CI runs in US English.
- **Backups, alerts, heartbeat** as Vehicle Manager's, through Common (`AddBackups<SqlServerBackupSource>`,
  `AddAlerting`, `AddHeartbeat`) with `CollectorIdentity("PayAndPensions")`: the backup's database is
  `ConnectionStrings:PayAndPensions`, its file `payandpensions-latest.bak`, the off-site prefix `payandpensions`. All off
  until configured; backups at 03:45. No scheduled jobs of its own, so the monitor only checks the database and
  backups. The tests turn the monitor on (`AppFactory`), with nothing to send to.
- **Payslip form:** one amount per pay element; blank means no line. Element and employer ids come back from the form,
  so they're checked against the person's own. A new payslip starts from the latest one (next tax month, same amounts,
  pay date a month on), leaving hidden elements off. The pay date must be in the tax year. An edit always saves the
  payslip row as changed, so its `RowVersion` guards the lines too.
- **Import** (`Services/Import/`, `Pages/Import/`): upload, preview, import; nothing is kept between the steps but
  the workbook itself, base64 in the preview's form (limit 2 MB), so the import reads exactly what was previewed.
  Heading row with Year (the tax year's **end**), Period, Date; pay columns from Date to Gross; Transfer Date →
  pension received; Notes (text only). Kinds come from the first row's Gross and Deductions `=SUM()` ranges (outside
  both: employer contribution), else columns from Tax on are deductions. A repeated heading gets its kind added.
  Formula cells are read as Excel saved them (`CachedValue`), or worked out if never saved (the tests' workbooks).
  Rows without a date are skipped quietly; with a bad Period or a date outside the tax year, skipped with the reason.
  Months the person already has are left alone; a column named like one of their pay elements uses it. The preview
  checks each row's gross and net against the sheet's own. It's all one `SaveChanges`. Log counts only.
- **Never commit a real workbook,** or put its figures in tests: `TestWorkbook` builds made-up ones in code.
