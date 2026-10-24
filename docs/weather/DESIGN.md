# WeatherCollector: design notes

How the Weather collector (WeatherCollector) works and why: the decisions, what was learned about the sensor and rtl_433, and the phases. Part of [GreenHedges.HomeServer](../../README.md); designed in the earlier `doowruc/WeatherCollector` repository and moved here before any code was written.

## Goals and constraints

**Goals, in priority order:**
1. **Archive:** a complete, permanent record of the outdoor sensor's readings, as received off the air, kept forever.
2. **Dashboards:** Grafana, local, reached over the tailnet.
3. **History:** import what the station uploaded to Weather Underground and Weathercloud before the collector existed.

**Constraints:**
- **Leave the station alone.** The console keeps receiving the sensor and uploading to its cloud services. WeatherCollector only listens.
- **Raw first.** Store each decoded message exactly as rtl_433 produced it. Every figure shown later (rain per hour, wind roses, daily highs) is derived and can be rebuilt.
- **Same conventions as the [Libre collector](../libre/DESIGN.md)**, sharing `GreenHedges.HomeServer.Common` with it: .NET Worker Service under systemd, SQLite, nightly backups pulled off the host, encrypted off-site copies, email alerts and an external heartbeat, Tailscale only.

## Architecture

```
Outdoor sensor ──868 MHz──► Station console ──► Weather Underground / Weathercloud (unchanged)
      │
      └──868 MHz──► RTL-SDR dongle (USB) ──► rtl_433 -F json (child process)
                                                 │  one JSON line per decoded message
                                                 ▼
                                       WeatherCollector (worker)
                                                 │
                        ┌────────────────────────┼─────────────────────────┐
                        ▼                        ▼                         ▼
                 SQLite archive           Derived summaries         Alerts + heartbeat
          (every message, as received;    (1-minute rollups,        (Resend email,
           source of truth, kept forever;  rain increments;          healthchecks.io)
           nightly backups + off-site)     SQL Server)
                                                 │
                                                 ▼
                              Grafana (Docker, 127.0.0.1) ◄── Caddy (tailnet HTTPS)
```

