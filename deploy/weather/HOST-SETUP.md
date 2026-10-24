# Weather collector: host setup

Adds the Weather collector to a host already prepared with [the Libre collector's HOST-SETUP](../libre/HOST-SETUP.md) sections 0–7 (updates, SSH keys, firewall, unattended upgrades, Tailscale). Run everything on the server unless a step says **on the Mac**. `<your-user>` is your login on the server.

What it does day to day (alerts, backups, restoring): [docs/weather/README.md](../../docs/weather/README.md). Design and reasoning: [docs/weather/DESIGN.md](../../docs/weather/DESIGN.md).

## 1. rtl_433 and the RTL-SDR tools

```bash
sudo apt install -y rtl-433 rtl-sdr sqlite3
```

Both are in Ubuntu's universe repository; no build needed. The `rtl-sdr` package also installs a udev rule that gives group **`plugdev`** access to the dongle.

## 2. Keep the DVB-T driver off the dongle

Linux treats an RTL-SDR dongle as a DVB-T TV tuner and loads `dvb_usb_rtl28xxu` for it. librtlsdr can usually detach it, but it's more reliable never to load it:

```bash
printf 'blacklist dvb_usb_rtl28xxu\nblacklist rtl2832_sdr\nblacklist rtl2832\n' | sudo tee /etc/modprobe.d/blacklist-rtl-sdr.conf
```

If the dongle is plugged in, the driver is attached to it and can't be unloaded ("Module dvb_usb_rtl28xxu is in use"). Detach it first (or unplug the dongle), then unload it:

```bash
for i in /sys/bus/usb/drivers/dvb_usb_rtl28xxu/*:*; do [ -e "$i" ] && echo -n "${i##*/}" | sudo tee /sys/bus/usb/drivers/dvb_usb_rtl28xxu/unbind; done
```

```bash
sudo modprobe -r dvb_usb_rtl28xxu rtl2832_sdr rtl2832; lsmod | grep rtl28 || echo "DVB-T driver not loaded"
```

The blacklist keeps it off after a replug or reboot.

## 3. Check reception (optional)

As yourself (you're in `plugdev`), for one minute:

```bash
timeout 60 rtl_433 -f 868.3M -s 1024k -R 172 -M level -M protocol -F json
```

Each line is a message. Your station's `id` is the one you'll configure in §5; note its `snr` (above about 10 dB is comfortable). Nothing? Check the dongle is plugged in (`lsusb | grep 2838`) and move it or its antenna; a USB extension lead helps.

## 4. Service account and directories

```bash
sudo useradd --system --home-dir /var/lib/weathercollector --create-home --shell /usr/sbin/nologin weathercollector && sudo chmod 750 /var/lib/weathercollector
```

```bash
sudo install -d -m 0755 /opt/weathercollector && sudo install -d -m 0750 /etc/weathercollector
```

So you can read its logs and database without sudo (takes effect at your next login):

```bash
sudo usermod -aG weathercollector <your-user>
```

The service gets the dongle through `SupplementaryGroups=plugdev` in its unit, so the account itself needs no extra groups.

## 5. Settings that stay off the repository

```bash
sudo install -m 0600 /dev/null /etc/weathercollector/env && sudo nano /etc/weathercollector/env
```

Contents (environment variable form of the config keys; `__` separates sections, `__0` is the first list item):

```
Station__DeviceIds__0=<your station's id>
```

Without it the collector still archives every message, but none counts as the station's; its log names each new device it hears (`New device heard: Bresser-6in1 id …`), so the id can be found there too. After a battery change, if the sensor picks a new id, **add** it as `Station__DeviceIds__1=…` and keep the old one.

Defaults for everything else (frequency, rtl_433 arguments, database path, logging) are in [`appsettings.json`](../../src/Collectors/Weather/GreenHedges.HomeServer.Collectors.Weather.Worker/appsettings.json); any can be overridden here the same way (e.g. `Rtl433__Arguments=…`).

## 6. Deploy (on the Mac)

```bash
deploy/weather/deploy.sh
```

The host defaults to `COLLECTORS_SSH` (or `LIBRECOLLECTOR_SSH`) in `deploy/local.env`; otherwise pass `<your-user>@<host>`. It publishes a self-contained build, installs it in `/opt/weathercollector` with the systemd unit, starts the service and shows its status and recent log.

## 7. Check it's archiving

```bash
journalctl -u weathercollector -f
```

Expect `Archive ready (schema v1)`, `rtl_433 started`, `New device heard: … (the station)` and `Station heard`, then a summary every 15 minutes. Then:

```bash
sqlite3 -readonly /var/lib/weathercollector/weathercollector.db "SELECT COUNT(*), MAX(received_at_utc) FROM messages; SELECT * FROM devices;"
```

## 8. Off-site access key and heartbeat check (in a browser)

1. **AWS console:** IAM → Users → `<stack>-weather` → Security credentials → **Create access key** ("Application running outside AWS"). Save both values in your password manager.
2. **healthchecks.io:** create a new check with period **5 minutes** and grace **15 minutes**, and add your email integration. Keep its ping URL.

## 9. Alert, heartbeat and off-site settings (on the server)

```bash
sudo apt install -y age
```

Copy the Libre collector's Resend settings, bucket, region and age public key:

```bash
sudo grep -E '^(Alerts__|Offsite__(Bucket|Region|AgeRecipient)=)' /etc/librecollector/env | sudo tee -a /etc/weathercollector/env > /dev/null
```

```bash
sudo nano /etc/weathercollector/env
```

Change the `Alerts__From` name to `WeatherCollector`, and add the three new lines, so the file has:

```
Station__DeviceIds__0=<your station's id>
Alerts__ResendApiKey=re_...
Alerts__From=WeatherCollector <alerts@example.com>
Alerts__To__0=you@example.com
Heartbeat__PingUrl=https://hc-ping.com/<uuid>
Offsite__Bucket=<bucket>
Offsite__Region=eu-west-2
Offsite__AccessKeyId=AKIA…
Offsite__SecretAccessKey=…
Offsite__AgeRecipient=age1…
```

## 10. Deploy and check (on the Mac, then the server)

**On the Mac**, from the repository:

```bash
deploy/weather/deploy.sh
```

**On the server**, send a test email and check the log:

```bash
sudo systemd-run --quiet --wait --pipe --uid=weathercollector -p EnvironmentFile=/etc/weathercollector/env -p WorkingDirectory=/var/lib/weathercollector /opt/weathercollector/GreenHedges.HomeServer.Collectors.Weather.Worker --test-alert
```

```bash
journalctl -u weathercollector -n 40 --no-pager -o cat
```

Expect `Backup written: …`, `Off-site backup to s3://<bucket>/weathercollector` and `Off-site copy uploaded: …`, with no `is not set` warnings. The healthchecks.io check turns green within 5 minutes.

Restore-test the first off-site copy: [README: Backups](../../docs/weather/README.md#backups).

## 11. Backup pull (on the Mac, then the server)

**On the Mac** (needs `brew install rsync`, and `COLLECTORS_USER` / `COLLECTORS_HOST` in `deploy/local.env`):

```bash
deploy/mac/install-backup-pull.sh weather
```

**On the server**, append the line it prints to `~/.ssh/authorized_keys`:

```bash
nano ~/.ssh/authorized_keys
```

**In a browser**, create another healthchecks.io check (period **1 day**, grace **1 day**). Then **on the Mac**, save its ping URL and test a pull:

```bash
printf '%s\n' 'https://hc-ping.com/<uuid>' > ~/.config/weathercollector/backup-ping-url && chmod 600 ~/.config/weathercollector/backup-ping-url
```

```bash
~/.local/bin/weathercollector-pull-backups
```

## 12. Summaries for Grafana (on the Mac, then the server)

Needs SQL Server ([deploy/sqlserver/HOST-SETUP.md](../sqlserver/HOST-SETUP.md)). **On the Mac**, create the `Weather`
database and the collector's login (it writes the connection string and restarts the collector):

```bash
deploy/weather/setup-database.sh
```

If the collector predates the summaries, deploy it too:

```bash
deploy/weather/deploy.sh
```

**On the server:**

```bash
journalctl -u weathercollector -n 30 --no-pager -o cat | grep -iE 'summar|SQL'
```

Expect `Publishing minute summaries to SQL Server (schema v1)` and, the first time, `Published N minute summaries` for
the whole archive (about 1,440 a day since the first message). Then:

```bash
sudo docker compose -f /opt/sqlserver/compose.yaml exec -T sqlserver sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d Weather -W -Q "SELECT COUNT(*) AS minutes, MIN(MinuteUtc) AS first, MAX(MinuteUtc) AS last, SUM(RainMm) AS rain_mm FROM dbo.MinuteSummaries"'
```

`last` is within the last minute or two.

## 13. Weather Underground history (in a browser, then the server)

**In a browser:** wunderground.com → **My Profile** → **API Keys**: copy the key (create one if there's none). Keep it in
your password manager, with the station id (`I…`, on the station's page).

**On the server**, add both to the env file (paste the key straight in):

```bash
sudo nano /etc/weathercollector/env
```

```
Wunderground__StationId=I…
Wunderground__ApiKey=…
```

Check them, fetching yesterday without storing it:

```bash
sudo systemd-run --quiet --wait --pipe --uid=weathercollector -p EnvironmentFile=/etc/weathercollector/env -p WorkingDirectory=/var/lib/weathercollector /opt/weathercollector/GreenHedges.HomeServer.Collectors.Weather.Worker --fetch-wunderground
```

Expect about 288 observations, with temperatures, rain and pressure. Then restart the collector, which starts fetching
the history a couple of minutes later:

```bash
sudo systemctl restart weathercollector
```

```bash
journalctl -u weathercollector -f -o cat | grep -i 'weather underground'
```

Expect `Weather Underground: … days to fetch`, progress every 100 days, and `Published history for N days`. The
backlog takes two nights or more: at most 1,000 days a day.

## 14. Weathercloud history (on the Mac, then the server)

**In a browser:** Weathercloud → the device → download each year's data (a zip of monthly CSVs). **On the Mac**, copy
them to the server:

```bash
scp ~/Downloads/Weathercloud_*.zip <your-user>@<host>:/tmp/
```

**On the server**, hand them to the service account and import them (safe while the collector runs, and to re-run):

```bash
sudo install -d -o weathercollector -g weathercollector -m 0700 /var/lib/weathercollector/import && sudo install -o weathercollector -g weathercollector -m 0600 /tmp/Weathercloud_*.zip /var/lib/weathercollector/import/ && rm /tmp/Weathercloud_*.zip
```

```bash
sudo -u weathercollector env Serilog__WriteTo__1__Args__path=logs/import-.log sh -c 'cd /var/lib/weathercollector && /opt/weathercollector/GreenHedges.HomeServer.Collectors.Weather.Worker --import-weathercloud import'
```

It prints a line per month and a total (about 258,000 rows in 1,800 days for 2021–2026). The collector publishes them
within a few minutes (`Published history for N days`). Then delete the copies:

```bash
sudo rm -r /var/lib/weathercollector/import
```

Later, a newer export (e.g. the rest of a month) is imported the same way; it replaces the days it contains.
