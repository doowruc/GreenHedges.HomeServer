# FreshRSS

[FreshRSS](https://github.com/FreshRSS/FreshRSS) is an open-source, self-hosted feed reader. It replaces a Feedly
subscription: it fetches the feeds around the clock, and ReadKit on the Mac and iPad reads and syncs through its API. It is
third-party software: it runs here as it comes, in Docker, and nothing in `src/` is ours.

- **Where:** `https://rss.<home domain>`, on the tailnet only, through Caddy (`deploy/caddy/`). ReadKit uses the same
  address, so each device needs Tailscale (VPN On Demand) to sync.
- **Version:** pinned to a release (see `image:` in the compose file); upgrade by editing it and redeploying.
- **What runs:** one container, `freshrss/freshrss` (Apache, PHP and cron), on `127.0.0.1:5085`, from
  [`deploy/freshrss/compose.yaml`](../../deploy/freshrss/compose.yaml). A refresh runs every 5 minutes and fetches each
  feed once its minimum time between refreshes has passed (set to 20 minutes, FreshRSS's lowest; its default is 1 hour),
  so a new post shows within about 20-25 minutes. Actualise follows the same rule; refreshing one feed from its own menu
  fetches it at once.
- **Data:** FreshRSS's built-in SQLite, one database per user, in `/var/lib/freshrss/data`. No database server.
- **Setup:** [`deploy/freshrss/HOST-SETUP.md`](../../deploy/freshrss/HOST-SETUP.md), including the move from Feedly
  (OPML) and ReadKit. Update with `deploy/freshrss/deploy-freshrss.sh`.
- **Sign-in:** FreshRSS's own account (no Auth0), with registration off. ReadKit uses a separate **API password** set in
  FreshRSS, not the web one. The tailnet is the access boundary.
- **Secrets:** none in the environment. The web and API password hashes are in FreshRSS's data folder; the passwords are
  in the password manager.
- **Alerts:** only for the backup. A broken feed shows in FreshRSS itself; nothing emails about it.

## Backups

Nightly at 04:47 (`freshrss-backup.timer`): FreshRSS's own consistent SQLite copy of each user's database, checked with
SQLite's integrity check and packed with the settings into `/var/lib/freshrss/backups` (`freshrss-<time>.tar.gz`, 7
kept), an `age`-encrypted copy to the shared off-site bucket under `freshrss/`, and a healthchecks.io heartbeat. The Mac
pulls the local copies daily (09:05, 30 kept). Setup and restoring:
[`deploy/freshrss/HOST-SETUP.md`](../../deploy/freshrss/HOST-SETUP.md).
