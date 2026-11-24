# Home Assistant: the trial

The plan for trying [Home Assistant](https://www.home-assistant.io/) (HA) on the home server for a few weeks, to see
whether one app and dashboard for the house's smart devices, with automations across brands, is worth one more service
to look after. Nothing in the house changes during the trial: the devices stay on their own hubs and Alexa keeps
working as it does now. If it isn't worth it, removing it is one container and three folders.

**Status:** planned, nothing built yet (2026-11-22).

## Why, and what it should show

Today each brand has its own app (Hue, Tapo, Eufy) and Alexa ties them together by voice. The trial should answer:

- **One app:** do I reach for HA's app and dashboard instead of the brand apps, day to day?
- **Automations:** are there automations worth having that Alexa routines can't do (conditions, state, time windows,
  one brand driving another)?
- **Cost:** memory, disk writes and upkeep (HA releases monthly) on the server.

## Scope

**In:**

- **HA Container** (the official Docker image) on the server, at `https://ha.<home domain>` on the tailnet, through
  Caddy, like the other web apps.
- **Philips Hue**, through the existing Hue Bridge (v2, still supported): HA's built-in Hue integration, local, with
  switch and dimmer presses pushed to HA at once.
- **Tapo**, through the existing H100 hub and the devices on Wi-Fi: HA's built-in TP-Link Smart Home integration,
  local after signing in once with the TP-Link account.
- **The HA app** on the iPhone (and Mac or iPad if wanted), over Tailscale.

**Out, for now** (each is its own decision if the trial goes well):

- **The ZBT-1 Zigbee dongle**: not needed while the Hue bridge is supported. No USB passthrough, no `privileged`.
- **Eufy** doorbell and HomeBase: community integration only (HACS plus an `eufy-security-ws` container), cloud
  dependent and the most fragile part.
- **Alexa through HA** (Nabu Casa, or my own skill and Lambda): Alexa keeps its own Hue and Tapo skills.
- **Matter**, Bluetooth and anything that needs HA's network discovery.
- **Off-site backups, heartbeat and the Mac's pull**: HA's own local backups only (see "Backups").

## Known limits to check in the trial

- **Tapo buttons (S200B):** HA's docs say the built-in integration doesn't report presses of hub-connected buttons:
  they show up, but only with connection entities. So "a Tapo button drives Hue lights" may not work through the
  built-in integration. Alternatives, after the trial: the H100 as a **Matter bridge** into HA (needs a Matter Server
  container and host networking), or a community integration.
- **H100 firmware:** some newer H100 firmware uses an encryption scheme the built-in integration refuses ("Unsupported
  device ... TPAP"). Check whether ours sets up. If not, Tapo waits for a fix or a community integration, and the
  trial carries on with Hue.
- **Disk writes:** HA's recorder writes every state change to SQLite (`home-assistant_v2.db`), and the NVMe is at 57%
  of its rated writes. Watch the writes during the trial (`sysstat` already collects them) and trim the recorder if
  they're high.

## Design

### Bridge network, not host network

HA's install guide uses **host networking** for device discovery (mDNS, SSDP, DHCP). The trial doesn't need it:

- **Hue:** HA connects to the bridge (by its address) and keeps an event stream open to it. All connections go out
  from HA.
- **Tapo:** HA connects to each device or hub by its address. Also outgoing.
- **Discovery wouldn't work anyway:** `ufw` blocks incoming multicast replies, and I'm not opening LAN ports.

So HA runs like FreshRSS: its own bridge network, only `127.0.0.1:8123` published, and devices added **by address**.
That keeps the house's devices on fixed addresses (DHCP reservations on the router, listed in the private notes).
Host networking comes back only with Matter or Bluetooth.

### The container

- **Image:** `ghcr.io/home-assistant/home-assistant`, pinned to a release (`image:` in the compose file). Upgrading is
  an edit there and a redeploy, after reading the release notes (breaking changes are listed for each release).
- **No `privileged`**, no devices, `no-new-privileges`. The official image runs HA as root inside the container; with
  no host networking, devices or `privileged` that is contained like the other Docker services. (The linuxserver image
  can run as a non-root user, but the official image is what HA supports.)
- **Memory:** HA uses about 300-600 MB; capped at **1 GB** (3.6 GiB was available with everything running,
  2026-11-20).
- **Health check:** HA's front page on `127.0.0.1:8123`.
- **Time zone:** `TZ=Europe/London`.

### Folders

| Where | What |
|---|---|
| `/opt/home-assistant` | `compose.yaml` (root) |
| `/var/lib/home-assistant/config` | HA's `/config`: `configuration.yaml`, `.storage/` (integrations, users, credentials such as the TP-Link sign-in), the recorder database, `backups/`. Root 0700 (the container runs as root) |

No `/etc/home-assistant`: HA has no secrets in its environment. Sign-ins and tokens live in `.storage/`, which is why
the backups are encrypted.

### Access

- **Caddy:** a block for `ha` → `127.0.0.1:8123` in `deploy/caddy/Caddyfile`. HA's WebSockets pass through as they
  are, as for Nightscout.
- **HA's HTTP settings** are in the UI since 2026.8 (**Settings → System → Network**; the YAML `http:` block stops
  working in 2027.2). Trust `X-Forwarded-For` from the Docker bridge range, `172.16.0.0/12`, as FreshRSS does: Caddy
  reaches the container through Docker's port proxy. Without it HA refuses proxied requests.
