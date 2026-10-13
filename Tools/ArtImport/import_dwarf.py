#!/usr/bin/env python3
"""
Imports Astra's eight-direction dwarf (the dwarf-walk-assets package, 2026-10-13) as the supplier's animations.

The package is already clean: transparent 256 x 256 cells, one common ground origin (128, 222) for every frame and
direction, a five-frame walk at 150 ms per frame, and one standing pose per direction. So nothing is keyed, scaled or
re-anchored: each frame is cropped to its painted area (plus a margin) and its pivot set at the origin, so the feet
stay planted on the same point in every frame.

Writes Characters/Supplier/dwarf_walk_<dir>_<n>.png and dwarf_idle_<dir>.png, their pivots in sprite_meta.json, and
Characters/Supplier/animations.json with the clips walk_<dir> and idle_<dir> (dir: N NE E SE S SW W NW). The work poses
(mining, chopping, climbing, carrying) are not painted yet: the animator plays the standing pose with a procedural
swing for those.

Usage: python3 -I import_dwarf.py <extracted package folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys

from PIL import Image

DIRS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
ORIGIN = (128, 222)       # the package's common ground point, from the top-left of a cell
PPU = 240                 # the dwarf stands about 0.85 units tall: a little shorter than the tailor (1.0)
FRAME_SECONDS = 0.15
PAD = 4


def crop(src, out_path):
    im = Image.open(src).convert("RGBA")
    box = im.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
    x0, y0 = max(0, box[0] - PAD), max(0, box[1] - PAD)
    x1, y1 = min(im.width, box[2] + PAD), min(im.height, max(box[3], ORIGIN[1]) + PAD)
    im.crop((x0, y0, x1, y1)).save(out_path)
    w, h = x1 - x0, y1 - y0
    return [round((ORIGIN[0] - x0) / w, 4), round(1.0 - (ORIGIN[1] - y0) / h, 4)]


def main(src, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    out_dir = os.path.join(art, "Characters", "Supplier")
    os.makedirs(out_dir, exist_ok=True)
    data = json.load(open(os.path.join(src, "sprite-data.json")))
    assert data["origin"] == list(ORIGIN), data["origin"]
    manifest = {"ppu": PPU, "clips": {}}
    for d in DIRS:
        frames = []
        for i in range(data["frameCount"]):
            name = f"dwarf_walk_{d}_{i}.png"
            pivot = crop(os.path.join(src, "frames", d, f"{i:02d}.png"), os.path.join(out_dir, name))
            rel = f"Characters/Supplier/{name}"
            meta["sprites"][rel] = {"pivot": pivot, "ppu": PPU}
            frames.append({"sprite": rel, "dur": FRAME_SECONDS})
        manifest["clips"][f"walk_{d}"] = {"fps": round(1.0 / FRAME_SECONDS, 3), "loop": True, "frames": frames}
        name = f"dwarf_idle_{d}.png"
        pivot = crop(os.path.join(src, "standing", f"{d}.png"), os.path.join(out_dir, name))
        rel = f"Characters/Supplier/{name}"
        meta["sprites"][rel] = {"pivot": pivot, "ppu": PPU}
        manifest["clips"][f"idle_{d}"] = {"fps": 1.0, "loop": True, "frames": [{"sprite": rel, "dur": 1.0}]}
        print(d, "ok")
    json.dump(manifest, open(os.path.join(out_dir, "animations.json"), "w"), indent=1)
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
