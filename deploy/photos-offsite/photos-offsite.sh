#!/usr/bin/env bash
# The photos' off-site copy: an rclone sync of the photo folders into their own S3 bucket (deploy/aws/photos-offsite.yaml),
# encrypted by rclone crypt (contents and names) and stored in Glacier Deep Archive. Why, and how it fits together:
# docs/photos-offsite/PLAN.md.
#
# The sources are one or more folders (now read-only mounts of the old file server's photo shares; later the home
# server's own datasets), each given a name: PHOTOS_OFFSITE_SOURCES="photos=/srv/backup/photos private=/srv/…". rclone
# joins them into one tree (a combine remote), so the bucket holds <name>/<path> for each, and the filter file
# (/etc/photos-offsite/filters.txt, filters.example.txt) chooses what's in it. The copy mirrors what the filters choose:
# a photo deleted, or a folder taken out of the filters, is deleted from the bucket too, where versioning keeps it for
# the bucket's NoncurrentDays (90) before it's gone.
#
# Nightly the comparison only lists the bucket (no request per photo): a photo is uploaded when it's newer than its
# copy (--update --use-server-modtime), which every new or edited photo is. On the first Sunday of the month (or with
# --full) it compares sizes instead (--size-only), which also catches a photo replaced by an older-dated one.
#
# Installed as /opt/photos-offsite/photos-offsite.sh by deploy-photos-offsite.sh; run nightly as the photos-offsite user
# by photos-offsite.service (photos-offsite.timer). Settings in /etc/photos-offsite/photos-offsite.env
# (photos-offsite.env.example); rclone's remotes in /etc/photos-offsite/rclone.conf (rclone.conf.example).
#
#   photos-offsite.sh            sync, then ping healthchecks.io with rclone's summary
#   photos-offsite.sh --full     the same, comparing sizes (the monthly run)
#   photos-offsite.sh --dry-run  what a sync would upload and delete (nothing changed, no ping)
#   photos-offsite.sh --check    the folders the filters don't mention, and exit (nothing read from the bucket)
#
# PHOTOS_OFFSITE_CHECK_MOUNTS=0 skips the check that each source is a mount, e.g. to try it with plain folders.
set -euo pipefail
umask 077

CONFIG_DIR="${PHOTOS_OFFSITE_CONFIG_DIR:-/etc/photos-offsite}"
RCLONE_CONF="${PHOTOS_OFFSITE_RCLONE_CONFIG:-$CONFIG_DIR/rclone.conf}"
FILTERS="${PHOTOS_OFFSITE_FILTERS:-$CONFIG_DIR/filters.txt}"
SOURCES="${PHOTOS_OFFSITE_SOURCES:-}"
# The crypt remote in rclone.conf (it wraps the S3 one).
REMOTE="${PHOTOS_OFFSITE_REMOTE:-photos-offsite}"
CHECK_MOUNTS="${PHOTOS_OFFSITE_CHECK_MOUNTS:-1}"
# /run/photos-offsite, the service's runtime folder, kept between runs so a run by hand uses the same lock.
RUN_DIR="${PHOTOS_OFFSITE_RUN_DIR:-/run/photos-offsite}"
# Working hours (both of us work from home): at most 20 MiB/s (about 170 Mbit/s of the gigabit line) Monday to Friday
# 08:00-18:00, otherwise unlimited. rclone's timetable: each entry holds until the next.
BWLIMIT="${PHOTOS_OFFSITE_BWLIMIT:-Mon-08:00,20M Mon-18:00,off Tue-08:00,20M Tue-18:00,off Wed-08:00,20M Wed-18:00,off Thu-08:00,20M Thu-18:00,off Fri-08:00,20M Fri-18:00,off}"
TRANSFERS="${PHOTOS_OFFSITE_TRANSFERS:-4}"
# A run may delete at most this share of the copies (and at least MAX_DELETE_ANYWAY): more likely a source came up
# empty or half there, or the filters are wrong, than that many photos were really deleted. rclone stops deleting at
# the limit and the run fails; what it did delete is still in the bucket as older versions.
MAX_DELETE_PERCENT="${PHOTOS_OFFSITE_MAX_DELETE_PERCENT:-10}"
MAX_DELETE_ANYWAY=20

