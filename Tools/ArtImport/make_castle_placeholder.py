#!/usr/bin/env python3
"""
Placeholder for the castle that posts the jobs (2026-10-15), until Astra draws it: a flat-coloured isometric keep with
two towers and a gate, no painted outline (the game adds it). Writes Art/Placeholder/Props/castle.png and its pivot.

Usage: python3 -I make_castle_placeholder.py <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys
from PIL import Image, ImageDraw

ART = sys.argv[1]
W, H = 640, 640
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
STONE_L, STONE_R, STONE_TOP = (196, 188, 172, 255), (160, 152, 138, 255), (214, 207, 192, 255)
ROOF_L, ROOF_R = (86, 112, 168, 255), (64, 86, 136, 255)
DARK = (70, 58, 50, 255)


def block(cx, base_y, half_w, depth, height):
    """An isometric box: front corner at (cx, base_y)."""
    f = (cx, base_y)
    r = (cx + half_w, base_y - half_w // 2)
    l = (cx - depth, base_y - depth // 2)
    up = lambda p: (p[0], p[1] - height)
    d.polygon([l, f, up(f), up(l)], fill=STONE_L)
    d.polygon([f, r, up(r), up(f)], fill=STONE_R)
    back = (l[0] + half_w, l[1] - half_w // 2)
    d.polygon([up(l), up(f), up(r), up(back)], fill=STONE_TOP)
    # crenellations along the two front top edges
    for i in range(0, 6):
        t = i / 6
        for a, b, col in ((l, f, STONE_L), (f, r, STONE_R)):
            x = a[0] + (b[0] - a[0]) * t
            y = a[1] + (b[1] - a[1]) * t - height
            d.rectangle([x + 2, y - 14, x + 12, y], fill=col)


def tower(cx, base_y, r, height):
    d.rectangle([cx - r, base_y - height, cx, base_y], fill=STONE_L)
    d.rectangle([cx, base_y - height, cx + r, base_y], fill=STONE_R)
    d.ellipse([cx - r, base_y - height - r // 2, cx + r, base_y - height + r // 2], fill=STONE_TOP)
    d.polygon([(cx - r - 6, base_y - height), (cx, base_y - height - r * 2.2), (cx, base_y - height + 6)], fill=ROOF_L)
    d.polygon([(cx, base_y - height + 6), (cx, base_y - height - r * 2.2), (cx + r + 6, base_y - height)], fill=ROOF_R)


block(320, 600, 200, 200, 170)       # curtain wall
tower(150, 520, 46, 250)              # left tower (behind)
tower(500, 520, 46, 250)              # right tower
block(320, 470, 90, 90, 230)          # keep
tower(320, 380, 40, 210)              # keep turret
# gate on the right-front face
d.polygon([(360, 580), (400, 560), (400, 500), (360, 520)], fill=DARK)
d.line([(320, 160), (320, 110)], fill=DARK, width=4)
d.polygon([(322, 110), (360, 120), (322, 132)], fill=(196, 64, 52, 255))   # flag
img.save(os.path.join(ART, "Props", "castle.png"))

meta_path = os.path.join(ART, "sprite_meta.json")
meta = json.load(open(meta_path))
meta["sprites"]["Props/castle.png"] = {"pivot": [0.5, 1 - 600 / H], "ppu": 160, "outline": True}
json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
print("castle placeholder written")
