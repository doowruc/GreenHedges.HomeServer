# Telemetry: host setup

Where the web apps' OpenTelemetry goes: Grafana Alloy receives it over OTLP on `127.0.0.1:4317` (gRPC) and `4318`
(HTTP), and keeps logs in Loki, traces in Tempo and metrics in Prometheus, all in Docker. Grafana shows them as its
**Logs**, **Traces** and **Metrics** data sources. How it's set up: the comments in [`compose.yaml`](compose.yaml) and
the config folders beside it.

Telemetry is for looking into problems, not a record. Logs and metrics are kept 30 days and traces 7, and none of it is
backed up. Nothing personal goes into it: logs carry Auth0 user ids, never emails, names or amounts, and SQL is traced
as its parameterised text, never the values.

**First:** Docker and Grafana ([deploy/grafana/HOST-SETUP.md](../grafana/HOST-SETUP.md)).

## 1. Folders (on the server)

```bash
sudo install -d -m 0755 /opt/telemetry && sudo install -d -m 0750 -o 10001 -g 10001 /var/lib/telemetry /var/lib/telemetry/alloy /var/lib/telemetry/loki /var/lib/telemetry/tempo /var/lib/telemetry/prometheus
```

All four containers run as uid 10001 (Loki's and Tempo's own user), which owns the data. It needs no settings file
and no secrets.

## 2. Deploy (on the Mac)

```bash
deploy/telemetry/deploy-telemetry.sh
```

It ends with each part ready: `Alloy ready`, `Loki ready`, `Tempo ready`, `Prometheus ready`.

## 3. Grafana's data sources (on the Mac)

```bash
deploy/grafana/deploy-grafana.sh
```

It adds **Logs** (Loki), **Traces** (Tempo) and **Metrics** (Prometheus) from
[`provisioning/datasources/telemetry.yaml`](../grafana/provisioning/datasources/telemetry.yaml).

## 4. Point the apps at it

Each web app's systemd unit sets `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`, so redeploying the app is all it
takes, e.g. `deploy/deploy.sh vehicle-manager`. If the stack is down, the apps carry on and their telemetry is dropped.

## 5. Check

**On the server:** what each part holds so far (the first apps' telemetry arrives within a minute of their first
requests):

```bash
curl -s http://127.0.0.1:3100/loki/api/v1/label/service_name/values; echo
```

It lists the apps that have sent logs, e.g. `{"status":"success","data":["VehicleManager"]}`.

**In Grafana** (`https://grafana.<home domain>`): **Explore** → **Logs** → label `service_name` = `VehicleManager`.
A log line with a trace id has a **View trace** link to it in **Traces**, and a trace links back to its logs. **Metrics**
has the request counts and durations (`http_server_request_duration_seconds`) and .NET runtime metrics, per
`service_name`.

## Updating

Change an image tag in [`compose.yaml`](compose.yaml), or a config file, in a PR, then re-run
`deploy/telemetry/deploy-telemetry.sh`. Check each release's notes for config changes first: Loki, Tempo and Alloy
rename settings between minor versions. Before deploying, the stack can be tried on the Mac: with OrbStack or Docker
Desktop running, from the repo root,

```bash
mkdir -p /tmp/telemetry-data/{alloy,loki,tempo,prometheus} && chmod -R 777 /tmp/telemetry-data && TELEMETRY_DATA=/tmp/telemetry-data docker compose -f deploy/telemetry/compose.yaml up -d
```

then run an app with `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317` and look at
`http://127.0.0.1:3100/loki/api/v1/label/service_name/values`. `docker compose -f deploy/telemetry/compose.yaml down`
stops it.
