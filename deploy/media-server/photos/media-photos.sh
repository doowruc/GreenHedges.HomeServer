#!/usr/bin/env bash
# The photo job: a copy of the photo share as JPEGs sized for TVs, for the media servers' photo library. Neither Plex
# nor Jellyfin shows HEIC on Roku, and the share also holds Live Photo videos that a library would show as clips, so the
# library reads this copy, not the share.
#
# The copies go to a share of their own on the old file server, mounted read-write at /srv/media/photos-tv (later a
# dataset on the home server's own drives, at the same path), beside the originals' read-only /srv/media/photos.
#
# For every photo on the share (HEIC/HEIF, JPEG, PNG), a JPEG at most 1920 px on its long side (Roku's interface is
# 1920 × 1080, even on 4K TVs), quality 85, turned the right way up, with its EXIF data (date taken) kept. Each copy is
# named after its original plus .jpg (IMG_0001.HEIC → IMG_0001.HEIC.jpg), in the same folders, and gets the original's
# modified time, so a later run converts only photos that are new or changed. Copies whose original has gone are
# deleted. Videos are skipped.
#
# Installed as /opt/media-server/media-photos.sh by deploy-media-server.sh photos; run nightly as the media user by
# media-photos.service (media-photos.timer). Settings in /etc/media-server/photos.env (photos.env.example).
#
#   media-photos.sh            convert, then delete orphaned copies, then ping healthchecks.io
#   media-photos.sh --dry-run  only count: what would be converted and deleted (nothing written, no ping)
#
# PHOTOS_SOURCE, PHOTOS_TARGET, PHOTOS_SIZE, PHOTOS_QUALITY and PHOTOS_JOBS override the defaults below;
# PHOTOS_CHECK_MOUNTS=0 skips the check that both are mounts, e.g. to try it on a dev machine with plain folders.
set -euo pipefail
umask 022

SOURCE="${PHOTOS_SOURCE:-/srv/media/photos}"
TARGET="${PHOTOS_TARGET:-/srv/media/photos-tv}"
CHECK_MOUNTS="${PHOTOS_CHECK_MOUNTS:-1}"
# Not on the share (SMB and flock don't mix well): /run/media-photos, the service's own runtime folder, kept between
# runs so a run by hand finds it too.
LOCK_DIR="${PHOTOS_LOCK_DIR:-/run/media-photos}"
SIZE="${PHOTOS_SIZE:-1920}"
QUALITY="${PHOTOS_QUALITY:-85}"
# Conversions at once. The box has 8 threads; 3 leaves room for everything else, and the service runs at low priority.
JOBS="${PHOTOS_JOBS:-3}"
# A run that would delete more than this share of the copies (and more than a handful) stops instead: more likely the
# share came up empty or half there than that many photos were really deleted.
MAX_DELETE_PERCENT="${PHOTOS_MAX_DELETE_PERCENT:-10}"
MAX_DELETE_ANYWAY=20

# One photo: convert-one <original> <copy>. Run by xargs, below, as this same script.
if [ "${1:-}" = convert-one ]; then
  src="$2"; out="$3"
  tmp="$(dirname "$out")/.$(basename "$out").tmp.jpg"
  full="${TMPDIR:-/tmp}/media-photos.$$.jpg"
  trap 'rm -f "$tmp" "$full"' EXIT
  trap 'echo "Could not convert $src" >&2' ERR
  mkdir -p "$(dirname "$out")"
  if command -v magick >/dev/null; then IM=(magick); else IM=(convert); fi
  # jpeg:size lets libjpeg decode a JPEG at half or quarter size when that's still at least twice the target, so a
  # 48-megapixel photo isn't held in memory at full size (about 400 MB per conversion otherwise).
  HINT=(-define "jpeg:size=$((SIZE * 2))x$((SIZE * 2))")
  case "$src" in
    *.[hH][eE][iI][cCfF])
      # libheif turns the picture the right way up itself (the HEIC's own rotation), but may copy the EXIF orientation
      # tag across as well, which would turn it again: the tag goes, and no -auto-orient here.
      heif-convert -q 100 "$src" "$full" >/dev/null
      exiftool -q -q -m -overwrite_original -Orientation= "$full"
      "${IM[@]}" "${HINT[@]}" "$full" -resize "${SIZE}x${SIZE}>" -quality "$QUALITY" "$tmp"
      ;;
    *)
      # -auto-orient turns it by its EXIF orientation and resets the tag, as TVs may ignore it.
      "${IM[@]}" "${HINT[@]}" "$src" -auto-orient -resize "${SIZE}x${SIZE}>" -quality "$QUALITY" "$tmp"
      ;;
  esac
  # The date taken, for sorting, whatever the converter kept. A photo with none (a screenshot) is fine.
  exiftool -q -q -m -overwrite_original -TagsFromFile "$src" \
    '-EXIF:DateTimeOriginal' '-EXIF:CreateDate' '-EXIF:OffsetTimeOriginal' "$tmp" || true
  touch -r "$src" "$tmp"
  mv -f "$tmp" "$out"
  exit 0
fi

DRY_RUN=0
case "${1:-}" in
  "") ;;
  --dry-run) DRY_RUN=1 ;;
  *) echo "Usage: media-photos.sh [--dry-run]" >&2; exit 2 ;;
esac

