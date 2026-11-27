# Host setup (Ubuntu Server 26.04 LTS)

Steps to prepare a fresh Ubuntu Server host for LibreCollector. It runs on a UGREEN DXP2800 GT (a small x86-64 NAS, with Ubuntu in place of UGOS: its fans and LEDs need [deploy/dxp2800gt](../dxp2800gt/HOST-SETUP.md)); it ran on an old laptop before that, so sections marked *laptop only* are kept for laptops and can be skipped on other hardware.

**Placeholders:** `<your-user>` is your login on the server, `<interface>` is its wired network interface, `<luks-partition>` is its encrypted partition (e.g. `/dev/nvme0n1p3`), and `<Area/City>` is your time zone (e.g. `Europe/London`). The host is assumed to be named `librecollector` and reachable as `librecollector.lan`; adjust if your network uses a different domain.

Run everything on the server (over SSH or at its console) unless a step says **on the Mac**.

The services run on the host's **shared .NET runtime** from Ubuntu's own packages (installed in section 1), so unattended upgrades keep it patched. Docker is only needed from Phase 2 (Nightscout).

---

## 0. Name and address

Name the host after its role, and give it a **DHCP reservation** in the router so its IP never changes.

```bash
sudo hostnamectl set-hostname librecollector && sudo sed -i 's/^127\.0\.1\.1\s.*/127.0.1.1 librecollector/' /etc/hosts
```

In your router: create a static DHCP lease for the wired MAC address with the name `librecollector`. On some routers the name stored on the reservation overrides the hostname the machine sends, so set it there too. Then renew the lease (use the wired interface from `ip -brief link`):

```bash
sudo networkctl renew <interface>
```

`librecollector.lan` should then resolve to the reserved IP.

## 1. Update and basic tools

```bash
sudo apt update && sudo apt full-upgrade -y
```

```bash
sudo apt install -y smartmontools sqlite3
```

