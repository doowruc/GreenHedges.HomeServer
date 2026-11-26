# Media server: host setup

The media server trial: **Jellyfin** and **Plex** side by side in Docker, reading the old file server's films, TV and
photo shares (mounted read-only on the host), transcoding on the graphics chip, and published on the **LAN** for the
Roku TVs as well as through Caddy for the tailnet. Plus the **photo job**, which keeps a JPEG copy of the photo share
sized for TVs (no server shows HEIC on Roku). Why it's set up this way, and what the trial should show:
[docs/media-server/PLAN.md](../../docs/media-server/PLAN.md). The details: the comments in
[`jellyfin/compose.yaml`](jellyfin/compose.yaml), [`plex/compose.yaml`](plex/compose.yaml) and
[`photos/media-photos.sh`](photos/media-photos.sh).

**First:** Docker (Libre collector's [HOST-SETUP §9](../libre/HOST-SETUP.md)), Caddy
([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)) and the **RAM upgrade**: each server is capped at 2 GB, the
photo job at 1.5 GB.

| Where | What |
|---|---|
| `/srv/media/{films,tv,photos}` | The old file server's shares, read-only CIFS automounts, owned by `media` |
| `/srv/photos-jpeg` | The photo job's copies (`media`) |
| `/etc/media-server/` | `compose.env` (LAN address and ids, for both servers), `smb-credentials`, `photos.env` |
| `/opt/jellyfin`, `/var/lib/jellyfin/{config,cache}` | Jellyfin's compose file; its settings, library database and caches (`media`) |
| `/opt/plex`, `/var/lib/plex/config` | Plex's compose file; its whole state (`media`) |
| `/opt/media-server/media-photos.sh` | The photo job, run by `media-photos.timer` |

## 1. The media user and folders (on the server)

A system user that runs both servers and the photo job, and owns everything they write. You join its group, to read
the copies and the servers' folders without sudo.

```bash
sudo adduser --system --group --no-create-home --home /nonexistent media && sudo usermod -aG media "$USER"
```

```bash
sudo install -d -m 0755 /srv/media /srv/media/films /srv/media/tv /srv/media/photos /etc/media-server && sudo install -d -m 0775 -o media -g media /srv/photos-jpeg && sudo install -d -m 0750 -o media -g media /var/lib/jellyfin /var/lib/jellyfin/config /var/lib/jellyfin/cache /var/lib/plex /var/lib/plex/config
```

## 2. Packages (on the server)

The photo job's tools: libheif's `heif-convert`, ImageMagick and `exiftool`. `cifs-utils` is likely there already
(the books copy used it).

```bash
sudo apt install cifs-utils libheif-examples imagemagick libimage-exiftool-perl
```

## 3. The shares

**On the old file server:** a user that may only **read** the films, TV and photo shares (no write access anywhere),
with a long random password, saved in your password manager ("Old file server media reader").

**On the server:** its credentials, in a root-only file. Create it empty, then type the password in yourself:

```bash
sudo install -m 0600 /dev/null /etc/media-server/smb-credentials && sudoedit /etc/media-server/smb-credentials
```

```text
username=<the reader user>
password=<its password>
```

Then one line per share in `/etc/fstab`, with the old file server's name and the share names from the private notes:

```bash
sudoedit /etc/fstab
```

```text
//<file server>/<films share>  /srv/media/films   cifs  ro,vers=2.1,credentials=/etc/media-server/smb-credentials,uid=media,gid=media,file_mode=0444,dir_mode=0555,iocharset=utf8,nofail,_netdev,x-systemd.automount,x-systemd.mount-timeout=30  0  0
//<file server>/<TV share>     /srv/media/tv      cifs  ro,vers=2.1,credentials=/etc/media-server/smb-credentials,uid=media,gid=media,file_mode=0444,dir_mode=0555,iocharset=utf8,nofail,_netdev,x-systemd.automount,x-systemd.mount-timeout=30  0  0
//<file server>/<photo share>  /srv/media/photos  cifs  ro,vers=2.1,credentials=/etc/media-server/smb-credentials,uid=media,gid=media,file_mode=0444,dir_mode=0555,iocharset=utf8,nofail,_netdev,x-systemd.automount,x-systemd.mount-timeout=30  0  0
```

- **`ro`**, and a reader account: nothing here can change the files.
- **`x-systemd.automount`**: mounted the first time something looks, so a boot with the old file server off neither
  hangs nor leaves the mount missing for good. The containers see it through `rslave` (compose files).
- **`vers=2.1`**: the newest the old file server offers.

Load them and look:

```bash
sudo systemctl daemon-reload && sudo systemctl restart remote-fs.target && sudo -u media ls /srv/media/films /srv/media/tv /srv/media/photos | head -20 && findmnt -t cifs
```

The three shares listed as `media`, and `findmnt` showing them `ro`.

**Later, the hard drives:** mount their datasets at the same `/srv/media/...` paths (and take these lines out), and the
libraries carry on without a rescan.

## 4. The servers' settings file

Both servers read the same small file: the LAN address they publish on, and the ids they run as. It's written from the
host's own values (check the interface name with `ip -br addr` if it isn't `enp1s0`):

