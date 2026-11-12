# Utilities

A web app for my home's electricity, gas and water meter readings, and later the bills. Part of
[GreenHedges.HomeServer](../../README.md); the plan, data model and build order are in [DESIGN.md](DESIGN.md).

**So far** (build step 1): the app's skeleton. It signs in, has its database (meters and their readings), and a
dashboard showing each utility's latest reading; there are no pages to add readings yet. Backups, alerts, the
heartbeat and telemetry are built in, as the other web apps', and off until configured. It isn't on the server yet.

## Running it locally

It needs SQL Server and an Auth0 application, like Vehicle Manager.

1. **SQL Server:** the local container from the repo root's `compose.yaml`, set up as in Vehicle Manager's
   [Local database](../vehicle-manager/README.md#local-database-sql-server-in-docker) (`.env`, then
   `docker compose up -d --wait`).
2. **Settings,** in user secrets, from `src/Utilities/GreenHedges.HomeServer.Utilities` (the password is the one in
   `.env`; the database is created and migrated on first run):

   ```bash
   dotnet user-secrets set "ConnectionStrings:Utilities" "Server=localhost,1433;Database=Utilities;User ID=sa;Password=<password>;TrustServerCertificate=True"
   dotnet user-secrets set "ConnectionStrings:UtilitiesTests" "Server=localhost,1433;Database=master;User ID=sa;Password=<password>;TrustServerCertificate=True"
   dotnet user-secrets set "Auth0:Domain" "<your-tenant>.auth0.com"
   dotnet user-secrets set "Auth0:ClientId" "<client id>"
   dotnet user-secrets set "Auth0:ClientSecret" "<client secret>"
   dotnet user-secrets set "AllowedEmails" "me@example.com"
   ```

   The Auth0 application is a **Regular Web Application**, set up as Vehicle Manager's
   ([Signing in](../vehicle-manager/README.md#signing-in-auth0)), with `http://localhost:5082` and
   `https://localhost:7082` in place of Vehicle Manager's ports: callback `…/callback`, logout `…/Account/SignedOut`.
   It can be another application in the same tenant.
3. **Run** with `dotnet watch` in that folder → <http://localhost:5082>.

`ConnectionStrings:UtilitiesTests` is for the tests: each run creates (and a later run drops) its own throwaway
database on that server. Without it, the database and page tests are skipped.

## Privacy

Meter readings, bills, meter serial numbers and account numbers are personal. The repository holds only code and
made-up test data: spreadsheets and PDFs are git-ignored under `src/Utilities/` and `tests/Utilities/`, and the real
ones stay in the app's database and the private notes.
