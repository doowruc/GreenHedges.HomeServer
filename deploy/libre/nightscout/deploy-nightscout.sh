#!/usr/bin/env bash
# Install or update the Nightscout compose stack on the host, then (re)start it.
# Run on the dev machine:  deploy/libre/nightscout/deploy-nightscout.sh [user@host]
# The host defaults to COLLECTORS_SSH from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/libre/HOST-SETUP.md §9 and §10). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../../local.env" ]; then . "$HERE/../../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-}}"
[ -n "$HOST" ] || { echo "Usage: deploy/libre/nightscout/deploy-nightscout.sh user@host (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2; }

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying compose file to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
scp -q "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HOST:nightscout-compose.yaml"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo test -s /etc/nightscout/nightscout.env || { echo "Missing /etc/nightscout/nightscout.env (see HOST-SETUP.md §10)"; exit 1; }
  sudo docker compose -f ~/nightscout-compose.yaml config -q
  sudo install -m 0644 -o root -g root ~/nightscout-compose.yaml /opt/nightscout/compose.yaml
  rm -f ~/nightscout-compose.yaml
  sudo docker compose -f /opt/nightscout/compose.yaml pull -q
  sudo docker compose -f /opt/nightscout/compose.yaml up -d --remove-orphans
  echo
  sudo docker compose -f /opt/nightscout/compose.yaml ps
  echo
  echo "Waiting for Nightscout to answer on 127.0.0.1:1337 ..."
  for i in $(seq 1 30); do
    if curl -fsS -m 5 http://127.0.0.1:1337/api/v1/status.json -o /tmp/ns-status.json 2>/dev/null; then
      echo "Nightscout is up:"; head -c 300 /tmp/ns-status.json; echo; rm -f /tmp/ns-status.json; exit 0
    fi
    sleep 2
  done
  echo "Nightscout did not answer within 60 s. Recent logs:"
  sudo docker compose -f /opt/nightscout/compose.yaml logs --tail 30 nightscout
  exit 1'

exit
}
