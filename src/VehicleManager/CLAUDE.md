# Vehicle Manager

Personal .NET 10 Razor Pages app for tracking the vehicles I own and their fuel history. Part of
GreenHedges.HomeServer: the repo-wide rules (Git, privacy, the server) are in the root `CLAUDE.md`; this file is only
about this app. See `docs/vehicle-manager/README.md` for user-facing features and the fuel spreadsheet import, and
`docs/vehicle-manager/TODO.md` for the backlog.

## Commands

Run from `GreenHedges.HomeServer.VehicleManager/` (the project folder, next to this file; the solution is
`GreenHedges.HomeServer.slnx` at the repo root):

```bash
dotnet watch                 # run with hot reload → http://localhost:5080 (what I normally use)
dotnet watch -lp https       # same over https → https://localhost:7080 (needed for sign-in in Safari)
dotnet build                 # must stay at 0 warnings, 0 errors
dotnet tool restore          # once, restores the pinned dotnet-ef (dotnet-tools.json at repo root)
dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet list package --vulnerable --include-transitive
```

From the repo root:

```bash
dotnet test tests/VehicleManager/GreenHedges.HomeServer.VehicleManager.Tests   # this app's tests; must all pass
dotnet test                  # the whole repo's tests (what CI runs)
docker compose up -d --wait  # local SQL Server (compose.yaml; needs OrbStack or Docker Desktop running)
```

## Tests

`GreenHedges.HomeServer.VehicleManager.Tests` (in `tests/VehicleManager/`, xUnit) is the main way to verify changes. I can't sign in to the running app for you,
so test pages through the in-memory host rather than relying on the browser.

- `Unit/` – pure logic, no database (calculations, stats, allow-list, text splitting, reminders, DVLA/DVSA lookups via the fake).
- `Data/` – EF Core against the tests' throwaway database: per-user isolation, audit columns, concurrency, stops,
  brands/stations, scheduled backups and reminders.
- `Pages/` – the whole app in memory (`Infrastructure/AppFactory`, a `WebApplicationFactory<Program>`). It loads pages,
  follows links and submits forms through `PageClient`, which sends a form's current values like a browser.
  Auth0 is replaced by a test sign-in, as whichever user the client is for; Auth0 and Resend settings and `AllowedEmails` are
  overridden with dummy values via environment variables. The app's connection string is overridden with the test
  database's (or, with no test SQL Server, one that can't connect), so it never reaches the real data, and
  `MigrateOnStartup=false` (the test database is already migrated). `AppFactoryTests` checks this.
- Database and page tests run against a **throwaway database** that `Infrastructure/TestDatabase` creates and
  migrates once per run (`VehicleManager_Tests_<time>_<id>`) on the SQL Server in `ConnectionStrings:VehicleManagerTests`
  (the local container from `compose.yaml`; see the README's Local database). It refuses Azure SQL. Test databases
  more than an hour old are dropped at the start of the next run (the test host ends before an end-of-run clean-up
  could finish).
- Derive database tests from `DatabaseTest`: each test still runs as its own throwaway `test|<guid>` user, which the
  per-user query filters keep apart, and `TestDatabase.DeleteUserDataAsync` removes everything it owns afterwards
  (it refuses any other user id). Mark them `[DatabaseFact]`/`[DatabaseTheory]` so they're skipped when no test
  SQL Server is configured.
- Never point tests at the real database, or run unscoped `ExecuteUpdate`/`ExecuteDelete` in tests. Test a new
  migration against the test database (the tests migrate it from scratch); before applying it to the real database,
  run its SQL (`dotnet ef migrations script <from> <to> --no-transactions`) inside a transaction against a copy of
  the real data (local, restored from a backup), check the results, and roll back. Then ask me before applying it.
- When fixing a bug, add a test that fails without the fix.

## Stack

- .NET 10, Razor Pages, EF Core 10 + SQL Server (Azure SQL Database for real data; SQL Server 2025 in Docker locally,
  from `compose.yaml`, for the tests and optionally for development). Connection strings are in user secrets:
  `ConnectionStrings:VehicleManager` (the app's) and `ConnectionStrings:VehicleManagerTests` (a server for the tests'
  throwaway databases). The local `sa` password is in the git-ignored `.env`.
- Packages: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all), `ClosedXML`, `Auth0.AspNetCore.Authentication`, `Coravel` (scheduler), `Resend` (email); `OpenTelemetry.*` through Common.Web (keep them on one version). Keep EF packages and `dotnet-ef` on the same version.
- Pinned only to fix vulnerability advisories in transitive packages (remove when the parent package catches up; each has
  a comment): `System.Security.Cryptography.Pkcs` (via SqlClient).
