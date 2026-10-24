# LibreCollector

*Part of [GreenHedges.HomeServer](../../README.md); formerly its own repository, `doowruc/LibreCollector`. Code in `src/Collectors/Libre/`, deployment in `deploy/libre/`.*

An always-on service that keeps **your own complete, permanent archive** of FreeStyle Libre glucose readings, fetched once a minute through LibreLinkUp, with optional **Nightscout** dashboards and **Apple Health** sync built on top.

> **Not a medical device.** LibreCollector is a personal data archive, not an alarm. It doesn't alert on glucose levels and must never replace your Libre app's own low/high alarms or your clinical team's advice. It uses Abbott's **unofficial** LibreLinkUp API, which can change or stop working without notice.

---

## How it works

```
Libre sensor ──► LibreLink app (phone) ──► Abbott LibreView (unchanged; your clinic still sees everything)
                                                │
                                   LibreLinkUp follower account (read-only)
                                                │  polled once a minute
                                                ▼
                                  LibreCollector (always-on Linux host)
                                                │
                       ┌────────────────────────┼─────────────────────────┐
                       ▼                        ▼                         ▼
              SQLite archive          Nightscout (Docker)         Email alerts + heartbeat
          (source of truth, kept       charts and reports,        (data stale, login problems,
           forever; nightly backups)   tailnet-only via HTTPS      failed backups, host down)
                                               │
                                               ▼
                                Nightguard on iPhone ──► Apple Health
```

- **You keep using LibreLink as normal.** LibreCollector reads your data as a *follower*, exactly like a family member using the LibreLinkUp app. Nothing connects to the sensor, and LibreView/clinic sharing is untouched.
- **The SQLite archive is the single source of truth.** Everything else (Nightscout, Apple Health) is derived from it and can be rebuilt from it.
- Polls are timed to each reading's sensor clock, so every reading LibreLinkUp makes available is captured. Minutes your phone never uploaded live (e.g. during overnight batching) can't be recovered at one-minute resolution, but 15-minute history fills those gaps.

## What you need

