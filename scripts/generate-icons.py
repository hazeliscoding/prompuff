#!/usr/bin/env python3
"""Generate src/Prompuff.App/Controls/IconData.g.cs from Lucide SVG icons.

Each icon's SVG elements (path, circle, ellipse, rect, line, polyline, polygon)
are converted into one combined path-data string in Lucide's 24x24 coordinate
space. Numbers are re-tokenized and emitted space-separated, with an explicit
command letter per segment, so Avalonia's path parser never has to deal with
compact forms like ".5.5", "1.5-2" or arc flags written as "011".

Pure standard library. SVGs are downloaded from unpkg once and cached outside
the repo (see --cache-dir).

Usage:
    python scripts/generate-icons.py [--cache-dir DIR] [--offline]
"""

from __future__ import annotations

import argparse
import re
import sys
import tempfile
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

LUCIDE_VERSION = "0.460.0"
URL_TEMPLATE = "https://unpkg.com/lucide-static@{version}/icons/{name}.svg"

# (PascalCase key, kebab-case Lucide file name). Order is preserved in the output.
ICONS: list[tuple[str, str]] = [
    ("Search", "search"),
    ("ClipboardPlus", "clipboard-plus"),
    ("Plus", "plus"),
    ("Library", "library"),
    ("Heart", "heart"),
    ("Clock", "clock"),
    ("Settings", "settings"),
    ("X", "x"),
    ("ArrowUpDown", "arrow-up-down"),
    ("Copy", "copy"),
    ("CopyPlus", "copy-plus"),
    ("Save", "save"),
    ("History", "history"),
    ("GitFork", "git-fork"),
    ("Play", "play"),
    ("RotateCcw", "rotate-ccw"),
    ("SlidersHorizontal", "sliders-horizontal"),
    ("HardDrive", "hard-drive"),
    ("ArrowLeftRight", "arrow-left-right"),
    ("Keyboard", "keyboard"),
    ("Cloud", "cloud"),
    ("FolderOpen", "folder-open"),
    ("Folder", "folder"),
    ("ExternalLink", "external-link"),
    ("Download", "download"),
    ("Upload", "upload"),
    ("RefreshCw", "refresh-cw"),
    ("FileText", "file-text"),
    ("Scale", "scale"),
    ("Sparkles", "sparkles"),
    ("LayoutTemplate", "layout-template"),
    ("Lightbulb", "lightbulb"),
    ("Check", "check"),
    ("Trash2", "trash-2"),
    ("Pencil", "pencil"),
    ("Ellipsis", "ellipsis"),
    ("Tag", "tag"),
    ("ChevronRight", "chevron-right"),
    ("ChevronDown", "chevron-down"),
    ("Info", "info"),
    ("TriangleAlert", "triangle-alert"),
    ("Minus", "minus"),
    ("Inbox", "inbox"),
    ("LayoutGrid", "layout-grid"),
    ("List", "list"),
    ("Command", "command"),
    ("CornerDownLeft", "corner-down-left"),
    ("Palette", "palette"),
    ("CircleHelp", "circle-help"),
    ("FilePlus", "file-plus"),
    ("FolderPlus", "folder-plus"),
    ("Star", "star"),
    ("Hash", "hash"),
    ("Eye", "eye"),
    ("Undo2", "undo-2"),
    ("Workflow", "workflow"),
    ("ArrowUp", "arrow-up"),
    ("ArrowDown", "arrow-down"),
    ("ArrowRight", "arrow-right"),
    ("CircleCheck", "circle-check"),
]

# Older/newer Lucide names to try when the primary file name is missing.
ALIASES: dict[str, list[str]] = {
    "triangle-alert": ["alert-triangle"],
    "ellipsis": ["more-horizontal"],
    "circle-help": ["help-circle"],
}

# Number of arguments per segment for each path command.
ARG_COUNTS = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}
ARC_FLAG_INDEXES = (3, 4)

NUMBER_RE = re.compile(r"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")
SEPARATORS = " \t\r\n,"


