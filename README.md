# GreenHedges.HomeServer

The services that run on a small always-on home server, in one repository and one .NET solution. Each deploys and runs as its own service.

| Service | What | Status |
|---|---|---|
| **Libre collector** ([docs](docs/libre/README.md)) | Keeps a complete, permanent archive of FreeStyle Libre glucose readings, fetched once a minute through LibreLinkUp, with Nightscout dashboards and Apple Health sync | Running |
| **Weather collector** ([docs](docs/weather/README.md)) | Archives every radio message from a home weather station's outdoor sensor, received with an RTL-SDR dongle and [rtl_433](https://github.com/merbanan/rtl_433), with Grafana dashboards | Archiving, with alerts, heartbeat and backups (Phase 1); dashboards next |
| **Vehicle Manager** ([docs](docs/vehicle-manager/README.md)) | A web app for the vehicles I own: fuel log and MPG, services, MOT history from the DVSA, due-date reminders | Running on the home server (moved from Azure SQL: [plan](docs/vehicle-manager/HOMESERVER-MIGRATION.md)) |

The **collectors** keep my own copy of data that otherwise only lives in someone else's cloud. They share a .NET 10 Worker Service under systemd, SQLite archives that keep the data as received, nightly backups pulled off the host, encrypted off-site copies, email alerts and an external heartbeat. **Vehicle Manager** is an ASP.NET Core Razor Pages app on SQL Server. Everything is reached over Tailscale only.

## Layout

| Path | What |
|---|---|
| `GreenHedges.HomeServer.slnx` | The one solution |
| `src/Common/`, `tests/Common/` | Shared by every service (`GreenHedges.HomeServer.Common`): host setup, email alerts, heartbeat, backups and off-site copies |
| `src/Collectors/Libre/`, `tests/Collectors/Libre/` | The Libre collector (`GreenHedges.HomeServer.Collectors.Libre.*`) |
| `src/Collectors/Weather/`, `tests/Collectors/Weather/` | The Weather collector (`GreenHedges.HomeServer.Collectors.Weather.*`) |
| `src/VehicleManager/`, `tests/VehicleManager/` | Vehicle Manager (`GreenHedges.HomeServer.VehicleManager`, `.AppHost` for local development, `.Tests`) |
| `docs/vehicle-manager/` | Its README, backlog and the plan for moving it onto the home server |
| `compose.yaml`, `.env.example` | SQL Server in Docker for local development and Vehicle Manager's tests |
| `deploy/sqlserver/` | SQL Server in Docker on the home server, for its web apps: compose file, deploy script, host setup |
| `deploy/vehicle-manager/` | Vehicle Manager's systemd unit, deploy script, database setup and host setup |
| `deploy/TAILSCALE-SERVICES.md` | One-off Tailscale setup: the server's tag and access to `svc:home`, the tailnet address of the web apps |
| `deploy/caddy/` | Caddy, the web apps' front door: `https://<app>.<home domain>` on the tailnet, one wildcard certificate |
| `deploy/deploy.sh` | Publishes a service and installs it on the host (`deploy/<service>/deploy.sh` for short) |
| `deploy/libre/` | The Libre collector's systemd unit, deploy script, host setup and Nightscout stack |
| `docs/libre/` | Its README (setup and use) and design notes |
| `deploy/weather/` | The Weather collector's systemd unit, deploy script and host setup |
| `docs/weather/` | Its README (day to day) and design notes |
| `deploy/mac/` | The Mac's daily backup pull, for either collector |
| `deploy/aws/` | CloudFormation for the shared off-site backup bucket and its write-only users |
| `deploy/local.env.example` | Personal defaults for the deploy scripts (copy to the git-ignored `deploy/local.env`) |

Building, testing and the pull-request workflow: [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[MIT](LICENSE)

---

Designed and built with [Claude Code](https://claude.com/claude-code).