The .NET runtime for every service on the host (ASP.NET Core's includes the base runtime). Ubuntu's security updates carry its patches, and `needrestart` (run after each upgrade) restarts the services onto them; after a .NET patch, check with `systemctl status <service>` that they restarted.

```bash
sudo apt install -y aspnetcore-runtime-10.0
```

```bash
sudo timedatectl set-timezone <Area/City>
```

Check the clock is synchronised. The stale-data alert depends on it.

```bash
timedatectl
```

Look for `System clock synchronized: yes`. Reboot if the upgrade installed a new kernel (`/var/run/reboot-required` exists).

## 2. SSD health

Find the disk with `lsblk` (an NVMe drive is `/dev/nvme0`, a SATA one `/dev/sda`), then:

```bash
sudo smartctl -a /dev/nvme0
```

Look for `SMART overall-health self-assessment test result: PASSED`, then on NVMe **Percentage Used**, **Available Spare** and **Media and Data Integrity Errors** (0); on SATA, attribute **231 `SSD_Life_Left`** and reallocated / uncorrectable sector counts at or near 0.

## 3. Run as a server with the lid shut (*laptop only*)

Ignore the lid switch and never suspend:

```bash
sudo mkdir -p /etc/systemd/logind.conf.d && printf '[Login]\nHandleLidSwitch=ignore\nHandleLidSwitchExternalPower=ignore\nHandleLidSwitchDocked=ignore\n' | sudo tee /etc/systemd/logind.conf.d/laptop-server.conf
```

```bash
sudo systemctl mask sleep.target suspend.target hibernate.target hybrid-sleep.target
```

The logind change takes effect after the next reboot (or `sudo systemctl restart systemd-logind`, which may end a console session, but not SSH).

### Battery conservation mode (Lenovo IdeaPad)

This holds the battery at about 60% so being on charge all the time doesn't wear it out. Check that the driver exposes it:

```bash
cat /sys/bus/platform/drivers/ideapad_acpi/*/conservation_mode
```

`0` or `1` means it's supported. Turn it on now and on every boot:

```bash
echo 'w /sys/bus/platform/drivers/ideapad_acpi/*/conservation_mode - - - - 1' | sudo tee /etc/tmpfiles.d/ideapad-conservation.conf
```

```bash
sudo systemd-tmpfiles --create /etc/tmpfiles.d/ideapad-conservation.conf && cat /sys/bus/platform/drivers/ideapad_acpi/*/conservation_mode
```

It should now print `1`.

## 4. SSH: keys only

**On the Mac**, copy your public key to the server (replace user and host):

```bash
ssh-copy-id <your-user>@librecollector.lan
```

Log in with the key in a **new** terminal to prove it works **before** turning passwords off. Then, on the server:

```bash
printf 'PasswordAuthentication no\nKbdInteractiveAuthentication no\nPermitRootLogin no\n' | sudo tee /etc/ssh/sshd_config.d/00-hardening.conf
```

(`00-` so it wins over any later file, e.g. `50-cloud-init.conf`; sshd uses the first value it reads.)

```bash
sudo sshd -t && sudo systemctl restart ssh
```

Keep your current session open and check a new SSH login still works.

## 5. Firewall

```bash
sudo ufw allow OpenSSH && sudo ufw enable && sudo ufw status verbose
```

> **Docker caveat (Phase 2):** ports that Docker publishes **bypass ufw**. Bind Nightscout to `127.0.0.1` or the Tailscale IP in the compose file, never `0.0.0.0`.

## 6. Security updates

Ubuntu Server enables unattended security upgrades by default. Check:

```bash
cat /etc/apt/apt.conf.d/20auto-upgrades
```

Both lines should end in `"1"`. If not, run `sudo dpkg-reconfigure -plow unattended-upgrades`.

**Livepatch** (kernel fixes without rebooting) comes with Ubuntu Pro, which is free for personal use on up to 5 machines. Get a token from <https://ubuntu.com/pro/dashboard>, then:

```bash
sudo pro attach
```

```bash
sudo pro status
```

Check `livepatch` is `enabled`. Leave automatic reboots **off** if the disk is LUKS-encrypted without TPM unlock (6a): an unattended reboot would stop at the passphrase prompt.

## 6a. Disk encryption (LUKS) and reboots

With LUKS, a boot waits for the passphrase at the console unless something else unlocks the disk. Ubuntu 26.04 builds its boot image with **dracut**, which has no packaged way to unlock over SSH (**don't** install `dropbear-initramfs`: it needs initramfs-tools, which conflicts with dracut).

### With a TPM: unlock at boot (the DXP2800 GT)

The TPM unlocks the disk when the firmware and boot chain are as enrolled (PCR 7, the Secure Boot state), so reboots and recovery after a power cut need no one at the console. The passphrase keeps working. Turn **Secure Boot on** first, and enrol on the **second** boot with it on: shim updates its SBAT level on the first, which changes PCR 7 and stops the enrolment matching.

dracut's `tpm2-tss` module is skipped unless `tpm2-tools` is installed:

```bash
sudo apt install -y tpm2-tools
```

Find the LUKS partition (`lsblk -f`, type `crypto_LUKS`), then enrol the TPM on it:

```bash
sudo systemd-cryptenroll --tpm2-device=auto --tpm2-pcrs=7 <luks-partition>
```

Add `tpm2-device=auto` to the options (the fourth field) of that partition's line in `/etc/crypttab`:

```bash
sudoedit /etc/crypttab
```

Add the module and rebuild the running kernel's image (`--regenerate-all` can fail on an old kernel's leftovers):

```bash
echo 'add_dracutmodules+=" tpm2-tss "' | sudo tee /etc/dracut.conf.d/tpm2.conf && sudo dracut -f --kver "$(uname -r)"
```

```bash
sudo lsinitrd /boot/initrd.img-$(uname -r) | grep -E 'tcti-device|token-systemd-tpm2'
```

Both should be listed. Reboot: it should come up with no passphrase.

**Anything that changes PCR 7** (a BIOS update, Secure Boot keys, a shim/SBAT update, a dbx update from fwupd) means one boot at the console: type the passphrase, then enrol again and reboot to check:

```bash
sudo systemd-cryptenroll --wipe-slot=tpm2 --tpm2-device=auto --tpm2-pcrs=7 <luks-partition>
```

### Without a TPM (the old laptop)

Every boot waits for the passphrase at the console. `clevis-dracut` (with a Tang server elsewhere on the LAN) is the packaged route to automatic unlock at home, if it's ever needed. Otherwise reboot only when you're at home (check `/var/run/reboot-required` occasionally; Livepatch covers most kernel fixes), and rely on the off-machine heartbeat to tell you if the machine is down.

## 7. Tailscale (remote access, no open ports)

This downloads and runs Tailscale's official install script:

```bash
curl -fsSL https://tailscale.com/install.sh | sh
```

```bash
sudo tailscale up
```

Open the printed URL to add the machine to your tailnet. Then, in the Tailscale admin console, **disable key expiry** for this machine so it doesn't drop off the tailnet after 180 days.

## 8. LibreCollector service account and directories

```bash
sudo useradd --system --home-dir /var/lib/librecollector --create-home --shell /usr/sbin/nologin librecollector && sudo chmod 750 /var/lib/librecollector
```

```bash
sudo install -d -m 0755 /opt/librecollector && sudo install -d -m 0750 /etc/librecollector
```

Secrets file, readable by root only. systemd reads it before dropping to the service user. (`sudoedit` opens it in ttt: see [Editing files](../../README.md#editing-files).)

```bash
sudo install -m 0600 /dev/null /etc/librecollector/env && sudoedit /etc/librecollector/env
```

Contents (environment variable form of the config keys; `__` separates sections):

```
LibreLinkUp__Email=follower@example.com
LibreLinkUp__Password=...
Alerts__ResendApiKey=re_...
Alerts__From=LibreCollector <alerts@example.com>
Alerts__To__0=you@example.com
Heartbeat__PingUrl=https://hc-ping.com/<uuid>
```

**Heartbeat (off-machine alert):** at <https://healthchecks.io> (free plan), create a check with **period 5 minutes** and **grace 20 minutes**, add an email integration, and copy its ping URL into `Heartbeat__PingUrl`. It alerts when this machine stops pinging, which the on-machine alerts can't do.

`/opt/librecollector` holds the app (read-only at runtime), and `/var/lib/librecollector` holds the database and logs. Deploying the app and the systemd unit (`deploy/libre/librecollector.service`) is a separate step.

> **Only one collector at a time.** Stop any other copy (e.g. a test run on the Mac) before starting the service, so two processes aren't logging in with the same follower account.

## 8a. Backups

The collector takes a nightly backup at 03:30 into `/var/lib/librecollector/backups/` (gzipped, integrity-checked, 7 kept). It also takes one at startup if none exists from the last day. A "Backups failing" email is sent if there's no good backup for 36 hours.

**Off-machine copy (the Mac pulls).** Let your user read the backups folder:

```bash
sudo usermod -aG librecollector "$USER"
```

Check that the restricted-rsync helper is present (from the `rsync` package):

```bash
ls -l /usr/bin/rrsync
```

**On the Mac**, install Homebrew's rsync (macOS's built-in one is a different implementation), then the pull job. The installer creates a dedicated key, installs a daily launchd job (09:00, or on wake if the Mac was asleep) that pulls over the home network, and prints an `authorized_keys` line:

```bash
brew install rsync
```

```bash
deploy/mac/install-backup-pull.sh libre
```

It takes your server user and host from `deploy/local.env` (`COLLECTORS_USER` and `COLLECTORS_HOST`; see `deploy/local.env.example`) or the environment, and saves them in `~/.config/librecollector/backup-pull.env`, which the job reads. The same scripts pull the Weather collector's backups (`… weather`; see its HOST-SETUP).

**On the server**, append the printed line to `~/.ssh/authorized_keys` (as one line). It limits that key to **read-only rsync of the backups folder**: no shell, no forwarding, nothing else readable.

```bash
ttt ~/.ssh/authorized_keys
```

**Heartbeat for the pull:** create a second healthchecks.io check (period **1 day**, grace **1 day**), and save its ping URL on the Mac:

```bash
printf '%s\n' 'https://hc-ping.com/<uuid>' > ~/.config/librecollector/backup-ping-url && chmod 600 ~/.config/librecollector/backup-ping-url
```

Test a pull now, from the Mac:

```bash
~/.local/bin/librecollector-pull-backups
```

Backups land in `~/Backups/LibreCollector` (30 kept), which Time Machine also covers.

**Restoring** (any copy):

```bash
gunzip -k librecollector-YYYYMMDDTHHMMSSZ.db.gz
```

Then on the server: `sudo systemctl stop librecollector`, copy the `.db` over `/var/lib/librecollector/librecollector.db` (owner `librecollector`, mode 0640, and remove any `-wal`/`-shm` files next to it), then `sudo systemctl start librecollector`.

## 8b. Off-site copies (encrypted, to S3)

Each new nightly backup is **encrypted on the server with [`age`](https://github.com/FiloSottile/age)** and uploaded to an S3 bucket. The server holds only the **public** key, so it can encrypt but never decrypt; the private key lives only in your password manager. The server's AWS key can **only upload** to one folder of one bucket: it can't list, read or delete. Checked hourly, so failures retry; an "Off-site backup failing" email follows after 36 hours without a good copy.

### 1. Encryption key pair (on your computer)

```bash
age-keygen -o librecollector-offsite.key
```

It prints the **public key** (`age1…`). Save the **whole key file** (it contains the private key, `AGE-SECRET-KEY-…`) in your password manager, for example as a Secure Note titled "LibreCollector off-site backup key". Without it, the off-site copies **can't be decrypted**. Then delete the local file:

```bash
rm librecollector-offsite.key
```

> **Shared bucket:** the bucket and a write-only user for each collector can now be created from [`deploy/aws/offsite.yaml`](../aws/README.md) (CloudFormation) instead of steps 2 and 3. Steps 1, 4 and 5 still apply.

### 2. S3 bucket (AWS console)

1. **S3 → Create bucket.** Pick a globally unique name and your nearest region (e.g. `eu-west-2`, London). Keep **Block all public access** on. Turn **Bucket Versioning** on. Leave default encryption (SSE-S3) on.
2. **Management → Create lifecycle rule** (apply to all objects):
   - *Transition current versions*: **Glacier Deep Archive** after **30** days.
   - *Transition noncurrent versions*: **Glacier Deep Archive** after **30** days.

   Recent copies stay in S3 Standard (instant to restore); older ones cost almost nothing in Deep Archive (restores take about 12–48 hours, which is fine for disaster recovery). Nothing is ever deleted.

### 3. Write-only IAM user (AWS console)

**IAM → Users → Create user** `librecollector-offsite` (no console access). Add an **inline policy** (JSON editor), replacing `<bucket>`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "UploadOffsiteBackupsOnly",
      "Effect": "Allow",
      "Action": "s3:PutObject",
      "Resource": "arn:aws:s3:::<bucket>/librecollector/*"
    }
  ]
}
```

Then **Security credentials → Create access key** ("Application running outside AWS"). Save both values in your password manager.

### 4. Server

```bash
sudo apt install -y age
```

Add to `/etc/librecollector/env`:

```
Offsite__Bucket=<bucket>
Offsite__Region=eu-west-2
Offsite__AccessKeyId=AKIA…
Offsite__SecretAccessKey=…
Offsite__AgeRecipient=age1…
```

Deploy (or `sudo systemctl restart librecollector`). Within a minute the log shows `Off-site copy uploaded: s3://<bucket>/librecollector/librecollector-….db.gz.age`. If a setting is wrong, collection carries on regardless: the log says `Off-site backup is misconfigured and won't run: …` and you get an "Off-site backup failing" email straight away. Fix the env file and restart. Optionally prefix the bucket with something else via `Offsite__Prefix` (then change the policy's `librecollector/*` to match).

