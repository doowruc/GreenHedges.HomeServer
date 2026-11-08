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
               Payslip, PayslipLine, PensionProvider (user-owned, shared by the people), Pension (+ PensionType),
               StatementType (a pension's kinds of statement), PensionStatement, PensionTransaction (+ TransactionKind),
               PensionFund (a fund's ISIN), FundPrice (daily prices, shared)
  ViewModels/  *Input classes for the forms (PayslipInput with a PayslipLineInput per pay element)
  Services/    TaxYears (6 April to 5 April; labels "2026-27", tax months), PayCalculations (each payslip's figures and
               each tax year's totals, as the spreadsheets worked them out; pure, unit tested), StatementCalculations
               (each statement's change, growth and per-day figures; pure, unit tested),
               Import/ (PayslipWorkbook reads a spreadsheet; PayslipImporter plans and imports it; StatementPaste reads
               statements pasted from one; TransactionCsv reads a provider's transaction export and picks the new
               lines; pure, unit tested),
               Holdings (each fund's units, cost and value from a pension's purchases and sales; pure, unit tested),
               ContributionMatching (which payslips each pension contribution came from; pure, unit tested) and
               PensionPayslips (runs it for a pension and sets the payslips' pension received date),
               PayHistory (a person's payslips and tax years, worked out: for the dashboard and Years)
  Data/        AppDbContext (per-user query filters; AuditedDbContext from Common.Web), DesignTimeDbContextFactory, Migrations/
  Monitoring/  AppHealth (database probes; the heartbeat source), PayAndPensionsAlertMonitor (database, and Common's
               BackupMonitor for backups and off-site copies), PayAndPensionsAlertOptions
  Pages/       Index (dashboard: one person at a time, ?person=), PayCharts (the charts' JSON), People/*, Employers/*,
               PayElements/*, Payslips/*, Pensions/* (add and edit share PensionFormModel; Assign sets one on a range
               of payslips), Statements/*, StatementTypes/*, Transactions/* and Holdings/ (a pension's, at /People/{id}/Pensions/{pensionId}/…,
               through PensionPageModel), Import/ and Years/ (tax years, at /People/{id}/TaxYears; the folder isn't
               TaxYears, which would hide the TaxYears class) (per person, under
               /People/{personId}/…, through PersonPageModel; Payslips' add and edit share PayslipFormModel),
               PensionProviders/* (at /PensionProviders: not a person's), Account/* (sign in/out), Shared/ (layout;
               PersonSwitcherViewComponent, the navbar's people switcher: the same page for another person;
               PensionsMenuViewComponent, the Pensions menu: providers, then each person's pensions), Names (name tidying)
  wwwroot/     css/site.css, js/color-mode.js (light/dark/system, as Vehicle Manager), js/payslip-totals.js (live totals
               on the payslip form), js/pay-charts.js (Chart.js 4.4.4 from jsDelivr, as Vehicle Manager), js/tooltips.js (every
               title="…" becomes a Bootstrap tooltip: pages just write title), js/row-menu.js (the payslips and transactions tables'
               right-click menu, beside each row's buttons: rows with data-row-menu, data-edit and data-delete), favicon.svg/.ico + icons/ + site.webmanifest
               (made by ../make-icons.py from Bootstrap Icons' cash-stack on the navbar's dark grey; re-run it to change
               them: needs rsvg-convert, brew install librsvg)
```

## Data rules

- **People** and **pension providers** belong to the signed-in user (`IUserOwned`); everything else is filtered
  through its person. Names of people and providers, and of a person's employers, pay elements and pensions, are unique
  ignoring case, with spaces tidied (`Names`).
- **Pensions:** a person's pension with one of the user's providers (several people can share a provider): name, type
  (`PensionType`, stored as text), optional policy number. A provider's website is an http(s) address (https:// is
  added if typed without). A provider can't be deleted while pensions are with it, nor a pension while payslips go to
  it (FK `Restrict`).
- **Statements:** a pension has statement types (named per pension, e.g. "Investment report"; unique per pension),
  each with or without **projections** (fund at retirement, annual income). A statement is a type's value on a date
  (one per type and date), with an optional label and notes; projections only on a type with them. Worked out, never
  stored (`StatementCalculations`, per type, oldest first): the change on the previous (the first's from 0 at the
  pension's optional **start date**), growth (change ÷ previous), below the type's highest, days since the start and
  value per day, the period's days and change per day; monthly income is annual ÷ 12. A type with statements can't be
  deleted (FK `Restrict`), so nor can its pension or person; a pension's types go with it.
- **Pasting statements** (`Statements/Paste`, `StatementPaste`): tab-separated rows as Excel copies them, from the
  first row with Date and Value headings (a title above is ignored); columns by heading (Date, Value, Label/When, and
  for projections Retirement/Fund and Annual/Annual income), others ignored. UK dates or Excel serial numbers; amounts
  with or without £ and commas. Unreadable rows are skipped with the reason; dates the type has are left alone; a 0
  first row is offered as the pension's start date (when it has none). Paste, preview, import: only the text is kept
  between steps, so the import reads what was previewed. Log counts only.
- **Transactions** (`Transactions/Import`, `TransactionCsv`): a pension's lines from the provider's CSV exports,
  several files at once. The lines above the heading row (Trade date and Value) hold the client's name and number:
  never store, log or show them. Columns by heading; UK dates; amounts with commas; "n/a" is none; UTF-8 or
  Windows-1252 (£ is one byte). A line's **kind** and **fund** are worked out from its reference and description and
  can be changed (Edit); nothing else can, as an export has no id: a transaction is its trade date, reference,
  description and value, and a file adds each as many times as any one file has it, less those already there (so
  overlapping files count once, but two identical lines in one file both count). Upload, preview, import: only the
  files are kept between steps (base64, 2 MB together). Log counts only. The list is by tax year, latest first, with
  totals per kind, paid in (contributions and transfers in) and the change in cash. A pension (or person) with
  transactions can't be deleted (FK `Restrict`). A line with units whose description is a deal ("fund units @ price",
  perhaps with a note such as "Fee Sale -" before the units) is a purchase or sale whatever the fund's name says.
- **Holdings** (`Holdings`, `Holdings/Index`): each fund's units from its purchases (adding) and sales (taking away;
  the exports give both as positive quantities), as at a date; cost is average cost (a sale takes off its share, whatever it raised), value is the units at the
  fund's latest deal's price (pence), so only as recent as that deal. Purchases or sales without a fund aren't counted,
  and the page says how many. A **conversion** (`Holdings/Convert`; the exports leave them out) is two lines of kind
  Conversion with reference `CONVERSION` on its date: the old fund's units held then (negative quantity, value its
  cost) and the units received of the new one (value minus that cost), so cost moves and cash doesn't change. It
  can't be edited, nor can a line become one; deleting either line deletes both.
- **Daily prices** (`FundPrices`, `FundPriceJob`, `EodhdPriceSource`, `Holdings/Isin`): a pension's fund can have an ISIN
  (`PensionFund`). Prices (`FundPrice`: ISIN and date, in pence) are public data shared by ISIN, with no owner or audit
  columns. Each ISIN asks for everything since its last price (a year back on the first), when saved and at startup and
  08:00 UK time; 20 calls a day on the free tier. Off until `Eodhd:ApiToken` is set. The key is in the request's URL, so
  that URL must never reach a log or trace (`Program`'s log filter, `Redact`, and logging only an exception's type). A
  fund's price is the latest on or before the date shown, unless its last deal is newer; the value uses the price
  rounded to 2 places, as the provider's does.
- **Security movements** (`TransactionCsv.Read` spots them by a heading row with Date, Type and Total cost; the same
  Import page): one fund's export, named on its first line. Corp.A lines become **cost adjustments** (kind
  CostAdjustment, value the cost taken off, no units); Conversion in/out become a conversion with the provider's cost
  and reference, replacing one recorded by hand on that date. Bought and Sold are ignored: the history has them, with
  exact units. A conversion's unit cost is a book cost, so it's never stored as a price, and a conversion away within a
  unit of what's held takes it all, as these files round down. Conversions and cost adjustments aren't in the change in
  cash. The lines above the heading hold the client's name and number, as in the history: never store, log or show them.
- **Contributions to payslips** (`ContributionMatching`, `PensionPayslips`): an import, or the Transactions page's
  Match payslips, sets the pension received date on the pension's payslips without one. A contribution (kind
  Contribution, positive) matches the oldest run of such payslips in a row by pay date, up to 6, paid in the 120 days
  before it, whose pension lines (`PayElement.IsPension`: employee's and employer's) add up to it exactly. A contribution
  on a date one of the pension's payslips was already received is taken as matched. The import's preview lists the
  matches and counts the contributions that match nothing; the dates save with the transactions, in one `SaveChanges`.
- **A payslip's pension** (optional) is where its pension lines go, employee's and employer's: only on a payslip with
  a pension line (`PayElement.IsPension`: Pension category, not an earning). The payslips table has a Pension column
  when a year's payslips with pension lines differ (or some have none). `Pensions/Assign` sets one on a range of
  tax months at once, replacing others: it says how many first, and checks again if that changed before confirming.
- **Pay history is never deleted along with something else.** Deleting a person takes their employers, pay
  elements and pensions (FK cascade), but not while they have payslips; an employer with payslips, or a pay element on a payslip,
  can't be deleted either (FK `Restrict`, and the Delete pages say so). Unused pay elements are hidden instead.
- **Payslips:** one per person and tax month (unique index; a check constraint keeps `TaxMonth` 1-12). `TaxYear` is the
  year the tax year starts (2026 = 2026-27). Amounts are `decimal(19,2)` and can be negative.
- A pay element's **kind** says whether it adds to gross, comes off it, or is the employer's (in neither); its
  **category** drives the derived figures (annual salary, pension %, bonus %). Both enums are stored as text, so order
  them in memory, not in SQL.
- **Figures are worked out, never stored** (`PayCalculations`): gross, deductions, net, annual salary (Salary × 12)
  and rises, pension % of salary, take-home %, year to date and the predicted year (12 ÷ tax month × year to date, from
  recorded payslips only), bonus % of annual salary; per tax year, totals, the change on the year just before, the annual salary at its start
  and end (its first and last payslips with a salary) and the increase, and
  higher-rate pension relief (20% of employee pension, except pay elements marked **salary sacrifice**, which only a
  pension deduction can be: it already has full relief). Pages pass payslips oldest first, with the year before's for
  the first rise. A year's change is on the **same tax months** of the year before, so a year in progress compares like
  for like.
- Format text outside a request (services, the import's messages) with en-GB explicitly: CI runs in US English.
- **Backups, alerts, heartbeat** as Vehicle Manager's, through Common (`AddBackups<SqlServerBackupSource>`,
  `AddAlerting`, `AddHeartbeat`) with `CollectorIdentity("PayAndPensions")`: the backup's database is
  `ConnectionStrings:PayAndPensions`, its file `payandpensions-latest.bak`, the off-site prefix `payandpensions`. All off
  until configured; backups at 03:45. No scheduled jobs of its own, so the monitor only checks the database and
  backups. The tests turn the monitor on (`AppFactory`), with nothing to send to.
- **Payslip form:** one amount per pay element; blank means no line. Element, employer and pension ids come back from
  the form, so they're checked against the person's own. A new payslip starts from the latest one (next tax month, same
  employer, pension and amounts, pay date a month on), leaving hidden elements off. The pay date must be in the tax year. An edit always saves the
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
