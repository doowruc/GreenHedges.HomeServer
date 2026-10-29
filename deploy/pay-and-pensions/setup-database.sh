#!/usr/bin/env bash
# Set up Pay & Pensions's database on the server's SQL Server: the PayAndPensions database (created empty if it
# doesn't exist), the payandpensions login and its database user (owner), then the app's connection string in
# /etc/payandpensions/env. Run on the dev machine:  deploy/pay-and-pensions/setup-database.sh [user@host]
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
  ""|-*) echo "Usage: deploy/pay-and-pensions/setup-database.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying the SQL to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
scp -q "${SSH_OPTS[@]}" "$HERE/setup-database.sql" "$HOST:payandpensions-setup-database.sql"

echo "==> Setting up the database, login and connection string on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  trap "rm -f ~/payandpensions-setup-database.sql" EXIT
  sudo test -f /etc/payandpensions/env || { echo "Missing /etc/payandpensions/env (see deploy/pay-and-pensions/HOST-SETUP.md)"; exit 1; }
  sudo test -f /opt/sqlserver/compose.yaml || { echo "SQL Server is not set up (see deploy/sqlserver/HOST-SETUP.md)"; exit 1; }
  P="$(openssl rand -base64 24 | tr -d "/+=")Aa1"
  # The password reaches sqlcmd as the first line of its script (:setvar), through a pipe.
  { printf ":setvar Password %s\n" "$P"; cat ~/payandpensions-setup-database.sql; } \
    | sudo docker compose -f /opt/sqlserver/compose.yaml exec -T sqlserver \
        sh -c "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -b -i /dev/stdin"
  sudo sed -i "/^ConnectionStrings__PayAndPensions=/d" /etc/payandpensions/env
  printf "ConnectionStrings__PayAndPensions=Server=127.0.0.1,1433;Database=PayAndPensions;User ID=payandpensions;Password=%s;TrustServerCertificate=True;Encrypt=True\n" "$P" \
    | sudo tee -a /etc/payandpensions/env > /dev/null
  P=
  echo "Connection string written to /etc/payandpensions/env."
  if systemctl is-active --quiet payandpensions; then
    sudo systemctl restart payandpensions && echo "Restarted payandpensions with the new password."
  fi'

exit
}
