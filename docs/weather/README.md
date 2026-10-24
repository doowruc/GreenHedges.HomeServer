# WeatherCollector

*Part of [GreenHedges.HomeServer](../../README.md). Code in `src/Collectors/Weather/`, deployment in `deploy/weather/`.*

An always-on service that keeps **your own complete, permanent archive** of a weather station's outdoor sensor, received off the air with an RTL-SDR dongle and [rtl_433](https://github.com/merbanan/rtl_433). It only listens: the station's console keeps working and uploading as before.

- Setting up a host: [deploy/weather/HOST-SETUP.md](../../deploy/weather/HOST-SETUP.md)
- How it works and why: [DESIGN.md](DESIGN.md)

---

## Day to day

Once it's running there's nothing to do. You'll hear from it only when something needs attention.

**Dashboards:** `https://grafana.<home domain>` → **Weather** (Grafana, [deploy/grafana/](../../deploy/grafana/HOST-SETUP.md)), from the minute summaries:

- **Weather:** now (temperature, today's min/max, humidity and dew point, wind and gust, direction, rain today and over 24 hours); over the selected range (temperature and humidity min/max, total rain, strongest wind and gust); then the last day's temperature with dew point, humidity, wind, direction, rain per hour, and how often the wind came from each of 16 directions.
- **Rain:** totals for the range, month and year; rain per UK day and per month; the running total; the wettest days and hours.
- **Station:** reception (messages received, per hour), signal (SNR, RSSI), battery, the last message, and gaps.

"Now" and "today" mean the end of the selected range, so the dashboards also work for past dates. Days are UK days. They're JSON files in `deploy/grafana/dashboards/weather/`: change them in a PR (Grafana won't save edits to them), and re-run `deploy/grafana/deploy-grafana.sh`.

### Emails you might get

Subjects start `[WeatherCollector]`. Every PROBLEM email is followed by one RESOLVED email, including across restarts (open alerts are kept in `/var/lib/weathercollector/alert-state.json`). An alert resolves only on real evidence, such as a new message from the station.

| Subject | Meaning | What to do |
|---|---|---|
| **No station messages** | Nothing from the station's ids for 5 minutes (it sends every 12 s) | Usually the sensor: batteries, or out of range. Not sent while a receiver or station-id alert already explains it. |
| **Receiver problem** | rtl_433 finds no usable RTL-SDR dongle, or has restarted 3 times in an hour | Check the dongle is plugged in (`lsusb \| grep 2838`) and the DVB-T driver isn't loaded ([HOST-SETUP §2](../../deploy/weather/HOST-SETUP.md)). rtl_433 is retried with a back-off. |
| **Station id changed** | The station's ids are silent and a new sensor of the same model is being heard, first heard after the station went quiet (a neighbour's sensor heard all along doesn't count) | After a battery change the sensor may pick a new id; the email names it. See [Battery changes](#battery-changes). |
| **Sensor battery low** | The sensor has reported `battery_ok` = 0 for an hour | Change its batteries. |
| **Backups failing** | No good backup for 36 hours | Check disk space and the log. |
| **Summaries for Grafana stalled** | Publishing minute summaries to SQL Server has failed for 30 minutes | Check SQL Server (`sudo docker compose -f /opt/sqlserver/compose.yaml ps`) and the log (`Publishing minute summaries failed`). Collection is unaffected, and the summaries catch up by themselves. |
| **Weather Underground history failing** | Fetching the station's history from Weather Underground has failed for 6 hours (it retries hourly) | Check the log (`Weather Underground`): a refused key (`Wunderground:ApiKey` / `StationId` in the env file) or the service being down. The radio archive is unaffected; missing days are fetched once it works. Test with `--fetch-wunderground` ([HOST-SETUP §13](../../deploy/weather/HOST-SETUP.md)). |
| **Off-site backup failing** | No off-site copy for 36 hours, or straight away if the off-site settings are wrong | Check the log (`Off-site copy … failed` or `Off-site backup is misconfigured`): AWS key, bucket, or `age` missing. Fix the env file and restart. Collection and local backups are unaffected. |
| healthchecks.io "down" | No ping for 5 minutes plus grace | The collector pings only while the station is being heard, so: host off or offline, collector stopped, or no reception. |

