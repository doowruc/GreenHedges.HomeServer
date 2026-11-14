#!/usr/bin/env bash
# Install or update Sure on the host: its compose file, then pull the images, (re)start it and check it answers;
# and its nightly database backup (backup.sh, with its systemd service and timer).
# Run on the dev machine:  deploy/sure/deploy-sure.sh [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/sure/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/sure/deploy-sure.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying Sure's files to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/sure-release && mkdir ~/sure-release'
scp -q "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HERE/backup.sh" "$HERE/sure-backup.service" "$HERE/sure-backup.timer" \
  "$HOST:sure-release/"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo test -s /etc/sure/sure.env || { echo "Missing /etc/sure/sure.env (see deploy/sure/HOST-SETUP.md)"; exit 1; }
  sudo test -d /var/lib/sure/postgres || { echo "Missing /var/lib/sure/postgres (see HOST-SETUP.md)"; exit 1; }
  sudo test -d /var/lib/sure/backups -a -f /etc/sure/backup.env || { echo "Missing /var/lib/sure/backups or /etc/sure/backup.env (see HOST-SETUP.md, \"Backups\")"; exit 1; }
  sudo docker compose -f ~/sure-release/compose.yaml config -q
  sudo install -d -m 0755 /opt/sure
  sudo install -m 0644 -o root -g root ~/sure-release/compose.yaml /opt/sure/compose.yaml
  sudo install -m 0755 -o root -g root ~/sure-release/backup.sh /opt/sure/backup.sh
  sudo install -m 0644 -o root -g root ~/sure-release/sure-backup.service ~/sure-release/sure-backup.timer /etc/systemd/system/
  sudo systemctl daemon-reload
  sudo systemctl enable --now sure-backup.timer
  rm -rf ~/sure-release
  sudo docker compose -f /opt/sure/compose.yaml pull -q
  sudo docker compose -f /opt/sure/compose.yaml up -d --remove-orphans --wait --wait-timeout 180
  echo
  sudo docker compose -f /opt/sure/compose.yaml ps
  echo
  curl -fsS -o /dev/null -w "GET /up: HTTP %{http_code}\n" http://127.0.0.1:5084/up
  echo
  systemctl list-timers sure-backup.timer --no-pager
  sudo docker compose -f /opt/sure/compose.yaml logs --no-log-prefix --since 2m web worker | grep -iE "error|fatal" | tail -10 || true'

exit
}
