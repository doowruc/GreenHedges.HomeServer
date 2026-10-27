# To do

Ideas for future features and improvements, roughly in order of how useful they'd be.

## Move to the home server

Bringing the app into the public `GreenHedges.HomeServer` repository (done: this is it) and running it on the home server, on SQL Server
Express in Docker instead of Azure SQL, at `https://vehicles.<tailnet>.ts.net` (Tailscale Services). The plan, with the
reasoning and the decisions: [HOMESERVER-MIGRATION.md](HOMESERVER-MIGRATION.md). In order:

- [x] **Move an unrelated, untracked DNS export file out of the repository folder** (untracked, not part of the
      app), so it can't be committed or copied across. Gone by 2026-10-23.
- [x] **Local SQL Server for development and tests** (in this repo first):
  - [x] A `compose.yaml` for SQL Server on the Mac (`mcr.microsoft.com/mssql/server` 2025, Developer edition, named
        volume, `127.0.0.1:1433`, sa password in a git-ignored `.env`), run with OrbStack; documented, with LocalDB for
        Windows, in the README's Local database.
  - [x] Database and page tests on a throwaway database per test run (`TestDatabase`: created and migrated on the
        server in `ConnectionStrings:VehicleManagerTests`, refuses Azure SQL; older ones dropped by the next run)
        instead of the real Azure database; the app under test can't reach its own database (`AppFactoryTests`).
        `[DatabaseFact]` still skips without a test SQL Server. CLAUDE.md's Tests section updated. All 272 tests run.
- [ ] **Point local development at the local database** (from the item above); copy data in with backup and restore
      when needed. Documented in the README; switching `ConnectionStrings:VehicleManager` is my call.
- [x] **Import into GreenHedges.HomeServer** (one PR there, 2026-10-23):
  - [x] Import this repo with its history (rewritten into `src/VehicleManager/`, `tests/VehicleManager/` and
        `docs/vehicle-manager/`), merged with `--allow-unrelated-histories`.
  - [x] Rename namespaces and projects to `GreenHedges.HomeServer.VehicleManager` (`.AppHost`, `.Tests`), including
        Razor `_ViewImports`, `RootNamespace`, `InternalsVisibleTo` and the EF migrations; keep the `UserSecretsId`.
  - [x] Package versions into `Directory.Packages.props`; `dotnet-ef` into the root tool manifest; `aspire.config.json`
        to the new AppHost path; projects into `GreenHedges.HomeServer.slnx`.
  - [x] CI: a SQL Server service container in `build-and-test`, so the database and page tests run on every PR; check
        the Aspire AppHost builds there.
  - [x] Docs: README and TODO to `docs/vehicle-manager/` (README rewritten for SQL Server in Docker and the server);
        HomeServer README lists services rather than collectors.
  - [x] CLAUDE.md: a new short root `CLAUDE.md` for the whole repo (build and test, branch/PR/merge rules, nothing
        personal in the public repo, never run deploy scripts, read-only SSH, read `notes/server/SERVER.md` first); this
        one moves to `src/VehicleManager/CLAUDE.md` without its Git section.
  - [x] Ignore `*.xlsx`, `*.json.gz` and `*.bak` under `src/VehicleManager/`, so a spreadsheet or backup can't be
        committed.
