# Vehicle Manager: host setup

Runs Vehicle Manager on the home server, on its SQL Server, at `https://vehicles.<home domain>` (through Caddy). What it does and
its settings: [docs/vehicle-manager/README.md](../../docs/vehicle-manager/README.md). `<your-user>@<host>` is your
login on the server; the deploy scripts default to `COLLECTORS_SSH` from `deploy/local.env`.

**First:** the .NET runtime ([section 1 of the Libre collector's HOST-SETUP](../libre/HOST-SETUP.md#1-update-and-basic-tools)), SQL Server ([deploy/sqlserver/HOST-SETUP.md](../sqlserver/HOST-SETUP.md)) and Caddy
([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)), whose Caddyfile already has the `vehicles` name.

## 1. Service account and folders (on the server)

```bash
sudo useradd --system --home-dir /var/lib/vehiclemanager --create-home --shell /usr/sbin/nologin vehiclemanager && sudo chmod 750 /var/lib/vehiclemanager
```

```bash
sudo usermod -aG sqlbackups vehiclemanager && sudo usermod -aG vehiclemanager "$USER"
```

```bash
sudo install -d -m 0755 /opt/vehiclemanager && sudo install -d -m 0750 /etc/vehiclemanager && sudo install -m 0600 /dev/null /etc/vehiclemanager/env
```

## 2. Settings (on the Mac, then the server)

**healthchecks.io:** create a check (period **5 minutes**, grace **15 minutes**) and keep its ping URL.

**On the Mac**, print the app's settings from your user secrets, in environment-variable form:

```bash
cd src/VehicleManager/GreenHedges.HomeServer.VehicleManager && dotnet user-secrets list | grep -E '^(Auth0|AllowedEmails|Resend|MotHistory|Dvla)' | awk -F' = ' '{k=$1; gsub(":","__",k); print k"="$2}'; cd - > /dev/null
```

**On the server**, copy the Libre collector's alert and off-site settings, then open the file:

```bash
sudo grep -E '^(Alerts__|Offsite__(Bucket|Region|AgeRecipient)=)' /etc/librecollector/env | sudo tee -a /etc/vehiclemanager/env > /dev/null
```

```bash
sudoedit /etc/vehiclemanager/env
```

Paste the lines printed on the Mac, change the `Alerts__From` name to `VehicleManager`, and add the heartbeat and the
`greenhedges-offsite-vehicles` access key, so the file has:

```
Auth0__Domain=…
Auth0__ClientId=…
Auth0__ClientSecret=…
AllowedEmails=…
Resend__ApiKey=re_…
Resend__From=Vehicle Manager <…>
MotHistory__ClientId=…
MotHistory__ClientSecret=…
MotHistory__ApiKey=…
MotHistory__TokenUrl=…
Alerts__ResendApiKey=re_…
Alerts__From=VehicleManager <…>
Alerts__To__0=…
Heartbeat__PingUrl=https://hc-ping.com/<uuid>
Offsite__Bucket=<bucket>
Offsite__Region=eu-west-2
Offsite__AccessKeyId=AKIA…
Offsite__SecretAccessKey=…
Offsite__AgeRecipient=age1…
```

`setup-database.sh` (§4) adds the connection string.

## 3. Bring the data across from Azure (on the Mac)

Skip this for a fresh install. Stop any local copy of the app first, and make no changes in it from now on.

```bash
dotnet tool install -g microsoft.sqlpackage
```

Export the Azure database (its connection string comes from your user secrets, without being shown):

```bash
mkdir -p ~/Backups/VehicleManager && cd src/VehicleManager/GreenHedges.HomeServer.VehicleManager && AZ="$(dotnet user-secrets list | sed -n 's/^ConnectionStrings:VehicleManager = //p')" && sqlpackage /Action:Export /SourceConnectionString:"$AZ" /TargetFile:"$HOME/Backups/VehicleManager/azure-$(date +%Y%m%d).bacpac"; unset AZ; cd - > /dev/null
```

Import it into the server's SQL Server, through an SSH tunnel, as `sa` (its password from your password manager):

```bash
ssh -fN -o ExitOnForwardFailure=yes -L 14330:127.0.0.1:1433 <your-user>@<host>
```

```bash
read -rs "SA?sa password: "; echo; sqlpackage /Action:Import /SourceFile:"$(ls -t ~/Backups/VehicleManager/azure-*.bacpac | head -1)" /TargetServerName:"localhost,14330" /TargetDatabaseName:VehicleManager /TargetUser:sa /TargetPassword:"$SA" /TargetTrustServerCertificate:True; unset SA
```

```bash
pkill -f "14330:127.0.0.1:1433"
```

Keep the `.bacpac`: it's the fallback until the Azure database is deleted.

## 4. Database login (on the Mac)

```bash
deploy/vehicle-manager/setup-database.sh
```

It creates the database if §3 didn't, the `vehiclemanager` login as its owner, and writes the connection string.

## 5. Auth0 (in a browser)

In the Auth0 dashboard, the application's settings: add `https://vehicles.<home domain>/callback` to **Allowed
Callback URLs** and `https://vehicles.<home domain>/Account/SignedOut` to **Allowed Logout URLs** (keep the
localhost ones). Save.

## 6. Deploy (on the Mac)

```bash
deploy/vehicle-manager/deploy.sh
```

## 7. Check

On the server:

```bash
curl -s http://127.0.0.1:5080/health; echo
```

```bash
sudo systemd-run --quiet --wait --pipe --uid=vehiclemanager -p EnvironmentFile=/etc/vehiclemanager/env -p WorkingDirectory=/var/lib/vehiclemanager /opt/vehiclemanager/GreenHedges.HomeServer.VehicleManager --test-alert
```

```bash
journalctl -u vehiclemanager -n 40 --no-pager -o cat
```

Expect `Healthy`, the test email, and in the log `Backup written: /var/lib/vehiclemanager/backups/vehiclemanager-….bak.gz`
and `Off-site copy uploaded: …` (the first backup is taken at startup). Then open `https://vehicles.<home domain>`
on the Mac and your phone (Tailscale on), sign in, and check the dashboard's figures against the local copy's. The
healthchecks.io check turns green within 5 minutes.

## 8. Backup pull to the Mac (on the Mac, then the server)

**On the Mac:**

```bash
deploy/mac/install-backup-pull.sh vehicle-manager
```

**On the server**, append the line it prints to `~/.ssh/authorized_keys`:

```bash
ttt ~/.ssh/authorized_keys
```

**In a browser**, create another healthchecks.io check (period **1 day**, grace **1 day**). Then **on the Mac**, save its
ping URL and test a pull:

```bash
printf '%s\n' 'https://hc-ping.com/<uuid>' > ~/.config/vehiclemanager/backup-ping-url && chmod 600 ~/.config/vehiclemanager/backup-ping-url
```

```bash
~/.local/bin/vehiclemanager-pull-backups
```

## 9. Afterwards (on the Mac)

Don't run the app locally against the real data again (its nightly jobs would send twice). For development, point
it at the local SQL Server: *Local database* in the [README](../../docs/vehicle-manager/README.md).
