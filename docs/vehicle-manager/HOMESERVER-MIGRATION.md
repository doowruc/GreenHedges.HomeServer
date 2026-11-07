# Moving Vehicle Manager into GreenHedges.HomeServer

The plan for bringing this app into the public `doowruc/GreenHedges.HomeServer` repository and running it on the home
server, alongside the Libre and Weather collectors. The individual steps, and which are done, are in
[TODO.md](TODO.md) under **Move to the home server**.

**Progress:** step 1 (local SQL Server, tests off Azure) was done in the old repository (its PR #2), step 2 (this
import, with its history) on 2026-10-23, then step 3: hosting basics (PR #19), alerts and heartbeat (PR #20) and database backups (PR #21); steps 4 and 5
(SQL Server, Tailscale Services, the app's service and the data from Azure: PRs #22-#24) on the evening of 2026-10-23.
Vehicle Manager runs on the home server; step 6 (deleting the Azure database, retiring the old repository) remains. Where the text below says "this repo", it means the old `doowruc/VehicleManager`.

**Decided (2026-10-23):**

- **SQL Server, not SQLite.** Vehicle Manager keeps SQL Server, as **SQL Server Express in Docker** on the server, so
  its model (rowversion concurrency, the NULLs-equal unique index, case-insensitive collation, `date` columns) and
  migrations stay as they are. The collectors keep their SQLite archives: they're archive-first and must never depend
  on another process being up. (Whether the Weather collector's Grafana dashboards read derived tables from SQL Server
  is decided in its Phase 2.)
- **Its own address through Tailscale Services:** `https://vehicles.<tailnet>.ts.net`, no port or path, on the
  tailnet only.
- **CLAUDE.md:** a short root one for the whole HomeServer repo, and Vehicle Manager's detailed one in
  `src/VehicleManager/CLAUDE.md` (Claude Code reads it only when working there).

## Where it ends up

| Now | In GreenHedges.HomeServer |
|---|---|
| `VehicleManager/` (`VehicleManager`) | `src/VehicleManager/GreenHedges.HomeServer.VehicleManager/` (`GreenHedges.HomeServer.VehicleManager`) |
| `VehicleManager.AppHost/` | `src/VehicleManager/GreenHedges.HomeServer.VehicleManager.AppHost/` |
| `VehicleManager.Tests/` | `tests/VehicleManager/GreenHedges.HomeServer.VehicleManager.Tests/` |
| `VehicleManager.slnx` | Folders in `GreenHedges.HomeServer.slnx` |
| `README.md`, `TODO.md` | `docs/vehicle-manager/README.md`, `docs/vehicle-manager/TODO.md` |
| `CLAUDE.md` | `src/VehicleManager/CLAUDE.md`, minus the Git section and anything repo-wide (that goes in the new root `CLAUDE.md`) |
| `dotnet-tools.json`, `aspire.config.json` | Repo root (`dotnet-ef` joins the tool manifest; `aspire.config.json` points at the new AppHost path) |
| Package versions in each `.csproj` | `Directory.Packages.props` (central package management, as the collectors) |
| (new) SQL Server container | `deploy/sqlserver/` (`compose.yaml`, `HOST-SETUP.md`): shared server infrastructure, like Nightscout's stack |
| (new) systemd unit, host setup | `deploy/vehicle-manager/` (`vehiclemanager.service`, `HOST-SETUP.md`), and a `vehicle-manager` case in `deploy/deploy.sh` |
| Personal setup (Auth0 tenant, Azure resources, server settings) | `notes/vehicle-manager/NOTES.md` in the private `GreenHedges.HomeServer.Notes` |

Namespaces follow the folders: `VehicleManager.Services` becomes `GreenHedges.HomeServer.VehicleManager.Services`, and so
on, including `@namespace`/`@using` in `_ViewImports.cshtml`, `RootNamespace`, `InternalsVisibleTo`, the EF migrations
and the tests. It's a web app, not a collector, so it sits beside `src/Collectors/` rather than in it. Keep the
`UserSecretsId` so local user secrets carry on working.

## Is it fit for a public repo?

Checked on 2026-10-23, across the working tree **and all 50 commits** of history (code, docs and commit messages):

- **No secrets.** Connection strings, Auth0, Resend, DVLA and DVSA settings have only ever been in user secrets; the
  tracked files hold placeholders (`<server>`, `<client id>`, `re_...`) and test values (`"tests"`, `samples.auth0.com`).
- **No personal data.** Emails are `@example.*` or `@*.invalid`; registrations are the placeholders `AB12 CDE` and
  `ZZ99 ZZZ`; commit identities are the GitHub noreply address. Example station names in the README (Meadowbank, Hill
  Top, Riverside) are generic.
- **Untracked file kept out:** an unrelated, untracked DNS export file (not part of the app) was in the project
  folder, untracked; it has since been moved out. Check the folder again for stray files before importing.
- **Tone:** the docs speak of "my real data" and "the vehicles I own", which is fine for a personal public repo, as the
  collectors' docs do.
- The fuel spreadsheet (`Fuel2.xlsx`) and backups were never committed; keep it that way (ignore `*.xlsx`, `*.json.gz`
  and `*.bak` under `src/VehicleManager/` in HomeServer's `.gitignore`).

So the history can be made public. **Recommendation:** import it with its history (rewritten into the new folders,
then merged), so the 50 commits and their reasoning stay with the code.

## Order of work

1. **Local SQL Server for development and tests**, in this repo first: a container on the Mac, and the tests on a
   throwaway database instead of the real Azure one. Small, and it removes the riskiest habit (tests against real data)
   before anything moves.
2. **Import into GreenHedges.HomeServer**: history import, namespace rename, central packages, docs and CLAUDE.md
   split, CI green with a SQL Server service container. One PR there.
3. **Ready to run on the server**: data-protection keys, forwarded headers, Kestrel on 127.0.0.1, `/health`,
   heartbeat and alerts, nightly backups and off-site copies through `Common`.
4. **The server's side**: the SQL Server container, Tailscale Services (one-off: tag the server, an access rule),
   then the app's service account, unit and `svc:vehicles`, and Auth0's callback URLs.
5. **Move the data** from Azure SQL to the server's container (a `.bacpac`, so every table comes across), then switch.
6. **Tidy up**: delete the Azure database and SQL server once the server copy is proven; retire this repository
   (bundle it, then delete); update `SERVER.md` and the notes.

## 1. Local SQL Server for development and tests

- **Mac:** SQL Server in Docker Desktop (`mcr.microsoft.com/mssql/server`, which runs under Rosetta on Apple Silicon;
  not Azure SQL Edge, which is retired). Developer edition is fine on a development machine. A `compose.yaml` for it,
  with the data in a named volume and the port on `127.0.0.1:1433`. Document it in the README; Windows can use LocalDB.
- **Tests:** the `Data/` and `Pages/` tests run against a **throwaway database** created for the test run (e.g.
  `VehicleManager_Tests_<guid>`, migrated, then dropped), instead of the real database with `test|…` users. The per-user
  isolation tests still use separate users within it. `[DatabaseFact]` still skips when no SQL Server is configured.
  The rules about never touching real data stay, as a backstop.
- **Local data:** copy real data into the local database with the app's backup and restore when needed (or a
  `.bacpac`), never point development at the server's database.
- Keep `EnableRetryOnFailure`: harmless locally, and useful while the server's container restarts.

## 2. Import into GreenHedges.HomeServer

- Rewrite this repo's history into the new folders (`git filter-repo` with path renames, on a copy), then merge it into
  a HomeServer branch with `--allow-unrelated-histories`. Then one commit renames namespaces and files.
- Central package management: move every `Version` into `Directory.Packages.props`. Check the shared settings in
  `Directory.Build.props` (net10.0, nullable, implicit usings, warnings as errors) match; this app already keeps 0 warnings.
- The Aspire AppHost stays for local development. Check CI builds it without the Aspire CLI (it suppresses ASPIRE010)
  and that the `MessagePack` pin goes into central packages.
- CI: HomeServer's `build-and-test` gains a SQL Server **service container** (Linux runners support it), so the database
  and page tests run on every PR, against a throwaway database.
- Docs: README to `docs/vehicle-manager/README.md`, rewritten for SQL Server in Docker and the server (the Azure SQL
  setup and "if it's ever hosted" sections go); TODO to `docs/vehicle-manager/TODO.md`; CLAUDE.md split as above. The
  HomeServer README gets a row for it, and becomes a list of services rather than collectors.
- **Root `CLAUDE.md`** (new): what the repo is; build and test commands; one branch and PR per change, merge commits,
  delete the branch; nothing personal in the public repo (it goes in `notes/`); never run deploy scripts to test them;
  SSH to the server is read-only; read `notes/server/SERVER.md` before anything that runs on the server. Each project's
  own `CLAUDE.md` holds only what's specific to it.

## 3. Ready to run on the server

- **Data-protection keys** persisted to `/var/lib/vehiclemanager/keys`, or every restart signs everyone out and breaks
  antiforgery tokens.
- **Behind `tailscale serve`**: Kestrel on `127.0.0.1:<port>` only; `UseForwardedHeaders` (proto and host from the local
  proxy) so Auth0's redirect URI is `https://vehicles.<tailnet>.ts.net/callback`.
- **`/health`** with a database check, for the heartbeat and for checking by hand.
- **Heartbeat and alerts** through `GreenHedges.HomeServer.Common`: a healthchecks.io ping while healthy, and email
  alerts for a failed nightly backup or reminder run (the TODO's "Alert on failures"). `Common`'s `CollectorHost` is
  built for worker services: either give `Common` a web-app variant or call its registrations directly. Decide when
  doing it.
- **Backups**: nightly, through `Common`'s `BackupService`, with an `IBackupSource` that runs `BACKUP DATABASE … TO
  DISK … WITH CHECKSUM, COPY_ONLY` into a folder shared with the container, then the usual gzip, 7 kept,
  age-encrypted off-site copy and the Mac's pull. `Common` names backups `*.db.gz`; let a source choose its extension
  (`.bak.gz`). Off-site: a `vehiclemanager/` prefix and write-only IAM user in `deploy/aws/offsite.yaml` (a stack
  update); `vehicle-manager` in `deploy/mac/install-backup-pull.sh`. The app's own emailed JSON backups stayed as a
  user-level copy (removed 2026-11-07: the database backups cover them).
- **Uploads** (import, restore): keep a request-size limit and rate limiting (both in the TODO).
- **Logging**: to the console, so the journal keeps it; OpenTelemetry stays optional (OTLP only when configured).

## 4. The server's side

Read `notes/server/SERVER.md` first, and update it after (service, port, user, folders, container).

**SQL Server container** (`deploy/sqlserver/`, shared, so another app can have its own database later):

- `mcr.microsoft.com/mssql/server` at a **pinned** tag, **Express** edition (`MSSQL_PID=Express`: free for real use,
  10 GB per database), `MSSQL_MEMORY_LIMIT_MB` around 1536 (the server has 8 GB, about 6 GB free, shared with
  MongoDB, Nightscout, the collectors and later Grafana).
- Bound to `127.0.0.1:1433` only (Docker bypasses ufw). Data in `/var/lib/sqlserver` (owner the container's `mssql`
  user, uid 10001), plus a backups folder shared with the app's backup step.
- The `sa` password in a root-only env file, used only for setup. Vehicle Manager gets its **own login**, owner of its
  own database only. `TrustServerCertificate=True` in its connection string is acceptable on localhost.
- `restart: unless-stopped`; Vehicle Manager's unit starts after Docker and retries the database at startup.
- Updates: pull the new pinned tag, restart; minor updates are in place. Note it in `SERVER.md`.

**Tailscale Services** (one-off, then per app):

- Tag the server (e.g. `tag:server`) and re-authenticate it with the tag: only tagged devices can host services. Tagged
  devices don't expire.
- An access rule granting your devices `svc:*` (or each service) on 443, and auto-approval of the server as a host.
- Then `tailscale serve --service=svc:vehicles --https=443 127.0.0.1:<port>`, approved in the admin console:
  `https://vehicles.<tailnet>.ts.net`. Optional: move Nightscout to `svc:nightscout` the same way, and Grafana gets
  `svc:grafana` in the Weather collector's Phase 2.
- Services is in public beta, free on all plans for now; if that changes, fall back to a port on the server's own name
  (only URLs and Auth0's callback change).

**The app:**

- Service account `vehiclemanager`; `/opt/vehiclemanager` (app), `/var/lib/vehiclemanager` (keys, backups, logs),
  `/etc/vehiclemanager/env` (connection string, Auth0, Resend, DVLA/DVSA, allowed emails, heartbeat, off-site settings).
- systemd unit like the collectors' (`Type=notify`, hardening, `After=docker.service`).
- `deploy/deploy.sh vehicle-manager`: publish self-contained, install, restart (it currently assumes collectors; add the
  case and its project path). Migrations apply at startup, as now (a single instance, so that's safe).
- **Auth0:** add `https://vehicles.<tailnet>.ts.net/callback` and `/Account/SignedOut` to the allowed callback and logout
  URLs (keep localhost for development). Auth0 still decides who's allowed; the tailnet limits who can reach it at all.
- Coravel jobs (01:00 UTC backups, until 2026-11-07; 07:00 UK reminders) run on the server from then on. Stop running a local copy against
  real data, or both send.
- Phone access: Tailscale on the phone, as for Nightscout. Revisit the PWA item then.

## 5 and 6. Switch over and tidy up

- **Data:** export a `.bacpac` from Azure SQL (`SqlPackage /Action:Export`) and import it into the server's container
  (`/Action:Import`), so every table comes across exactly, including `UserSettings` and `SentReminder` (which the app's
  backup leaves out). Check with the app's own backup and the dashboard figures before and after.
- Freeze edits during the copy, then use only the server.
- Keep the Azure database for a couple of weeks as a fallback, with the `.bacpac` kept with the Mac's backups; then
  delete the database and SQL server (and its firewall rules). Check nothing else uses them.
- Retire `doowruc/VehicleManager`: bundle it to `~/Backups/OldRepos`, then delete it on GitHub, as with the collectors'
  old repositories.
- TODO items that no longer apply: "Develop against a local database again" (becomes step 1), "Deployment … Azure App
  Service", "Receipt photo … Azure Blob Storage" (local disk on the server instead), "CI on GitHub Actions"
  (HomeServer's CI covers it).
