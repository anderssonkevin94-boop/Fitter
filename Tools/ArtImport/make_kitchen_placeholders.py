#!/usr/bin/env python3
"""
Placeholder art for the crafting expansion's Kitchen (2026-10-16), until Astra's buildings and items:
- Buildings/bld_kitchen.png: the Apothecary's building recoloured (a warm red roof), same canvas and pivot.
- Icons and carried-pile sprites for berries and mushrooms (Resources) and the three dishes and two light armours (Items).

Usage: python3 -I make_kitchen_placeholders.py <Assets/Ravenholm/Art/Placeholder>
"""
import colorsys
import json
import os
import sys
from PIL import Image, ImageDraw

ART = sys.argv[1]
meta_path = os.path.join(ART, "sprite_meta.json")
meta = json.load(open(meta_path))

# The Kitchen: the Apothecary's building with its greens and blues turned to warm reds and browns.
src = Image.open(os.path.join(ART, "Buildings/bld_apothecary.png")).convert("RGBA")
px = src.load()
for y in range(src.height):
    for x in range(src.width):
        r, g, b, a = px[x, y]
        if a == 0:
            continue
        h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        if s > 0.25 and 0.2 < h < 0.75:   # greens, teals and blues: the roof and trim
            h = 0.02
            s = min(1, s * 1.1)
        r2, g2, b2 = colorsys.hsv_to_rgb(h, s, v)
        px[x, y] = (int(r2 * 255), int(g2 * 255), int(b2 * 255), a)
src.save(os.path.join(ART, "Buildings/bld_kitchen.png"))
meta["sprites"]["Buildings/bld_kitchen.png"] = dict(meta["sprites"]["Buildings/bld_apothecary.png"])


def berries(d, w, h):
    for cx, cy in ((0.4, 0.55), (0.6, 0.5), (0.5, 0.68), (0.32, 0.72), (0.68, 0.7)):
        r = w * 0.12
        d.ellipse((w * cx - r, h * cy - r, w * cx + r, h * cy + r), fill=(150, 40, 80), outline=(90, 20, 50), width=2)
    d.polygon([(w * 0.45, h * 0.32), (w * 0.62, h * 0.22), (w * 0.58, h * 0.4)], fill=(70, 130, 60))


def mushroom(d, w, h):
    d.rectangle((w * 0.43, h * 0.5, w * 0.57, h * 0.85), fill=(236, 226, 200))
    d.pieslice((w * 0.18, h * 0.22, w * 0.82, h * 0.78), 180, 360, fill=(170, 90, 50))
    for cx in (0.35, 0.55, 0.68):
        d.ellipse((w * cx - 4, h * 0.38 - 4, w * cx + 4, h * 0.38 + 4), fill=(240, 226, 200))


def rations(d, w, h):
    d.rectangle((w * 0.2, h * 0.4, w * 0.8, h * 0.8), fill=(196, 160, 116), outline=(120, 84, 54), width=3)
    d.line([(w * 0.2, h * 0.55), (w * 0.8, h * 0.55)], fill=(120, 84, 54), width=3)
    berries(d, w * 0.6, h * 0.55)


def bowl(d, w, h, soup):
    d.pieslice((w * 0.15, h * 0.25, w * 0.85, h * 0.85), 0, 180, fill=(150, 102, 66))
    d.ellipse((w * 0.15, h * 0.45, w * 0.85, h * 0.62), fill=soup)


def pie(d, w, h):
    d.ellipse((w * 0.12, h * 0.45, w * 0.88, h * 0.85), fill=(200, 140, 70), outline=(140, 90, 40), width=3)
    for k in range(3):
        d.line([(w * (0.3 + k * 0.18), h * 0.55), (w * (0.36 + k * 0.18), h * 0.72)], fill=(140, 90, 40), width=3)


def jerkin(d, w, h, main, trim):
    d.polygon([(w * 0.3, h * 0.2), (w * 0.7, h * 0.2), (w * 0.82, h * 0.35), (w * 0.72, h * 0.85), (w * 0.28, h * 0.85), (w * 0.18, h * 0.35)], fill=main, outline=trim)
    d.line([(w * 0.5, h * 0.25), (w * 0.5, h * 0.85)], fill=trim, width=3)


ART_ITEMS = {
    ("Resources", "berries"): lambda d, w, h: berries(d, w, h),
    ("Resources", "mushroom"): lambda d, w, h: mushroom(d, w, h),
    ("Items", "trail_rations"): lambda d, w, h: rations(d, w, h),
    ("Items", "mushroom_stew"): lambda d, w, h: bowl(d, w, h, (170, 110, 60)),
    ("Items", "hearty_pie"): lambda d, w, h: pie(d, w, h),
    ("Items", "leather_jerkin"): lambda d, w, h: jerkin(d, w, h, (150, 102, 66), (90, 62, 40)),
    ("Items", "padded_gambeson"): lambda d, w, h: jerkin(d, w, h, (214, 200, 168), (150, 130, 100)),
}
for (kind, key), draw in ART_ITEMS.items():
    icon = Image.new("RGBA", (96, 96), (0, 0, 0, 0))
    draw(ImageDraw.Draw(icon), 96, 96)
    rel = f"Icons/{kind}/icon_{key}.png"
    icon.save(os.path.join(ART, rel))
    meta["sprites"][rel] = {"pivot": [0.5, 0.5]}
    stack = Image.new("RGBA", (256, 192), (0, 0, 0, 0))
    sub = Image.new("RGBA", (176, 150), (0, 0, 0, 0))
    draw(ImageDraw.Draw(sub), 176, 150)
    stack.alpha_composite(sub, (40, 30))
    rel = f"Stacks/stack_{key}.png"
    stack.save(os.path.join(ART, rel))
    meta["sprites"][rel] = {"pivot": [0.5, 0.0833333], "ppu": 512}
json.dump(meta, open(meta_path, "w"), indent=1)
print("kitchen placeholders written")
