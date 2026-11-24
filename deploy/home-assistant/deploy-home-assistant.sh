#!/usr/bin/env bash
# Install or update Home Assistant on the host: its compose file, then pull the image, (re)start it and check it
# answers. No backup timer during the trial: HA makes its own (HOST-SETUP.md §4).
# Run on the dev machine:  deploy/home-assistant/deploy-home-assistant.sh [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/home-assistant/HOST-SETUP.md). You'll be asked for your sudo password on the
# host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/home-assistant/deploy-home-assistant.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying Home Assistant's files to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/home-assistant-release && mkdir ~/home-assistant-release'
scp -q "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HOST:home-assistant-release/"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo test -d /var/lib/home-assistant/config || { echo "Missing /var/lib/home-assistant/config (see deploy/home-assistant/HOST-SETUP.md)"; exit 1; }
  sudo docker compose -f ~/home-assistant-release/compose.yaml config -q
  sudo install -d -m 0755 /opt/home-assistant
  sudo install -m 0644 -o root -g root ~/home-assistant-release/compose.yaml /opt/home-assistant/compose.yaml
  rm -rf ~/home-assistant-release
  sudo docker compose -f /opt/home-assistant/compose.yaml pull -q
  # --wait waits for the health check (manifest.json), which HA answers once its web server is up.
  sudo docker compose -f /opt/home-assistant/compose.yaml up -d --remove-orphans --wait --wait-timeout 300
  echo
  sudo docker compose -f /opt/home-assistant/compose.yaml ps
  echo
  code="$(curl -sS -o /dev/null -w "%{http_code}" http://127.0.0.1:8123/manifest.json 2>/dev/null || true)"
  echo "GET /manifest.json: HTTP $code"
  if sudo test -f /var/lib/home-assistant/config/.storage/onboarding; then
    echo "Onboarding started or done (owner account made)."
  else
    echo "Not onboarded yet: finish in the browser (HOST-SETUP.md §3)."
  fi
  sudo docker compose -f /opt/home-assistant/compose.yaml logs --no-log-prefix --since 5m homeassistant | grep -E "ERROR|CRITICAL" | tail -10 || true'

exit
}