class PathDataError(ValueError):
    pass


def fmt(value: float) -> str:
    """Format a number compactly but unambiguously (no exponent, no '-0')."""
    text = f"{value:.4f}".rstrip("0").rstrip(".")
    if text in ("-0", ""):
        text = "0"
    return text


def _skip_separators(d: str, i: int) -> int:
    while i < len(d) and d[i] in SEPARATORS:
        i += 1
    return i


def parse_path_data(d: str) -> list[tuple[str, list[float]]]:
    """Tokenize SVG path data into (command, args) segments, one per segment.

    Implicit repeated segments are expanded; a repeated moveto becomes lineto.
    Arc flags are read as single '0'/'1' characters.
    """
    segments: list[tuple[str, list[float]]] = []
    i = _skip_separators(d, 0)
    cmd: str | None = None
    while i < len(d):
        ch = d[i]
        if ch.upper() in ARG_COUNTS and ch.isalpha():
            cmd = ch
            i += 1
            if cmd.upper() == "Z":
                segments.append((cmd, []))
                i = _skip_separators(d, i)
                continue
        elif cmd is None:
            raise PathDataError(f"path data must start with a command at {i}: {d!r}")
        elif cmd.upper() == "Z":
            raise PathDataError(f"unexpected arguments after Z at {i}: {d!r}")
        # Read one segment's arguments for the current command.
        count = ARG_COUNTS[cmd.upper()]
        args: list[float] = []
        for k in range(count):
            i = _skip_separators(d, i)
            if i >= len(d):
                raise PathDataError(f"missing arguments for {cmd} in {d!r}")
            if cmd.upper() == "A" and k in ARC_FLAG_INDEXES:
                if d[i] not in "01":
                    raise PathDataError(f"bad arc flag {d[i]!r} at {i}: {d!r}")
                args.append(float(d[i]))
                i += 1
                continue
            m = NUMBER_RE.match(d, i)
            if not m:
                raise PathDataError(f"expected number at {i} ({d[i:i + 10]!r}) in {d!r}")
            args.append(float(m.group()))
            i = m.end()
        segments.append((cmd, args))
        # A moveto followed by extra coordinate pairs implies lineto.
        if cmd == "M":
            cmd = "L"
        elif cmd == "m":
            cmd = "l"
        i = _skip_separators(d, i)
    if not segments or segments[0][0] not in "Mm":
        raise PathDataError(f"path data must start with moveto: {d!r}")
    return segments


def emit(segments: list[tuple[str, list[float]]]) -> str:
    parts: list[str] = []
    for cmd, args in segments:
        parts.append(cmd)
        for k, value in enumerate(args):
            if cmd.upper() == "A" and k in ARC_FLAG_INDEXES:
                parts.append("1" if value else "0")
            else:
                parts.append(fmt(value))
    return " ".join(parts)


def num(el: ET.Element, attr: str, default: float | None = None) -> float:
    raw = el.get(attr)
    if raw is None:
        if default is None:
            raise PathDataError(f"<{local(el.tag)}> missing required attribute {attr!r}")
        return default
    raw = raw.strip()
    if raw.endswith("px"):
        raw = raw[:-2]
    return float(raw)


