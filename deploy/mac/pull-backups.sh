#!/usr/bin/env bash
# Pull one service's backups from the host to this Mac. Run daily by launchd, or by hand to test (see
# install-backup-pull.sh, which installs this as ~/.local/bin/<service>-pull-backups).
# Uses a dedicated SSH key that the host restricts to read-only rsync of that service's backups folder.
# Backups are <name>-<UTC time>.<extension>.gz: .db.gz for the collectors (SQLite), .bak.gz for the web apps (SQL Server).
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

# The service comes from this script's installed name (<service>-pull-backups), and the settings from
# the file the installer writes. Jobs installed before the scripts were shared set LIBRECOLLECTOR_* instead.
NAME="$(basename "$0")"; NAME="${NAME%-pull-backups}"
case "$NAME" in librecollector|weathercollector|vehiclemanager|payandpensions) ;; *) NAME=librecollector ;; esac
CONFIG="$HOME/.config/$NAME/backup-pull.env"
if [ -f "$CONFIG" ]; then . "$CONFIG"; fi
HOST="${BACKUP_HOST:-${LIBRECOLLECTOR_HOST:-}}"
REMOTE_USER="${BACKUP_USER:-${LIBRECOLLECTOR_USER:-}}"
[ -n "$HOST" ] && [ -n "$REMOTE_USER" ] || {
  case "$NAME" in vehiclemanager) ARG=vehicle-manager ;; payandpensions) ARG=pay-and-pensions ;; *) ARG="${NAME%collector}" ;; esac
  echo "No settings for $NAME in $CONFIG: run deploy/mac/install-backup-pull.sh $ARG" >&2; exit 2; }
DEST="${BACKUP_DIR:-${LIBRECOLLECTOR_BACKUP_DIR:-$HOME/Backups/LibreCollector}}"
KEY="$HOME/.ssh/${NAME}_backup"
KEEP=30                                                  # the host keeps 7; the Mac keeps a month
PING_URL_FILE="$HOME/.config/$NAME/backup-ping-url"

# macOS's /usr/bin/rsync is openrsync; use Homebrew's rsync 3.x for compatibility with rrsync on the host.
RSYNC=/opt/homebrew/bin/rsync
[ -x "$RSYNC" ] || { echo "Homebrew rsync not found: brew install rsync" >&2; exit 1; }

PING_URL=""
[ -f "$PING_URL_FILE" ] && PING_URL="$(tr -d '[:space:]' < "$PING_URL_FILE")"

ping_hc() { [ -n "$PING_URL" ] && curl -fsS -m 10 --retry 3 -o /dev/null "$PING_URL$1" || true; }
trap 'ping_hc /fail' ERR

echo "$(date '+%Y-%m-%d %H:%M:%S') pulling $NAME backups from $HOST"
mkdir -p "$DEST"

"$RSYNC" -a --ignore-existing --include="$NAME-*.gz" --exclude='*' \
  -e "ssh -i $KEY -o IdentitiesOnly=yes -o BatchMode=yes -o ConnectTimeout=20" \
  "$REMOTE_USER@$HOST:./" "$DEST/"

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