- Front end: Bootstrap 5.3, Bootstrap Icons, Chart.js 4 and DataTables 3 (Bootstrap 5 styling, no jQuery; fuel log tables) from the jsDelivr CDN (no npm/bundler). Pages add CSS via the layout's `Styles` section. Small vanilla JS files in `wwwroot/js`.
- en-GB culture throughout (dd/MM/yyyy, £). MPG uses UK gallons (4.54609 L).

## Layout

```
GreenHedges.HomeServer.VehicleManager/
  Models/      EF Core entities: Owner, Vehicle, FuelEntry, FuelEntryStop, FuelBrand, FuelStation, ServiceRecord, UserSettings (storage only),
               SentReminder (+ DueDateKind), MotTest (+ MotDefect), IFuelPlace
  ViewModels/  What pages use: *View (output, with calculated figures) and *Input (form/import input)
  Data/        AppDbContext (per-user query filters), DesignTimeDbContextFactory, Migrations/
  Services/    FuelStatsService (summaries), SpendStats (dashboard spend by month/year and vehicle, year on year), FuelImportService (ClosedXML .xlsx import), FuelCalculations,
               ExistingDataClaim, FuelStops (typed names → stops),
               BackupFile (backup JSON format: read/validate/write) + BackupService (create, restore, record),
               BackupEmailer (emails a backup via Resend), ScheduledBackups (Coravel invocable, nightly),
               Reminders (when to remind; pure) + ReminderEmailer + ScheduledReminders (Coravel, each morning),
               MileageTrend (recent miles per day from fill-up and MOT odometer readings, for service-by-mileage estimates), ServiceSchedule (next service from intervals),
               VehicleLookup (MOT/tax dates) over DvlaClient (DVLA Vehicle Enquiry) and MotHistoryClient (DVSA MOT History),
               MotTests (keeps stored MOT tests in step with the DVSA), Telemetry (OpenTelemetry setup, ActivitySource)
  Pages/       Index (dashboard, with a "Coming up" due-dates list), Owners/*, Vehicles/* (+ Services/* per vehicle), FuelLog/* (log + fill-up pages), Import/*, Backup/* (Index, Restore), Settings/* (user menu), FuelBrands/* (+ Stations/* per brand),
               Account/* (Login, Logout, SignedOut), Shared/ (layout + partials)
  wwwroot/     css/site.css, js/color-mode.js, js/period-toggle.js, js/data-table.js (DataTables setup), js/typeahead.js (dropdown suggestions),
               js/price-chart.js (station price charts), js/mileage-chart.js (mileage over time: vehicle Services and MOT tabs),
               js/spend-chart.js (dashboard fuel spending charts), favicon.svg/.ico + icons/ + site.webmanifest (made by
               ../make-icons.py: Bootstrap Icons' car on the navbar's dark grey; re-run it to change them)
  Monitoring/  AppHealth (database probes and job runs; the heartbeat source), VehicleManagerAlertMonitor (the alert rules),
               VehicleManagerAlertOptions, ScheduledJob, JobRun (the database probe is Common.Web's)
```

Shared with the other web apps, in `src/Common/GreenHedges.HomeServer.Common.Web/` (tests in `tests/Common/`):

```
  Auth/        AllowedUsers, CurrentUser, SignIn (AddAllowedUserSignIn: Auth0 + the AllowedUser policy)
  Data/        AuditedEntity, IUserOwned, AuditedDbContext (audit stamping; AppDbContext's base), ConcurrencyExtensions,
               DatabaseMigrator, DatabaseHealthCheck<TContext>, DatabaseProbe<TContext> (+ IDatabaseProbe, for the alert monitors)
  Backups/     SqlServerBackupSource + SqlServerBackupOptions: the nightly SQL Server backup for Common's BackupService
  Hosting/     WebAppHosting: RequiredSetting, AddForwardedHeaders (behind Caddy), AddLastingDataProtection;
               WebAppTelemetry: OpenTelemetry over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set
```

## Conventions and how things work

**Database / migrations**
- Schema changes go through EF Core migrations; never use `EnsureCreated()`.
- `DatabaseMigrator` (Common.Web) runs at startup (`db.MigrateDatabase(logger)` in `Program.cs`) and applies pending migrations. The migrations were restarted for SQL Server (`InitialCreate`); the SQLite-era ones are gone.
- My real data lives in the Azure SQL database: never drop, recreate or bulk-change it. Tests use their own throwaway database (`TestDatabase`), never it.
- `EnableRetryOnFailure` is on, so explicit transactions must run inside `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` (see `ExistingDataClaim`).

