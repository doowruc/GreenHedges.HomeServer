# FreshRSS: host setup

[FreshRSS](https://github.com/FreshRSS/FreshRSS) (feed reader, in place of Feedly) in Docker: one container on
`127.0.0.1:5085` with FreshRSS's built-in SQLite, at `https://rss.<home domain>` through Caddy. ReadKit on the Mac and
iPad syncs with it over its API. How it's set up: the comments in [`compose.yaml`](compose.yaml). Upstream's own guide:
<https://github.com/FreshRSS/FreshRSS/tree/edge/Docker>.

**First:** Docker (Libre collector's [HOST-SETUP §9](../libre/HOST-SETUP.md)) and Caddy
([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)). It needs about 100 MB of RAM (capped at 256 MB). The backup
uses `sqlite3` on the host, which is already there.

## 1. Folders (on the server)

```bash
sudo install -d -m 0755 /opt/freshrss && sudo install -d -m 0750 /etc/freshrss && sudo install -d -m 0711 /var/lib/freshrss && sudo install -d -m 0770 -o 33 -g 33 /var/lib/freshrss/data
```

Apache in the container runs as `www-data`, uid and gid 33, which on the host is the unused `www-data` account (no web
server runs on the host), so the data folder is owned by 33. There are no app secrets: FreshRSS keeps its users and
password hashes in its data folder.

## 2. Backups (on the server, the Mac and in healthchecks.io)

The deploy script installs the nightly backup too, so set it up first. Details: "Backups" below.

### Backups folder and group (on the server)

Readable by you (group `freshrss`), for the Mac's pull:

```bash
sudo groupadd --system freshrss && sudo usermod -aG freshrss "$USER" && sudo install -d -m 2750 -g freshrss /var/lib/freshrss/backups
```

### Off-site user (on the Mac)

The off-site stack ([deploy/aws](../aws/README.md)) has a write-only user for FreshRSS, `<stack>-freshrss`. Update the
stack (the parameters keep their values):

```bash
aws cloudformation deploy --region eu-west-2 --stack-name greenhedges-offsite --template-file deploy/aws/offsite.yaml --capabilities CAPABILITY_NAMED_IAM
```

Then create its access key ("Application running outside AWS") in the console, **IAM → Users →
`greenhedges-offsite-freshrss` → Security credentials → Create access key**, straight into your password manager ("Home
server FreshRSS off-site key").

### Heartbeat

In healthchecks.io, add a check "FreshRSS backup": period **1 day**, grace **2 hours**. Copy its ping URL.

### Settings (on the server)

The bucket, region and `age` public key are the ones the other services use; copy them from Sure's backup settings:

```bash
sudo install -m 0600 /dev/null /etc/freshrss/backup.env && sudo grep -E '^OFFSITE_(BUCKET|REGION|AGE_RECIPIENT)=' /etc/sure/backup.env | sudo tee /etc/freshrss/backup.env > /dev/null
```

Then add the access key and the ping URL, pasted from your password manager and healthchecks.io
([`backup.env.example`](backup.env.example) has the names):

```bash
sudoedit /etc/freshrss/backup.env
```

```ini
OFFSITE_ACCESS_KEY_ID=AKIA…
OFFSITE_SECRET_ACCESS_KEY=…
HEARTBEAT_PING_URL=https://hc-ping.com/<uuid>
```

## 3. Deploy (on the Mac)

```bash
deploy/freshrss/deploy-freshrss.sh
```

It pulls the image, starts the container and ends with its state, `GET /i/: HTTP 200` (or a 3xx to the installer) and
`Not installed yet: finish in the browser`. Then load the Caddyfile, which has the `rss` name:

```bash
deploy/caddy/deploy-caddy.sh
```

## 4. Install, in the browser

Open `https://rss.<home domain>` from a tailnet device. FreshRSS's installer asks, in turn:

1. **Language:** English.
2. **Checks:** all green.
3. **Database:** **SQLite** (the default; nothing else to fill in).
4. **General configuration:** your username and password (save them in your password manager first), authentication
   **Web form**.

Then, signed in:

- **Settings → Authentication** (administration): keep **Allow anonymous reading** off and **Allow user registration**
  off (FreshRSS's defaults: only you, the admin), and tick **Allow API access (required for mobile apps)**.
- **Settings → Profile → API management**: set an **API password**, generated in your password manager ("Home server
  FreshRSS API password") and pasted in. ReadKit signs in with this, not your web password.
- **Settings → Archiving**: set **Do not automatically refresh more often than** to **20 minutes** (the lowest; the
  default is 1 hour). The refresh runs every 5 minutes but fetches a feed only once this has passed since its last
  fetch, so new posts then show within about 20-25 minutes. **Actualise** follows the same rule; to fetch one feed now,
  refresh it from its own menu.

## 5. Move from Feedly

1. **Export** from Feedly's web app: **Organize** (or Settings) → **OPML export** (<https://feedly.com/i/opml>). OPML
   holds only the subscriptions and their folders: read state and saved (starred) articles don't come across, so save
   anything you want to keep first.
2. **Import** in FreshRSS: **Subscription management → Import / export → Import**, choose the `.opml` file. The
   folders become categories. The next refresh (every 5 minutes) fetches them, or use **Actualise** now.
3. **ReadKit** (Mac and iPad; the free version has one account, so this replaces Feedly): remove the Feedly account,
   then add a **FreshRSS** account with the server address `https://rss.<home domain>`, your username and the **API
   password**. If ReadKit asks for the API address rather than the site, FreshRSS shows it under **Settings → Profile →
   API management** (the Google Reader one, ending `/api/greader.php`).
4. **The iPad needs Tailscale**, as the Mac and iPhone have: install it, sign in, and turn on **VPN On Demand** so ReadKit
   can always reach the server. Nothing syncs while Tailscale is off; the server keeps fetching feeds regardless.

## 6. First backup and the Mac's pull

On the server, run a backup now:

```bash
sudo systemctl start freshrss-backup.service && journalctl -u freshrss-backup -n 10 --no-pager
```

It shows `Backed up freshrss-….tar.gz (…, 1 user(s))`, then `Off-site copy uploaded: s3://…/freshrss/freshrss-….tar.gz.age`,
and the healthchecks.io check turns green.

On the Mac:

```bash
deploy/mac/install-backup-pull.sh freshrss
```

It installs the launchd job (09:05 daily, into `~/Backups/FreshRSS`) and prints one line. Add that line to
`~/.ssh/authorized_keys` for your user on the server (`ttt ~/.ssh/authorized_keys`; it limits the key to read-only rsync
of `/var/lib/freshrss/backups/`). Then try it once:

```bash
~/.local/bin/freshrss-pull-backups
```

It ends `ok: newest is freshrss-….tar.gz`. Optionally put a healthchecks.io ping URL for the pull in
`~/.config/freshrss/backup-ping-url`, as for the other services.

## 7. Check

**On the server:**

```bash
sudo docker compose -f /opt/freshrss/compose.yaml ps && sudo docker compose -f /opt/freshrss/compose.yaml exec -T freshrss ./cli/health.php && echo ok
```

`running`, then `ok`. Feed errors (a feed that moved or died) show in FreshRSS itself, on the feed and under
**Subscription management**; nothing emails about them.

## Upgrading

Pick a release from <https://github.com/FreshRSS/FreshRSS/releases> and read its notes. Change the `image:` tag in
`compose.yaml` and run `deploy/freshrss/deploy-freshrss.sh` again; FreshRSS updates its database on start. Back up first
if you like: `sudo systemctl start freshrss-backup.service`.

## Backups

Every night at 04:47, [`backup.sh`](backup.sh) (run by `freshrss-backup.timer`) has FreshRSS write a consistent SQLite
copy of each user's database (`cli/db-backup.php`, into `users/<user>/backup.sqlite`), checks each copy with SQLite's
integrity check, packs them with `config.php` and the users' settings into `/var/lib/freshrss/backups`
(`freshrss-<time>.tar.gz`, 7 kept), uploads an `age`-encrypted copy to the off-site bucket under `freshrss/`, and pings
healthchecks.io, which emails if a night fails or is missed. The Mac pulls the local copies daily (30 kept).

Not included: the live `db.sqlite` (the copy stands in for it), caches, favicons and extensions (they can be fetched or
installed again).

## Restoring

From a local backup, or the Mac's copy (copy it to the server first):

```bash
ls -l /var/lib/freshrss/backups
```

Or from the off-site copy: download `freshrss/freshrss-<time>.tar.gz.age` from the bucket (it may first need restoring
from Glacier Deep Archive, up to 12 hours), decrypt it on the Mac with the `age` private key from your password manager
(`age -d -i <key file> -o freshrss.tar.gz freshrss-<time>.tar.gz.age`), and copy it to the server.

Unpack it somewhere temporary (on the server):

```bash
mkdir -p /tmp/freshrss-restore && sudo tar -C /tmp/freshrss-restore -xzf /var/lib/freshrss/backups/freshrss-<time>.tar.gz && sudo ls /tmp/freshrss-restore/users
```

**If the whole server is rebuilt** (or the data folder is lost): set it up as in §1-3, but **don't** run the web
installer. Put the settings back first, then restart:

```bash
sudo cp -a /tmp/freshrss-restore/config.php /var/lib/freshrss/data/ && sudo cp -a /tmp/freshrss-restore/users/. /var/lib/freshrss/data/users/ && sudo chown -R 33:33 /var/lib/freshrss/data && sudo docker compose -f /opt/freshrss/compose.yaml restart
```

**Then, in either case**, load each user's articles and subscriptions from the copy (replace `<user>`; this overwrites
that user's database):

```bash
sudo install -m 0660 -o 33 -g 33 /tmp/freshrss-restore/users/<user>/backup.sqlite /var/lib/freshrss/data/users/<user>/restore.sqlite && sudo docker compose -f /opt/freshrss/compose.yaml exec -T --user www-data freshrss ./cli/import-sqlite-for-user.php --user <user> --force-overwrite --filename ./data/users/<user>/restore.sqlite
```

Then tidy up:

```bash
sudo rm /var/lib/freshrss/data/users/<user>/restore.sqlite && sudo rm -rf /tmp/freshrss-restore
```
