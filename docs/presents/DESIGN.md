# Presents: design notes

A web app for what we spend on our children's Christmas presents, which has lived in an Excel workbook with a tab a
year. It keeps the purchases, splits the big ones, and works out the cash each child needs so that every child's
Christmas comes to the same. Part of [GreenHedges.HomeServer](../../README.md), built the same way as
[Utilities](../utilities/README.md), [Vehicle Manager](../vehicle-manager/README.md) and
[Pay & Pensions](../pay-and-pensions/README.md). This is the agreed plan; [Build order](#build-order) says how far it
has got.

## Goals and constraints

**Goals, in priority order:**
1. **Purchases:** record each present: who it's for, what, where, when and the price, with the splits the sheet did
   by hand.
2. **A year's summary:** what each child has had, how many presents, and the cash that balances them, worked out
   rather than typed in.
3. **History:** each year's totals side by side.
4. **Import:** bring in the existing workbook once.

**Constraints:**
- **One login, one household.** Only I sign in; children are records in my data, as people are in Pay & Pensions.
- **Same conventions as the other web apps:** Razor Pages, EF Core on the server's SQL Server, Auth0 sign-in limited
  by `AllowedEmails`, audit columns and optimistic concurrency, pages that never bind entities, nightly backups pulled
  to the Mac, encrypted off-site copies, email alerts and a heartbeat, telemetry to Grafana, tailnet only.
- **Public repository.** Tests and docs use made-up children, presents and amounts. The real workbook and the
  children's names stay out of the repository (`notes/presents/` in the private notes, and the app's database).

## The workbook

A tab a year, named by the year. Each has a list of purchases in columns A-E, one row each, and a pivot beside it:

| Column | What |
|---|---|
| Date | When it was bought. Sometimes months before Christmas (birthday-time bargains), sometimes blank or text |
| Shop | Where. Sometimes blank |
| Item | What |
| Who | Which child |
| Amount | What it counts as, often a formula |

The pivot sums Amount by Who (and, in later years, counts the items); a formula beside it gives the difference
between the children.

**Quirks:**
- **Splits are typed as formulas:** `=price/4` for something shared both with my mum and with the child's birthday,
  `/2` for one of them. Quantities (`=9.99*2`) and discounts (`=21.5-2.15`) are formulas too.
- **Cash is a row** (Item `Cash`, typed in after Christmas) that makes the totals equal. Some years only the child
  with less had cash, to match the other; in others there was a budget per child and both had cash to reach it. The
  earliest years have no cash rows, or not enough to balance.
- **The year is the tab, not the date:** a present bought in the summer still belongs to that Christmas.
- One year has a second, separate list of presents for other people (grandparents, friends), with its own workings.
  It's out of scope and the import ignores it.

## Data model

| Entity | What |
|---|---|
| `Child` | The user-owned root (`IUserOwned`, per-user query filter): name and display order |
| `Christmas` | The user-owned year: the year (unique per user) and an optional **budget per child** (`decimal(10,2)`) |
| `Purchase` | Belongs to a Christmas and a child: **item**; optional **shop** and **date**; **price** (`decimal(10,2)`, the full price of one); **quantity** (whole number, default 1); **split with Mum** and **split with birthday** (both `bool`) |

Every table inherits `AuditedEntity`. A child with purchases can't be deleted (FK `Restrict`); deleting a Christmas
deletes its purchases, after a confirmation that says how many.

**Derived, never stored** (a calculation service with unit tests):

- **A purchase's share:** price × quantity, halved for each split ticked (so ÷ 2 for one, ÷ 4 for both), rounded to
  the penny. The list shows the share, and the full price beside it when it's split.
