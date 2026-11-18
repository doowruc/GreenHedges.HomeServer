#!/usr/bin/env bash
# FreshRSS's nightly backup: a consistent SQLite copy of each user's database (FreshRSS's own cli/db-backup.php), packed
# with the settings into /var/lib/freshrss/backups (7 kept), an age-encrypted copy to the off-site S3 bucket, and a
# healthchecks.io ping. Installed as /opt/freshrss/backup.sh by deploy-freshrss.sh; run as root by freshrss-backup.service
# (freshrss-backup.timer, nightly). Settings in /etc/freshrss/backup.env (backup.env.example).
#
# The backup is freshrss-<UTC time>.tar.gz (the Mac's backup pull picks it up by its name), holding data/config.php and,
# for each user, users/<user>/config.php (settings, password hashes) and users/<user>/backup.sqlite (subscriptions,
# articles, read and starred state). Not the live db.sqlite (copied while FreshRSS writes, it could be torn), caches or
# favicons. Restoring: HOST-SETUP.md, "Restoring". FRESHRSS_COMPOSE, FRESHRSS_DATA, FRESHRSS_BACKUPS and
# FRESHRSS_BACKUPS_KEPT override the defaults, e.g. to try it on a dev machine.
set -euo pipefail
umask 027

COMPOSE_FILE="${FRESHRSS_COMPOSE:-/opt/freshrss/compose.yaml}"
DATA_DIR="${FRESHRSS_DATA:-/var/lib/freshrss/data}"
BACKUP_DIR="${FRESHRSS_BACKUPS:-/var/lib/freshrss/backups}"
KEEP="${FRESHRSS_BACKUPS_KEPT:-7}"
NAME="freshrss-$(date -u +%Y%m%dT%H%M%SZ).tar.gz"
TMP="$BACKUP_DIR/.$NAME.tmp"
ENCRYPTED="$BACKUP_DIR/.$NAME.age.tmp"

# healthchecks.io: /start, then success or /fail with the reason, so a missed or failed night emails.
ping_heartbeat() {
  [ -n "${HEARTBEAT_PING_URL:-}" ] || return 0
  curl -fsS -m 10 --retry 3 -o /dev/null --data-raw "$2" "$HEARTBEAT_PING_URL$1" || echo "Heartbeat ping failed" >&2
}
fail() {
  trap - ERR
  echo "Backup failed: $1" >&2
  ping_heartbeat /fail "$1"
  exit 1
}
trap 'fail "command at line $LINENO exited with $?"' ERR
trap 'rm -f "$TMP" "$ENCRYPTED"' EXIT

ping_heartbeat /start ""
[ -d "$BACKUP_DIR" ] || fail "no backups folder $BACKUP_DIR"
[ -f "$DATA_DIR/config.php" ] || fail "FreshRSS isn't installed yet (no $DATA_DIR/config.php)"

# FreshRSS writes users/<user>/backup.sqlite for every user, through SQLite's own copy, so it's consistent while feeds
# refresh. As www-data, so the files are FreshRSS's own.
docker compose -f "$COMPOSE_FILE" exec -T --user www-data freshrss ./cli/db-backup.php -q ||
  fail "cli/db-backup.php failed"

# Each copy must be a sound SQLite database, not just a file. On the host, opened immutable (no locks, journal or -shm
# files: the data folder is read-only to this service, and nothing writes the copy now).
USERS=0
for db in "$DATA_DIR"/users/*/backup.sqlite; do
  [ -f "$db" ] || continue
  [ "$(sqlite3 "file:$db?immutable=1" 'PRAGMA integrity_check;')" = ok ] || fail "integrity check failed: $db"
  USERS=$((USERS + 1))
done
[ "$USERS" -gt 0 ] || fail "no users/*/backup.sqlite written"

# The settings and the copies; GNU tar's --exclude comes before the names it applies to.
tar -C "$DATA_DIR" -czf "$TMP" \
  --exclude='users/*/db.sqlite' --exclude='users/*/db.sqlite-*' --exclude='users/*/sqlite-backups' \
  config.php users
gzip -t "$TMP" || fail "gzip can't read back $NAME"
mv "$TMP" "$BACKUP_DIR/$NAME"
SIZE="$(du -h "$BACKUP_DIR/$NAME" | cut -f1)"
echo "Backed up $NAME ($SIZE, $USERS user(s))"

# The newest $KEEP stay. Names sort by time.
ls -1 "$BACKUP_DIR"/freshrss-*.tar.gz | sort -r | tail -n +"$((KEEP + 1))" | while read -r old; do
  rm -f -- "$old" && echo "Removed $(basename "$old")"
done

if [ -n "${OFFSITE_BUCKET:-}" ]; then
  for setting in OFFSITE_REGION OFFSITE_ACCESS_KEY_ID OFFSITE_SECRET_ACCESS_KEY OFFSITE_AGE_RECIPIENT; do
    [ -n "${!setting:-}" ] || fail "OFFSITE_BUCKET is set but $setting isn't"
  done
  KEY="${OFFSITE_PREFIX:-freshrss}/$NAME.age"
  age --recipient "$OFFSITE_AGE_RECIPIENT" --output "$ENCRYPTED" "$BACKUP_DIR/$NAME"
  # A plain PutObject, signed by curl itself (SigV4): the user may do nothing else (deploy/aws/offsite.yaml). The key
  # goes in on stdin, not the command line, so other users can't see it in ps.
  printf 'user = "%s:%s"\n' "$OFFSITE_ACCESS_KEY_ID" "$OFFSITE_SECRET_ACCESS_KEY" |
    curl -K - -sS --fail-with-body --connect-timeout 30 -m 600 --retry 3 -o /dev/null \
      --aws-sigv4 "aws:amz:$OFFSITE_REGION:s3" \
      -H "x-amz-content-sha256: $(sha256sum "$ENCRYPTED" | cut -d' ' -f1)" \
      -T "$ENCRYPTED" "https://$OFFSITE_BUCKET.s3.$OFFSITE_REGION.amazonaws.com/$KEY" ||
    fail "off-site upload of $NAME failed"
  echo "Off-site copy uploaded: s3://$OFFSITE_BUCKET/$KEY"
else
  echo "Off-site copy skipped: OFFSITE_BUCKET is not set" >&2
fi

ping_heartbeat "" "Backed up $NAME ($SIZE)"
