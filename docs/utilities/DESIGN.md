# Utilities: design notes

A web app for my home's electricity, gas and water meter readings, which have lived in one Excel sheet for years;
later, the bills too (suppliers, tariffs, costs). Part of [GreenHedges.HomeServer](../../README.md), built the same
way as [Vehicle Manager](../vehicle-manager/README.md) and [Pay & Pensions](../pay-and-pensions/README.md). This is
the agreed plan; [Build order](#build-order) says how far it has got, and the [README](README.md) how to run it.

## Goals and constraints

**Goals, in priority order:**
1. **Readings:** record each meter reading, with the figures the sheet works out: units used since the reading
   before, the days between them, and units a day.
2. **A page per utility:** a chart of units a day by date with a trendline, a form to add a reading, and the table.
3. **Import:** bring in the existing sheet once.
4. **Bills (later):** supplier, tariff and costs from the bills.

**Constraints:**
- **One login, one home.** Only I sign in; meters belong to the signed-in user.
- **Same conventions as the other web apps:** Razor Pages, EF Core on the server's SQL Server, Auth0 sign-in limited
  by `AllowedEmails`, audit columns and optimistic concurrency, pages that never bind entities, nightly backups pulled
  to the Mac, encrypted off-site copies, email alerts and a heartbeat, telemetry to Grafana, tailnet only.
- **Public repository.** Tests and docs use made-up readings only. The real sheet, bills, meter serial numbers and
  account details stay out of the repository (`notes/utilities/` in the private notes, and the app's database).

## The sheet

One sheet, a block of five columns per utility, one row per reading, oldest first:

| Columns | What |
|---|---|
| A-E | Electricity: Date, Reading, then formulas Units (reading − the one before), Days (date − the one before), Units/Day |
| G-K | Gas, the same |
| M-Q | Water, the same |
| S-W | An old waste water block: ignored |
| X onwards | One-off workings: ignored |

Quirks: each utility's meter has been replaced once, where the reading falls; that row's Units formula is
`reading − 0` (the new meter started at 0) and its Days still count from the last reading on the old meter. Smart
meters give decimals (gas to three places). A couple of stray text cells sit below the water block. The sheet's
charts plot Units/Day against the row, not the date, with an order 4 polynomial trendline on electricity and gas.

## Units

Checked against the bills:

| Utility | The meter counts | Billed in | Shown as |
|---|---|---|---|
| Electricity | kWh | kWh | kWh, kWh a day |
| Gas | m³ | kWh: m³ × 1.02264 (volume correction) × the bill's calorific value ÷ 3.6 | m³, m³ a day. kWh comes with the bills, as the calorific value changes with each one |
| Water | m³ | m³ (waste water as a percentage of it) | m³, and litres a day (1 m³ = 1,000 litres), as the supplier shows it |

## Data model

| Entity | What |
|---|---|
| `Meter` | One physical meter. The root: `IUserOwned`, with a per-user query filter. **Utility** (Electricity, Gas, Water) and **unit** (kWh, m³), both stored as text; optional serial number; **installed on** and **start reading**, both empty for a utility's first meter here (its start isn't known) |
| `Reading` | Belongs to a meter: date and value (`decimal(12,3)`). One per meter and date |

Every table inherits `AuditedEntity`. Readings are filtered through their meter. A meter with readings can't be
deleted (FK `Restrict`).

**Derived, never stored** (a calculation service with unit tests), per utility over all its meters, oldest first:

- **Units:** the reading less the one before on the same meter. A meter's first reading: less its start reading;
  none for a utility's first meter (no start reading), where counting only begins.
- **Days:** since the utility's reading before, even when that was on the meter before, as the sheet has it.
- **Units a day:** units ÷ days.
- **Trendline:** an order 4 polynomial least-squares fit of units a day against the date (the sheet's fits against
  the row, so its curve differs a little where readings are unevenly spaced). With fewer than five daily figures the
  order is the highest they can pin down; with fewer than three there's no trendline.

A reading's meter is the latest one installed on or before its date, so the add form only asks for the date and
reading. A meter only counts up: a reading can't be lower than the one before it on its meter, or higher than the one
after; a lower reading means a new meter, added first.

**Room for bills later:** a supplier and an account (one energy account covers gas and electricity); tariffs with a
name, end date, exit fee, unit rate and daily standing charge, which change mid-period; bills with their lines, VAT,
credits and payments; gas calorific values. None of it changes the readings model.

## Pages

- **Dashboard:** each utility's latest reading and units a day.
- **Electricity, Gas, Water:** the chart (Chart.js, a true date axis) with its trendline; the add-reading form (date
  defaults to today); the table of readings with units, days and units a day; edit and delete.
- **New meter:** for a replacement: the date, start reading and serial number. A meter can be edited while every
  reading stays on the meter it was taken from, and deleted while it has no readings.
- **Import.**

## Import

Upload the `.xlsx` (ClosedXML). Each utility's block is found by its title (Electricity, Gas, Water…) above the
headings Date and Reading, and only those two columns are read; a utility's second block (the old waste water one)
and everything else are ignored. Rows with only one of the two, or with text, are skipped with the reason. For a
utility with no meters yet, a reading lower than the one before starts a new meter, installed that day at 0. A
utility that already has meters only gains readings on dates it doesn't have, each on the meter its date belongs to;
one that doesn't fit there is skipped, never made a new meter. A preview shows each utility's readings, meters and
worked-out figures before anything is saved, and checks units a day against the sheet's own Units/Day column.

## Hosting

| Thing | Value |
|---|---|
| Code | `src/Utilities/GreenHedges.HomeServer.Utilities`, `tests/Utilities/…Tests` |
| Docs, deploy | `docs/utilities/`, `deploy/utilities/` (systemd unit, deploy script, database setup, host setup) |
| Service | systemd `utilities` (user `utilities`, in `sqlbackups`), `127.0.0.1:5082` |
| Address | `https://utilities.<home domain>` through Caddy (a new block in `deploy/caddy/Caddyfile`) |
| Database | `Utilities` on the shared SQL Server, login `utilities` (owner) |
| Folders | `/opt/utilities`, `/var/lib/utilities` (keys, backups, alert state), `/etc/utilities/env` |
| Backups | Nightly SQL Server backup at 04:00, `*.bak.gz` in `/var/lib/utilities/backups`, 7 kept |
| Off-site | `age`-encrypted copies to the shared bucket under `utilities/`, by a new write-only IAM user in `deploy/aws/offsite.yaml` |
| Mac | Daily pull to `~/Backups/Utilities` (a `utilities` case in `deploy/mac/pull-backups.sh` and `install-backup-pull.sh`) |
| Monitoring | healthchecks.io heartbeat; Resend alerts |
| Telemetry | OpenTelemetry over OTLP to the server's Grafana, as the other web apps (`service_name` `Utilities`) |

## Build order

One branch and PR each:

**Progress:** steps 1 (PR #104), 2 (PR #106), 3 (PR #107), 4 (PR #108) and 5's deploy files done. Next: deploying it.

1. **Skeleton.** The project on `Common.Web` (sign-in, `/health`, backups, alerts, heartbeat and telemetry, all off
   until configured), the data model and first migration, a dashboard with each utility's latest reading, the test
   project.
2. **Readings.** The calculation service; a page per utility with the table and the add form; edit and delete; new
   meter.
3. **Charts.** Units a day by date with the order 4 trendline.
4. **Import** of the sheet, with the preview.
5. **Deploy.** Deploy files, Caddy block, database setup, off-site user, Mac pull, host setup; the server's notes
   updated.

**Later:** bills (suppliers, accounts, tariffs, costs, gas in kWh).
