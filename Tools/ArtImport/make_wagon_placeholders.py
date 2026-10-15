#!/usr/bin/env python3
"""
Placeholder art for the wagon shop (the journey, step 3, 2026-10-15), until Astra's ox and wagon land:

- Wagon/wagon_parked_<1..3>.png: the parked wagon, its long side facing the camera (running along A, up-right), folded
  down into a counter. Pivot: the middle of its base. 1024 x 768, 256 px per unit.
- Wagon/carpet_<1..3>.png: one carpet tile (a 256 x 128 diamond), repeating; carpet_edge_<1..3>_a / _b: the fringe
  along a tile edge running along A / along B (pivot at the edge's middle).
- Furniture/<key>_<n>_stand_A / _B: free-standing versions of the wall pieces (weapon rack, bow and staff rack, potion
  shelves): the wall art on a frame with legs, 2 x 1 tiles, ground point at the footprint's centre. Their slots are the
  wall piece's, merged into Furniture/points.json.

Usage: python3 -I make_wagon_placeholders.py <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys
from PIL import Image, ImageDraw

ART = sys.argv[1]
meta_path = os.path.join(ART, "sprite_meta.json")
meta = json.load(open(meta_path))
os.makedirs(os.path.join(ART, "Wagon"), exist_ok=True)

A = (128, -64)   # one tile along A, in pixels (x right, y down)
B = (-128, -64)  # one tile along B


def at(o, a, b, up=0):
    return (o[0] + A[0] * a + B[0] * b, o[1] + A[1] * a + B[1] * b - up)


def save(img, rel, pivot_px, outline):
    img.save(os.path.join(ART, rel))
    w, h = img.size
    meta["sprites"][rel] = {"outline": outline, "pivot": [round(pivot_px[0] / w, 5), round(1 - pivot_px[1] / h, 5)], "ppu": 256}


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c[:3]) + (255,)


# ---------------------------------------------------------------- the wagon

WAGON = [
    # body, trim, canvas, canvas stripe, wheel
    ((150, 104, 66), (110, 76, 48), (236, 226, 196), None, (96, 66, 42)),
    ((70, 120, 96), (60, 60, 66), (238, 230, 205), (200, 70, 60), (70, 52, 40)),
    ((150, 50, 46), (214, 170, 70), (244, 236, 214), (60, 90, 150), (120, 80, 40)),
]
for lvl, (body, trim, canvas, stripe, wheel) in enumerate(WAGON, 1):
    W, H = 1024, 768
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    L, D = 3.0, 1.4            # length along A, depth along B (tiles)
    o = at((W // 2, 600), -L / 2, -D / 2)   # the base's front corner, so the base centre lands at the pivot
    bed, top = 70, 200        # bed height, side height (px)
    # wheels on the near long side
    for a in (0.55, L - 0.55):
        cx, cy = at(o, a, 0, 34)
        d.ellipse((cx - 46, cy - 52, cx + 46, cy + 52), fill=wheel, outline=shade(wheel, 0.6), width=8)
        d.ellipse((cx - 10, cy - 10, cx + 10, cy + 10), fill=shade(wheel, 0.6))
    # body: the near long side (along A) and the near end (along B)
    side = [at(o, 0, 0, bed), at(o, L, 0, bed), at(o, L, 0, top), at(o, 0, 0, top)]
    end = [at(o, 0, 0, bed), at(o, 0, D, bed), at(o, 0, D, top), at(o, 0, 0, top)]
    d.polygon(end, fill=shade(body, 0.78))
    d.polygon(side, fill=body)
    d.line([at(o, 0, 0, top), at(o, L, 0, top)], fill=trim, width=8)
    # the canvas cover: hoops over the bed, the far half showing above
    for i in range(7):
        a = L * i / 6
        p0 = at(o, a, 0, top)
        p1 = at(o, a, D, top)
        mid = ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2 - 150)
        d.line([p0, mid, p1], fill=shade(canvas, 0.7), width=6)
    cover = [at(o, 0, 0, top)]
    for i in range(9):
        t = i / 8
        p = at(o, 0, D * t, top + 150 * (1 - (2 * t - 1) ** 2))
        cover.append(p)
    cover += [at(o, L, D, top), at(o, L, D * 0.5, top + 150), at(o, L, 0, top)]
    d.polygon(cover + [at(o, L, 0, top)], fill=canvas)
    roof = [at(o, 0, 0, top), at(o, 0, D * 0.5, top + 150), at(o, L, D * 0.5, top + 150), at(o, L, 0, top)]
    d.polygon(roof, fill=shade(canvas, 0.93))
    if stripe:
        for k in (0.2, 0.5, 0.8):
            d.line([at(o, L * k, 0, top), at(o, L * k, D * 0.5, top + 150)], fill=stripe, width=14)
    if lvl == 3:   # banners
        for a in (0.1, L - 0.1):
            p = at(o, a, D * 0.5, top + 150)
            d.line([p, (p[0], p[1] - 90)], fill=trim, width=6)
            d.polygon([(p[0], p[1] - 90), (p[0] + 48, p[1] - 74), (p[0], p[1] - 58)], fill=stripe)
    # the folded-down counter along the near side, with a ledger, scales and a bell
    c0, c1 = at(o, 0.2, 0, bed + 40), at(o, L - 0.2, 0, bed + 40)
    c2, c3 = at(o, L - 0.2, -0.45, bed + 40), at(o, 0.2, -0.45, bed + 40)
    d.polygon([c0, c1, c2, c3], fill=shade(body, 1.15))
    d.polygon([c3, c2, (c2[0], c2[1] + 16), (c3[0], c3[1] + 16)], fill=shade(body, 0.7))
    for a in (0.35, L - 0.35):   # props under the counter
        p = at(o, a, -0.4, bed + 40)
        d.line([p, at(o, a, -0.4, 0)], fill=trim, width=8)
    lx, ly = at(o, 0.9, -0.22, bed + 44)
    d.polygon([(lx - 30, ly), (lx + 10, ly - 18), (lx + 46, ly), (lx + 6, ly + 18)], fill=(240, 236, 220))
    sx, sy = at(o, 1.8, -0.22, bed + 44)
    d.line([(sx, sy), (sx, sy - 46)], fill=(200, 170, 80), width=5)
    d.line([(sx - 30, sy - 40), (sx + 30, sy - 40)], fill=(200, 170, 80), width=5)
    bx, by = at(o, 2.5, -0.22, bed + 44)
    d.ellipse((bx - 16, by - 26, bx + 16, by + 4), fill=(220, 180, 60))
    save(img, f"Wagon/wagon_parked_{lvl}.png", (W // 2, 600), True)

# ---------------------------------------------------------------- the carpet

CARPET = [
    ((196, 160, 116), (176, 134, 94), (150, 88, 62)),   # plain woven rug
    ((168, 58, 52), (230, 190, 110), (120, 40, 40)),     # patterned, with fringe
    ((52, 74, 140), (226, 182, 72), (150, 40, 50)),      # rich, bordered
]
for lvl, (base, pat, accent) in enumerate(CARPET, 1):
    img = Image.new("RGBA", (256, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(128, 0), (256, 64), (128, 128), (0, 64)], fill=base)
    d.polygon([(128, 26), (204, 64), (128, 102), (52, 64)], fill=pat)
    d.polygon([(128, 46), (164, 64), (128, 82), (92, 64)], fill=accent)
    save(img, f"Wagon/carpet_{lvl}.png", (128, 64), False)
    for axis in ("a", "b"):
        e = Image.new("RGBA", (160, 96), (0, 0, 0, 0))
        d = ImageDraw.Draw(e)
        p0, p1 = ((16, 80), (144, 16)) if axis == "a" else ((16, 16), (144, 80))
        d.line([p0, p1], fill=pat if lvl > 1 else shade(base, 0.7), width=6)
        for i in range(9):   # tassels hanging toward the camera
            t = (i + 0.5) / 9
            x, y = p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t
            d.line([(x, y), (x, y + 10)], fill=accent if lvl > 1 else shade(base, 0.8), width=3)
        save(e, f"Wagon/carpet_edge_{lvl}_{axis}.png", (80, 48), False)

# ---------------------------------------------------------------- the ox (walking, facing right) and the camp sign

ox = Image.new("RGBA", (420, 300), (0, 0, 0, 0))
d = ImageDraw.Draw(ox)
hide, dark, horn = (150, 102, 66), (104, 70, 46), (236, 226, 196)
for x in (110, 150, 270, 310):   # legs
    d.rectangle((x - 12, 190, x + 12, 268), fill=dark)
    d.rectangle((x - 14, 262, x + 14, 274), fill=(60, 44, 32))
d.ellipse((70, 110, 340, 230), fill=hide)          # body
d.ellipse((90, 100, 210, 170), fill=shade(hide, 1.08))   # shoulder hump
d.polygon([(300, 130), (390, 120), (400, 190), (330, 200)], fill=hide)   # head
d.ellipse((370, 160, 410, 200), fill=(196, 150, 130))   # muzzle
d.polygon([(330, 120), (300, 84), (318, 82), (346, 118)], fill=horn)
d.polygon([(372, 118), (396, 80), (412, 84), (388, 122)], fill=horn)
d.ellipse((352, 140, 364, 152), fill=(30, 24, 20))   # eye
d.line([(72, 150), (40, 196)], fill=dark, width=8)   # tail
d.line([(250, 140), (300, 190)], fill=(90, 62, 40), width=10)   # the harness
save(ox, "Wagon/ox_walk.png", (210, 272), True)

sign = Image.new("RGBA", (200, 300), (0, 0, 0, 0))
d = ImageDraw.Draw(sign)
d.rectangle((92, 90, 108, 286), fill=(120, 84, 54))
d.polygon([(20, 60), (180, 40), (180, 120), (20, 140)], fill=(176, 134, 94))
d.polygon([(20, 60), (180, 40), (180, 52), (20, 72)], fill=(196, 160, 116))
for k in range(3):
    d.line([(40, 82 + k * 18), (160, 66 + k * 18)], fill=(110, 76, 48), width=5)
d.ellipse((70, 276, 130, 296), fill=(0, 0, 0, 50))
save(sign, "Wagon/camp_sign.png", (100, 288), True)

# ---------------------------------------------------------------- free-standing racks and shelves

pts_path = os.path.join(ART, "Furniture", "points.json")
pts = json.load(open(pts_path))
for key in ("weapon_rack", "long_rack", "potion_shelf"):
    for n in (1, 2, 3):
        for side, facing in (("left", "A"), ("right", "B")):
            name = f"{key}_{n}_left" if side == "left" else f"{key}_{n}_right"
            src = pts["assets"].get(name + ".png")
            if src is None and side == "left":
                name = f"{key}_{n}"
                src = pts["assets"].get(name + ".png")
            if src is None:
                continue
            wall = Image.open(os.path.join(ART, "Furniture", name + ".png")).convert("RGBA")
            w, h = wall.size
            ax, ay = src["anchor"]["x"], src["anchor"]["y"]
            img = Image.new("RGBA", (w, h + 40), (0, 0, 0, 0))
            d = ImageDraw.Draw(img)
            # The back line of the footprint (where the wall was): rising to the right for facing A.
            left_y, right_y = (ay + 64, ay - 64) if facing == "A" else (ay - 64, ay + 64)
            bottom = src["bounds"]["y"] + src["bounds"]["h"]
            wood, dark = (126, 88, 56), (90, 62, 40)
            for x, gy in ((ax - 112, left_y - 8), (ax + 112, right_y - 8)):   # legs from the piece down to the ground
                d.rectangle((x - 7, min(bottom - 40, gy - 20), x + 7, gy), fill=wood)
                d.ellipse((x - 14, gy - 6, x + 14, gy + 6), fill=dark)
            d.line([(ax - 112, left_y - 30), (ax + 112, right_y - 30)], fill=wood, width=10)   # a stretcher bar
            img.alpha_composite(wall)
            ground = (ax + 64, ay + 32) if facing == "A" else (ax - 64, ay + 32)   # the 2 x 1 footprint's centre
            rel = f"Furniture/{key}_{n}_stand_{facing}.png"
            save(img, rel, ground, True)
            pts["assets"][f"{key}_{n}_stand_{facing}.png"] = {
                "file": rel, "canvas": {"w": w, "h": h + 40},
                "pivot": {"x": ground[0] / w, "y": 1 - ground[1] / (h + 40)},
                "ground": {"x": ground[0], "y": ground[1]},
                "footprint": {"a": 2, "b": 1} if facing == "A" else {"a": 1, "b": 2},
                "facing": facing, "mount": "floor", "slots": src.get("slots", []),
                "source_pack": "placeholder: the wall piece on legs (make_wagon_placeholders.py)",
            }
json.dump(pts, open(pts_path, "w"), indent=1)
json.dump(meta, open(meta_path, "w"), indent=1)
print("wagon placeholders written")
