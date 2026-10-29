# Pay & Pensions: host setup

Runs Pay & Pensions on the home server, on its SQL Server, at `https://pay.<home domain>` (through Caddy). What it does
and its settings: [docs/pay-and-pensions/README.md](../../docs/pay-and-pensions/README.md). `<your-user>@<host>` is
your login on the server; the deploy scripts default to `COLLECTORS_SSH` from `deploy/local.env`.

**First:** everything Vehicle Manager needed ([its HOST-SETUP](../vehicle-manager/HOST-SETUP.md)): the .NET runtime,
SQL Server with its shared backups folder, Caddy, and the telemetry stack ([deploy/telemetry/](../telemetry/HOST-SETUP.md)).

## 1. Service account and folders (on the server)

```bash
sudo useradd --system --home-dir /var/lib/payandpensions --create-home --shell /usr/sbin/nologin payandpensions && sudo chmod 750 /var/lib/payandpensions
```

```bash
sudo usermod -aG sqlbackups payandpensions && sudo usermod -aG payandpensions "$USER"
```

```bash
sudo install -d -m 0755 /opt/payandpensions && sudo install -d -m 0750 /etc/payandpensions && sudo install -m 0600 /dev/null /etc/payandpensions/env
```

## 2. Off-site upload user (on the Mac)

The off-site template now has a `<stack>-payandpensions` user. Re-run its deploy, with the bucket name the stack already
uses ([deploy/aws/README.md](../aws/README.md#deploy)); CloudFormation adds only the new user:

```bash
aws cloudformation deploy --region eu-west-2 --stack-name greenhedges-offsite --template-file deploy/aws/offsite.yaml --parameter-overrides BucketName=<bucket> --capabilities CAPABILITY_NAMED_IAM
```

Create its access key, and put both parts straight into your password manager ("Home server Pay & Pensions off-site
key"); they're also needed in §4. The secret is shown once:

```bash
aws iam create-access-key --user-name greenhedges-offsite-payandpensions
```

## 3. Auth0 (in a browser)

In the Auth0 dashboard, create a **Regular Web Application** called Pay & Pensions, in the same tenant as Vehicle
Manager. Its **Settings**:

| Setting | Value |
|---|---|
| Allowed Callback URLs | `https://pay.<home domain>/callback, https://localhost:7081/callback, http://localhost:5081/callback` |
| Allowed Logout URLs | `https://pay.<home domain>/Account/SignedOut, https://localhost:7081/Account/SignedOut, http://localhost:5081/Account/SignedOut` |

Save. Under **Connections**, allow the same ones as Vehicle Manager's application. Keep the **Domain**, **Client ID** and
**Client Secret** for §4 (in your password manager, not anywhere else).

## 4. Settings (on the server)

Copy Vehicle Manager's alert and off-site settings (the same Resend key, recipient, bucket, region and `age` key), then
open the file:

```bash
sudo grep -E '^(Alerts__|Offsite__(Bucket|Region|AgeRecipient)=)' /etc/vehiclemanager/env | sudo tee -a /etc/payandpensions/env > /dev/null
```

```bash
sudoedit /etc/payandpensions/env
```

Change the `Alerts__From` name to `PayAndPensions`, and add the Auth0 application (§3), who may sign in, a new
healthchecks.io check's ping URL (create one: period **5 minutes**, grace **15 minutes**) and the off-site key (§2), so
the file has:

```
Alerts__ResendApiKey=re_…
Alerts__From=PayAndPensions <…>
Alerts__To__0=…
Offsite__Bucket=<bucket>
Offsite__Region=eu-west-2
Offsite__AgeRecipient=age1…
Auth0__Domain=…
Auth0__ClientId=…
Auth0__ClientSecret=…
AllowedEmails=…
Heartbeat__PingUrl=https://hc-ping.com/<uuid>
Offsite__AccessKeyId=AKIA…
Offsite__SecretAccessKey=…
```

`setup-database.sh` (§5) adds the connection string.

## 5. Database and login (on the Mac)

```bash
deploy/pay-and-pensions/setup-database.sh
```

It creates the empty `PayAndPensions` database, the `payandpensions` login as its owner, and writes the connection
string. The app creates its tables on first start.

## 6. Deploy (on the Mac)

```bash
deploy/pay-and-pensions/deploy.sh
```

While Ubuntu's ASP.NET Core runtime is older than the SDK's, the deploy stops and says so: add `--self-contained` to
bundle the SDK's runtime instead (as Vehicle Manager is for now), and redeploy without it once Ubuntu catches up.

## 7. Caddy (on the Mac)

The Caddyfile has the `pay` name. Load it:

```bash
deploy/caddy/deploy-caddy.sh
```

## 8. Check

On the server:

```bash
curl -s http://127.0.0.1:5081/health; echo
```

```bash
sudo systemd-run --quiet --wait --pipe --uid=payandpensions -p EnvironmentFile=/etc/payandpensions/env -p WorkingDirectory=/var/lib/payandpensions /opt/payandpensions/GreenHedges.HomeServer.PayAndPensions --test-alert
```

```bash
journalctl -u payandpensions -n 40 --no-pager -o cat
```

Expect `Healthy`, the test email, and in the log `Backup written: /var/lib/payandpensions/backups/payandpensions-….bak.gz`
and `Off-site copy uploaded: …` (the first backup is taken at startup). The healthchecks.io check turns green within 5
minutes.

Then open `https://pay.<home domain>` on the Mac (Tailscale on) and sign in. Add each person, and import their
spreadsheet (**People** → import): the preview checks every row against the sheet's own totals before anything is
saved. Grafana's **Explore** → **Logs** has `service_name` = `PayAndPensions`.

## 9. Backup pull to the Mac (on the Mac, then the server)

**On the Mac:**

```bash
deploy/mac/install-backup-pull.sh pay-and-pensions
```

**On the server**, append the line it prints to `~/.ssh/authorized_keys`:

```bash
ttt ~/.ssh/authorized_keys
```

**In a browser**, create another healthchecks.io check (period **1 day**, grace **1 day**). Then **on the Mac**, save its
ping URL and test a pull:

```bash
printf '%s\n' 'https://hc-ping.com/<uuid>' > ~/.config/payandpensions/backup-ping-url && chmod 600 ~/.config/payandpensions/backup-ping-url
```

```bash
~/.local/bin/payandpensions-pull-backups
```

## Updating

Change the app in a PR, then `deploy/pay-and-pensions/deploy.sh` (or `deploy/deploy.sh all`). Schema changes are EF
Core migrations, applied when the app starts.
