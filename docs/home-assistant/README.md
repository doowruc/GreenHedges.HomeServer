# Home Assistant

[Home Assistant](https://www.home-assistant.io/) (HA) is open-source home automation: one app and dashboard for the
house's smart devices, with automations across brands. **On trial** (see [PLAN.md](PLAN.md)): it runs alongside the
devices' own hubs and Alexa, which are unchanged. It is third-party software: it runs here as it comes, in Docker, and
nothing in `src/` is ours.

- **Where:** `https://ha.<home domain>`, on the tailnet only, through Caddy (`deploy/caddy/`). The companion app uses
  the same address, so the phone needs Tailscale (VPN On Demand).
- **Version:** pinned to a release (see `image:` in the compose file); upgrade by editing it and redeploying.
- **What runs:** one container, the official `ghcr.io/home-assistant/home-assistant` image (HA Container), on
  `127.0.0.1:8123`, from [`deploy/home-assistant/compose.yaml`](../../deploy/home-assistant/compose.yaml). Its own
  bridge network, not the host's: no discovery, so devices are added by address.
- **Devices:** Philips Hue through the Hue bridge, and Tapo through the H100 hub and on Wi-Fi, both local. Not (yet):
  the ZBT-1 Zigbee dongle, Eufy, Matter, Bluetooth, or Alexa through HA.
- **Data:** everything in `/var/lib/home-assistant/config`: settings, `.storage/` (integrations, users, sign-ins), and
  the recorder's history in SQLite.
- **Setup:** [`deploy/home-assistant/HOST-SETUP.md`](../../deploy/home-assistant/HOST-SETUP.md). Update with
  `deploy/home-assistant/deploy-home-assistant.sh`.
- **Sign-in:** HA's own accounts. The tailnet is the access boundary.
- **Secrets:** none in the environment. The TP-Link sign-in and HA's tokens are in `.storage/`; the passwords and the
  backup key are in the password manager.

## Backups

During the trial, HA's own: daily, 7 kept, encrypted with HA's backup key, in
`/var/lib/home-assistant/config/backups`. No off-site copy, heartbeat or Mac pull yet; those come if HA stays.