**Audit columns** (every table, via Common.Web's `AuditedEntity` and `AuditedDbContext`)
- `AddedDate`, `AddedBy`, `ModifiedDate`, `ModifiedBy` are set in `AppDbContext.SaveChanges` (UTC; user = Auth0 id). Added values can't be changed after insert. Bulk `ExecuteUpdate` bypasses `SaveChanges`, so set `ModifiedDate`/`ModifiedBy` explicitly there.
- `RowVersion` is a SQL Server `rowversion` (`[Timestamp]`), maintained by the database.
- Edits use optimistic concurrency: `*Input` classes carry `RowVersion` (a hidden field in the form, base64), and edit pages save with `db.TrySaveEditAsync(entity, input.RowVersion)` (Common.Web's `ConcurrencyExtensions`). On a conflict, show the error and put the latest `RowVersion` back in the form.
- A new table should inherit `AuditedEntity`.

**Pages never use entity classes** (to limit what's read from the database and prevent overposting)
- Output: project queries straight to view models, e.g. `.Select(VehicleView.Projection)`, or to a small record or anonymous type when a page needs only a few columns. Don't `Include()` navigations or load entities for display.
- Input: bind `[BindProperty]` to an `*Input` class, never an entity. Ids come from the route, not the form. Create a new entity, or load it with `FindAsync`, then copy the fields across with `input.ApplyTo(entity)`.
- Deletes use `ExecuteDeleteAsync()`. Fill-ups go with their vehicle via the FK cascade.
- Entities keep only the attributes that shape the schema (`[Required]`, `[StringLength]`). Display names and validation ranges belong on the `*Input` classes.
- When adding a field, update the entity, its `*View`/`*Input` projections and `ApplyTo`, and the backup
  (`Services/BackupFile.cs` record + validation, `BackupService` create/restore). A new data table needs adding to the backup too
  (`UserSettings` deliberately isn't: restoring shouldn't change where backups go).

**Per-user data**
- Owners and vehicles implement `IUserOwned` (`UserId` = Auth0 `sub`, via Common.Web's `CurrentUser`). Fill-ups belong to the user who owns their vehicle.
- `AppDbContext` query filters limit every query to the current user, including `ExecuteUpdate`/`ExecuteDelete`. `SaveChanges` stamps new owners/vehicles with the user's id and throws if nobody is signed in. Pages don't filter by user themselves.
- With no signed-in user, queries return nothing (fail closed). Only use `IgnoreQueryFilters()` for deliberate cross-user work, as `ExistingDataClaim` does.
- Use `FirstOrDefaultAsync(x => x.Id == id)` rather than `FindAsync`, so the filters always apply.
- Rows with an empty `UserId` are pre-per-user data. On sign-in (`OnTokenValidated`), `ExistingDataClaim` gives them to the user whose verified email matches the `ExistingDataOwner` setting.
- A new user-owned root entity needs `IUserOwned`, a query filter and per-user unique indexes. A child entity needs a filter through its parent.
- `dotnet ef` uses `DesignTimeDbContextFactory`, so it doesn't need the Auth0 settings (it reads the connection string from user secrets).

**Data rules**
- Registration numbers are normalised to upper case and must be unique per user (`Pages/Vehicles/VehicleRules.cs`).
- Every vehicle has a required owner (`Owners` table, FK with `Restrict`, so owners with vehicles can't be deleted). Owner names are unique per user, case-insensitive (`Pages/Owners/OwnerRules.cs`).
- Calculated figures (total cost, real MPG, dash-vs-real, pence per mile) are computed from raw data, never stored. They are properties on `FuelEntryView`; the cost formulas are shared with `FuelEntryInput` via `Services/FuelCalculations.cs`. Average MPG is weighted: total miles ÷ total gallons, not an average of averages.
- Every `FuelStation` belongs to a `FuelBrand` (required `FuelBrandId`). A fill-up's stops are `FuelEntryStop` rows (ordered by `Sequence`) linking only a station; the brand comes from the station. A split fill-up has one stop per station. `FuelEntryView.Brand`/`.Station` join them with " + " for display. Spreadsheet text "Bx + By" / "Lx + Ly" pairs up by position (`FuelStopInput.FromText`).
- A station's brand is fixed, so a rebrand is a new station and history keeps the old brand. Names are case-insensitive: brands are unique per user, stations unique per brand (one location can have stations for several brands).
- Forms take typed brand + station names. `Services/FuelStops.ValidateAsync` requires both (filling in the brand when only one brand has that station name); `SetStopsAsync` reuses or creates them and updates stops in place (marking the fill-up modified so its RowVersion check applies).
- Brands with stations, and stations with fill-ups, can't be deleted (FK `Restrict`); merge them instead. Merging a brand moves its stations, combining same-named ones.
- Fuel brands are managed at `/FuelBrands` (Manage menu); a brand's stations are a sub-page, `/FuelBrands/{brandId}/Stations/...` (`Pages/FuelBrands/Stations`, base `StationPageModel`, which 404s a station not under the brand in the URL). Links and redirects between station pages must pass `brandId`: ASP.NET doesn't carry a route value over to a different page, and the link comes out empty. A station's own page (`Stations/Details`, `/FuelBrands/{brandId}/Stations/{id}`) shows its fill-ups and a pump-price chart with a polynomial trend curve, one tab per fuel type (the vehicle's `FuelType`). `_FuelTable` links each stop there with `?fuelType=` of that row's vehicle, which picks the tab (else the latest fill-up's). `FuelPlaceList<T>` (`Pages/FuelBrands/FuelPlaceList.cs`) holds the database logic shared by both, with `FuelBrandList`/`FuelStationList` subclasses.
- Fuel import de-duplicates on (vehicle, date, litres) so re-importing is safe. ClosedXML reads `CachedValue` so formula cells use Excel's saved results.

**Backup / restore** (`/Backup`, `/Backup/Restore`, under Manage; scheduled backups set up at `/Settings`)
- A backup is gzip-compressed JSON (`BackupFile`): `format`/`version` plus a flat list per table, rows linked by their ids in
  the file. No user id is stored. Restore accepts gzip or plain JSON (detected by the gzip header, not the file name).
- `BackupFile.Read` validates everything (links, required values, lengths, case-insensitive uniqueness) before any data is
  touched. `format`/`version` are `required`, so arbitrary JSON can't pass as an empty backup and wipe the data.
- `BackupService.RestoreAsync` deletes the user's rows (filtered `ExecuteDelete`) and inserts the backup's in one transaction
  in the execution strategy. It sets `AppDbContext.KeepAuditValues` so restored rows keep their audit values; `UserId` is always
  the current user. Don't use `KeepAuditValues` elsewhere.
- Scheduled backups: `ScheduledBackups` runs daily at 01:00 UTC (Coravel, `Program.cs`). For each user with a
  `UserSettings.BackupEmail` it acts as them (`CurrentUser.ActAs`, background work only, in its own DI scope) and
  `BackupEmailer` emails the `.json.gz` via Resend if the data changed. "Changed" = the backup's `ContentHash()` (SHA-256 of
  the JSON minus `createdDate`) differs from `UserSettings.LastBackupHash`, so deletions count. Only scheduled emails set
  `LastBackupDate`/`LastBackupHash`; downloads and "Email a backup now" (`EmailAsync(scheduled: false)`) don't.
- `Resend:ApiKey` and `Resend:From` come from user secrets (required at startup, like Auth0's). `EmailOptions.From` is
  used for both backups and reminders.
- Tests: `AppFactory` swaps `IResend` for `Infrastructure/FakeResend` (records emails; addresses containing "fail" fail)
  and removes Coravel's hosted service (so neither scheduled backups nor reminders run). Never call `ScheduledBackups.Invoke()` in tests: it covers every user, real ones
  included. Test `BackUpAsync(testUserId)`, or `RunAsync([testUserIds])` for a whole run (it records the run in `AppHealth`).

**Service history**
- The vehicle page (`Vehicles/Details`) has Fuel, Services and MOT tabs (Bootstrap `nav-tabs`). The active one is rendered
  server-side from `?tab=fuel|services|mot` (no flicker); the tab links carry it and a script keeps the address bar on the
  selected tab. Service pages redirect and cancel back with `tab=services` (`DetailsModel.ServicesTab`). The vehicle
  switcher's options carry the tab too (rendered server-side, updated by the same script), so switching vehicle keeps it.
- `ServiceRecord` (date, mileage, garage, optional location, `UnderPreviousOwner` (done before the vehicle was the user's;
  it still counts as the latest service for the next one; ticked by `_ServiceForm`'s script for a date before the
  vehicle's `PurchasedDate`, and always set for one when a service is added); filter and cascade through its vehicle; in the backup) is
  managed at `/Vehicles/{vehicleId}/Services/...` (`Pages/Vehicles/Services`, base `ServicePageModel`, which 404s a
  service not under the vehicle in the URL). Links and redirects between them must pass `vehicleId`, as for stations.
- `Vehicle.ServiceIntervalMonths`/`ServiceIntervalMiles`: adding the vehicle's latest service (or changing the latest's
  date or mileage) sets `ServiceDueDate`/`ServiceDueMileage` via `ServiceSchedule.UpdateNextDueAsync`; an unset interval
  leaves that value alone. Older services and deletes don't change them; other edits don't either, so hand-set values stay.
- `_ServiceTable`'s Notes column holds the badges: red "N months late"/"N miles over" when a service came after the
  interval following the one before it (`ServiceSchedule.SetDue` sets `ServiceRecordView.DueDate`/`DueMileage` from the
  vehicle's current interval; the first service is never late), and "Previous owner".

**Due dates, reminders and MOT/tax lookups**
- `Vehicle` has `MotDueDate`, `TaxDueDate`, `InsuranceDueDate`, `ServiceDueDate` (all optional, in the backup).
  `VehicleView.DueDates(today)` gives the set ones as `DueDateView`s (badge colour, "in 5 days"/"2 days overdue");
  `Pages/Shared/_DueDates` shows them as badges. "Today" is `Reminders.UkToday()`, not the server's date.
- Service can also be due at `ServiceDueMileage`: whichever of date and mileage comes first. The mileage's date is
  estimated by `Services/MileageTrend` (miles per day from odometer readings over the last 6 months, else all
  of them; null without two readings 28+ days apart), so `DueDates(today, trend)` needs the trend
  (`MileageTrend.From(entries, motReadings)` or `ForVehiclesAsync(db)`). An estimate has `DueDateView.DueMileage` set.
  The readings are the fill-ups' and the stored MOT tests' (`MotReadingsAsync`, or `MotTestView.Miles`; UK date of
  the test, kilometres converted to miles by `MileageTrend.Miles`).
- Reminders go 30 and 7 days before each date (`Reminders.DaysBefore`); a date first seen inside a threshold gets only
  the nearest one. `SentReminder` rows (vehicle, kind, due date, due mileage, days before; cascade with the vehicle; not
  in the backup) stop repeats, matched by `ReminderKey`: by date, or for a mileage estimate by the mileage (its date
  moves with each fill-up). A changed date or mileage gets reminders afresh. Rows are recorded only after Resend
  accepts the email. The unique index is unfiltered (`HasFilter(null)`), so date-based rows (null mileage) are unique too.
- `ScheduledReminders` runs at 07:00 UK time for every user with vehicles: `VehicleLookup.RefreshAsync` each vehicle
  (if a service is configured), then `ReminderEmailer` to `UserSettings.ReminderEmail` (none = no emails).
  Never call `Invoke()` in tests; test `RunForAsync(testUserId, today)`, or `RunAsync([testUserIds], today)` for a run.
- Lookups: tax from the DVLA (SORN clears it), MOT from the DVSA (latest test expiry, or `motTestDueDate` for a new
  vehicle), falling back to the DVLA's `motExpiryDate`. Dates not found are left alone; problems are returned as
  messages (shown via `TempData["Warning"]`), never thrown. `Dvla:ApiKey` and `MotHistory:ClientId/ClientSecret/ApiKey/TokenUrl`
  are optional user secrets (DVSA: all or none, else startup throws); without them `VehicleLookup.IsAvailable` is false
  and the lookup UI is hidden. Messages name only the services set up (`VehicleLookup.ServiceNames`, `LookedUp`,
  `MorningCheck`): with just the DVSA, only the MOT is looked up. The DVSA token (Entra client credentials, 1 hour) is cached in `MotHistoryTokenCache`.
- MOT history: `VehicleLookup.RefreshAsync` also brings the vehicle's `MotTest` rows (each with ordered `MotDefect`s:
  advisories, minors, majors…) in step with the DVSA via `Services/MotTests.SyncAsync`: matched by completion time and
  test number, updated in place (unchanged rows aren't touched, so their audit columns stay put), and removed when the
  DVSA no longer has them. No answer from the DVSA (not set up, failed, unknown vehicle) leaves them alone. They're
  filtered and cascade through the vehicle, and are in the backup. The vehicle page's MOT tab (`_MotTests`) lists them;
  it shows when the DVSA is set up or tests are stored. Northern Ireland tests (`DataSource` "DVA NI") have no defect details.
  Order them with `MotTestView.NewestFirst` (by UK date, and a same-day pass after the failure it's the retest of: the
  DVSA's times can be identical or the wrong way round), not by `CompletedDate` alone. A test before the vehicle's
  `PurchasedDate` gets a grey "Previous owner" badge, like services; it's worked out on the page
  (`MotTestView.MarkPreviousOwner` sets `BoughtAfter`), not stored, so it follows changes to the purchase date.
- Checks can cover just the MOT, just the tax, or both (`LookupParts`; the vehicle page's `?handler=Lookup&check=mot|tax`).
  MOT alone asks only the DVSA (the DVLA if it's the only service; its `motExpiryDate` fallback applies only when both are
  checked); tax alone asks only the DVLA; each updates only its own date (MOT also syncs the history).
  `VehicleLookupResult.CheckedWith` names the services asked. Adding a vehicle and the morning run check both.
  The header has separate Check MOT / Check tax links, each shown only while its date is missing
  (`ShowHeaderMotCheck`/`ShowHeaderTaxCheck`; tax needs the DVLA). The MOT tab always has a Check MOT button (top right,
  returning to `tab=mot`).
- Tests: `AppFactory` sets dummy DVLA/DVSA settings and routes both typed `HttpClient`s to `Infrastructure/FakeVehicleApis`,
  which answers only for registrations added with `FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), …)`.
  Each pass it sends has an advisory, plus a failed test (01/01/2020, as "OLD 1") with `FakeVehicleApis.FailedTestDefect`.

**Hosting behind Caddy** (`Program.cs`)
- `UseForwardedHeaders` runs first: `X-Forwarded-Proto` and `-For` are trusted from loopback proxies only (ASP.NET's
  default `KnownProxies`/`KnownIPNetworks`); never widen that. Caddy keeps the `Host` header, so
  `X-Forwarded-Host` isn't used.
- On the server the unit sets `ASPNETCORE_CONTENTROOT=/opt/vehiclemanager` (where `appsettings.json` is) while the
  working directory is the data folder; relative data paths (keys, backups, alert state) go through `Path.GetFullPath`,
  so they resolve against the working directory, not the content root. Keep it that way.
- `DataProtection:KeysFolder` (on the server) persists data-protection keys there, with the application name
  `VehicleManager`; unset, ASP.NET's default applies (development, tests).
- `/health` (anonymous, Common.Web's `DatabaseHealthCheck<AppDbContext>`, 10 s timeout): `Healthy`/`Unhealthy` text only, never details.
- `HostingTests` cover all three.

**Database backups** (Common.Web's `Backups/`, through `GreenHedges.HomeServer.Common`'s `AddBackups`, as the collectors)
- `SqlServerBackupSource` is `Common`'s `IBackupSource` (`FileExtension` `.bak`): `BACKUP DATABASE … WITH CHECKSUM,
  COPY_ONLY, INIT, FORMAT` of `ConnectionStrings:VehicleManager` (named by the `CollectorIdentity`) to
  `Backup:SqlServerFolder/vehiclemanager-latest.bak` (SQL Server's path, in its container),
  then copies it from `Backup:SqlServerFolderOnHost` (the app's path to the same folder). `BackupService` gzips it as
  `backups/vehiclemanager-<UTC>.bak.gz`, keeps 7; off-site copies go to `Offsite:*` (prefix `vehiclemanager`).
- Off by default (`Backup:Enabled`): only the server takes them. `Backup:SqlServerFolder` is required when on.
- Never `RESTORE VERIFYONLY` in app code: it needs a login that can create databases. The tests do it as `sa`.
- `Common`'s `BackupMonitor` alerts ("Backups failing", "Off-site backup failing") run in `VehicleManagerAlertMonitor`.
- `Common` has its own `BackupService`/`BackupOptions`, unrelated to `Services/BackupService` (the per-user JSON
  backups): `Program.cs` aliases `CommonBackupOptions`.
- Tests: `Data/SqlServerBackupSourceTests` take a real backup on the local container through `.sqlserver-backups/`
  (shared by `compose.yaml`) and check it as `sa`; `[SqlServerBackupFact]` skips them without that folder (as in CI).

**Alerts and heartbeat** (`Monitoring/`, through `GreenHedges.HomeServer.Common`, shared with the collectors)
- `Program.cs` registers a `CollectorIdentity("VehicleManager")` (alert subjects) and `TimeProvider.System`, then
  `Common`'s `AddAlerting()` and `AddHeartbeat()`. Both are off until configured (`Alerts:ResendApiKey`/`From`/`To`,
  `Heartbeat:PingUrl`); without a key, alerts are only logged. `--test-alert` sends one and exits.
- `VehicleManagerAlertMonitor` (a `Common` `AlertMonitorService`, every `Alerts:CheckInterval`) probes the database and
  raises, with stable keys: `Database` (not answering for `Alerts:DatabaseDownAfter`, 5 min), `NightlyBackups` and
  `MorningReminders` (the latest run had failures, or none completed for `Alerts:JobOverdueAfter`, 26 h, from startup).
  Job alerts wait while the database is failing. Alerts resolve only on positive evidence since startup.
- The scheduled jobs record each run (users, failures, last error) in `AppHealth`, including a run that fails before
  reaching any user. A new scheduled job should do the same, and get a `ScheduledJob` value and an alert key.
- The heartbeat pings while the database answered within `Heartbeat:Interval`.
- `Alerts:Monitor` turns the monitor on or off: on by default outside Development, off in Development (its
  every-minute probe would keep a pausing Azure SQL database awake). Without the monitor there are no probes, so no
  heartbeat either.
- Alert emails go to the owner of the server, so they may carry error messages; logs and traces still mustn't.
- Tests: `AppFactory` keeps the alert state file in the temp folder; neither email nor pings are configured.
  `VehicleManagerAlertMonitorTests` cover the rules with a fake clock and probe.

**Telemetry and logging** (`Services/Telemetry.cs`, over Common.Web's `WebAppTelemetry`)
- OpenTelemetry logs (through `ILogger`), traces and metrics go over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set:
  on the server by `vehiclemanager.service`, to the telemetry stack and Grafana (`deploy/telemetry/`). Otherwise nothing
  is set up: `dotnet watch` and the tests log to the console only.
- Logs and traces may leave the app, so never log or tag emails, registrations, tokens or connection strings; use the
  Auth0 user id (`enduser.id` on spans). `Telemetry.RedactUrl` takes registrations out of the DVSA's request URLs in
  traces, and `System.Net.Http.HttpClient` logging is at Warning (it would log every request URL).
- Background work gets its own spans from `Telemetry.Source`, like the nightly jobs: one per run, one per user inside it,
  with `AddException` and an error status on a user's failure.

**Authentication (Auth0)**
- `Auth0:Domain`, `Auth0:ClientId`, `Auth0:ClientSecret`, `AllowedEmails`, `Resend:ApiKey`, `Resend:From` and optional `ExistingDataOwner`, `Dvla:*`, `MotHistory:*` come from user secrets (env vars outside Development). Never put them in `appsettings*.json` or commit them. The app throws at startup if any required ones are missing.
- Authorization code flow, scopes `openid profile email`.
- Allow-list: `AllowedEmails` (user secrets, `;`/`,` separated) via Common.Web's `AllowedUsers` (set up by `AddAllowedUserSignIn`). The `AllowedUser` policy (also the fallback policy, so it covers every endpoint) requires a signed-in user whose `email_verified` email is on the list. Signed-in users who fail it are forbidden → `Account/AccessDenied`. The app throws at startup if the list is missing (fail closed).
- `[AllowAnonymous]` only on `Account/Login`, `Account/Logout`, `Account/SignedOut`, `Account/AccessDenied`, `Error`, `/health` and static assets. New pages are protected automatically. The layout shows the nav only when the user passes the `AllowedUser` policy.
- `Account/Login` challenges Auth0 and only accepts local return URLs. `Account/Logout` is POST-only and anonymous (so a denied user can switch account). It signs out of both the cookie and Auth0, returning to `Account/SignedOut`.
- To test locally without real credentials, set `AllowedEmails` and the `Auth0__*` env vars to dummy values (e.g. `samples.auth0.com`). Pages then redirect to Auth0 but you can't get past sign-in. Don't sign in with a real account from automated tests.

**Icons and the web app manifest** (`Pages/Shared/_Layout.cshtml`, `wwwroot`)
- Every page links `favicon.ico` and `favicon.svg` (tabs), `icons/apple-touch-icon.png` (iOS home screen; 180 px,
  full-bleed, no transparency) and `site.webmanifest` (name, short name "Vehicles", `standalone`, 192/512 and maskable
  icons). The layout's `~/` links come out fingerprinted (static assets), so a changed icon gets a new address; the
  manifest's own icon paths are plain. All are static assets, so anonymous (phones fetch them without the cookie).
- `AppIconTests` cover the links, anonymity, types, the touch icon and the manifest's icon sizes.

**Routing**: path ids, e.g. `/Vehicles/Details/1`, `/FuelLog/Edit/5`.

**Light / dark mode**
- Bootstrap's `data-bs-theme` on `<html>`, set by `wwwroot/js/color-mode.js`. It's loaded synchronously in `<head>` so there's no flash.
- Preference (Light / Dark / System) is stored in localStorage key `theme`; absent means System, the default. The theme choice is a Theme item in the user (account) menu that expands in place (a Bootstrap collapse; the
  dropdown has `data-bs-auto-close="outside"` so it stays open, and `color-mode.js` closes it after a choice). Signed-out
  pages have no picker, but the stored choice still applies. The navbar stays dark in both themes.
- Everything must work in both themes. Don't use `bg-white`, `table-light` or hard-coded colours. Use `bg-body`, the custom `<thead class="table-head">` and Bootstrap CSS variables in `site.css`.
- Anything drawing its own colours (e.g. Chart.js) should listen for the `themechange` event and use `window.themeColors()`. See `Pages/Vehicles/Details.cshtml`.
- Charts: put the canvas in a `<div class="chart-box">` and use `maintainAspectRatio: false`, so the height suits phones as well as desktop (a fixed ratio makes it very short on a phone).
- Mileage charts: `Pages/Shared/_MileageChart` (model `(Title, Json)`) drawn by `wwwroot/js/mileage-chart.js` (dates on a
  linear axis, failed MOTs in red, a previous owner's readings as hollow grey points on a dashed grey line, from each
  reading's `previousOwner`: the service's `UnderPreviousOwner`, or the MOT's `BoughtAfter`; a legend explains the colours
  when any are used). The vehicle page's Services and MOT tabs each have one, from
  `DetailsModel.ServiceChartJson`/`MotChartJson` (null with fewer than two readings). The MOT chart shows every test
  whatever the period toggle says (a year's MOTs are a single point). The page loads Chart.js once for all its charts.
  A reading's optional `ref` links its point to the element with that `data-chart-ref` (MOT points →
  `_MotTests` items, `MotTestView.ChartRef(id)`): clicking scrolls there and highlights it (`site.css`), switching the
  period toggle to "All time" (which is remembered) when the item is only in that list.

**Stats period toggle ("Last 12 months" / "All time")**
- Shared partial `Pages/Shared/_PeriodToggle.cshtml` plus `wwwroot/js/period-toggle.js`, used on the dashboard and vehicle details
  (the Fuel, Services and MOT tabs each have one). Reuse it rather than duplicating it. A page can have several: each copy of the
  script binds only buttons not yet bound, and every toggle on the page shows the same period.
- It's small (`nav-pills-sm` in `site.css`, sized like `btn-sm`), to sit with the small buttons and selects used beside it.
- Mark up period-specific content as `<div class="period-stats" data-period="12m">` / `data-period="all"`. A `site.css` rule hides the non-selected one, based on `data-stats-period` on `<html>`.
- The selection is remembered across pages in the `statsPeriod` cookie (`all`; absent means 12 months). The partial renders the active button server-side from the cookie, and the script runs synchronously right after the buttons, so nothing flickers on load. Keep it that way.

**Tables (DataTables): fuel log and service history**
- `_FuelTable` (Fuel Log page, each vehicle's page, station pages) and `_ServiceTable` (a vehicle's service history) are
  DataTables: sorting, search and paging in the browser; the Fuel Log's vehicle/year filters stay server-side. Each is
  wrapped in `<div class="data-table dt-pending" data-noun="fill-ups">` (the noun words the search box and counts), with
  `table-data` on the table, inside a `card-body py-2` (`site.css` drops DataTables' extra row margin and gutters so the
  padding matches the stat cards). A page using them adds `<partial name="_DataTableStyles" />` to its `Styles` section
  and `<partial name="_DataTableScripts" />` to `Scripts`; the settings are in `wwwroot/js/data-table.js`, shared by both.
  A new table should follow the same pattern rather than get its own script.
- Cells whose text doesn't sort as-is carry `data-order` (dates as ISO, Real MPG, £/litre after voucher).
- Defaults: 10 rows per page; sorted by Date (newest first) then Vehicle (A–Z, found by `<th data-column="vehicle">`, absent on a vehicle's page). The Fuel Log query returns the same order so nothing moves when DataTables starts. If you change the page size, change the `dt-pending` row limit in `site.css` too (a test checks they agree).
- `dt-pending` on the wrapper shows only the first page of rows until DataTables has loaded (no jump); the script removes it in a `finally`, so the full table still shows if the CDN fails.
- Column widths stay put while paging, searching or switching tables: each table repeats rows in an invisible,
  zero-height `<tfoot class="column-sizer">` (`site.css`): the one with the most characters in each figure column (digits
  are `tabular-nums`, so equal width) and one per distinct name (vehicle, station, garage). `data-table.js` sets DataTables up
  without it (DataTables would rework it as column footers) and puts it back, with `autoWidth: false` (DataTables would
  otherwise fix widths from each table's own rows). A page with several tables passes all their fill-ups in
  `ViewData["SizeColumnsFrom"]` so they match (the station page's fuel type tabs; `_ServiceTable` takes it too, for the
  Services tab's last-12-months and all-time tables, passed through the partial's `view-data`). Rows stay on one line; on a phone a
  table scrolls sideways within its card (`table-responsive`), not the page.

**General**
- One type per file: each top-level class, record, enum, interface or struct has its own file, named after it
  (`FuelPlaceList<T>` in `FuelPlaceList.cs`; a page model in its `.cshtml.cs`), in the same folder and namespace as
  the code it belongs with. Nested types are fine. Applies to the tests too; generated migrations are exempt.
- Prefer shared partials and small shared scripts over copying markup or JS between pages.
- No breadcrumbs: a page below another has a Back button on the right of its header (`Pages/Shared/_BackButton`,
  model `BackButton(href)`), a plain link to the parent page, unless it's a form with its own Cancel (e.g. station Edit,
  Merge and Delete). A page reached from several places (the station page)
  uses `BackButton(href, History: true)`: it goes back to the previous page of this app, else follows the link.
- A form partial rendered with `for="X"` (`_OwnerForm`, `_VehicleForm`, `_ServiceForm`) must not hold the
  `asp-validation-summary="ModelOnly"`: its prefix hides page-level errors such as an edit conflict. Put it on the page.
  It also can't read the page model (its model is the input); pass extras in `ViewData`.
- Avoid visible flicker on page load: render initial state server-side where possible.
- Check pages at phone width as well as desktop.
