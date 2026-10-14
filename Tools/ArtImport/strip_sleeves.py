#!/usr/bin/env python3
"""
Takes the painted sleeves off the store owner's body pieces (2026-10-14, Kevin: "make the shoulder sleeve part of the
arm"). The game draws each sleeve as the top of the rubber-hose arm instead, so it moves with the arm.

A sleeve is the cream shirt colour (#E9D2B3, shadow #D0B99C) outside the torso's core: per row, everything cream
beyond the apron/strap columns. The shirt between the straps (the chest, the back) stays. Writes new bodies; the
originals stay in ArtSource/storeowner-v1.

Usage: python3 -I strip_sleeves.py <ArtSource/storeowner-v1> <Assets/.../Characters/StoreOwner>
"""
import sys
from PIL import Image

CREAM = [(0xE9, 0xD2, 0xB3), (0xD0, 0xB9, 0x9C)]
DARK = [(0x5A, 0x43, 0x35), (0x73, 0x53, 0x3A)]   # apron, straps


def near(c, palette, tol=18):
    return any(abs(c[0] - p[0]) + abs(c[1] - p[1]) + abs(c[2] - p[2]) <= tol for p in palette)


# The torso core's columns per view (pixels): sleeves are cream outside them, at shoulder height.
CORE = {"S": (200, 312), "SE": (205, 318), "E": (204, 335), "NE": (196, 316), "N": (200, 312)}
# Views where the sleeve was painted over the torso (profile): inside the core its shading and hem are flattened to the
# plain shirt colour, so no sleeve edge is left on the torso.
FLATTEN = {"E"}


def strip(src, dst, view):
    im = Image.open(src).convert("RGBA")
    px = im.load()
    lo, hi = CORE[view]
    w, h = im.size
    for y in range(200, 330):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            if lo <= x <= hi:
                if view in FLATTEN and near((r, g, b), CREAM[1:]):
                    px[x, y] = CREAM[0] + (255,)
                continue
            if near((r, g, b), CREAM):
                px[x, y] = (0, 0, 0, 0)
    im.save(dst)


if __name__ == "__main__":
    src, dst = sys.argv[1], sys.argv[2]
    for v in CORE:
        strip(f"{src}/body_{v}.png", f"{dst}/body_{v}.png", v)
        print(v, "ok")
