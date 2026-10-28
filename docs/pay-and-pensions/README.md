# Pay & Pensions

A web app for my household's payslip history, and later our pension pots. Part of
[GreenHedges.HomeServer](../../README.md); the plan, data model and build order are in [DESIGN.md](DESIGN.md).

**So far** (build step 3): people, each person's employers, and the pay elements on their payslips (the lines, e.g.
Salary, Tax, Employer pension, each with a kind and a category); and **payslips**, a table per tax year with the
figures the spreadsheets worked out and the year's totals. A new payslip starts from the latest one, so usually only
a few amounts change. The spreadsheet import, the dashboard, backups and deployment follow in the next steps.

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

## Privacy

Payslips are personal. The repository holds only code and made-up test data: spreadsheets are git-ignored under
`src/PayAndPensions/` and `tests/PayAndPensions/`, and real names, employers and figures stay in the app's database
and the private notes.
