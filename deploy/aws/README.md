# Off-site bucket (AWS CloudFormation)

[`offsite.yaml`](offsite.yaml) creates the S3 bucket that every service's encrypted off-site copies go to, and one **write-only** IAM user per service:

| Resource | Settings |
|---|---|
| Bucket | Private (all public access blocked, bucket-owner-enforced), SSE-S3 encryption, versioning, TLS-only bucket policy. Copies (current and older versions) move to **Glacier Deep Archive** after 30 days; nothing is ever deleted. Kept if the stack is deleted. |
| `<stack>-libre` | May only `s3:PutObject` under `librecollector/` |
| `<stack>-weather` | May only `s3:PutObject` under `weathercollector/` |
| `<stack>-vehicles` | May only `s3:PutObject` under `vehiclemanager/` (Vehicle Manager) |
| `<stack>-payandpensions` | May only `s3:PutObject` under `payandpensions/` (Pay & Pensions) |
| `<stack>-utilities` | May only `s3:PutObject` under `utilities/` (Utilities) |
| `<stack>-presents` | May only `s3:PutObject` under `presents/` (Presents) |
| `<stack>-sure` | May only `s3:PutObject` under `sure/` (Sure's database dumps, from [its backup script](../sure/backup.sh)) |
| `<stack>-freshrss` | May only `s3:PutObject` under `freshrss/` (FreshRSS's backups, from [its backup script](../freshrss/backup.sh)) |

The services encrypt with `age` before uploading (the host holds only the public key), so the bucket only ever sees ciphertext. A leaked key can add files under one prefix, nothing more.

## Deploy

Needs the AWS CLI signed in as an administrator, in the region you want the bucket in. Pick a globally unique bucket name.

```bash
aws cloudformation deploy --region eu-west-2 --stack-name greenhedges-offsite --template-file deploy/aws/offsite.yaml --parameter-overrides BucketName=<bucket> --capabilities CAPABILITY_NAMED_IAM
```

The stack name prefixes the IAM user names (`greenhedges-offsite-libre`, `greenhedges-offsite-weather`, `greenhedges-offsite-vehicles`, `greenhedges-offsite-payandpensions`, `greenhedges-offsite-utilities`, `greenhedges-offsite-presents`, `greenhedges-offsite-sure`, `greenhedges-offsite-freshrss`). Re-run the same command after changing the template; CloudFormation applies only the difference.

## Access keys

The template creates no access keys, so no secret ever passes through CloudFormation. For each service, create one yourself and put it straight into your password manager and the host's env file. In the console: **IAM → Users → `<stack>-libre` → Security credentials → Create access key** ("Application running outside AWS"). Or with the CLI (it prints the secret once):

```bash
aws iam create-access-key --user-name greenhedges-offsite-libre
```

## Point a service at it

In the service's env file on the host (e.g. `/etc/librecollector/env`):

```ini
Offsite__Bucket=<bucket>
Offsite__Region=eu-west-2
Offsite__AccessKeyId=AKIA…
Offsite__SecretAccessKey=…
Offsite__AgeRecipient=age1…
```

The prefix defaults to the service's name in lower case (`librecollector`, `weathercollector`, `vehiclemanager`, `payandpensions`, `utilities`, `presents`), which matches the template. Services may share one `age` key pair or have one each; restoring needs the matching private key.

The service remembers the last backup it copied (`backups/.offsite-last`). When switching an existing service to a new bucket, delete that file before restarting so the newest backup is copied straight away rather than after the next nightly backup:

```bash
sudo rm /var/lib/librecollector/backups/.offsite-last
sudo systemctl restart librecollector
```

Then the log shows `Off-site backup to s3://<bucket>/librecollector` and `Off-site copy uploaded: …`. Do a restore test as in [Libre's HOST-SETUP §8b](../libre/HOST-SETUP.md) step 5.

## Checks

CI lints the template with `cfn-lint` on every pull request.

# Photos' off-site bucket

[`photos-offsite.yaml`](photos-offsite.yaml) is a stack of its own for the photos' off-site copy (plan:
[docs/photos-offsite/PLAN.md](../../docs/photos-offsite/PLAN.md)). It isn't a prefix in the bucket above: an rclone
sync has to list and delete, and the users above may only ever add files.

| Resource | Settings |
|---|---|
| Bucket | Private (all public access blocked, bucket-owner-enforced), SSE-S3 encryption, TLS-only bucket policy, **versioning**. The sync uploads straight to **Glacier Deep Archive** (a lifecycle rule moves anything that arrives in another class). A deleted or overwritten photo stays as an older version for **`NoncurrentDays`** (default 90), then goes; current photos never expire. Kept if the stack is deleted |
| `<stack>-sync` | May list the bucket and read (`HEAD`), add and delete its current objects (and send the one ACL S3 accepts with ACLs disabled, which Ubuntu's rclone 1.60 sends with every upload). **Can't** delete versions, restore from Deep Archive or change any bucket setting (an explicit deny as well), so a stolen key can hide photos for `NoncurrentDays` at most |

## Deploy

As an administrator (`aws login`), with a globally unique bucket name:

```bash
aws cloudformation deploy --region eu-west-2 --stack-name greenhedges-photos-offsite --template-file deploy/aws/photos-offsite.yaml --parameter-overrides BucketName=<bucket> --capabilities CAPABILITY_NAMED_IAM
aws cloudformation update-termination-protection --region eu-west-2 --stack-name greenhedges-photos-offsite --enable-termination-protection
```

The IAM user is `greenhedges-photos-offsite-sync`.

## Access key

Created by hand, straight into the password manager, as for the users above ("Application running outside AWS" in the
console, or):

```bash
aws iam create-access-key --user-name greenhedges-photos-offsite-sync
```

It goes in rclone's configuration on the host (`/etc/photos-offsite/rclone.conf`), set up by the photo job's host
setup.

## Restores

Restoring from Deep Archive and reading older versions need an administrator (the sync user can do neither); the
steps are in the photo job's host setup.
