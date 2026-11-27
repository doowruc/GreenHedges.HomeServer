# Photos off-site: the plan

The plan for replacing the old file server's off-site photo backup, a CloudBerry Online Backup plan running as a
Windows Home Server add-in, with a job on the home server. It has to be in place, and proven by a restore, before the
old file server is retired.

**Status:** planned 2026-11-27. Nothing written or deployed yet. The folder list, the current bucket's figures and the
restore-test log are in the private notes (`photos-offsite/NOTES.md`).

## What it has to do

1. **Keep an off-site copy of every photo** in the photo share and in a **named list** of folders in the private
   photos share, with a few sub-folders (mostly videos) left out. The list is deliberate: folders not on it stay out.
2. **Run nightly**, unattended, catching up after a missed night; never slow down working hours.
3. **Keep a deleted or overwritten photo for a while** before it's gone for good (CloudBerry: 30 days).
4. **Say when it fails**, and leave a record of each run (CloudBerry: an email report every run).
5. **Restore** one folder, or everything, with only what's in 1Password.

## What it replaces

The CloudBerry plan, as configured today:

| | CloudBerry today |
|---|---|
| Sources | The photo share, and named folders of the private photos share with some sub-folders excluded |
| Filter | Everything except system and hidden files; one device-index folder skipped |
| Storage | S3, its own bucket in eu-west-1, **Reduced Redundancy** (legacy, now dearer than Standard), no lifecycle rule; file-for-file, "advanced" format, versions kept |
| Encryption | None on the client; S3 server-side encryption only |
| Retention | Files deleted locally are removed after 30 days |
| Schedule | Daily 01:00, stop after 15 hours, run a missed backup at startup; a weekly "full" only if the differentials have grown past half its size |
| Reporting | Email after every run, with a detailed report; Windows Event Log on failure |

On 2026-11-27 the bucket held about **240 GB in about 64,500 files** (nearly all JPEG, then RAW, HEIC and a few
large videos), almost all of it Reduced Redundancy: about **$5.70 a month** for storage at $0.024/GB.

## Decided

- **rclone, file for file, to a new Glacier Deep Archive bucket.** Photos don't deduplicate, so restic or Kopia would
  gain little, and both need to read back and rewrite their own pack files to prune, which Deep Archive makes slow and
  costly. A file-for-file copy keeps CloudBerry's model: one object per photo, restorable by folder. MSP360's own Linux
  client would keep the old format, but it's paid and closed, and runs nothing like the rest of this server.
