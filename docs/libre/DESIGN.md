# LibreCollector: design notes

How LibreCollector works and why: the decisions, what was learned about the LibreLinkUp API and LibreView exports, and the backlog. For setup and use, see the [README](README.md); for host preparation, [deploy/libre/HOST-SETUP.md](../../deploy/libre/HOST-SETUP.md).

## Goals and constraints

**Goals, in priority order:**
1. **Archive:** a complete, permanent record that the user owns, kept forever.
2. **Dashboards:** Nightscout reports.
3. **Apple Health:** via an existing app where possible.

**Constraints:**
- **Stay on Abbott's infrastructure.** Clinics monitor through LibreView, so the official LibreLink app must stay in charge of the sensor. Apps that connect to the sensor directly (xDrip4iOS, Juggluco) are ruled out.
- **iOS can't be the collector.** An app pulling LibreLinkUp into Apple Health can't run reliably in the background, and the 15-minute `/graph` history only covers about 12 hours, so gaps are unavoidable. Hence an **always-on server** polling LibreLinkUp as a **follower**.

## Architecture

```
LibreLinkUp (follower) ──► Collector ──► SQLite archive (source of truth)
                                              │
                    ┌─────────────────────────┼──────────────────────────┐
                    ▼                         ▼                          ▼
          Nightscout publisher         Nightly backups          Alerts + heartbeat
          (same process)               (+ off-machine pull)     (Resend email, healthchecks.io)
                    │
          Nightscout + MongoDB (Docker, localhost only) ◄── Caddy (tailnet HTTPS)
                    │
          Nightguard (iPhone) ──► Apple Health
```

| Topic | Decision |
|---|---|
| Runtime | .NET 10 Worker Service, self-contained publish (no .NET on the host); systemd (`Type=notify`). Windows Service support is kept in the host builder. |
| Projects | `LibreLinkUp` (API client), `Storage` (SQLite), `Nightscout` (publisher), `LibreView` (CSV import), `Worker` (host, polling, alerts, backups, CLI modes), each with tests. |
| Archive | **SQLite**, the single source of truth; everything downstream is derived from it and can be rebuilt. Kept forever (about 500k rows a year). |
| Account | A **dedicated LibreLinkUp follower**, used only by the service. |
| Remote access | Nothing exposed to the internet; Tailscale only. |
| Alerts | Email through Resend, plus an external heartbeat for "host silent". |

## LibreLinkUp API

