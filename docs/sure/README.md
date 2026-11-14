# Sure

[Sure](https://github.com/we-promise/sure) is an open-source, self-hosted personal finance app (accounts, transactions,
budgets, net worth). It is third-party software: it runs here as it comes, in Docker, and nothing in `src/` is ours.

- **Where:** `https://sure.<home domain>`, on the tailnet only, through Caddy (`deploy/caddy/`).
- **What runs:** `web` (Rails, `127.0.0.1:5084`), `worker` (Sidekiq), PostgreSQL 16 and Redis 7, from
  [`deploy/sure/compose.yaml`](../../deploy/sure/compose.yaml).
- **Setup:** [`deploy/sure/HOST-SETUP.md`](../../deploy/sure/HOST-SETUP.md). Update with `deploy/sure/deploy-sure.sh`.
- **Sign-in:** Sure's own accounts (no Auth0). The tailnet is the access boundary; open sign-up is turned off after the
  first account.
- **Data:** CSV imports only for now; no bank connections and no AI features (their keys are left unset).
- **Secrets:** `/etc/sure/sure.env` on the host (generated there, copy in the password manager). `SECRET_KEY_BASE` and the
  `ACTIVE_RECORD_ENCRYPTION_*` keys must never change after first use.

## Still to do

- **Backups:** a nightly `pg_dump` into `/var/lib/sure/backups` (7 kept), off-site `age`-encrypted copies to the shared
  bucket, the Mac's daily pull, and a healthchecks.io heartbeat, as for the other services. Personal financial data:
  treat it like Pay & Pensions.
- **Pin the image** to a release tag instead of `stable`.
