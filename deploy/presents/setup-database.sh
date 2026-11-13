#!/usr/bin/env bash
# Set up Presents' database on the server's SQL Server: the Presents database (created empty if it
# doesn't exist), the presents login and its database user (owner), then the app's connection string in
# /etc/presents/env. Run on the dev machine:  deploy/presents/setup-database.sh [user@host]
# The login's password is generated on the host and goes straight to SQL Server and the env file (never on a command
# line, never off the host). Safe to re-run: it sets a new password, rewrites the connection string and restarts the
# app if it's running. The data comes in afterwards, through the app's spreadsheet import. The host defaults to COLLECTORS_SSH
# from deploy/local.env. You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/presents/setup-database.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying the SQL to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
scp -q "${SSH_OPTS[@]}" "$HERE/setup-database.sql" "$HOST:presents-setup-database.sql"

echo "==> Setting up the database, login and connection string on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  trap "rm -f ~/presents-setup-database.sql" EXIT
  sudo test -f /etc/presents/env || { echo "Missing /etc/presents/env (see deploy/presents/HOST-SETUP.md)"; exit 1; }
  sudo test -f /opt/sqlserver/compose.yaml || { echo "SQL Server is not set up (see deploy/sqlserver/HOST-SETUP.md)"; exit 1; }
  P="$(openssl rand -base64 24 | tr -d "/+=")Aa1"
  # The password reaches sqlcmd as the first line of its script (:setvar), through a pipe.
  { printf ":setvar Password %s\n" "$P"; cat ~/presents-setup-database.sql; } \
    | sudo docker compose -f /opt/sqlserver/compose.yaml exec -T sqlserver \
        sh -c "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -b -i /dev/stdin"
  sudo sed -i "/^ConnectionStrings__Presents=/d" /etc/presents/env
  printf "ConnectionStrings__Presents=Server=127.0.0.1,1433;Database=Presents;User ID=presents;Password=%s;TrustServerCertificate=True;Encrypt=True\n" "$P" \
    | sudo tee -a /etc/presents/env > /dev/null
  P=
  echo "Connection string written to /etc/presents/env."
  if systemctl is-active --quiet presents; then
    sudo systemctl restart presents && echo "Restarted presents with the new password."
  fi'

exit
}
