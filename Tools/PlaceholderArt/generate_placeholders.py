#!/usr/bin/env python3
"""
Ravenholm Supply Co -- Phase 0 mid-tier placeholder kit generator.

Every sprite here is ORIGINAL procedural art drawn with Pillow primitives by this
script (no external or licensed source images). Re-running the script regenerates
the whole kit deterministically. Provenance is written to PROVENANCE.md.

Conventions (see Docs/ART_CONVENTIONS.md):
  * 128 pixels per world unit (PPU). Drawn at 2x and downsampled for anti-aliasing.
  * 2:1 isometric. One ground tile diamond = 1 unit wide x 0.5 unit tall (128 x 64 px).
  * World sprite pivots: footprint centre for buildings/props with a footprint,
    feet/base centre for characters, trees and rocks, diamond centre for tiles.
  * Pivots and 9-slice borders are written to sprite_meta.json, which the Unity
    importer (PlaceholderSpriteImporter) reads. File names carry no game logic.

Usage:  python3 Tools/PlaceholderArt/generate_placeholders.py
"""
import json
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Ravenholm", "Art", "Placeholder")
PPU = 128
S = 2                 # supersample factor
U = PPU * S           # one world unit in drawing pixels
OL = 3 * S            # outline width in drawing pixels

# ---------------------------------------------------------------- palette
INK = (59, 42, 32, 255)            # warm dark outline
SHADOW = (40, 30, 22, 70)
STONE_L, STONE_M, STONE_D = (214, 200, 176, 255), (178, 160, 136, 255), (128, 112, 96, 255)
TIMBER_L, TIMBER_M, TIMBER_D = (196, 136, 78, 255), (152, 98, 52, 255), (110, 68, 36, 255)
PLANK = (222, 180, 120, 255)
GRASS_A, GRASS_B, GRASS_D = (142, 192, 92, 255), (128, 180, 80, 255), (96, 146, 64, 255)
PATH_C, PATH_D = (220, 194, 146, 255), (190, 160, 112, 255)
PLAZA_C, PLAZA_D = (206, 198, 182, 255), (172, 162, 146, 255)
DIRT_C, DIRT_D = (176, 140, 104, 255), (140, 106, 76, 255)
FOREST_C, FOREST_D = (104, 150, 86, 255), (78, 120, 66, 255)
SKIRT_L, SKIRT_R = (150, 112, 80, 255), (118, 86, 60, 255)
ROOF = {
    "store": ((70, 136, 152, 255), (52, 106, 120, 255)),
    "smelter": ((186, 86, 64, 255), (146, 62, 46, 255)),
    "forge": ((82, 88, 104, 255), (60, 64, 78, 255)),
    "mill": ((104, 150, 82, 255), (78, 118, 60, 255)),
    "bench": ((214, 160, 66, 255), (176, 126, 44, 255)),
    "apoth": ((132, 100, 176, 255), (100, 74, 140, 255)),
    "trader": ((206, 72, 70, 255), (244, 232, 208, 255)),
}
GLOW = (255, 170, 64, 255)
IRON, COPPER, COAL, GEM = (150, 156, 168, 255), (212, 128, 72, 255), (54, 54, 62, 255), (96, 200, 220, 255)
CEDAR, MAPLE = (176, 110, 70, 255), (214, 160, 108, 255)
HERB = (110, 176, 84, 255)
STEEL = (196, 206, 220, 255)
MOONSILVER = (206, 222, 246, 255)
IRONWOOD = (96, 70, 58, 255)

meta = {}
provenance = []


# ---------------------------------------------------------------- helpers
def canvas(w_units, h_units):
    return Image.new("RGBA", (int(w_units * U), int(h_units * U)), (0, 0, 0, 0))


def iso_pts(F, a, b):
    """Footprint corners from the front (lowest) corner F, a cells along +i (right/up), b along +j (left/up)."""
    fx, fy = F
    R = (fx + a * U / 2, fy - a * U / 4)
    L = (fx - b * U / 2, fy - b * U / 4)
    B = (R[0] + L[0] - fx, R[1] + L[1] - fy)
    return (fx, fy), R, B, L


def up(p, h):
    return (p[0], p[1] - h)


def poly(d, pts, fill, outline=INK, width=OL):
    d.polygon([tuple(map(float, p)) for p in pts], fill=fill, outline=outline, width=width)


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c[:3]) + (c[3],)


def box(d, F, a, b, h, top, left, right):
    F, R, B, L = iso_pts(F, a, b)
    poly(d, [F, L, up(L, h), up(F, h)], left)
    poly(d, [F, R, up(R, h), up(F, h)], right)
    poly(d, [up(F, h), up(R, h), up(B, h), up(L, h)], top)
    return F, R, B, L


def shadow_ellipse(d, cx, cy, rx, ry):
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=SHADOW)


def gable_roof(d, F, a, b, h, rh, over, c1, c2, gable_fill):
    """Gable roof whose ridge runs along the a axis, eaves at height h."""
    F, R, B, L = iso_pts(F, a, b)
    ox, oy = U / 2 * over, U / 4 * over
    Fe = (F[0], F[1] + 2 * oy)
    Re = (R[0] + ox, R[1] + oy)
    Le = (L[0] - ox, L[1] + oy)
    Be = (B[0], B[1])
    Fe, Re, Be, Le = [up(p, h) for p in (Fe, Re, Be, Le)]
    M1 = ((Fe[0] + Le[0]) / 2, (Fe[1] + Le[1]) / 2 - rh)
    M2 = ((Re[0] + Be[0]) / 2, (Re[1] + Be[1]) / 2 - rh)
    poly(d, [Le, Be, M2, M1], c2)              # back slope
    poly(d, [Fe, Le, M1], gable_fill)          # left gable end
    poly(d, [Fe, Re, M2, M1], c1)              # front slope
    # shingle lines on the front slope
    for t in (0.33, 0.66):
        p1 = (Fe[0] + (M1[0] - Fe[0]) * t, Fe[1] + (M1[1] - Fe[1]) * t)
        p2 = (Re[0] + (M2[0] - Re[0]) * t, Re[1] + (M2[1] - Re[1]) * t)
        d.line([p1, p2], fill=shade(c1, 0.78), width=2 * S)


def post(d, base, h, w=0.09, col=TIMBER_M):
    x, y = base
    pw = U * w
    poly(d, [(x - pw / 2, y), (x + pw / 2, y), (x + pw / 2, y - h), (x - pw / 2, y - h)], col)