**S3-compatible storage** (Backblaze B2, Cloudflare R2, MinIO): also set `Offsite__ServiceUrl` to the provider's S3 endpoint and `Offsite__Region` to its region name, and use that provider's equivalent of a write-only key.

### 5. Restore test (do it once now, then occasionally)

Download the newest object from the S3 console (if it's in Deep Archive, first **Initiate restore** and wait). Put the private key from your password manager in a file, then:

```bash
age --decrypt --identity librecollector-offsite.key --output restored.db.gz librecollector-<timestamp>.db.gz.age
gunzip -k restored.db.gz && sqlite3 restored.db "PRAGMA integrity_check; SELECT source, COUNT(*) FROM readings GROUP BY source;"
rm librecollector-offsite.key
```

`ok` and plausible counts mean the off-site copy is good. Then follow **Restoring** in §8a if you need to put it back.

## 9. Docker (Phase 2, can be done now)

Docker's official apt repository (not the Ubuntu `docker.io` package, and not the snap):

```bash
sudo install -m 0755 -d /etc/apt/keyrings && sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc && sudo chmod a+r /etc/apt/keyrings/docker.asc
```

```bash
printf 'Types: deb\nURIs: https://download.docker.com/linux/ubuntu\nSuites: %s\nComponents: stable\nSigned-By: /etc/apt/keyrings/docker.asc\n' "$(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")" | sudo tee /etc/apt/sources.list.d/docker.sources
```

```bash
sudo apt update && sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

```bash
sudo docker run --rm hello-world
```

Don't add your user to the `docker` group unless you want to: membership is effectively root. `sudo docker …` is fine.

## 10. Nightscout (Phase 2)

Needs Docker (§9). Nightscout and MongoDB run from [`deploy/libre/nightscout/compose.yaml`](nightscout/compose.yaml): pinned images, MongoDB on an internal-only Docker network, and Nightscout listening on **`127.0.0.1:1337` only**. It's reached from other devices through Caddy, at `https://nightscout.<home domain>` (HTTPS, tailnet only).

**On the server**, create the folders and the secret. `API_SECRET` is Nightscout's admin password; the publisher will use it too.

```bash
sudo install -d -m 0755 /opt/nightscout && sudo install -d -m 0750 /etc/nightscout && sudo install -d -m 0700 /var/lib/nightscout/mongo
```

```bash
sudo install -m 0600 /dev/null /etc/nightscout/nightscout.env && echo "API_SECRET=$(openssl rand -hex 16)" | sudo tee /etc/nightscout/nightscout.env > /dev/null
```

Save the generated value in your password manager (you'll need it for Nightscout's admin pages and for the publisher):

```bash
sudo cat /etc/nightscout/nightscout.env
```

**On the Mac**, install and start the stack. This copies the compose file, has Docker validate it, pulls the images, starts both containers, and waits until Nightscout answers:

```bash
deploy/libre/nightscout/deploy-nightscout.sh <your-user>@librecollector.lan
```

It should end with `Nightscout is up:` and a JSON status that includes `"version":"15.0.9"`. Re-run the same script after changing `compose.yaml`, e.g. to upgrade an image tag.

### HTTPS over the tailnet

Set up Caddy ([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)); its Caddyfile already has the `nightscout` name.

### Devices that view Nightscout

Each needs **Tailscale installed and signed in to the same account** (the server is the only one at first):

- **Your computer:** Tailscale from <https://tailscale.com/download> (or the Mac App Store).
- **iPhone:** Tailscale from the App Store. Allow the VPN configuration when asked.

Then open `https://nightscout.<home domain>` in a browser. Until the publisher is running (below), the chart is empty; that's expected.

### Publishing from the collector

Tell the collector where Nightscout is. Append to `/etc/librecollector/env`, using the same secret as in `/etc/nightscout/nightscout.env`:

```
Nightscout__BaseUrl=http://127.0.0.1:1337
Nightscout__ApiSecret=<API_SECRET>
```

```bash
sudoedit /etc/librecollector/env
```

Then deploy the collector from your computer (`deploy/libre/deploy.sh <your-user>@librecollector.lan`). On first start it migrates the archive to schema v2 and publishes the whole archive to Nightscout; after that it publishes each new reading within seconds. The log shows `Publishing to Nightscout at …` and `Published N entries`. If Nightscout is unreachable, collection carries on, and a "Nightscout publishing stalled" email arrives after 30 minutes.

**Rebuilding Nightscout from the archive** (e.g. after wiping MongoDB): stop the collector, run `sudo -u librecollector sqlite3 /var/lib/librecollector/librecollector.db "DELETE FROM publish_state WHERE target LIKE 'nightscout%';"`, then start it again. Re-sending is safe: Nightscout upserts on timestamp + type.

**No firewall changes are needed:** Nightscout is bound to localhost, and Caddy connects to it locally.

## 11. Importing LibreView history (optional, re-runnable)

Brings your LibreView history (historic glucose since the account began, scans, insulin, carbs, notes) into the archive; Nightscout then fills in automatically. See *LibreView export import* in [docs/libre/DESIGN.md](../../docs/libre/DESIGN.md) for the details and rules.

1. From your computer, deploy the current version first (`deploy/libre/deploy.sh <your-user>@librecollector.lan`; the importer needs schema v4).
2. In LibreView (your own account): **Glucose History → Download glucose data**. Then copy the file to the server:

```bash
scp ~/Downloads/<your-export>.csv <your-user>@librecollector.lan:/tmp/libreview.csv
```

3. On the server, hand it to the service account and run the importer (it logs to its own file so it doesn't clash with the running service):

```bash
sudo install -o librecollector -g librecollector -m 0600 /tmp/libreview.csv /var/lib/librecollector/libreview-import.csv && rm /tmp/libreview.csv
```

```bash
sudo -u librecollector env Serilog__WriteTo__1__Args__path=logs/import-.log sh -c 'cd /var/lib/librecollector && /opt/librecollector/GreenHedges.HomeServer.Collectors.Libre.Worker --import-libreview libreview-import.csv'
```

It prints a summary: readings, scans and events imported, already present, skipped. Then delete the export:

```bash
sudo rm /var/lib/librecollector/libreview-import.csv
```

4. Nightscout catches up within about 5 minutes (about 100k entries in batches of 500); the journal shows `Published N entries to Nightscout`. Apple Health is unaffected.

---

## Checklist

- [ ] System updated, `sqlite3` + `smartmontools` installed, clock synchronised
- [ ] SSD health checked
- [ ] Lid ignored, sleep masked, conservation mode on (*laptop*)
- [ ] SSH key-only, tested from a new session
- [ ] ufw on (SSH allowed)
- [ ] Unattended upgrades on; Ubuntu Pro + Livepatch attached
- [ ] Disk unlocked by the TPM, tested with a reboot (or, without a TPM, reboots planned for when you're home)
- [ ] Tailscale up, key expiry disabled
- [ ] `librecollector` user, `/opt/librecollector`, `/var/lib/librecollector`, `/etc/librecollector/env`
- [ ] Off-site copies: age key in password manager, bucket (versioning + lifecycle), write-only IAM key, `Offsite__*` in env, first upload logged, restore test passed
- [ ] Docker installed (Phase 2)
- [ ] Nightscout: `/etc/nightscout/nightscout.env` created (secret saved), stack started with `deploy-nightscout.sh`, answers on `127.0.0.1:1337`
- [ ] Caddy set up; Tailscale on your computer and phone; Nightscout opens at `https://nightscout.<home domain>`
- [ ] `Nightscout__BaseUrl` / `Nightscout__ApiSecret` in `/etc/librecollector/env`; collector redeployed; archive visible in Nightscout
