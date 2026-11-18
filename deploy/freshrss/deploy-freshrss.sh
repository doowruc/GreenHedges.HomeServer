#!/usr/bin/env bash
# Install or update FreshRSS on the host: its compose file, then pull the image, (re)start it and check it answers;
# and its nightly backup (backup.sh, with its systemd service and timer).
# Run on the dev machine:  deploy/freshrss/deploy-freshrss.sh [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/freshrss/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/freshrss/deploy-freshrss.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying FreshRSS's files to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/freshrss-release && mkdir ~/freshrss-release'
scp -q "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HERE/backup.sh" "$HERE/freshrss-backup.service" \
  "$HERE/freshrss-backup.timer" "$HOST:freshrss-release/"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo test -d /var/lib/freshrss/data || { echo "Missing /var/lib/freshrss/data (see deploy/freshrss/HOST-SETUP.md)"; exit 1; }
  [ "$(sudo stat -c %u:%g /var/lib/freshrss/data)" = 33:33 ] || { echo "/var/lib/freshrss/data must be owned by 33:33, the container'"'"'s www-data (see HOST-SETUP.md)"; exit 1; }
  sudo test -d /var/lib/freshrss/backups -a -f /etc/freshrss/backup.env || { echo "Missing /var/lib/freshrss/backups or /etc/freshrss/backup.env (see HOST-SETUP.md, \"Backups\")"; exit 1; }
  command -v sqlite3 > /dev/null || { echo "The backup needs sqlite3 on the host: sudo apt install sqlite3"; exit 1; }
  sudo docker compose -f ~/freshrss-release/compose.yaml config -q
  sudo install -d -m 0755 /opt/freshrss
  sudo install -m 0644 -o root -g root ~/freshrss-release/compose.yaml /opt/freshrss/compose.yaml
  sudo install -m 0755 -o root -g root ~/freshrss-release/backup.sh /opt/freshrss/backup.sh
  sudo install -m 0644 -o root -g root ~/freshrss-release/freshrss-backup.service ~/freshrss-release/freshrss-backup.timer /etc/systemd/system/
  sudo systemctl daemon-reload
  sudo systemctl enable --now freshrss-backup.timer
  rm -rf ~/freshrss-release
  sudo docker compose -f /opt/freshrss/compose.yaml pull -q
  sudo docker compose -f /opt/freshrss/compose.yaml up -d --remove-orphans --wait --wait-timeout 120
  echo
  sudo docker compose -f /opt/freshrss/compose.yaml ps
  echo
  # Apache takes a moment after the container starts. Before the web installer has run, FreshRSS answers with its
  # installer (or a redirect to it); afterwards, its own health check reads the API page.
  for i in $(seq 1 12); do
    code="$(curl -sS -o /dev/null -w "%{http_code}" http://127.0.0.1:5085/i/ 2>/dev/null || true)"
    case "$code" in 2??|3??) echo "GET /i/: HTTP $code"; break ;; esac
    [ "$i" -lt 12 ] || { echo "GET /i/: no answer after a minute (see: sudo docker compose -f /opt/freshrss/compose.yaml logs freshrss)"; exit 1; }
    sleep 5
  done
  if sudo test -f /var/lib/freshrss/data/config.php; then
    sudo docker compose -f /opt/freshrss/compose.yaml exec -T freshrss ./cli/health.php && echo "FreshRSS health check: ok"
  else
    echo "Not installed yet: finish in the browser (HOST-SETUP.md §4)."
  fi
  echo
  systemctl list-timers freshrss-backup.timer --no-pager
  sudo docker compose -f /opt/freshrss/compose.yaml logs --no-log-prefix --since 2m freshrss | grep -iE "error|fatal|❌" | tail -10 || true'

exit
}