def finish(name, img, pivot_px, folder, border=None, desc="", crop=True):
    """Downsample, crop to content, write PNG and record pivot/border metadata."""
    if crop:
        bbox = img.getbbox()
        if bbox is None:
            bbox = (0, 0, img.width, img.height)
        pad = 4 * S
        x0, y0 = max(0, bbox[0] - pad), max(0, bbox[1] - pad)
        x1, y1 = min(img.width, bbox[2] + pad), min(img.height, bbox[3] + pad)
        # keep dimensions even so the 2x downsample is exact
        if (x1 - x0) % 2: x1 += 1
        if (y1 - y0) % 2: y1 += 1
        img = img.crop((x0, y0, x1, y1))
        pivot_px = (pivot_px[0] - x0, pivot_px[1] - y0)
    small = img.resize((img.width // S, img.height // S), Image.LANCZOS)
    px = (pivot_px[0] / S, pivot_px[1] / S)
    rel = os.path.join(folder, name + ".png")
    path = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    small.save(path)
    entry = {"pivot": [round(px[0] / small.width, 4), round(1 - px[1] / small.height, 4)]}
    if border:
        entry["border"] = border  # l, b, r, t in final pixels
    meta[rel.replace(os.sep, "/")] = entry
    provenance.append((rel.replace(os.sep, "/"), desc or name))
    return small


# ---------------------------------------------------------------- ground tiles
def tile(name, top, dark, pattern, seed):
    rnd = random.Random(seed)
    skirt = 0.22 * U
    img = Image.new("RGBA", (U, int(U / 2 + skirt) + 2 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = U / 2, U / 4
    T, Rr, Bt, Lf = (cx, 0), (U, cy), (cx, U / 2), (0, cy)
    # skirt (visible only at plot edges, covered by front neighbours elsewhere)
    d.polygon([Lf, Bt, (Bt[0], Bt[1] + skirt), (Lf[0], Lf[1] + skirt)], fill=SKIRT_L)
    d.polygon([Bt, Rr, (Rr[0], Rr[1] + skirt), (Bt[0], Bt[1] + skirt)], fill=SKIRT_R)
    d.polygon([T, Rr, Bt, Lf], fill=top)
    # interior pattern stays inside the diamond
    def inside(x, y):
        return abs(x - cx) / (U / 2) + abs(y - cy) / (U / 4) < 0.82
    if pattern == "tufts":
        for _ in range(7):
            x, y = rnd.uniform(0, U), rnd.uniform(0, U / 2)
            if inside(x, y):
                d.line([(x, y), (x - 3 * S, y - 6 * S)], fill=dark, width=2 * S)
                d.line([(x, y), (x + 3 * S, y - 6 * S)], fill=dark, width=2 * S)
    elif pattern == "pebbles":
        for _ in range(6):
            x, y = rnd.uniform(0, U), rnd.uniform(0, U / 2)
            if inside(x, y):
                r = rnd.uniform(2, 4) * S
                d.ellipse([x - r, y - r / 2, x + r, y + r / 2], fill=dark)
    elif pattern == "flags":
        # flagstone joints along iso axes
        d.line([(cx - U / 4, cy - U / 8), (cx + U / 4, cy + U / 8)], fill=dark, width=2 * S)
        d.line([(cx + U / 4, cy - U / 8), (cx - U / 4, cy + U / 8)], fill=dark, width=2 * S)
    # faint edge so the grid reads subtly
    d.line([T, Rr, Bt, Lf, T], fill=shade(top, 0.93), width=1 * S)
    finish(name, img, (cx, cy), "Tiles", desc=f"Isometric ground tile ({pattern})", crop=False)


# ---------------------------------------------------------------- buildings
def building_canvas(a, b, extra_h):
    w = (a + b) / 2 + 1.2
    h = (a + b) / 4 + extra_h + 0.8
    img = canvas(w, h)
    F = (img.width / 2 + (b - a) * U / 4, img.height - 0.35 * U)
    return img, ImageDraw.Draw(img), F


def footprint_centre(F, a, b):
    F, R, B, L = iso_pts(F, a, b)
    return ((F[0] + B[0]) / 2, (F[1] + B[1]) / 2)


def store():
    a, b, h = 3, 3, 1.15 * U
    img, d, F = building_canvas(a, b, 2.2)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2 + 0.05 * U, 1.7 * U, 0.85 * U)
    # plank floor platform
    box(d, F, a, b, 0.12 * U, PLANK, TIMBER_M, TIMBER_D)
    fl = 0.12 * U
    # back walls (open front cutaway)
    poly(d, [up(L, fl), up(B, fl), up(B, fl + h), up(L, fl + h)], STONE_L)
    poly(d, [up(B, fl), up(R, fl), up(R, fl + h), up(B, fl + h)], STONE_M)
    # timber beams on back walls
    for p, q, c in ((L, B, TIMBER_M), (B, R, TIMBER_D)):
        d.line([up(p, fl + h * 0.55), up(q, fl + h * 0.55)], fill=c, width=5 * S)
    # sign board on the left back wall: crate emblem
    sx, sy = (L[0] + B[0]) / 2, (L[1] + B[1]) / 2 - fl - h * 0.82
    poly(d, [(sx - 0.42 * U, sy + 0.05 * U), (sx + 0.42 * U, sy - 0.16 * U), (sx + 0.42 * U, sy + 0.08 * U), (sx - 0.42 * U, sy + 0.29 * U)], ROOF["store"][0])
    poly(d, [(sx - 0.1 * U, sy + 0.08 * U), (sx + 0.1 * U, sy + 0.03 * U), (sx + 0.1 * U, sy + 0.17 * U), (sx - 0.1 * U, sy + 0.22 * U)], PLANK, width=2 * S)
    # job board on the right back wall
    jx, jy = B[0] + (R[0] - B[0]) * 0.55, B[1] + (R[1] - B[1]) * 0.55 - fl - h * 0.45
    poly(d, [(jx - 0.3 * U, jy - 0.3 * U), (jx + 0.3 * U, jy - 0.15 * U), (jx + 0.3 * U, jy + 0.2 * U), (jx - 0.3 * U, jy + 0.05 * U)], TIMBER_L)
    for i, col in enumerate(((244, 232, 208, 255), (238, 214, 160, 255), (244, 232, 208, 255))):
        px_ = jx - 0.2 * U + i * 0.17 * U
        py_ = jy - 0.18 * U + i * 0.045 * U
        poly(d, [(px_, py_), (px_ + 0.12 * U, py_ + 0.03 * U), (px_ + 0.12 * U, py_ + 0.17 * U), (px_, py_ + 0.14 * U)], col, width=2 * S)
    # corner posts and front awning beam
    for p in (L, F_, R):
        post(d, up(p, fl), h * 1.05, 0.1)
    # striped awning along both back walls' top
    for p, q, c1 in ((L, B, ROOF["store"][0]), (B, R, ROOF["store"][1])):
        a1, a2 = up(p, fl + h), up(q, fl + h)
        poly(d, [a1, a2, (a2[0], a2[1] - 0.18 * U), (a1[0], a1[1] - 0.18 * U)], c1)
    # counter at the front-right edge
    cF = (F_[0] + (R[0] - F_[0]) * 0.15, F_[1] + (R[1] - F_[1]) * 0.15 - fl)
    box(d, cF, 1.6, 0.4, 0.42 * U, TIMBER_L, TIMBER_M, TIMBER_D)
    return finish("bld_store", img, footprint_centre(F, a, b), "Buildings", desc="Store: open-front cutaway with job board, counter, sign")


def smelter():
    a, b, h = 2, 2, 0.95 * U
    img, d, F = building_canvas(a, b, 2.6)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2, 1.2 * U, 0.6 * U)
    box(d, F, a, b, h, STONE_L, STONE_M, STONE_D)
    # stone courses
    for t in (0.33, 0.66):
        d.line([up(L, h * t), up(F_, h * t), up(R, h * t)], fill=STONE_D, width=2 * S)
    # furnace mouth on left face
    mx, my = (F_[0] + L[0]) / 2, (F_[1] + L[1]) / 2 - 0.12 * U
    d.chord([mx - 0.26 * U, my - 0.42 * U, mx + 0.22 * U, my + 0.1 * U], 180, 360, fill=INK)
    d.rectangle([mx - 0.26 * U, my - 0.16 * U, mx + 0.22 * U, my + 0.02 * U], fill=INK)
    d.ellipse([mx - 0.16 * U, my - 0.24 * U, mx + 0.12 * U, my + 0.0 * U], fill=GLOW)
    # chimney
    cF = (B[0] + 0.05 * U, B[1] + 0.32 * U - h)
    box(d, cF, 0.6, 0.6, 1.1 * U, STONE_M, STONE_L, STONE_D)
    gable_roof(d, F, a, b, h, 0.4 * U, 0.15, *ROOF["smelter"], STONE_L)
    # ore hopper (receiver) in front of the right face
    hF = (R[0] - 0.2 * U, R[1] + 0.42 * U)
    box(d, hF, 0.7, 0.7, 0.35 * U, IRON, TIMBER_L, TIMBER_D)
    return finish("bld_smelter", img, footprint_centre(F, a, b), "Buildings", desc="Smelter: stone furnace, chimney, glowing mouth, ore hopper")


def forge():
    a, b = 2, 2
    h = 1.0 * U
    img, d, F = building_canvas(a, b, 2.2)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2, 1.2 * U, 0.6 * U)
    box(d, F, a, b, 0.08 * U, STONE_M, STONE_D, STONE_D)
    # back half-wall
    poly(d, [up(L, 0.08 * U), up(B, 0.08 * U), up(B, 0.6 * U), up(L, 0.6 * U)], STONE_L)
    poly(d, [up(B, 0.08 * U), up(R, 0.08 * U), up(R, 0.6 * U), up(B, 0.6 * U)], STONE_M)
    # hearth glow in back corner
    d.ellipse([B[0] - 0.3 * U, B[1] - 0.48 * U, B[0] + 0.3 * U, B[1] - 0.18 * U], fill=GLOW, outline=INK, width=OL)
    # anvil
    ax, ay = (F[0] + B[0]) / 2, (F[1] + B[1]) / 2 + 0.05 * U
    poly(d, [(ax - 0.12 * U, ay), (ax + 0.12 * U, ay), (ax + 0.08 * U, ay - 0.18 * U), (ax - 0.08 * U, ay - 0.18 * U)], COAL)
    poly(d, [(ax - 0.3 * U, ay - 0.18 * U), (ax + 0.26 * U, ay - 0.18 * U), (ax + 0.2 * U, ay - 0.3 * U), (ax - 0.22 * U, ay - 0.3 * U)], IRON)
    for p in (L, F_, R, B):
        post(d, up(p, 0.08 * U), h, 0.1)
    gable_roof(d, F, a, b, h + 0.08 * U, 0.38 * U, 0.12, *ROOF["forge"], TIMBER_L)
    return finish("bld_forge", img, footprint_centre(F, a, b), "Buildings", desc="Forge: covered outdoor work area, anvil, hearth")


def mill():
    a, b = 2.5, 2
    img, d, F = building_canvas(a, b, 2.0)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2, 1.4 * U, 0.65 * U)
    box(d, F, a, b, 0.08 * U, PLANK, TIMBER_M, TIMBER_D)
    # log rack along back
    for i in range(3):
        y_off = i * 0.13 * U
        p1, p2 = up(L, 0.2 * U + y_off), up(B, 0.2 * U + y_off)
        d.line([p1, p2], fill=INK, width=int(0.16 * U))
        d.line([p1, p2], fill=CEDAR, width=int(0.16 * U) - 2 * OL)
    # saw table with blade
    sx, sy = (F[0] + B[0]) / 2 + 0.2 * U, (F[1] + B[1]) / 2 + 0.1 * U
    box(d, (sx, sy), 1.1, 0.5, 0.35 * U, PLANK, TIMBER_M, TIMBER_D)
    bx, by = sx + 0.1 * U, sy - 0.55 * U
    d.ellipse([bx - 0.17 * U, by - 0.17 * U, bx + 0.17 * U, by + 0.17 * U], fill=STEEL, outline=INK, width=OL)
    # triangular tarp on two pillars, staked to the ground at the front-left
    p_back, p_right = up(B, 0.08 * U), up(R, 0.08 * U)
    post(d, p_back, 1.35 * U, 0.11)
    post(d, p_right, 1.2 * U, 0.11)
    ground = (F_[0] - 0.4 * U, F_[1] - 0.05 * U)
    poly(d, [up(p_back, 1.35 * U), up(p_right, 1.2 * U), ground], ROOF["mill"][0])
    d.line([up(p_back, 1.35 * U), ground], fill=ROOF["mill"][1], width=3 * S)
    return finish("bld_lumbermill", img, footprint_centre(F, a, b), "Buildings", desc="Lumbermill: tarp on two pillars, log rack, saw table")


def bench():
    a, b = 1.5, 1.5
    img, d, F = building_canvas(a, b, 1.8)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2, 0.95 * U, 0.48 * U)
    tF = (F_[0], F_[1] - 0.15 * U)
    box(d, tF, 1.4, 0.7, 0.42 * U, PLANK, TIMBER_M, TIMBER_D)
    # tools on bench: staff + bow silhouettes
    tx, ty = tF[0] + 0.1 * U, tF[1] - 0.68 * U
    d.line([(tx - 0.3 * U, ty + 0.1 * U), (tx + 0.3 * U, ty - 0.2 * U)], fill=INK, width=int(0.07 * U))
    d.line([(tx - 0.3 * U, ty + 0.1 * U), (tx + 0.3 * U, ty - 0.2 * U)], fill=CEDAR, width=int(0.07 * U) - 2 * S)
    for p in (L, B, R):
        post(d, p, 1.05 * U, 0.08)
    gable_roof(d, F, a, b, 1.05 * U, 0.28 * U, 0.12, *ROOF["bench"], TIMBER_L)
    return finish("bld_workbench", img, footprint_centre(F, a, b), "Buildings", desc="Workbench: small covered bench with tools")


