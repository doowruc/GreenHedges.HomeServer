#!/usr/bin/env bash
# Sure's nightly database backup: pg_dump from the db container into /var/lib/sure/backups (7 kept), an age-encrypted
# copy to the off-site S3 bucket, and a healthchecks.io ping. Installed as /opt/sure/backup.sh by deploy-sure.sh; run as
# root by sure-backup.service (sure-backup.timer, nightly). Settings in /etc/sure/backup.env (backup.env.example), kept
# apart from the app's: the containers never see the AWS key.
#
# The dump is a gzipped pg_dump custom-format file (sure-<UTC time>.dump.gz), which the Mac's backup pull picks up by its
# name. Only the database: uploaded files in the app-storage volume aren't backed up (imports can be repeated). Restoring:
# HOST-SETUP.md, "Restoring". SURE_COMPOSE, SURE_BACKUPS and SURE_BACKUPS_KEPT override the defaults, e.g. to try it on a
# dev machine.
set -euo pipefail
umask 027

COMPOSE_FILE="${SURE_COMPOSE:-/opt/sure/compose.yaml}"
BACKUP_DIR="${SURE_BACKUPS:-/var/lib/sure/backups}"
KEEP="${SURE_BACKUPS_KEPT:-7}"
NAME="sure-$(date -u +%Y%m%dT%H%M%SZ).dump.gz"
RAW="$BACKUP_DIR/.$NAME.raw.tmp"
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
trap 'rm -f "$RAW" "$TMP" "$ENCRYPTED"' EXIT

ping_heartbeat /start ""
[ -d "$BACKUP_DIR" ] || fail "no backups folder $BACKUP_DIR"

# Custom format (pg_restore can restore all of it or single tables), first as a plain file: pg_restore --list reads only the
# table of contents, so feeding it from a pipe ends in "Broken pipe" for the writer once the dump is bigger than the pipe's
# buffer (64 KB), and pipefail turns that into a failed backup. A file redirect has no such problem.
docker compose -f "$COMPOSE_FILE" exec -T db pg_dump -U sure -d sure_production --format=custom --compress=0 > "$RAW"
# A dump pg_restore can read back, not just a file.
docker compose -f "$COMPOSE_FILE" exec -T db pg_restore --list < "$RAW" > /dev/null || fail "pg_restore can't read $NAME"
# Compressed once by gzip, so the file is a valid .gz for the Mac's pull check.
gzip -6 -c "$RAW" > "$TMP"
gzip -t "$TMP" || fail "gzip can't read back $NAME"
rm -f "$RAW"
mv "$TMP" "$BACKUP_DIR/$NAME"
SIZE="$(du -h "$BACKUP_DIR/$NAME" | cut -f1)"
echo "Backed up $NAME ($SIZE)"

# The newest $KEEP stay. Names sort by time.
ls -1 "$BACKUP_DIR"/sure-*.dump.gz | sort -r | tail -n +"$((KEEP + 1))" | while read -r old; do
  rm -f -- "$old" && echo "Removed $(basename "$old")"
done

if [ -n "${OFFSITE_BUCKET:-}" ]; then
  for setting in OFFSITE_REGION OFFSITE_ACCESS_KEY_ID OFFSITE_SECRET_ACCESS_KEY OFFSITE_AGE_RECIPIENT; do
    [ -n "${!setting:-}" ] || fail "OFFSITE_BUCKET is set but $setting isn't"
  done
  KEY="${OFFSITE_PREFIX:-sure}/$NAME.age"
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