- [x] **Ready to run on the server:**
  - [x] Persist ASP.NET data-protection keys under the data folder (else each restart signs everyone out):
        `DataProtection:KeysFolder`.
  - [x] Forwarded headers from `tailscale serve`, so Auth0 gets `https://vehicles.<tailnet>.ts.net/callback` (from
        loopback only; checked against Tailscale's source: it sets `X-Forwarded-Proto`/`-For` and keeps `Host`).
  - [x] Kestrel on `127.0.0.1` only, port from configuration: ASP.NET's default is localhost only; the port comes from
        `ASPNETCORE_URLS` in the env file (README: Running behind a proxy).
  - [x] `/health` endpoint with a database check (replaces "Health-check endpoint" below): anonymous,
        `Healthy`/`Unhealthy`, 10 s timeout.
  - [x] Heartbeat (healthchecks.io) and email alerts through `GreenHedges.HomeServer.Common` for failed nightly backups
        and reminder runs (covers "Alert on failures" below). Done: `Monitoring/` (database unreachable 5 min; nightly
        backups or morning reminders failing, or not run for 26 h); heartbeat while the database answers; `--test-alert`. Decide whether `Common` gets a web-app host variant or the
        app calls its registrations directly. **Decided 2026-10-23:** widen `Common`'s `AddAlerting`/`AddHeartbeat`/
        `AddBackups` to `IHostApplicationBuilder`, so this app calls them as the collectors do and keeps its own
        OpenTelemetry logging.
  - [x] **Decide how Resend is used across the repo.** **Decided 2026-10-23: keep both.** `Common`'s `ResendAlertSender` calls Resend's REST API directly
        (plain-text alerts, no package); this app uses Resend's .NET SDK (`IResend`: backup attachments, reminder
        emails, `FakeResend` in tests). Either keep both (each the simpler tool for its job) or move `Common` onto the
        SDK so there's one client. Decide when the alerts above go through `Common`.
  - [x] Nightly backups through `Common`: `Backups/SqlServerBackupSource` (`BACKUP DATABASE … WITH CHECKSUM,
        COPY_ONLY` into a folder shared with the container, decided 2026-10-23: `.bak`, not a `.bacpac`); `Common` lets
        a source choose its file extension (`.bak.gz`); off-site copies (`<stack>-vehicles`, prefix `vehiclemanager/`, in
        `deploy/aws/offsite.yaml`) and the Mac's pull (`install-backup-pull.sh vehicle-manager`); "Backups failing" and
        "Off-site backup failing" alerts. The emailed JSON backups stay as a user-level copy.
  - [x] After merging: update the `greenhedges-offsite` stack (the new IAM user), then create its access key (done
        2026-10-23).
- [x] **The server's side** (read and update `notes/server/SERVER.md`; done 2026-10-23):
  - [x] `deploy/sqlserver/` (deployed 2026-10-23, PR #22): `compose.yaml` (pinned image tag, Express edition, memory limit about 1.5 GB,
        `127.0.0.1:1433`, data in `/var/lib/sqlserver`, a backups folder shared with the app) and `HOST-SETUP.md`.
        The backups folder (`/var/lib/sqlserver/backups`, `/var/opt/mssql/backups` in the container): owner uid 10001
        (`mssql`), group `sqlbackups` (the app's account joins it), mode `2770` (setgid), as SQL Server writes files
        `0660`; `deploy-sqlserver.sh` installs and checks it;
        `sa` only for setup, a `vehiclemanager` login owning only its own database.
  - [x] Tailscale Services, one-off: tag the server, an access rule for `svc:vehicles` on 443, auto-approval of the
        server as a host: `deploy/TAILSCALE-SERVICES.md` (done).
  - [x] `deploy/vehicle-manager/` (PRs #23 and #24): `vehiclemanager.service` (systemd, `Type=notify`
        via `AddSystemd`, hardened, after Docker, group `sqlbackups`), `setup-database.sh` (database, login, connection
        string; the password never leaves the host) and
        `HOST-SETUP.md` (account, `/opt` and `/var/lib/vehiclemanager`, `/etc/vehiclemanager/env`,
        `tailscale serve --service=svc:vehicles --https=443 127.0.0.1:<port>`).
  - [x] `deploy/deploy.sh vehicle-manager` (and `deploy/vehicle-manager/deploy.sh`).
  - [x] Auth0: add the `https://vehicles.<tailnet>.ts.net` callback and signed-out URLs (keep localhost for development).
  - [x] Personal setup (Auth0 tenant, port, healthchecks check) in `notes/vehicle-manager/NOTES.md` of
        `GreenHedges.HomeServer.Notes`, never the public repo.
- [x] **Switch over** (done 2026-10-23; the steps are §3 of `deploy/vehicle-manager/HOST-SETUP.md`, done before the first start): a `.bacpac` export from Azure SQL imported into the server's container (every table, including
      `UserSettings` and `SentReminder`); compare with the app's backup and the dashboard figures; then use only the
      server, and stop running a local copy against real data (Coravel jobs would send twice).
- [ ] **Tidy up:** after a couple of weeks (from about 2026-11-07), keep the `.bacpac` with the Mac's backups and delete the Azure SQL database
      and server; bundle this repository to `~/Backups/OldRepos` and delete it on GitHub.

## New features

