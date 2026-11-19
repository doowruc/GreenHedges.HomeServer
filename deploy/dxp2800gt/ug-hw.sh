#!/usr/bin/env bash
# ug-hw: the UGREEN DXP2800 GT's fans and LEDs, through UGREEN's ug201x driver (deploy/dxp2800gt/driver).
# Run by ug-hw.service as root. "ug-hw failsafe" just sets both fans to full speed (the service's ExecStopPost).
#
# Fans: UGREEN's own curves, from UGOS 1.21.1's /etc/default/dxp2800gt.conf, applied as UGOS's hwmonitor-amd does:
# straight lines between the points. The CPU fan follows the CPU (k10temp Tctl); the system fan follows the hotter
# of the SATA drives (on the "ata" curve, when there are any with a drivetemp reading) and the NVMe drives (on the
# "ssd" curve). Speeds are 1-255. If a temperature can't be read, both fans go to 255 until it can.
# LEDs: power steady white; network white while the cable is connected, blinking orange while it isn't; the two
# disk LEDs off (the bays are empty for now).
# Every setting can be overridden in /etc/ug-hw/ug-hw.env (read by the service).
set -uo pipefail

CPUFAN_CURVE="${CPUFAN_CURVE:-0,25 60,39 65,54 70,68 75,83 80,98 85,122 88,200 90,255}"
SYSFAN_CURVE_ATA="${SYSFAN_CURVE_ATA:-0,67 40,78 45,121 50,132 55,154 58,200 60,255}"
SYSFAN_CURVE_SSD="${SYSFAN_CURVE_SSD:-0,67 50,78 55,121 60,132 65,154 70,200 75,255}"
INTERVAL="${INTERVAL:-10}"             # seconds between checks
NET_IF="${NET_IF:-}"                    # the interface the network LED shows; empty: the default route's
REFRESH="${REFRESH:-60}"                # rewrite unchanged fan speeds this often (seconds), in case the chip reset

FAN=/proc/it86/fan
LEDS=/sys/class/leds
WHITE=1 ORANGE=2

log() { echo "$*"; }

set_fans() {  # set_fans <cpu> <sys>
  echo "cpu $1" > "$FAN" && echo "set $2" > "$FAN"
}

failsafe() { set_fans 255 255; }

if [ "${1:-}" = failsafe ]; then
  failsafe && log "Fans at full speed"
  exit 0
fi

# hwmon_temps <name>: every temp1_input (in whole °C) of the hwmon devices with that name, one per line.
hwmon_temps() {
  local h
  for h in /sys/class/hwmon/hwmon*; do
    [ "$(cat "$h/name" 2>/dev/null)" = "$1" ] || continue
    [ -r "$h/temp1_input" ] && echo $(( $(cat "$h/temp1_input") / 1000 ))
  done
}

max_of() { sort -n | tail -n 1; }

# curve <"t,p t,p ..."> <temp>: the speed for that temperature, interpolated and rounded as UGOS does.
curve() {
  awk -v pts="$1" -v t="$2" 'BEGIN {
    n = split(pts, p, " ")
    for (i = 1; i <= n; i++) { split(p[i], q, ","); T[i] = q[1] + 0; P[i] = q[2] + 0 }
    if (t <= T[1]) v = P[1]
    else if (t >= T[n]) v = P[n]
    else for (i = 1; i < n; i++) if (t < T[i + 1]) { v = P[i] + int((P[i + 1] - P[i]) * (t - T[i]) / (T[i + 1] - T[i]) + 0.5); break }
    if (v < 1) v = 1; if (v > 255) v = 255
    print v
  }'
}

led() {  # led <name> <attribute> <value>
  echo "$3" > "$LEDS/$1/$2"
}

led_steady() {  # led_steady <name> <colour>
  led "$1" trigger none && led "$1" color "$2" && led "$1" brightness 255
}

led_blink() {  # led_blink <name> <colour>: the chip blinks it, 300 ms on, 700 off
  led "$1" trigger none && led "$1" color "$2" && led "$1" trigger timer \
    && led "$1" delay_on 300 && led "$1" delay_off 700
}

led_off() { led "$1" trigger none && led "$1" brightness 0; }

net_if() {
  if [ -n "$NET_IF" ]; then echo "$NET_IF"; else ip -o route show default 2>/dev/null | awk '{print $5; exit}'; fi
}

[ -w "$FAN" ] || { echo "No $FAN: is the ug201x driver loaded?" >&2; exit 1; }
trap 'failsafe; log "Stopping: fans at full speed"; exit 0' TERM INT

led_steady power "$WHITE"
led_off disk1
led_off disk2

last_fans="" last_write=0 last_net="" iface=""
while :; do
  cpu="$(hwmon_temps k10temp | max_of)"
  nvme="$(hwmon_temps nvme | max_of)"
  sata="$(hwmon_temps drivetemp | max_of)"

  if [ -z "$cpu" ] || [ -z "$nvme" ]; then
    fans="255 255"
    why="can't read the CPU (${cpu:-?}) or NVMe (${nvme:-?}) temperature: full speed"
  else
    cpufan="$(curve "$CPUFAN_CURVE" "$cpu")"
    sysfan="$(curve "$SYSFAN_CURVE_SSD" "$nvme")"
    if [ -n "$sata" ]; then
      atafan="$(curve "$SYSFAN_CURVE_ATA" "$sata")"
      [ "$atafan" -gt "$sysfan" ] && sysfan="$atafan"
    fi
    fans="$cpufan $sysfan"
    why="CPU ${cpu}°C, NVMe ${nvme}°C${sata:+, SATA ${sata}°C}"
  fi

  now="$(date +%s)"
  if [ "$fans" != "$last_fans" ] || [ $(( now - last_write )) -ge "$REFRESH" ]; then
    if set_fans $fans; then
      [ "$fans" != "$last_fans" ] && log "Fans: CPU ${fans% *}, system ${fans#* } ($why)"
      last_fans="$fans" last_write="$now"
    else
      log "Couldn't write $FAN"
    fi
  fi

  [ -n "$iface" ] || iface="$(net_if)"
  if [ "$(cat "/sys/class/net/${iface:-none}/carrier" 2>/dev/null)" = 1 ]; then net=up; else net=down; fi
  if [ "$net" != "$last_net" ]; then
    if [ "$net" = up ]; then led_steady network_stat "$WHITE"; else led_blink network_stat "$ORANGE"; fi
    log "Network LED: ${iface:-no interface} $net"
    last_net="$net"
  fi

  sleep "$INTERVAL" & wait $!
done
