# Vehicle Manager

*Part of [GreenHedges.HomeServer](../../README.md); formerly the private repository `doowruc/VehicleManager`. Code
in `src/VehicleManager/`, tests in `tests/VehicleManager/`, backlog in [TODO.md](TODO.md), and the move onto the home
server in [HOMESERVER-MIGRATION.md](HOMESERVER-MIGRATION.md).*

A .NET 10 Razor Pages app for tracking the vehicles I own and their fuel history.

## Running it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd src/VehicleManager/GreenHedges.HomeServer.VehicleManager
dotnet run
```

The app needs a SQL Server database (Azure SQL, or the [local one in Docker](#local-database-sql-server-in-docker)) and
Auth0 sign-in, so set both up first (see below). Then open
http://localhost:5080. The tables are created on first run, and any pending schema migrations are
applied each time the app starts.

### Logs and traces (Aspire dashboard)

To see the app's logs, traces and metrics, run it through the Aspire AppHost instead, from the repo root, with the
[Aspire CLI](https://aspire.dev):

```bash
aspire run
```

It starts the app (still on http://localhost:5080, with the same user secrets) and the
[Aspire dashboard](https://aspire.dev) at https://localhost:17080. Open the dashboard with the login link printed in
the console. No Docker is needed. The CLI must be at least the AppHost's Aspire version (13.6): an older one hangs at
"Connecting to apphost", so update it with `aspire update --self`. Without the CLI,
`dotnet run --project src/VehicleManager/GreenHedges.HomeServer.VehicleManager.AppHost` does the same.

The app sends telemetry over OpenTelemetry (OTLP) only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, as the AppHost does
for the dashboard. Plain `dotnet run`/`dotnet watch` and the tests log to the console only. Each nightly job run is a
trace with a span per user, with the database, email and DVLA/DVSA calls under it. Logs carry Auth0 user ids rather
than emails or registrations, and registrations are taken out of the DVSA request URLs in traces.

## Database (Azure SQL)

Data is stored in an Azure SQL Database (SQL Server). To set one up:

1. In the [Azure portal](https://portal.azure.com), create a **SQL database**. The free offer
   ("Apply free offer" on the create page) is plenty for this app. Create a new server with
   **SQL authentication** and note the admin login and password.
2. On the server's **Networking** page, allow public access and add your client IP address to the
   firewall rules.
3. Store the connection string in user secrets, from the `src/VehicleManager/GreenHedges.HomeServer.VehicleManager` folder:

   ```bash
   dotnet user-secrets set "ConnectionStrings:VehicleManager" "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;User ID=<login>;Password=<password>;Encrypt=True;Connection Timeout=60;"
   ```

A free or serverless database pauses when idle. The first request after a pause can take up to a
minute while it resumes; the app retries automatically. Azure backs the database up automatically
and can restore it to an earlier point in time.

Every table has audit columns: `AddedDate`, `AddedBy`, `ModifiedDate` and `ModifiedBy` (UTC times
and Auth0 user ids, set automatically when rows are saved), and a SQL Server `rowversion` column,
`RowVersion`. If you save an edit to something that was changed after you opened it (e.g. in
another tab), you're warned instead of overwriting the other change.

## Local database (SQL Server in Docker)

For the tests, and to develop without touching the Azure database, run SQL Server locally in a container. On a Mac,
install [OrbStack](https://orbstack.dev) (`brew install --cask orbstack`, then open it once) or Docker Desktop; the
image is x86 only, so on Apple Silicon it runs under Rosetta (OrbStack does this by default; in Docker Desktop turn
on *Use Rosetta for x86/amd64 emulation*). On Windows, SQL Server Express LocalDB (`(localdb)\MSSQLLocalDB`) works
too.

1. From the repo root, copy `.env.example` to `.env` and set a strong `MSSQL_SA_PASSWORD` (`.env` is git-ignored).
2. Start it (`compose.yaml`: SQL Server 2025 Developer edition, on `localhost:1433` only, data kept in a named volume):

   ```bash
   docker compose up -d --wait
   ```

3. Tell the tests where it is, from the `src/VehicleManager/GreenHedges.HomeServer.VehicleManager` folder (use the password from `.env`):

   ```bash
   dotnet user-secrets set "ConnectionStrings:VehicleManagerTests" "Server=localhost,1433;Database=master;User ID=sa;Password=<password>;TrustServerCertificate=True"
   ```

Then `dotnet test` (from the repo root) runs everything, including the database and page tests. `docker compose stop` stops it; `docker compose down -v` also deletes its data.

**Developing against it** instead of Azure: point the app's own connection string at a database on it (created and
migrated on the next run), then copy your data in with **Backup** from the Azure-backed app and **Restore** into the
local one (sign in as the same user):

```bash
dotnet user-secrets set "ConnectionStrings:VehicleManager" "Server=localhost,1433;Database=VehicleManager;User ID=sa;Password=<password>;TrustServerCertificate=True"
```

This replaces the Azure connection string in your user secrets, so keep a copy of it first. While the app runs
locally against real data, its scheduled backups and reminders still send email.

## Signing in (Auth0)

Every page requires signing in with Auth0. In the Auth0 dashboard, create a **Regular Web
Application** and set:

| Setting | Value |
|---|---|
| Allowed Callback URLs | `https://localhost:7080/callback, http://localhost:5080/callback` |
| Allowed Logout URLs | `https://localhost:7080/Account/SignedOut, http://localhost:5080/Account/SignedOut` |

