# Grafana: host setup

Grafana in Docker on `127.0.0.1:3000`, at `https://grafana.<home domain>` through Caddy, reading the Weather
collector's minute summaries from SQL Server. How it's set up: the comments in [`compose.yaml`](compose.yaml) and
[`provisioning/`](provisioning).

**First:** SQL Server ([deploy/sqlserver/HOST-SETUP.md](../sqlserver/HOST-SETUP.md)), the Weather collector's
summaries ([deploy/weather/HOST-SETUP.md](../weather/HOST-SETUP.md) §12) and Caddy
([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)).

## 1. Folders and settings (on the server)

```bash
sudo install -d -m 0755 /opt/grafana && sudo install -d -m 0750 /etc/grafana && sudo install -d -m 0750 -o 472 -g 472 /var/lib/grafana && sudo install -m 0600 /dev/null /etc/grafana/grafana.env
```

```bash
printf 'GF_SERVER_ROOT_URL=https://grafana.%s\nGF_SECURITY_ADMIN_PASSWORD=%s\n' "$(sudo sed -n 's/^HOME_DOMAIN=//p' /etc/caddy/caddy.env)" "$(openssl rand -base64 24 | tr -d '/+=')" | sudo tee /etc/grafana/grafana.env > /dev/null
```

It takes the home domain from Caddy's settings. Save the admin password in your password manager ("Home server Grafana admin", user `admin`):

```bash
sudo grep ADMIN_PASSWORD /etc/grafana/grafana.env
```

## 2. Database login (on the Mac)

```bash
deploy/grafana/setup-database.sh
```

It creates the read-only `grafana` login on the `Weather` database and adds its password to the env file.

## 3. Deploy (on the Mac)

```bash
deploy/grafana/deploy-grafana.sh
```

It ends with the container `healthy` and Grafana's health (`"database": "ok"`).

## 4. Caddy (on the Mac)

The Caddyfile has the `grafana` name. Load it:

```bash
deploy/caddy/deploy-caddy.sh
```

## 5. Check

**On the server:**

```bash
curl -s -u admin http://127.0.0.1:3000/api/datasources/uid/weather/health; echo
```

It asks for the admin password, then shows `Database Connection OK`.

**On the Mac and the iPhone** (Tailscale on): open `https://grafana.<home domain>` → **Dashboards** →
**Weather** → **Weather**, **Rain** and **Station**. No sign-in is needed to view. To edit, sign in as `admin`. The provisioned dashboards
can't be saved in Grafana: change them in the repository.

## Updating

Change the image tag in [`compose.yaml`](compose.yaml), or a dashboard or data source, in a PR, then re-run
`deploy/grafana/deploy-grafana.sh`.