The API is unofficial; there is no Abbott documentation. Reference capture: [khskekec's gist](https://gist.github.com/khskekec/6c13ba01b10d3018d816706a32ae8ab2).

**Client behaviour:**
- Login is `POST /llu/auth/login`. Handle `redirect: true` + `region` for **any** region by building `https://api-{region}.libreview.io` (a hard-coded region list caused an infinite loop in an earlier project). Region codes are validated (`^[a-zA-Z0-9]{1,10}$`) so a response can't redirect to an arbitrary host.
- Headers: `product: llu.android`, a **configurable** `version`, and, on authenticated calls, `account-id` = SHA-256 hex of the user id.
- Reuse the token until shortly before `authTicket.expires`, then log in again. A single retry after a 401.
- Typed errors: bad credentials (status 2), login step (status 4), version too old (status 920), rate limited (HTTP or status 429). Errors that need the user to act pause collection for 30 minutes (no tight login loops, so no account lockouts) and raise an alert.

**Version and login findings (October 2026; community sources: [nightscout-clock#236](https://github.com/ktomy/nightscout-clock/pull/236), [nightscout-connect#73](https://github.com/nightscout/nightscout-connect/pull/73), [#53](https://github.com/nightscout/nightscout-connect/pull/53), [librelink-mcp-server](https://github.com/ValeriiVasyliev/librelink-mcp-server)):**
- A **minimum version is enforced:** HTTP 403 with `{"status": 920, "data": {"minimumVersion": "4.16.0"}}`. The alert includes the minimum, so the fix is a config change.
- The **v5 identity** (`llu.android` / `5.0.1`, user-agent `LibreLinkUp/5.0.1`) went live server-side in early October 2026 with the same plain-JSON responses. It's the default; 4.16.0 still worked at the time.
- Reports said v5 tokens last about 1 hour, but a live UK account got about **180 days**, and **no region redirect**. Expiry-driven refresh handles both.
- **Login steps:** `status: 4` with `data.step.type`, e.g. `tou` (re-accept the Terms of Use), completed with `POST /auth/continue/{type}` using the short-lived token. **Decision: don't auto-accept.** Alert instead; the user signs in to the LibreLinkUp app once.
- A lockout looks like `{"status": 429, "data": {"code": 60}}`; honour `Retry-After`.

**Data observed from a live account:**
- `FactoryTimestamp` is **UTC**; `Timestamp` is device local time with no zone. Format `M/d/yyyy h:mm:ss tt` regardless of locale. **Key on `FactoryTimestamp`.**
- Current reading: `type: 1`, with `TrendArrow` and `TrendMessage` (null). Graph points: `type: 0`, **no `TrendArrow`**, 15-minute spacing, about 12 hours.
- **TrendArrow 0–5:** 1 falling fast … 3 flat … 5 rising fast (as in nightscout-connect). **0 = no trend**, seen together with `isLow: true` at `ValueInMgPerDl` 40: the Libre **"LO"** state (true value below 40 mg/dL; 40 is the reported floor; `MeasurementColor` 4).
- **Sensor object** on each connection: `{"deviceId":"","sn":"<serial>","a":<activation, Unix seconds>,"w":60,"pt":3,"s":true,"lj":false}`. `w` = warm-up minutes, `pt` = 3 for a Libre 2 Plus (a Libre 3 capture showed 4); `s` and `lj` are unknown flags. LibreLink itself only shows whole days remaining (rounded up).

## Data collection

- **`/llu/connections` once a minute**, storing the current measurement; **`/graph` at startup and every 30 minutes**, storing 15-minute history (fills gaps if the service was down for less than about 12 hours).
- **Polls are aligned to the sensor's clock:** the next poll is `PollOffset` (35 s) after the latest reading's sensor time, stepped by a minute and never sooner than half a minute away. Readings appear a few seconds after their sensor time and are replaced a minute later, so polling halfway leaves slack both ways. A fixed phase can race the upload and skip readings: polling at :27 with readings stamped :17–:20 missed some. The schedule follows the sensor's drift and re-aligns automatically after a sensor change.
- **What "complete" means:** the collector captures every reading LibreLinkUp makes available. Phones (iOS especially, overnight) sometimes upload late or in batches; LibreLinkUp's "current" then jumps straight to the newest, so the skipped minutes never appear to followers. They still reach LibreView, and graph backfill covers them at 15-minute resolution. In testing, each collected reading arrived 33–37 s after its sensor time, and every gap traced back to the phone.
- **Sensor tracking:** each poll records the connection's sensor (serial, activation, warm-up, product type, raw JSON). Startup logs the current sensor's expected end (activation + 15 days); a swap logs `Sensor changed: old -> new …`. Recording failures never stop collection.
- **Which sensor a reading came from:** current readings are tagged with the sensor in use at the reading's own time: the current sensor, or the previous one if the reading is older than the current sensor's activation (null if that isn't known). The collector remembers the last two sensors, also across restarts. On a real swap, the old sensor's last reading arrived in the same poll that first reported the new sensor; before this rule it was tagged with the new serial. Graph points aren't tagged.
- **What a swap looks like** (Libre 2 Plus, observed): the new serial and activation (`a`) appear on the first poll after activation. The sensor object stays present throughout warm-up while `current` keeps the old sensor's last reading. `w` is the warm-up in minutes (60): the first new reading came 40 s after `a` + `w`. `/graph` has nothing for the warm-up and showed the new sensor's points only about an hour later. The new sensor's readings fall at a different second past the minute, and aligned polling follows within one poll.

## Archive (SQLite)

Versioned migrations (`PRAGMA user_version`) run at startup, before any hosted service touches the database. WAL mode.

| Version | Change |
|---|---|
| v1 | `readings`, keyed on `(timestamp_utc, source)`, with the raw JSON |
| v2 | Explicit `id INTEGER PRIMARY KEY` (keeping rowids; an implicit rowid can be renumbered by `VACUUM`); `publish_state` watermarks |
| v3 | `sensors` table; `readings.sensor_serial` |
| v4 | Sources widened to `libreview` / `libreview_scan`; `events` table (insulin, carbs, notes from imports) |

`readings`: `id`, `timestamp_utc` (fixed-width ISO UTC, sorts in time order), `source` (`current`, `graph`, `libreview`, `libreview_scan`), `timestamp_local` (as received), `value_mgdl`, `trend_arrow`, `measurement_color`, `is_high`, `is_low`, `received_at_utc`, `raw_json`, `sensor_serial`. Rows are insert-or-ignore and never updated or deleted, so ids increase in insertion order.

**Backups:** nightly at 03:30 local via the SQLite backup API (consistent while running), switched to rollback-journal mode, `integrity_check`ed, gzipped; 7 kept; also taken at startup if none exists from the last day. "Backups failing" alerts after 36 hours. Off-machine copies are **pulled** by another machine with a key restricted to `rrsync -ro` on the backups folder, so the server holds no credentials for anything else.

**Off-site (cloud) copies:** `OffsiteBackupService` checks hourly; when a newer local backup exists than the last one copied (tracked in a `.offsite-last` marker, since the credentials can't list the bucket), it encrypts it with **`age`** to a public-key recipient and uploads it with the AWS SDK (`PutObject` only). The server can't decrypt its own uploads; the private key lives in a password manager. The IAM user is write-only on one prefix; bucket versioning protects against overwrites, and a lifecycle rule moves copies to Glacier Deep Archive after 30 days. S3-compatible providers work through `ServiceUrl` (path-style addressing, checksums only when required). "Off-site backup failing" alerts after 36 hours without a good copy.

## Alerts

| Alert | Raised when | Resolved when (evidence since startup) |
|---|---|---|
| No new glucose readings | Newest reading older than 20 min (timed from startup at the earliest). If a sensor was activated after the newest reading, timed from the end of its warm-up instead (activation + `w`, or `Collector:SensorWarmup` if missing), so a normal swap raises nothing; the email then says when readings were expected. After the sensor's expected end, the email says it has probably expired | A reading newer than 20 min |
| LibreLinkUp needs attention | An error that needs the user (credentials, terms, version, no connection); suppresses the stale alert | A successful poll |
| Backups failing | No good backup for 36 h | A backup within 36 h |
| Nightscout publishing stalled | Publishing failing for 30 min | A successful publish |

One PROBLEM email per incident and one RESOLVED email; failed sends are retried each minute. **Open alerts persist** in `alert-state.json` (atomic writes; a missing or corrupt file means none open), so restarts neither repeat a PROBLEM nor lose a RESOLVED, and durations count from the original start. Because health state is empty after a restart, alerts resolve **only on positive evidence**, never because "no error has been seen yet".

An **external heartbeat** (healthchecks.io) is pinged every 5 minutes while polls succeed, covering what the service can't report itself: host off, offline, or waiting at a disk-encryption prompt.

## Nightscout

**Stack** (`deploy/libre/nightscout/compose.yaml`): Nightscout and MongoDB with **pinned** versions (`nightscout/cgm-remote-monitor:15.0.8`, `mongo:5.0.32`, as in Nightscout's own example). Newer MongoDB versions are unconfirmed for Nightscout ([#8305](https://github.com/nightscout/cgm-remote-monitor/issues/8305)); 5.0 is past end of life, which is acceptable only because MongoDB sits on an **internal-only Docker network** with no published port. Nightscout is bound to **`127.0.0.1:1337`** (Docker-published ports bypass ufw), with HTTPS for tailnet devices via `tailscale serve` (certificate names appear in public CT logs; data doesn't). `INSECURE_USE_HTTP=true` because TLS ends at Tailscale, and `AUTH_DEFAULT_ROLES=readable` because the tailnet is the access boundary. Nightscout's own alarms are off. MongoDB holds only derived data, so it needs no backup.

**Publisher:**
- **Watermark by row id** (not timestamp, since backfill inserts old timestamps later), in `publish_state`, advanced only after a 2xx response. Batches of 500. Woken by the collector after each insert, with a 5-minute fallback; backs off when Nightscout is down; never affects collection.
- **Idempotent:** `/api/v1/entries` upserts on `sysTime + type` (`lib/server/entries.js`), so re-sending overwrites. Renaming the watermark target (`nightscout` → `nightscout-v2`) republishes everything once; that's how changes to the entry format reach past data.
- **Gap fill by source priority:** current > graph > libreview. A row is published only if no higher-priority reading lies within ±2 minutes. Scans are never published.
- **Entry mapping:** `type: "sgv"`, `sgv` in mg/dL (Nightscout always stores mg/dL), `date` = sensor UTC epoch ms, `dateString` ISO UTC, `direction` from TrendArrow (`NOT COMPUTABLE` when absent), `device` `LibreCollector` (imported history: `LibreCollector (LibreView)`). Auth: `api-secret` = SHA-1 hex of `API_SECRET`.
- **LO/HI:** Nightscout treats sgv < 39 as error codes (hidden, excluded), **39 as "LOW"**, and **> 400 as "HIGH"** (`lib/client/index.js`, `lib/plugins/bgnow.js`, reports). So Libre LO (`is_low`) is published as **39** and HI (`is_high`) as **≥ 401**. The words LOW/HIGH appear only in the big current-reading display; chart tooltips always show the number (`renderer.js`), so LO still reads 2.2 mmol/L there. Genuine 401–500 mg/dL readings also display "HIGH" (a Nightscout design limit), though they're plotted and counted correctly.
- **Gotcha:** the entries API limits queries **without a date filter to the last 4 days** (`lib/server/query.js`, `deltaAgo`); add `find[date][$gte]=<ms>` when querying history.

## Apple Health

Health no longer has to be the complete record: Nightscout holds the archive, so any app that syncs "everything since last time" when it runs makes Health **eventually complete**.

| Option | Verdict |
|---|---|
| **Nightguard** (App Store, free, [source](https://github.com/nightscout/nightguard), AGPL-3.0) | **Used.** Optional HealthKit sync with a last-synced bookmark; backfills from Nightscout up to 10,000 readings (about a week at 1/min); first sync starts from the beginning; syncs **only while the app is open**. |
| Sweet Dreams | Fallback; Health export is a Premium feature. |
| xDrip4iOS (follower) / custom app | Need a paid Apple Developer account (£79/yr); free signing must drop HealthKit and re-sign every 7 days. |

Nightguard caveats (from `AppleHealthService.swift`): it advances its bookmark **before** HealthKit confirms the save, and sets no sync identifiers. Hence: open it at least twice a week (inside the backfill limit), **never automate opening it** (Health writes fail while the phone is locked, which would skip readings permanently), and expect duplicates if its settings are reset.

## LibreView export import

`GreenHedges.HomeServer.Collectors.Libre.Worker --import-libreview <file.csv> [--time-zone <IANA>] [--date-format <format>]`. Idempotent, batched, safe while the service runs.

**Export format (LibreView "Download glucose data"):**
- Line 1: title/metadata, which includes the account holder's name (never logged or stored). Line 2: 21 headers.
- `Device Timestamp`: **local time, `dd-MM-yyyy HH:mm`**, no seconds or zone.
- Values in mmol/L to 1 dp, or mg/dL (the unit is in the header names). No `LO`/`HI` text: lows appear as the floor (2.2 mmol/L).
- `Serial Number` identifies the LibreLink **app install**, not the sensor.
- Record types: **0** historic glucose (**15-minute**), **1** scan glucose (one per app open), **2** strip glucose, **4** insulin, **5** food/carbs, **6** notes (many type-6 rows carry nothing but a timestamp).
- **LibreView history is 15-minute only**, so it can't fill per-minute gaps; its value is the history from before the collector existed.

**Decisions:**
- Type 0 → `libreview`, type 1 → `libreview_scan`; mg/dL = mmol × 18 (approximate; raw row kept). `is_low`/`is_high` inferred from the floor (≤ 2.2) and ceiling (≥ 27.8) values.
- Other types → `events` (archive only; not sent to Nightscout). Empty rows are skipped and counted.
- Local → UTC with the given zone. In the **autumn clock change** the repeated hour appears twice, and the export **interleaves** the two copies, so those rows can't be placed reliably and are **skipped and counted** (about 8 a year). Spring-gap times are skipped too. Travel across time zones can't be detected from the file.
- Readings are insert-or-ignore on `(timestamp_utc, source)` (same-minute repeats collapse); events have a SHA-256 `dedup_key`.

## Hosting notes

- **Hardware:** anything always-on running Linux. MongoDB 5+ needs **AVX** (x86, roughly 2011+) or **ARMv8.2** (Raspberry Pi 5; the Pi 4 is ARMv8.0 and limited to unsupported MongoDB 4.4). An old laptop works well: its battery acts as a UPS (set a charge limit, ignore the lid, block sleep).
- **State to migrate:** the SQLite file, the config/secrets, and optionally the alert state. Nightscout can be rebuilt by republishing.
- **Disk encryption (LUKS):** worth having, but every boot waits for the passphrase. On Ubuntu 26.04 with **dracut**, there is no packaged SSH-unlock module (`dracut-crypt-ssh` / `dracut-sshd` aren't in the archive; `dropbear-initramfs` needs initramfs-tools, which conflicts with dracut). Options: unlock at the console (plus Livepatch to reduce reboots), TPM2 auto-unlock if the hardware has a TPM, or `clevis-dracut` with a Tang server on another home machine.

## Backlog

- [x] **Sensor change alerting.** Sensors recorded and changes logged (step 1); after a real swap, the stale alert is held through the new sensor's warm-up, still alerting if it overruns, and says when a sensor has probably expired (step 2). No separate "warming up" email: the person changing the sensor knows, and the journal logs it.
- [ ] **Windows build/deploy machine** (the server stays Linux). Choose WSL2 (the existing bash scripts, `rsync` and `ssh` work unchanged) and/or native PowerShell equivalents. Notes: Windows OpenSSH lacks `ControlMaster`; the backup pull needs Task Scheduler and an rsync client (or switch to an SFTP-only restricted key); docs use `ssh-copy-id` and `read -s`; add a `.gitattributes` forcing LF for scripts, hooks, units and YAML. Test on real Windows before documenting.
- [ ] **Fully automatic Apple Health sync:** a custom iOS app (paid developer account) with background refresh, a protected-data check *before* advancing its bookmark, and HealthKit sync identifiers. Only if the Nightguard routine becomes a nuisance.
- [ ] **Automatic LUKS unlock at home** (`clevis-dracut` + Tang), only if console unlocks become a nuisance.
- [ ] **Other off-site storage providers.** Off-site copies currently go to S3 or any S3-compatible service (Backblaze B2, Cloudflare R2, MinIO via `Offsite:ServiceUrl`). Make the destination pluggable (`IOffsiteStore`) for non-S3 options a user might already have: Google Drive, OneDrive or Dropbox (e.g. via `rclone`, with the caveat that OAuth tokens usually grant access to the whole drive and expire), or iCloud Drive (no Linux API; would have to run from a Mac). Keep encryption on the server and write-only credentials where the provider allows.

**Decided against:** auto-accepting LibreLinkUp terms (alert instead); filling per-minute gaps from LibreView (it's 15-minute data); sending insulin and carbs to Nightscout as treatments.
