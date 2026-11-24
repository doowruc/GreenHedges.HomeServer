# Media server: the trial

The plan for moving the house's media server (films, TV and photos, watched on Roku TVs) from the old file server onto
the home server, and for choosing the software: **Jellyfin** or **Plex**, tried side by side. The old file server
keeps the files and goes on sharing them over SMB until the home server's hard drives go in; the media server reads
them from there, then from the drives.

**Status:** planned 2026-11-24. Nothing deployed. Waits for the RAM upgrade (see "Before it starts").

## What it has to do

1. **Play films with PGS subtitles on the Roku TVs.** Today's Plex, on the old file server, can't: it falls back to
   software transcoding and fails.
2. **A native Roku app.** Every TV in the house runs Roku OS.
3. **Show HEIC photos** (iPhone photos) from the photo share on the TVs. Not working in Plex today.
4. **Read the media from the old file server's SMB shares now**, and from the home server's own drives once they're in,
   without rebuilding the library.

A lifetime Plex Pass is already paid for, so Plex stays in the running, but it doesn't have to win.

## What's known already

### PGS subtitles mean transcoding, whichever server

PGS subtitles are pictures, not text. **Roku can't show them**, and neither the Plex nor the Jellyfin Roku app draws
them itself, so the server has to **burn them into the video**: decode, overlay, encode, for the whole film. That's
the job the old file server can't do in software. It's the same with every server and app, so the question is only
whether the server software can do it **on the home server's graphics chip**.

### The home server can do it in hardware

The DXP2800 GT's Ryzen Embedded R2514 (Picasso, 4 cores / 8 threads) has Radeon Vega 8 graphics with AMD's VCN 1.0
video engine:

- **Decodes:** H.264, HEVC 8-bit and 10-bit (up to 4K), VP9. **Not AV1.**
- **Encodes:** H.264 and **8-bit** HEVC.
- On the server: `/dev/dri/renderD128` (group `render`) is there, with the `amdgpu` driver.

So a burn-in transcode can run decode and encode on the chip, with the CPU (or the chip) doing the overlay. 1080p
sources should be comfortable; **4K is the thing to test**. Burning subtitles into an **HDR** film loses the HDR
(the encoder is 8-bit only), so it needs tone mapping to look right in SDR.

### Plex or Jellyfin

| | Plex | Jellyfin |
|---|---|---|
| Hardware transcoding, AMD on Linux | Not officially supported; forum reports are mixed (Plex loads its own bundled AMD driver, which fails on some setups) | Officially supported through VA-API, "full acceleration on Vega+" |
| Roku app | The most polished; the household knows it | Official app, plainer. It can't ask for burn-in itself: the server is set to burn in PGS |
| HEIC photos | Not supported by the server at all (so never added to the library) | Not supported (its image library has no HEIF) |
| Cost | Lifetime Pass already paid | Free |
| Accounts | Signs in through plex.tv | Local accounts only, like the other services here |

Plex's weak point is exactly the one that matters, which is why **Jellyfin is the favourite**. But Plex is free to try
here, so the trial runs both against the same files and lets the TVs decide. (Emby, the third option with a Roku app,
is paid and offers nothing here over Jellyfin. Immich is excellent at photos but has no Roku app.)

### HEIC: JPEG copies, made on the server

None of Plex, Jellyfin or Emby shows HEIC reliably on Roku, so switching software won't fix it. The fix is a job on
the server that keeps **JPEG copies** of the HEIC photos, which the media server's photo library shows instead (see
"The photo job").

### A cheaper fix for PGS, alongside

A film's PGS track can be converted to an **SRT** (text) track once, by OCR (Subtitle Edit) or by fetching a matching
SRT (Bazarr). Roku then plays the film **directly**, with no transcode, and keeps HDR. Worth doing for the films
watched most, whichever server wins; burn-in stays for the rest.

## Before it starts

