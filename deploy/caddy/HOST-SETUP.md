# Caddy: host setup

Caddy publishes the server's web apps at `https://<app>.<home domain>` (e.g. `vehicles.home.example.com`), tailnet
only, with one Let's Encrypt wildcard certificate. How it fits together: the comments in
[`compose.yaml`](compose.yaml) and [`Caddyfile`](Caddyfile). `<home domain>` is the name the apps go under; its zone is
on Cloudflare.

**First:** Docker (Libre collector's [HOST-SETUP §9](../libre/HOST-SETUP.md)), and the server tagged `tag:server`
([deploy/TAILSCALE-SERVICES.md](../TAILSCALE-SERVICES.md) §2).

## 1. Tailscale Service `svc:home` (admin console)

1. **Access controls:** add `svc:home` to the policy's grant and `autoApprovers`
   ([TAILSCALE-SERVICES.md](../TAILSCALE-SERVICES.md) §1).
2. **Services** → **Advertise** → **Define a Service**: name `home`, description "Web apps (Caddy)", endpoint
   `tcp:443`. **Add service**.
3. Open the new Service and note its **IPv4** (`100.…`) and **IPv6** (`fd7a:…`) addresses.

## 2. Cloudflare (in a browser)

**DNS records:** the zone → **DNS** → **Records** → **Add record**, twice, both **DNS only** (grey cloud):

| Type | Name | Content |
|---|---|---|
| `A` | `*.home` (the part of `<home domain>` before the zone, plus `*.`) | `svc:home`'s IPv4 |
| `AAAA` | the same | `svc:home`'s IPv6 |

**API token:** **My Profile** → **API Tokens** → **Create Token** → **Edit zone DNS** template → **Zone Resources**:
*Include*, *Specific zone*, the zone → **Continue to summary** → **Create Token**. Leave the page open for §3.

## 3. Folders and settings (on the server)

```bash
sudo install -d -m 0755 /opt/caddy && sudo install -d -m 0750 /etc/caddy && sudo install -d -m 0700 /var/lib/caddy /var/lib/caddy/data /var/lib/caddy/config && sudo install -m 0600 /dev/null /etc/caddy/caddy.env
```

```bash
sudoedit /etc/caddy/caddy.env
```

Paste the token straight from Cloudflare's page, so the file has ([caddy.env.example](caddy.env.example)):

```
HOME_DOMAIN=<home domain>
ACME_EMAIL=<your email>
CLOUDFLARE_API_TOKEN=<token>
```

Save the token in your password manager too ("Cloudflare: Caddy DNS token"), then close Cloudflare's page.

## 4. Deploy (on the Mac)

```bash
deploy/caddy/deploy-caddy.sh
```

It ends with the container `healthy` and Caddy's log. The certificate takes a minute or two (the DNS challenge); on the
server, wait for `certificate obtained successfully` for `*.<home domain>`:

```bash
sudo docker compose -f /opt/caddy/compose.yaml logs caddy | grep -iE 'obtain|error'
```

## 5. Publish it on the tailnet (on the server)

```bash
sudo tailscale serve --service=svc:home --tcp=443 --proxy-protocol=2 tcp://127.0.0.1:8443
```

```bash
tailscale serve status
```

## 6. Check (on the Mac, Tailscale on)

```bash
curl -s https://vehicles.<home domain>/health; echo
```

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://nightscout.<home domain>/
```

Expect `Healthy` and `200`. Then, on the server, the access log shows the Mac's tailnet address (`100.…`), not
`127.0.0.1`:

```bash
sudo docker compose -f /opt/caddy/compose.yaml logs --tail 4 caddy
```

## 7. Move the apps across (browser and phone)

- **Auth0** (the Vehicle Manager application's settings): add `https://vehicles.<home domain>/callback` to **Allowed
  Callback URLs** and `https://vehicles.<home domain>/Account/SignedOut` to **Allowed Logout URLs**. Save.
- **Vehicle Manager:** open `https://vehicles.<home domain>` on the Mac and the iPhone, sign in; on the iPhone, add it
  to the Home Screen again and remove the old icon.
- **Nightguard** (iPhone): change the Nightscout URL to `https://nightscout.<home domain>`. Update any bookmarks.

## 8. Retire the old addresses

**On the server:**

```bash
sudo tailscale serve clear svc:vehicles
```

```bash
sudo tailscale serve --https=443 off
```

```bash
tailscale serve status
```

Only `svc:home` is left. **In the Tailscale admin console:** **Services** → `vehicles` → delete; remove `svc:vehicles`
from the policy's grant and `autoApprovers`. **In Auth0:** remove the two `…ts.net` URLs.

## Adding an app

Give it a `@<name> host <name>.{$HOME_DOMAIN}` matcher and `handle` block in the [Caddyfile](Caddyfile), then re-run
`deploy/caddy/deploy-caddy.sh`. No DNS, certificate or Tailscale change.

## Updating

Change both versions in the [Dockerfile](Dockerfile) (and the image tag in `compose.yaml`) in a PR, then re-run
`deploy/caddy/deploy-caddy.sh`.
