#!/usr/bin/env python3
"""Makes Utilities' icons in wwwroot from one design: Bootstrap Icons' speedometer2 (MIT, as in the navbar),
white on the navbar's dark grey. Needs rsvg-convert (brew install librsvg). Run from the repo root:

    python3 src/Utilities/make-icons.py

It writes wwwroot/favicon.svg and favicon.ico (16, 32, 48 px), and wwwroot/icons/: apple-touch-icon.png (180 px,
full-bleed: iOS rounds the corners), icon-192.png and icon-512.png (rounded, the manifest's "any"), and
icon-maskable-512.png (full-bleed, the dial within the centre 80% that Android's masks keep).
"""
import os, struct, subprocess, tempfile

W = os.path.join(os.path.dirname(os.path.abspath(__file__)), "GreenHedges.HomeServer.Utilities", "wwwroot")
S = tempfile.mkdtemp()
os.makedirs(os.path.join(W, "icons"), exist_ok=True)
DIAL = ("M8 4a.5.5 0 0 1 .5.5V6a.5.5 0 0 1-1 0V4.5A.5.5 0 0 1 8 4M3.732 5.732a.5.5 0 0 1 .707 0l.915.914a.5.5 0 1 1-.708.708l-.914-.915a.5.5 0 0 1 0-.707M2 10a.5.5 0 0 1 .5-.5h1.586a.5.5 0 0 1 0 1H2.5A.5.5 0 0 1 2 10m9.5 0a.5.5 0 0 1 .5-.5h1.5a.5.5 0 0 1 0 1H12a.5.5 0 0 1-.5-.5m.754-4.246a.39.39 0 0 0-.527-.02L7.547 9.31a.91.91 0 1 0 1.302 1.258l3.434-4.297a.39.39 0 0 0-.029-.518z")
RIM = ("M0 10a8 8 0 1 1 15.547 2.661c-.442 1.253-1.845 1.602-2.932 1.25C11.309 13.488 9.475 13 8 13c-1.474 0-3.31.488-4.615.911-1.087.352-2.49.003-2.932-1.25A8 8 0 0 1 0 10m8-7a7 7 0 0 0-6.603 9.329c.203.575.923.876 1.68.63C4.397 12.533 6.358 12 8 12s3.604.532 4.923.96c.757.245 1.477-.056 1.68-.631A7 7 0 0 0 8 3")
BG = "#212529"   # Bootstrap's dark grey: the navbar
NOTE = "<!-- Utilities' icon: Bootstrap Icons speedometer2 (MIT, (c) The Bootstrap Authors), as in the navbar. -->"

def svg(rounded, glyph_width):
    # A 512 canvas; the dial (16 x 12 units, centred on 8,8) scaled to glyph_width.
    k = glyph_width / 16
    bg = f'<rect width="512" height="512" rx="{96 if rounded else 0}" fill="{BG}"/>'
    glyph = (f'<g fill="#fff" transform="translate(256 256) scale({k:.4f}) translate(-8 -8)">'
             f'<path d="{DIAL}"/><path fill-rule="evenodd" d="{RIM}"/></g>')
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">{NOTE}{bg}{glyph}</svg>\n'

def png(source, size, out):
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", out, source], check=True)

variants = {
    "rounded": svg(True, 384),    # tabs, manifest "any"
    "square": svg(False, 352),    # apple-touch-icon: iOS rounds the corners itself
    "maskable": svg(False, 288),  # Android's masks keep the centre 80%: the dial stays inside it
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
