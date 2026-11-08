# Pay & Pensions

A web app for my household's payslip history, and later our pension pots. Part of
[GreenHedges.HomeServer](../../README.md); the plan, data model and build order are in [DESIGN.md](DESIGN.md).

**So far** (build step 3): people, each person's employers, and the pay elements on their payslips (the lines, e.g.
Salary, Tax, Employer pension, each with a kind and a category; a pension deduction can be marked salary sacrifice,
which leaves it out of the higher-rate pension relief); and **payslips**, a table per tax year with the
figures the spreadsheets worked out and the year's totals. A new payslip starts from the latest one, so usually only
a few amounts change. **Importing** a person's existing spreadsheet (below). The **dashboard**, one person at a time:
their latest payslip, the tax year so far and predicted, and charts of gross and net by month and by tax year. And
**tax years**: every year's totals, with the change on the same months of the year before. **Pensions**: pension
providers (shared by the people, with their websites) and each person's pensions with them (type and policy number),
and the pension each payslip's pension amounts go to: chosen on the payslip (a new one takes the latest's), or set on
a range of payslips at once from the pension's **Assign** button, to fill in the history. Each pension's
**statements**, under its own statement types (e.g. a quarterly investment report and an annual illustration with
projections), a table per type with the change, growth and per-day figures worked out; a type's history can be
pasted in from a spreadsheet (below). And each pension's **transactions**, imported from the provider's CSV
exports (below), by tax year with totals per kind.

## Pasting statements in

On a pension's **Statements** page, a type's **Paste** button. In the spreadsheet, select the type's statements with
their heading row (a title row above is fine) and copy, then paste. Columns are found by heading: **Date** and
**Value**, **Label** or **When** if there is one, and for a type with projections **Retirement** (or Fund) and
**Annual** (or Annual income); any others, such as the sheet's own Change, are ignored. The preview shows what it
would import, with the change and growth worked out to check against the sheet's; rows it can't read, and why; and
dates the type already has (left alone, so pasting again adds only new ones). A first row with a value of 0 is taken
as the pension's start, and offered as its start date.

## Importing transactions

