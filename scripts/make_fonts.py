"""Instance Manrope[wght].ttf into static TTFs and copy IBM Plex Mono statics.

Usage: python -I make_fonts.py <download_dir> <out_fonts_dir>
"""
import shutil
import sys
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

dl = Path(sys.argv[1])
out = Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)

FAMILY = "Manrope"
STYLES = [(400, "Regular"), (500, "Medium"), (600, "SemiBold"), (700, "Bold")]

vf_path = dl / "manrope__Manrope[wght].ttf"

for wght, style in STYLES:
    vf = TTFont(vf_path)
    font = instancer.instantiateVariableFont(vf, {"wght": wght}, inplace=False, updateFontNames=False)
    for tag in ("STAT",):
        if tag in font:
            del font[tag]

    ribbi = style in ("Regular", "Bold")
    legacy_family = FAMILY if ribbi else f"{FAMILY} {style}"
    legacy_sub = style if ribbi else "Regular"
    ps_name = f"{FAMILY}-{style}"
    full_name = f"{FAMILY} {style}"

    name = font["name"]
    version = name.getDebugName(5) or "Version 1.000"
    vnum = version.replace("Version ", "").split(";")[0].strip()
    # Drop variable-font-only names and any stale family/style records.
    for nid in (1, 2, 3, 4, 6, 16, 17, 25):
        name.removeNames(nameID=nid)
    # Remove instance names pointed to by fvar (ids >= 256) that are now unused.
    name.names = [r for r in name.names if r.nameID < 256 or r.nameID == 0]

    def set_name(nid, value):
        name.setName(value, nid, 3, 1, 0x409)
        name.setName(value, nid, 1, 0, 0)

    set_name(1, legacy_family)
    set_name(2, legacy_sub)
    set_name(3, f"{vnum};{(font['OS/2'].achVendID or 'NONE').strip()};{ps_name}")
    set_name(4, full_name)
    set_name(6, ps_name)
    set_name(16, FAMILY)
    set_name(17, style)

    os2 = font["OS/2"]
    os2.usWeightClass = wght
    sel = os2.fsSelection
    sel &= ~((1 << 0) | (1 << 5) | (1 << 6) | (1 << 9))  # italic, bold, regular, oblique
    if style == "Bold":
        sel |= 1 << 5
    else:
        sel |= 1 << 6
    os2.fsSelection = sel

    head = font["head"]
    head.macStyle = (1 if style == "Bold" else 0)

    dest = out / f"{FAMILY}-{style}.ttf"
    font.save(dest)
    print("wrote", dest)

for style in ("Regular", "Medium", "SemiBold"):
    src = dl / f"ibmplexmono__IBMPlexMono-{style}.ttf"
    dest = out / f"IBMPlexMono-{style}.ttf"
    shutil.copyfile(src, dest)
    print("copied", dest)
