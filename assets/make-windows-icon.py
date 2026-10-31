#!/usr/bin/env python3
"""Draws the Clipo icon (the shapes of clipo-icon.svg) into a multi-size .ico.

No dependencies: shapes are rasterized with 4x4 supersampling and stored as
PNG images inside the .ico, which Windows Vista and later read.

    python3 assets/make-windows-icon.py windows/Clipo/Clipo.ico
    python3 assets/make-windows-icon.py windows/Clipo/ClipoPrivate.ico private

The "private" variant is the tray icon while private mode is on: a grey
tile with an orange badge, so it differs at a glance, also in grey tones.
"""
import struct
import sys
import zlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SS = 4  # samples per pixel side

# (x, y, w, h, corner radius, RGB) in the SVG's 1024 x 1024 space, back to front.
SHAPES = [
    (0, 0, 1024, 1024, 224, (0x3B, 0x6F, 0xE0)),
    (272, 236, 480, 600, 64, (0xFF, 0xFF, 0xFF)),
    (392, 184, 240, 120, 48, (0x1F, 0x3F, 0x8F)),
    (352, 440, 320, 40, 20, (0x3B, 0x6F, 0xE0)),
    (352, 540, 320, 40, 20, (0x3B, 0x6F, 0xE0)),
    (352, 640, 200, 40, 20, (0x3B, 0x6F, 0xE0)),
]

# The same clipboard on a grey tile, with an orange badge and a white bar.
PRIVATE_SHAPES = [(0, 0, 1024, 1024, 224, (0x59, 0x5F, 0x6E))] + SHAPES[1:3] + [
    (352, 440, 320, 40, 20, (0x59, 0x5F, 0x6E)),
    (352, 540, 320, 40, 20, (0x59, 0x5F, 0x6E)),
    (352, 640, 200, 40, 20, (0x59, 0x5F, 0x6E)),
    (544, 544, 480, 480, 240, (0xE0, 0x6B, 0x1F)),
    (644, 748, 280, 72, 36, (0xFF, 0xFF, 0xFF)),
]


def inside(px, py, x, y, w, h, r):
    if not (x <= px < x + w and y <= py < y + h):
        return False
    cx = min(max(px, x + r), x + w - r)
    cy = min(max(py, y + r), y + h - r)
    return (px - cx) ** 2 + (py - cy) ** 2 <= r * r


def render(size, shapes):
    scale = 1024 / (size * SS)
    rows = []
    for j in range(size):
        row = bytearray([0])  # PNG filter: none
        for i in range(size):
            r = g = b = a = 0.0
            for sj in range(SS):
                for si in range(SS):
                    px = (i * SS + si + 0.5) * scale
                    py = (j * SS + sj + 0.5) * scale
                    hit = None
                    for x, y, w, h, rad, color in reversed(shapes):
                        if inside(px, py, x, y, w, h, rad):
                            hit = color
                            break
                    if hit:
                        r += hit[0]
                        g += hit[1]
                        b += hit[2]
                        a += 1
            n = SS * SS
            if a:
                row += bytes([round(r / a), round(g / a), round(b / a), round(255 * a / n)])
            else:
                row += bytes(4)
        rows.append(bytes(row))
    return png(size, b"".join(rows))


def png(size, raw):
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main(path, variant):
    shapes = PRIVATE_SHAPES if variant == "private" else SHAPES
    images = [render(size, shapes) for size in SIZES]
    out = bytearray(struct.pack("<HHH", 0, 1, len(images)))
    offset = 6 + 16 * len(images)
    for size, data in zip(SIZES, images):
        out += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for data in images:
        out += data
    with open(path, "wb") as f:
        f.write(out)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "")
