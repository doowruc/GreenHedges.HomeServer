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

## Backups

Nightly at 04:30 (`sure-backup.timer`): `pg_dump` into `/var/lib/sure/backups` (`sure-<time>.dump.gz`, 7 kept, checked
with `pg_restore --list`), an `age`-encrypted copy to the shared off-site bucket under `sure/`, and a healthchecks.io
heartbeat. The Mac pulls the local copies daily (09:55, 30 kept). Only the database: uploaded files aren't included.
Setup and restoring: [`deploy/sure/HOST-SETUP.md`](../../deploy/sure/HOST-SETUP.md). Personal financial data: treat the
backups like Pay & Pensions'.

## Still to do

- **Pin the image** to a release tag instead of `stable`.
