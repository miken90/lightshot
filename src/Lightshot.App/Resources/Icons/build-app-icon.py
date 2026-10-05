#!/usr/bin/env python3
"""Rebuild app.svg and the multi-size app.ico from one definition.

Run: python3 build-app-icon.py   (needs Pillow and numpy).

The icon is a gradient tile carrying the tray glyph: four viewfinder corner brackets around a
dot. The brackets and dot geometry are imported from Tray/build-tray-icons.py, so the app icon
and the tray glyph cannot drift apart. The tile and dot colours are the only additions.

Two masters share that glyph. The "detail" master (48 px and up) has a drop shadow, a soft top
gloss and a glyph with the tray's own proportions. The "small" master (below 48 px) drops the
effects, lets the tile fill the canvas and thickens the glyph, because a 1.2 px stroke at 16 px
turns to grey. app.svg is the detail master.
"""
import importlib.util
import io
import os
import struct

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = (16, 20, 24, 32, 40, 48, 64, 256)
DETAIL_FROM = 48

TILE_START = (0x4A, 0x58, 0xF5)  # top-left of the tile gradient
TILE_END = (0x0B, 0x9B, 0xD0)  # bottom-right
GLYPH = (0xFF, 0xFF, 0xFF)
DOT = (0xFF, 0xC5, 0x3D)
SHADOW_ALPHA = 0.30
GLOSS_ALPHA = 0.20

# Geometry on the 256 grid. The glyph is the tray's 24 grid mapped through x' = origin + scale * x.
DETAIL = {"inset": 16, "radius": 52, "scale": 8.0, "stroke": None, "dot": None, "effects": True}
SMALL = {"inset": 0, "radius": 60, "scale": 9.5, "stroke": 3.0, "dot": 3.9, "effects": False}