- [x] **MOT, tax, insurance and service reminders.** Add due dates for each vehicle (MOT, VED, insurance renewal,
      next service). Show what's coming up on the dashboard, and email a reminder about 30 days and 7 days before,
      using the existing Coravel and Resend setup.
  - [x] Optional: look up MOT and tax due dates from the registration number using the DVLA Vehicle Enquiry API and
        the DVSA MOT History API (both free with an API key). Each is optional; messages name only the ones set up.
    - [x] Register for the DVSA MOT History API and add its four `MotHistory:*` settings.
    - [ ] Register for the DVLA Vehicle Enquiry Service once it reopens (registration was closed in October 2026)
          and add `Dvla:ApiKey`, so tax dates are looked up too.
  - [x] MOT history on the vehicle page: an MOT tab listing every test with its defects and advisories (kept in step
        with the DVSA on each check, and in the backup), a mileage chart whose points scroll to their test, and
        "Previous owner" badges for tests before the purchase date. Header links check the MOT and tax separately.
  - [x] Use the MOT tests' mileages in the mileage trend, alongside the fill-ups'.
  - [ ] Check the MOT tests' recorded mileages against the fill-up odometer readings, and flag big mismatches.
  - [x] Service due by mileage as well as date, estimated from the recent miles per month.
- [x] **Service history** for each vehicle: date, mileage, garage and location, and whether it was under a previous
      owner (ticked automatically for dates before the purchase date). A service interval (months and/or miles) on the
      vehicle sets the next service's due date and mileage when its latest service is added. The history is a
      DataTable with red badges for services later than the interval after the one before.
- [ ] **Running costs other than fuel.** One `Expense` table (category, date, cost, odometer, notes, optional
      attachments) for servicing, tyres, repairs, insurance, tax, parking and tolls. This makes pence per mile a true
      total cost of ownership rather than fuel only. Add it to the backup.
  - [ ] Start with a cost on each service record, now that there's a service history.
- [ ] **Vehicle lifecycle: sold/disposed date and an "active" flag.** Hide sold vehicles from the dashboard and the
      fill-up vehicle dropdown, but keep their history. Optionally record purchase and sale price to show depreciation.
- [ ] **Partial fill-ups.** A "full tank" flag on `FuelEntry`, so real MPG is worked out from one full fill to the
      next and a partial fill doesn't skew it. Still total miles ÷ total gallons.
- [ ] **Export.** A ClosedXML `.xlsx` export of the fuel log (filtered by vehicle or year), mirroring the import
      format so data can make the round trip.

## Smaller improvements

- [x] **Vehicle page tabs:** Fuel, Services and MOT on separate tabs, the active one kept in the address (`?tab=`) and
      when switching vehicle. Services and MOT have the 12 months / all time switch and a mileage chart, with previous
      owners' readings shown differently.
- [x] **Tidier menus and buttons:** the theme choice is a Theme item in the user menu; Add fill-up moved from the menu
      bar onto the Fuel Log page, the Fuel tab and the dashboard cards (with Add service beside it); small buttons,
      selects and pills (period toggle, station fuel types) throughout.
- [x] **Back buttons instead of breadcrumbs:** the station list and station page have a Back button in the header (the
      station page's returns to wherever it was opened from); form pages rely on Cancel. Every form's Cancel and Back
      is an outlined button rather than a link.
- [ ] **Odometer sanity checks on the fill-up form:** warn (not error) when the odometer is lower than the previous
      fill-up's, or when trip miles and the odometer difference disagree a lot.
- [ ] **Prefill the fill-up form** with the last vehicle, station and pump price used, and today's date.
- [ ] **Station comparison:** a table of the cheapest stations over the last 3 months for each fuel type, building on
      the station pages' price data.
- [x] **Fuel spending on the dashboard:** spend each month (last 12 months) or year (all time), stacked by vehicle, and
      a year-on-year chart of each year's running total, with a table of yearly totals and the change on the year before
      (this year against last year to the same day). Running costs can join it once they're recorded.
- [x] **Installable on a phone (PWA):** a manifest and icons so it opens like an app from the home screen. *Done
      2026-10-24: favicon (tabs), Apple touch icon, `site.webmanifest` (`standalone`, "Vehicles"); no service worker, as it
      needs the server anyway.*
- [ ] **Receipt photo** on a fill-up, stored on the server's disk (under the data folder, so it's backed up; was Azure Blob Storage before the move to the home server). Only if it'd actually be used: it adds storage,
      backup format changes and some cost.

## Technical improvements

- [x] **Edit conflict warnings now show on every edit page:** the vehicle and owner pages hid "someone changed this
      after you opened it", because the validation summary was inside the prefixed form partials. Fixed and tested.
- [x] **One type per file** throughout, now a rule in CLAUDE.md.
- [x] **Shared DataTables setup:** `data-table.js` and `_DataTableStyles`/`_DataTableScripts` serve both the fuel log
      and the service history, so a new table follows the same pattern.