- **RAM.** The box has 6.7 GiB usable, and on 2026-11-24 (Home Assistant just added) **2.6 GiB was available, with
  800 MiB in swap**. A media server wants 1-2 GB, two of them side by side more, and transcodes want memory too (see
  "Transcode scratch space"). The 2 × 16 GB upgrade, already the next hardware step, goes in **first**. Without it, the
  fallback is one server at a time, with Plex given a week and then stopped.
- **A test set.** A handful of real files that cover the hard cases, written down in the private notes with what each
  should do:
  - 1080p H.264 with a PGS track
  - 1080p HEVC with a PGS track
  - 4K HEVC 10-bit HDR with a PGS track
  - one film with SRT subtitles (should play directly, no transcode)
  - a photo folder with HEIC, JPEG and Live Photo (`.MOV`) files

## Scope

**In:**

- **Jellyfin and Plex**, each in Docker, side by side for the trial, both reading the same shares read-only.
- **Hardware transcoding** on the Vega graphics, in both.
- **The SMB shares** (films, TV, photos) mounted read-only on the server.
- **The photo job:** JPEG copies of the HEIC photos.
- **The Roku apps** on the TVs; the iPhone, iPad and Mac apps if wanted, over Tailscale.

**Out, for now:**

- **Music**, audiobooks, live TV and DVR.
- **Watching away from home** beyond the tailnet (no public port, no Plex relay set up on purpose).
- **Pre-converting PGS to SRT** (worth doing, but a job of its own after the trial).
- **The hard drives and ZFS**: their own step (`DXP2800-MIGRATION.md` in the private notes). The design below keeps
  the media paths fixed so the move needs no library rebuild.

## Design

### The shares: mounted once, on the host

The old file server's shares are mounted by the host, not by the containers, read-only, at fixed paths:

| Mount | What |
|---|---|
| `/srv/media/films` | the films share |
| `/srv/media/tv` | the TV share |
| `/srv/media/photos` | the photo share |

- **CIFS**, read-only (`ro`), through **systemd automount** (`x-systemd.automount,nofail,_netdev`), so a boot with the
  old file server off doesn't hang, and a mount comes back when it's on again. SMB 2.1, the newest the old file
  server offers.
- **Credentials:** a read-only account on the old file server, in a root-only file
  (`/etc/media-server/smb-credentials`, 0600), password in 1Password. Not in the repo.
- **File owner** in the mount: a `media` user and group (`uid`/`gid` mount options), so the containers read the files
  as `media` and never as root.
- **The containers see `/srv/media`** read-only, at `/media` inside. When the drives go in, the ZFS datasets are
  mounted at the **same paths**, the CIFS mounts are taken away, and the libraries carry on without a rescan.

### Network: the TVs need a LAN port

Every service here is on `127.0.0.1` behind Caddy, reached over Tailscale, and the firewall rule so far is **no LAN
ports**. Roku TVs can't run Tailscale, so the media server is the first exception:

- **Jellyfin** publishes `8096` on the server's **LAN address** (not `0.0.0.0`). Docker's published ports bypass
  `ufw`, so the bind address is what keeps it off any other interface. The Roku app connects to the server's LAN name
  and port, over HTTP on the LAN.
- **Plex** publishes `32400` the same way. Its Roku app finds the server through the plex.tv account and then
  connects over the LAN. Plex's "Remote Access" stays **off**.
- **Over Tailscale** (phone, iPad, Mac): Jellyfin also at `https://jellyfin.<home domain>` through Caddy, like the
  other apps, so its sign-in isn't sent in plain HTTP off the LAN. Plex's own apps connect to it directly.
- **Discovery** (Jellyfin's and Plex's LAN broadcasts) isn't needed: the TVs are given the address once.

The exception is noted in `SERVER.md`'s port table and firewall line when it's made.

### The containers

