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
  ViewModels/  *Input classes for the forms
  Data/        AppDbContext (per-user query filters; AuditedDbContext from Common.Web), DesignTimeDbContextFactory, Migrations/
  Pages/       Index (dashboard), People/*, Employers/* and PayElements/* (per person, under /People/{personId}/…,
               through PersonPageModel), Account/* (sign in/out), Shared/ (layout), Names (name tidying)
  wwwroot/     css/site.css, js/color-mode.js (light/dark/system, as Vehicle Manager)
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
