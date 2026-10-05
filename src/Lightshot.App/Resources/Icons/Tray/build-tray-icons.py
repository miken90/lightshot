#!/usr/bin/env python3
"""Rebuild tray-light/dark .svg and multi-size .ico from one glyph definition.

Run: python3 build-tray-icons.py   (needs Pillow). The glyph is four viewfinder corner
brackets around a solid dot on a 24x24 grid. "light" is the ink for a light taskbar theme
(dark glyph); "dark" is the ink for a dark taskbar theme (light glyph).
"""
import io
import os
import struct

from PIL import Image, ImageDraw

SIZES = (16, 20, 24, 32, 40, 48)
VARIANTS = {"light": (0x1F, 0x1F, 0x1F), "dark": (0xF5, 0xF5, 0xF5)}
STROKE = 2.4
DOT_RADIUS = 3.6
BRACKETS = (
    ((3, 9), (3, 3), (9, 3)),
    ((15, 3), (21, 3), (21, 9)),
    ((3, 15), (3, 21), (9, 21)),
    ((21, 15), (21, 21), (15, 21)),
)
SUPERSAMPLE = 16


def svg_text(rgb):
    color = "#%02X%02X%02X" % rgb
    paths = " ".join(
        "M%d,%d L%d,%d L%d,%d" % (a[0], a[1], b[0], b[1], c[0], c[1]) for a, b, c in BRACKETS
    )
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">\n'
        '  <path d="%s" fill="none" stroke="%s" stroke-width="%s"'
        ' stroke-linecap="round" stroke-linejoin="round"/>\n'
        '  <circle cx="12" cy="12" r="%s" fill="%s"/>\n'
        "</svg>\n" % (paths, color, STROKE, DOT_RADIUS, color)
    )


def render(size, rgb):
    big = size * SUPERSAMPLE
    scale = big / 24.0
    mask = Image.new("L", (big, big), 0)
    draw = ImageDraw.Draw(mask)
    radius = STROKE * scale / 2.0
    for bracket in BRACKETS:
        points = [(x * scale, y * scale) for x, y in bracket]
        draw.line(points, fill=255, width=int(round(STROKE * scale)), joint="curve")
        for x, y in points:
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=255)
    c = 12 * scale
    r = DOT_RADIUS * scale
    draw.ellipse((c - r, c - r, c + r, c + r), fill=255)
    mask = mask.resize((size, size), Image.LANCZOS)
    img = Image.new("RGBA", (size, size), rgb + (0,))
    img.putalpha(mask)
    return img


def ico_bytes(images):
    blobs = []
    for img in images:
        buf = io.BytesIO()
        img.save(buf, format="PNG")
        blobs.append(buf.getvalue())
    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for img, blob in zip(images, blobs):
        w = img.width if img.width < 256 else 0
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    return out + entries + b"".join(blobs)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    for name, rgb in VARIANTS.items():
        with open(os.path.join(here, "tray-%s.svg" % name), "w", newline="\n") as f:
            f.write(svg_text(rgb))
        with open(os.path.join(here, "tray-%s.ico" % name), "wb") as f:
            f.write(ico_bytes([render(s, rgb) for s in SIZES]))


if __name__ == "__main__":
    main()
