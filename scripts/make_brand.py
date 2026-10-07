"""Build docs/brand SVGs (mark, app icon, lockups) for Prompuff.

Usage: python -I make_brand.py <ManropeBold.ttf> <out_dir>
"""
import sys
from pathlib import Path

import uharfbuzz as hb
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.svgLib.path import parse_path
from fontTools.ttLib import TTFont

font_path = Path(sys.argv[1])
out = Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)

LAVENDER = "#B48EFF"
INK = "#0A0C10"
TILE = "#151920"
TILE_BORDER = "#2A3240"

BODY = "M6 16.5h10.5a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 6.6 7.2 4.7 4.7 0 0 0 6 16.5Z"
MOUTH = "M10 13.9q1.25 1.1 2.5 0"


def n(v: float) -> str:
    t = f"{v:.3f}".rstrip("0").rstrip(".")
    return "0" if t in ("-0", "") else t


# Exact bounds of the cloud body (arcs are converted to cubics by fontTools).
bp = BoundsPen(None)
parse_path(BODY, bp)
bx0, by0, bx1, by1 = bp.bounds
bw, bh = bx1 - bx0, by1 - by0
print(f"cloud bounds x {bx0:.3f}..{bx1:.3f} y {by0:.3f}..{by1:.3f} (w {bw:.3f} h {bh:.3f})")
bcx, bcy = (bx0 + bx1) / 2, (by0 + by1) / 2


def puff(transform: str = "") -> str:
    attr = f' transform="{transform}"' if transform else ""
    return (
        f"<g{attr}>"
        f'<path d="{BODY}" fill="{LAVENDER}"/>'
        f'<circle cx="9" cy="12" r="1" fill="{INK}"/>'
        f'<circle cx="13.5" cy="12" r="1" fill="{INK}"/>'
        f'<path d="{MOUTH}" fill="none" stroke="{INK}" stroke-width=".9" stroke-linecap="round" stroke-linejoin="round"/>'
        "</g>"
    )


def svg(w: float, h: float, body: str, title: str, vb=None, px_scale=1.0) -> str:
    vb = vb or (0, 0, w, h)
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{n(w * px_scale)}" height="{n(h * px_scale)}" '
        f'viewBox="{" ".join(n(v) for v in vb)}" role="img" aria-label="{title}">'
        f"<title>{title}</title>{body}</svg>\n"
    )


# --- mark.svg: tight viewBox around the cloud with a little padding ---------------
pad = 0.5
vb = (bx0 - pad, by0 - pad, bw + 2 * pad, bh + 2 * pad)
(out / "mark.svg").write_text(
    svg(vb[2], vb[3], puff(), "Puff", vb=vb, px_scale=8), encoding="utf-8", newline="\n"
)

# --- app-icon.svg: 512 rounded tile, cloud width = 68% of tile -------------------
S = 512
radius = round(S * 0.22)
border = 8
scale = 0.68 * S / bw
tx = S / 2 - bcx * scale
ty = S / 2 - bcy * scale
tile = (
    f'<rect x="{n(border / 2)}" y="{n(border / 2)}" width="{n(S - border)}" height="{n(S - border)}" '
    f'rx="{n(radius - border / 2)}" fill="{TILE}" stroke="{TILE_BORDER}" stroke-width="{border}"/>'
)
(out / "app-icon.svg").write_text(
    svg(S, S, tile + puff(f"translate({n(tx)} {n(ty)}) scale({scale:.5f})"), "Prompuff"),
    encoding="utf-8",
    newline="\n",
)
print(f"app-icon: scale {scale:.4f}, cloud {bw * scale:.1f}x{bh * scale:.1f}px centred at 256,256")

# --- lockups: Puff + "Prompuff" in Manrope Bold, outlined --------------------------
font = TTFont(font_path)
upm = font["head"].unitsPerEm
cap = font["OS/2"].sCapHeight
gs = font.getGlyphSet()

blob = hb.Blob.from_file_path(str(font_path))
hbfont = hb.Font(hb.Face(blob))
buf = hb.Buffer()
buf.add_str("Prompuff")
buf.guess_segment_properties()
hb.shape(hbfont, buf, {"kern": True, "liga": True})
order = font.getGlyphOrder()
glyphs = [(order[i.codepoint], p.x_advance, p.x_offset, p.y_offset) for i, p in zip(buf.glyph_infos, buf.glyph_positions)]
print("shaped glyphs:", [g[0] for g in glyphs])

FONT_SIZE = 100.0
fs = FONT_SIZE / upm
cap_px = cap * fs
mark_h = cap_px * 1.6
gap = 0.35 * mark_h
m = mark_h / bh
mark_w = bw * m
tracking = -0.01 * upm  # letter-spacing -0.01em, in font units

# First glyph's left side bearing so the gap is measured ink-to-ink.
first_bounds = BoundsPen(gs)
gs[glyphs[0][0]].draw(first_bounds)
lsb = first_bounds.bounds[0]

baseline = 0.0  # temporary; everything is shifted into a 0-origin viewBox afterwards
text_x0 = mark_w + gap - lsb * fs

word_pen_parts = []
wb = BoundsPen(gs)
x = 0.0
for idx, (gname, adv, xo, yo) in enumerate(glyphs):
    t = (fs, 0, 0, -fs, text_x0 + (x + xo) * fs, baseline - yo * fs)
    sp = SVGPathPen(gs, ntos=n)
    gs[gname].draw(TransformPen(sp, t))
    word_pen_parts.append(sp.getCommands())
    gs[gname].draw(TransformPen(wb, t))
    x += adv + (tracking if idx < len(glyphs) - 1 else 0)
wx0, wy0, wx1, wy1 = wb.bounds

# Mark: vertically centred on the cap-height midline, left edge at x=0.
mark_cy = baseline - cap_px / 2
mx = 0 - bx0 * m
my = mark_cy - bcy * m
mark_y0, mark_y1 = mark_cy - mark_h / 2, mark_cy + mark_h / 2

x0 = min(0.0, wx0)
y0 = min(mark_y0, wy0)
x1 = max(mark_w, wx1)
y1 = max(mark_y1, wy1)
W, H = x1 - x0, y1 - y0
print(f"lockup: cap {cap_px:.1f} mark {mark_w:.1f}x{mark_h:.1f} gap {gap:.1f} word ink {wx0:.1f}..{wx1:.1f} / {wy0:.1f}..{wy1:.1f}; size {W:.1f}x{H:.1f}")

word_d = " ".join(p for p in word_pen_parts if p)
for name, color in (("lockup.svg", "#171B22"), ("lockup-dark.svg", "#E8ECF1")):
    body = (
        f'<g transform="translate({n(-x0)} {n(-y0)})">'
        + puff(f"translate({n(mx)} {n(my)}) scale({m:.5f})")
        + f'<path d="{word_d}" fill="{color}"/>'
        + "</g>"
    )
    (out / name).write_text(svg(W, H, body, "Prompuff"), encoding="utf-8", newline="\n")

print("wrote", sorted(p.name for p in out.glob("*.svg")))
