# Tailscale Services: one-off setup

The server's web apps are reached through one Tailscale Service, `svc:home`: a tailnet address of its own whose TCP
443 `tailscale serve` passes to Caddy, which serves each app at `https://<app>.<home domain>`
([caddy/HOST-SETUP.md](caddy/HOST-SETUP.md)). Only devices on the tailnet can reach it. Services is in public beta
(free on all plans for now).

A host must have a tag (not a user) to serve a Service. These steps make the server `tag:server` and let your devices
reach `svc:home`. Do them once.

## 1. Access controls (admin console)

**Access controls** → edit the policy file, and add (merging into any sections already there):

```jsonc
"tagOwners": {
  "tag:server": ["autogroup:admin"],
},
"grants": [
  // Your devices may reach the server's web apps (Caddy).
  { "src": ["autogroup:member"], "dst": ["svc:home"], "ip": ["443"] },
],
"autoApprovers": {
  "services": {
    "svc:home": ["tag:server"],
  },
},
```

## 2. Tag the server (admin console)

**Machines** → the server → **⋯** → **Edit tags** → add `tag:server` → **Save**. No re-authentication is needed. The
machine stops belonging to your user account (a device has tags or a user, not both); its key expiry stays off, and
SSH and everything else carry on.

## 3. Check (on the server)

```bash
tailscale status --self --json | grep -A2 '"Tags"'
```

It lists `tag:server`. The Service itself is defined in [caddy/HOST-SETUP.md](caddy/HOST-SETUP.md) §1.
