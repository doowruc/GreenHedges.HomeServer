# Photos off-site: host setup

The photos' off-site copy: a nightly **rclone** sync of the photo folders into their own S3 bucket, encrypted with
rclone crypt (contents and names) and stored in **Glacier Deep Archive**. It replaces the old file server's CloudBerry
plan. Why it's done this way: [docs/photos-offsite/PLAN.md](../../docs/photos-offsite/PLAN.md). The details: the
comments in [`photos-offsite.sh`](photos-offsite.sh) and [`photos-offsite.service`](photos-offsite.service).

**First:** the bucket's stack and its sync user's access key ([deploy/aws/README.md](../aws/README.md), "Photos'
off-site bucket").

| Where | What |
|---|---|
| `/srv/backup/photos`, `/srv/backup/photos-private` | The old file server's two photo shares, read-only CIFS automounts, owned by `photos-offsite`. Later the home server's own datasets |
| `/etc/photos-offsite/` | `photos-offsite.env` (sources, heartbeat), `rclone.conf` (the S3 key, the crypt password and salt), `filters.txt` (what's copied), `smb-credentials`. All `root:photos-offsite` 0640 (the credentials 0600) |
| `/opt/photos-offsite/photos-offsite.sh` | The job, run by `photos-offsite.timer` at 01:00 |

## 1. The user and folders (on the server)

A system user that can only read the photos and its own settings. You join its group, to read its settings without
sudo.

```bash
sudo adduser --system --group --no-create-home --home /nonexistent photos-offsite && sudo usermod -aG photos-offsite "$USER"
```

```bash
sudo install -d -m 0755 /srv/backup /srv/backup/photos /srv/backup/photos-private && sudo chattr +i /srv/backup/photos /srv/backup/photos-private && sudo install -d -m 0750 -g photos-offsite /etc/photos-offsite
```

`chattr +i` keeps the empty mount folders empty while nothing is mounted on them (the script also refuses a source
that isn't a mount, or is empty).

## 2. Packages (on the server)

Ubuntu's rclone (1.60, patched through ESM by unattended upgrades), the CIFS tools and jq (for the email report;
both likely there already):

```bash
sudo apt install rclone cifs-utils jq
```

## 3. The shares

**On the old file server:** a **backup reader** user that may only **read** the two photo shares (names in the private
notes), and nothing else. A long random password, in your password manager ("Old file server photos backup reader").
It's the only account on the home server that can open the private photos share, so it's its own, not the media
server's reader.

**On the server:** its credentials, in a root-only file. Create it empty, then type the password in yourself:

```bash
sudo install -m 0600 /dev/null /etc/photos-offsite/smb-credentials && sudoedit /etc/photos-offsite/smb-credentials
```

```text
username=<the backup reader>
password=<its password>
```

Then two lines in `/etc/fstab`, with the old file server's name and the share names from the private notes (a space in
a name is `\040`):

```bash
sudoedit /etc/fstab
```

```text
//<file server>/<photo share>          /srv/backup/photos          cifs  ro,vers=2.1,credentials=/etc/photos-offsite/smb-credentials,uid=photos-offsite,gid=photos-offsite,file_mode=0440,dir_mode=0550,iocharset=utf8,nofail,_netdev,x-systemd.automount,x-systemd.mount-timeout=30  0  0
//<file server>/<private photo share>  /srv/backup/photos-private  cifs  ro,vers=2.1,credentials=/etc/photos-offsite/smb-credentials,uid=photos-offsite,gid=photos-offsite,file_mode=0440,dir_mode=0550,iocharset=utf8,nofail,_netdev,x-systemd.automount,x-systemd.mount-timeout=30  0  0
```

`ro` and a read-only account: nothing here can change the photos. `0440`/`0550`: only `photos-offsite` (and its group)
can read them on this box. Load them and look:

```bash
sudo systemctl daemon-reload && sudo systemctl restart remote-fs.target && sudo -u photos-offsite ls /srv/backup/photos /srv/backup/photos-private | head -20 && findmnt -t cifs
```

## 4. rclone's settings: the key and the encryption

Make two long random passwords in your password manager, one item "Photos off-site encryption (rclone crypt)" with
both: the **password** and the **salt**. **Without them the off-site copy can't be read**, and they can never change.

The file, from [`rclone.conf.example`](rclone.conf.example): create it, then fill it in yourself (bucket, the sync
user's access key from 1Password, and both passwords, each run through `rclone obscure -` first, which reads it from
the terminal, so it stays out of the shell's history):

```bash
sudo install -m 0640 -g photos-offsite /dev/null /etc/photos-offsite/rclone.conf && sudoedit /etc/photos-offsite/rclone.conf
```

```bash
rclone obscure -
```

(Paste the password, Enter, Ctrl+D; copy the output into `password =`. The same for the salt into `password2 =`.)

Check rclone can see the bucket (empty at first):

```bash
sudo -u photos-offsite rclone --config /etc/photos-offsite/rclone.conf lsf photos-offsite: && echo listed
```

## 5. What's copied

From [`filters.example.txt`](filters.example.txt), with the real folder list from the private notes:

```bash
sudo install -m 0640 -g photos-offsite /dev/null /etc/photos-offsite/filters.txt && sudoedit /etc/photos-offsite/filters.txt
```

A copy of the real file goes in the private notes too; change both together.

## 6. healthchecks.io, the email report and the settings file

A healthchecks.io check, "Photos off-site", period **1 day**, grace **15 hours** (CloudBerry's limit): it emails if a
night fails or doesn't run at all.

The **email report** after every run, like CloudBerry's: whether it worked, rclone's summary, and the photos added,
updated and deleted (the first 200 of each in the email, the full list attached when there are more). It goes through
Resend with the same API key and sender address as the services' alerts; see them with
`sudo grep -E '^Alerts__(ResendApiKey|From)=' /etc/vehiclemanager/env` and type them into the settings file (the
sender's name becomes "Photos off-site"). The report names the photos and folders, as CloudBerry's did.

The settings file, from [`photos-offsite.env.example`](photos-offsite.env.example), with the ping URL and the report's
settings:

```bash
sudo install -m 0640 -g photos-offsite /dev/null /etc/photos-offsite/photos-offsite.env && sudoedit /etc/photos-offsite/photos-offsite.env
```

## 7. Deploy, then the first run

On the Mac:

```bash
deploy/photos-offsite/deploy-photos-offsite.sh
```

It installs the job and enables the timer (it doesn't run a sync), then lists any top-level folder the filters don't
mention: fix `filters.txt` until there are none. Then on the server, what the first run would do:

```bash
sudo systemd-run --quiet --wait --pipe --collect -p User=photos-offsite -p EnvironmentFile=/etc/photos-offsite/photos-offsite.env /opt/photos-offsite/photos-offsite.sh --dry-run 2>&1 | tail -20
```

It should say about 64,000 files to copy and none to delete; compare with the counts in the private notes. Then the
first run, an evening or a weekend (about 240 GB at the old file server's SMB speed is a couple of hours; in working
hours it's limited to 20 MiB/s):

```bash
sudo systemctl start --no-block photos-offsite.service && journalctl -u photos-offsite -f
```

Ctrl+C stops following, not the job. When it's done: `systemctl status photos-offsite` (`status=0/SUCCESS`), the
report in your inbox (about 64,000 added, in the attached list), the healthchecks.io check green with the summary in its
last ping, and a second dry run wants to copy nothing. CloudBerry
keeps running alongside until the restore test (§9) has passed and about four weeks have been green.

## 8. Day to day

- **The last run:** the morning's email, `journalctl -u photos-offsite -n 30`, or healthchecks.io's log (each ping
  carries the summary).
- **No email but the check is green:** the report couldn't be sent (`journalctl -u photos-offsite | grep 'Report email'`
  shows Resend's answer); the copy itself worked. Fewer emails: `REPORT_WHEN=changes` in the settings file.
- **A warning about folders not in the filters:** a new top-level folder in the private share. Include it or exclude
  it in `/etc/photos-offsite/filters.txt` (and the private notes); the run carries on either way.
- **"Max delete count" or rclone exit 7:** the run would have deleted more than 10% of the copies, so it stopped
  deleting there. Check the sources are really mounted (`findmnt -t cifs`) and `filters.txt`. If the photos really
  were deleted, run it once with the limit raised:
  `sudo systemd-run --quiet --wait --pipe --collect -p User=photos-offsite -p EnvironmentFile=/etc/photos-offsite/photos-offsite.env -E PHOTOS_OFFSITE_MAX_DELETE_PERCENT=100 /opt/photos-offsite/photos-offsite.sh`.
- **The monthly run** (first Sunday) compares sizes instead of dates; it takes no longer.
- **After a change here:** `deploy/photos-offsite/deploy-photos-offsite.sh` again.

## 9. Restoring (and the restore test)

Restores are done from the Mac as the administrator, not with the server's key (which can't restore from Deep Archive
or read older versions). They need only what's in 1Password: the crypt password and salt, and an administrator sign-in.

1. **rclone on the Mac** (`brew install rclone`; newer than the server's, which is fine) and a config of its own,
   `~/.config/rclone/rclone.conf`, the same as the server's but `env_auth = true` instead of the access key lines, and
   `remote = photos-s3:<bucket>` as before. Then, each time, the administrator's session for rclone:

   ```bash
   aws login && eval "$(aws configure export-credentials --format env)"
   ```

2. **Find the folder's name in the bucket** (names are encrypted):

   ```bash
   rclone backend encode photos-offsite: "private/<folder>"
   ```

3. **Ask S3 to restore it** from Deep Archive (Bulk: up to 48 hours, $0.005/GB; `priority=Standard`: up to 12 hours,
   $0.021/GB), kept readable for 7 days:

   ```bash
   rclone backend restore "photos-s3:<bucket>/<encrypted path>" -o priority=Bulk -o lifetime=7
   ```

   `rclone backend restore-status "photos-s3:<bucket>/<encrypted path>"` shows progress.
4. **Copy it down** and compare with the original:

   ```bash
   rclone copy "photos-offsite:private/<folder>" ~/Restore/<folder> -v
   ```

   ```bash
   rclone check ~/Restore/<folder> "/Volumes/<private photo share>/<folder>"
   ```

5. **A deleted or overwritten photo** is an older version. `--s3-version-at "<date and time>"` shows the bucket as it
   was then, through the encryption: list with it, then restore and copy with it as above. If rclone won't restore
   an older version, `aws s3api list-object-versions` and `aws s3api restore-object --version-id …` on the encrypted
   name do it directly.

**The restore test:** before CloudBerry goes, and then twice a year: one folder from each share, plus a photo deleted
on purpose (after its deletion has synced), all compared as in step 4. Log each in the private notes.

## 10. When the drives go in

Copy the photos onto the datasets **keeping modification times** (`rsync -a`), change the folders in
`PHOTOS_OFFSITE_SOURCES` (keep the names: they're the top folders in the bucket), and run `--dry-run`: it should want
to copy and delete nothing. Then take the two `/etc/fstab` lines out, and the backup reader account off the old file
server.

## Removing it

```bash
sudo systemctl disable --now photos-offsite.timer && sudo rm /etc/systemd/system/photos-offsite.{service,timer} && sudo systemctl daemon-reload && sudo rm -rf /opt/photos-offsite
```

Then the fstab lines, `/etc/photos-offsite`, the user (`sudo deluser photos-offsite`), the healthchecks.io check, and
the stack (the bucket is kept; empty it first if it's to go).
