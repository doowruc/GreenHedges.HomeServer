# Off-site bucket (AWS CloudFormation)

[`offsite.yaml`](offsite.yaml) creates the S3 bucket that every service's encrypted off-site copies go to, and one **write-only** IAM user per service:

| Resource | Settings |
|---|---|
| Bucket | Private (all public access blocked, bucket-owner-enforced), SSE-S3 encryption, versioning, TLS-only bucket policy. Copies (current and older versions) move to **Glacier Deep Archive** after 30 days; nothing is ever deleted. Kept if the stack is deleted. |
| `<stack>-libre` | May only `s3:PutObject` under `librecollector/` |
| `<stack>-weather` | May only `s3:PutObject` under `weathercollector/` |
| `<stack>-vehicles` | May only `s3:PutObject` under `vehiclemanager/` (Vehicle Manager) |
| `<stack>-payandpensions` | May only `s3:PutObject` under `payandpensions/` (Pay & Pensions) |

The services encrypt with `age` before uploading (the host holds only the public key), so the bucket only ever sees ciphertext. A leaked key can add files under one prefix, nothing more.

## Deploy

Needs the AWS CLI signed in as an administrator, in the region you want the bucket in. Pick a globally unique bucket name.

```bash
aws cloudformation deploy --region eu-west-2 --stack-name greenhedges-offsite --template-file deploy/aws/offsite.yaml --parameter-overrides BucketName=<bucket> --capabilities CAPABILITY_NAMED_IAM
```

The stack name prefixes the IAM user names (`greenhedges-offsite-libre`, `greenhedges-offsite-weather`, `greenhedges-offsite-vehicles`, `greenhedges-offsite-payandpensions`). Re-run the same command after changing the template; CloudFormation applies only the difference.

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

The prefix defaults to the service's name in lower case (`librecollector`, `weathercollector`, `vehiclemanager`, `payandpensions`), which matches the template. Services may share one `age` key pair or have one each; restoring needs the matching private key.

The service remembers the last backup it copied (`backups/.offsite-last`). When switching an existing service to a new bucket, delete that file before restarting so the newest backup is copied straight away rather than after the next nightly backup:

```bash
sudo rm /var/lib/librecollector/backups/.offsite-last
sudo systemctl restart librecollector
```

Then the log shows `Off-site backup to s3://<bucket>/librecollector` and `Off-site copy uploaded: …`. Do a restore test as in [Libre's HOST-SETUP §8b](../libre/HOST-SETUP.md) step 5.

## Checks

CI lints the template with `cfn-lint` on every pull request.
