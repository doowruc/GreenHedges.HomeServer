#!/usr/bin/env bash
# Pull one service's backups from the host to this Mac. Run daily by launchd, or by hand to test (see
# install-backup-pull.sh, which installs this as ~/.local/bin/<service>-pull-backups).
# Uses a dedicated SSH key that the host restricts to read-only rsync of that service's backups folder.
# Backups are <name>-<UTC time>.<extension>.gz: .db.gz for the collectors (SQLite), .bak.gz for the web apps (SQL Server), .dump.gz for Sure (PostgreSQL), .tar.gz for FreshRSS.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

# The service comes from this script's installed name (<service>-pull-backups), and the settings from
# the file the installer writes.
NAME="$(basename "$0")"; NAME="${NAME%-pull-backups}"
case "$NAME" in librecollector|weathercollector|vehiclemanager|payandpensions|utilities|presents|sure|freshrss) ;;
  *) echo "Run this as <service>-pull-backups, as deploy/mac/install-backup-pull.sh installs it" >&2; exit 2 ;; esac
CONFIG="$HOME/.config/$NAME/backup-pull.env"
if [ -f "$CONFIG" ]; then . "$CONFIG"; fi
HOST="${BACKUP_HOST:-}"
REMOTE_USER="${BACKUP_USER:-}"
DEST="${BACKUP_DIR:-}"
[ -n "$HOST" ] && [ -n "$REMOTE_USER" ] && [ -n "$DEST" ] || {
  case "$NAME" in vehiclemanager) ARG=vehicle-manager ;; payandpensions) ARG=pay-and-pensions ;; utilities) ARG=utilities ;; presents) ARG=presents ;; sure) ARG=sure ;; freshrss) ARG=freshrss ;; *) ARG="${NAME%collector}" ;; esac
  echo "No settings for $NAME in $CONFIG: run deploy/mac/install-backup-pull.sh $ARG" >&2; exit 2; }
KEY="$HOME/.ssh/${NAME}_backup"
KEEP=30                                                  # the host keeps 7; the Mac keeps a month
PING_URL_FILE="$HOME/.config/$NAME/backup-ping-url"

# macOS's /usr/bin/rsync is openrsync; use Homebrew's rsync 3.x for compatibility with rrsync on the host.
RSYNC=/opt/homebrew/bin/rsync
[ -x "$RSYNC" ] || { echo "Homebrew rsync not found: brew install rsync" >&2; exit 1; }

PING_URL=""
[ -f "$PING_URL_FILE" ] && PING_URL="$(tr -d '[:space:]' < "$PING_URL_FILE")"

# The Mac may have only just woken (launchd runs a missed job on wake), when the network can take a while to come
# back, so retry every kind of failure for a few minutes. A ping that still fails is logged, not fatal: the backup is safe.
ping_hc() {
  [ -n "$PING_URL" ] || return 0
  curl -fsS -m 20 --retry 6 --retry-delay 30 --retry-all-errors -o /dev/null "$PING_URL$1" \
    || echo "$(date '+%Y-%m-%d %H:%M:%S') warning: could not ping healthchecks.io ($1)" >&2
  return 0
}
trap 'ping_hc /fail' ERR

echo "$(date '+%Y-%m-%d %H:%M:%S') pulling $NAME backups from $HOST"
mkdir -p "$DEST"

# Six tries, 30 seconds apart (about 3 to 5 minutes in all), for the same reason: just after a wake, DNS and the
# network may not be back yet. --ignore-existing makes a retry fetch only what's still missing.
tries=0
until "$RSYNC" -a --ignore-existing --include="$NAME-*.gz" --exclude='*' \
  -e "ssh -i $KEY -o IdentitiesOnly=yes -o BatchMode=yes -o ConnectTimeout=20" \
  "$REMOTE_USER@$HOST:./" "$DEST/"; do
  tries=$((tries + 1))
  [ "$tries" -lt 6 ] || { echo "$(date '+%Y-%m-%d %H:%M:%S') rsync failed $tries times, giving up" >&2; false; }
  echo "$(date '+%Y-%m-%d %H:%M:%S') rsync failed, retrying in 30 seconds" >&2
  sleep 30
done

# Keep the newest $KEEP backups (names sort by timestamp).
find "$DEST" -maxdepth 1 -name "$NAME-*.gz" | sort -r | tail -n +$((KEEP + 1)) | while read -r old; do
  rm -f "$old" && echo "deleted $old"
done

# Sanity check: the newest copy must be a valid gzip and no more than 2 days old.
newest="$(find "$DEST" -maxdepth 1 -name "$NAME-*.gz" | sort -r | head -1)"
[ -n "$newest" ] || { echo "no backups found" >&2; false; }
gzip -t "$newest"
[ -z "$(find "$newest" -mtime +2)" ] || { echo "newest backup is more than 2 days old: $newest" >&2; false; }

echo "ok: newest is $(basename "$newest")"
ping_hc ""

exit
}
