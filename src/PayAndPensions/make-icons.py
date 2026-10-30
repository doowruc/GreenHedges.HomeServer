#!/usr/bin/env python3
"""Makes Pay & Pensions' icons in wwwroot from one design: Bootstrap Icons' cash-stack (MIT, as in the navbar),
white on the navbar's dark grey. Needs rsvg-convert (brew install librsvg). Run from the repo root:

    python3 src/PayAndPensions/make-icons.py

It writes wwwroot/favicon.svg and favicon.ico (16, 32, 48 px), and wwwroot/icons/: apple-touch-icon.png (180 px,
full-bleed: iOS rounds the corners), icon-192.png and icon-512.png (rounded, the manifest's "any"), and
icon-maskable-512.png (full-bleed, the notes within the centre 80% that Android's masks keep).
"""
import os, struct, subprocess, tempfile

W = os.path.join(os.path.dirname(os.path.abspath(__file__)), "GreenHedges.HomeServer.PayAndPensions", "wwwroot")
S = tempfile.mkdtemp()
os.makedirs(os.path.join(W, "icons"), exist_ok=True)
CASH = ("M1 3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1zm7 8a2 2 0 1 0 0-4 2 2 0 0 0 0 4M0 5a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H1a1 1 0 0 1-1-1zm3 0a2 2 0 0 1-2 2v4a2 2 0 0 1 2 2h10a2 2 0 0 1 2-2V7a2 2 0 0 1-2-2z")
BG = "#212529"   # Bootstrap's dark grey: the navbar
NOTE = "<!-- Pay & Pensions' icon: Bootstrap Icons cash-stack (MIT, (c) The Bootstrap Authors), as in the navbar. -->"

def svg(rounded, glyph_width):
    # A 512 canvas; the notes (16 x 12 units, centred on 8,8) scaled to glyph_width.
    k = glyph_width / 16
    bg = f'<rect width="512" height="512" rx="{96 if rounded else 0}" fill="{BG}"/>'
    glyph = f'<path fill="#fff" transform="translate(256 256) scale({k:.4f}) translate(-8 -8)" d="{CASH}"/>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">{NOTE}{bg}{glyph}</svg>\n'

def png(source, size, out):
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", out, source], check=True)

variants = {
    "rounded": svg(True, 384),    # tabs, manifest "any"
    "square": svg(False, 352),    # apple-touch-icon: iOS rounds the corners itself
    "maskable": svg(False, 288),  # Android's masks keep the centre 80%: the notes stay inside it
}
for name, text in variants.items():
    open(os.path.join(S, f"{name}.svg"), "w").write(text)

open(os.path.join(W, "favicon.svg"), "w").write(variants["rounded"])
png(os.path.join(S, "square.svg"), 180, os.path.join(W, "icons", "apple-touch-icon.png"))
png(os.path.join(S, "rounded.svg"), 192, os.path.join(W, "icons", "icon-192.png"))
png(os.path.join(S, "rounded.svg"), 512, os.path.join(W, "icons", "icon-512.png"))
png(os.path.join(S, "maskable.svg"), 512, os.path.join(W, "icons", "icon-maskable-512.png"))

# favicon.ico: 16, 32 and 48 px PNGs inside an ICO container (supported by every browser that reads ICO).
images = []
for size in (16, 32, 48):
    out = os.path.join(S, f"fav-{size}.png")
    png(os.path.join(S, "rounded.svg"), size, out)
    images.append((size, open(out, "rb").read()))
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + 16 * len(images)
entries, data = b"", b""
for size, blob in images:
    entries += struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(blob), offset + len(data))
    data += blob
open(os.path.join(W, "favicon.ico"), "wb").write(header + entries + data)
print("done")
