#!/usr/bin/env bash
# Install or update BookOrbit on the host: its compose file, then pull the images, (re)start it and check it answers.
# Run on the dev machine:  deploy/bookorbit/deploy-bookorbit.sh [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/bookorbit/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/bookorbit/deploy-bookorbit.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying BookOrbit's compose file to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/bookorbit-release && mkdir ~/bookorbit-release'
scp -q "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HOST:bookorbit-release/"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo test -s /etc/bookorbit/bookorbit.env || { echo "Missing /etc/bookorbit/bookorbit.env (see deploy/bookorbit/HOST-SETUP.md)"; exit 1; }
  for key in APP_URL PUID PGID POSTGRES_PASSWORD JWT_SECRET SETUP_BOOTSTRAP_TOKEN; do
    sudo grep -q "^$key=." /etc/bookorbit/bookorbit.env || { echo "No $key in /etc/bookorbit/bookorbit.env (see HOST-SETUP.md)"; exit 1; }
  done
  sudo test -d /var/lib/bookorbit/app -a -d /var/lib/bookorbit/postgres || { echo "Missing /var/lib/bookorbit/app or /postgres (see HOST-SETUP.md)"; exit 1; }
  # Without the drive, the app would start on an empty /srv/books folder on the root disk.
  mountpoint -q /srv/books || { echo "/srv/books is not mounted: is the USB drive plugged in and unlocked?"; exit 1; }
  sudo docker compose -f ~/bookorbit-release/compose.yaml config -q
  sudo install -d -m 0755 /opt/bookorbit
  sudo install -m 0644 -o root -g root ~/bookorbit-release/compose.yaml /opt/bookorbit/compose.yaml
  rm -rf ~/bookorbit-release
  sudo docker compose -f /opt/bookorbit/compose.yaml pull -q
  sudo docker compose -f /opt/bookorbit/compose.yaml up -d --remove-orphans --wait --wait-timeout 180
  echo
  sudo docker compose -f /opt/bookorbit/compose.yaml ps
  echo
  curl -fsS http://127.0.0.1:5082/api/v1/health; echo
  # Its logs are JSON lines with numeric levels: 40 warn, 50 error, 60 fatal. LegacyRouteConverter warns at every
  # start about route patterns it converts itself (3.6.0): not a problem.
  sudo docker compose -f /opt/bookorbit/compose.yaml logs --no-log-prefix --since 3m app | grep -E "\"level\":(40|50|60)" | grep -v LegacyRouteConverter | tail -10 || true'

exit
}