- **Encrypted with rclone crypt**, contents and names, so the folder names (people's names) aren't stored readable;
  plus S3 server-side encryption. The crypt password and salt live in 1Password; without them the copy is useless.
- **Deleted or overwritten photos are kept for 90 days** (was 30), by bucket versioning and a lifecycle rule, not by
  the job, so a compromised server can't shorten it.
- **Start now, from the old file server's shares**, before the drives: a second off-site copy is in place before the
  riskiest step (copying the data off the old server), and CloudBerry keeps running alongside until this is proven.
  When the photos move to the new drives only the source paths change, and nothing is uploaded again as long as the
  copy keeps modification times.
- **The folder list stays a named list** (an include list, as today), held on the server and in the private notes,
  never in this repository.
- **No upload limit outside working hours**; a limit Monday to Friday 08:00-18:00 (the line is symmetric gigabit).

## Design

### The bucket: its own CloudFormation stack

`deploy/aws/photos-offsite.yaml`, a stack of its own (`greenhedges-photos-offsite`, eu-west-2), not a prefix in the
shared off-site bucket: that stack promises no service ever gets list or delete, and a sync needs both.

| Resource | Settings |
|---|---|
| Bucket | Private (public access blocked, bucket-owner-enforced), SSE-S3, TLS-only bucket policy, **versioning on**. Objects uploaded straight to `DEEP_ARCHIVE`. Lifecycle: non-current versions **expire 90 days** after they stop being current; expired delete markers removed; incomplete multipart uploads aborted after 7 days. `DeletionPolicy: Retain` |
| `<stack>-sync` user | `s3:ListBucket`, `s3:GetObject` (for `HEAD`), `s3:PutObject`, `s3:DeleteObject` on the bucket. **Not** `DeleteObjectVersion`, `PutLifecycleConfiguration`, `PutBucketVersioning`, `PutBucketPolicy` or anything else; an explicit deny on those too. A `DeleteObject` on a versioned bucket only adds a delete marker, so the worst a stolen key can do is hide everything for 90 days |

Access key created by hand straight into 1Password, as for the other off-site users (`deploy/aws/README.md`).

### The job

`deploy/photos-offsite/`: `photos-offsite.sh`, `photos-offsite.service` and `.timer`, `filters.example.txt`, a deploy
script and `HOST-SETUP.md`, in the same shape as the Sure, FreshRSS and photo-job scripts.

- **Account:** system user `photos-offsite`, read-only access to the sources and nothing else.
- **Settings:** `/etc/photos-offsite/` (`root:photos-offsite`, 0640): `rclone.conf` (the S3 key, the crypt password and
  salt, already obscured by rclone), `filters.txt` (the folder list), `photos-offsite.env` (heartbeat URL, limits).
- **rclone:** Ubuntu's package (1.60, patched through ESM by unattended upgrades). Everything below is in it.
- **systemd:** timer at **01:00**, `Persistent=true` (a missed night runs at boot), `RuntimeMaxSec=15h`; the service
  `ProtectSystem=strict`, `ProtectHome=yes`, `PrivateTmp=yes`, `NoNewPrivileges=yes`, sources read-only.
- **Nightly:** `rclone sync` with `--fast-list --update --use-server-modtime`: it lists the bucket (about 65 list
  requests) and uploads only what's newer locally than its stored copy, with no per-object `HEAD` and no in-place
  "touch" (S3 can't copy an archived object). New and edited photos are always newer.
- **Monthly** (first Sunday): the same sync with `--size-only` instead, which catches what the nightly rule can't
  (a file replaced by an older-dated one). Also list-only.
- **Safeguards:** stops unless every source is really mounted and non-empty (as the photo job does); `--max-delete`
  stops a run that would delete more than about **10%** of the copies; `--bwlimit` from a timetable (Monday to
  Friday, 08:00-18:00 limited, otherwise off).
- **Filter:** the include list from `filters.txt`, then excludes for `.*`, `Thumbs.db`, `desktop.ini`, `@eaDir/`
  and the device-index folder (SMB doesn't carry the Windows hidden and system flags, so these are by name).
  **Warns** (not fails) when a top-level folder appears in the private share that's in neither list, so a new folder is
  a decision, not an accident.
- **Reporting:** a healthchecks.io check, "Photos off-site", period 1 day, grace 15 hours. The script pings `/start`,
  then success or `/fail` with rclone's summary (files and bytes copied, deleted, errors) as the ping body, so each
  run's report is in healthchecks.io's log; healthchecks.io emails on a failure or a missed run. The full log is in
  the journal (`journalctl -u photos-offsite`).

### The sources, now and later

| | Now (old file server) | Later (the drives) |
|---|---|---|
| Photo share | A read-only CIFS automount, like the media server's, but under `/srv/backup/` and with its own **backup reader** account (read-only, on the two photo shares only; it's the only account here allowed into the private share) | The ZFS dataset, read-only to `photos-offsite` |
| Private photos share | The same, second automount | The same |
| Consistency | None (SMB), fine for photos | A ZFS snapshot each run; rclone reads `.zfs/snapshot/` |

When the data moves, copy it with modification times kept (`rsync -a`), change the two paths in the env file, and
run once with `--dry-run`: it should want to upload nothing.

### Cost

London prices, from the AWS Pricing API on 2026-11-27:

| | |
|---|---|
| Storage | Deep Archive about **$0.002/GB a month**: about **$0.50 a month** for 240 GB (plus 40 KB of overhead an object: about 2.6 GB more) |
| First upload | About $0.06 per 1,000 objects into Deep Archive: about **$4 once**. Every object is billed for at least 180 days |
| Nightly | A few list requests: pennies a month |
| A full restore | Bulk retrieval $0.005/GB (about $1.20) plus data out (roughly $0.09/GB, about $20). Up to 48 hours (Standard: 12 hours, $0.021/GB) |

### Restore

Written into `HOST-SETUP.md` and proven before CloudBerry goes:

1. On any machine with rclone, recreate the remote from 1Password (the S3 key, or the admin's, and the crypt password
   and salt).
2. `rclone backend restore` the folder (through `rclone backend encode` for its encrypted name), priority Bulk,
   lifetime a few days; wait for it.
3. `rclone copy` it down; compare checksums with the originals.
4. A deleted photo is a non-current version: rclone's `--s3-versions` (check in the test that 1.60 handles it through
   crypt).

## Steps

1. **This plan** (this PR) and the private notes.
2. **The stack:** `deploy/aws/photos-offsite.yaml` and its README section, linted in CI. Then you deploy it and create
   the access key into 1Password.
3. **The job:** the script, units, filters example, deploy script and `HOST-SETUP.md`, checked with `bash -n`.
4. **On the old file server:** the backup reader account (1Password). **On the home server:** the account, the two
   mounts, `/etc/photos-offsite/`, the healthchecks.io check; deploy the job.
5. **First run** by hand, evening or weekend: about 240 GB at the old server's SMB speed (about 47 MB/s when the
   books were copied) is around **1.5 hours**, so it fits in one night. CloudBerry keeps running.
6. **Restore test** (above) of a folder from each share, including a deleted file; log it in the private notes.
7. **After about four weeks green:** turn the CloudBerry plan off, delete the `cloudberry-backup` IAM user's key and
   the user, and empty and delete the old bucket (it still holds a folder dropped from the plan long ago: check with
   the private notes first).
8. **When the drives go in:** switch the sources to the ZFS datasets (above).
9. **Ongoing:** a sample restore twice a year; look at the bill after the first full month.

## Out, for now

- Object Lock (stronger than versioning against a stolen key, but hard to undo): versioning and a narrow key are
  enough here.
- A second off-site copy elsewhere, and the Mac pulling the photos: the drives' ZFS mirror plus the old drives kept
  offline cover the local side.
- Backing up anything other than the photos (videos, music, the other shares).
