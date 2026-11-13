# Presents

A web app for what we spend on the children's Christmas presents. Part of [GreenHedges.HomeServer](../../README.md);
the plan, data model and build order are in [DESIGN.md](DESIGN.md).

**Built** (all five build steps): a page for each **Christmas** (this year's is the home page; the arrows and **Year** move
between them), with a card per child showing what they've had, the **cash** that evens them up, and the total; a
form to add a present; and the year's presents, latest first. **History** has every year side by side. The **Children**
page adds, renames, reorders (move up and down) and deletes them (not while they have presents). And, as the other
web apps, sign-in, nightly **backups** with encrypted off-site copies, email **alerts** and a **heartbeat**, all off
until configured. **Import** brings in the old spreadsheet. It runs on the home server at
`https://presents.<home domain>`: [host setup](../../deploy/presents/HOST-SETUP.md).

## Presents

Each present has who it's for, the item, and optionally the shop and the date it was bought; the **price** of one and
the **quantity**; and two boxes for the big ones:

- **Split with Mum:** shared with my mum, so it counts half.
- **Split with birthday:** shared with the child's birthday, so it counts half.

Both ticked, it counts a quarter. The form shows what it **counts as** as you type, and the list shows it beside the
price (with scissors on split ones). The year a present belongs to is the Christmas it's added to, whatever its date,
so something bought in the summer still counts for that Christmas; editing a present can move it to another year.
After adding one, the form keeps the child, shop and date for the next.

**Budget** on a Christmas's page sets an optional budget per child for that year, or deletes the year with its
presents.

## Cash

Each child's card shows what their presents count as added up (**spent**), the **cash** that tops them up, and their
**total**. Cash brings every child up to the same total:

- **Without a budget,** to whoever had most: they get no cash, the others the difference.
- **With a budget,** to the budget: everyone gets the rest of it in cash, unless someone has had more than the
  budget. Then everyone is topped up to them instead, and the page says who went over.

Every child counts every year, even with no presents (they'd get it all in cash). Nothing is stored: change a present
or the budget and the cash follows.

**History** shows each year's spent and cash for each child, what each came to, and the year's total, with the budget
where there was one.

## Importing the spreadsheet

**Import** reads the workbook the presents were kept in (.xlsx, up to 2 MB): a sheet per year, named by the year,
with the headings **Date**, **Shop**, **Item**, **Who** and **Amount** side by side in its first ten rows and a row
per present below. Only those five columns are read, so the pivot beside them, any other list and sheets not named by
a year are ignored. Amounts are read as Excel last saved them, so a formula such as `=640/4` comes in as its result.

Uploading shows a preview and saves nothing. For each year it shows:

- the presents it would import, each at its amount, one of, with no splits (a split one's sum is already its share);
- the rows it skips, and why (no item, no name, or no amount above £0);
- the children it would add: a name that isn't one here yet (ignoring case), after the others;
- a **cash check**: the cash worked out here beside the sheet's **Cash** rows, which aren't imported. Where every
  child had a cash row and their totals are equal, that total was the budget, so it becomes the year's budget. A
  year whose cash didn't balance shows what would have.

**Import** then adds it all in one go. A year already here is left as it is, so the spreadsheet can be imported again.

## Running it locally

It needs SQL Server and an Auth0 application, like Vehicle Manager.

1. **SQL Server:** the local container from the repo root's `compose.yaml`, set up as in Vehicle Manager's
   [Local database](../vehicle-manager/README.md#local-database-sql-server-in-docker) (`.env`, then
   `docker compose up -d --wait`).
2. **Settings,** in user secrets, from `src/Presents/GreenHedges.HomeServer.Presents` (the password is the one in
   `.env`; the database is created and migrated on first run):

   ```bash
   dotnet user-secrets set "ConnectionStrings:Presents" "Server=localhost,1433;Database=Presents;User ID=sa;Password=<password>;TrustServerCertificate=True"
   dotnet user-secrets set "ConnectionStrings:PresentsTests" "Server=localhost,1433;Database=master;User ID=sa;Password=<password>;TrustServerCertificate=True"
   dotnet user-secrets set "Auth0:Domain" "<your-tenant>.auth0.com"
   dotnet user-secrets set "Auth0:ClientId" "<client id>"
   dotnet user-secrets set "Auth0:ClientSecret" "<client secret>"
   dotnet user-secrets set "AllowedEmails" "me@example.com"
   ```

   The Auth0 application is a **Regular Web Application**, set up as Vehicle Manager's
   ([Signing in](../vehicle-manager/README.md#signing-in-auth0)), with `http://localhost:5083` and
   `https://localhost:7083` in place of Vehicle Manager's ports: callback `…/callback`, logout `…/Account/SignedOut`.
   It can be another application in the same tenant.
3. **Run** with `dotnet watch` in that folder → <http://localhost:5083>.

`ConnectionStrings:PresentsTests` is for the tests: each run creates (and a later run drops) its own throwaway
database on that server. Without it, the database and page tests are skipped.

## Backups, alerts and the heartbeat

All off until configured, so a local copy does none of it. On the server, [deploy/presents/HOST-SETUP.md](../../deploy/presents/HOST-SETUP.md)
sets them up:

| What | How | Settings |
|---|---|---|
| Nightly backup | At 04:15 (after the other web apps'), SQL Server backs up `ConnectionStrings:Presents` into its shared folder; Common's `BackupService` gzips it to `backups/presents-<UTC>.bak.gz` and keeps 7 | `Backup:Enabled`, `Backup:SqlServerFolder` (required when on), `Backup:SqlServerFolderOnHost` |
| Off-site | Each backup `age`-encrypted to the shared bucket under `presents/`, by the write-only IAM user `<stack>-presents` ([deploy/aws/](../../deploy/aws/README.md)) | `Offsite:Bucket`, `Region`, `AccessKeyId`, `SecretAccessKey`, `AgeRecipient` |
| Alerts | Resend emails when the database doesn't answer for 5 minutes, or backups or off-site copies fail; resolved emails when they recover. `--test-alert` sends one and exits | `Alerts:ResendApiKey`, `From`, `To` |
| Heartbeat | A healthchecks.io ping every 5 minutes while the database answers | `Heartbeat:PingUrl` |
| Mac pull | Daily at 09:50 into `~/Backups/Presents`: `deploy/mac/install-backup-pull.sh presents` | `deploy/local.env` |

## Privacy

The children's names and what we spend on them are personal. The repository holds only code and made-up test data:
workbooks are git-ignored under `src/Presents/` and `tests/Presents/`, and the real one stays in the app's database
and the private notes.