```bash
printf 'LAN_ADDRESS=%s\nMEDIA_UID=%s\nMEDIA_GID=%s\nRENDER_GID=%s\nPLEX_CLAIM=\n' "$(ip -4 -o addr show enp1s0 | awk '{print $4}' | cut -d/ -f1)" "$(id -u media)" "$(id -g media)" "$(getent group render | cut -d: -f3)" | sudo tee /etc/media-server/compose.env && sudo chmod 0644 /etc/media-server/compose.env
```

Check it shows the LAN address and three numbers. The LAN address must be a **DHCP reservation** on the router (it
already is: everything here uses it).

## 5. Jellyfin

**Deploy (on the Mac):**

```bash
deploy/media-server/deploy-media-server.sh jellyfin
```

It ends with the container `healthy`, `GET /health: HTTP 200`, and the graphics chip's driver with its H.264 and HEVC
profiles (`VLD` decode, `EncSlice` encode). No profiles listed: stop and check `/dev/dri/renderD128` and the render
group.

**First run, from the Mac on the LAN** (not over Tailscale: the LAN port is open, so no tunnel is needed), straight
after the deploy, as whoever gets there first becomes the administrator: `http://<server LAN name>:8096`.

1. **Language** English (UK); **administrator** account (save it in your password manager first, "Home server
   Jellyfin").
2. **Libraries** (Jellyfin's paths, not the host's):
   - **Films:** content type Movies, folder `/media/films`.
   - **TV:** content type Shows, folder `/media/tv`.
   - **Photos:** content type Photos, folder `/photos` (the photo job's copies, §7; empty until it has run). Not
     `/media/photos`: the originals have HEIC and Live Photo videos.
   - For each: metadata language English, country United Kingdom; **real-time monitoring off** (SMB shares don't
     report changes: the scheduled scan picks them up); **trickplay images off** (lots of writes and CPU for scrub-bar
     previews).
3. **Remote access:** leave "Allow remote connections" **on** (the tailnet comes through Caddy, which Jellyfin counts
   as remote); automatic port mapping (UPnP) **off**. Nothing is published to the internet either way.

Then, as administrator, **Dashboard**:

- **Playback → Transcoding:**
  - Hardware acceleration **Video Acceleration API (VAAPI)**, device `/dev/dri/renderD128`.
  - Hardware decoding for **H264, HEVC, HEVC 10bit, VP9, VP9 10bit** (not AV1: the chip can't).
  - Hardware encoding **on**. HEVC encoding **off** to start (H.264 suits every Roku).
  - **Tone mapping on** (HDR films with burnt-in subtitles come out in SDR).
  - **Throttle transcodes on**, **delete segments on**.
  - Transcode path: empty, or `/cache/transcodes` (the memory-backed folder). Anything else writes to the NVMe.
- **Networking:** Known proxies: the Jellyfin network's gateway, from
  `sudo docker network inspect jellyfin_jellyfin_net --format '{{(index .IPAM.Config 0).Gateway}}'` (Caddy's requests
  arrive from it). LAN networks: the LAN's range (from the private notes), so the TVs count as local.
- **Users:** an account for each person in the house (passwords in your password manager), not administrators.

**Over the tailnet:** the Caddyfile's `jellyfin` name (§8), `https://jellyfin.<home domain>`, for the phone, iPad and
Mac apps.

**The Roku TVs:** add the **Jellyfin** channel, server `http://<server LAN address>:8096` (the address, in case a TV
can't resolve the LAN name), and sign in.

## 6. Plex

**The claim token** ties the new server to the Plex account and lasts four minutes, so: get one from
<https://plex.tv/claim> (signed in), put it after `PLEX_CLAIM=` in `/etc/media-server/compose.env`, and deploy straight
away:

```bash
sudoedit /etc/media-server/compose.env
```

```bash
deploy/media-server/deploy-media-server.sh plex
```

Once the deploy says `healthy` and `GET /identity: HTTP 200`, **empty `PLEX_CLAIM=`** again (the deploy reminds you).
No restart is needed: the token is only read at the first start, and the next deploy recreates the container without
it.

**Does the plex user get the graphics chip?** The image runs Plex as its own `plex` user (with the media user's ids),
which needs the render group:

```bash
sudo docker compose -f /opt/plex/compose.yaml exec plex id plex
```

The render group's number (from `compose.env`) should be in the list. If it isn't, hardware transcoding can't work:
note it and stop there, as it needs a change to the compose file.

**First run, from the Mac on the LAN:** `http://<server LAN address>:32400/web`, signed in to the Plex account. Give
the server a name (not the old file server's), then the libraries: **Movies** `/media/films`, **TV Shows**
`/media/tv`, **Photos** `/photos`. Then **Settings**, with **Show Advanced** on:

- **Remote Access: disabled.** Nothing is published to the internet.
- **Network:** Custom server access URLs `https://plex.<home domain>:443` (the tailnet, through Caddy, §8); Secure
  connections **Preferred**; no IP addresses allowed without auth.
- **Transcoder:** Use hardware acceleration when available **on**, hardware-accelerated video encoding **on**, HDR
  tone mapping **on**; Transcoder temporary directory `/transcode` (the memory-backed folder).
- **Library:** Scan my library periodically **on** (daily); Run a partial scan when changes are detected **off** (SMB
  doesn't report changes).
- **General / privacy:** turn off what you don't want sent to Plex (crash reports, usage data).

**The Roku TVs:** the **Plex** channel, signed in to the Plex account: the new server shows beside the old one. Pick it
by name.

**The old file server's Plex** stays until the trial is decided (PLAN.md, step 9): don't remove its libraries yet.

## 7. The photo job

A healthchecks.io check, "Media photos", period **1 day**, grace **6 hours** (the first run takes hours). Then its
settings file, with the ping URL typed in yourself:

```bash
sudo install -m 0600 /dev/null /etc/media-server/photos.env && sudoedit /etc/media-server/photos.env
```

```text
HEARTBEAT_PING_URL=https://hc-ping.com/<uuid>
```

(See [`photos/photos.env.example`](photos/photos.env.example) for the optional settings.) Install it (on the Mac):

```bash
deploy/media-server/deploy-media-server.sh photos
```

That enables the nightly timer (01:30) but doesn't run it. **First, a dry run**, which only counts:

```bash
sudo -u media /opt/media-server/media-photos.sh --dry-run
```

`<n> photos on the share, 0 copies; <n> to convert, 0 to delete`. At about 0.3-0.6 MB a copy, check `<n>` fits on the
NVMe with room to spare (`df -h /`): about 10,000 photos is about 5 GB. If it doesn't, stop: the copies wait for the
hard drives (PLAN.md, "Open questions"). Note the count in the private notes.

**Then the first run**, now rather than at 01:30 (it carries on if you log out):

```bash
sudo systemctl start --no-block media-photos.service && journalctl -u media-photos -f
```

Ctrl+C stops following, not the job. When it's done: `systemctl status media-photos` (`status=0/SUCCESS`) and the
check green. Once the photo libraries have scanned, look at a few photos you know on a TV: the right way up, in date
order, HEIC ones included. After that it runs nightly, converting only new and changed photos.

**A photo that won't convert** (a damaged file) doesn't stop the others, but fails the run, and so every night's run
until it's fixed or taken off the share: `journalctl -u media-photos` names it (`Could not convert …`).

**If a run says "would delete … copies":** the share was probably empty or half-mounted. Check `ls /srv/media/photos`;
if photos really were deleted, run it once with `PHOTOS_MAX_DELETE_PERCENT=100` set
(`sudo -u media env PHOTOS_MAX_DELETE_PERCENT=100 /opt/media-server/media-photos.sh`).

## 8. Caddy

The Caddyfile has the `jellyfin` and `plex` names. Load it (on the Mac):

```bash
deploy/caddy/deploy-caddy.sh
```

Then from a tailnet device: `https://jellyfin.<home domain>` shows Jellyfin's sign-in, `https://plex.<home domain>/web`
Plex's.

## 9. Check

**Both running, and their memory:**

```bash
sudo docker compose -f /opt/jellyfin/compose.yaml ps && sudo docker compose -f /opt/plex/compose.yaml ps && sudo docker stats --no-stream jellyfin-jellyfin-1 plex-plex-1
```

**The LAN ports, on the LAN address only** (plus `127.0.0.1` for Caddy):

```bash
sudo ss -tlnp | grep -E ':(8096|32400)\b'
```

**A transcode in hardware:** play a film with a PGS track on a Roku TV, with the subtitles on. Jellyfin: Dashboard →
Active devices → the stream's info shows the transcode reason (subtitle) and the video codecs; the log line for it
(`sudo docker compose -f /opt/jellyfin/compose.yaml logs --since 5m | grep -i vaapi`) shows VA-API. Plex: Dashboard
shows the stream as `Transcode (hw)`. And the graphics chip busy, the CPU not:

```bash
cat /sys/class/drm/card1/device/gpu_busy_percent && top -bn1 | head -15
```

The test set (PLAN.md, "Before it starts" and step 7) and its results go in the private notes.

## Upgrading

- **Jellyfin:** pick a release from <https://github.com/jellyfin/jellyfin/releases> and read its notes; change the
  `image:` tag in `jellyfin/compose.yaml` (the full tag from Docker Hub) and run
  `deploy/media-server/deploy-media-server.sh jellyfin`.
- **Plex:** pick a tag from <https://hub.docker.com/r/plexinc/pms-docker/tags>, change `plex/compose.yaml`, run
  `deploy/media-server/deploy-media-server.sh plex`.
- **The photo job:** `deploy/media-server/deploy-media-server.sh photos` after a change.

## Removing one (when the trial decides)

The loser, on the server (`jellyfin` or `plex`):

```bash
sudo docker compose -f /opt/plex/compose.yaml down --rmi all && sudo rm -rf /opt/plex /var/lib/plex
```

Then its block out of `deploy/caddy/Caddyfile` and Caddy redeployed, its compose file out of the repository, and
`SERVER.md` updated. Its LAN port goes with the container.

**Everything** (if neither stays): both servers as above; then
`sudo systemctl disable --now media-photos.timer && sudo rm /etc/systemd/system/media-photos.{service,timer} && sudo systemctl daemon-reload`,
`sudo rm -rf /opt/media-server /srv/photos-jpeg /etc/media-server`, the three `/etc/fstab` lines (then
`sudo systemctl daemon-reload` and a reboot, which leaves the shares unmounted), `sudo rmdir /srv/media/* /srv/media` and
`sudo deluser media`.
The old file server and its shares are untouched, apart from the reader account.