- [x] ~~**Develop against a local database again,**~~ *Done as Move to the home server's first step: SQL Server in Docker (`compose.yaml`), the tests on it.* Was: instead of the Azure one. It must stay SQL Server, because the
      model relies on it (`rowversion` concurrency, the unique index that counts NULLs as equal, `date` columns,
      retry-on-failure). Support all three platforms, with a setup for each:
  - Windows: SQL Server Express LocalDB (`(localdb)\MSSQLLocalDB`), which comes with Visual Studio or as a small
    separate install.
  - Mac and Linux: SQL Server in a container (Docker or Podman, the `mcr.microsoft.com/mssql/server` image; on Apple
    Silicon it runs under Rosetta in Docker Desktop). Not Azure SQL Edge, which has been retired.
  - Choose by the `ConnectionStrings:VehicleManager` user secret, as now. Document each setup in the README, maybe
    with a `compose.yaml` for the container.
  - Database tests would then run locally too (still as throwaway `test|…` users). Use backup and restore to copy
    real data between the Azure and local databases.
- [ ] **Persistent logging, with OpenTelemetry.** Logs only went to the console, so a failed nightly job
      (`ScheduledBackups`, `ScheduledReminders`) or a page exception was lost once the terminal closed. OpenTelemetry
      (OTel) logs, traces and metrics are sent over OTLP, so the destination can change without changing code.
  - [x] Locally, with the Aspire dashboard: `VehicleManager.AppHost` runs the app alongside the dashboard (no Docker
        needed). The app sends telemetry only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, as the AppHost does; plain
        `dotnet watch` and the tests stay console-only. Logs go through `builder.Logging.AddOpenTelemetry`, so existing
        `ILogger` calls are kept; traces cover ASP.NET Core, `HttpClient` (Resend, DVLA, DVSA) and SQL Client; each
        nightly job run is a trace with a span per user. Logs no longer carry registrations or emails, and the DVSA's
        request URLs are redacted in traces.
  - [ ] Grafana Cloud, for telemetry kept beyond a local session (or, once on the home server, its own Grafana, planned for the Weather collector's dashboards; decide then): [the free tier](https://grafana.com/products/cloud/free-tier)
        (50 GB each of logs and traces a month, 10k metric series, 14 days' retention, free indefinitely) takes OTLP
        directly. Set the standard `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS` (the stack's token) and
        `OTEL_SERVICE_NAME` from user secrets or environment variables, as with the other secrets; the endpoint and
        token are on the stack's OpenTelemetry setup page. No code changes needed. Grafana's alerting could then cover
        "Alert on failures" below, e.g. a failed nightly job or no successful backup in 48 hours. It sends data to a
        third party, so check what the logs and traces carry first: user ids are Auth0 ids; Resend errors may quote
        an email address.
- [ ] **Log more of what matters.** Sign-ins and access-denied attempts (`AllowedUsers`), restores and imports
      (counts of rows), merges of brands/stations, concurrency conflicts in `TrySaveEditAsync`, and unhandled
      exceptions via the `/Error` page with its request id.
- [x] **Alert on failures.** *Done with the move to the home server (Monitoring/): failed or missing nightly backup and reminder runs alert by email; there's no weekly "all OK", as the heartbeat covers silence.* Email (via Resend) or alert when the nightly backup job fails for any user, instead of
      only logging it. Optionally a weekly "all OK" summary so silence isn't mistaken for success.
- [x] ~~**Health-check endpoint**~~ *Moved to Move to the home server (`/health`).* (`AddHealthChecks` with a database check), if the app is ever hosted somewhere other
      than locally.
- [ ] ~~**CI on GitHub Actions:**~~ *Comes with GreenHedges.HomeServer's CI after the import, with a SQL Server service container so the database tests run there too.* build (0 warnings) and run the unit and page tests on each push. Database tests skip
      without a connection string, so they can stay local.
- [ ] **Dependency checks:** Dependabot for NuGet updates, or run `dotnet list package --vulnerable
      --include-transitive` and check for outdated packages from time to time.
- [ ] **Security headers:** a Content-Security-Policy allowing only self and the jsDelivr CDN (with Subresource
      Integrity hashes on the CDN scripts and stylesheets), plus `X-Content-Type-Options` and `Referrer-Policy`.
- [ ] **Rate limiting** on the upload pages (import and restore) and a maximum request size, using ASP.NET's
      built-in rate limiter.
- [ ] ~~**Deployment**~~ *Replaced by Move to the home server (the home server, not Azure).* Was: if it's ever hosted: Azure App Service with settings in Key Vault or app settings, a managed
      identity for Azure SQL instead of a SQL password, and applying migrations as a deploy step rather than at
      startup.
