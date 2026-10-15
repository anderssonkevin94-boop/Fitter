#!/usr/bin/env python3
"""
Placeholder shell pieces for the modular shop (until Astra's land), on the same canvases and anchors as the templates in
Docs/astra-refs/shop-modular/: full walls and low walls for both edge directions, posts, a plot sign and the placement
overlays. Writes Art/Placeholder/Shop/*.png and their pivots (the anchor) into sprite_meta.json.

"_left" pieces run along A (rising to the right: back-left walls, front-right low walls); "_right" pieces along B.

Usage: python3 -I make_shop_placeholders.py <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys
from PIL import Image, ImageDraw

ART = sys.argv[1]
OUT = os.path.join(ART, "Shop")
os.makedirs(OUT, exist_ok=True)
meta_path = os.path.join(ART, "sprite_meta.json")
meta = json.load(open(meta_path))


def save(img, name, anchor):
    img.save(os.path.join(OUT, name + ".png"))
    w, h = img.size
    meta["sprites"][f"Shop/{name}.png"] = {"pivot": [round(anchor[0] / w, 5), round(1 - anchor[1] / h, 5)], "ppu": 256}


def wall(name, height, canvas_h, right, face, top, trim=None):
    w = 176
    img = Image.new("RGBA", (w, canvas_h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ax, ay = 24, canvas_h - 16
    bx, by = 152, canvas_h - 80
    pts = lambda l: [(w - x, y) for x, y in l] if right else l
    t = (-13, -6) if not right else (-13, -6)
    d.polygon(pts([(ax, ay - height), (bx, by - height), (bx + t[0], by - height + t[1]), (ax + t[0], ay - height + t[1])]), fill=top)
    d.polygon(pts([(ax, ay), (bx, by), (bx, by - height), (ax, ay - height)]), fill=face)
    if trim:   # skirting board and a timber rail
        for lo, hi in ((0, 22), (height * 0.42, height * 0.42 + 16)):
            d.polygon(pts([(ax, ay - lo), (bx, by - lo), (bx, by - hi), (ax, ay - hi)]), fill=trim)
    anchor = ((ax + bx) // 2, (ay + by) // 2)
    save(img, name, (w - anchor[0], anchor[1]) if right else anchor)


PLASTER_L, PLASTER_R = (236, 218, 186, 255), (212, 194, 164, 255)
TIMBER, TIMBER_DARK = (166, 122, 82, 255), (128, 92, 60, 255)
wall("wall_tall_left", 563, 700, False, PLASTER_L, TIMBER_DARK, TIMBER)
wall("wall_tall_right", 563, 700, True, PLASTER_R, TIMBER_DARK, TIMBER)
wall("wall_low_left", 77, 220, False, TIMBER, (196, 156, 112, 255))
wall("wall_low_right", 77, 220, True, TIMBER_DARK, (196, 156, 112, 255))

for name, h, ch in (("post_tall", 583, 700), ("post_low", 97, 220)):
    img = Image.new("RGBA", (64, ch), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([18, ch - 16 - h, 32, ch - 16], fill=TIMBER)
    d.rectangle([32, ch - 16 - h, 46, ch - 16], fill=TIMBER_DARK)
    d.polygon([(18, ch - 16 - h), (32, ch - 22 - h), (46, ch - 16 - h), (32, ch - 10 - h)], fill=(196, 156, 112, 255))
    save(img, name, (32, ch - 16))

# Plot sign: a stake with a board.
img = Image.new("RGBA", (128, 192), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rectangle([58, 70, 70, 176], fill=TIMBER_DARK)
d.rectangle([20, 40, 108, 100], fill=(214, 176, 120, 255))
d.rectangle([20, 92, 108, 100], fill=TIMBER)
d.line([(46, 70), (82, 70)], fill=(90, 64, 40, 255), width=8)
d.line([(64, 52), (64, 88)], fill=(90, 64, 40, 255), width=8)
save(img, "plot_sign", (64, 176))

# Placement overlays: a tile diamond, green and red.
for name, col in (("tile_ok", (90, 200, 110)), ("tile_blocked", (220, 80, 70))):
    img = Image.new("RGBA", (256, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    dia = [(128, 2), (254, 64), (128, 126), (2, 64)]
    d.polygon(dia, fill=col + (110,), outline=col + (255,))
    d.line(dia + [dia[0]], fill=col + (255,), width=5)
    save(img, name, (128, 64))

# Weapon rack placeholder (until Astra's weapon_rack): the old rack art at a size that spans two wall edges.
import shutil
shutil.copyfile(os.path.join(ART, "Props", "rack_weapons_empty.png"), os.path.join(OUT, "weapon_rack_placeholder.png"))
meta["sprites"]["Shop/weapon_rack_placeholder.png"] = {"pivot": [0.5, 0.1682], "ppu": 160}

json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
print("shop placeholders written")