MODE=nightly
DRY_RUN=0
case "${1:-}" in
  "") [ "$(date +%u)" = 7 ] && [ "$(date +%-d)" -le 7 ] && MODE=full ;;
  --full) MODE=full ;;
  --dry-run) DRY_RUN=1 ;;
  --check) MODE=check ;;
  *) echo "Usage: photos-offsite.sh [--full|--dry-run|--check]" >&2; exit 2 ;;
esac
[ "$#" -le 1 ] || { echo "Usage: photos-offsite.sh [--full|--dry-run|--check]" >&2; exit 2; }

# healthchecks.io: /start, then success or /fail with the reason, so a missed or failed night emails. Not for dry runs
# and checks.
ping_heartbeat() {
  [ "$DRY_RUN" = 0 ] && [ "$MODE" != check ] && [ -n "${HEARTBEAT_PING_URL:-}" ] || return 0
  curl -fsS -m 10 --retry 3 -o /dev/null --data-raw "$2" "$HEARTBEAT_PING_URL$1" || echo "Heartbeat ping failed" >&2
}
fail() {
  trap - ERR
  echo "Photos off-site failed: $1" >&2
  ping_heartbeat /fail "$1"
  exit 1
}
trap 'fail "command at line $LINENO exited with $?"' ERR

ping_heartbeat /start ""

command -v rclone >/dev/null || fail "rclone isn't installed"
[ -r "$RCLONE_CONF" ] || fail "can't read $RCLONE_CONF"
[ -r "$FILTERS" ] || fail "can't read $FILTERS"
[ -n "$SOURCES" ] || fail "PHOTOS_OFFSITE_SOURCES is empty (name=folder pairs)"

# Each source must really be there: listing a folder triggers its automount; then it must be a mount of its own, not the
# empty folder underneath (an empty source would look like "every photo deleted"), and must hold something.
mounted() {
  ls "$1" >/dev/null 2>&1 || return 1
  [ "$CHECK_MOUNTS" = 1 ] || return 0
  [ "$(findmnt -n -o TARGET --target "$1")" = "$1" ] && [ "$(findmnt -n -o FSTYPE --target "$1")" != autofs ]
}
UPSTREAMS=""
NAMES=()
for pair in $SOURCES; do
  name="${pair%%=*}"; dir="${pair#*=}"
  case "$name" in ''|*[!a-z0-9-]*) fail "source name '$name' must be lower-case letters, digits and hyphens" ;; esac
  case "$dir" in /*) ;; *) fail "source '$name' must be an absolute folder" ;; esac
  case "$dir" in *[[:space:]]*) fail "source folder '$dir' mustn't contain spaces" ;; esac
  mounted "$dir" || fail "$dir isn't mounted (source '$name')"
  [ -n "$(ls -A "$dir")" ] || fail "$dir is empty (source '$name')"
  UPSTREAMS="$UPSTREAMS $name=$dir"
  NAMES+=("$name")
done

# Folders the filters don't mention, one level down in each source that isn't taken whole: a new folder should be a
# decision, not an accident. A warning only (in the summary), not a failure.
UNLISTED=()
for pair in $SOURCES; do
  name="${pair%%=*}"; dir="${pair#*=}"
  grep -qxF "+ /$name/**" "$FILTERS" && continue
  for d in "$dir"/*/; do
    [ -d "$d" ] || continue
    base="$(basename "$d")"
    grep -qF "/$name/$base/" "$FILTERS" || UNLISTED+=("$name/$base")
  done
done
WARNING=""
if [ "${#UNLISTED[@]}" -gt 0 ]; then
  WARNING="Not in the filters (neither included nor excluded): $(printf '%s, ' "${UNLISTED[@]}")"
  WARNING="${WARNING%, }"
  echo "$WARNING" >&2
fi
if [ "$MODE" = check ]; then
  [ -n "$WARNING" ] || echo "Every folder is in the filters."
  exit 0
fi

