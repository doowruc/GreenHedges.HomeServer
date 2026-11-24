# Home Assistant: host setup

[Home Assistant](https://www.home-assistant.io/) (HA Container, the official image) in Docker, on trial: one container
on `127.0.0.1:8123`, at `https://ha.<home domain>` through Caddy, with the Hue bridge and the Tapo hub and devices added
by address. Why it's set up this way, and what the trial should show: [docs/home-assistant/PLAN.md](../../docs/home-assistant/PLAN.md).
The details: the comments in [`compose.yaml`](compose.yaml).

**First:** Docker (Libre collector's [HOST-SETUP §9](../libre/HOST-SETUP.md)) and Caddy
([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)). It needs about 300-600 MB of RAM (capped at 1 GB). The Hue bridge
and the Tapo hub and Wi-Fi devices need **fixed addresses**: DHCP reservations on the router (list them in the private
notes), since HA finds them by address, not by discovery.

## 1. Folders (on the server)

```bash
sudo install -d -m 0755 /opt/home-assistant && sudo install -d -m 0711 /var/lib/home-assistant && sudo install -d -m 0700 /var/lib/home-assistant/config
```

HA runs as root in the container, so its config folder is root's. There's no `/etc/home-assistant`: HA keeps its users,
sign-ins and tokens in its own `.storage/` folder, not in its environment.

## 2. Deploy (on the Mac)

```bash
deploy/home-assistant/deploy-home-assistant.sh
```

It pulls the image, starts the container (HA's first start takes a minute or two) and ends with its state,
`GET /manifest.json: HTTP 200` and `Not onboarded yet`. Then load the Caddyfile, which has the `ha` name:

```bash
deploy/caddy/deploy-caddy.sh
```

## 3. First run, in the browser

**Through an SSH tunnel, not Caddy, the first time.** Until HA is told to trust Caddy, it refuses any request that comes
through a proxy (HTTP 400). From the Mac:

```bash
ssh -N -L 8123:127.0.0.1:8123 "$COLLECTORS_SSH"
```

(or your usual `user@host`), then open <http://localhost:8123>. Whoever onboards first becomes the owner, so do it
straight after the deploy.

1. **Create my smart home:** your name, a username and password (save them in your password manager first, "Home server
   Home Assistant").
2. **Location:** the home location, time zone **Europe/London**, units **metric**, currency **GBP**. These stay in HA,
   not here.
3. **Analytics:** leave everything off.
4. **Devices found:** skip; integrations come in §5.

Then **let Caddy in**: **Settings → System → Network**, the HTTP server settings (since HA 2026.8 they're here, not in
`configuration.yaml`): turn on **use X-Forwarded-For** and add **`172.16.0.0/12`** as a **trusted proxy**. Caddy
reaches the container through Docker's port proxy, so requests come from the bridge gateway, in that range. Saving
restarts HA. Then close the tunnel (Ctrl+C) and open `https://ha.<home domain>` from a tailnet device: it should show
HA's sign-in.

## 4. Backups, in the browser

**Settings → System → Backups → Set up backups:**

- **Automatic:** daily, keep **7**, to **this system** (`/var/lib/home-assistant/config/backups`).
- **Include:** settings and history.
- **Encryption key:** HA makes one. Download the **emergency kit** and save it in your password manager ("Home server
  Home Assistant backup key"): **without it a backup can't be restored.**

During the trial that's all: no off-site copy, heartbeat or Mac pull. If HA stays, those come with a
`home-assistant-backup.timer`, as for FreshRSS (PLAN.md, "Backups").

## 5. Integrations, in the browser

**Settings → Devices & services → Add integration:**

- **Philips Hue:** enter the bridge's address (not the discovered one: there won't be any), then press the round button
  on the bridge when HA asks. The bulbs, rooms, scenes and switches come in. Switch and dimmer presses show as events,
  for automations.
- **TP-Link Smart Home:** enter the **H100's** address, then your TP-Link account (typed into HA's form; HA keeps it in
  `.storage/`). The hub's devices come in with it. Then the same again for each Tapo device on Wi-Fi (bulbs, plugs) by
  its address.

Two things to watch for (PLAN.md, "Known limits"): the S200B buttons come in without press events, and some newer H100
firmware is refused ("Unsupported device ... TPAP"). Note in the private notes what came in and what didn't.

## 6. The app

Install **Home Assistant** (the companion app) on the iPhone, and on the iPad or Mac if wanted. Server address
`https://ha.<home domain>` (enter it by hand: discovery won't find it), then sign in. It works wherever Tailscale is on
(VPN On Demand).

## 7. Check

**On the server:**

```bash
sudo docker compose -f /opt/home-assistant/compose.yaml ps && sudo docker stats --no-stream homeassistant-homeassistant-1
```

`healthy`, and its memory use. HA's own log is **Settings → System → Logs** (or `sudo docker compose -f
/opt/home-assistant/compose.yaml logs --since 1h homeassistant`).

**Disk writes** (the NVMe is at 57% of its rated writes, at about 16 GB a day before HA). Note the drive's total now and
again after a week:

```bash
sudo smartctl -A /dev/nvme0 | grep "Data Units Written" && sudo du -h /var/lib/home-assistant/config/home-assistant_v2.db
```

One data unit is 512,000 bytes. If HA adds a lot, keep fewer days of history (`recorder: purge_keep_days:` in
`configuration.yaml`, default 10) or leave the chattiest sensors out of the recorder.

## Upgrading

Pick a release from <https://github.com/home-assistant/core/releases> and read its notes' "Backward-incompatible
changes". Change the `image:` tag in `compose.yaml` and run `deploy/home-assistant/deploy-home-assistant.sh` again.
HA can make a backup first: **Settings → System → Backups → Backup now**.

## Restoring

From one of HA's backups (in `/var/lib/home-assistant/config/backups`, or one downloaded from **Settings → System →
Backups**): on a fresh install, at step 1 of §3 choose **Upload backup** (or pick one already in the backups folder)
instead of creating a new home, and give it the **encryption key** from the emergency kit.

## Removing it (if the trial says no)

On the server:

```bash
sudo docker compose -f /opt/home-assistant/compose.yaml down --rmi all && sudo rm -rf /opt/home-assistant /var/lib/home-assistant
```

Then take the `ha` block out of `deploy/caddy/Caddyfile` and redeploy Caddy, and take HA out of `SERVER.md`. Nothing in
the house needs undoing: the hubs, devices and Alexa are untouched.
