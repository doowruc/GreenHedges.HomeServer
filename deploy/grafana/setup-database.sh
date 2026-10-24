#!/usr/bin/env bash
# Set up Grafana's read-only login on the server's SQL Server (db_datareader on the Weather database), and its password
# in /etc/grafana/grafana.env. Run on the dev machine:  deploy/grafana/setup-database.sh [user@host]
# The password is generated on the host and goes straight to SQL Server and the env file (never on a command line,
# never off the host). Safe to re-run: it sets a new password and restarts Grafana if it's running. The host defaults to
# COLLECTORS_SSH from deploy/local.env. You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/grafana/setup-database.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying the SQL to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
scp -q "${SSH_OPTS[@]}" "$HERE/setup-database.sql" "$HOST:grafana-setup-database.sql"

echo "==> Setting up Grafana's login and password on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  trap "rm -f ~/grafana-setup-database.sql" EXIT
  sudo test -f /etc/grafana/grafana.env || { echo "Missing /etc/grafana/grafana.env (see deploy/grafana/HOST-SETUP.md)"; exit 1; }
  sudo test -f /opt/sqlserver/compose.yaml || { echo "SQL Server is not set up (see deploy/sqlserver/HOST-SETUP.md)"; exit 1; }
  P="$(openssl rand -base64 24 | tr -d "/+=")Aa1"
  # The password reaches sqlcmd as the first line of its script (:setvar), through a pipe.
  { printf ":setvar Password %s\n" "$P"; cat ~/grafana-setup-database.sql; } \
    | sudo docker compose -f /opt/sqlserver/compose.yaml exec -T sqlserver \
        sh -c "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -b -i /dev/stdin"
  sudo sed -i "/^WEATHER_DB_PASSWORD=/d" /etc/grafana/grafana.env
  printf "WEATHER_DB_PASSWORD=%s\n" "$P" | sudo tee -a /etc/grafana/grafana.env > /dev/null
  P=
  echo "Password written to /etc/grafana/grafana.env."
  if [ -f /opt/grafana/compose.yaml ] && sudo docker compose -f /opt/grafana/compose.yaml ps -q grafana | grep -q .; then
    sudo docker compose -f /opt/grafana/compose.yaml up -d --force-recreate --wait && echo "Restarted Grafana with the new password."
  fi'

exit
}