def apothecary():
    a, b, h = 2, 2, 0.9 * U
    img, d, F = building_canvas(a, b, 2.2)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2, 1.2 * U, 0.6 * U)
    box(d, F, a, b, h, TIMBER_L, PLANK, TIMBER_L)
    # round window on right face, door on left face
    wx, wy = (F_[0] + R[0]) / 2, (F_[1] + R[1]) / 2 - h * 0.55
    d.ellipse([wx - 0.15 * U, wy - 0.15 * U, wx + 0.15 * U, wy + 0.15 * U], fill=(250, 230, 160, 255), outline=INK, width=OL)
    dx, dy = (F_[0] + L[0]) / 2, (F_[1] + L[1]) / 2
    poly(d, [(dx - 0.17 * U, dy + 0.04 * U), (dx + 0.17 * U, dy - 0.12 * U), (dx + 0.17 * U, dy - 0.62 * U), (dx - 0.17 * U, dy - 0.46 * U)], ROOF["apoth"][1])
    gable_roof(d, F, a, b, h, 0.5 * U, 0.15, *ROOF["apoth"], PLANK)
    # herb basket (receiver) in front
    hx, hy = F_[0] + 0.45 * U, F_[1] + 0.1 * U
    d.ellipse([hx - 0.25 * U, hy - 0.2 * U, hx + 0.25 * U, hy + 0.05 * U], fill=TIMBER_L, outline=INK, width=OL)
    for i in range(4):
        d.ellipse([hx - 0.2 * U + i * 0.1 * U, hy - 0.3 * U, hx - 0.06 * U + i * 0.1 * U, hy - 0.14 * U], fill=HERB, outline=INK, width=2 * S)
    return finish("bld_apothecary", img, footprint_centre(F, a, b), "Buildings", desc="Apothecary: hut with round window, herb basket receiver")


def trader():
    a, b = 2, 2
    img, d, F = building_canvas(a, b, 2.0)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2, 1.2 * U, 0.6 * U)
    box(d, F, a, b, 0.1 * U, PLAZA_C, STONE_M, STONE_D)
    # crates and barrel
    box(d, (F_[0] - 0.3 * U, F_[1] - 0.45 * U), 0.6, 0.6, 0.45 * U, PLANK, TIMBER_L, TIMBER_M)
    box(d, (F_[0] + 0.5 * U, F_[1] - 0.55 * U), 0.5, 0.5, 0.38 * U, PLANK, TIMBER_L, TIMBER_M)
    for p in (L, F_, R, B):
        post(d, up(p, 0.1 * U), 1.15 * U, 0.08)
    # striped canopy: alternating stripes across the front slope
    Fe, Re, Be, Le = [up(p, 1.25 * U) for p in (F_, R, B, L)]
    n = 6
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n
        p0 = (Le[0] + (Fe[0] - Le[0]) * t0, Le[1] + (Fe[1] - Le[1]) * t0)
        p1 = (Le[0] + (Fe[0] - Le[0]) * t1, Le[1] + (Fe[1] - Le[1]) * t1)
        q0 = (Be[0] + (Re[0] - Be[0]) * t0, Be[1] + (Re[1] - Be[1]) * t0)
        q1 = (Be[0] + (Re[0] - Be[0]) * t1, Be[1] + (Re[1] - Be[1]) * t1)
        col = ROOF["trader"][i % 2]
        poly(d, [p0, p1, (q1[0], q1[1] - 0.2 * U), (q0[0], q0[1] - 0.2 * U)], col, width=2 * S)
    return finish("bld_trader_pad", img, footprint_centre(F, a, b), "Buildings", desc="Trader pad: striped canopy stall with crates")


def mine_mouth():
    img = canvas(4.4, 3.4)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.5 * U
    shadow_ellipse(d, cx, base, 2.0 * U, 0.45 * U)
    # layered cliff (visible strata)
    layers = [(STONE_L, 1.0), (STONE_M, 0.75), ((160, 130, 100, 255), 0.5), (STONE_D, 0.25)]
    pts_top = [(-2.0, 0.0), (-1.9, 1.9), (-1.2, 2.5), (-0.3, 2.75), (0.6, 2.6), (1.4, 2.2), (2.0, 1.5), (2.05, 0.0)]
    outline = [(cx + x * U, base - y * U) for x, y in pts_top]
    poly(d, outline, STONE_M)
    for col, frac in layers[1:]:
        y = base - frac * 2.4 * U
        d.line([(cx - 1.95 * U, y + 0.1 * U), (cx + 2.0 * U, y - 0.12 * U)], fill=shade(col, 0.9), width=int(0.06 * U))
    # ore flecks
    rnd = random.Random(7)
    for _ in range(14):
        x, y = cx + rnd.uniform(-1.6, 1.6) * U, base - rnd.uniform(0.3, 2.2) * U
        c = IRON if rnd.random() < 0.7 else COPPER
        d.ellipse([x - 0.05 * U, y - 0.035 * U, x + 0.05 * U, y + 0.035 * U], fill=c)
    # timber-framed tunnel entrance
    tw, th = 0.62 * U, 1.15 * U
    d.chord([cx - tw, base - th - tw * 0.6, cx + tw, base - th + tw * 0.6], 180, 360, fill=INK)
    d.rectangle([cx - tw, base - th, cx + tw, base], fill=INK)
    for x in (cx - tw, cx + tw):
        post(d, (x, base), th + 0.1 * U, 0.14)
    poly(d, [(cx - tw - 0.15 * U, base - th - 0.05 * U), (cx + tw + 0.15 * U, base - th - 0.05 * U), (cx + tw + 0.15 * U, base - th - 0.22 * U), (cx - tw - 0.15 * U, base - th - 0.22 * U)], TIMBER_M)
    # cart track
    for dx in (-0.25, 0.25):
        d.line([(cx + dx * U, base), (cx + dx * 1.6 * U, base + 0.45 * U)], fill=TIMBER_D, width=3 * S)
    return finish("bld_mine_mouth", img, (cx, base), "Buildings", desc="Mine mouth: stratified cliff, timber-framed tunnel, cart track")


def forest_gate():
    img = canvas(2.6, 2.6)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.4 * U
    shadow_ellipse(d, cx, base, 1.0 * U, 0.25 * U)
    for x in (-0.85, 0.85):
        post(d, (cx + x * U, base), 1.5 * U, 0.14)
    poly(d, [(cx - 1.05 * U, base - 1.45 * U), (cx + 1.05 * U, base - 1.45 * U), (cx + 1.05 * U, base - 1.65 * U), (cx - 1.05 * U, base - 1.65 * U)], TIMBER_M)
    # hanging trail marker: leaf emblem
    d.line([(cx, base - 1.45 * U), (cx, base - 1.25 * U)], fill=INK, width=2 * S)
    d.ellipse([cx - 0.2 * U, base - 1.27 * U, cx + 0.2 * U, base - 0.95 * U], fill=PLANK, outline=INK, width=OL)
    poly(d, [(cx, base - 1.22 * U), (cx + 0.1 * U, base - 1.1 * U), (cx, base - 0.99 * U), (cx - 0.1 * U, base - 1.1 * U)], HERB, width=2 * S)
    return finish("bld_forest_gate", img, (cx, base), "Buildings", desc="Forest entrance: timber gate with trail marker")


# ---------------------------------------------------------------- trees and rocks
def tree_cedar():
    img = canvas(1.6, 2.6)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.25 * U
    shadow_ellipse(d, cx, base, 0.55 * U, 0.18 * U)
    post(d, (cx, base), 0.5 * U, 0.16, TIMBER_D)
    greens = [(70, 128, 84, 255), (82, 144, 92, 255), (96, 160, 100, 255)]
    for i, (w, y0, y1) in enumerate(((0.62, 0.35, 1.25), (0.5, 0.85, 1.75), (0.36, 1.35, 2.2))):
        poly(d, [(cx - w * U, base - y0 * U), (cx + w * U, base - y0 * U), (cx, base - y1 * U)], greens[i])
    return finish("tree_cedar", img, (cx, base), "Nature", desc="Cedar tree: tiered conifer silhouette")


def tree_maple():
    img = canvas(1.8, 2.4)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.25 * U
    shadow_ellipse(d, cx, base, 0.6 * U, 0.2 * U)
    post(d, (cx, base), 0.75 * U, 0.17, TIMBER_M)
    blobs = [(-0.32, 1.05, 0.38), (0.32, 1.05, 0.38), (0, 1.45, 0.48), (-0.2, 1.75, 0.3), (0.25, 1.7, 0.3)]
    for x, y, r in blobs:
        d.ellipse([cx + (x - r) * U, base - (y + r) * U, cx + (x + r) * U, base - (y - r) * U], fill=INK)
    for x, y, r in blobs:
        r2 = r - OL / U
        d.ellipse([cx + (x - r2) * U, base - (y + r2) * U, cx + (x + r2) * U, base - (y - r2) * U], fill=(210, 120, 64, 255))
    for x, y, r in blobs[2:]:
        r2 = r * 0.55
        d.ellipse([cx + (x - r2 - 0.05) * U, base - (y + r2 + 0.05) * U, cx + (x + r2 - 0.05) * U, base - (y - r2 + 0.05) * U], fill=(232, 150, 82, 255))
    return finish("tree_maple", img, (cx, base), "Nature", desc="Maple tree: round warm canopy silhouette")


def tree_ironwood():
    """Phase 7/10: ironwood: tall dark trunk, layered deep-green and russet canopy (distinct from cedar and maple)."""
    img = canvas(1.7, 2.9)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.25 * U
    shadow_ellipse(d, cx, base, 0.55 * U, 0.18 * U)
    post(d, (cx, base), 1.15 * U, 0.2, (78, 56, 46, 255))
    layers = [((46, 92, 70, 255), 0.62, 1.05, 1.45), ((62, 108, 76, 255), 0.5, 1.45, 1.9), ((150, 86, 52, 255), 0.34, 1.85, 2.4)]
    for col, w, y0, y1 in layers:
        poly(d, [(cx - w * U, base - y0 * U), (cx - w * 0.5 * U, base - y1 * U), (cx + w * 0.5 * U, base - y1 * U), (cx + w * U, base - y0 * U)], col)
    return finish("tree_ironwood", img, (cx, base), "Nature", desc="Ironwood tree: tall trunk, flat-topped layered canopy")


def rock(name, vein, seed, desc):
    rnd = random.Random(seed)
    img = canvas(1.6, 1.4)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.25 * U
    shadow_ellipse(d, cx, base, 0.62 * U, 0.2 * U)
    pts = [(-0.62, 0), (-0.58, 0.45), (-0.3, 0.8), (0.1, 0.88), (0.45, 0.66), (0.62, 0.3), (0.6, 0)]
    poly(d, [(cx + x * U, base - y * U) for x, y in pts], STONE_M)
    poly(d, [(cx - 0.3 * U, base - 0.8 * U), (cx + 0.1 * U, base - 0.88 * U), (cx + 0.45 * U, base - 0.66 * U), (cx + 0.05 * U, base - 0.5 * U)], STONE_L, width=2 * S)
    poly(d, [(cx + 0.05 * U, base - 0.5 * U), (cx + 0.45 * U, base - 0.66 * U), (cx + 0.62 * U, base - 0.3 * U), (cx + 0.6 * U, base), (cx + 0.1 * U, base)], STONE_D, width=2 * S)
    if vein:
        for _ in range(6):
            x, y = cx + rnd.uniform(-0.45, 0.45) * U, base - rnd.uniform(0.15, 0.65) * U
            poly(d, [(x, y - 0.06 * U), (x + 0.07 * U, y), (x, y + 0.05 * U), (x - 0.07 * U, y)], vein, width=2 * S)
    return finish(name, img, (cx, base), "Nature", desc=desc)