def _load_tray():
    spec = importlib.util.spec_from_file_location(
        "build_tray_icons", os.path.join(HERE, "Tray", "build-tray-icons.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TRAY = _load_tray()


def _params(size):
    p = dict(DETAIL if size >= DETAIL_FROM else SMALL)
    p["stroke"] = p["stroke"] or TRAY.STROKE
    p["dot"] = p["dot"] or TRAY.DOT_RADIUS
    return p


def _hex(rgb):
    return "#%02X%02X%02X" % rgb


def svg_text():
    p = _params(DETAIL_FROM)
    a, size = p["inset"], 256 - 2 * p["inset"]
    scale = p["scale"]

    def pt(x, y):
        return "%g,%g" % (128 + scale * (x - 12), 128 + scale * (y - 12))

    paths = " ".join("M%s L%s L%s" % (pt(*a_), pt(*b_), pt(*c_)) for a_, b_, c_ in TRAY.BRACKETS)
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">\n'
        "  <defs>\n"
        '    <linearGradient id="tile" x1="0" y1="0" x2="1" y2="1">\n'
        '      <stop offset="0" stop-color="%s"/>\n'
        '      <stop offset="1" stop-color="%s"/>\n'
        "    </linearGradient>\n"
        '    <linearGradient id="gloss" x1="0" y1="0" x2="0" y2="1">\n'
        '      <stop offset="0" stop-color="#FFFFFF" stop-opacity="%s"/>\n'
        '      <stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/>\n'
        "    </linearGradient>\n"
        '    <filter id="shadow" x="-20%%" y="-20%%" width="140%%" height="150%%">\n'
        '      <feGaussianBlur stdDeviation="6"/>\n'
        "    </filter>\n"
        "  </defs>\n"
        '  <rect x="%d" y="%d" width="%d" height="%d" rx="%d" fill="#000000"'
        ' fill-opacity="%s" filter="url(#shadow)" transform="translate(0 6)"/>\n'
        '  <rect x="%d" y="%d" width="%d" height="%d" rx="%d" fill="url(#tile)"/>\n'
        '  <rect x="%d" y="%d" width="%d" height="%d" rx="%d" fill="url(#gloss)"/>\n'
        '  <path d="%s" fill="none" stroke="%s" stroke-width="%g"'
        ' stroke-linecap="round" stroke-linejoin="round"/>\n'
        '  <circle cx="128" cy="128" r="%g" fill="%s"/>\n'
        "</svg>\n"
        % (
            _hex(TILE_START),
            _hex(TILE_END),
            GLOSS_ALPHA,
            a, a, size, size, p["radius"], SHADOW_ALPHA,
            a, a, size, size, p["radius"],
            a, a, size, int(size * 0.55), p["radius"],
            paths, _hex(GLYPH), p["stroke"] * scale,
            p["dot"] * scale, _hex(DOT),
        )
    )


def _rounded_mask(canvas, box, radius):
    mask = Image.new("L", (canvas, canvas), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    return mask


def _layer(canvas, rgb, mask, opacity=1.0):
    layer = Image.new("RGBA", (canvas, canvas), rgb + (0,))
    layer.putalpha(mask.point(lambda v: int(v * opacity)))
    return layer


def render(size):
    p = _params(size)
    ss = 8 if size <= 64 else 4
    canvas = size * ss
    k = canvas / 256.0
    lo, hi = p["inset"] * k, (256 - p["inset"]) * k
    box = (lo, lo, hi - 1, hi - 1)

    tile_mask = _rounded_mask(canvas, box, p["radius"] * k)

    # Diagonal gradient over the tile's own bounding box.
    ys, xs = np.mgrid[0:canvas, 0:canvas].astype(np.float32)
    t = np.clip(((xs - lo) + (ys - lo)) / (2 * (hi - lo)), 0, 1)[..., None]
    start = np.array(TILE_START, np.float32)
    end = np.array(TILE_END, np.float32)
    grad = Image.fromarray((start + (end - start) * t).astype(np.uint8), "RGB").convert("RGBA")
    grad.putalpha(tile_mask)

    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))

    if p["effects"]:
        shadow = _layer(canvas, (0, 0, 0), tile_mask, SHADOW_ALPHA)
        shadow = shadow.transform(
            shadow.size, Image.AFFINE, (1, 0, 0, 0, 1, -6 * k)
        ).filter(ImageFilter.GaussianBlur(6 * k))
        img = Image.alpha_composite(img, shadow)

    img = Image.alpha_composite(img, grad)

    if p["effects"]:
        gloss_h = (hi - lo) * 0.55
        fade = np.clip(1 - (ys - lo) / gloss_h, 0, 1) * GLOSS_ALPHA * 255
        gloss = Image.new("RGBA", (canvas, canvas), (255, 255, 255, 0))
        gloss.putalpha(Image.fromarray(fade.astype(np.uint8), "L"))
        gloss_mask = Image.composite(gloss.getchannel("A"), Image.new("L", gloss.size, 0), tile_mask)
        gloss.putalpha(gloss_mask)
        img = Image.alpha_composite(img, gloss)

    scale = p["scale"] * k
    origin = 128 * k

    def pt(x, y):
        return (origin + scale * (x - 12), origin + scale * (y - 12))

    glyph = Image.new("L", (canvas, canvas), 0)
    draw = ImageDraw.Draw(glyph)
    width = p["stroke"] * scale
    for bracket in TRAY.BRACKETS:
        points = [pt(x, y) for x, y in bracket]
        draw.line(points, fill=255, width=int(round(width)), joint="curve")
        for x, y in points:
            r = width / 2.0
            draw.ellipse((x - r, y - r, x + r, y + r), fill=255)

    if p["effects"]:
        glyph_shadow = _layer(canvas, (0x0B, 0x1E, 0x5C), glyph, 0.35)
        glyph_shadow = glyph_shadow.transform(
            glyph_shadow.size, Image.AFFINE, (1, 0, 0, 0, 1, -4 * k)
        ).filter(ImageFilter.GaussianBlur(3 * k))
        img = Image.alpha_composite(img, glyph_shadow)

    img = Image.alpha_composite(img, _layer(canvas, GLYPH, glyph))

    dot = Image.new("L", (canvas, canvas), 0)
    c, r = origin, p["dot"] * scale
    ImageDraw.Draw(dot).ellipse((c - r, c - r, c + r, c + r), fill=255)
    img = Image.alpha_composite(img, _layer(canvas, DOT, dot))

    return img.resize((size, size), Image.LANCZOS)


def _bmp_frame(img):
    """Classic ICO bitmap: BITMAPINFOHEADER, bottom-up BGRA rows, then an all-zero AND mask."""
    w, h = img.size
    rgba = np.asarray(img.convert("RGBA"), dtype=np.uint8)
    bgra = rgba[::-1, :, [2, 1, 0, 3]].tobytes()
    mask = bytes(((w + 31) // 32) * 4 * h)
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(bgra) + len(mask), 0, 0, 0, 0)
    return header + bgra + mask


def _png_frame(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def ico_bytes(images):
    """Frames below 256 px are BMP for the widest shell compatibility; the 256 px frame is PNG."""
    blobs = [_png_frame(i) if i.width >= 256 else _bmp_frame(i) for i in images]
    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for img, blob in zip(images, blobs):
        w = img.width if img.width < 256 else 0
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    return out + entries + b"".join(blobs)


def main():
    with open(os.path.join(HERE, "app.svg"), "w", newline="\n") as f:
        f.write(svg_text())
    with open(os.path.join(HERE, "app.ico"), "wb") as f:
        f.write(ico_bytes([render(s) for s in SIZES]))


if __name__ == "__main__":
    main()
