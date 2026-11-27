#!/usr/bin/env bash
# Install or update SQL Server's compose file on the host, then (re)start it and check it answers.
# Run on the dev machine:  deploy/sqlserver/deploy-sqlserver.sh [user@host]
# The host defaults to COLLECTORS_SSH from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/sqlserver/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/sqlserver/deploy-sqlserver.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying compose file to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
scp -q "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HOST:sqlserver-compose.yaml"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo test -s /etc/sqlserver/sqlserver.env || { echo "Missing /etc/sqlserver/sqlserver.env (see deploy/sqlserver/HOST-SETUP.md)"; exit 1; }
  sudo test -d /var/lib/sqlserver/data -a -d /var/lib/sqlserver/backups || { echo "Missing /var/lib/sqlserver/data or backups (see HOST-SETUP.md)"; exit 1; }
  sudo docker compose -f ~/sqlserver-compose.yaml config -q
  sudo install -m 0644 -o root -g root ~/sqlserver-compose.yaml /opt/sqlserver/compose.yaml
  rm -f ~/sqlserver-compose.yaml
  sudo docker compose -f /opt/sqlserver/compose.yaml pull -q
  echo "Starting (the first start sets up SQL Server, which takes a minute) ..."
  sudo docker compose -f /opt/sqlserver/compose.yaml up -d --remove-orphans --wait --wait-timeout 300
  echo
  sudo docker compose -f /opt/sqlserver/compose.yaml ps
  echo
  # Asked inside the container, where the password is already in the environment, so it never appears on this command line.
  sudo docker compose -f /opt/sqlserver/compose.yaml exec -T sqlserver sh -c \
    "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -h -1 -W -Q \"SET NOCOUNT ON; SELECT @@VERSION; SELECT SERVERPROPERTY('"'"'Edition'"'"')\""'

exit
}
