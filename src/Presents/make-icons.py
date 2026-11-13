#!/usr/bin/env python3
"""Makes Presents' icons in wwwroot from one design: Bootstrap Icons' gift (MIT, as in the navbar),
white on the navbar's dark grey. Needs rsvg-convert (brew install librsvg). Run from the repo root:

    python3 src/Presents/make-icons.py

It writes wwwroot/favicon.svg and favicon.ico (16, 32, 48 px), and wwwroot/icons/: apple-touch-icon.png (180 px,
full-bleed: iOS rounds the corners), icon-192.png and icon-512.png (rounded, the manifest's "any"), and
icon-maskable-512.png (full-bleed, the gift within the centre 80% that Android's masks keep).
"""
import os, struct, subprocess, tempfile

W = os.path.join(os.path.dirname(os.path.abspath(__file__)), "GreenHedges.HomeServer.Presents", "wwwroot")
S = tempfile.mkdtemp()
os.makedirs(os.path.join(W, "icons"), exist_ok=True)
GIFT = ("M3 2.5a2.5 2.5 0 0 1 5 0 2.5 2.5 0 0 1 5 0v.006c0 .07 0 .27-.038.494H15a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1v7.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 1 14.5V7a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h2.038A3 3 0 0 1 3 2.506zm1.068.5H7v-.5a1.5 1.5 0 1 0-3 0c0 .085.002.274.045.43zM9 3h2.932l.023-.07c.043-.156.045-.345.045-.43a1.5 1.5 0 0 0-3 0zM1 4v2h6V4zm8 0v2h6V4zm5 3H9v8h4.5a.5.5 0 0 0 .5-.5zm-7 8V7H2v7.5a.5.5 0 0 0 .5.5z")
BG = "#212529"   # Bootstrap's dark grey: the navbar
NOTE = "<!-- Presents' icon: Bootstrap Icons gift (MIT, (c) The Bootstrap Authors), as in the navbar. -->"

def svg(rounded, glyph_width):
    # A 512 canvas; the gift (16 x 16 units, centred on 8,8) scaled to glyph_width.
    k = glyph_width / 16
    bg = f'<rect width="512" height="512" rx="{96 if rounded else 0}" fill="{BG}"/>'
    glyph = (f'<g fill="#fff" transform="translate(256 256) scale({k:.4f}) translate(-8 -8)">'
             f'<path d="{GIFT}"/></g>')
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">{NOTE}{bg}{glyph}</svg>\n'

def png(source, size, out):
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", out, source], check=True)

variants = {
    "rounded": svg(True, 320),    # tabs, manifest "any"
    "square": svg(False, 320),    # apple-touch-icon: iOS rounds the corners itself
    "maskable": svg(False, 256),  # Android's masks keep the centre 80%: the gift stays inside it
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
