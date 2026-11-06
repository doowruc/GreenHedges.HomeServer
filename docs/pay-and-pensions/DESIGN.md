# Pay & Pensions: design notes

A web app for my household's payslip history and, later, our pension pots. Both have lived in Excel workbooks for
years; this replaces them. Part of [GreenHedges.HomeServer](../../README.md), built the same way as
[Vehicle Manager](../vehicle-manager/README.md). This is the agreed plan; [Build order](#build-order) says how far
it has got, and the [README](README.md) how to run it.

## Goals and constraints

**Goals, in priority order:**
1. **Payslips:** record every monthly payslip for each person, with the figures the workbooks work out (gross,
   deductions, net, annual salary and rises, pension %, take-home %, year to date, tax-year totals).
2. **Import:** bring in each person's existing workbook once, checked against the workbook's own totals.
3. **Pensions (later):** track each person's pension pots, fed by the contributions already on the payslips.
4. **Documents (later):** payslip PDFs, P60s and P11Ds.

**Constraints:**
- **One login, several people.** Only I sign in; each person (two of us) is a record in my data, as owners are in
  Vehicle Manager. Each person's payslips are kept apart and shown one person at a time.
- **Same conventions as Vehicle Manager:** Razor Pages, EF Core on the server's SQL Server, Auth0 sign-in limited by
  `AllowedEmails`, audit columns and optimistic concurrency, pages that never bind entities, nightly backups pulled to
  the Mac, encrypted off-site copies, email alerts and a heartbeat, tailnet only.
- **Public repository.** Tests and docs use made-up people and figures only, including an invented test workbook.
  Real workbooks and personal setup stay out of the repository (`notes/pay-and-pensions/` in the private notes).

## The workbooks

One sheet per person, one row per payslip, oldest first:

| Columns | What |
|---|---|
| Year, Period | The tax year, given as the year it **ends** (2019 = 2018-19), and the tax month (1-12) |
| Date | Pay date (usually the 25th, earlier in December) |
| Earnings | One column per kind of pay: salary, adjustments, allowances, overtime, back pay (can be negative), bonuses |
| Deductions | Tax, NI, employee pension, other |
| ER Pension | The employer's pension contribution: for information, not part of gross or net |
| Transfer Date | Since a pension change: when the month's contributions reached the pension provider (paid in by hand) |
| Notes | Text, or a formula (a bonus as a % of salary) |
| Formula columns | Gross, Deductions, Net, Annual (salary × 12), Rise (£, %), pension %, take-home %, YTD, Predicted (12 ÷ period × YTD); on each month-12 row, the tax year's totals and their change on the year before |

Quirks: a few amounts are formulas (`=26+74`); cached values carry float noise; rows for the rest of the current tax
year exist with no date; a few cells beside the table hold one-off workings. Each person's workbook has its own set of
earnings columns, so the columns can't be fixed in the schema.

## Data model

| Entity | What |
|---|---|
| `Person` | Name (unique per user). The root: `IUserOwned`, with a per-user query filter |
| `Employer` | Belongs to a person: name. Payslips from a job change stay comparable |
| `PayElement` | Belongs to a person: name, **kind** (Earning, Deduction, Employer contribution), **category** (Salary, Bonus, Allowance, Overtime, Back pay, Tax, NI, Pension, Other), sort order, hidden, **salary sacrifice** (pension deductions only). One per workbook column |
| `Payslip` | Person, employer, tax year (stored as its start year), tax month, pay date, **pension received** date (the workbook's Transfer Date), **pension** (where its pension lines go; optional, only with a pension line), notes. Unique per person, tax year and month |
| `PayslipLine` | Payslip, pay element, amount (`decimal(19,2)`) |
| `PensionProvider` | The pension company: name, website. Belongs to the user (`IUserOwned`), not a person, as both of us can have pensions with the same provider |
| `Pension` | Belongs to a person: provider, name, policy number, **type** (Workplace, SIPP, Personal pension, Stakeholder, Other), optional **start date**. Payslips point at the pension their pension lines go to |
| `StatementType` | Belongs to a pension: name (e.g. "Investment report", "Annual illustration"), **has projections** |
| `PensionStatement` | Belongs to a statement type: date (one per type), label (e.g. "Spring 2026"), value, and with projections the projected fund at retirement and annual income; notes |

Every table inherits `AuditedEntity`. Child tables are filtered through their person; providers through the user.

**Derived, never stored** (a calculation service with unit tests): gross, deductions, net; annual salary (Salary
category × 12) and each rise in £ and %; employee and employer pension as % of salary; take-home %; year-to-date gross
and the predicted annual figure; bonus as % of annual salary; and per tax year: gross, pension, net, take-home %,
change on the year before, and **higher-rate pension relief** (20% of the year's employee pension contributions, the
extra relief reclaimed through Self Assessment), leaving out pension paid by salary sacrifice, which comes off before
tax and so already has full relief.

**Statements, derived** (per type, oldest first, as the pensions workbook did): the change on the previous statement
(the first's from 0 at the pension's start), growth (change ÷ previous), below the type's highest, days since the
start and the value per day, days since the previous and the change per day, and monthly income (annual ÷ 12).

**Room for later:** each payslip with pension lines points at the pension they go to, so payslip contributions can
feed its history without typing them twice, and the pension received date becomes the date a contribution reached it. Documents attach to a payslip (payslip PDF) or a person's tax year (P60, P11D).

## Pages

- **Dashboard:** choose a person; the latest payslip, this tax year to date and predicted, gross and net over time
  (Chart.js), totals by tax year.
- **Payslips:** one table per tax year, laid out like the workbook with the derived columns (DataTables).
- **Add / edit payslip:** a new payslip starts from the previous month's lines, so usually only a few figures change.
- **Tax years:** totals, change on the year before, take-home %, pension and relief.
- **Pension providers** (shared) and each person's **Pensions**.
- **People, Employers, Pay elements; Import; Backup; Settings.** The menu groups **Pay** and **Pensions**.

## Import

Upload an `.xlsx` (ClosedXML) and choose the person. From the header row:

- **Recognised by name:** Year, Period, Date, Transfer Date, Notes.
- **Ignored:** the formula columns, from Gross onwards, and anything right of them.
- **Every other column** becomes a pay element. A preview shows each with a guessed kind and category (before Tax:
  Earning; Tax to Other: Deduction; ER Pension: Employer contribution), to change before anything is saved.

Rows use the cells' evaluated values; tax year = Year − 1; text notes are kept, formula notes dropped; rows without a
date are skipped; tax months that already exist for that person are skipped, so a re-run adds only new rows.
**Self-check:** each row's net and each tax year's totals are compared with the workbook's own Net and totals
columns, and differences are listed.

## Hosting

| Thing | Value |
|---|---|
| Code | `src/PayAndPensions/GreenHedges.HomeServer.PayAndPensions`, `tests/PayAndPensions/…Tests` |
| Docs, deploy | `docs/pay-and-pensions/`, `deploy/pay-and-pensions/` (systemd unit, deploy script, database setup, host setup) |
| Service | systemd `payandpensions` (user `payandpensions`, in `sqlbackups`), `127.0.0.1:5081` |
| Address | `https://pay.<home domain>` through Caddy (a new block in `deploy/caddy/Caddyfile`) |
| Database | `PayAndPensions` on the shared SQL Server, login `payandpensions` (owner) |
| Folders | `/opt/payandpensions`, `/var/lib/payandpensions` (keys, backups, alert state), `/etc/payandpensions/env` |
| Backups | Nightly SQL Server backup, `*.bak.gz` in `/var/lib/payandpensions/backups`, 7 kept |
| Off-site | `age`-encrypted copies to the shared bucket under `payandpensions/`, by a new write-only IAM user in `deploy/aws/offsite.yaml` |
| Mac | Daily pull to `~/Backups/PayAndPensions` (a `payandpensions` case in `deploy/mac/pull-backups.sh` and `install-backup-pull.sh`) |
| Monitoring | healthchecks.io heartbeat; Resend alerts |
| Telemetry | OpenTelemetry logs, traces and metrics over OTLP to the server's Grafana (through Alloy into Loki, Tempo and Prometheus), set up in `Common.Web` and shared with Vehicle Manager. No Aspire: locally, with no OTLP endpoint set, logs go to the console only |

## Build order

One branch and PR each:

**Progress:** steps 1 (PR #47), 2 (PR #48), 3 (PR #50), 4 (PR #51), 5 (PR #52), 6 (PR #53) and 7's deploy files done, and telemetry to Grafana (PR #49). Next: deploying it.

1. **Shared groundwork.** Move what both web apps need out of Vehicle Manager into a shared project: the SQL Server
   backup source, Auth0 sign-in with `AllowedEmails`, the current user, audit columns and concurrency helpers.
   Vehicle Manager behaves exactly as before.
2. **Skeleton.** The project, data model, first migration, test project; People, Employers and Pay elements pages.

   **Then, before step 3: telemetry to Grafana** (its own PR, for both web apps). On the server, Grafana Alloy receives
   OTLP and stores logs in Loki, traces in Tempo and metrics in Prometheus, in Docker on 127.0.0.1 like Grafana, as its
   data sources. Vehicle Manager's OpenTelemetry setup moves into `Common.Web` for both apps, and its Aspire AppHost
   (with `aspire.config.json`) goes.
3. **Payslips.** Add and edit (starting from the previous month), the per-tax-year tables, the calculation service.
4. **Import,** with the preview and self-check (checked first against the other person's workbook headings).
5. **Dashboard and tax years,** with charts.
6. **Backups, off-site copies, Mac pull, alerts and heartbeat.**
7. **Deploy.** Deploy files, Caddy block, database setup, host setup; the server's notes updated.

**Pensions,** one PR each:

1. **Providers and pensions:** the pension companies (shared by the people) and each person's pensions, with their pages
   and the Pensions menu. Done.
2. **Payslips to pensions:** an optional pension on each payslip with a pension line (the latest payslip's by default),
   a Pension column on the payslips table when a year has more than one, and setting it on a range of payslips at once.
   Done.
3. **Statements:** each pension's statement types (named, with or without projections) and statements, a table per
   type with the workbook's worked-out figures, and the pension's start date. Done.
4. **Pasting statements in:** a type's history pasted from a spreadsheet's columns, with a preview, to bring in the
   pensions workbook once. Done.

**Later:** documents (payslip PDFs, P60s, P11Ds, statement PDFs); pension values (designed from the pensions workbook when we get
there); a JSON export.
