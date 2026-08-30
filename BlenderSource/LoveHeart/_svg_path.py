"""SVG path helpers for the Love Heart builder. Pure Python, no bpy."""
from __future__ import annotations

import re
from pathlib import Path


_TOKEN = re.compile(
    r"([MmLlHhVvCcSsQqTtAaZz])|([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)"
)


def tokenize(d: str):
    tokens = []
    for cmd, num in _TOKEN.findall(d):
        if cmd:
            tokens.append(("cmd", cmd))
        else:
            tokens.append(("num", float(num)))
    return tokens


def parse_path(d: str):
    """Return list of contours, each a list of (x, y, hlx, hly, hrx, hry) bezier points.

    Handle left/right are absolute coords. Lines get aligned handles at the point.
    """
    tokens = tokenize(d)
    i = 0
    contours = []
    pts = []
    cx = cy = 0.0
    sx = sy = 0.0
    last_cmd = ""
    start_handles = (0.0, 0.0)

    def add_point(x, y, hlx, hly, hrx, hry):
        pts.append([x, y, hlx, hly, hrx, hry])

    def ensure_pt():
        if not pts:
            add_point(cx, cy, cx, cy, cx, cy)

    while i < len(tokens):
        kind, val = tokens[i]
        if kind == "cmd":
            cmd = val
            i += 1
        else:
            cmd = last_cmd
            if cmd in "Mm":
                cmd = "L" if cmd == "M" else "l"

        rel = cmd.islower()
        c = cmd.upper()
        last_cmd = cmd

        def nums(n):
            nonlocal i
            out = []
            for _ in range(n):
                if i >= len(tokens) or tokens[i][0] != "num":
                    raise ValueError(f"expected number for {cmd} at {i}")
                out.append(tokens[i][1])
                i += 1
            return out

        if c == "M":
            if pts:
                contours.append(pts)
                pts = []
            x, y = nums(2)
            if rel:
                x += cx
                y += cy
            cx, cy = x, y
            sx, sy = x, y
            add_point(x, y, x, y, x, y)
            last_cmd = "l" if rel else "L"
        elif c == "L":
            x, y = nums(2)
            if rel:
                x += cx
                y += cy
            ensure_pt()
            pts[-1][4] = pts[-1][0]
            pts[-1][5] = pts[-1][1]
            add_point(x, y, x, y, x, y)
            cx, cy = x, y
        elif c == "H":
            (x,) = nums(1)
            if rel:
                x += cx
            y = cy
            ensure_pt()
            pts[-1][4] = pts[-1][0]
            pts[-1][5] = pts[-1][1]
            add_point(x, y, x, y, x, y)
            cx, cy = x, y
        elif c == "V":
            (y,) = nums(1)
            if rel:
                y += cy
            x = cx
            ensure_pt()
            pts[-1][4] = pts[-1][0]
            pts[-1][5] = pts[-1][1]
            add_point(x, y, x, y, x, y)
            cx, cy = x, y
        elif c == "C":
            x1, y1, x2, y2, x, y = nums(6)
            if rel:
                x1 += cx
                y1 += cy
                x2 += cx
                y2 += cy
                x += cx
                y += cy
            ensure_pt()
            pts[-1][4] = x1
            pts[-1][5] = y1
            add_point(x, y, x2, y2, x, y)
            cx, cy = x, y
        elif c == "Z":
            if pts:
                # close: last handle-right already set; first point handle-left
                # if last != first, the close is a line
                x0, y0 = pts[0][0], pts[0][1]
                if abs(cx - x0) > 1e-6 or abs(cy - y0) > 1e-6:
                    pts[-1][4] = pts[-1][0]
                    pts[-1][5] = pts[-1][1]
                    pts[0][2] = pts[0][0]
                    pts[0][3] = pts[0][1]
                contours.append(pts)
                pts = []
            cx, cy = sx, sy
        else:
            raise ValueError(f"unsupported SVG command {cmd}")

    if pts:
        contours.append(pts)
    return contours


def extract_fill_d(svg_text: str) -> str:
    """Pick the primary filled path (skip mask-offset strokes and background rects)."""
    paths = re.findall(
        r'<path[^>]*\sd="([^"]+)"[^>]*>',
        svg_text,
    )
    if not paths:
        raise ValueError("no path in svg")
    # Prefer the shortest substantial fill path when multiple exist (mask offset is huge)
    if len(paths) == 1:
        return paths[0]
    scored = sorted(paths, key=len)
    # drop tiny / empty
    scored = [p for p in scored if len(p) > 50]
    return scored[0]


def write_clean_svgs(folder: Path) -> dict:
    folder = Path(folder)
    mapping = {
        "heart": folder / "heart_asset.svg",
        "love": folder / "love.svg",
        "house": folder / "house.svg",
        "for_the": folder / "for_the.svg",
        "of": folder / "of.svg",
    }
    out = {}
    for name, src in mapping.items():
        d = extract_fill_d(src.read_text())
        contours = parse_path(d)
        npts = sum(len(c) for c in contours)
        out[name] = {"d": d, "contours": len(contours), "points": npts}
        print(f"{name}: {len(contours)} contours, {npts} points, d={len(d)}")
    return out


if __name__ == "__main__":
    here = Path(__file__).resolve().parent
    write_clean_svgs(here)
