# Presents

Personal .NET 10 Razor Pages app for what we spend on the children's Christmas presents: the purchases (with the
big ones split with my mum or with a birthday) and the cash that balances each child, worked out. Part of
GreenHedges.HomeServer: the repo-wide rules (Git, privacy, the server) are in the root `CLAUDE.md`; this file is only
about this app. The plan and build order are in `docs/presents/DESIGN.md`; setup is in `docs/presents/README.md`.

It's built the same way as Utilities, Vehicle Manager and Pay & Pensions, and shares their plumbing through
`GreenHedges.HomeServer.Common.Web` (sign-in, audit columns, concurrency, migrations, `/health`, SQL Server backups,
OpenTelemetry). Its telemetry goes to Grafana as `service_name` `Presents` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set;
never log the children's names, items or amounts. Where this file says nothing, follow
`src/VehicleManager/CLAUDE.md`'s conventions: pages never use entity classes (`*Input` and projections), edits use
`TrySaveEditAsync`, deletes use `ExecuteDeleteAsync`, use `FirstOrDefaultAsync` rather than `FindAsync`, and every
schema change is an EF Core migration.

## Commands

Run from `GreenHedges.HomeServer.Presents/` (the project folder, next to this file):

```bash
dotnet watch                 # run with hot reload → http://localhost:5083
dotnet watch -lp https       # same over https → https://localhost:7083 (needed for sign-in in Safari)
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

From the repo root:

```bash
dotnet test tests/Presents/GreenHedges.HomeServer.Presents.Tests   # this app's tests; must all pass
docker compose up -d --wait  # local SQL Server (compose.yaml), shared with the other web apps
```

## Server

Not deployed yet (build step 5). It will be `https://presents.<home domain>`: systemd `presents` on
`127.0.0.1:5083`, database `Presents` on the shared SQL Server, files in `deploy/presents/`.

## Tests

`GreenHedges.HomeServer.Presents.Tests` (in `tests/Presents/`, xUnit), laid out like the other web apps': `Data/`
(EF Core against a throwaway database), `Pages/` (the app in memory through `Infrastructure/AppFactory` and
`PageClient`, with a test sign-in) and `Unit/` (pure logic). The throwaway database is created on the SQL Server in
`ConnectionStrings:PresentsTests` (`Presents_Tests_<time>_<id>`); without it, database and page tests are skipped.
Derive database tests from `DatabaseTest` and mark them `[DatabaseFact]`/`[DatabaseTheory]`. Never point tests at
the real database. **Test data is made up** (`Infrastructure/TestData`): never the children's names, real presents
or amounts, and never the real workbook.

## Layout

```
GreenHedges.HomeServer.Presents/
  Models/      EF Core entities: Child and Christmas (the user-owned roots), Purchase (a Christmas's, for a child)
  ViewModels/  *Input classes for the forms: ChildInput, PurchaseInput, ChristmasInput (the budget)
  Services/    Shares (what a purchase counts as: price × quantity, halved for each split, to the penny; pure,
               unit tested), Summary (a Christmas's figures: each child's spent, the target, cash and total; pure,
               unit tested), ChristmasSummaries (loads them for a year, or every year, for the pages)
  Data/        AppDbContext (per-user query filters; AuditedDbContext from Common.Web), DesignTimeDbContextFactory, Migrations/
  Monitoring/  AppHealth (database probes; the heartbeat source), PresentsAlertMonitor (database, and Common's
               BackupMonitor for backups and off-site copies), PresentsAlertOptions
  Pages/       Index (a Christmas: this year's at /, another's at /{year}; a card per child with spent, cash and
               total, an over-budget warning, the add-a-present form,
               the year's presents, previous/next and a year picker), Purchases/* (Edit, which can move it to another
               year; Delete; _PurchaseFields, the form shared with Index; PurchaseRules), Christmases/* (Edit at
               /{year}/Edit: the budget; Delete at /{year}/Delete, with its presents), History/Index (every year:
               each child's spent and cash, each child's total, the year's), Children/* (Index with move
               up/down, Create, Edit, Delete; ChildRules), Account/* (sign in/out), Shared/ (layout)
  wwwroot/     css/site.css, js/color-mode.js (light/dark/system, as Vehicle Manager), js/tooltips.js (every
               title="…" becomes a Bootstrap tooltip: pages just write title), js/purchase-share.js (the
               form's "Counts as" as you type, rounded as Shares), favicon.svg/.ico + icons/ +
               site.webmanifest (made by ../make-icons.py from Bootstrap Icons' gift on the navbar's dark grey;
               re-run it to change them: needs rsvg-convert, brew install librsvg)
```

## Data rules

- **Children** and **Christmases** belong to the signed-in user (`IUserOwned`); purchases are filtered through their
  Christmas.
- **A child's name** is unique per user, ignoring case, with spaces tidied (`ChildRules`). **Display order** sets
  where they come everywhere: a new child goes last, and moving one renumbers them all 1, 2, 3…, so gaps never stop
  a move. A child with purchases can't be deleted (FK `Restrict`).
- **A Christmas** is one per user and year (2000-2999), with an optional budget per child (`decimal(10,2)`). Any
  year's page can be shown; its Christmas is made by its first present or by setting its budget, and a present moved
  to another year makes that one's. Deleting one deletes its purchases (FK `Cascade`).
- **A purchase** keeps the full price of one (`decimal(10,2)`), the quantity and the two splits (with Mum, with the
  birthday). Its year is its Christmas, never its date, which is optional and can't be in the future. The price has
  whole pence; text is tidied (spaces trimmed, an empty shop is none) and the child must be the user's
  (`PurchaseRules`). After adding one, the form comes back on the same child, shop and date.
- **A share** (what a purchase counts as) is price × quantity ÷ 1, 2 or 4, rounded to the penny with half a penny up
  (`Shares.Of`); a child's spend is the sum of their rounded shares.
- **Cash** (`Summary.For`): the target is the most any child spent, or the budget if there is one and it's more;
  each child's cash is the target less their spend, so every child's total is the target. Every child counts in
  every year, with or without presents. Over the budget, the target is the highest spend and the page says who.
- **Figures are worked out, never stored** (a purchase's share, spent, cash, totals): see the design notes.
- Format text outside a request with en-GB explicitly: CI runs in US English.
- **Backups, alerts, heartbeat** as the other web apps', through Common (`AddBackups<SqlServerBackupSource>`,
  `AddAlerting`, `AddHeartbeat`) with `CollectorIdentity("Presents")`: the backup's database is
  `ConnectionStrings:Presents`, its file `presents-latest.bak`, the off-site prefix `presents`. All off until
  configured; backups at 04:15. No scheduled jobs of its own, so the monitor only checks the database and backups.
  The tests turn the monitor on (`AppFactory`), with nothing to send to.
- **Never commit the real workbook** or put its names or figures in tests.