- **Spent:** the sum of a child's shares that Christmas. **Presents:** how many purchases (quantity counts).
- **Target:** the most any child has spent, or the budget if there is one and it's more.
- **Cash:** target − spent, for each child (none for whoever spent the most, unless there's a budget above it).
- **Total:** spent + cash: the same for every child.
- **Over budget:** if a child has spent more than the budget, the target is their spend (so everyone still balances)
  and the summary says so.

The cash works for any number of children, though there are two.

## Pages

- **Christmas** (the home page, this year by default): a card per child with spent, presents, cash and total, and the
  budget if set; the add-purchase form (child, item, shop, date defaulting to today, price, quantity and the two split
  boxes, with the share shown as you type); the purchases, newest first, with edit and delete. A picker to move
  between years, and to start a new one (with the budget, optional).
- **History:** a table of every year: each child's spent, cash and total, and the year's total.
- **Children:** add, rename, reorder; delete while they have no purchases.
- **Import.**

## Import

Upload the `.xlsx` (ClosedXML), preview, then import, as Utilities' import does. Each tab named as a year is a
Christmas; on it, the headings Date, Shop, Item, Who and Amount (found in the first rows) mark the purchase list, and
only those columns are read. Who names the child, made on the first import if they don't exist.

- **Amounts** come in as Excel saved them (`CachedValue`), as the price, quantity 1 and no splits: a `/4` doesn't
  say which split it was, and the share is what matters for past years.
- **Cash rows** (Item `Cash`) are skipped, as the app works cash out. Where every child had a cash row and the totals
  are equal, that year's budget is set to the total, so its cash comes out the same.
- **Dates** that aren't dates are left empty; rows without an item, child or number are skipped with the reason.
- **The preview** shows each year's purchases and summary, and compares the app's cash with the sheet's cash rows.
  The earliest years didn't balance, so the app will show the cash that would have balanced them.
- A year that already exists is left alone. It's all one `SaveChanges`; log counts only.

## Hosting

| Thing | Value |
|---|---|
| Code | `src/Presents/GreenHedges.HomeServer.Presents`, `tests/Presents/…Tests` |
| Docs, deploy | `docs/presents/`, `deploy/presents/` (systemd unit, deploy script, database setup, host setup) |
| Service | systemd `presents` (user `presents`, in `sqlbackups`), `127.0.0.1:5083` |
| Address | `https://presents.<home domain>` through Caddy (a new block in `deploy/caddy/Caddyfile`) |
| Database | `Presents` on the shared SQL Server, login `presents` (owner) |
| Folders | `/opt/presents`, `/var/lib/presents` (keys, backups, alert state), `/etc/presents/env` |
| Backups | Nightly SQL Server backup at 04:15, `*.bak.gz` in `/var/lib/presents/backups`, 7 kept |
| Off-site | `age`-encrypted copies to the shared bucket under `presents/`, by a new write-only IAM user in `deploy/aws/offsite.yaml` |
| Mac | Daily pull to `~/Backups/Presents` (a `presents` case in `deploy/mac/pull-backups.sh` and `install-backup-pull.sh`) |
| Monitoring | healthchecks.io heartbeat; Resend alerts |
| Telemetry | OpenTelemetry over OTLP to the server's Grafana, as the other web apps (`service_name` `Presents`); never log names, items or amounts |

## Build order

One branch and PR each:

**Progress:** step 1 done. Next: step 2, purchases.

1. **Skeleton.** The project on `Common.Web` (sign-in, `/health`, backups, alerts, heartbeat and telemetry, all off
   until configured), the data model and first migration, the test project, the Children page.
2. **Purchases.** The Christmas page: years, the add form with the splits, the list, edit and delete.
3. **Summary.** The calculation service (shares, spent, cash, totals), the cards on the Christmas page, History.
4. **Import** of the workbook, with the preview.
5. **Deploy.** Deploy files, Caddy block, database setup, off-site user, Mac pull, host setup; the server's notes
   updated.

## Open questions

- **Who signs in:** just me (as above), or both of us sharing the data? Sharing needs a household both logins belong
  to, which none of the other apps has yet.
- **Budgets:** keep the optional budget per year, or only ever balance to the child who had the most?
- **Presents for other people** (the one year's second list): out of scope, as above?