def bush_herb():
    img = canvas(0.9, 0.7)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.15 * U
    shadow_ellipse(d, cx, base, 0.3 * U, 0.1 * U)
    for x, y, r in ((-0.15, 0.18, 0.16), (0.15, 0.18, 0.16), (0, 0.3, 0.18)):
        d.ellipse([cx + (x - r) * U, base - (y + r) * U, cx + (x + r) * U, base - (y - r) * U], fill=HERB, outline=INK, width=OL)
    d.ellipse([cx - 0.04 * U, base - 0.36 * U, cx + 0.04 * U, base - 0.28 * U], fill=(250, 236, 120, 255))
    return finish("bush_herb", img, (cx, base), "Nature", desc="Herb bush")


# ---------------------------------------------------------------- characters
SKINS = [(240, 200, 160, 255), (196, 140, 100, 255), (140, 96, 70, 255)]


def char_canvas():
    img = canvas(1.2, 1.4)
    return img, ImageDraw.Draw(img), img.width / 2, img.height - 0.12 * U


def char_base(name, skin, desc):
    img, d, cx, base = char_canvas()
    shadow_ellipse(d, cx, base, 0.26 * U, 0.08 * U)
    # legs
    for dx in (-0.09, 0.09):
        d.rounded_rectangle([cx + (dx - 0.06) * U, base - 0.22 * U, cx + (dx + 0.06) * U, base], radius=0.04 * U, fill=(84, 70, 64, 255), outline=INK, width=OL)
    # body (chunky bean)
    d.rounded_rectangle([cx - 0.24 * U, base - 0.66 * U, cx + 0.24 * U, base - 0.16 * U], radius=0.16 * U, fill=(228, 220, 204, 255), outline=INK, width=OL)
    # head
    d.ellipse([cx - 0.22 * U, base - 1.04 * U, cx + 0.22 * U, base - 0.6 * U], fill=skin, outline=INK, width=OL)
    # eyes
    for dx in (-0.08, 0.08):
        d.ellipse([cx + (dx - 0.025) * U, base - 0.84 * U, cx + (dx + 0.025) * U, base - 0.78 * U], fill=INK)
    return finish(name, img, (cx, base), "Characters", desc=desc)


def overlay(name, desc, draw_fn):
    img, d, cx, base = char_canvas()
    draw_fn(d, cx, base)
    # overlays use the same canvas and pivot as the base so they align exactly
    return finish(name, img, (cx, base), "Characters", desc=desc, crop=False)


def tunic(d, cx, base, col, trim=None):
    d.rounded_rectangle([cx - 0.24 * U, base - 0.6 * U, cx + 0.24 * U, base - 0.16 * U], radius=0.14 * U, fill=col, outline=INK, width=OL)
    if trim:
        d.line([(cx - 0.22 * U, base - 0.32 * U), (cx + 0.22 * U, base - 0.32 * U)], fill=trim, width=int(0.05 * U))


def ov_supplier(d, cx, base):
    tunic(d, cx, base, (90, 120, 150, 255))
    d.rounded_rectangle([cx - 0.15 * U, base - 0.52 * U, cx + 0.15 * U, base - 0.18 * U], radius=0.05 * U, fill=PLANK, outline=INK, width=2 * S)  # apron
    d.chord([cx - 0.24 * U, base - 1.1 * U, cx + 0.24 * U, base - 0.78 * U], 180, 360, fill=(170, 70, 60, 255), outline=INK, width=OL)  # cap


def ov_miner(d, cx, base):
    tunic(d, cx, base, (120, 100, 80, 255), TIMBER_D)
    d.chord([cx - 0.25 * U, base - 1.12 * U, cx + 0.25 * U, base - 0.76 * U], 180, 360, fill=(230, 190, 70, 255), outline=INK, width=OL)
    d.ellipse([cx - 0.05 * U, base - 1.08 * U, cx + 0.05 * U, base - 0.98 * U], fill=(255, 250, 200, 255))
    # pickaxe
    d.line([(cx + 0.28 * U, base - 0.2 * U), (cx + 0.42 * U, base - 0.78 * U)], fill=INK, width=int(0.07 * U))
    d.line([(cx + 0.28 * U, base - 0.2 * U), (cx + 0.42 * U, base - 0.78 * U)], fill=TIMBER_L, width=int(0.07 * U) - 2 * S)
    d.arc([cx + 0.2 * U, base - 0.92 * U, cx + 0.66 * U, base - 0.6 * U], 200, 340, fill=IRON, width=int(0.07 * U))


def ov_woodcutter(d, cx, base):
    tunic(d, cx, base, (176, 66, 56, 255), (60, 50, 44, 255))
    d.chord([cx - 0.23 * U, base - 1.08 * U, cx + 0.23 * U, base - 0.8 * U], 180, 360, fill=(70, 110, 70, 255), outline=INK, width=OL)
    d.line([(cx + 0.28 * U, base - 0.2 * U), (cx + 0.38 * U, base - 0.8 * U)], fill=INK, width=int(0.07 * U))
    d.line([(cx + 0.28 * U, base - 0.2 * U), (cx + 0.38 * U, base - 0.8 * U)], fill=TIMBER_L, width=int(0.07 * U) - 2 * S)
    poly(d, [(cx + 0.36 * U, base - 0.82 * U), (cx + 0.56 * U, base - 0.86 * U), (cx + 0.54 * U, base - 0.64 * U), (cx + 0.38 * U, base - 0.68 * U)], IRON)


def ov_warrior(d, cx, base):
    tunic(d, cx, base, (150, 156, 170, 255), (110, 116, 130, 255))
    d.chord([cx - 0.24 * U, base - 1.1 * U, cx + 0.24 * U, base - 0.72 * U], 180, 360, fill=IRON, outline=INK, width=OL)
    d.ellipse([cx - 0.42 * U, base - 0.6 * U, cx - 0.12 * U, base - 0.26 * U], fill=(170, 60, 50, 255), outline=INK, width=OL)  # shield
    d.line([(cx + 0.3 * U, base - 0.2 * U), (cx + 0.3 * U, base - 0.92 * U)], fill=INK, width=int(0.08 * U))
    d.line([(cx + 0.3 * U, base - 0.3 * U), (cx + 0.3 * U, base - 0.92 * U)], fill=STEEL, width=int(0.08 * U) - 2 * S)
    d.line([(cx + 0.2 * U, base - 0.32 * U), (cx + 0.4 * U, base - 0.32 * U)], fill=INK, width=int(0.05 * U))


def ov_ranger(d, cx, base):
    tunic(d, cx, base, (84, 130, 76, 255), (60, 96, 54, 255))
    d.pieslice([cx - 0.27 * U, base - 1.14 * U, cx + 0.27 * U, base - 0.6 * U], 180, 360, fill=(70, 112, 64, 255), outline=INK, width=OL)  # hood
    d.arc([cx + 0.12 * U, base - 1.0 * U, cx + 0.52 * U, base - 0.2 * U], 270, 90, fill=TIMBER_M, width=int(0.06 * U))
    d.line([(cx + 0.32 * U, base - 1.0 * U), (cx + 0.32 * U, base - 0.2 * U)], fill=(240, 236, 220, 255), width=2 * S)


def ov_rogue(d, cx, base):
    tunic(d, cx, base, (70, 64, 84, 255), (130, 40, 56, 255))
    d.rectangle([cx - 0.22 * U, base - 0.76 * U, cx + 0.22 * U, base - 0.68 * U], fill=(130, 40, 56, 255))  # mask band
    d.pieslice([cx - 0.25 * U, base - 1.1 * U, cx + 0.25 * U, base - 0.66 * U], 180, 360, fill=(60, 56, 72, 255), outline=INK, width=OL)
    d.line([(cx + 0.28 * U, base - 0.22 * U), (cx + 0.4 * U, base - 0.56 * U)], fill=INK, width=int(0.07 * U))
    d.line([(cx + 0.3 * U, base - 0.3 * U), (cx + 0.4 * U, base - 0.56 * U)], fill=STEEL, width=int(0.07 * U) - 2 * S)


def ov_mage(d, cx, base):
    tunic(d, cx, base, (92, 82, 160, 255), (220, 190, 90, 255))
    poly(d, [(cx - 0.3 * U, base - 0.92 * U), (cx + 0.3 * U, base - 0.92 * U), (cx + 0.06 * U, base - 1.38 * U)], (82, 72, 150, 255))
    d.line([(cx - 0.32 * U, base - 0.92 * U), (cx + 0.32 * U, base - 0.92 * U)], fill=INK, width=int(0.06 * U))
    d.line([(cx + 0.34 * U, base - 0.1 * U), (cx + 0.34 * U, base - 1.0 * U)], fill=INK, width=int(0.07 * U))
    d.line([(cx + 0.34 * U, base - 0.12 * U), (cx + 0.34 * U, base - 0.98 * U)], fill=TIMBER_L, width=int(0.07 * U) - 2 * S)
    d.ellipse([cx + 0.26 * U, base - 1.12 * U, cx + 0.42 * U, base - 0.96 * U], fill=GEM, outline=INK, width=2 * S)