# healthchecks.io: /start, then success or /fail with the reason, so a missed or failed night emails.
ping_heartbeat() {
  [ "$DRY_RUN" = 0 ] && [ -n "${HEARTBEAT_PING_URL:-}" ] || return 0
  curl -fsS -m 10 --retry 3 -o /dev/null --data-raw "$2" "$HEARTBEAT_PING_URL$1" || echo "Heartbeat ping failed" >&2
}
fail() {
  trap - ERR
  echo "Photo job failed: $1" >&2
  ping_heartbeat /fail "$1"
  exit 1
}
trap 'fail "command at line $LINENO exited with $?"' ERR

ping_heartbeat /start ""
LIST="$(mktemp -d "${TMPDIR:-/tmp}/media-photos.XXXXXX")"
trap 'rm -rf "$LIST"' EXIT

# Both shares must really be there. Listing a folder triggers its automount; then it must be a mount of its own, not
# the empty folder underneath (writing there would fill the NVMe, and an empty source would look like "every photo
# deleted").
mounted() {
  ls "$1" >/dev/null 2>&1 || return 1
  [ "$CHECK_MOUNTS" = 1 ] || return 0
  [ "$(findmnt -n -o TARGET --target "$1")" = "$1" ] && [ "$(findmnt -n -o FSTYPE --target "$1")" != autofs ]
}
mounted "$SOURCE" || fail "$SOURCE isn't mounted (the photo share)"
mounted "$TARGET" || fail "$TARGET isn't mounted (the copies' share)"
# One run at a time (the timer's and one started by hand).
if [ "$DRY_RUN" = 0 ]; then
  [ -d "$LOCK_DIR" ] && [ -w "$LOCK_DIR" ] || LOCK_DIR="${TMPDIR:-/tmp}"
  exec 9>"$LOCK_DIR/media-photos.lock"
  flock -n 9 || fail "another run is still going"
fi

# Every photo on the share, relative to it; dot folders and files (macOS and the like) left out. -mindepth 1, or "."
# itself would match '.*' and prune everything.
( cd "$SOURCE" && find . -mindepth 1 -name '.*' -prune -o -type f \
    \( -iname '*.heic' -o -iname '*.heif' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' \) -print0 ) \
  >"$LIST/originals" || fail "can't list $SOURCE"
ORIGINALS=$(( $(tr -cd '\0' <"$LIST/originals" | wc -c) ))
[ "$ORIGINALS" -gt 0 ] || fail "no photos found in $SOURCE (is the share mounted?)"

# What needs converting: no copy yet, or the original changed since (a different modified time).
: >"$LIST/todo"
TODO=0
while IFS= read -r -d '' rel; do
  rel="${rel#./}"
  out="$TARGET/$rel.jpg"
  if [ ! -f "$out" ] || [ "$SOURCE/$rel" -nt "$out" ] || [ "$SOURCE/$rel" -ot "$out" ]; then
    printf '%s\0%s\0' "$SOURCE/$rel" "$out" >>"$LIST/todo"
    TODO=$((TODO + 1))
  fi
done <"$LIST/originals"

# Copies whose original has gone (and tmp files left by a run that was stopped).
( cd "$TARGET" && find . -type f \( -name '*.jpg' -o -name '.*.tmp.jpg' \) -print0 ) >"$LIST/copies"
COPIES=$(( $(tr -cd '\0' <"$LIST/copies" | wc -c) ))
: >"$LIST/orphans"
ORPHANS=0
while IFS= read -r -d '' rel; do
  rel="${rel#./}"
  case "$(basename "$rel")" in
    .*.tmp.jpg) ;;
    *) [ -f "$SOURCE/${rel%.jpg}" ] && continue ;;
  esac
  printf '%s\0' "$TARGET/$rel" >>"$LIST/orphans"
  ORPHANS=$((ORPHANS + 1))
done <"$LIST/copies"

echo "$ORIGINALS photos on the share, $COPIES copies; $TODO to convert, $ORPHANS to delete"

if [ "$DRY_RUN" = 1 ]; then
  echo "Dry run: nothing written."
  du -sh "$TARGET" 2>/dev/null | sed 's/^/Copies so far: /' || true
  exit 0
fi

if [ "$ORPHANS" -gt "$MAX_DELETE_ANYWAY" ] && [ $((ORPHANS * 100)) -gt $((COPIES * MAX_DELETE_PERCENT)) ]; then
  fail "would delete $ORPHANS of $COPIES copies (over $MAX_DELETE_PERCENT%): check the share; to allow it, run once with PHOTOS_MAX_DELETE_PERCENT=100"
fi

# Convert. xargs goes on after a photo that fails (a damaged file shouldn't stop the rest) and exits 123 at the end.
FAILED=0
if [ "$TODO" -gt 0 ]; then
  xargs -0 -n 2 -P "$JOBS" "$0" convert-one <"$LIST/todo" || FAILED=$?
fi

DELETED=0
while IFS= read -r -d '' copy; do
  rm -f -- "$copy" && DELETED=$((DELETED + 1))
done <"$LIST/orphans"
# Folders left empty by deletions.
find "$TARGET" -mindepth 1 -type d -empty -delete

SUMMARY="$ORIGINALS photos; $TODO new or changed, $DELETED deleted; copies $(du -sh "$TARGET" | cut -f1)"
if [ "$FAILED" != 0 ]; then
  fail "some photos didn't convert (xargs exit $FAILED; see journalctl -u media-photos). $SUMMARY"
fi
echo "$SUMMARY"
ping_heartbeat "" "$SUMMARY"