def points(el: ET.Element) -> list[tuple[float, float]]:
    values = [float(v) for v in NUMBER_RE.findall(el.get("points", ""))]
    if len(values) < 4 or len(values) % 2:
        raise PathDataError(f"bad points on <{local(el.tag)}>: {el.get('points')!r}")
    return list(zip(values[0::2], values[1::2]))


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def element_to_path(el: ET.Element) -> str | None:
    tag = local(el.tag)
    if el.get("transform"):
        raise PathDataError(f"transforms are not supported (<{tag} transform=...>)")
    if tag == "path":
        segments = parse_path_data(el.get("d", ""))
        # A leading relative moveto is absolute in SVG; it must stay absolute once this
        # path is appended to the end of another element's subpaths.
        if segments[0][0] == "m":
            segments[0] = ("M", segments[0][1])
        return emit(segments)
    filled = el.get("fill") not in (None, "none")
    if filled and tag not in ("circle", "ellipse"):
        raise PathDataError(f"filled <{tag}> cannot be expressed as a stroke-only path")
    if tag in ("circle", "ellipse"):
        cx, cy = num(el, "cx", 0), num(el, "cy", 0)
        if tag == "circle":
            rx = ry = num(el, "r")
        else:
            rx, ry = num(el, "rx"), num(el, "ry")
        d = emit_ellipse(cx, cy, rx, ry)
        if filled:
            # Lucide's filled dots (r=.5, stroke-width 2): stroking a circle smaller than
            # the stroke leaves a pinhole in most renderers, so add a round-capped dot at
            # the centre (Lucide's own "h.01" idiom) to fill it.
            if max(rx, ry) > 1:
                raise PathDataError(f"filled <{tag}> with radius > 1 is not supported")
            d += " " + emit([("M", [cx, cy]), ("h", [0.01])])
        return d
    if tag == "rect":
        return emit_rect(el)
    if tag == "line":
        x1, y1 = num(el, "x1", 0), num(el, "y1", 0)
        x2, y2 = num(el, "x2", 0), num(el, "y2", 0)
        return emit([("M", [x1, y1]), ("L", [x2, y2])])
    if tag in ("polyline", "polygon"):
        pts = points(el)
        segs = [("M", list(pts[0]))] + [("L", list(p)) for p in pts[1:]]
        if tag == "polygon":
            segs.append(("Z", []))
        return emit(segs)
    if tag in ("svg", "g", "title", "desc", "defs"):
        return None
    raise PathDataError(f"unsupported element <{tag}>")


def emit_ellipse(cx: float, cy: float, rx: float, ry: float) -> str:
    return emit([
        ("M", [cx - rx, cy]),
        ("A", [rx, ry, 0, 1, 0, cx + rx, cy]),
        ("A", [rx, ry, 0, 1, 0, cx - rx, cy]),
        ("Z", []),
    ])


def emit_rect(el: ET.Element) -> str:
    x, y = num(el, "x", 0), num(el, "y", 0)
    w, h = num(el, "width"), num(el, "height")
    rx_raw, ry_raw = el.get("rx"), el.get("ry")
    rx = num(el, "rx") if rx_raw is not None else None
    ry = num(el, "ry") if ry_raw is not None else None
    if rx is None and ry is None:
        rx = ry = 0.0
    elif rx is None:
        rx = ry
    elif ry is None:
        ry = rx
    rx = min(max(rx, 0.0), w / 2)
    ry = min(max(ry, 0.0), h / 2)
    if rx == 0 or ry == 0:
        return emit([
            ("M", [x, y]), ("L", [x + w, y]), ("L", [x + w, y + h]), ("L", [x, y + h]), ("Z", []),
        ])
    return emit([
        ("M", [x + rx, y]),
        ("L", [x + w - rx, y]),
        ("A", [rx, ry, 0, 0, 1, x + w, y + ry]),
        ("L", [x + w, y + h - ry]),
        ("A", [rx, ry, 0, 0, 1, x + w - rx, y + h]),
        ("L", [x + rx, y + h]),
        ("A", [rx, ry, 0, 0, 1, x, y + h - ry]),
        ("L", [x, y + ry]),
        ("A", [rx, ry, 0, 0, 1, x + rx, y]),
        ("Z", []),
    ])


def svg_to_path(svg_text: str) -> str:
    # Lucide icons never declare a DTD; refuse entity tricks instead of parsing them.
    if "<!DOCTYPE" in svg_text or "<!ENTITY" in svg_text:
        raise PathDataError("SVG with DOCTYPE/ENTITY declarations is not accepted")
    root = ET.fromstring(svg_text)
    if local(root.tag) != "svg":
        raise PathDataError("root element is not <svg>")
    if root.get("viewBox", "0 0 24 24").split() != ["0", "0", "24", "24"]:
        raise PathDataError(f"unexpected viewBox {root.get('viewBox')!r}")
    parts = [p for p in (element_to_path(el) for el in root.iter()) if p]
    if not parts:
        raise PathDataError("icon has no drawable elements")
    return " ".join(parts)


