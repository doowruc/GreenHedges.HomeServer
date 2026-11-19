# UGREEN DXP2800 GT: fans and LEDs (host setup)

Under Ubuntu, the DXP2800 GT's two fans run at one fixed speed (about 4200 rpm for the CPU fan, 1500 rpm for the
system fan) and its LAN and disk LEDs just cycle, because nothing drives the board's **Chipsea CS201x** chip (chip ID
0x2011 at I/O port 0x2e). This sets that up as UGOS does:

- **The driver:** UGREEN's own, published under GPL-2.0 in
  [ugreen-opensource/kernel-6.12](https://github.com/ugreen-opensource/kernel-6.12/tree/main/drivers/ugreen)
  (`ug_201x.c` for the fans, temperatures, power-fail setting and watchdog; `leds-sio-201x.c` for the LEDs). The
  deploy script fetches both at a pinned commit, checks their SHA-256s, and has DKMS apply
  [`driver/ug201x.patch`](driver/ug201x.patch), build them into `ug201x.ko`, sign it and rebuild it for each new
  kernel. The patch (GPL-2.0, unlike the rest of this repository) makes the driver's `/proc/it86` files root-only
  (UGREEN made them writable by anyone, so any account could stop the fans), fixes a memory leak and two small bugs, and
  lets LEDs turn on for any brightness.
- **[`ug-hw`](ug-hw.sh), a small service**, in place of UGOS's `hwmonitor`. Every 10 seconds:
  - **Fans:** UGREEN's own curves, from UGOS 1.21.1's `/etc/default/dxp2800gt.conf`, with UGOS's straight lines
    between the points. The CPU fan follows the CPU (`k10temp`); the system fan follows the NVMe drives, or the SATA
    drives on their own curve when there are any (with the `drivetemp` module), whichever needs more. At idle that's
    about 1400 and 730 rpm. If a temperature can't be read, or the service stops for any reason, both fans go to full
    speed.
  - **LEDs:** power steady white; network steady white while the cable is connected, blinking orange (by the chip)
    while it isn't; the two disk LEDs off.

The defaults can be overridden in `/etc/ug-hw/ug-hw.env` ([example](ug-hw.env.example)), such as a curve.

**With Secure Boot on** (as it should be), the kernel only loads modules signed by a key it trusts, so DKMS needs a
signing key (a **MOK**) enrolled once in shim, at the console. The key stays on the box, readable by root only, so DKMS
can sign each new kernel's module unattended. Adding a MOK doesn't change TPM PCR 7, so a TPM-unlocked disk still
unlocks by itself (checked on this box).

## 1. DKMS (on the server)

The kernel headers come with `linux-generic`.

```bash
sudo apt install dkms
```

## 2. Signing key (on the server, then at the console)

Ubuntu's DKMS signs with the key in `/var/lib/shim-signed/mok/`. If that's missing when it first builds a module, it
makes one and stops at a "Configuring Secure Boot" dialog to queue it, so create it first. It's marked for kernel
modules only (the `1.3.6.1.4.1.2312.16.1.2` usage), so shim won't trust it for a bootloader.

```bash
sudo install -d -m 0755 /var/lib/shim-signed/mok && sudo test ! -e /var/lib/shim-signed/mok/MOK.priv && sudo openssl req -new -x509 -newkey rsa:2048 -nodes -days 36500 -subj "/CN=DXP2800 GT DKMS module signing/" -addext "basicConstraints=critical,CA:FALSE" -addext "keyUsage=digitalSignature" -addext "extendedKeyUsage=codeSigning,1.3.6.1.4.1.2312.16.1.2" -keyout /var/lib/shim-signed/mok/MOK.priv -outform DER -out /var/lib/shim-signed/mok/MOK.der && sudo chmod 600 /var/lib/shim-signed/mok/MOK.priv
```

(It stops if a key is already there: use that one instead.) Queue it for enrolment. It asks for a one-time password,
8 to 16 characters, typed once more at the console in a moment (letters only: the console's keyboard layout is US):

```bash
sudo mokutil --import /var/lib/shim-signed/mok/MOK.der
```

Then reboot **at the console**: a blue **MokManager** screen comes up early, for 10 seconds; press a key, then
**Enroll MOK** → **Continue** → **Yes**, the password, **Reboot**. Missed it? It boots normally; run `mokutil --import`
again. Afterwards:

```bash
mokutil --test-key /var/lib/shim-signed/mok/MOK.der
```

should say it's already enrolled, and `sudo journalctl -k -b | grep "DXP2800 GT DKMS"` shows the kernel loading it.

## 3. Deploy (on the Mac)

```bash
deploy/dxp2800gt/deploy-dxp2800gt.sh [user@host]
```

It stops unless the host is a DXP2800 GT with DKMS and an enrolled key. It builds and installs the driver, installs
`ug-hw` (`/usr/local/sbin/ug-hw`, `ug-hw.service`) and `/etc/modules-load.d/ug201x.conf`, then restarts `ug-hw` on the
new module (the fans run at full speed for a few seconds) and shows its first log lines. Run it again after changing
anything here; bump `PACKAGE_VERSION` in [`driver/dkms.conf`](driver/dkms.conf) when the patch changes.

## Day to day

```bash
sudo cat /proc/it86/fan
```

```bash
journalctl -u ug-hw -b -o cat | tail
```

`ug-hw` logs each speed change, with the temperatures behind it, and each change of the network LED.

- **Kernel updates:** DKMS rebuilds and signs the module by itself (`dkms status ug201x`). If a build ever fails, the
  module is missing after the reboot, `ug-hw` can't start, and the fans stay at the chip's own fixed speed: loud but
  safe.
- **Watchdog:** the driver adds the chip's watchdog as `/dev/watchdog0`. Nothing uses it while the box runs
  (systemd's `RuntimeWatchdogSec` is off); systemd arms it for 10 minutes while shutting down or rebooting, so a hung
  shutdown still ends in a reset, and the driver turns it off again at the reboot and when it loads.
- **Taking it out:** `sudo systemctl disable --now ug-hw` (fans to full speed), `sudo rm /etc/modules-load.d/ug201x.conf`,
  `sudo dkms remove ug201x/<version> --all`, then reboot.
