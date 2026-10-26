#!/usr/bin/env bash
# Publish a service for Linux and install or update it on the host, then restart it.
# Run on the dev machine from anywhere in the repo:
#   deploy/deploy.sh <libre|weather|vehicle-manager> [--self-contained] [user@host]
# (deploy/<service>/deploy.sh does the same for its service.)
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/<service>/HOST-SETUP.md). You'll be asked for your sudo password on the host.
#
# Builds run on the host's shared .NET runtime (Ubuntu's aspnetcore-runtime package, patched by unattended upgrades),
# and the deploy stops if the host's runtime is older than the one this SDK ships. --self-contained bundles the SDK's
# runtime in the build instead: for a security fix Ubuntu hasn't shipped yet. Redeploy without it once Ubuntu has.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ -f "$ROOT/deploy/local.env" ]; then . "$ROOT/deploy/local.env"; fi

USAGE="Usage: deploy/deploy.sh <libre|weather|vehicle-manager> [--self-contained] [user@host]"
case "${1:-}" in
  libre)   PROJECT="src/Collectors/Libre/GreenHedges.HomeServer.Collectors.Libre.Worker";     SERVICE=librecollector;   FRAMEWORK=Microsoft.NETCore.App ;;
  weather) PROJECT="src/Collectors/Weather/GreenHedges.HomeServer.Collectors.Weather.Worker"; SERVICE=weathercollector; FRAMEWORK=Microsoft.NETCore.App ;;
  vehicle-manager) PROJECT="src/VehicleManager/GreenHedges.HomeServer.VehicleManager";     SERVICE=vehiclemanager;   FRAMEWORK=Microsoft.AspNetCore.App ;;
  *) echo "$USAGE" >&2; exit 2 ;;
esac
COLLECTOR="$1"
shift
SELF_CONTAINED=false
HOST_ARG=""
for arg in "$@"; do
  case "$arg" in
    --self-contained) SELF_CONTAINED=true ;;
    -*) echo "$USAGE" >&2; exit 2 ;;
    *) if [ -n "$HOST_ARG" ]; then echo "$USAGE" >&2; exit 2; fi; HOST_ARG="$arg" ;;
  esac
done
UNIT="$ROOT/deploy/$COLLECTOR/$SERVICE.service"
HOST="${HOST_ARG:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/deploy.sh $COLLECTOR [--self-contained] [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac
OUT="$ROOT/artifacts/publish/$COLLECTOR/linux-x64"

# One shared SSH connection for every step, so the key is unlocked once per deploy.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

if $SELF_CONTAINED; then
  echo "==> Publishing $SERVICE (Release, linux-x64, self-contained)"
  PUBLISH_MODE=--self-contained
else
  echo "==> Publishing $SERVICE (Release, linux-x64, for the host's shared .NET runtime)"
  PUBLISH_MODE=--no-self-contained
fi
rm -rf "$OUT"
dotnet publish "$ROOT/$PROJECT" -c Release -r linux-x64 "$PUBLISH_MODE" -o "$OUT" -v quiet -nologo
# Development settings are never wanted on the host.
rm -f "$OUT/appsettings.Development.json"

ssh "${SSH_OPTS[@]}" -fN "$HOST"   # open the shared connection (asks for the key passphrase once)

if ! $SELF_CONTAINED; then
  # The runtime this SDK (the one global.json picks) would bundle: the host's must be at least as new, or deploying
  # this way would undo a security fix. ASP.NET Core ships with the same version numbers.
  SDK_VERSION="$(cd "$ROOT" && dotnet --version)"
  SDK_DIR="$(cd "$ROOT" && dotnet --list-sdks | awk -v v="$SDK_VERSION" '$1 == v { sub(/^[^[]*\[/, ""); sub(/\]$/, ""); print }')"
  WANT="$(sed -n 's:.*<BundledNETCoreAppPackageVersion>\(.*\)</BundledNETCoreAppPackageVersion>.*:\1:p' \
    "$SDK_DIR/$SDK_VERSION/Microsoft.NETCoreSdk.BundledVersions.props")"
  CHANNEL="${WANT%.*}"   # e.g. 10.0
  echo "==> Checking $HOST has $FRAMEWORK $WANT or later"
  HAVE="$(ssh "${SSH_OPTS[@]}" "$HOST" "dotnet --list-runtimes 2>/dev/null || true" \
    | awk -v f="$FRAMEWORK" -v c="$CHANNEL." '$1 == f && index($2, c) == 1 { print $2 }' | sort -t. -k3,3n | tail -n 1)"
  if [ -z "$HAVE" ]; then
    echo "$HOST has no $FRAMEWORK $CHANNEL. Install it there (sudo apt install aspnetcore-runtime-$CHANNEL)," >&2
    echo "or deploy with --self-contained." >&2
    exit 1
  fi
  HAVE_PATCH="${HAVE##*.}"
  if [ "${HAVE_PATCH%%-*}" -lt "${WANT##*.}" ]; then
    echo "$HOST's $FRAMEWORK is $HAVE, older than $WANT (this SDK's, which may have security fixes)." >&2
    echo "Wait for Ubuntu's update (apt-cache policy aspnetcore-runtime-$CHANNEL), or deploy with --self-contained." >&2
    exit 1
  fi
fi

echo "==> Copying to $HOST"
rsync -az --delete -e "ssh ${SSH_OPTS[*]}" "$OUT/" "$HOST:$SERVICE-release/"
scp -q "${SSH_OPTS[@]}" "$UNIT" "$HOST:$SERVICE-release.service"

echo "==> Installing and restarting $SERVICE on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" "set -e
  sudo rsync -a --delete --chown=root:root ~/$SERVICE-release/ /opt/$SERVICE/
  sudo install -m 0644 -o root -g root ~/$SERVICE-release.service /etc/systemd/system/$SERVICE.service
  rm -rf ~/$SERVICE-release ~/$SERVICE-release.service
  sudo systemctl daemon-reload
  sudo systemctl enable --quiet $SERVICE
  sudo systemctl restart $SERVICE
  sleep 5
  systemctl --no-pager --lines=0 status $SERVICE || true
  echo
  sudo journalctl -u $SERVICE -n 15 --no-pager -o cat"

exit
}
