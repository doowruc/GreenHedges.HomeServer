# Presents: host setup

Runs Presents on the home server, on its SQL Server, at `https://presents.<home domain>` (through Caddy). What it does
and its settings: [docs/presents/README.md](../../docs/presents/README.md). `<your-user>@<host>` is
your login on the server; the deploy scripts default to `COLLECTORS_SSH` from `deploy/local.env`.

**First:** everything Vehicle Manager needed ([its HOST-SETUP](../vehicle-manager/HOST-SETUP.md)): the .NET runtime,
SQL Server with its shared backups folder, Caddy, and the telemetry stack ([deploy/telemetry/](../telemetry/HOST-SETUP.md)).

**Do the steps in order.** Deploying (§6) before §1 to §5 installs a unit with no account or settings file, and systemd
retries it every 30 seconds: `sudo systemctl stop presents`, finish the earlier steps, then `sudo systemctl start presents`.

## 1. Service account and folders (on the server)

```bash
sudo useradd --system --home-dir /var/lib/presents --create-home --shell /usr/sbin/nologin presents && sudo chmod 750 /var/lib/presents
```

```bash
sudo usermod -aG sqlbackups presents && sudo usermod -aG presents "$USER"
```

```bash
sudo install -d -m 0755 /opt/presents && sudo install -d -m 0750 /etc/presents && sudo install -m 0600 /dev/null /etc/presents/env
```

## 2. Off-site upload user (on the Mac)

The off-site template now has a `<stack>-presents` user. Re-run its deploy, with the bucket name the stack already
uses (the existing bucket's, not this service's name: a different name makes CloudFormation try to replace the bucket) ([deploy/aws/README.md](../aws/README.md#deploy)); CloudFormation adds only the new user:

```bash
aws cloudformation deploy --region eu-west-2 --stack-name greenhedges-offsite --template-file deploy/aws/offsite.yaml --parameter-overrides BucketName=<bucket> --capabilities CAPABILITY_NAMED_IAM
```

Create its access key, and put both parts straight into your password manager ("Home server Presents off-site
key"); they're also needed in §4. The secret is shown once:

```bash
aws iam create-access-key --user-name greenhedges-offsite-presents
```

## 3. Auth0 (in a browser)

In the Auth0 dashboard, create a **Regular Web Application** called Presents, in the same tenant as Vehicle
Manager. Its **Settings**:

| Setting | Value |
|---|---|
| Allowed Callback URLs | `https://presents.<home domain>/callback, https://localhost:7083/callback, http://localhost:5083/callback` |
| Allowed Logout URLs | `https://presents.<home domain>/Account/SignedOut, https://localhost:7083/Account/SignedOut, http://localhost:5083/Account/SignedOut` |

Save. Under **Connections**, allow the same ones as Vehicle Manager's application. Keep the **Domain**, **Client ID** and
**Client Secret** for §4 (in your password manager, not anywhere else).

## 4. Settings (on the server)

Copy Vehicle Manager's alert and off-site settings (the same Resend key, recipient, bucket, region and `age` key), then
open the file:

```bash
sudo grep -E '^(Alerts__|Offsite__(Bucket|Region|AgeRecipient)=)' /etc/vehiclemanager/env | sudo tee -a /etc/presents/env > /dev/null
```

```bash
sudoedit /etc/presents/env
```

Change the `Alerts__From` name to `Presents`, and add the Auth0 application (§3), who may sign in, a new
healthchecks.io check's ping URL (create one: period **5 minutes**, grace **15 minutes**) and the off-site key (§2), so
the file has:

```
Alerts__ResendApiKey=re_…
Alerts__From=Presents <…>
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
deploy/presents/setup-database.sh
```

It creates the empty `Presents` database, the `presents` login as its owner, and writes the connection
string. The app creates its tables on first start.

## 6. Deploy (on the Mac)

```bash
deploy/presents/deploy.sh
```

While Ubuntu's ASP.NET Core runtime is older than the SDK's, the deploy stops and says so: add `--self-contained` to
bundle the SDK's runtime instead and redeploy without it once Ubuntu catches up.

## 7. Caddy (on the Mac)

The Caddyfile has the `presents` name. Load it:

```bash
deploy/caddy/deploy-caddy.sh
```

## 8. Check

On the server:

```bash
curl -s http://127.0.0.1:5083/health; echo
```

```bash
sudo systemd-run --quiet --wait --pipe --uid=presents -p EnvironmentFile=/etc/presents/env -p WorkingDirectory=/var/lib/presents /opt/presents/GreenHedges.HomeServer.Presents --test-alert
```

```bash
journalctl -u presents -n 40 --no-pager -o cat
```

Expect `Healthy`, the test email, and in the log `Backup written: /var/lib/presents/backups/presents-….bak.gz`
and `Off-site copy uploaded: …` (the first backup is taken at startup). The healthchecks.io check turns green within 5
minutes.

Then open `https://presents.<home domain>` on the Mac (Tailscale on) and sign in. Import the presents spreadsheet
(**Import** in the menu): the preview shows each year's presents and the children it would add, and checks the cash
worked out here against the sheet's Cash rows, before anything is saved. Then check the **Children** page has them
in the right order. Every present comes in at its sheet amount with no splits, which counts the same as the sheet
did; to show a big one as split, edit it to its full price and tick the split (it still counts the same).
Grafana's **Explore** → **Logs** has `service_name` = `Presents`.

## 9. Backup pull to the Mac (on the Mac, then the server)

**On the Mac:**

```bash
deploy/mac/install-backup-pull.sh presents
```

**On the server**, append the line it prints to `~/.ssh/authorized_keys`:

```bash
ttt ~/.ssh/authorized_keys
```

**In a browser**, create another healthchecks.io check (period **1 day**, grace **1 day**). Then **on the Mac**, save its
ping URL and test a pull:

```bash
printf '%s\n' 'https://hc-ping.com/<uuid>' > ~/.config/presents/backup-ping-url && chmod 600 ~/.config/presents/backup-ping-url
```

```bash
~/.local/bin/presents-pull-backups
```

## Updating

Change the app in a PR, then `deploy/presents/deploy.sh` (or `deploy/deploy.sh all`). Schema changes are EF
Core migrations, applied when the app starts.
