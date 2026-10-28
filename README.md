# GreenHedges.HomeServer

The services that run on a small always-on home server, in one repository and one .NET solution. Each deploys and runs as its own service.

| Service | What | Status |
|---|---|---|
| **Libre collector** ([docs](docs/libre/README.md)) | Keeps a complete, permanent archive of FreeStyle Libre glucose readings, fetched once a minute through LibreLinkUp, with Nightscout dashboards and Apple Health sync | Running |
| **Weather collector** ([docs](docs/weather/README.md)) | Archives every radio message from a home weather station's outdoor sensor, received with an RTL-SDR dongle and [rtl_433](https://github.com/merbanan/rtl_433), with Grafana dashboards | Archiving, with alerts, heartbeat and backups (Phase 1); dashboards next |
| **Vehicle Manager** ([docs](docs/vehicle-manager/README.md)) | A web app for the vehicles I own: fuel log and MPG, services, MOT history from the DVSA, due-date reminders | Running on the home server (moved from Azure SQL: [plan](docs/vehicle-manager/HOMESERVER-MIGRATION.md)) |
| **Pay & Pensions** ([docs](docs/pay-and-pensions/README.md), [design](docs/pay-and-pensions/DESIGN.md)) | A web app for my household's payslip history, imported from Excel, and later our pension pots | Being built: people, employers, pay elements and payslips so far |

The **collectors** keep my own copy of data that otherwise only lives in someone else's cloud. They share a .NET 10 Worker Service under systemd, SQLite archives that keep the data as received, nightly backups pulled off the host, encrypted off-site copies, email alerts and an external heartbeat. **Vehicle Manager** and **Pay & Pensions** are ASP.NET Core Razor Pages apps on SQL Server, with their logs, traces and metrics in Grafana. Everything is reached over Tailscale only.

## Layout

| Path | What |
|---|---|
| `GreenHedges.HomeServer.slnx` | The one solution |
| `src/Common/`, `tests/Common/` | Shared by every service (`GreenHedges.HomeServer.Common`): host setup, email alerts, heartbeat, backups and off-site copies; by the collectors (`.Common.Sqlite`): their SQLite databases; by the web apps (`.Common.Web`): Auth0 sign-in for allowed emails, per-user data with audit columns, SQL Server backups |
| `src/Collectors/Libre/`, `tests/Collectors/Libre/` | The Libre collector (`GreenHedges.HomeServer.Collectors.Libre.*`) |
| `src/Collectors/Weather/`, `tests/Collectors/Weather/` | The Weather collector (`GreenHedges.HomeServer.Collectors.Weather.*`) |
| `src/VehicleManager/`, `tests/VehicleManager/` | Vehicle Manager (`GreenHedges.HomeServer.VehicleManager`, `.AppHost` for local development, `.Tests`) |
| `docs/vehicle-manager/` | Its README, backlog and the plan for moving it onto the home server |
| `src/PayAndPensions/`, `tests/PayAndPensions/` | Pay & Pensions (`GreenHedges.HomeServer.PayAndPensions`, `.Tests`) |
| `docs/pay-and-pensions/` | Its README (setup) and design notes |
| `compose.yaml`, `.env.example` | SQL Server in Docker for local development and Vehicle Manager's tests |
| `deploy/sqlserver/` | SQL Server in Docker on the home server, for its web apps: compose file, deploy script, host setup |
| `deploy/vehicle-manager/` | Vehicle Manager's systemd unit, deploy script, database setup and host setup |
| `deploy/TAILSCALE-SERVICES.md` | One-off Tailscale setup: the server's tag and access to `svc:home`, the tailnet address of the web apps |
| `deploy/caddy/` | Caddy, the web apps' front door: `https://<app>.<home domain>` on the tailnet, one wildcard certificate |
| `deploy/grafana/` | Grafana: dashboards (so far the Weather collector's), its data sources (including the web apps' telemetry) and dashboards as files, deploy and database scripts |
| `deploy/telemetry/` | Where the web apps' OpenTelemetry goes, for Grafana: Alloy (OTLP) into Loki, Tempo and Prometheus, in Docker; deploy script and host setup |
| `deploy/deploy.sh` | Publishes services and installs them on the host: `deploy/deploy.sh all`, or name them (`deploy/deploy.sh libre weather`; `deploy/<service>/deploy.sh` for one). Services run on the host's .NET runtime (Ubuntu's package); `--self-contained` bundles the SDK's runtime instead, for a security fix Ubuntu hasn't shipped yet |
| `deploy/libre/` | The Libre collector's systemd unit, deploy script, host setup and Nightscout stack |
| `docs/libre/` | Its README (setup and use) and design notes |
| `deploy/weather/` | The Weather collector's systemd unit, deploy script and host setup |
| `docs/weather/` | Its README (day to day) and design notes |
| `deploy/mac/` | The Mac's daily backup pull, for either collector |
| `deploy/aws/` | CloudFormation for the shared off-site backup bucket and its write-only users |
| `deploy/local.env.example` | Personal defaults for the deploy scripts (copy to the git-ignored `deploy/local.env`) |

Building, testing and the pull-request workflow: [CONTRIBUTING.md](CONTRIBUTING.md).

## Editing files

The guides here edit files with [ttt](https://github.com/eugenioenko/ttt), on the Mac and on the server. Root-owned
files on the server (`/etc/<service>/env` and the like) are edited with `sudoedit`: it opens a copy in ttt as you, then
writes it back with its owner and mode unchanged, so the editor never runs as root.

On the Mac, install it with Homebrew and make it the default editor:

```bash
brew install eugenioenko/ttt/ttt
```

```bash
printf '%s\n' 'export EDITOR=ttt VISUAL=ttt' >> ~/.zshrc && . ~/.zshrc
```

On the server, install the latest release binary for your own account (not root), checked against the release's
checksums. Set `VERSION` to the [latest release](https://github.com/eugenioenko/ttt/releases):

```bash
VERSION=v1.9.0 && mkdir -p ~/.local/bin && cd "$(mktemp -d)" && curl -fsSLO "https://github.com/eugenioenko/ttt/releases/download/$VERSION/ttt-linux-amd64" && curl -fsSLO "https://github.com/eugenioenko/ttt/releases/download/$VERSION/checksums.txt" && sha256sum --ignore-missing -c checksums.txt && install -m 755 ttt-linux-amd64 ~/.local/bin/ttt
```

Then make it the default editor, including for `sudoedit` (log in again first if `~/.local/bin` was only just created,
so it's on your `PATH`):

```bash
printf '%s\n' 'export EDITOR=ttt VISUAL=ttt SUDO_EDITOR="$HOME/.local/bin/ttt"' >> ~/.bashrc && . ~/.bashrc
```

## Licence

[MIT](LICENSE)

---

Designed and built with [Claude Code](https://claude.com/claude-code).
