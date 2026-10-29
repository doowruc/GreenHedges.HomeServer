# Pay & Pensions

A web app for my household's payslip history, and later our pension pots. Part of
[GreenHedges.HomeServer](../../README.md); the plan, data model and build order are in [DESIGN.md](DESIGN.md).

**So far** (build step 3): people, each person's employers, and the pay elements on their payslips (the lines, e.g.
Salary, Tax, Employer pension, each with a kind and a category); and **payslips**, a table per tax year with the
figures the spreadsheets worked out and the year's totals. A new payslip starts from the latest one, so usually only
a few amounts change. **Importing** a person's existing spreadsheet (below). The **dashboard**, one person at a time:
their latest payslip, the tax year so far and predicted, and charts of gross and net by month and by tax year. And
**tax years**: every year's totals, with the change on the same months of the year before. And, as Vehicle Manager,
nightly **backups** with encrypted off-site copies and a daily pull to the Mac, email **alerts** and a **heartbeat**
(below). Deployment is the last step.

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

All off until configured, so a local copy does none of it. On the server (step 7 sets these up):

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