- **Sign-in:** HA's own accounts. The owner account is made in HA's first-run screen. The tailnet is the access
  boundary, as for the other apps.
- **The app:** the HA app's server address is `https://ha.<home domain>`, so the phone needs Tailscale (VPN On Demand
  is already on).

### Backups

For the trial, HA's own **automatic backups**: nightly, a few kept, in `/var/lib/home-assistant/config/backups`,
**encrypted** with HA's backup key. Save the key (HA's "emergency kit") in 1Password when backups are set up: without
it a backup can't be restored. Nothing here is hard to rebuild during the trial (two integrations and some
automations), so there's no off-site copy, heartbeat or Mac pull yet.

**If HA stays:** the usual set, like FreshRSS: a `home-assistant-backup.timer` that copies the newest HA backup
(already encrypted) off-site, an IAM user in the off-site stack, a healthchecks.io check and the Mac's pull.

## Steps

1. **PR: the deploy files.** `deploy/home-assistant/` with `compose.yaml`, `deploy-home-assistant.sh` (copy, validate,
   pull, start, check `127.0.0.1:8123` answers; in the style of `deploy-freshrss.sh`) and `HOST-SETUP.md`;
   `docs/home-assistant/README.md`; the `ha` block in the Caddyfile; the README's list of services.
2. **Server (my commands):** create `/opt/home-assistant` and `/var/lib/home-assistant/config`; run
   `deploy-home-assistant.sh`; run `deploy-caddy.sh`.
3. **First run, in the browser:** owner account (password in 1Password); home location, units and time zone; the HTTP
   proxy settings; automatic backups and their key in 1Password.
4. **Integrations:** Hue (by the bridge's address; press the bridge's button to pair); TP-Link (the H100 and each Wi-Fi
   device by address, with the TP-Link account typed into HA's form). Note in the private notes which devices came in
   and which didn't.
5. **The app** on the iPhone, a dashboard and a first few automations.
6. **Notes:** `SERVER.md` (port 8123, folders, the service row, history) and the device addresses in the private notes.
7. **After about three weeks: decide.** Keep it (then backups as above, and the next stage: Tapo buttons, Eufy, Alexa
   or the ZBT-1) or remove it.

## Removing it

On the server: `sudo docker compose -f /opt/home-assistant/compose.yaml down --rmi all`, then delete
`/opt/home-assistant` and `/var/lib/home-assistant`; take the `ha` block out of the Caddyfile and redeploy Caddy; take
it out of `SERVER.md`. Nothing in the house needs undoing: the hubs, devices and Alexa were never changed.

## Decided, and still open

**Decided:**

- HA Container, not HA OS (HA OS wants the whole machine).
- Hue stays on its bridge; the ZBT-1 waits.
- Alexa unchanged.

**Open (before step 1):**

- **Name:** `ha.<home domain>`, or something else?
- **Trial backups:** HA's own only (as above), or the full set from the start?
