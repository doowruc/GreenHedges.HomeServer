# BookOrbit: host setup

[BookOrbit](https://bookorbit.app) in Docker on `127.0.0.1:5082`, at `https://books.<home domain>` through Caddy: a
library and reader for books, comics and manga (EPUB, PDF, CBZ, CBR, CB7 and more), with its own PostgreSQL. How it's
set up: the comments in [`compose.yaml`](compose.yaml).

**First:** Caddy ([deploy/caddy/HOST-SETUP.md](../caddy/HOST-SETUP.md)), and the books folder `/srv/books` mounted at
boot. Here that's an encrypted (LUKS) ext4 partition on a USB drive, unlocked by a key file from `/etc/crypttab` and
mounted from `/etc/fstab`, both with `nofail`; any filesystem mounted there works.

## 1. User and folders (on the server)

A system user for the app, with you in its group so you can add books and read its data:

```bash
sudo useradd --system --home-dir /var/lib/bookorbit --create-home --shell /usr/sbin/nologin bookorbit && sudo chmod 750 /var/lib/bookorbit
```

```bash
sudo usermod -aG bookorbit "$USER"
```

Log out and back in for the group to apply. The app's data, and an empty folder for PostgreSQL, owned by `postgres` in
its image (uid 999). Its container only takes over the `pgdata` folder it creates inside, so it must be able to get
into this one:

```bash
sudo install -d -m 0750 -o bookorbit -g bookorbit /var/lib/bookorbit/app && sudo install -d -m 0700 -o 999 -g 999 /var/lib/bookorbit/postgres
```

```bash
sudo install -d -m 0755 /opt/bookorbit && sudo install -d -m 0750 /etc/bookorbit && sudo install -m 0600 /dev/null /etc/bookorbit/bookorbit.env
```

**The books folder.** If the drive is ever missing, `/srv/books` is just an empty folder on the root disk, and the app
would see an empty library (and anything copied in would fill the root disk). So lock the folder underneath the
mount, then give the mounted drive to `bookorbit`, with group write and setgid so books you copy in stay in its
group:

```bash
sudo umount /srv/books && sudo chmod 000 /srv/books && sudo mount /srv/books
```

```bash
sudo chown bookorbit:bookorbit /srv/books && sudo chmod 2775 /srv/books && ls -ld /srv/books
```

It shows `drwxrwsr-x ... bookorbit bookorbit`. With the drive unmounted, `ls -ld /srv/books` shows `d---------`
owned by root instead: the app gets "permission denied" rather than an empty library, and the deploy script refuses
to run.

## 2. Settings (on the server)

```bash
printf 'APP_URL=https://books.%s\nPUID=%s\nPGID=%s\nPOSTGRES_PASSWORD=%s\nJWT_SECRET=%s\nSETUP_BOOTSTRAP_TOKEN=%s\n' "$(sudo sed -n 's/^HOME_DOMAIN=//p' /etc/caddy/caddy.env)" "$(id -u bookorbit)" "$(id -g bookorbit)" "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" | sudo tee /etc/bookorbit/bookorbit.env > /dev/null
```

It takes the home domain from Caddy's settings and generates the secrets. Check the first three lines, then save the
setup token in your password manager ("Home server BookOrbit setup token"); it's needed once, in step 5:

```bash
sudo head -3 /etc/bookorbit/bookorbit.env
```

```bash
sudo grep SETUP_BOOTSTRAP_TOKEN /etc/bookorbit/bookorbit.env
```

The database password and JWT secret never need typing anywhere, so they can stay in the file only.

## 3. Deploy (on the Mac)

```bash
deploy/bookorbit/deploy-bookorbit.sh
```

The first run downloads both images. It ends with both containers `healthy` and the app's health answer.

## 4. Caddy (on the Mac)

The Caddyfile has the `books` name. Load it:

```bash
deploy/caddy/deploy-caddy.sh
```

## 5. First sign-in (on the Mac, Tailscale on)

Open `https://books.<home domain>`. It goes to the setup page: paste the setup token, then create the admin account
and save it in your password manager ("Home server BookOrbit", with the address). More accounts: **Settings → Users**.
Sign-in is BookOrbit's own accounts; single sign-on (OIDC, e.g. Auth0) can be added later under **Settings**, with no
change here.

## Adding books

Copy them into `/srv/books`, in folders, as yourself (you're in the `bookorbit` group). `--chmod` makes them writable
by the app too, for uploads and metadata edits. From the Mac:

```bash
rsync -rt --info=progress2 --chmod=D2775,F664 ~/Books/Manga/ you@your-host.lan:/srv/books/Manga/
```

Or from a drive mounted on the server, the same with local paths. Then in BookOrbit, **create a library** for each
top folder. The scan mode can't be changed later: **File as Book** for comics and manga (one file per volume, a folder
per series), **Folder as Book** for ebooks and audiobooks (a folder per book). See
<https://bookorbit.app/library-file-structure/>.

## Backups

Not yet: a nightly database dump with an off-site copy comes next. The books themselves aren't backed up here; keep
the originals.

## Updating

Change the image tags in [`compose.yaml`](compose.yaml) in a PR, then re-run `deploy/bookorbit/deploy-bookorbit.sh`.
Read BookOrbit's release notes first (<https://github.com/bookorbit/bookorbit/releases>): its migrations run at
startup and only go forward. A new PostgreSQL major version (pg18 → pg19) can't start on the old data: it needs a
dump and restore, so keep the `-pg18` tag until then.
