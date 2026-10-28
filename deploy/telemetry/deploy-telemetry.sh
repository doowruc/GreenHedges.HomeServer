#!/usr/bin/env bash
# Install or update the telemetry stack on the host (Alloy, Loki, Tempo, Prometheus): its compose file and config
# folders, then (re)start it so it reads them, and check each part answers.
# Run on the dev machine:  deploy/telemetry/deploy-telemetry.sh [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/telemetry/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/telemetry/deploy-telemetry.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying the telemetry stack's files to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/telemetry-release && mkdir ~/telemetry-release'
scp -rq "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HERE/alloy" "$HERE/loki" "$HERE/tempo" "$HERE/prometheus" "$HOST:telemetry-release/"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  for d in alloy loki tempo prometheus; do
    sudo test "$(sudo stat -c %u /var/lib/telemetry/$d 2>/dev/null)" = 10001 ||
      { echo "Missing /var/lib/telemetry/$d, or not owned by 10001 (see deploy/telemetry/HOST-SETUP.md)"; exit 1; }
  done
  sudo docker compose -f ~/telemetry-release/compose.yaml config -q
  sudo rsync -a --delete --chown=root:root --chmod=D755,F644 ~/telemetry-release/ /opt/telemetry/
  rm -rf ~/telemetry-release
  sudo docker compose -f /opt/telemetry/compose.yaml pull -q
  # Recreated every time, so changed config is read.
  sudo docker compose -f /opt/telemetry/compose.yaml up -d --force-recreate --remove-orphans --wait --wait-timeout 120
  echo
  sudo docker compose -f /opt/telemetry/compose.yaml ps
  echo
  # Loki and Tempo report ready about 15 seconds after starting.
  for check in "Alloy http://127.0.0.1:12345/-/ready" "Loki http://127.0.0.1:3100/ready" "Tempo http://127.0.0.1:3200/ready" "Prometheus http://127.0.0.1:9090/-/ready"; do
    set -- $check
    for i in $(seq 1 30); do curl -fsS -o /dev/null "$2" && break; sleep 2; done
    curl -fsS -o /dev/null "$2" && echo "$1 ready" || { echo "$1 is not ready: $2"; exit 1; }
  done
  echo
  sudo docker compose -f /opt/telemetry/compose.yaml logs --no-log-prefix --since 2m | grep -iE "level=error|^Error" | grep -v "empty ring" | tail -10 || true'

exit
}
