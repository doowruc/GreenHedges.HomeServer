#!/usr/bin/env bash
# Install or update the UGREEN DXP2800 GT hardware support on the host: UGREEN's driver for the board's Chipsea CS201x
# chip (fans, temperatures, LEDs), built and signed by DKMS as ug201x.ko, and ug-hw, the service that runs the fans
# on UGREEN's curves and sets the LEDs.
# Run on the dev machine:  deploy/dxp2800gt/deploy-dxp2800gt.sh [user@host]
# The host defaults to COLLECTORS_SSH from deploy/local.env (see local.env.example); the script stops unless the host
# is a DXP2800 GT. The host must be prepared first (deploy/dxp2800gt/HOST-SETUP.md: DKMS and an enrolled signing key).
# You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$HERE/../local.env" ]; then . "$HERE/../local.env"; fi
HOST="${1:-${COLLECTORS_SSH:-}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/dxp2800gt/deploy-dxp2800gt.sh [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# UGREEN's sources, pinned: ugreen-opensource/kernel-6.12, drivers/ugreen/, at this commit, with these SHA-256s.
UGREEN_COMMIT=0e4d73a6893d2aaa9772345c85bcccb3798784c1
UGREEN_FILES=(
  "ug_201x.c a59042ec0a5f3b699a671821fae36ea575d1568d3388fe8429978481497c8acc"
  "leds-sio-201x.c 3cf4d535866990be60de212241791f3564e1297770eab4b51c2461342084e138"
)
# The DKMS package version: UGREEN's commit, then our patch's revision (bump it when ug201x.patch changes).
DRIVER_VERSION="$(sed -n 's/^PACKAGE_VERSION="\(.*\)"$/\1/p' "$HERE/driver/dkms.conf")"
[ -n "$DRIVER_VERSION" ] || { echo "No PACKAGE_VERSION in driver/dkms.conf" >&2; exit 1; }

STAGE="$(mktemp -d)"
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR" "$STAGE"; }
trap cleanup EXIT

echo "==> Fetching UGREEN's driver sources (commit ${UGREEN_COMMIT:0:7}) and checking them"
mkdir -p "$STAGE/src/patches"
for entry in "${UGREEN_FILES[@]}"; do
  read -r name sum <<< "$entry"
  curl -fsSL -o "$STAGE/src/$name" \
    "https://raw.githubusercontent.com/ugreen-opensource/kernel-6.12/$UGREEN_COMMIT/drivers/ugreen/$name"
  echo "$sum  $STAGE/src/$name" | shasum -a 256 -c --quiet - \
    || { echo "$name doesn't match its pinned SHA-256: not installing" >&2; exit 1; }
done
cp "$HERE/driver/dkms.conf" "$HERE/driver/Kbuild" "$STAGE/src/"
cp "$HERE/driver/ug201x.patch" "$STAGE/src/patches/"
# The patch must apply to exactly these files, or DKMS would stop halfway on the host.
(cd "$STAGE" && cp -R src check && cd check && patch -p1 --dry-run --quiet < patches/ug201x.patch > /dev/null) \
  || { echo "driver/ug201x.patch doesn't apply to UGREEN's files" >&2; exit 1; }

echo "==> Copying the driver and ug-hw to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"
ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/dxp2800gt-release && mkdir ~/dxp2800gt-release'
scp -q -r "${SSH_OPTS[@]}" "$STAGE/src" "$HOST:dxp2800gt-release/"
scp -q "${SSH_OPTS[@]}" "$HERE/ug-hw.sh" "$HERE/ug-hw.service" "$HERE/modules-load.conf" "$HOST:dxp2800gt-release/"

echo "==> Building, signing and installing ug201x $DRIVER_VERSION and ug-hw on $HOST"
REMOTE="$(cat <<'EOF'
set -e
[ "$(cat /sys/class/dmi/id/product_name)" = "DXP2800 GT" ] || { echo "Not a DXP2800 GT: stopping"; exit 1; }
command -v dkms > /dev/null || { echo "Missing dkms (see deploy/dxp2800gt/HOST-SETUP.md)"; exit 1; }
# Ubuntu DKMS signs with the shim-signed key, and makes and queues a new one (with a prompt) when that is missing.
sudo test -f /var/lib/shim-signed/mok/MOK.priv || { echo "Missing the signing key /var/lib/shim-signed/mok/MOK.priv (see HOST-SETUP.md)"; exit 1; }
mokutil --test-key /var/lib/shim-signed/mok/MOK.der 2>/dev/null | grep -q "is already enrolled" \
  || { echo "The signing key isn't enrolled yet (see HOST-SETUP.md, \"Signing key\")"; exit 1; }
# Replace every installed version, this one included: DKMS keeps each version it was given.
for v in $(dkms status ug201x 2>/dev/null | sed -n 's#^ug201x/\([^,:]*\).*#\1#p' | sort -u); do
  sudo dkms remove "ug201x/$v" --all
  sudo rm -rf "/usr/src/ug201x-$v"
done
sudo rm -rf "/usr/src/ug201x-$DRIVER_VERSION"
sudo cp -R ~/dxp2800gt-release/src "/usr/src/ug201x-$DRIVER_VERSION"
sudo chown -R root:root "/usr/src/ug201x-$DRIVER_VERSION"
sudo dkms install "ug201x/$DRIVER_VERSION"
modinfo -F signer ug201x | sed "s/^/Signed by: /"

# ug-hw, and the modules loaded at boot.
sudo install -m 0755 -o root -g root ~/dxp2800gt-release/ug-hw.sh /usr/local/sbin/ug-hw
sudo install -m 0644 -o root -g root ~/dxp2800gt-release/ug-hw.service /etc/systemd/system/ug-hw.service
sudo install -m 0644 -o root -g root ~/dxp2800gt-release/modules-load.conf /etc/modules-load.d/ug201x.conf
sudo install -d -m 0755 /etc/ug-hw
rm -rf ~/dxp2800gt-release
sudo systemctl daemon-reload

# Stop ug-hw (it leaves the fans at full speed), swap in the module just built, then start it again.
sudo systemctl stop ug-hw.service 2>/dev/null || true
if lsmod | grep -q "^ug201x "; then sudo rmmod ug201x; fi
sudo systemctl enable --now ug-hw.service
sleep 3
echo
dkms status ug201x
systemctl --no-pager --lines=0 status ug-hw.service | head -n 3
sudo journalctl -u ug-hw.service -b --no-pager -n 6 -o cat
sudo cat /proc/it86/fan
EOF
)"
ssh -t "${SSH_OPTS[@]}" "$HOST" "DRIVER_VERSION='$DRIVER_VERSION'; $REMOTE"

echo "==> Done"

}