# ---------------------------------------------------------------- resources and items
def icon_canvas():
    img = Image.new("RGBA", (96 * S, 96 * S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img), 48 * S, 48 * S


def ore_shape(d, cx, cy, s, fill, fleck):
    pts = [(-0.42, 0.2), (-0.3, -0.25), (0.05, -0.38), (0.4, -0.18), (0.42, 0.22), (0.05, 0.36)]
    poly(d, [(cx + x * s, cy + y * s) for x, y in pts], STONE_M)
    for x, y in ((-0.15, -0.05), (0.15, 0.1), (0.1, -0.2)):
        r = 0.09 * s
        d.ellipse([cx + x * s - r, cy + y * s - r, cx + x * s + r, cy + y * s + r], fill=fleck)
    if fill:
        pass


def ingot(d, cx, cy, s, col):
    poly(d, [(cx - 0.42 * s, cy + 0.12 * s), (cx + 0.18 * s, cy + 0.28 * s), (cx + 0.42 * s, cy + 0.02 * s), (cx - 0.18 * s, cy - 0.14 * s)], shade(col, 0.8))
    poly(d, [(cx - 0.32 * s, cy - 0.02 * s), (cx + 0.18 * s, cy + 0.12 * s), (cx + 0.32 * s, cy - 0.06 * s), (cx - 0.18 * s, cy - 0.2 * s)], col, width=2 * S)


def log(d, cx, cy, s, bark, core):
    d.rounded_rectangle([cx - 0.42 * s, cy - 0.16 * s, cx + 0.3 * s, cy + 0.16 * s], radius=0.1 * s, fill=bark, outline=INK, width=OL)
    d.ellipse([cx + 0.18 * s, cy - 0.17 * s, cx + 0.44 * s, cy + 0.17 * s], fill=core, outline=INK, width=OL)
    d.ellipse([cx + 0.27 * s, cy - 0.06 * s, cx + 0.35 * s, cy + 0.06 * s], fill=shade(core, 0.8))


def plank(d, cx, cy, s, col):
    for k in (0.12, -0.04):
        poly(d, [(cx - 0.44 * s, cy + k * s + 0.06 * s), (cx + 0.3 * s, cy + k * s + 0.2 * s), (cx + 0.44 * s, cy + k * s + 0.08 * s), (cx - 0.3 * s, cy + k * s - 0.06 * s)], col if k < 0 else shade(col, 0.82), width=2 * S)


def herb_bundle(d, cx, cy, s, col=HERB):
    for x in (-0.18, 0.0, 0.18):
        d.ellipse([cx + (x - 0.14) * s, cy - 0.36 * s, cx + (x + 0.14) * s, cy - 0.02 * s], fill=col, outline=INK, width=2 * S)
    d.rectangle([cx - 0.12 * s, cy - 0.04 * s, cx + 0.12 * s, cy + 0.28 * s], fill=TIMBER_L, outline=INK, width=2 * S)


def gem(d, cx, cy, s):
    poly(d, [(cx, cy - 0.38 * s), (cx + 0.3 * s, cy - 0.08 * s), (cx, cy + 0.36 * s), (cx - 0.3 * s, cy - 0.08 * s)], GEM)
    d.line([(cx - 0.3 * s, cy - 0.08 * s), (cx + 0.3 * s, cy - 0.08 * s)], fill=shade(GEM, 0.7), width=2 * S)


def coal_lump(d, cx, cy, s):
    pts = [(-0.36, 0.18), (-0.24, -0.24), (0.12, -0.32), (0.38, -0.06), (0.3, 0.26), (-0.06, 0.32)]
    poly(d, [(cx + x * s, cy + y * s) for x, y in pts], COAL)
    d.line([(cx - 0.1 * s, cy - 0.18 * s), (cx + 0.12 * s, cy - 0.06 * s)], fill=(110, 110, 124, 255), width=2 * S)


def rope(d, cx, cy, s):
    for r in (0.34, 0.22, 0.1):
        d.ellipse([cx - r * s, cy - r * 0.6 * s, cx + r * s, cy + r * 0.6 * s], outline=INK, width=int(0.1 * s))
        d.ellipse([cx - r * s, cy - r * 0.6 * s, cx + r * s, cy + r * 0.6 * s], outline=(214, 186, 128, 255), width=int(0.1 * s) - 2 * S)


def fur(d, cx, cy, s):
    pts = [(-0.4, 0.1), (-0.3, -0.22), (0, -0.3), (0.3, -0.22), (0.4, 0.1), (0.2, 0.3), (-0.2, 0.3)]
    poly(d, [(cx + x * s, cy + y * s) for x, y in pts], (150, 110, 80, 255))
    for x in (-0.15, 0.05, 0.22):
        d.line([(cx + x * s, cy - 0.15 * s), (cx + (x + 0.05) * s, cy + 0.1 * s)], fill=(120, 84, 60, 255), width=2 * S)


def dust(d, cx, cy, s):
    d.ellipse([cx - 0.3 * s, cy - 0.05 * s, cx + 0.3 * s, cy + 0.32 * s], fill=(110, 90, 160, 255), outline=INK, width=OL)
    for x, y in ((-0.1, -0.15), (0.12, -0.25), (0.02, -0.38)):
        d.ellipse([cx + (x - 0.05) * s, cy + (y - 0.05) * s, cx + (x + 0.05) * s, cy + (y + 0.05) * s], fill=(220, 200, 255, 255))


RESOURCES = {
    "iron_ore": lambda d, cx, cy, s: ore_shape(d, cx, cy, s, None, IRON),
    "copper_ore": lambda d, cx, cy, s: ore_shape(d, cx, cy, s, None, COPPER),
    "coal": coal_lump,
    "cedar_log": lambda d, cx, cy, s: log(d, cx, cy, s, CEDAR, (232, 196, 140, 255)),
    "maple_log": lambda d, cx, cy, s: log(d, cx, cy, s, (150, 120, 96, 255), (244, 214, 168, 255)),
    "herb": herb_bundle,
    "iron_ingot": lambda d, cx, cy, s: ingot(d, cx, cy, s, IRON),
    "copper_ingot": lambda d, cx, cy, s: ingot(d, cx, cy, s, COPPER),
    "steel_ingot": lambda d, cx, cy, s: ingot(d, cx, cy, s, STEEL),
    "cedar_lumber": lambda d, cx, cy, s: plank(d, cx, cy, s, CEDAR),
    "maple_lumber": lambda d, cx, cy, s: plank(d, cx, cy, s, MAPLE),
    "gem": gem,
    "rope": rope,
    "fur": fur,
    "arcane_dust": dust,
    # Phase 7: v1 higher-tier components (Mine depths 4-6, Forest areas 4-6)
    "moonsilver_ore": lambda d, cx, cy, s: ore_shape(d, cx, cy, s, None, MOONSILVER),
    "moonsilver_ingot": lambda d, cx, cy, s: ingot(d, cx, cy, s, MOONSILVER),
    "ironwood_log": lambda d, cx, cy, s: log(d, cx, cy, s, IRONWOOD, (180, 128, 96, 255)),
    "ironwood_lumber": lambda d, cx, cy, s: plank(d, cx, cy, s, (140, 96, 72, 255)),
}


def item_sword(d, cx, cy, s, col=IRON):
    d.line([(cx - 0.3 * s, cy + 0.3 * s), (cx + 0.32 * s, cy - 0.32 * s)], fill=INK, width=int(0.16 * s))
    d.line([(cx - 0.18 * s, cy + 0.18 * s), (cx + 0.3 * s, cy - 0.3 * s)], fill=col, width=int(0.16 * s) - 2 * OL)
    d.line([(cx - 0.28 * s, cy + 0.08 * s), (cx - 0.08 * s, cy + 0.28 * s)], fill=INK, width=int(0.08 * s))
    d.line([(cx - 0.4 * s, cy + 0.4 * s), (cx - 0.25 * s, cy + 0.25 * s)], fill=TIMBER_M, width=int(0.08 * s))


def item_dagger(d, cx, cy, s):
    item_sword(d, cx + 0.1 * s, cy - 0.1 * s, s * 0.7)


def item_bow(d, cx, cy, s):
    bb = [cx - 0.38 * s, cy - 0.44 * s, cx + 0.46 * s, cy + 0.44 * s]
    sx = cx + 0.04 * s + 0.42 * s * math.cos(math.radians(115))
    d.line([(sx, cy - 0.4 * s), (sx, cy + 0.4 * s)], fill=(240, 236, 220, 255), width=3 * S)
    d.arc(bb, 115, 245, fill=INK, width=int(0.15 * s))
    d.arc([bb[0] + OL / 2, bb[1] + OL / 2, bb[2] - OL / 2, bb[3] - OL / 2], 117, 243, fill=CEDAR, width=int(0.15 * s) - 2 * OL)


def item_staff(d, cx, cy, s):
    d.line([(cx - 0.3 * s, cy + 0.4 * s), (cx + 0.2 * s, cy - 0.25 * s)], fill=INK, width=int(0.12 * s))
    d.line([(cx - 0.28 * s, cy + 0.37 * s), (cx + 0.2 * s, cy - 0.25 * s)], fill=CEDAR, width=int(0.12 * s) - 2 * OL)
    d.ellipse([cx + 0.1 * s, cy - 0.42 * s, cx + 0.36 * s, cy - 0.16 * s], fill=COPPER, outline=INK, width=OL)


def item_coat(d, cx, cy, s):
    pts = [(-0.24, -0.36), (0.24, -0.36), (0.42, -0.1), (0.3, 0.0), (0.26, 0.4), (-0.26, 0.4), (-0.3, 0.0), (-0.42, -0.1)]
    poly(d, [(cx + x * s, cy + y * s) for x, y in pts], (150, 120, 96, 255))
    for x, y in ((-0.12, -0.1), (0.12, -0.1), (-0.12, 0.15), (0.12, 0.15)):
        d.ellipse([cx + (x - 0.04) * s, cy + (y - 0.04) * s, cx + (x + 0.04) * s, cy + (y + 0.04) * s], fill=IRON)


def item_tonic(d, cx, cy, s):
    d.ellipse([cx - 0.28 * s, cy - 0.12 * s, cx + 0.28 * s, cy + 0.4 * s], fill=(214, 74, 84, 255), outline=INK, width=OL)
    d.rectangle([cx - 0.09 * s, cy - 0.36 * s, cx + 0.09 * s, cy - 0.1 * s], fill=(220, 230, 236, 255), outline=INK, width=OL)
    d.rectangle([cx - 0.11 * s, cy - 0.44 * s, cx + 0.11 * s, cy - 0.34 * s], fill=TIMBER_M, outline=INK, width=2 * S)
    d.ellipse([cx - 0.15 * s, cy + 0.0 * s, cx - 0.04 * s, cy + 0.1 * s], fill=(255, 210, 210, 255))


def item_ward_staff(d, cx, cy, s):
    d.line([(cx - 0.3 * s, cy + 0.4 * s), (cx + 0.2 * s, cy - 0.25 * s)], fill=INK, width=int(0.12 * s))
    d.line([(cx - 0.28 * s, cy + 0.37 * s), (cx + 0.2 * s, cy - 0.25 * s)], fill=CEDAR, width=int(0.12 * s) - 2 * OL)
    d.ellipse([cx + 0.04 * s, cy - 0.48 * s, cx + 0.42 * s, cy - 0.1 * s], fill=COPPER, outline=INK, width=OL)
    d.ellipse([cx + 0.13 * s, cy - 0.39 * s, cx + 0.33 * s, cy - 0.19 * s], fill=(120, 220, 210, 255), outline=INK, width=2 * S)


def item_longbow(d, cx, cy, s):
    bb = [cx - 0.42 * s, cy - 0.5 * s, cx + 0.5 * s, cy + 0.5 * s]
    sx = cx + 0.04 * s + 0.46 * s * math.cos(math.radians(115))
    d.line([(sx, cy - 0.46 * s), (sx, cy + 0.46 * s)], fill=(240, 236, 220, 255), width=3 * S)
    d.arc(bb, 115, 245, fill=INK, width=int(0.15 * s))
    d.arc([bb[0] + OL / 2, bb[1] + OL / 2, bb[2] - OL / 2, bb[3] - OL / 2], 117, 243, fill=MAPLE, width=int(0.15 * s) - 2 * OL)


def item_steel_sword(d, cx, cy, s):
    item_sword(d, cx, cy, s * 1.1, STEEL)


def item_steel_coat(d, cx, cy, s):
    pts = [(-0.24, -0.36), (0.24, -0.36), (0.42, -0.1), (0.3, 0.0), (0.26, 0.4), (-0.26, 0.4), (-0.3, 0.0), (-0.42, -0.1)]
    poly(d, [(cx + x * s, cy + y * s) for x, y in pts], STEEL)
    d.line([(cx, cy - 0.3 * s), (cx, cy + 0.35 * s)], fill=shade(STEEL, 0.7), width=2 * S)
    for y in (-0.15, 0.05, 0.25):
        d.line([(cx - 0.22 * s, cy + y * s), (cx + 0.22 * s, cy + y * s)], fill=shade(STEEL, 0.75), width=2 * S)


def item_maul(d, cx, cy, s):
    d.line([(cx - 0.32 * s, cy + 0.36 * s), (cx + 0.14 * s, cy - 0.14 * s)], fill=INK, width=int(0.12 * s))
    d.line([(cx - 0.3 * s, cy + 0.34 * s), (cx + 0.14 * s, cy - 0.14 * s)], fill=TIMBER_M, width=int(0.12 * s) - 2 * OL)
    poly(d, [(cx + 0.0 * s, cy - 0.3 * s), (cx + 0.22 * s, cy - 0.44 * s), (cx + 0.42 * s, cy - 0.12 * s), (cx + 0.2 * s, cy + 0.02 * s)], STEEL)


def item_mail(d, cx, cy, s, col=MOONSILVER):
    pts = [(-0.24, -0.36), (0.24, -0.36), (0.42, -0.1), (0.3, 0.0), (0.26, 0.4), (-0.26, 0.4), (-0.3, 0.0), (-0.42, -0.1)]
    poly(d, [(cx + x * s, cy + y * s) for x, y in pts], col)
    for x in (-0.15, 0.0, 0.15):
        for y in (-0.15, 0.05, 0.25):
            d.ellipse([cx + (x - 0.04) * s, cy + (y - 0.04) * s, cx + (x + 0.04) * s, cy + (y + 0.04) * s], outline=shade(col, 0.6), width=S)


def item_brigandine(d, cx, cy, s):
    item_coat(d, cx, cy, s)
    d.line([(cx - 0.24 * s, cy - 0.34 * s), (cx + 0.24 * s, cy - 0.34 * s)], fill=(150, 110, 80, 255), width=int(0.1 * s))


def item_focus(d, cx, cy, s):
    d.line([(cx - 0.3 * s, cy + 0.4 * s), (cx + 0.2 * s, cy - 0.25 * s)], fill=INK, width=int(0.12 * s))
    d.line([(cx - 0.28 * s, cy + 0.37 * s), (cx + 0.2 * s, cy - 0.25 * s)], fill=IRONWOOD, width=int(0.12 * s) - 2 * OL)
    poly(d, [(cx + 0.24 * s, cy - 0.5 * s), (cx + 0.4 * s, cy - 0.28 * s), (cx + 0.24 * s, cy - 0.08 * s), (cx + 0.08 * s, cy - 0.28 * s)], MOONSILVER)


def item_greatbow(d, cx, cy, s):
    bb = [cx - 0.44 * s, cy - 0.52 * s, cx + 0.52 * s, cy + 0.52 * s]
    sx = cx + 0.04 * s + 0.48 * s * math.cos(math.radians(115))
    d.line([(sx, cy - 0.48 * s), (sx, cy + 0.48 * s)], fill=(240, 236, 220, 255), width=3 * S)
    d.arc(bb, 115, 245, fill=INK, width=int(0.17 * s))
    d.arc([bb[0] + OL / 2, bb[1] + OL / 2, bb[2] - OL / 2, bb[3] - OL / 2], 117, 243, fill=IRONWOOD, width=int(0.17 * s) - 2 * OL)


def potion(colour):
    def draw(d, cx, cy, s):
        d.ellipse([cx - 0.28 * s, cy - 0.12 * s, cx + 0.28 * s, cy + 0.4 * s], fill=colour, outline=INK, width=OL)
        d.rectangle([cx - 0.09 * s, cy - 0.36 * s, cx + 0.09 * s, cy - 0.1 * s], fill=(220, 230, 236, 255), outline=INK, width=OL)
        d.rectangle([cx - 0.11 * s, cy - 0.44 * s, cx + 0.11 * s, cy - 0.34 * s], fill=TIMBER_M, outline=INK, width=2 * S)
        d.ellipse([cx - 0.15 * s, cy + 0.0 * s, cx - 0.04 * s, cy + 0.1 * s], fill=(255, 255, 255, 200))
    return draw


ITEMS = {
    "iron_sword": item_sword,
    "cedar_bow": item_bow,
    "iron_dagger": item_dagger,
    "cedar_staff": item_staff,
    "riveted_coat": item_coat,
    "recovery_tonic": item_tonic,
    "copper_ward_staff": item_ward_staff,
    "maple_longbow": item_longbow,
    "steel_sword": item_steel_sword,
    "steel_coat": item_steel_coat,
    "antidote": potion((96, 176, 92, 255)),
    "warding_draught": potion((120, 96, 190, 255)),
    # Phase 7 advanced catalogue
    "steel_maul": item_maul,
    "steel_stiletto": lambda d, cx, cy, s: item_sword(d, cx + 0.1 * s, cy - 0.1 * s, s * 0.75, STEEL),
    "moonsilver_blade": lambda d, cx, cy, s: item_sword(d, cx, cy, s * 1.15, MOONSILVER),
    "moonsilver_mail": item_mail,
    "fur_brigandine": item_brigandine,
    "ironwood_greatbow": item_greatbow,
    "moonsilver_focus": item_focus,
    "strong_tonic": potion((176, 40, 60, 255)),
    "pathfinder_draught": potion((200, 170, 70, 255)),
    "trapsense_draught": potion((80, 150, 200, 255)),
}


def icons():
    for key, fn in list(RESOURCES.items()) + list(ITEMS.items()):
        img, d, cx, cy = icon_canvas()
        fn(d, cx, cy, 80 * S)
        folder = "Icons/Resources" if key in RESOURCES else "Icons/Items"
        finish("icon_" + key, img, (cx, cy), folder, desc=f"Icon: {key.replace('_', ' ')}", crop=False)
    # carry-stack pieces: same drawings, smaller, pivot at bottom centre so they stack upward
    for key, fn in list(RESOURCES.items()) + list(ITEMS.items()):
        img = Image.new("RGBA", (64 * S, 48 * S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        fn(d, 32 * S, 26 * S, 54 * S)
        finish("stack_" + key, img, (32 * S, 44 * S), "Stacks", desc=f"Carry/stack piece: {key.replace('_', ' ')}", crop=False)


# ---------------------------------------------------------------- racks and props
def rack(name, kind, desc, empty=False):
    img = canvas(1.5, 1.6)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.3 * U
    shadow_ellipse(d, cx, base, 0.55 * U, 0.16 * U)
    if kind == "weapon":
        for x in (-0.45, 0.45):
            post(d, (cx + x * U, base), 0.95 * U, 0.08)
        d.line([(cx - 0.5 * U, base - 0.75 * U), (cx + 0.5 * U, base - 0.75 * U)], fill=INK, width=int(0.08 * U))
        d.line([(cx - 0.5 * U, base - 0.75 * U), (cx + 0.5 * U, base - 0.75 * U)], fill=TIMBER_L, width=int(0.08 * U) - 2 * S)
        if not empty:
            for i, fn in enumerate((item_sword, item_bow, item_staff)):
                fn(d, cx + (i - 1) * 0.3 * U, base - 0.45 * U, 0.5 * U)
    elif kind == "armor":
        post(d, (cx, base), 0.9 * U, 0.08)
        if empty:
            d.line([(cx - 0.3 * U, base - 0.88 * U), (cx + 0.3 * U, base - 0.88 * U)], fill=INK, width=int(0.07 * U))
            d.line([(cx - 0.3 * U, base - 0.88 * U), (cx + 0.3 * U, base - 0.88 * U)], fill=TIMBER_L, width=int(0.07 * U) - 2 * S)
        else:
            item_coat(d, cx, base - 0.62 * U, 0.75 * U)
    else:
        for y in (0.3, 0.7):
            poly(d, [(cx - 0.5 * U, base - y * U), (cx + 0.5 * U, base - y * U), (cx + 0.5 * U, base - (y + 0.07) * U), (cx - 0.5 * U, base - (y + 0.07) * U)], TIMBER_L)
            if not empty:
                for i in range(3):
                    item_tonic(d, cx + (i - 1) * 0.3 * U, base - (y + 0.2) * U, 0.32 * U)
        for x in (-0.5, 0.5):
            post(d, (cx + x * U, base), 1.0 * U, 0.07)
    return finish(name, img, (cx, base), "Props", desc=desc)


def bell():
    img = canvas(0.5, 0.6)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.08 * U
    d.rectangle([cx - 0.14 * U, base - 0.04 * U, cx + 0.14 * U, base], fill=TIMBER_M, outline=INK, width=2 * S)
    d.chord([cx - 0.14 * U, base - 0.34 * U, cx + 0.14 * U, base + 0.0 * U], 180, 360, fill=(232, 190, 80, 255), outline=INK, width=OL)
    d.rectangle([cx - 0.14 * U, base - 0.17 * U, cx + 0.14 * U, base - 0.04 * U], fill=(232, 190, 80, 255))
    d.line([(cx - 0.14 * U, base - 0.04 * U), (cx + 0.14 * U, base - 0.04 * U)], fill=INK, width=2 * S)
    d.ellipse([cx - 0.03 * U, base - 0.4 * U, cx + 0.03 * U, base - 0.34 * U], fill=INK)
    return finish("prop_bell", img, (cx, base), "Props", desc="Store counter bell (sandbox persistence sample)")


def staging_pile(name, key, desc):
    img = canvas(1.2, 0.9)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.15 * U
    shadow_ellipse(d, cx, base, 0.5 * U, 0.14 * U)
    fn = RESOURCES[key]
    for i, (x, y) in enumerate(((-0.25, 0.1), (0.0, 0.08), (0.25, 0.1), (-0.12, 0.26), (0.13, 0.26), (0.0, 0.42))):
        fn(d, cx + x * U, base - y * U, 0.42 * U)
    return finish(name, img, (cx, base), "Props", desc=desc)


def blob_shadow():
    img = Image.new("RGBA", (64 * S, 32 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([0, 0, 64 * S - 1, 32 * S - 1], fill=(40, 30, 22, 90))
    img = img.filter(ImageFilter.GaussianBlur(3 * S))
    finish("blob_shadow", img, (32 * S, 16 * S), "Props", desc="Soft blob shadow", crop=False)


def deco_flowers():
    for i, col in enumerate(((240, 220, 120, 255), (236, 140, 160, 255), (250, 250, 250, 255))):
        img = Image.new("RGBA", (40 * S, 28 * S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        rnd = random.Random(30 + i)
        for _ in range(4):
            x, y = rnd.uniform(8, 32) * S, rnd.uniform(8, 20) * S
            d.ellipse([x - 3 * S, y - 3 * S, x + 3 * S, y + 3 * S], fill=col, outline=INK, width=1 * S)
        finish(f"deco_flowers_{i}", img, (20 * S, 22 * S), "Props", desc="Ground flower decal", crop=False)


# ---------------------------------------------------------------- UI kit
def ui_panel(name, fill, edge, radius, size=96, border=30, desc=""):
    img = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([2 * S, 4 * S, size * S - 2 * S, size * S - 1 * S], radius=radius * S, fill=(40, 30, 22, 80))  # drop shadow
    d.rounded_rectangle([2 * S, 1 * S, size * S - 2 * S, size * S - 4 * S], radius=radius * S, fill=fill, outline=edge, width=4 * S)
    d.line([(radius * S, 7 * S), (size * S - radius * S, 7 * S)], fill=shade(fill, 1.08), width=2 * S)
    finish(name, img, (size * S / 2, size * S / 2), "UI", border=[border, border, border, border], desc=desc, crop=False)


def ui_icon(name, fn, desc):
    img = Image.new("RGBA", (64 * S, 64 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    fn(d, 32 * S, 32 * S, 26 * S)
    finish(name, img, (32 * S, 32 * S), "UI/Icons", desc=desc, crop=False)


def ic_coin(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(236, 190, 70, 255), outline=INK, width=OL)
    d.ellipse([cx - r * 0.55, cy - r * 0.55, cx + r * 0.55, cy + r * 0.55], outline=(200, 150, 40, 255), width=2 * S)


def ic_star(d, cx, cy, r):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = r if i % 2 == 0 else r * 0.45
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    poly(d, pts, (120, 170, 220, 255))


def ic_bag(d, cx, cy, r):
    d.rounded_rectangle([cx - r * 0.8, cy - r * 0.45, cx + r * 0.8, cy + r * 0.9], radius=r * 0.3, fill=TIMBER_L, outline=INK, width=OL)
    d.arc([cx - r * 0.45, cy - r * 0.95, cx + r * 0.45, cy - r * 0.05], 180, 360, fill=INK, width=OL)


def ic_check(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(110, 176, 96, 255), outline=INK, width=OL)
    d.line([(cx - r * 0.45, cy), (cx - r * 0.1, cy + r * 0.38), (cx + r * 0.5, cy - r * 0.35)], fill=(255, 255, 255, 255), width=4 * S, joint="curve")


def ic_warning(d, cx, cy, r):
    poly(d, [(cx, cy - r), (cx + r, cy + r * 0.8), (cx - r, cy + r * 0.8)], (236, 180, 60, 255))
    d.line([(cx, cy - r * 0.35), (cx, cy + r * 0.25)], fill=INK, width=4 * S)
    d.ellipse([cx - 2.5 * S, cy + r * 0.42, cx + 2.5 * S, cy + r * 0.42 + 5 * S], fill=INK)


def ic_lock(d, cx, cy, r):
    d.arc([cx - r * 0.5, cy - r * 0.95, cx + r * 0.5, cy + r * 0.1], 180, 360, fill=INK, width=OL + 2 * S)
    d.rounded_rectangle([cx - r * 0.75, cy - r * 0.3, cx + r * 0.75, cy + r * 0.9], radius=r * 0.15, fill=(160, 160, 172, 255), outline=INK, width=OL)


def ic_clock(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(244, 236, 220, 255), outline=INK, width=OL)
    d.line([(cx, cy), (cx, cy - r * 0.6)], fill=INK, width=3 * S)
    d.line([(cx, cy), (cx + r * 0.45, cy + r * 0.2)], fill=INK, width=3 * S)


def ic_unknown(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(150, 140, 170, 255), outline=INK, width=OL)
    d.arc([cx - r * 0.35, cy - r * 0.65, cx + r * 0.35, cy + r * 0.05], 200, 90, fill=(255, 255, 255, 255), width=4 * S)
    d.ellipse([cx - 3 * S, cy + r * 0.45, cx + 3 * S, cy + r * 0.45 + 6 * S], fill=(255, 255, 255, 255))


def ic_hand(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(244, 236, 220, 255), outline=INK, width=OL)
    d.rounded_rectangle([cx - r * 0.4, cy - r * 0.2, cx + r * 0.4, cy + r * 0.6], radius=r * 0.2, fill=SKINS[0], outline=INK, width=2 * S)
    d.rounded_rectangle([cx - r * 0.12, cy - r * 0.75, cx + r * 0.12, cy], radius=r * 0.1, fill=SKINS[0], outline=INK, width=2 * S)


def ic_gear(d, cx, cy, r):
    pts = []
    for i in range(16):
        a = i * math.pi / 8
        rr = r if i % 2 == 0 else r * 0.75
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    poly(d, pts, (170, 170, 184, 255))
    d.ellipse([cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3], fill=(244, 236, 220, 255), outline=INK, width=2 * S)


def ic_board(d, cx, cy, r):
    d.rounded_rectangle([cx - r, cy - r * 0.8, cx + r, cy + r * 0.75], radius=r * 0.12, fill=TIMBER_L, outline=INK, width=OL)
    for i, (x, y) in enumerate(((-0.55, -0.45), (0.05, -0.5), (-0.25, 0.05))):
        poly(d, [(cx + x * r, cy + y * r), (cx + (x + 0.45) * r, cy + (y + 0.05) * r), (cx + (x + 0.45) * r, cy + (y + 0.5) * r), (cx + x * r, cy + (y + 0.45) * r)],
             (244, 232, 208, 255), width=2 * S)
    d.ellipse([cx + 0.35 * r, cy + 0.15 * r, cx + 0.65 * r, cy + 0.45 * r], fill=(200, 72, 60, 255), outline=INK, width=2 * S)


def ui_round_button():
    img = Image.new("RGBA", (128 * S, 128 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([4 * S, 8 * S, 124 * S, 126 * S], fill=(40, 30, 22, 90))
    d.ellipse([4 * S, 2 * S, 124 * S, 120 * S], fill=(232, 186, 92, 255), outline=INK, width=5 * S)
    d.ellipse([14 * S, 10 * S, 114 * S, 104 * S], outline=(248, 214, 140, 255), width=3 * S)
    finish("ui_round_button", img, (64 * S, 64 * S), "UI", desc="Round context button background", crop=False)


def output_pad():
    """Wooden pallet where finished goods appear (station output pad)."""
    img = canvas(1.6, 1.0)
    d = ImageDraw.Draw(img)
    a = b = 1.1
    F = (img.width / 2, img.height - 0.2 * U)
    F_, R, B, L = iso_pts(F, a, b)
    shadow_ellipse(d, (F[0] + B[0]) / 2, (F[1] + B[1]) / 2 + 0.03 * U, 0.62 * U, 0.3 * U)
    box(d, F, a, b, 0.1 * U, PLANK, TIMBER_M, TIMBER_D)
    for t in (0.33, 0.66):   # plank gaps
        p1 = (L[0] + (F_[0] - L[0]) * t, L[1] + (F_[1] - L[1]) * t - 0.1 * U)
        p2 = (B[0] + (R[0] - B[0]) * t, B[1] + (R[1] - B[1]) * t - 0.1 * U)
        d.line([p1, p2], fill=TIMBER_M, width=2 * S)
    return finish("prop_output_pad", img, footprint_centre(F, a, b), "Props", desc="Station output pad (pallet) for finished goods")


def receiver_zone():
    """Dashed diamond ring marking a receiver zone; tinted and faded in-engine. 1 unit radius."""
    img = Image.new("RGBA", (2 * U + 8 * S, U + 8 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = img.width / 2, img.height / 2
    pts = [(cx, cy - U / 2), (cx + U, cy), (cx, cy + U / 2), (cx - U, cy)]
    d.polygon(pts, fill=(255, 244, 214, 70))
    for i in range(4):
        a, b = pts[i], pts[(i + 1) % 4]
        for k in range(6):
            t0, t1 = k / 6 + 0.02, k / 6 + 0.11
            d.line([(a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0), (a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1)],
                   fill=(255, 248, 226, 255), width=4 * S)
    finish("receiver_zone", img, (cx, cy), "Props", desc="Receiver zone ground marker (dashed diamond)", crop=False)


def icon_bubble():
    img = Image.new("RGBA", (80 * S, 92 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([4 * S, 4 * S, 76 * S, 72 * S], radius=18 * S, fill=(252, 246, 232, 255), outline=INK, width=OL)
    d.polygon([(30 * S, 70 * S), (50 * S, 70 * S), (40 * S, 86 * S)], fill=(252, 246, 232, 255), outline=INK)
    d.line([(31 * S, 70 * S), (49 * S, 70 * S)], fill=(252, 246, 232, 255), width=OL)
    finish("ui_bubble", img, (40 * S, 86 * S), "Props", desc="World icon bubble above receivers", crop=False)


def joystick():
    img = Image.new("RGBA", (256 * S, 256 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([6 * S, 6 * S, 250 * S, 250 * S], fill=(40, 30, 22, 50), outline=(252, 246, 232, 200), width=6 * S)
    finish("ui_joystick_ring", img, (128 * S, 128 * S), "UI", desc="Floating joystick ring", crop=False)
    img = Image.new("RGBA", (128 * S, 128 * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([6 * S, 10 * S, 122 * S, 126 * S], fill=(40, 30, 22, 80))
    d.ellipse([6 * S, 4 * S, 122 * S, 118 * S], fill=(252, 246, 232, 235), outline=INK, width=4 * S)
    finish("ui_joystick_knob", img, (64 * S, 64 * S), "UI", desc="Floating joystick knob", crop=False)


def tree_stump():
    img = canvas(1.0, 0.8)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.2 * U
    shadow_ellipse(d, cx, base, 0.36 * U, 0.12 * U)
    d.rectangle([cx - 0.14 * U, base - 0.26 * U, cx + 0.14 * U, base], fill=TIMBER_M, outline=INK, width=OL)
    d.ellipse([cx - 0.14 * U, base - 0.33 * U, cx + 0.14 * U, base - 0.19 * U], fill=(232, 196, 140, 255), outline=INK, width=OL)
    d.ellipse([cx - 0.05 * U, base - 0.28 * U, cx + 0.05 * U, base - 0.24 * U], fill=TIMBER_L)
    for x in (-0.24, 0.2):   # wood chips
        d.ellipse([cx + x * U, base - 0.04 * U, cx + (x + 0.07) * U, base], fill=(232, 196, 140, 255))
    return finish("tree_stump", img, (cx, base), "Nature", desc="Felled tree stump")


def trail_marker():
    img = canvas(0.5, 0.9)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.1 * U
    post(d, (cx, base), 0.55 * U, 0.07, TIMBER_M)
    poly(d, [(cx - 0.02 * U, base - 0.55 * U), (cx + 0.2 * U, base - 0.5 * U), (cx - 0.02 * U, base - 0.42 * U)], (230, 200, 90, 255), width=2 * S)
    return finish("trail_marker", img, (cx, base), "Props", desc="Forest trail marker post (one per area reached)")


def write_meta_and_provenance():
    with open(os.path.join(OUT, "sprite_meta.json"), "w") as f:
        json.dump({"ppu": PPU, "sprites": meta}, f, indent=1, sort_keys=True)
    lines = [
        "# Placeholder kit provenance",
        "",
        "All files in this folder are ORIGINAL procedural placeholder art generated by",
        "`Tools/PlaceholderArt/generate_placeholders.py` (Pillow drawing primitives only).",
        "No third-party, licensed, traced or AI-generated images were used.",
        "They are temporary Phase 0 placeholders to be replaced after the progression checkpoint.",
        "",
        "| File | Description |",
        "|---|---|",
    ]
    lines += [f"| `{p}` | {d} |" for p, d in sorted(provenance)]
    with open(os.path.join(OUT, "PROVENANCE.md"), "w") as f:
        f.write("\n".join(lines) + "\n")


def beacon_trophy():
    """Phase 7: settlement trophy shown in town once the final expedition succeeds."""
    img = canvas(1.2, 2.0)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2, img.height - 0.3 * U
    shadow_ellipse(d, cx, base, 0.4 * U, 0.12 * U)
    box(d, (cx, base), 0.5, 0.5, 0.25 * U, STONE_L, STONE_M, STONE_D)
    post(d, (cx, base - 0.2 * U), 1.0 * U, 0.07, STONE_D)
    d.ellipse([cx - 0.24 * U, base - 1.5 * U, cx + 0.24 * U, base - 1.02 * U], fill=GLOW, outline=INK, width=OL)
    d.ellipse([cx - 0.12 * U, base - 1.38 * U, cx + 0.12 * U, base - 1.14 * U], fill=(255, 236, 170, 255))
    return finish("prop_beacon_trophy", img, (cx, base), "Props", desc="Beacon trophy: settlement monument after the final expedition")


def pennant():
    """Phase 8: building level pennant (one per level above 1)."""
    img = canvas(0.4, 0.7)
    d = ImageDraw.Draw(img)
    cx, base = img.width / 2 - 0.08 * U, img.height - 0.04 * U
    post(d, (cx, base), 0.6 * U, 0.05, TIMBER_D)
    poly(d, [(cx + 0.025 * U, base - 0.58 * U), (cx + 0.3 * U, base - 0.5 * U), (cx + 0.025 * U, base - 0.4 * U)], (196, 64, 52, 255), width=2 * S)
    return finish("prop_pennant", img, (cx, base), "Props", desc="Building level pennant (one per upgrade level)")


def app_icon():
    """Placeholder app icon (1024 px, original): a supply crate under the Beacon's flame, warm sky. Not a sprite."""
    N = 1024
    img = Image.new("RGBA", (N, N), (0, 0, 0, 255))
    d = ImageDraw.Draw(img)
    top, bottom = (58, 82, 96), (232, 170, 92)
    for y in range(N):
        k = y / (N - 1)
        d.line([(0, y), (N, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * k) for i in range(3)) + (255,))
    d.ellipse([90, 760, 934, 1100], fill=(110, 160, 84, 255))
    # crate (isometric box)
    cx, base, w, h = 512, 820, 250, 230
    left = [(cx - w, base - w // 2), (cx, base), (cx, base - h), (cx - w, base - w // 2 - h)]
    right = [(cx, base), (cx + w, base - w // 2), (cx + w, base - w // 2 - h), (cx, base - h)]
    topf = [(cx - w, base - w // 2 - h), (cx, base - h), (cx + w, base - w // 2 - h), (cx, base - w - h)]
    for face, col in ((left, (176, 116, 64)), (right, (140, 88, 46)), (topf, (214, 160, 100))):
        d.polygon(face, fill=col + (255,), outline=(59, 42, 32, 255))
        d.line(face + [face[0]], fill=(59, 42, 32, 255), width=14)
    for k in (0.33, 0.66):
        d.line([(cx - w, base - w // 2 - h * k), (cx, base - h * k)], fill=(59, 42, 32, 255), width=10)
        d.line([(cx, base - h * k), (cx + w, base - w // 2 - h * k)], fill=(59, 42, 32, 255), width=10)
    # beacon flame
    fx, fy = 512, 300
    d.ellipse([fx - 150, fy - 150, fx + 150, fy + 150], fill=(255, 200, 96, 120))
    d.polygon([(fx, fy - 170), (fx + 95, fy + 40), (fx, fy + 120), (fx - 95, fy + 40)], fill=(255, 170, 64, 255), outline=(59, 42, 32, 255))
    d.polygon([(fx, fy - 70), (fx + 45, fy + 50), (fx, fy + 95), (fx - 45, fy + 50)], fill=(255, 236, 170, 255))
    out = os.path.join(ROOT, "Assets", "Ravenholm", "Art", "AppIcon")
    os.makedirs(out, exist_ok=True)
    img.convert("RGB").save(os.path.join(out, "app_icon.png"))
    provenance.append(("../AppIcon/app_icon.png", "Placeholder app icon: supply crate under the Beacon flame (1024 px)"))


def main():
    tile("tile_grass_a", GRASS_A, GRASS_D, "tufts", 1)
    tile("tile_grass_b", GRASS_B, GRASS_D, "tufts", 2)
    tile("tile_path", PATH_C, PATH_D, "pebbles", 3)
    tile("tile_plaza", PLAZA_C, PLAZA_D, "flags", 4)
    tile("tile_dirt", DIRT_C, DIRT_D, "pebbles", 5)
    tile("tile_forest_floor", FOREST_C, FOREST_D, "tufts", 6)
    store(); smelter(); forge(); mill(); bench(); apothecary(); trader(); mine_mouth(); forest_gate()
    tree_cedar(); tree_maple(); tree_ironwood(); bush_herb()
    rock("rock_stone", None, 11, "Rock face: plain stone")
    rock("rock_iron", (184, 110, 80, 255), 12, "Rock face: iron-veined (rust flecks)")
    rock("rock_copper", (96, 186, 160, 255), 13, "Rock face: copper-veined (verdigris flecks)")
    for i, skin in enumerate(SKINS):
        char_base(f"char_base_{i}", skin, f"Character base body, skin variant {i}")
    for name, fn, desc in (
        ("ov_supplier", ov_supplier, "Overlay: supplier (player) outfit and cap"),
        ("ov_miner", ov_miner, "Overlay: miner helmet, tunic, pickaxe"),
        ("ov_woodcutter", ov_woodcutter, "Overlay: woodcutter cap, tunic, axe"),
        ("ov_warrior", ov_warrior, "Overlay: warrior helm, shield, sword"),
        ("ov_ranger", ov_ranger, "Overlay: ranger hood, bow"),
        ("ov_rogue", ov_rogue, "Overlay: rogue hood, mask, dagger"),
        ("ov_mage", ov_mage, "Overlay: mage hat, robe, staff"),
    ):
        overlay(name, desc, fn)
    icons()
    rack("rack_weapons", "weapon", "Store weapon rack")
    rack("rack_armor", "armor", "Store armor stand")
    rack("rack_potions", "potion", "Store potion shelf")
    rack("rack_weapons_empty", "weapon", "Store weapon rack (empty; items drawn in-engine)", True)
    rack("rack_armor_empty", "armor", "Store armor stand (empty; items drawn in-engine)", True)
    rack("rack_potions_empty", "potion", "Store potion shelf (empty; items drawn in-engine)", True)
    bell()
    beacon_trophy()
    pennant()
    staging_pile("pile_ore", "iron_ore", "Mine staging pile (ore)")
    staging_pile("pile_logs", "cedar_log", "Forest staging pile (logs)")
    staging_pile("pile_ingots", "iron_ingot", "Processed output pile (ingots)")
    staging_pile("pile_lumber", "cedar_lumber", "Processed output pile (lumber)")
    blob_shadow()
    deco_flowers()
    output_pad()
    tree_stump()
    trail_marker()
    receiver_zone()
    icon_bubble()
    joystick()
    ui_panel("ui_panel", (246, 236, 214, 255), INK, 18, desc="9-slice parchment panel")
    ui_panel("ui_panel_dark", (64, 54, 50, 235), (30, 22, 18, 255), 18, desc="9-slice dark translucent panel")
    ui_panel("ui_button", (232, 186, 92, 255), INK, 22, desc="9-slice timber/gold button")
    ui_panel("ui_chip", (252, 246, 232, 240), INK, 40, size=96, border=40, desc="9-slice pill chip for HUD counters")
    ui_round_button()
    for name, fn, desc in (
        ("ic_coin", ic_coin, "Status icon: gold"), ("ic_star", ic_star, "Status icon: reputation"),
        ("ic_bag", ic_bag, "Status icon: carry"), ("ic_check", ic_check, "Status icon: ready/ok"),
        ("ic_warning", ic_warning, "Status icon: warning/blocked"), ("ic_lock", ic_lock, "Status icon: locked"),
        ("ic_clock", ic_clock, "Status icon: timer"), ("ic_unknown", ic_unknown, "Status icon: unknown/not identified"),
        ("ic_hand", ic_hand, "Status icon: interact"), ("ic_gear", ic_gear, "Status icon: settings/debug"),
        ("ic_board", ic_board, "Status icon: Job Board"),
    ):
        ui_icon(name, fn, desc)
    app_icon()
    write_meta_and_provenance()
    print(f"wrote {len(meta)} sprites to {OUT}")


if __name__ == "__main__":
    main()
