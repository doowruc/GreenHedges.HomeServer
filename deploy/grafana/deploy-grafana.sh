#!/usr/bin/env bash
# Install or update Grafana on the host: its compose file, provisioning (data sources, dashboard folders) and
# dashboards, then (re)start it so it reads them, and check it answers.
# Run on the dev machine:  deploy/grafana/deploy-grafana.sh [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/grafana/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/grafana/deploy-grafana.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying Grafana's files to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/grafana-release && mkdir ~/grafana-release'
scp -rq "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HERE/provisioning" "$HERE/dashboards" "$HOST:grafana-release/"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo test -s /etc/grafana/grafana.env || { echo "Missing /etc/grafana/grafana.env (see deploy/grafana/HOST-SETUP.md)"; exit 1; }
  sudo grep -q "^WEATHER_DB_PASSWORD=" /etc/grafana/grafana.env || { echo "No WEATHER_DB_PASSWORD yet: run deploy/grafana/setup-database.sh"; exit 1; }
  sudo test -d /var/lib/grafana || { echo "Missing /var/lib/grafana (see HOST-SETUP.md)"; exit 1; }
  sudo docker compose -f ~/grafana-release/compose.yaml config -q
  sudo rsync -a --delete --chown=root:root --chmod=D755,F644 ~/grafana-release/ /opt/grafana/
  rm -rf ~/grafana-release
  sudo docker compose -f /opt/grafana/compose.yaml pull -q
  # Recreated every time, so changed data sources are read (dashboards are also picked up while running).
  sudo docker compose -f /opt/grafana/compose.yaml up -d --force-recreate --remove-orphans --wait --wait-timeout 120
  echo
  sudo docker compose -f /opt/grafana/compose.yaml ps
  echo
  curl -fsS http://127.0.0.1:3000/api/health; echo
  sudo docker compose -f /opt/grafana/compose.yaml logs --no-log-prefix --since 2m grafana | grep -iE "level=(error|warn)" | grep -vE "migrator|dual writing" | tail -10 || true'

exit
}
