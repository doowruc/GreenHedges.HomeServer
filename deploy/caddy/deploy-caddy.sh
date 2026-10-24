#!/usr/bin/env bash
# Install or update Caddy on the host: copy its files, build the image, (re)start it and load the Caddyfile.
# Run on the dev machine:  deploy/caddy/deploy-caddy.sh [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/caddy/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/caddy/deploy-caddy.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying Caddy's files to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/caddy-release && mkdir ~/caddy-release'
scp -q "${SSH_OPTS[@]}" "$HERE/compose.yaml" "$HERE/Dockerfile" "$HERE/Caddyfile" "$HOST:caddy-release/"

echo "==> Validating, installing and starting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -eo pipefail
  sudo test -s /etc/caddy/caddy.env || { echo "Missing /etc/caddy/caddy.env (see deploy/caddy/HOST-SETUP.md)"; exit 1; }
  sudo test -d /var/lib/caddy/data -a -d /var/lib/caddy/config || { echo "Missing /var/lib/caddy/data or config (see HOST-SETUP.md)"; exit 1; }
  sudo docker compose -f ~/caddy-release/compose.yaml config -q
  sudo install -d -m 0755 /opt/caddy/etc
  sudo install -m 0644 -o root -g root ~/caddy-release/compose.yaml ~/caddy-release/Dockerfile /opt/caddy/
  sudo install -m 0644 -o root -g root ~/caddy-release/Caddyfile /opt/caddy/etc/
  rm -rf ~/caddy-release
  echo "Building the image (the first build takes a few minutes) ..."
  sudo docker compose -f /opt/caddy/compose.yaml build -q --pull
  # Check the new Caddyfile with the real settings before it goes live.
  sudo docker compose -f /opt/caddy/compose.yaml run --rm --no-deps -T caddy caddy validate --config /etc/caddy/Caddyfile 2>&1 | tail -1
  sudo docker compose -f /opt/caddy/compose.yaml up -d --remove-orphans --wait --wait-timeout 120
  # up only recreates the container when the image or compose file changed; reload picks up a changed Caddyfile.
  sudo docker compose -f /opt/caddy/compose.yaml exec -T caddy caddy reload --config /etc/caddy/Caddyfile
  echo
  sudo docker compose -f /opt/caddy/compose.yaml ps
  echo
  sudo docker compose -f /opt/caddy/compose.yaml logs --no-log-prefix --tail 15 caddy'

exit
}