def fetch(name: str, cache_dir: Path, offline: bool) -> str | None:
    cached = cache_dir / f"{name}.svg"
    if cached.exists():
        return cached.read_text(encoding="utf-8")
    if offline:
        return None
    url = URL_TEMPLATE.format(version=LUCIDE_VERSION, name=name)
    req = urllib.request.Request(url, headers={"User-Agent": "prompuff-generate-icons"})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            text = resp.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return None
        raise
    if "<svg" not in text:
        return None
    cache_dir.mkdir(parents=True, exist_ok=True)
    cached.write_text(text, encoding="utf-8", newline="\n")
    return text


def validate(d: str) -> None:
    """Strictly re-parse emitted data: space-separated tokens, known commands, right arity."""
    tokens = d.split(" ")
    if not tokens or tokens[0] not in ("M", "m"):
        raise PathDataError(f"does not start with moveto: {d[:40]!r}")
    plain_number = re.compile(r"-?\d+(?:\.\d+)?")
    i = 0
    while i < len(tokens):
        cmd = tokens[i]
        if cmd.upper() not in ARG_COUNTS or len(cmd) != 1:
            raise PathDataError(f"invalid command token {cmd!r}")
        count = ARG_COUNTS[cmd.upper()]
        args = tokens[i + 1:i + 1 + count]
        if len(args) != count:
            raise PathDataError(f"{cmd} has {len(args)} args, expected {count}")
        for k, tok in enumerate(args):
            if cmd.upper() == "A" and k in ARC_FLAG_INDEXES:
                if tok not in ("0", "1"):
                    raise PathDataError(f"bad arc flag {tok!r}")
            elif not plain_number.fullmatch(tok):
                raise PathDataError(f"bad number token {tok!r}")
        i += 1 + count


def write_cs(paths: dict[str, str], out: Path) -> None:
    lines = [
        f"// <auto-generated> by scripts/generate-icons.py from lucide-static {LUCIDE_VERSION} (ISC). Do not edit by hand.",
        "namespace Prompuff.App.Controls;",
        "",
        "internal static class IconData",
        "{",
        "    public static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> Paths =",
        "        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal)",
        "        {",
    ]
    for key, d in paths.items():
        lines.append(f'            ["{key}"] = "{d}",')
    lines += ["        };", "}", ""]
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--cache-dir",
        type=Path,
        default=Path(tempfile.gettempdir()) / f"prompuff-lucide-{LUCIDE_VERSION}",
        help="where downloaded SVGs are cached (default: system temp dir, outside the repo)",
    )
    parser.add_argument("--out", type=Path, default=repo / "src/Prompuff.App/Controls/IconData.g.cs")
    parser.add_argument("--offline", action="store_true", help="use cached SVGs only")
    args = parser.parse_args()

    paths: dict[str, str] = {}
    aliased: list[str] = []
    for key, name in ICONS:
        svg = None
        used = name
        for candidate in [name, *ALIASES.get(name, [])]:
            svg = fetch(candidate, args.cache_dir, args.offline)
            if svg is not None:
                used = candidate
                break
        if svg is None:
            print(f"error: icon {key} ({name}) not found in lucide-static {LUCIDE_VERSION}", file=sys.stderr)
            return 1
        if used != name:
            aliased.append(f"{key}: {name} -> {used}")
        try:
            d = svg_to_path(svg)
            validate(d)
        except PathDataError as exc:
            print(f"error: {key} ({used}.svg): {exc}", file=sys.stderr)
            return 1
        paths[key] = d

    write_cs(paths, args.out)
    for line in aliased:
        print(f"aliased {line}")
    print(f"generated {len(paths)} icons -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