| Topic | Decision |
|---|---|
| Runtime | .NET 10 Worker Service, self-contained `linux-x64` publish; systemd `Type=notify`; Serilog to the journal. |
| Receiving | `rtl_433` from the Ubuntu package, run by the worker as a **child process** reading JSON lines from stdout. One service and one account; no MQTT broker. (MQTT can be added later, e.g. for Home Assistant.) |
| What is archived | **Every decoded message from every device**, including neighbours' sensors, with the raw JSON line. "My station" is a configured device id, with a history of id changes. |
| Archive | **SQLite** (WAL), the single source of truth; insert-only. |
| Dashboards | **Grafana** in Docker on `127.0.0.1:3000`, published on the tailnet by Caddy as `https://grafana.<home domain>` (a Weather folder of dashboards; other dashboards may join it). Reads the derived data from **SQL Server** (the server's, shared with the web apps) through Grafana's built-in Microsoft SQL Server data source; datasource and dashboards provisioned from this repository. |
| Remote access | Nothing exposed to the internet or the LAN; Tailscale only. |

## The sensor and rtl_433

**Station:** Bresser 5-in-1 Wi-Fi colour station (art. no. 7002580): outdoor sensor with temperature, humidity, wind speed, gust, wind direction and rain; the console adds indoor readings and pressure.

**Decoder:** rtl_433 protocol **172**, model `Bresser-6in1` (rtl_433's "6-in-1, 7-in-1 indoor, soil, **new 5-in-1** …"). The decoder's source notes name art. no. 7002580. The older protocol 119 (`Bresser-5in1`) is for earlier 5-in-1 sensors and doesn't match. Confirmed by a live capture.

**Radio:** **868.3 MHz** FSK (EU/UK version; rtl_433 defaults to 433.92 MHz, so `-f 868.3M`). Sample rate `-s 1024k`. A stock RTL-SDR antenna indoors received it with an SNR of 21–23 dB.

**Messages:** one every **12 s**. Wind is in every message; temperature/humidity and rain **alternate**, so each arrives every 24 s. About 7,200 messages a day, 2.6 million a year.

| Field | Meaning | Notes |
|---|---|---|
| `model`, `protocol` | `Bresser-6in1`, 172 | |
| `id` | Sensor id (integer in JSON) | Probably changes when the batteries are changed (common for such sensors; to be confirmed) |
| `channel`, `sensor_type` | 0, 1 (weather station) | |
| `flags` | 0 = temperature/humidity message, 1 = rain message | |
| `temperature_C`, `humidity` | °C (0.1), % | `flags` 0 only |
| `battery_ok` | 1 = OK, 0 = low | `flags` 0 only. **Reliable:** observed 0 while the console showed low battery |
| `wind_avg_m_s`, `wind_max_m_s` | Average and gust, m/s (0.1) | Every message |
| `wind_dir_deg` | Degrees | 16 points, 22.5° steps, truncated (112 = ESE 112.5°) |
| `rain_mm` | Running total since the sensor's power-on, mm (0.1) | `flags` 1 only. Steps of 0.4 mm (observed) |
| `uvi` | Always 0.000 | This model has no UV sensor; stored but ignored |
| `startup` | Observed 1 | Meaning unclear; to be watched across a battery change |
| `mic` | `CRC` | Messages failing the CRC are dropped by rtl_433 |
| `freq1`, `freq2`, `rssi`, `snr`, `noise` | Signal metadata from `-M level` | Useful for reception monitoring |
| `time` | rtl_433's timestamp | Local time **without a zone** by default; see below |

**Rain counter:** the decoder reads 6 BCD digits (rollover at 99,999.9 mm). **Observed: it does not wrap at 999.9 mm** as the older 5-in-1 sensors do: on 2026-10-18 it went 999.6 → 1000.0 → 1005.6 mm. So in practice it never wraps (about 100 years of UK rain). **The total resets to 0 when the batteries are changed** (to be confirmed). So rain per period is always computed from differences between consecutive totals, with resets and possible rollover detected (see Derived data).

**Not on the radio:** barometric pressure and indoor temperature/humidity are measured inside the console, which doesn't transmit them. See Open questions.

### rtl_433 invocation

```
rtl_433 -f 868.3M -s 1024k -R 172 -M time:unix:usec -M level -M protocol -F json
```

- `-R 172` limits decoding to the station's protocol. Archiving every decoded device means "every device using protocol 172"; widening to all default decoders (no `-R`) costs CPU and archives many false positives at 868 MHz. Revisit if other sensors are wanted.
- `-M time:unix:usec` gives an unambiguous epoch time; the worker also stamps its own UTC receive time, which is the archive's key time.
- No `-M bits` (the demodulated bits, for decoding again later): every message passes the CRC and the decoder is right for this model, so the decoded JSON is enough. It can be added through the `Rtl433:Arguments` setting without a code change.

## Collection

- The worker starts rtl_433, reads stdout line by line and writes each line to the archive **before** any other processing. Lines that aren't valid JSON are archived too, flagged, and logged.
- **Supervision:** if rtl_433 exits, restart it with backoff. If **no line at all** arrives for 2 minutes (rtl_433 can hang after a USB glitch), kill and restart it. stderr goes to the journal.
- **Startup:** wait for the USB dongle; a missing dongle is an alert, not a crash loop.
- **Station id:** configured (`Station:DeviceIds`, a list: after a battery change the new id is added and the old one stays the station's, so its history stays attached). When messages from the configured model stop and a **new id** of the same model appears with `startup`/rain-reset behaviour, raise an alert ("sensor id changed from A to B") rather than switching automatically, at least until a real battery change has been observed. Then decide whether to follow it automatically.

## Archive (SQLite)

Versioned migrations (`PRAGMA user_version`) at startup, WAL mode and online backups, through `Common.Sqlite` (shared with the Libre collector). `messages` is insert-only.

**`messages`** (every decoded line):

| Column | |
|---|---|
| `id` | `INTEGER PRIMARY KEY` (explicit, so `VACUUM` can't renumber) |
| `received_at_utc` | Worker's clock, fixed-width ISO UTC with milliseconds |
| `rtl_time` | rtl_433's `time`, as received |
| `protocol`, `model`, `device_id`, `channel` | Extracted for indexing; `device_id` and `channel` as text (models differ) |
| `is_valid_json` | 0 for a line that isn't a JSON object (kept anyway) |
| `raw_json` | The line exactly as rtl_433 wrote it |

Index on `(model, device_id, received_at_utc)`.

**`station_readings`** (typed, for the station's device ids only; written in the same transaction): `message_id`, `received_at_utc`, `device_id`, `temperature_c`, `humidity`, `wind_avg_ms`, `wind_max_ms`, `wind_dir_deg`, `rain_total_mm`, `battery_ok`, `flags`, `startup`, `rssi`, `snr`, all nullable. Rebuildable from `messages`.

**`devices`:** each `(model, device_id)` seen, with first and last seen, message count, and whether it's the station.

**Size:** a JSON line is about 450 bytes, so about 3 MB a day and **about 1.2 GB a year** with the typed tables. Disk is no problem; backups grow with it (gzip compresses JSON well). Revisit (e.g. compress `raw_json`, or weekly off-site copies) if backups become unwieldy.

**Duplicates:** rtl_433 may emit the same transmission twice if the sensor repeats it. Everything is archived; derived data ignores a message identical to the previous one from the same device within 2 s.

## Derived data

Published by the worker to its own database, `Weather`, on the server's SQL Server (see Grafana), every minute, incrementally: a watermark by `messages.id` (as the Libre collector's Nightscout publisher does), **kept in SQL Server** with the data, so an empty database is refilled from the start of the archive with no separate rebuild step. Each batch re-summarises whole minutes from its first new reading's minute, so a minute still filling at the last run is completed; the minutes and the watermark are written in one transaction. Changing the calculation bumps the watermark's name (`minutes-v1`), which recalculates every minute once. The calculation (`MinuteSummariser`) is pure code, unit-tested; the database needs no backup.

- **`MinuteSummaries`** per UTC minute (the station's readings only): message count (5 for a complete minute: completeness), temperature avg/min/max, humidity avg, wind average, gust max, wind direction as the **vector mean weighted by speed** (null when calm), rain increment (null when no rain message in the minute), battery (false if any message in the minute was low), RSSI and SNR averages.
- **Duplicates:** a message identical to the previous one from the same device within 2 s (signal fields aside) is skipped.
- **Rain increments:** for consecutive rain totals `prev` → `cur` from the station:
  - `cur ≥ prev`: increment `cur − prev`.
  - `cur < prev`: a **reset** (battery change): increment `cur`. Logged as a warning. (A rollover would only happen at 99,999.9 mm; see Rain counter.)
  - A gap (service down) just puts the whole difference in the first minute after it.
  - The very first total, with none before it, is only a baseline (0 mm).
- Higher rollups (hour, day) are Grafana queries over minutes, or views.

## Grafana

- Docker compose in `/opt/grafana` (pinned version), `127.0.0.1:3000`, published by Caddy as `grafana.<home domain>` (a block in `deploy/caddy/Caddyfile`). Anonymous **Viewer** access (the tailnet is the access boundary, as for Nightscout); an admin account for editing.
- **Data: SQL Server** (decided 2026-10-24). The worker writes the derived tables to its own database on the server's SQL Server (`deploy/sqlserver/`), and Grafana reads them with its built-in Microsoft SQL Server data source and a read-only login. This replaces the original plan of the community SQLite plugin reading the archive directly, which needed a spike on sharing a WAL database with another process. The SQLite archive stays the single source of truth; the SQL Server copy is derived and can be rebuilt from it. About 0.5 million minute rows a year, far inside Express's 10 GB.
- Dashboards are provisioned from this repository (JSON), so they're versioned. No community dashboard exists for this data; build: current conditions, temperature/humidity, wind (speed, gust, rose), rain (hour/day/month), battery and signal.

## Backups and monitoring

Shared with the Libre collector through `Common`: nightly SQLite backup API → gzip, 7 kept, pulled by another machine over an `rrsync -ro` key; `age`-encrypted off-site copies to S3 with a PutObject-only IAM user; Resend email alerts (PROBLEM/RESOLVED, persisted state, resolve only on positive evidence); healthchecks.io heartbeat while station messages arrive.

| Alert | Raised when |
|---|---|
| No station messages | Nothing from the station's ids for 5 minutes (from startup, so a restart gets a grace period) |
| Receiver problem | rtl_433 reports no usable dongle, or 3 rtl_433 runs ended (exit, silence, failed start) in the last hour |
| Station id changed | The station's ids silent for 5 minutes while an id of the same model is heard that was first heard after them (so a neighbour heard all along doesn't count) |
| Sensor battery low | `battery_ok` = 0 for an hour (only the temperature messages carry it) |
| Summaries for Grafana stalled | Publishing minute summaries to SQL Server failing for 30 minutes |
| Weather Underground history failing | Weather Underground history runs failing for 6 hours (retried hourly) |
| Backups failing / off-site failing | No good copy for 36 hours |

Thresholds are `Alerts:StationSilentAfter`, `Alerts:ReceiverEndsPerHour`, `Alerts:BatteryLowAfter`, `Alerts:PublishStalledAfter` and `Alerts:WundergroundFailingAfter`. An alert resolves only on positive evidence since startup: a station message from a configured id, rtl_433 writing lines with no missing dongle and fewer restarts, `battery_ok` = 1. While a receiver or station-id alert is open, "No station messages" isn't raised as well.

## History import

The station's own uploads, from before the collector existed (and, for pressure, from now on), decided 2026-10-24.
Both sources go into the **SQLite archive** first, as received, so they're backed up and copied off-site with the radio
data (once the services stop holding them, ours is the only copy); from there they're published to SQL Server's
`HistoryObservations` (one shape for both; each row an interval ending at `TimeUtc`, with pressure and, from
Weathercloud, the indoor readings the radio never carries). The dashboards merge it with the minute summaries: the radio
first, then Weather Underground, then Weathercloud.

- **Weather Underground** (PWS history API, `api.weather.com/v2/pws/history/all`, one local day per call, the station
  owner's API key, metric units): 5-minute intervals with high/low/average temperature, humidity, wind, gust, dew point,
  pressure range and the day's running rain total. History from 2021-11-08. Archive schema v2: `wunderground_days` (each
  day's response as received, and when it was fetched) and `wunderground_observations` (typed, rebuildable from them).
  - **Fetched by the worker itself**, not a one-off: a run a few minutes after startup and every night at 04:30, of
    every day from `Wunderground:From` to yesterday not yet fetched in full, newest first. A day is fetched again until
    a fetch happens 24 hours after it ended (so late uploads are caught: yesterday is fetched twice). The first runs
    fill the backlog (about 1,800 days) within two nights.
  - **Rate limits** (owner keys: 1,500 calls a day, 30 a minute): at most `MaxCallsPerDay` (1,000), counting the days
    already fetched in the last 24 hours, so restarts can't exceed it; 2.5 s between calls; a 429 stops the run.
  - Publishing: each day whole (a re-fetched day replaces its rows), km/h to m/s, pressure as the middle of the
    interval's range, rain per interval from the day's running total (which restarts at local midnight).
  - `--fetch-wunderground [yyyy-MM-dd]` fetches one day and prints it without storing: a check of the id and key.
  - Alert "Weather Underground history failing" after 6 hours of failed runs (retried hourly).
- **Weathercloud** CSV exports (Phase 3b): one file per month (a year comes as a zip of months), UTF-16LE without a
  byte-order mark, `;`-separated, local time `dd/MM/yyyy HH:mm:ss` (the header names the zone), 10-minute rows,
  pressure with a thousands comma, rain as the local day's running total. The 2021 files have short headers
  (`temp (°C)`), 2026's long ones (`Temperature (°C)`), same columns in the same order. First reading 2021-11-07 13:20.
  The autumn repeated hour needs the same care as the Libre collector's LibreView import.

## Host setup (summary)

- Packages: `rtl-433`, `rtl-sdr` (Ubuntu universe). Check that the DVB-T kernel driver doesn't claim the dongle (blacklist `dvb_usb_rtl28xxu` if it does) and that the udev rules give the service user access (group `plugdev`).
- Service account `weathercollector` (in `plugdev`), `/opt/weathercollector`, `/var/lib/weathercollector`, `/etc/weathercollector/env`.
- The dongle on a USB extension lead, placed for reception; each antenna element about 8 cm (a quarter wave at 868 MHz).

## Phases

0. **Reception test** ✅: protocol 172 confirmed on the host, strong signal.
   Then the move into GreenHedges.Collectors (since renamed GreenHedges.HomeServer) ✅: shared host setup, alerts, heartbeat, backups and off-site copies in `Common`.
1. **Archive:** projects `GreenHedges.HomeServer.Collectors.Weather.Rtl433` (process + parsing), `.Storage` and `.Worker`, with tests; collection and supervision, archive schema, deploy script and host setup, systemd unit (first deploy here, to be running before the sensor's battery change); then backups, off-site copies, alerts and heartbeat from `Common`. Deploy and leave running.
2. **Dashboards** ✅ (2026-10-24), in three PRs: (a) minute summaries published to SQL Server, with the `Weather` database's setup; (b) Grafana: compose, provisioning (data source, dashboard folder), a Caddy name; (c) the dashboards.
3. **History import:** (a) Weather Underground, nightly, with the backlog, published to SQL Server; (b) Weathercloud exports; (c) dashboards merging the history with the minute summaries, with pressure and indoor charts.

## Open questions

- **Pressure and indoor readings** aren't on the radio. Decided 2026-10-24: pressure comes from Weather Underground's history, fetched nightly (History import); indoor readings from Weathercloud's exports. A sensor of our own (e.g. a Bresser 868 MHz indoor sensor that rtl_433 decodes, or an I²C pressure sensor) stays an option for live values.
- What `startup` means, whether the id changes on a battery change, and whether the rain total resets: observe at the next battery change and update this document. (Rain doesn't wrap at 999.9: answered 2026-10-18.)