| | Jellyfin | Plex |
|---|---|---|
| Image | `jellyfin/jellyfin`, pinned to a release | `plexinc/pms-docker`, pinned to a release |
| User | `media` (`user:` in compose) | `media` (`PLEX_UID`/`PLEX_GID`) |
| Graphics | `/dev/dri/renderD128`, `group_add` the host's `render` gid | the same |
| Media | `/srv/media` → `/media`, read-only | the same |
| Config and metadata | `/var/lib/jellyfin/{config,cache}` | `/var/lib/plex/config` |
| Transcode scratch | tmpfs at `/transcode` | tmpfs at `/transcode` |
| Memory cap | 2 GB to start (including the tmpfs) | 2 GB to start |
| Port | LAN `8096`; `127.0.0.1:8096` for Caddy | LAN `32400` |

- **No `privileged`**, `no-new-privileges`, only the render device (not the whole `/dev/dri`).
- **Jellyfin's ffmpeg** comes in its image (`jellyfin-ffmpeg`, with its own Mesa build), so it doesn't depend on
  Ubuntu's driver packages. Check with `vainfo` inside the container that H.264 and HEVC encode are listed.
- **Plex's first sign-in** uses a claim token (`PLEX_CLAIM`, valid four minutes), given once at the first start and
  not kept in the compose file.
- **Settings to check:** hardware decoding and encoding on (VA-API in Jellyfin; "Use hardware acceleration when
  available" in Plex), tone mapping on, **subtitle burn-in for PGS** ("Burn subtitles: only image formats" in
  Jellyfin), "throttle transcodes" and "delete segments" on.

### Transcode scratch space

Transcodes write video segments continuously, and the NVMe is at **57% of its rated writes**. So the transcode folder
is a **tmpfs** (in memory), 1 GB to start, inside each container's memory cap; with throttling and segment deletion
on, a stream needs far less than that. It's sized up after the RAM upgrade if a 4K transcode needs it.

Metadata, artwork and Jellyfin's trickplay images (scrub-bar previews) **do** go on the NVMe. Trickplay is off at first:
generating it for a whole library is a lot of writes and CPU for a nicety.

### The photo job

A small job on the server that keeps a JPEG copy of every HEIC photo, for the media server to show:

