# Media server

Films, TV and photos for the house's Roku TVs, served from the home server. **On trial:** [Jellyfin](https://jellyfin.org/)
and [Plex](https://www.plex.tv/) run side by side against the same files until one wins; why, and how it's decided:
[PLAN.md](PLAN.md).

- **The files** stay on the old file server for now, shared over SMB and mounted read-only on the home server at
  `/srv/media/{films,tv,photos}`; later the home server's own drives, at the same paths.
- **Transcoding** runs on the home server's graphics chip (VA-API), so films with PGS subtitles, which Roku can't
  show, play with the subtitles burnt in.
- **Photos:** a nightly job (`media-photos.timer`, 01:30) keeps a copy of the photo share as JPEGs sized for TVs in
  `/srv/photos-jpeg`, since no server shows HEIC on Roku. The photo libraries read the copies.
- **Reached:** on the home LAN for the TVs (Jellyfin `http://<server>:8096`, Plex through its Roku channel), and on the
  tailnet at `https://jellyfin.<home domain>` and `https://plex.<home domain>`.

Setting it up, checking it and removing the loser: [deploy/media-server/HOST-SETUP.md](../../deploy/media-server/HOST-SETUP.md).

## Day to day

- **New films or photos** on the shares are picked up by the servers' scheduled scans (SMB doesn't report changes), and
  new photos by the nightly job. To see them sooner: Jellyfin, Dashboard → Libraries → Scan all libraries; Plex, the
  library's ⋯ → Scan Library Files; the photos, `sudo systemctl start --no-block media-photos.service`.
- **The photo job's last run:** `journalctl -u media-photos -n 20` (and its healthchecks.io check).
- **A film that won't play** on a TV: the server's dashboard shows whether it's transcoding, why, and whether in
  hardware. PGS subtitles always mean a transcode; a text (SRT) track for that film avoids it.
