#!/usr/bin/env bash
# BookOrbit's nightly database backup: pg_dump from the postgres container into /var/lib/bookorbit/backups (7 kept),
# an age-encrypted copy to the off-site S3 bucket, and a healthchecks.io ping. Installed as /opt/bookorbit/backup.sh by
# deploy-bookorbit.sh; run as root by bookorbit-backup.service (bookorbit-backup.timer, nightly). Settings in
# /etc/bookorbit/backup.env (backup.env.example), kept apart from the app's: the containers never see the AWS key.
#
# Only the database: the books are on their own drive and not backed up here. Restoring: HOST-SETUP.md, "Restoring".
# BOOKORBIT_COMPOSE, BOOKORBIT_BACKUPS and BOOKORBIT_BACKUPS_KEPT override the defaults, e.g. to try it on a dev machine.
set -euo pipefail
umask 027

COMPOSE_FILE="${BOOKORBIT_COMPOSE:-/opt/bookorbit/compose.yaml}"
BACKUP_DIR="${BOOKORBIT_BACKUPS:-/var/lib/bookorbit/backups}"
KEEP="${BOOKORBIT_BACKUPS_KEPT:-7}"
NAME="bookorbit-$(date -u +%Y%m%dT%H%M%SZ).dump"
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

# Custom format: compressed, and pg_restore can restore all of it or single tables.
docker compose -f "$COMPOSE_FILE" exec -T postgres pg_dump -U bookorbit -d bookorbit --format=custom --compress=6 > "$TMP"
# A dump pg_restore can read back, not just a file.
docker compose -f "$COMPOSE_FILE" exec -T postgres pg_restore --list < "$TMP" > /dev/null || fail "pg_restore can't read $NAME"
mv "$TMP" "$BACKUP_DIR/$NAME"
SIZE="$(du -h "$BACKUP_DIR/$NAME" | cut -f1)"
echo "Backed up $NAME ($SIZE)"

# The newest $KEEP stay. Names sort by time.
ls -1 "$BACKUP_DIR"/bookorbit-*.dump | sort -r | tail -n +"$((KEEP + 1))" | while read -r old; do
  rm -f -- "$old" && echo "Removed $(basename "$old")"
done

if [ -n "${OFFSITE_BUCKET:-}" ]; then
  for setting in OFFSITE_REGION OFFSITE_ACCESS_KEY_ID OFFSITE_SECRET_ACCESS_KEY OFFSITE_AGE_RECIPIENT; do
    [ -n "${!setting:-}" ] || fail "OFFSITE_BUCKET is set but $setting isn't"
  done
  KEY="${OFFSITE_PREFIX:-bookorbit}/$NAME.age"
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
