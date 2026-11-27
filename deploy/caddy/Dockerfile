# Caddy with the Cloudflare DNS plugin, for the wildcard certificate's DNS challenge.
# Built on the host by deploy-caddy.sh (docker compose build). To update: change both versions, then re-run it.
FROM caddy:2.11.9-builder AS builder
RUN xcaddy build v2.11.9 --with github.com/caddy-dns/cloudflare@v0.2.4

FROM caddy:2.11.9
COPY --from=builder /usr/bin/caddy /usr/bin/caddy
