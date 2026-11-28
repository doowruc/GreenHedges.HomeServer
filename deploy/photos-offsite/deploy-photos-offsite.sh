#!/usr/bin/env bash
# Install or update the photos' off-site copy on the host: photos-offsite.sh into /opt/photos-offsite, its service and
# timer into systemd, the timer enabled. It doesn't start a sync: the first run is by hand (HOST-SETUP.md §7).
# Run on the dev machine:  deploy/photos-offsite/deploy-photos-offsite.sh [user@host]
# The host defaults to COLLECTORS_SSH from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/photos-offsite/HOST-SETUP.md). You'll be asked for your sudo password on the
# host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi

usage() {
  echo "Usage: deploy/photos-offsite/deploy-photos-offsite.sh [user@host]" >&2
  echo "       (the host defaults to COLLECTORS_SSH in deploy/local.env)" >&2
  exit 2
}
case "${1:-}" in -*) usage ;; esac
[ "$#" -le 1 ] || usage
HOST="${1:-${COLLECTORS_SSH:-}}"
[ -n "$HOST" ] || usage

bash -n "$HERE/photos-offsite.sh"

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying the photos off-site files to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/photos-offsite-release && mkdir -p ~/photos-offsite-release'
scp -q "${SSH_OPTS[@]}" "$HERE/photos-offsite.sh" "$HERE/photos-offsite.service" "$HERE/photos-offsite.timer" \
  "$HOST:photos-offsite-release/"

# The steps on the host, as a script copied over and run there with a terminal (for sudo's password prompt; a script on
# ssh's stdin would leave sudo without one).
cat >"$CONTROL_DIR/remote.sh" <<'REMOTE'
set -euo pipefail
R=~/photos-offsite-release
need() { sudo test "$@" || { echo "Missing: test $* (see deploy/photos-offsite/HOST-SETUP.md)"; exit 1; }; }

id photos-offsite >/dev/null 2>&1 || { echo "No photos-offsite user (HOST-SETUP.md §1)"; exit 1; }
command -v rclone >/dev/null || { echo "Missing rclone (HOST-SETUP.md §2)"; exit 1; }
command -v jq >/dev/null || { echo "Missing jq (HOST-SETUP.md §2)"; exit 1; }
need -f /etc/photos-offsite/photos-offsite.env
need -f /etc/photos-offsite/rclone.conf
need -f /etc/photos-offsite/filters.txt
for f in photos-offsite.env rclone.conf filters.txt; do
  [ "$(sudo stat -c '%U:%G %a' "/etc/photos-offsite/$f")" = "root:photos-offsite 640" ] \
    || { echo "/etc/photos-offsite/$f should be root:photos-offsite 0640 (HOST-SETUP.md)"; exit 1; }
done
bash -n "$R/photos-offsite.sh"
sudo install -d -m 0755 /opt/photos-offsite
sudo install -m 0755 -o root -g root "$R/photos-offsite.sh" /opt/photos-offsite/photos-offsite.sh
sudo install -m 0644 -o root -g root "$R/photos-offsite.service" "$R/photos-offsite.timer" /etc/systemd/system/
sudo systemd-analyze verify /etc/systemd/system/photos-offsite.service /etc/systemd/system/photos-offsite.timer
sudo systemctl daemon-reload
sudo systemctl enable --now photos-offsite.timer
systemctl list-timers photos-offsite.timer --no-pager
echo
echo "Folders the filters don't mention (photos-offsite.sh --check):"
# As the service runs it (its user and settings file), but only the check: nothing is read from the bucket.
sudo systemd-run --quiet --wait --pipe --collect -p User=photos-offsite -p Group=photos-offsite \
  -p EnvironmentFile=/etc/photos-offsite/photos-offsite.env /opt/photos-offsite/photos-offsite.sh --check || true
rm -rf "$R"
REMOTE
scp -q "${SSH_OPTS[@]}" "$CONTROL_DIR/remote.sh" "$HOST:photos-offsite-release/"

echo "==> Checking and installing on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" "bash ~/photos-offsite-release/remote.sh"

exit
}