| | Required? | Notes |
|---|---|---|
| A FreeStyle Libre sensor with the **LibreLink** app and a **LibreView** account | Yes | Developed and tested with a Libre 2 Plus in the UK. Other sensors that share via LibreLinkUp should work but are untested. |
| A **LibreLinkUp follower account**, used only by this service | Yes | Free; set up in step 1 below. |
| An **always-on Linux machine** | Yes | Tested on Ubuntu Server 26.04 on an old x86-64 laptop. A Raspberry Pi 5 or mini PC also suits. The collector uses about 40 MB of RAM. MongoDB (for Nightscout) needs a CPU with AVX (x86) or ARMv8.2 (e.g. Pi 5, not Pi 4). |
| A Mac or Linux machine with the **.NET 10 SDK**, `ssh` and `rsync` | Yes | To build and deploy. Builds are self-contained, so the host needs no .NET. |
| A [Resend](https://resend.com) account with a verified sending domain | Recommended | Email alerts. Without it, alerts are only logged. |
| A [healthchecks.io](https://healthchecks.io) account | Recommended | Tells you when the host itself goes silent. Free plan is plenty. |
| [Tailscale](https://tailscale.com) | For Nightscout | Private remote access; nothing is exposed to the internet. |
| An iPhone with **Nightguard** | For Apple Health | Free on the App Store. |

---

## Setup

### 1. Create a LibreLinkUp follower

1. Choose an email address for the follower that differs from your LibreView login (a new alias is ideal) and a strong, unique password.
2. In **FreeStyle LibreLink**: Menu → **Connected Apps → LibreLinkUp → Add Connection**; enter a name and the follower email.
3. Install **LibreLinkUp** on any phone, **sign up** with the follower email (same country as your LibreView account), confirm the email, **accept the terms**, and accept the invitation. Check that your reading appears.
4. **Sign out** of LibreLinkUp. The follower account is now for LibreCollector only.

### 2. Try it on your computer (optional but recommended)

```bash
git clone <this repository> && cd GreenHedges.HomeServer
dotnet test
dotnet user-secrets set "LibreLinkUp:Email" "follower@example.com" --project src/Collectors/Libre/GreenHedges.HomeServer.Collectors.Libre.Worker
read -s "pw?Follower password: " && dotnet user-secrets set "LibreLinkUp:Password" "$pw" --project src/Collectors/Libre/GreenHedges.HomeServer.Collectors.Libre.Worker; unset pw
dotnet run --project src/Collectors/Libre/GreenHedges.HomeServer.Collectors.Libre.Worker
```

(The `read -s "pw?…"` prompt is zsh syntax; in bash use `read -s -p "Follower password: " pw`.)

Within a few seconds you should see `Logged in to LibreLinkUp`, a `Graph backfill` of about 12 hours, then a `New reading` line every minute. Stop it with Ctrl+C before deploying: **only one collector should use the follower account at a time.**

### 3. Prepare the host

Follow [deploy/libre/HOST-SETUP.md](../../deploy/libre/HOST-SETUP.md) sections 0–8: name and fixed IP, updates, SSH keys, firewall, unattended upgrades, Tailscale, and the `librecollector` service account. Laptop and disk-encryption specifics are covered there too.

### 4. Configure secrets on the host

Secrets live in `/etc/librecollector/env` (root-only, `0600`), as environment variables (`__` separates sections):

```ini
LibreLinkUp__Email=follower@example.com
LibreLinkUp__Password=…
Alerts__ResendApiKey=re_…
Alerts__From=LibreCollector <alerts@yourdomain.example>
Alerts__To__0=you@example.com
Heartbeat__PingUrl=https://hc-ping.com/<uuid>
```

For the heartbeat, create a healthchecks.io check with a **5-minute period and about 20 minutes' grace**.

### 5. Deploy

From your computer:

```bash
deploy/libre/deploy.sh you@your-host
```

To avoid typing the host every time, copy [`deploy/local.env.example`](../../deploy/local.env.example) to `deploy/local.env` (git-ignored) and set `COLLECTORS_SSH`; then `deploy/libre/deploy.sh` needs no argument.

It publishes a self-contained Linux build, installs it to `/opt/librecollector` with the systemd unit, and restarts the service (data lives in `/var/lib/librecollector`). The output ends with the service status and recent log lines. Then send yourself a test email from the host:

```bash
sudo systemd-run --quiet --wait --pipe --uid=librecollector -p EnvironmentFile=/etc/librecollector/env -p WorkingDirectory=/var/lib/librecollector /opt/librecollector/GreenHedges.HomeServer.Collectors.Libre.Worker --test-alert
```

(`systemd-run` reads the root-only env file the same way the service does, then runs the command as the `librecollector` user.)

### 6. Optional extras

| Feature | How |
|---|---|
| **Off-machine backups** | Nightly backups are automatic on the host (7 kept). To copy them to a Mac daily with a read-only key: [HOST-SETUP §8a](../../deploy/libre/HOST-SETUP.md). Set `LIBRECOLLECTOR_USER` (and `LIBRECOLLECTOR_HOST` if needed) in `deploy/local.env` first. |
| **Off-site encrypted backups** | Nightly backups encrypted on the host with `age` (public key only on the server) and uploaded to an S3 bucket with a write-only key; old copies move to Glacier Deep Archive. S3-compatible providers work too. [HOST-SETUP §8b](../../deploy/libre/HOST-SETUP.md). |
| **Nightscout dashboards** | [HOST-SETUP §10](../../deploy/libre/HOST-SETUP.md): Docker stack, HTTPS through Caddy (`https://nightscout.<home domain>`), then add `Nightscout__BaseUrl=http://127.0.0.1:1337` and `Nightscout__ApiSecret=…` to the env file and redeploy. Review [`deploy/libre/nightscout/compose.yaml`](../../deploy/libre/nightscout/compose.yaml) first: it's set for **mmol/L, Europe/London and a 3.9–10.0 target range**. |
| **Apple Health** | Install Tailscale and **Nightguard** on the iPhone, point Nightguard at your Nightscout address (`https://nightscout.<home domain>`), and turn on its Apple Health sync. It backfills Health from Nightscout each time you open it (about a week at most), so open it at least **twice a week**; a recurring Reminder works well. Don't automate opening it: Health writes fail while the phone is locked. |
| **Your LibreView history** | Download your data from LibreView (**Glucose History → Download glucose data**) and import it: [HOST-SETUP §11](../../deploy/libre/HOST-SETUP.md). Adds 15-minute history back to the start of your account, plus insulin, carbs and notes (archive only). Re-runnable. |

---

## Day to day

Once it's running, there's nothing to do. You'll hear from it only when something needs attention.

### Emails you might get

| Subject | Meaning | What to do |
|---|---|---|
| **No new glucose readings** | No new reading for 20 minutes (after a sensor change, for 20 minutes after its warm-up was due to end) | Usually phone or sensor: phone offline, LibreLink closed, Bluetooth, a warm-up that overran, or an **expired sensor** (the email says so). A RESOLVED email follows when readings return. |
| **LibreLinkUp needs attention** | Wrong password, terms to re-accept, app version retired, or no connection | Read the email. For terms, sign in to the LibreLinkUp app with the follower account once. For a version error, raise `LibreLinkUp:Version`. Collection retries every 30 minutes. |
| **Backups failing** | No good backup for 36 hours | Check disk space and the log. |
| **Off-site backup failing** | No successful off-site copy for 36 hours | Check the log (`Off-site copy … failed`): AWS key, bucket policy, or `age` missing. Local backups are unaffected. |
| **Nightscout publishing stalled** | Couldn't reach Nightscout for 30 minutes | Check the Docker stack. Collection carries on; Nightscout catches up. |
| healthchecks.io "down" | The host stopped pinging | Host off, offline, or waiting at a disk-encryption prompt after a reboot. |

Every PROBLEM email is followed by one RESOLVED email, including across restarts.

### Useful commands (on the host)

```bash
systemctl status librecollector                                    # running?
sudo journalctl -u librecollector -f                               # live log
sudo journalctl -u librecollector --since today -p warning         # problems today
sudo -u librecollector sqlite3 /var/lib/librecollector/librecollector.db \
  "SELECT source, COUNT(*), MIN(timestamp_utc), MAX(timestamp_utc) FROM readings GROUP BY source;"
```

### Updating

```bash
git pull
deploy/libre/deploy.sh you@your-host
```

Database migrations run automatically at startup, and a backup is taken at startup if none exists from the last day.

### Sensor changes

Nothing to do. The collector logs `Sensor changed: old -> new … readings expected from …`, holds the "No new glucose readings" alert until 20 minutes after warm-up is due to end, and adjusts polling to the new sensor's timing automatically. A normal swap sends no email.

---

## Configuration reference

Settings come from `appsettings.json` (defaults, shipped with the build), overridden by environment variables in `/etc/librecollector/env` (`Section__Key`, arrays as `Section__Key__0`). Durations use `hh:mm:ss`.

**LibreLinkUp**

| Key | Default | Notes |
|---|---|---|
| `LibreLinkUp:Email` / `Password` | (none) | Follower account. Required. |
| `LibreLinkUp:Region` | auto | e.g. `eu`, `eu2`, `us`. Normally discovered from the login redirect. |
| `LibreLinkUp:Version` | `5.0.1` | App version sent to Abbott. Raise it if the API rejects it (the alert says so). |
| `LibreLinkUp:Product` | `llu.android` | |
| `LibreLinkUp:TokenRefreshMargin` | `00:05:00` | |

**Collector and archive**

| Key | Default | Notes |
|---|---|---|
| `Collector:PatientId` | first connection | Only needed if the follower follows more than one person. |
| `Collector:CurrentInterval` | `00:01:00` | |
| `Collector:PollOffset` | `00:00:35` | Poll this long after each reading's sensor time. |
| `Collector:GraphInterval` | `00:30:00` | 15-minute history backfill. |
| `Collector:SensorLifetime` | `15.00:00:00` | For the "expected end" log line and the "probably expired" alert wording. |
| `Collector:SensorWarmup` | `01:00:00` | Used only if LibreLinkUp doesn't report a sensor's warm-up. |
| `Collector:UserActionPause` | `00:30:00` | Wait after errors that need you to act (avoids account lockout). |
| `Archive:DatabasePath` | `librecollector.db` | Relative to the working directory (`/var/lib/librecollector` on the host). |

**Alerts, heartbeat and backups**

| Key | Default | Notes |
|---|---|---|
| `Alerts:ResendApiKey` / `From` / `To__0…` | (none) | Without a key, alerts are only logged. Use a **sending-only** Resend key. |
| `Alerts:StaleAfter` | `00:20:00` | |
| `Alerts:PublishStalledAfter` | `00:30:00` | |
| `Alerts:StateFile` | `alert-state.json` | Keeps open alerts across restarts. |
| `Heartbeat:PingUrl` | (none) | healthchecks.io ping URL. Treat it as a secret. |
| `Heartbeat:Interval` | `00:05:00` | |
| `Backup:Enabled` / `Directory` / `TimeOfDay` / `Retain` | `true` / `backups` / `03:30:00` / `7` | Local time of the host. |
| `Backup:AlertAfter` | `1.12:00:00` | 36 hours. |

**Off-site copies** (off unless `Offsite:Bucket` is set)

| Key | Default | Notes |
|---|---|---|
| `Offsite:Bucket` / `Region` | (none) | e.g. `eu-west-2`. |
| `Offsite:AgeRecipient` | (none) | `age1…` public key. Keep the private key off the server. |
| `Offsite:AccessKeyId` / `SecretAccessKey` | (default AWS chain) | A key that may only `s3:PutObject` under the prefix. |
| `Offsite:Prefix` | `librecollector` | Folder in the bucket. |
| `Offsite:ServiceUrl` | (AWS) | For S3-compatible providers. |
| `Offsite:StorageClass` | `STANDARD` | Use a lifecycle rule for Deep Archive. |
| `Offsite:CheckInterval` / `AlertAfter` | `01:00:00` / `1.12:00:00` | |

**Nightscout publishing**

| Key | Default | Notes |
|---|---|---|
| `Nightscout:BaseUrl` | (off) | e.g. `http://127.0.0.1:1337`. Publishing is off when empty. |
| `Nightscout:ApiSecret` | (none) | Nightscout's `API_SECRET` (at least 12 characters). |
| `Nightscout:BatchSize` | `500` | |
| `Nightscout:Device` | `LibreCollector` | Device name on entries. |

**Command-line modes** (run on the host as the `librecollector` user, from `/var/lib/librecollector`):

| Command | Does |
|---|---|
| `GreenHedges.HomeServer.Collectors.Libre.Worker --test-alert` | Sends a test email and exits. |
| `GreenHedges.HomeServer.Collectors.Libre.Worker --import-libreview <file.csv> [--time-zone <IANA id>] [--date-format <format>]` | Imports a LibreView export and prints a summary. Defaults: `Europe/London`, `dd-MM-yyyy HH:mm`. |

---

## Data and privacy

- **What's stored, and where:** every reading (with the raw API data), sensor details and imported history, in a SQLite file on *your* host. Logs, alert state and backups sit alongside it. Nightscout's MongoDB holds a derived copy, and Apple Health holds what Nightguard writes.
- **What leaves the host:** requests to Abbott's LibreLinkUp API, alert emails through Resend, heartbeat pings (timestamps only) to healthchecks.io, and (if enabled) off-site backups, **encrypted before upload** with a key the host can't decrypt. Nothing else.
- **What's never in the repository:** credentials, glucose data, exports, logs and backups (see `.gitignore` and [CONTRIBUTING.md](../../CONTRIBUTING.md)). Keep it that way if you fork it.
- Encrypting the host's disk (LUKS) is recommended; [HOST-SETUP](../../deploy/libre/HOST-SETUP.md) covers the trade-offs.

## Known limitations

- **Unofficial API.** Abbott can change LibreLinkUp at any time. Version and terms changes are detected and emailed; deeper changes need a code update.
- **Per-minute completeness depends on your phone.** If LibreLink uploads late or in batches (common overnight on iOS), the skipped minutes never appear to followers. Graph backfill fills those gaps at 15-minute resolution.
- **LibreView exports are 15-minute only**, and readings in the autumn clock-change hour are skipped on import (they can't be placed reliably).
- **Nightscout display:** Libre "LO" shows as **LOW** in the big current reading, but chart tooltips still show 2.2 mmol/L. Values above 400 mg/dL (22.2 mmol/L) display as "HIGH" even when they're a real reading (Nightscout was designed around Dexcom's limits).
- **Apple Health** only updates when Nightguard is opened.

## More

- [CONTRIBUTING.md](../../CONTRIBUTING.md): building, testing, the pull-request workflow, CI.
- [DESIGN.md](DESIGN.md): design decisions, findings about the API and exports, and the backlog.
- [deploy/libre/HOST-SETUP.md](../../deploy/libre/HOST-SETUP.md): host preparation, Nightscout, backups, LibreView import.

## Licence

[MIT](../../LICENSE).

---

Designed and built with [Claude Code](https://claude.com/claude-code).
