#!/usr/bin/env python3
"""
Placeholder icons and carried-pile sprites for the crafting expansion's helmets and shields (2026-10-16), until Astra's
art: Icons/Items/icon_<key>.png (96 x 96) and Stacks/stack_<key>.png (256 x 192, 512 px per unit), pivots into
sprite_meta.json like the existing items.

Usage: python3 -I make_armoury_placeholders.py <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys
from PIL import Image, ImageDraw

ART = sys.argv[1]
meta_path = os.path.join(ART, "sprite_meta.json")
meta = json.load(open(meta_path))

HELMS = {"iron_helm": ((150, 156, 166), (96, 100, 110)), "leather_cap": ((150, 102, 66), (104, 70, 46)),
         "steel_helm": ((196, 204, 214), (120, 128, 140)), "moonsilver_circlet": ((214, 222, 240), (150, 120, 200))}
SHIELDS = {"wooden_shield": ((170, 120, 74), (110, 76, 48)), "iron_shield": ((150, 110, 70), (120, 124, 134)),
           "steel_kite_shield": ((60, 90, 150), (196, 204, 214))}


def helm(d, w, h, main, trim, circlet):
    cx, cy = w / 2, h * 0.6
    r = w * 0.34
    if circlet:
        d.ellipse((cx - r, cy - r * 0.45, cx + r, cy + r * 0.45), outline=main, width=max(3, int(w * 0.07)))
        d.ellipse((cx - w * 0.07, cy - r * 0.6, cx + w * 0.07, cy - r * 0.3), fill=trim)
        return
    d.pieslice((cx - r, cy - r, cx + r, cy + r), 180, 360, fill=main)
    d.rectangle((cx - r, cy - 2, cx + r, cy + h * 0.08), fill=trim)
    d.rectangle((cx - w * 0.03, cy - r * 0.2, cx + w * 0.03, cy + h * 0.16), fill=trim)


def shield(d, w, h, main, trim, kite):
    cx = w / 2
    top, bottom = h * 0.12, h * 0.9
    half = w * 0.3
    if kite:
        pts = [(cx - half, top), (cx + half, top), (cx + half, h * 0.5), (cx, bottom), (cx - half, h * 0.5)]
    else:
        pts = [(cx - half, top), (cx + half, top), (cx + half, h * 0.62), (cx, bottom), (cx - half, h * 0.62)]
    d.polygon(pts, fill=main, outline=trim)
    d.line([(cx, top), (cx, bottom)], fill=trim, width=max(2, int(w * 0.05)))
    d.line([(cx - half, h * 0.4), (cx + half, h * 0.4)], fill=trim, width=max(2, int(w * 0.05)))


for key, (main, trim) in list(HELMS.items()) + list(SHIELDS.items()):
    for kind, size, ppu, pivot in (("Icons/Items/icon_", (96, 96), None, [0.5, 0.5]), ("Stacks/stack_", (256, 192), 512, [0.5, 0.0833333])):
        img = Image.new("RGBA", size, (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        w, h = size if kind.startswith("Icons") else (192, 160)
        if not kind.startswith("Icons"):
            sub = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            dd = ImageDraw.Draw(sub)
        else:
            sub, dd = img, d
        if key in HELMS:
            helm(dd, w, h, main, trim, key.endswith("circlet"))
        else:
            shield(dd, w, h, main, trim, "kite" in key)
        if not kind.startswith("Icons"):
            img.alpha_composite(sub, ((256 - w) // 2, 192 - h - 12))
        rel = f"{kind}{key}.png"
        img.save(os.path.join(ART, rel))
        entry = {"pivot": pivot}
        if ppu:
            entry["ppu"] = ppu
        meta["sprites"][rel] = entry
json.dump(meta, open(meta_path, "w"), indent=1)
print("armoury placeholders written")