Then store its settings in user secrets, from the `src/VehicleManager/GreenHedges.HomeServer.VehicleManager` folder:

```bash
dotnet user-secrets set "Auth0:Domain" "<your-tenant>.auth0.com"
dotnet user-secrets set "Auth0:ClientId" "<client id>"
dotnet user-secrets set "Auth0:ClientSecret" "<client secret>"
dotnet user-secrets set "AllowedEmails" "me@example.com; someone@example.com"
```

`AllowedEmails` lists who may use the app (separate addresses with `;` or `,`; case doesn't
matter). Signing in isn't enough: the account's email must be on the list **and verified** in
Auth0, otherwise you see an "Access denied" page. Changes to the list apply on the next request.
If you create a user in the Auth0 dashboard, make sure its email ends up verified (e.g. via the
verification email).

Each signed-in user has their own owners, vehicles and fill-ups, and can't see anyone else's.

**Data from before per-user data was added** belongs to nobody until it's claimed. Name who
should get it, then sign in as that person (sign out first if you're already signed in):

```bash
dotnet user-secrets set "ExistingDataOwner" "me@example.com"
```

On that user's next sign-in (with that email verified and on the allow-list) all unclaimed
owners and vehicles, with their fill-ups, become theirs. After that the setting does nothing and
can be removed.

User secrets are only read in Development. Elsewhere, use environment variables
(`ConnectionStrings__VehicleManager`, `Auth0__Domain`, `Auth0__ClientId`, `Auth0__ClientSecret`,
`AllowedEmails`, `ExistingDataOwner`, `Resend__ApiKey`, `Resend__From`). The app won't start
without them.

### Running behind a proxy (the home server)

Setting it up on the server, step by step: [deploy/vehicle-manager/HOST-SETUP.md](../../deploy/vehicle-manager/HOST-SETUP.md).
The systemd unit sets the address (`127.0.0.1:5080`), the keys folder and the backup folders; the env file holds the rest.

On the server the app listens on localhost only and is published on the tailnet by Caddy (`deploy/caddy/`), which
handles HTTPS:

- **Address:** ASP.NET listens on `localhost` by default; set the port with `ASPNETCORE_URLS=http://127.0.0.1:<port>`.
- **Forwarded headers:** `X-Forwarded-Proto` and `X-Forwarded-For` are trusted from a proxy on the same machine only,
  so the app sees the original `https://…` address (which Auth0's callback needs) and the caller's tailnet address.
- **Keys:** set `DataProtection__KeysFolder` (e.g. `keys`, under the working directory) so the keys protecting the
  sign-in cookie and forms survive restarts and deploys. Without it, ASP.NET keeps them in the user profile.
- **Health:** `GET /health` answers `Healthy` (200) when the database is reachable, else `Unhealthy` (503), without
  signing in.

### Alerts and heartbeat

Shared with the home server's collectors. Each is off until configured (environment variables on the server, user
secrets locally):

```
Alerts__ResendApiKey=re_...
Alerts__From=VehicleManager <alerts@example.com>
Alerts__To__0=you@example.com
Heartbeat__PingUrl=https://hc-ping.com/<uuid>
```

| Email subject | Meaning |
|---|---|
| **Database unreachable** | The database hasn't answered for 5 minutes. Pages and the scheduled jobs fail until it's back. |
| **Nightly backups failing** | The latest nightly emailed backup run failed for some users, or none has completed for 26 hours. |
| **Morning reminders failing** | The same, for the morning MOT/tax check and reminder emails. |

Each PROBLEM email is followed by one RESOLVED email, including across restarts (open alerts are kept in
`alert-state.json` in the working directory). Job alerts wait while the database is failing. The heartbeat pings
healthchecks.io every 5 minutes while the database answers, so a stopped app, server or database shows there.
`--test-alert` (e.g. `dotnet run -- --test-alert`) sends a test email and exits. The checks run outside Development
only (set `Alerts:Monitor` to `true` to try them locally): they ask the database every minute, which would keep a
pausing Azure database awake. Thresholds:
`Alerts:DatabaseDownAfter`, `Alerts:JobOverdueAfter` in `appsettings.json`.

Auth0 posts the sign-in result back to the app, which needs `Secure` cookies. Chrome, Edge and
Firefox allow those on `http://localhost`, but Safari doesn't. If you use Safari, run the https
profile instead: `dotnet watch -lp https`, then open https://localhost:7080.

As a second layer on top of the allow-list, disable sign-ups (and any social connections you
don't use) for the application in Auth0.

## Features

**Owners** – managed from **Owners** in the navigation bar. Names must be unique, and an owner
can't be deleted while they still own vehicles.

**Vehicles** – owner, type, fuel type, make, model, registration number, registration date, purchased date,
when the MOT, tax, insurance and service are next due (see [Reminders](#reminders-mot-tax-insurance-and-service)),
and how often it's serviced (every so many months and/or miles).

**Service history** (per vehicle, on its page) – each service's date, mileage, garage and garage location, and
whether it was under a previous owner (e.g. from the service book). That's ticked for you when the date is before the
vehicle's purchased date. The list is sortable and searchable, 10 to a page, and its Notes column has badges: "Previous
owner", and in red, when a service was later than the interval after the one before it (e.g. "3 months late",
"5,000 miles over"). The
form starts from today and the latest odometer reading, and suggests garages (and their locations) used before.
When you add the vehicle's latest service, the next service's due date and mileage are worked out from it and the
service interval (e.g. serviced 01/10/2026 at 45,000 miles, every 12 months or 10,000 miles → next due 01/10/2027
or at 55,000 miles). An interval that isn't set leaves that due value alone. Adding an older service, or deleting
one, doesn't change them; editing the latest one's date or mileage works them out again.
Registration numbers are normalised to upper case and must be unique (per user). Vehicles that existed before
owners were added are assigned to an "Unassigned" owner; rename it or reassign the vehicles.

**Fuel Log** (per vehicle) – date, fuel brand and station, litres, cost per litre, optional
voucher discount per litre, and trip computer figures (average MPG, trip miles, range, odometer).
The brand and station start blank (with suggestions as you type), and the form shows the total cost,
real MPG and odometer change as you type. For a **split fill-up** (topped up at two stations),
click **Add another station** to record each brand and station in order; lists show them as
"Shell + Sainsbury's" / "Meadowbank + Hill Top".

**Fuel Brands** – under **Manage** in the navigation bar (per user): rename, merge duplicates (e.g. a
misspelling) into the right one, or delete unused ones. Each fuel brand has its own **Stations** page
(e.g. Fuel Brands › Shell) to rename, merge or delete that brand's stations. Every station belongs to a
brand, so one location can have several stations (e.g. Riverside for Asda and for Morrisons); a fill-up
records the station, and its brand comes from the station. New brands and stations are added by typing
them on a fill-up; names match ignoring case. The fill-up form suggests every brand; the station box
is enabled once a brand is entered and suggests only that brand's stations (none for a new brand). If a station is rebranded, record it as a new station so earlier fill-ups keep the old brand.
Merging a fuel brand moves its stations to the other brand, combining any with the same name.

**Calculated figures** (not stored, always worked out from the raw data):

| Figure | Formula |
|---|---|
| Total cost | litres × (price per litre − voucher) |
| Real MPG | trip miles ÷ (litres ÷ 4.54609) – UK gallons |
| Dash vs real | trip computer MPG − real MPG (red = dash is optimistic) |
| Pence per mile | total cost ÷ trip miles |
| Average MPG | total miles ÷ total gallons (weighted, not an average of averages) |

**Dashboard** – a **Coming up** list of anything due within 30 days (or overdue), then one card per
vehicle with its due dates, 12-month MPG, spend, odometer and last fill-up.
**Vehicle page** – due dates (coloured by how soon they are), then two tabs: **Fuel** (last-12-months / all-time stats, an MPG and fuel-price chart, and the full log) and
**Services** (the service history). The address bar follows the tab (`?tab=services`), so a refresh stays on it.
**Fuel Log page** (`/FuelLog`) – all fill-ups, filterable by vehicle and year.
**On a phone** – in Safari, **Share → Add to Home Screen** gives a "Vehicles" icon (the car from the menu bar)
that opens it like an app, without the browser's toolbars. Browser tabs and bookmarks show the same icon.
**Light / dark mode** – follows your system setting by default; pick Light, Dark or System from the
account menu (top right) under **Theme**. The choice is remembered per browser.

## Reminders (MOT, tax, insurance and service)

Each vehicle has four optional due dates: MOT, tax, insurance renewal and service, set on its edit form.
They show on the vehicle's page and dashboard card and, when due within 30 days or overdue, in the dashboard's
**Coming up** list.

**Service by mileage** – a service can also be due at a mileage, and is due at the date or the mileage, whichever
comes first. The mileage's date is estimated from the odometer readings of the fill-ups and (with the DVSA set up)
the MOT tests: the average miles per day over
the last 6 months (or all the readings, if the recent ones span less than 28 days). It's shown with "≈", e.g.
"Service ≈06/11/2026 · in 30 days · 43,000 mi", and moves as you add fill-ups or MOT checks bring in new tests; the vehicle page shows the recent
miles per month. Without two odometer readings at least 28 days apart there's no estimate yet. Reminders for a
mileage go once each, however much its estimated date moves.

**Email reminders** – every morning at 07:00 UK time, each user who has set a reminder email address under
**Settings** is emailed about anything due in 30 days, and again 7 days before (once each; a date first
entered closer than that, or already overdue, gets one reminder). Changing a date, e.g. after renewing the
insurance, starts its reminders afresh.

**MOT and tax lookups** – the MOT and tax dates can be filled in from the DVLA and DVSA: when a vehicle is
added without them, from **Check MOT and tax** on the vehicle's page, and every morning before the reminders.
Tax (and the MOT, as a fallback) comes from the DVLA's
[Vehicle Enquiry Service](https://developer-portal.driver-vehicle-licensing.api.gov.uk/apis/vehicle-enquiry-service/vehicle-enquiry-service-description.html);
the MOT from the DVSA's [MOT History API](https://documentation.history.mot.api.gov.uk/) (the latest test's
expiry, or a new vehicle's first MOT date). A vehicle declared off the road (SORN) has no tax date. Both are
free but need registering, and both are optional: without them, type the dates in.

With the DVSA set up, each check also brings in the vehicle's **MOT history**, shown on the MOT tab of its
page: every test, passed or failed, with its mileage, expiry and any defects and advisories.

```bash
# DVLA Vehicle Enquiry Service: the API key from the DVLA
dotnet user-secrets set "Dvla:ApiKey" "<api key>"

# DVSA MOT History API: all four from the DVSA's registration email
dotnet user-secrets set "MotHistory:ClientId" "<client id>"
dotnet user-secrets set "MotHistory:ClientSecret" "<client secret>"
dotnet user-secrets set "MotHistory:ApiKey" "<api key>"
dotnet user-secrets set "MotHistory:TokenUrl" "https://login.microsoftonline.com/<tenant id>/oauth2/v2.0/token"
```

Elsewhere, use `Dvla__ApiKey` and `MotHistory__…`. The DVSA settings are all or none: the app won't start
with only some of them. To try the DVLA's test service, also set `Dvla:BaseUrl` to
`https://uat.driver-vehicle-licensing.api.gov.uk/`. A lookup that fails (or doesn't know the registration)
leaves the dates as they were, and the vehicle page says why.

## Importing Fuel2.xlsx

1. Add your vehicles first (e.g. the Mercedes, the Yaris and the A-Class).
2. Go to **Import**, upload the spreadsheet and check the preview.
   Each worksheet becomes one vehicle; sheets are auto-matched to vehicles by name where it is
   unambiguous (a sheet called "Mercedes" with two Mercedes cars has to be picked manually).
3. Click **Import**.

The importer reads the header row (`Date, Brand, Location, Litres, Per Litre, [Voucher], Range,
Miles, MPG, Odometer`) and ignores the spreadsheet's calculated columns. A split fill-up is written
with `+` in both columns, in the same order (`Bx + By` / `Lx + Ly`). Re-importing is safe:
fill-ups already present (same vehicle, date and litres) are skipped, so you can re-upload an
updated spreadsheet to pick up just the new rows.

## Backup and restore

Under **Manage**:

- **Backup** downloads all your data (owners, vehicles with their due dates, fuel brands, stations, fill-ups and services) as a
  compressed JSON file, `vehicle-manager-backup-<date>.json.gz`. Other users' data isn't included.
- **Restore** replaces **all** your data with a backup's. It accepts the `.json.gz` file or the plain
  `.json` inside it. The file is checked before anything is changed, and the restore runs in one
  transaction, so if it fails your data is left as it was. Restored rows keep their original audit
  dates and "added by" values, and become yours, so a backup can also be restored into another account.

**Scheduled backups** – every night at 01:00 UTC, each user who has set a backup email address under
**Settings** (in the account menu, top right) is emailed a backup, as an attachment, if their data has
changed since the last scheduled backup. Settings also has **Email a backup now**, to check it works;
like downloading a backup, it doesn't affect the schedule. Leave the address blank to turn scheduled backups off. Settings aren't part of a backup.

Email is sent through [Resend](https://resend.com). Create an API key (sending access is enough) and
verify the domain you'll send from, then store both in user secrets, from the `src/VehicleManager/GreenHedges.HomeServer.VehicleManager` folder:

```bash
dotnet user-secrets set "Resend:ApiKey" "re_..."
dotnet user-secrets set "Resend:From" "Vehicle Manager <backups@example.com>"
```

The app won't start without them (elsewhere, use `Resend__ApiKey` and `Resend__From`). The schedule
runs inside the app (Coravel), so the app must be running at 01:00 UTC; if several copies are running
(e.g. locally and deployed), each one sends. The same goes for reminders at 07:00 UK time.

### Database backups (the home server)

Separately from those per-user emails, the whole database is backed up each night at 03:30 (server time), as the home
server's collectors are, through `GreenHedges.HomeServer.Common`:

1. SQL Server writes a full backup (`BACKUP DATABASE … WITH CHECKSUM, COPY_ONLY`: every page's checksum is checked as
   it's read) to `vehiclemanager-latest.bak` in a folder it shares with the app, overwriting the last one.
2. The app copies it to `backups/vehiclemanager-<UTC time>.bak.gz` (gzipped; 7 kept).
3. Each new one is encrypted with `age` and uploaded to the off-site bucket under `vehiclemanager/`
   ([deploy/aws](../../deploy/aws/README.md): IAM user `<stack>-vehicles`), and the Mac pulls the folder daily
   (`deploy/mac/install-backup-pull.sh vehicle-manager`).

"Backups failing" and "Off-site backup failing" emails follow after 36 hours without a good one. Settings (environment
variables on the server):

```
Backup__Enabled=true
Backup__SqlServerFolder=/var/opt/mssql/backups        # the shared folder as SQL Server sees it (in its container)
Backup__SqlServerFolderOnHost=/var/lib/sqlserver/backups   # the same folder as the app sees it
Offsite__Bucket=<bucket>
Offsite__Region=eu-west-2
Offsite__AccessKeyId=AKIA…
Offsite__SecretAccessKey=…
Offsite__AgeRecipient=age1…
```

SQL Server writes the file readable by its owner and group only, so the shared folder gives new files its group,
`sqlbackups` (the setgid bit), and the app's account joins that group: see
[deploy/sqlserver/HOST-SETUP.md](../../deploy/sqlserver/HOST-SETUP.md). Backups are off by
default (`Backup:Enabled`), so development and the tests don't take them. Locally, `compose.yaml` shares
`.sqlserver-backups/` (git-ignored) the same way, for the backup test.

**Restore test** (once after setting up, then occasionally): decrypt and unzip a copy, put the `.bak` in the shared
folder, and check it as SQL Server's `sa` (the local container is fine):

```sql
RESTORE VERIFYONLY FROM DISK = '/var/opt/mssql/backups/restored.bak' WITH CHECKSUM;
RESTORE FILELISTONLY FROM DISK = '/var/opt/mssql/backups/restored.bak';
```

**Restoring** over the live database: stop the app, then as `sa`
`RESTORE DATABASE [<database>] FROM DISK = '…/restored.bak' WITH REPLACE` (add `MOVE` clauses for the file names from
`FILELISTONLY` when restoring into a different server or name), start the app. Data entered since that backup is lost.

## Project layout

```
src/VehicleManager/GreenHedges.HomeServer.VehicleManager/
  Models/        EF Core entities: Owner, Vehicle, FuelEntry, FuelEntryStop, FuelBrand, FuelStation, ServiceRecord, UserSettings, SentReminder,
                 MotTest + MotDefect
  ViewModels/    View (output) and input models used by the pages, with the calculated figures
  Data/          AppDbContext (EF Core + SQL Server), DatabaseMigrator, Migrations/
  Services/      FuelStatsService, FuelImportService (reads .xlsx via ClosedXML), FuelStops, BackupFile + BackupService,
                 BackupEmailer + ScheduledBackups (nightly emailed backups via Resend, scheduled with Coravel),
                 Reminders + ReminderEmailer + ScheduledReminders (due-date reminders), VehicleLookup + DvlaClient +
                 MotHistoryClient (MOT and tax dates from the DVLA and DVSA), MotTests (stored MOT history),
                 Telemetry (OpenTelemetry setup)
  Backups/       SqlServerBackupSource (+ options): the nightly database backup, for Common's BackupService
  Monitoring/    AppHealth + VehicleManagerAlertMonitor: database probes, scheduled jobs' runs, alerts and the heartbeat
  Pages/         Dashboard, Owners/*, Vehicles/* (and each vehicle's Services/*), FuelLog/* (log + fill-up pages), Import/*, Backup/* (backup + restore), Settings/*, FuelBrands/* (and each brand's Stations/*),
                 Account/* (sign in/out)
src/VehicleManager/GreenHedges.HomeServer.VehicleManager.AppHost/   Aspire AppHost: runs the app with the Aspire dashboard (logs, traces, metrics) locally
tests/VehicleManager/GreenHedges.HomeServer.VehicleManager.Tests/
  Unit/, Data/, Pages/   unit, database and in-memory page tests; Infrastructure/ holds the helpers
```

Bootstrap, Bootstrap Icons and Chart.js are loaded from the jsDelivr CDN.

## Tests

```bash
dotnet test
```

`GreenHedges.HomeServer.VehicleManager.Tests` has unit tests, database tests and page tests. The page tests run the whole
app in memory, signed in as a test user. The database and page tests need the local SQL Server (see
[Local database](#local-database-sql-server-in-docker)): each run creates its own throwaway database there
(`VehicleManager_Tests_<time>_<id>`), migrated from scratch, and never uses the app's database or Azure SQL. Each
test also acts as its own throwaway `test|…` user. Test databases more than an hour old are dropped by the next run.
Without `ConnectionStrings:VehicleManagerTests` the database and page tests are skipped.

## Database migrations

The schema is managed with EF Core migrations in `src/VehicleManager/GreenHedges.HomeServer.VehicleManager/Data/Migrations`, applied
automatically at startup. The `dotnet-ef` tool is pinned in `dotnet-tools.json`; restore it once with:

```bash
dotnet tool restore
```

After changing a model or `AppDbContext`, add a migration from the `src/VehicleManager/GreenHedges.HomeServer.VehicleManager` folder:

```bash
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

Review the generated file, then run the app to apply it (or `dotnet ef database update`).
To undo the last migration before it has been applied anywhere, use `dotnet ef migrations remove`.

## Notes

- Dates and currency use the en-GB culture (dd/MM/yyyy, £).
