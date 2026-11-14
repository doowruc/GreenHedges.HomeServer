# Sure: host setup

[Sure](https://github.com/we-promise/sure) (personal finance) in Docker: web on `127.0.0.1:5084`, a Sidekiq worker,
PostgreSQL and Redis, at `https://sure.<home domain>` through Caddy. How it's set up: the comments in
[`compose.yaml`](compose.yaml). Upstream's own guide: <https://github.com/we-promise/sure/blob/main/docs/hosting/docker.md>.

**First:** Docker (Libre collector's [HOST-SETUP §9](../libre/HOST-SETUP.md)) and Caddy
([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)). It needs roughly 1 GB of RAM (about 350 MB for the app at rest).

## 1. Folders and settings (on the server)

```bash
sudo install -d -m 0755 /opt/sure && sudo install -d -m 0750 /etc/sure && sudo install -d -m 0711 /var/lib/sure && sudo install -d -m 0700 -o 999 -g 999 /var/lib/sure/postgres && sudo install -m 0600 /dev/null /etc/sure/sure.env
```

PostgreSQL's image runs as uid 999, which is why its data folder is owned by 999. Generate every secret on the host, so
none leaves it:

```bash
printf 'POSTGRES_PASSWORD=%s\nSECRET_KEY_BASE=%s\nACTIVE_RECORD_ENCRYPTION_PRIMARY_KEY=%s\nACTIVE_RECORD_ENCRYPTION_DETERMINISTIC_KEY=%s\nACTIVE_RECORD_ENCRYPTION_KEY_DERIVATION_SALT=%s\n' "$(openssl rand -hex 24)" "$(openssl rand -hex 64)" "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" | sudo tee /etc/sure/sure.env > /dev/null
```

**Save a copy in your password manager now** ("Home server Sure secrets"): losing `SECRET_KEY_BASE` or the encryption keys
makes the encrypted data unreadable, and they must never change. View the file in the terminal and paste it straight into
1Password:

```bash
sudo cat /etc/sure/sure.env
```

## 2. Deploy (on the Mac)

```bash
deploy/sure/deploy-sure.sh
```

It pulls the images, starts the four containers and ends with their state and `GET /up: HTTP 200`. The first start runs
Sure's database migrations, which takes a minute.

## 3. Caddy (on the Mac)

The Caddyfile has the `sure` name. Load it:

```bash
deploy/caddy/deploy-caddy.sh
```

## 4. First sign-in

Open `https://sure.<home domain>` from a tailnet device and **create your account** (the first one). Then, in Sure's
settings, turn off open sign-ups (or invitations only) so nobody else on the tailnet can register.

## 5. Check

**On the server:**

```bash
sudo docker compose -f /opt/sure/compose.yaml ps
```

All four `running` (`db` and `redis` `healthy`).

## Upgrading

Change the `image:` tag in `x-sure-app` in `compose.yaml` and run `deploy/sure/deploy-sure.sh` again; migrations run on
start. (While it says `stable`, the deploy script's `pull` fetches whatever `stable` is now.)

## Backups

Every night at 04:30, [`backup.sh`](backup.sh) (run by `sure-backup.timer`) dumps the database into
`/var/lib/sure/backups` (`sure-<time>.dump.gz`, 7 kept), checks the dump reads back, uploads an `age`-encrypted copy to
the off-site bucket under `sure/`, and pings healthchecks.io, which emails if a night fails or is missed. The Mac pulls
the local copies daily. The deploy script installs and enables the timer. Only the database: files uploaded to Sure
(the `app-storage` volume) aren't backed up, so keep the originals of anything you import.

**Personal financial data:** the dumps hold all of it unencrypted on the host and on the Mac (as for the other services'
backups); only the off-site copy is encrypted.

### 1. Backups folder and group (on the server)

Readable by you (group `sure`), for the Mac's pull:

```bash
sudo groupadd --system sure && sudo usermod -aG sure "$USER" && sudo install -d -m 2750 -g sure /var/lib/sure/backups
```

### 2. Off-site user (on the Mac)

The off-site stack ([deploy/aws](../aws/README.md)) has a write-only user for Sure, `<stack>-sure`. Update the stack (the
parameters keep their values):

```bash
aws cloudformation deploy --region eu-west-2 --stack-name greenhedges-offsite --template-file deploy/aws/offsite.yaml --capabilities CAPABILITY_NAMED_IAM
```

Then create its access key ("Application running outside AWS") in the console, **IAM → Users →
`greenhedges-offsite-sure` → Security credentials → Create access key**, straight into your password manager ("Home
server Sure off-site key").

### 3. Heartbeat

In healthchecks.io, add a check "Sure backup": period **1 day**, grace **2 hours**. Copy its ping URL.

### 4. Settings (on the server)

The bucket, region and `age` public key are the ones the other services use; copy them from one of their env files
(here Vehicle Manager's), renamed:

```bash
sudo install -m 0600 /dev/null /etc/sure/backup.env && sudo sed -n -e 's/^Offsite__Bucket=/OFFSITE_BUCKET=/p' -e 's/^Offsite__Region=/OFFSITE_REGION=/p' -e 's/^Offsite__AgeRecipient=/OFFSITE_AGE_RECIPIENT=/p' /etc/vehiclemanager/env | sudo tee /etc/sure/backup.env > /dev/null
```

Then add the access key and the ping URL, pasted from your password manager and healthchecks.io
([`backup.env.example`](backup.env.example) has the names):

```bash
sudoedit /etc/sure/backup.env
```

```ini
OFFSITE_ACCESS_KEY_ID=AKIA…
OFFSITE_SECRET_ACCESS_KEY=…
HEARTBEAT_PING_URL=https://hc-ping.com/<uuid>
```

### 5. Deploy and try it

Re-run `deploy/sure/deploy-sure.sh` on the Mac: it installs the script and timer and shows the next run. Then run a
backup now, on the server:

```bash
sudo systemctl start sure-backup.service && journalctl -u sure-backup -n 10 --no-pager
```

It shows `Backed up sure-….dump.gz`, then `Off-site copy uploaded: s3://…/sure/sure-….dump.gz.age`, and the
healthchecks.io check turns green.

### 6. The Mac's daily pull

On the Mac:

```bash
deploy/mac/install-backup-pull.sh sure
```

It installs the launchd job (09:55 daily, into `~/Backups/Sure`) and prints one line. Add that line to
`~/.ssh/authorized_keys` for your user on the server (`ttt ~/.ssh/authorized_keys`; it limits the key to read-only rsync
of `/var/lib/sure/backups/`). Then try it once:

```bash
~/.local/bin/sure-pull-backups
```

It ends `ok: newest is sure-….dump.gz`. Optionally put a healthchecks.io ping URL for the pull in
`~/.config/sure/backup-ping-url`, as for the other services.

## Restoring

Keep `/etc/sure/sure.env` (it's in your password manager): the restored data needs the same `SECRET_KEY_BASE` and
`ACTIVE_RECORD_ENCRYPTION_*` keys.

From a local dump, or the Mac's copy (copy it to the server first):

```bash
ls -l /var/lib/sure/backups
```

Or from the off-site copy: download `sure/sure-<time>.dump.gz.age` from the bucket (it may first need restoring from
Glacier Deep Archive, up to 12 hours), decrypt it on the Mac with the `age` private key from your password manager
(`age -d -i <key file> -o sure.dump.gz sure-<time>.dump.gz.age`), and copy it to the server.

Then stop the app and worker, restore over the database, and start them again (on the server):

```bash
sudo docker compose -f /opt/sure/compose.yaml stop web worker
```

```bash
gzip -dc /var/lib/sure/backups/sure-<time>.dump.gz | sudo docker compose -f /opt/sure/compose.yaml exec -T db pg_restore -U sure -d sure_production --clean --if-exists --no-owner
```

```bash
sudo docker compose -f /opt/sure/compose.yaml start web worker
```

If the whole server is rebuilt: set it up as above with the same `sure.env`, let the first start create an empty
database, then restore.
