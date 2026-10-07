"""Rasterize app-icon.svg into prompuff.png (512), prompuff-256.png and a multi-size ICO.

ICO entries: 32-bit BMP (DIB + AND mask) below 256 px for maximum compatibility,
PNG-compressed for 256 px. Each size is rendered directly from the SVG.

Usage: python -I make_icons.py <app-icon.svg> <assets_out_dir>
"""
import io
import struct
import sys
from pathlib import Path

import resvg_py
from PIL import Image

svg_path = Path(sys.argv[1])
out = Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]


def render(size: int) -> Image.Image:
    png = bytes(resvg_py.svg_to_bytes(svg_path=str(svg_path), width=size, height=size))
    img = Image.open(io.BytesIO(png)).convert("RGBA")
    assert img.size == (size, size), img.size
    return img


def png_bytes(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def dib_bytes(img: Image.Image) -> bytes:
    w, h = img.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    px = img.load()
    xor = bytearray()
    for y in range(h - 1, -1, -1):  # bottom-up
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    row_bytes = ((w + 31) // 32) * 4
    and_mask = bytearray()
    for y in range(h - 1, -1, -1):
        row = bytearray(row_bytes)
        for x in range(w):
            if px[x, y][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        and_mask += row
    return header + bytes(xor) + bytes(and_mask)


big = render(512)
(out / "prompuff.png").write_bytes(png_bytes(big))
(out / "prompuff-256.png").write_bytes(png_bytes(render(256)))

entries = []
for size in ICO_SIZES:
    img = render(size)
    data = png_bytes(img) if size >= 256 else dib_bytes(img)
    entries.append((size, data))

ico = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
offset = 6 + 16 * len(entries)
for size, data in entries:
    dim = 0 if size >= 256 else size
    ico += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
for _, data in entries:
    ico += data
(out / "prompuff.ico").write_bytes(bytes(ico))
print("wrote", [p.name for p in sorted(out.glob("prompuff*"))])
