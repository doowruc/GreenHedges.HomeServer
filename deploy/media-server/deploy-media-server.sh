#!/usr/bin/env bash
# Install or update the media servers on trial and the photo job on the host:
#   jellyfin  Jellyfin's compose file into /opt/jellyfin, then pull, (re)start and check it answers
#   plex      Plex's into /opt/plex, the same way
#   photos    the photo job (media-photos.sh) into /opt/media-server, its service and timer into systemd, the timer
#             enabled. It doesn't start the job: the first run is by hand (HOST-SETUP.md §7)
#   all       all three (the default)
# Run on the dev machine:  deploy/media-server/deploy-media-server.sh [jellyfin|plex|photos|all ...] [user@host]
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/media-server/HOST-SETUP.md). You'll be asked for your sudo password on the
# host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi

usage() {
  echo "Usage: deploy/media-server/deploy-media-server.sh [jellyfin|plex|photos|all ...] [user@host]" >&2
  echo "       (the host defaults to COLLECTORS_SSH in deploy/local.env)" >&2
  exit 2
}

TARGETS=()
HOST=""
for arg in "$@"; do
  case "$arg" in
    jellyfin|plex|photos) TARGETS+=("$arg") ;;
    all) TARGETS+=(jellyfin plex photos) ;;
    -*) usage ;;
    *) [ -z "$HOST" ] || usage; HOST="$arg" ;;
  esac
done
[ "${#TARGETS[@]}" -gt 0 ] || TARGETS=(jellyfin plex photos)
HOST="${HOST:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
[ -n "$HOST" ] || usage
# One of each, in a fixed order.
WANT=""
for t in jellyfin plex photos; do
  case " ${TARGETS[*]} " in *" $t "*) WANT="$WANT $t" ;; esac
done
WANT="${WANT# }"

# One shared SSH connection for every step, so the key is unlocked once.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Copying the media server files ($WANT) to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/media-server-release && mkdir -p ~/media-server-release/jellyfin ~/media-server-release/plex ~/media-server-release/photos'
for t in $WANT; do
  case "$t" in
    jellyfin) scp -q "${SSH_OPTS[@]}" "$HERE/jellyfin/compose.yaml" "$HOST:media-server-release/jellyfin/" ;;
    plex) scp -q "${SSH_OPTS[@]}" "$HERE/plex/compose.yaml" "$HOST:media-server-release/plex/" ;;
    photos) scp -q "${SSH_OPTS[@]}" "$HERE/photos/media-photos.sh" "$HERE/photos/media-photos.service" \
      "$HERE/photos/media-photos.timer" "$HOST:media-server-release/photos/" ;;
  esac
done

# The steps on the host, as a script copied over and run there with a terminal (for sudo's password prompt; a script on
# ssh's stdin would leave sudo without one). WANT is its arguments: only the fixed words above.
cat >"$CONTROL_DIR/remote.sh" <<'REMOTE'
set -euo pipefail
WANT="$*"
R=~/media-server-release
need() { sudo test "$@" || { echo "Missing: test $* (see deploy/media-server/HOST-SETUP.md)"; exit 1; }; }

# Shared by everything.
id media >/dev/null 2>&1 || { echo "No media user (HOST-SETUP.md §1)"; exit 1; }
need -f /etc/media-server/compose.env
need -d /srv/media
need -d /srv/photos-jpeg

# A server: its folders, its compose file, the .env link, then pull and start.
server() {
  local name="$1"; shift
  local dir="/opt/$name"
  for d in "$@"; do need -d "$d"; done
  sudo install -d -m 0755 "$dir"
  sudo ln -sfn /etc/media-server/compose.env "$dir/.env"
  sudo docker compose --project-directory "$dir" -f "$R/$name/compose.yaml" config -q
  sudo install -m 0644 -o root -g root "$R/$name/compose.yaml" "$dir/compose.yaml"
  sudo docker compose -f "$dir/compose.yaml" pull -q
  # --wait waits for the health check.
  sudo docker compose -f "$dir/compose.yaml" up -d --remove-orphans --wait --wait-timeout 300
  echo
  sudo docker compose -f "$dir/compose.yaml" ps
  sudo docker compose -f "$dir/compose.yaml" logs --no-log-prefix --since 5m | grep -iE "\b(error|fatal|critical)\b" | tail -10 || true
  echo
}

for t in $WANT; do
  case "$t" in
    jellyfin)
      echo "--- Jellyfin"
      server jellyfin /var/lib/jellyfin/config /var/lib/jellyfin/cache
      code="$(curl -sS -o /dev/null -w "%{http_code}" http://127.0.0.1:8096/health 2>/dev/null || true)"
      echo "Jellyfin GET /health: HTTP $code"
      echo "Graphics chip in the container (vainfo, if the image has it):"
      sudo docker compose -f /opt/jellyfin/compose.yaml exec -T jellyfin sh -c \
        'if [ -x /usr/lib/jellyfin-ffmpeg/vainfo ]; then /usr/lib/jellyfin-ffmpeg/vainfo --display drm --device /dev/dri/renderD128 2>&1 | grep -E "Driver version|VAProfile(H264|HEVC).*(EncSlice|VLD)" ; else echo "  (no vainfo in the image)"; fi' || true
      echo
      ;;
    plex)
      echo "--- Plex"
      server plex /var/lib/plex/config
      code="$(curl -sS -o /dev/null -w "%{http_code}" http://127.0.0.1:32400/identity 2>/dev/null || true)"
      echo "Plex GET /identity: HTTP $code"
      if sudo grep -qE '^PLEX_CLAIM=.+' /etc/media-server/compose.env; then
        echo "PLEX_CLAIM is still set in /etc/media-server/compose.env: once Plex shows up in your account, empty it (HOST-SETUP.md §6)."
      fi
      echo
      ;;
    photos)
      echo "--- Photo job"
      need -f /etc/media-server/photos.env
      for tool in heif-convert exiftool; do
        command -v "$tool" >/dev/null || { echo "Missing $tool (HOST-SETUP.md §2)"; exit 1; }
      done
      command -v magick >/dev/null || command -v convert >/dev/null || { echo "Missing ImageMagick (HOST-SETUP.md §2)"; exit 1; }
      bash -n "$R/photos/media-photos.sh"
      sudo install -d -m 0755 /opt/media-server
      sudo install -m 0755 -o root -g root "$R/photos/media-photos.sh" /opt/media-server/media-photos.sh
      sudo install -m 0644 -o root -g root "$R/photos/media-photos.service" "$R/photos/media-photos.timer" /etc/systemd/system/
      sudo systemd-analyze verify /etc/systemd/system/media-photos.service /etc/systemd/system/media-photos.timer
      sudo systemctl daemon-reload
      sudo systemctl enable --now media-photos.timer
      systemctl list-timers media-photos.timer --no-pager
      echo
      ;;
  esac
done
rm -rf "$R"
REMOTE
scp -q "${SSH_OPTS[@]}" "$CONTROL_DIR/remote.sh" "$HOST:media-server-release/"

echo "==> Checking, installing and starting on $HOST"
# shellcheck disable=SC2086 # WANT is fixed words, split on purpose
ssh -t "${SSH_OPTS[@]}" "$HOST" "bash ~/media-server-release/remote.sh $WANT"

exit
}