Thresholds are in the `Alerts` section of [`appsettings.json`](../../src/Collectors/Weather/GreenHedges.HomeServer.Collectors.Weather.Worker/appsettings.json) (`StationSilentAfter`, `ReceiverEndsPerHour`, `BatteryLowAfter`, `PublishStalledAfter`, `WundergroundFailingAfter`) and can be overridden in the env file. Without `Station:DeviceIds` the station alerts are off and the heartbeat stays silent. Without `ConnectionStrings:Weather` the summaries (and their alert) are off; without `Wunderground:StationId` and `Wunderground:ApiKey`, Weather Underground's history is.

### Useful commands (on the host)

```bash
systemctl status weathercollector                                  # running?
journalctl -u weathercollector -f                                  # live log
journalctl -u weathercollector --since today -p warning            # problems today
sqlite3 -readonly /var/lib/weathercollector/weathercollector.db \
  "SELECT COUNT(*), MAX(received_at_utc) FROM messages; SELECT * FROM devices;"
```

### Updating

```bash
git pull
deploy/weather/deploy.sh
```

Migrations run at startup, and a backup is taken at startup if none exists from the last day. Messages that arrive while the service restarts are lost (the sensor doesn't repeat them), so a deploy costs a message or two.

### Rebuilding the summaries

The `Weather` database on SQL Server holds only minute summaries derived from the archive. To rebuild them (e.g. after
a change to how they're calculated, which also bumps `SummaryPublisher.Target` so it happens by itself), empty it on
the server and the collector refills it within a few minutes:

```bash
sudo docker compose -f /opt/sqlserver/compose.yaml exec -T sqlserver sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d Weather -Q "DELETE dbo.MinuteSummaries; DELETE dbo.PublishState;"'
```

### Weather Underground history

The collector keeps a copy of the station's Weather Underground history in the archive (`wunderground_days`, as
received): a few minutes after startup and every night at 04:30 it fetches every day not yet fetched in full, newest
first, up to 1,000 a day, so the backlog since November 2021 arrives over the first two nights. The log shows
`Weather Underground: N days to fetch` and `fetched N days`. Then it's published to SQL Server with the summaries.

```bash
sqlite3 -readonly /var/lib/weathercollector/weathercollector.db \
  "SELECT COUNT(*), MIN(local_date), MAX(local_date), SUM(observation_count) FROM wunderground_days;"
```

### Battery changes

After changing the sensor's batteries, check `devices` (above) for a new id of the same model. If there is one, **add** it to `/etc/weathercollector/env` as the next item (`Station__DeviceIds__1=…`), keeping the old one so its history stays the station's, then `sudo systemctl restart weathercollector`. Until then its messages are archived, but not as the station's.

---

## Backups

| Copy | Where | Kept |
|---|---|---|
| Nightly (03:30) | `/var/lib/weathercollector/backups/weathercollector-<UTC time>.db.gz` on the host: SQLite online backup, integrity-checked, gzipped | 7 |
| Off-site | `s3://<bucket>/weathercollector/…db.gz.age`, encrypted with `age` on the host before upload. The host has only the public key, and its AWS key may only upload under `weathercollector/`. Older copies move to Glacier Deep Archive ([deploy/aws](../../deploy/aws/README.md)) | All |
| On the Mac | `~/Backups/WeatherCollector`, pulled daily at 09:10 (or on wake) by [`deploy/mac/install-backup-pull.sh weather`](../../deploy/mac/install-backup-pull.sh) with a key the host limits to read-only rsync of the backups folder; a healthchecks.io check catches a missed pull | 30 |

Backups grow with the archive (about 1.2 GB a year before compression).

The collectors can share one `age` key pair (one private key to keep safe) or have one each; HOST-SETUP copies the Libre collector's public key. For a separate pair, generate one as in step 1 of [Libre's HOST-SETUP §8b](../../deploy/libre/HOST-SETUP.md).

**Restore test** (once after setting up, then occasionally): download the newest `weathercollector/` object (if it's in Deep Archive, first **Initiate restore** and wait), put the private key from your password manager in a file, then:

```bash
age --decrypt --identity offsite.key --output restored.db.gz weathercollector-<timestamp>.db.gz.age
gunzip -k restored.db.gz && sqlite3 restored.db "PRAGMA integrity_check; SELECT COUNT(*), MAX(received_at_utc) FROM messages;"
rm offsite.key
```

`ok` and a plausible count mean the copy is good.

**Restoring** a backup: `sudo systemctl stop weathercollector`, `gunzip -k` it, copy the `.db` over `/var/lib/weathercollector/weathercollector.db` (owner `weathercollector`, mode 0640; remove any `-wal`/`-shm` files next to it), then `sudo systemctl start weathercollector`. Messages received since the backup are lost, so restore only when the database is broken.
