# Utilities

A web app for my home's electricity, gas and water meter readings, and later the bills. Part of
[GreenHedges.HomeServer](../../README.md); the plan, data model and build order are in [DESIGN.md](DESIGN.md).

**So far** (build step 4): a page each for **Electricity**, **Gas** and **Water**, with a form to add a reading (the
date and what the meter shows) and the table of readings, latest first, with what the spreadsheet worked out: units
used since the reading before, the days between them, and units a day (litres a day for water). Readings can be
edited and deleted. Above the form, a **chart** of units a day at each reading's date, with a trendline (a polynomial
of order 4, as the spreadsheet's chart had, fitted against the date); it appears once there are two daily figures. The **dashboard** shows each utility's latest reading and daily figure. Backups, alerts, the
heartbeat and telemetry are built in, as the other web apps', and off until configured. **Importing** the spreadsheet
the readings were kept in (below). It isn't on the server yet.

## Meters

A utility's first reading adds its first meter, and its units start with the second reading. A meter only counts up,
so a reading lower than the one before is refused. When a meter is **replaced**, add it first with **New meter** on
the utility's page: the date it was installed, what it showed then (usually 0) and, if you like, its serial number.
Readings from that date on are the new meter's; its first one counts its units from the start reading, over the days
since the old meter's last reading, and is marked *New meter* in the table. A meter's details can be edited, and a
meter without readings deleted, from the Meters list.

## Importing the spreadsheet

**Import** in the menu. Upload the workbook (.xlsx): its first sheet needs, for each utility, a title
(**Electricity**, **Gas** or **Water**, e.g. "Water In") in the row above the headings **Date** and **Reading**,
with a row per reading below. Only those two columns are read; units, days and units a day are worked out again. If a
utility has two blocks, the first is used.

The preview shows, for each utility, before anything is saved:

- how many readings it would import, and how many are on dates that already have one (those are left as they are, so
  an updated workbook can be imported again);
- the meters it would add: the first, and a replacement on each date the reading falls, starting at 0. That's only
  for a utility with no meters yet; once it has some, a reading that doesn't fit its meter is skipped instead, so add
  a replacement with **New meter** before importing readings from it;
- whether units a day match the spreadsheet's own **Units/Day** on every row, and which rows don't;
- rows it can't import and why, e.g. a reading with no date;
- the readings, with their figures worked out.

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
