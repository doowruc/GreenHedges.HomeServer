#!/usr/bin/env bash
# Publish services for Linux and install or update them on the host, then restart them.
# Run on the dev machine from anywhere in the repo:
#   deploy/deploy.sh <libre|weather|vehicle-manager|pay-and-pensions|all>... [--self-contained] [user@host]
# e.g. deploy/deploy.sh all, or deploy/deploy.sh libre weather. (deploy/<service>/deploy.sh does one service.)
# Every service is published before anything is copied, so a failed build leaves the host untouched; then they're all
# installed and restarted in one SSH session (one sudo password).
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/<service>/HOST-SETUP.md). You'll be asked for your sudo password on the host.
#
# Builds run on the host's shared .NET runtime (Ubuntu's aspnetcore-runtime package, patched by unattended upgrades),
# and the deploy stops if the host's runtime is older than the one this SDK ships. --self-contained bundles the SDK's
# runtime in the builds instead: for a security fix Ubuntu hasn't shipped yet. Redeploy without it once Ubuntu has.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ -f "$ROOT/deploy/local.env" ]; then . "$ROOT/deploy/local.env"; fi

ALL="libre weather vehicle-manager pay-and-pensions"   # in deploy order
USAGE="Usage: deploy/deploy.sh <libre|weather|vehicle-manager|pay-and-pensions|all>... [--self-contained] [user@host]"

# Sets PROJECT, SERVICE (systemd unit, user and folders) and FRAMEWORK (the shared runtime it needs) for one name.
service_info() {
  case "$1" in
    libre)   PROJECT="src/Collectors/Libre/GreenHedges.HomeServer.Collectors.Libre.Worker";     SERVICE=librecollector;   FRAMEWORK=Microsoft.NETCore.App ;;
    weather) PROJECT="src/Collectors/Weather/GreenHedges.HomeServer.Collectors.Weather.Worker"; SERVICE=weathercollector; FRAMEWORK=Microsoft.NETCore.App ;;
    vehicle-manager) PROJECT="src/VehicleManager/GreenHedges.HomeServer.VehicleManager";     SERVICE=vehiclemanager;   FRAMEWORK=Microsoft.AspNetCore.App ;;
    pay-and-pensions) PROJECT="src/PayAndPensions/GreenHedges.HomeServer.PayAndPensions";    SERVICE=payandpensions;   FRAMEWORK=Microsoft.AspNetCore.App ;;
    *) return 1 ;;
  esac
}

SELF_CONTAINED=false
HOST_ARG=""
WANTED=" "   # the names asked for, space-separated, each once
for arg in "$@"; do
  case "$arg" in
    --self-contained) SELF_CONTAINED=true ;;
    -*) echo "$USAGE" >&2; exit 2 ;;
    all) WANTED=" $ALL " ;;
    *@*)
      if [ -n "$HOST_ARG" ]; then echo "$USAGE" >&2; exit 2; fi
      HOST_ARG="$arg" ;;
    *)
      if ! service_info "$arg"; then echo "Unknown service: $arg" >&2; echo "$USAGE" >&2; exit 2; fi
      case "$WANTED" in *" $arg "*) ;; *) WANTED="$WANTED$arg " ;; esac ;;
  esac
done
# Deploy in a fixed order whatever order they were given in.
NAMES=""
for name in $ALL; do
  case "$WANTED" in *" $name "*) NAMES="$NAMES $name" ;; esac
done
if [ -z "$NAMES" ]; then echo "$USAGE" >&2; exit 2; fi
HOST="${HOST_ARG:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "$USAGE (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac

# One shared SSH connection for every step, so the key is unlocked once per deploy.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

if $SELF_CONTAINED; then PUBLISH_MODE=--self-contained; else PUBLISH_MODE=--no-self-contained; fi
FRAMEWORKS=" "
SERVICES=""
for name in $NAMES; do
  service_info "$name"
  if $SELF_CONTAINED; then
    echo "==> Publishing $SERVICE (Release, linux-x64, self-contained)"
  else
    echo "==> Publishing $SERVICE (Release, linux-x64, for the host's shared .NET runtime)"
  fi
  OUT="$ROOT/artifacts/publish/$name/linux-x64"
  rm -rf "$OUT"
  dotnet publish "$ROOT/$PROJECT" -c Release -r linux-x64 "$PUBLISH_MODE" -o "$OUT" -v quiet -nologo
  # Development settings are never wanted on the host.
  rm -f "$OUT/appsettings.Development.json"
  case "$FRAMEWORKS" in *" $FRAMEWORK "*) ;; *) FRAMEWORKS="$FRAMEWORKS$FRAMEWORK " ;; esac
  SERVICES="$SERVICES $SERVICE"
done

ssh "${SSH_OPTS[@]}" -fN "$HOST"   # open the shared connection (asks for the key passphrase once)

if ! $SELF_CONTAINED; then
  # The runtime this SDK (the one global.json picks) would bundle: the host's must be at least as new, or deploying
  # this way would undo a security fix. ASP.NET Core ships with the same version numbers.
  SDK_VERSION="$(cd "$ROOT" && dotnet --version)"
  SDK_DIR="$(cd "$ROOT" && dotnet --list-sdks | awk -v v="$SDK_VERSION" '$1 == v { sub(/^[^[]*\[/, ""); sub(/\]$/, ""); print }')"
  WANT="$(sed -n 's:.*<BundledNETCoreAppPackageVersion>\(.*\)</BundledNETCoreAppPackageVersion>.*:\1:p' \
    "$SDK_DIR/$SDK_VERSION/Microsoft.NETCoreSdk.BundledVersions.props")"
  CHANNEL="${WANT%.*}"   # e.g. 10.0
  RUNTIMES="$(ssh "${SSH_OPTS[@]}" "$HOST" "dotnet --list-runtimes 2>/dev/null || true")"
  for FRAMEWORK in $FRAMEWORKS; do
    echo "==> Checking $HOST has $FRAMEWORK $WANT or later"
    HAVE="$(echo "$RUNTIMES" \
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
  done
fi

for name in $NAMES; do
  service_info "$name"
  echo "==> Copying $SERVICE to $HOST"
  rsync -az --delete -e "ssh ${SSH_OPTS[*]}" "$ROOT/artifacts/publish/$name/linux-x64/" "$HOST:$SERVICE-release/"
  scp -q "${SSH_OPTS[@]}" "$ROOT/deploy/$name/$SERVICE.service" "$HOST:$SERVICE-release.service"
done

echo "==> Installing and restarting$SERVICES on $HOST"
# One session for all of them, so sudo asks once. The services are its arguments.
REMOTE='set -e
  for s in "$@"; do
    sudo rsync -a --delete --chown=root:root ~/$s-release/ /opt/$s/
    sudo install -m 0644 -o root -g root ~/$s-release.service /etc/systemd/system/$s.service
    rm -rf ~/$s-release ~/$s-release.service
  done
  sudo systemctl daemon-reload
  for s in "$@"; do
    sudo systemctl enable --quiet $s
    sudo systemctl restart $s
  done
  sleep 5
  for s in "$@"; do
    echo
    systemctl --no-pager --lines=0 status $s || true
    echo
    sudo journalctl -u $s -n 15 --no-pager -o cat
  done'
ssh -t "${SSH_OPTS[@]}" "$HOST" "bash -c $(printf '%q' "$REMOTE") deploy$SERVICES"

exit
}