On a pension's **Transactions** page, **Import**. Choose one or more of the provider's transaction history exports
(.csv; one a year, say, as an export covers up to 12 months). Each needs a heading row with **Trade date** and
**Value**; the lines above it (the account's name and number) are ignored and never kept. Columns are found by
heading: Trade date, Settle date, Reference, Description, Unit cost (p), Quantity and Value (£). The preview shows the
transactions that are new, with each one's kind (contribution, transfer in, interest, fee, rebate, purchase or sale)
and fund worked out from its description; lines it can't read, and why; and how many the pension already has, which
are left out, so files can overlap and the latest can be imported again as it grows. A kind or fund that's wrong can
be changed on the transaction afterwards.

Importing also sets the **pension received date** on the pension's payslips: each contribution is matched to the
payslips whose pension amounts, employee's and employer's, add up to it exactly (one month's, or several months in a
row paid in together), paid in the four months before it, oldest first. The preview lists which payslips each
contribution would mark. Payslips with a received date already are left alone. For payslips assigned to the pension
after its transactions were imported, the Transactions page's **Match payslips** does the same.

A pension's **Holdings** (from its Transactions or Statements page, or the pensions list) shows each fund's units,
worked out from the purchases and sales (units sold to pay a fee included), as at today or any date: their cost
(the average cost of the units held, so a sale takes off its share of it), and their value, gain and % gain at the fund's last purchase or sale price, as an export has no other
prices. Cash isn't included. A fund converted to another (such as one share class to another) doesn't appear in the
exports: **Record a conversion** with its date, the two funds and the units received (from the provider's statement
or current holdings). All the old fund's units held on that date become the new fund's, which takes over their cost.
A conversion shows as two lines on the Transactions page; it can't be edited, and deleting it deletes both.

And, as Vehicle Manager,
nightly **backups** with encrypted off-site copies and a daily pull to the Mac, email **alerts** and a **heartbeat**
(below). It runs on the home server at `https://pay.<home domain>`: [host setup](../../deploy/pay-and-pensions/HOST-SETUP.md).

## Importing a spreadsheet

**People** → the person's import button (or **Import** on their payslips page). Upload the workbook (.xlsx): its first
sheet needs a heading row with **Year** (the year the tax year *ends*, so 2019 is 2018-19), **Period** (the tax
month, 1 to 12) and **Date** (the pay date), then a column per kind of pay up to **Gross**. Columns after Gross are
worked out here instead, except **Notes**; **Transfer Date** becomes each payslip's pension received date.

The preview shows, before anything is saved:

- how many payslips it would import, and which months are already there (those are left as they are, so an updated
  workbook can be imported again);
- rows it can't import and why, e.g. a date outside its tax year: correct the cell and import again, or add that
  payslip by hand;
- whether every row's gross and net match the spreadsheet's own;
- what each column becomes: a pay element with a kind (from the sheet's Gross and Deductions formulas) and a guessed
  category, both changeable. A heading used twice (an earnings Adj and a deductions Adj) gets its kind added. Name a
  column after one of the person's pay elements to use it;
- which employer the payslips are from: one of theirs, or a new one.

## Running it locally

It needs SQL Server and an Auth0 application, like Vehicle Manager.

1. **SQL Server:** the local container from the repo root's `compose.yaml`, set up as in Vehicle Manager's
   [Local database](../vehicle-manager/README.md#local-database-sql-server-in-docker) (`.env`, then
   `docker compose up -d --wait`).
2. **Settings,** in user secrets, from `src/PayAndPensions/GreenHedges.HomeServer.PayAndPensions` (the password is the
   one in `.env`; the database is created and migrated on first run):

   ```bash
   dotnet user-secrets set "ConnectionStrings:PayAndPensions" "Server=localhost,1433;Database=PayAndPensions;User ID=sa;Password=<password>;TrustServerCertificate=True"
   dotnet user-secrets set "ConnectionStrings:PayAndPensionsTests" "Server=localhost,1433;Database=master;User ID=sa;Password=<password>;TrustServerCertificate=True"
   dotnet user-secrets set "Auth0:Domain" "<your-tenant>.auth0.com"
   dotnet user-secrets set "Auth0:ClientId" "<client id>"
   dotnet user-secrets set "Auth0:ClientSecret" "<client secret>"
   dotnet user-secrets set "AllowedEmails" "me@example.com"
   ```

   The Auth0 application is a **Regular Web Application**, set up as Vehicle Manager's
   ([Signing in](../vehicle-manager/README.md#signing-in-auth0)), with `http://localhost:5081` and
   `https://localhost:7081` in place of Vehicle Manager's ports: callback `…/callback`, logout `…/Account/SignedOut`.
   It can be a second application in the same tenant.
3. **Run** with `dotnet watch` in that folder → <http://localhost:5081>.

`ConnectionStrings:PayAndPensionsTests` is for the tests: each run creates (and a later run drops) its own throwaway
database on that server. Without it, the database and page tests are skipped.

## Backups, alerts and the heartbeat

All off until configured, so a local copy does none of it. On the server, [deploy/pay-and-pensions/HOST-SETUP.md](../../deploy/pay-and-pensions/HOST-SETUP.md)
sets them up:

| What | How | Settings |
|---|---|---|
| Nightly backup | At 03:45 (after Vehicle Manager's), SQL Server backs up `ConnectionStrings:PayAndPensions` into its shared folder; Common's `BackupService` gzips it to `backups/payandpensions-<UTC>.bak.gz` and keeps 7 | `Backup:Enabled`, `Backup:SqlServerFolder` (required when on), `Backup:SqlServerFolderOnHost` |
| Off-site | Each backup `age`-encrypted to the shared bucket under `payandpensions/`, by the write-only IAM user `<stack>-payandpensions` ([deploy/aws/](../../deploy/aws/README.md)) | `Offsite:Bucket`, `Region`, `AccessKeyId`, `SecretAccessKey`, `AgeRecipient` |
| Mac pull | Daily at 09:30 into `~/Backups/PayAndPensions`: `deploy/mac/install-backup-pull.sh pay-and-pensions` | `deploy/local.env` |
| Alerts | Resend emails when the database doesn't answer for 5 minutes, or backups or off-site copies fail; resolved emails when they recover. `--test-alert` sends one and exits | `Alerts:ResendApiKey`, `From`, `To` |
| Heartbeat | A healthchecks.io ping every 5 minutes while the database answers | `Heartbeat:PingUrl` |

## Privacy

Payslips are personal. The repository holds only code and made-up test data: spreadsheets are git-ignored under
`src/PayAndPensions/` and `tests/PayAndPensions/`, and real names, employers and figures stay in the app's database
and the private notes.