- **Reads** `/srv/media/photos` (read-only), **writes** `/srv/photos-jpeg`, keeping the same folder layout and file
  names (`.jpg` for `.heic`). The photo library in Jellyfin and Plex points at **both**: the share for photos already in
  JPEG, the copies for the HEIC ones. HEIC originals are never added (they'd show as broken).
- **Converts** with `heif-convert` (libheif) or ImageMagick with libheif, both in Ubuntu; resizes to **1920 px on the
  long side**, quality 85. Roku's interface is 1920 × 1080 even on 4K TVs, so more is wasted on the NVMe.
- **Keeps the EXIF data** (date taken, so photos sort properly; orientation, so they aren't sideways), copying it
  across with `exiftool` if the converter drops it.
- **Skips Live Photo videos** (the `.MOV` with the same name as a HEIC): they'd show as short videos. The media
  libraries ignore `.MOV` files in the photo folders, or the job leaves them out.
- **Incremental:** converts only files that are new or newer than their copy, and deletes copies whose original has
  gone. Nightly, as a systemd timer, as user `media` (read access to the share, write access to the copies).
- **Size:** at about 0.5 MB a copy, 10,000 photos is about 5 GB. Count the HEIC files on the share before the first
  run (test step); if it doesn't fit comfortably in the 112 GB free, the copies wait for the hard drives.
- **Watched** like the other jobs: a healthchecks.io heartbeat each night, an alert on failure.
- **Where it lives:** a shell script and timer in `deploy/media-server/` to start with. If it grows (reports, retries,
  alerts with detail), a small .NET service under `src/` like the collectors.

### Backups

During the trial: **none**. The libraries rebuild from the files, and the JPEG copies from the originals. If the
winner stays: its config folder (watched status, users, settings) backed up nightly with the usual off-site copy,
heartbeat and Mac pull. Not the cache or metadata (rebuilt by a rescan).

## Steps

1. **RAM upgrade** (the DXP2800 GT's own step).
2. **PR: deploy files.** `deploy/media-server/` with the compose files (Jellyfin, Plex), `deploy-media-server.sh`
   (copy, validate, pull, start, check each answers), the photo job's script and timer, and `HOST-SETUP.md` (the
   `media` user, the read-only SMB account and mounts, the folders, the graphics group, the LAN ports);
   `docs/media-server/README.md`; the `jellyfin` block in the Caddyfile; the README's list of services.
3. **Old file server (by hand):** a read-only account with access to the films, TV and photo shares.
4. **Server (my commands):** the `media` user, the credentials file and mounts, the folders; check the mounts as
   `media`; `vainfo` in the Jellyfin container. Run the deploy script; redeploy Caddy.
5. **First run:** Jellyfin's admin account and household accounts, the libraries, the transcoding settings; Plex signed
   in with the claim token, the same libraries, Remote Access off. Passwords in 1Password.
6. **The photo job:** count the HEIC files, a dry run, then the first full run; add the copies to both photo libraries.
7. **The test set, on a Roku TV, in both:** each file played for ten minutes, with the server's dashboard showing
   whether the transcode is in hardware (Jellyfin: "(HW)" in the playback info; Plex: "(hw)" on the dashboard), the CPU
   use, and whether it keeps up (no buffering). Two streams at once. The photos: HEIC copies shown the right way up and
   in date order. Results in the private notes.
8. **A week or two of real use**, on the TVs.
9. **Decide:** keep one, remove the other (container, image, folders, LAN port). Then: backups for the winner, the
   old file server's Plex turned off, and `SERVER.md` updated.

## What decides it

- **Pass:** every PGS file in the test set plays on a Roku with a **hardware** transcode, without buffering, and two
  1080p streams at once keep up; HEIC photos show. 4K HDR with burn-in may be the one that struggles: acceptable if
  those films get SRT tracks instead.
- **Between the two:** if both pass, the household's preference for the Roku app decides. If only Jellyfin's hardware
  transcoding works, it's Jellyfin.
- **If neither can burn in 4K PGS in hardware:** SRT conversion for those films, and the server that handles the rest.

## Removing it

On the server: `sudo docker compose -f /opt/<server>/compose.yaml down --rmi all`, delete `/opt/<server>` and
`/var/lib/<server>`; the photo job's timer and `/srv/photos-jpeg`; the mounts (`/etc/fstab` lines, credentials file);
the `media` user; Caddy's `jellyfin` block. The old file server and its shares are never changed, apart from the
read-only account.

## Open questions

- **The LAN exception:** is a LAN-only port for the TVs acceptable, as above? (No alternative lets a Roku reach the
  server.)
- **The photo copies' size and place:** the NVMe, or the old file server (a writable share), until the drives go in?
  Decided by the HEIC count in step 6.
- **Script or .NET service** for the photo job.

## Sources

- [Jellyfin: AMD hardware acceleration](https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/amd)
- [Jellyfin: known hardware acceleration issues](https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/known-issues)
- [Jellyfin forum: subtitle burn-in on the Roku client](https://forum.jellyfin.org/t-subtitle-burn-in-on-roku-client)
- [Jellyfin forum: HEIF](https://forum.jellyfin.org/t-heif)
- [Plex forum: Linux + AMD Vega, no hardware acceleration](https://forums.plex.tv/t/pms-linux-amd-vega-no-hardware-acceleration/932042)
- [Plex forum: HEIC/HEIF support in photo libraries](https://forums.plex.tv/t/heic-heif-support-in-photo-libraries-new-photo-format-for-iphones/195514?page=11)
- [Emby: Roku support (image formats)](https://emby.media/support/articles/Roku.html)
