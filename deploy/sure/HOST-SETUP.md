# Sure: host setup

[Sure](https://github.com/we-promise/sure) (personal finance) in Docker: web on `127.0.0.1:5084`, a Sidekiq worker,
PostgreSQL and Redis, at `https://sure.<home domain>` through Caddy. How it's set up: the comments in
[`compose.yaml`](compose.yaml). Upstream's own guide: <https://github.com/we-promise/sure/blob/main/docs/hosting/docker.md>.

**First:** Docker (Libre collector's [HOST-SETUP §9](../libre/HOST-SETUP.md)) and Caddy
([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)). It needs roughly 1 GB of RAM (about 350 MB for the app at rest).

## 1. Folders and settings (on the server)

```bash
sudo install -d -m 0755 /opt/sure && sudo install -d -m 0750 /etc/sure && sudo install -d -m 0700 /var/lib/sure && sudo install -d -m 0700 -o 999 -g 999 /var/lib/sure/postgres && sudo install -m 0600 /dev/null /etc/sure/sure.env
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

## Not done yet

Backups: nothing copies Sure's database anywhere. Until the backup change lands (nightly `pg_dump`, off-site and Mac
pull), don't put data in that you can't re-import.