# One run at a time (the timer's and one started by hand).
if [ "$DRY_RUN" = 0 ]; then
  [ -d "$RUN_DIR" ] && [ -w "$RUN_DIR" ] || RUN_DIR="${TMPDIR:-/tmp}"
  exec 9>"$RUN_DIR/photos-offsite.lock"
  flock -n 9 || fail "another run is still going"
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/photos-offsite.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export RCLONE_CONFIG="$RCLONE_CONF"
# The sources, joined, as a remote defined here rather than in rclone.conf (it only names local folders).
export RCLONE_CONFIG_SOURCES_TYPE=combine
export RCLONE_CONFIG_SOURCES_UPSTREAMS="${UPSTREAMS# }"
# rclone needs nowhere to write, but keep anything it might away from $HOME (there isn't one).
export RCLONE_CACHE_DIR="$WORK/cache"

# The rules, as one file: rclone reads every --filter before any --filter-from, whatever their order on the command line,
# so the excludes before the file and the "- **" after it must go in with it.
{
  printf '%s\n' "- .*" "- .*/**" "- Thumbs.db" "- desktop.ini" "- @eaDir/**"
  cat "$FILTERS"
  echo
  echo "- **"
} >"$WORK/filters.txt"
COMMON=(--filter-from "$WORK/filters.txt" --fast-list --transfers "$TRANSFERS" --checkers 8)

# What's in the bucket now, for the delete limit. One listing (about a request per 1,000 files).
COPIES="$(rclone size --json "${COMMON[@]}" "$REMOTE:" 2>"$WORK/size.err" | sed -n 's/.*"count":\([0-9]*\).*/\1/p')" \
  || fail "can't list the bucket: $(tail -3 "$WORK/size.err" | tr '\n' ' ')"
[ -n "$COPIES" ] || fail "can't read the bucket's size: $(tail -3 "$WORK/size.err" | tr '\n' ' ')"
MAX_DELETE=$((COPIES * MAX_DELETE_PERCENT / 100))
[ "$MAX_DELETE" -ge "$MAX_DELETE_ANYWAY" ] || MAX_DELETE=$MAX_DELETE_ANYWAY

if [ "$MODE" = full ]; then
  COMPARE=(--size-only)
else
  COMPARE=(--update --use-server-modtime)
fi
EXTRA=()
[ "$DRY_RUN" = 0 ] || EXTRA=(--dry-run)

echo "Photos off-site: $MODE sync of ${NAMES[*]} to $REMOTE: ($COPIES copies there; at most $MAX_DELETE deletions)"
RC=0
# rclone's log to the journal as it goes and to a file for the summary; RC is rclone's exit code (pipefail).
rclone sync "${COMMON[@]}" "${COMPARE[@]}" ${EXTRA[@]+"${EXTRA[@]}"} \
  --delete-excluded --max-delete "$MAX_DELETE" --bwlimit "$BWLIMIT" \
  --log-level INFO --stats 15m --stats-log-level NOTICE --stats-one-line \
  "sources:" "$REMOTE:" 2>&1 | tee "$WORK/sync.log" >&2 || RC=$?

# rclone's own summary, from its last stats line, and the counts from its log.
STATS="$(grep -E 'Transferred:|ETA' "$WORK/sync.log" | tail -1 | sed 's/^.*NOTICE: *//' || true)"
COPIED="$(grep -cE 'INFO *: .*: (Copied|Multi-thread Copied)' "$WORK/sync.log" || true)"
DELETED="$(grep -cE 'INFO *: .*: Deleted' "$WORK/sync.log" || true)"
SUMMARY="$MODE: $COPIED uploaded, $DELETED deleted, $COPIES copies before; $STATS"
[ -z "$WARNING" ] || SUMMARY="$SUMMARY. $WARNING"

if [ "$RC" != 0 ]; then
  ERRORS="$(grep -E 'ERROR|CRITICAL|Failed' "$WORK/sync.log" | tail -5 | sed -E "s/^.*(ERROR|CRITICAL) *: *//" || true)"
  fail "rclone exited $RC (see journalctl -u photos-offsite). $SUMMARY. Last errors: $ERRORS"
fi
echo "$SUMMARY"
[ "$DRY_RUN" = 1 ] && echo "Dry run: nothing changed." || ping_heartbeat "" "$SUMMARY"
