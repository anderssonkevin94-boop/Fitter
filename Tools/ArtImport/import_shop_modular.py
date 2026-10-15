#!/usr/bin/env python3
"""
Imports Astra's modular shop pieces (ravenholm-shop-modular-step1 and later steps): Shell/*.png into
Art/Placeholder/Shop/ (256 px per unit, pivots from points.json; no game outline, the pieces join seamlessly) and
Furniture/*.png into Art/Placeholder/Furniture/ (with the game outline), merging their points into that folder's
points.json so setup can read ground points and slots (e.g. desk_1_B, a second facing).

Usage: python3 -I import_shop_modular.py <pack folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import shutil
import sys


def main(pack, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    pts = json.load(open(os.path.join(pack, "points.json")))
    assert pts["pixels_per_unit"] == 256
    furn_pts_path = os.path.join(art, "Furniture", "points.json")
    furn_pts = json.load(open(furn_pts_path))
    n = 0
    for name, p in sorted(pts["assets"].items()):
        pivot = [round(p["pivot"]["x"], 5), round(p["pivot"]["y"], 5)]
        if os.path.exists(os.path.join(pack, "Shell", name)):
            shutil.copyfile(os.path.join(pack, "Shell", name), os.path.join(art, "Shop", name))
            meta["sprites"]["Shop/" + name] = {"pivot": pivot, "ppu": 256}
        elif os.path.exists(os.path.join(pack, "Furniture", name)):
            shutil.copyfile(os.path.join(pack, "Furniture", name), os.path.join(art, "Furniture", name))
            meta["sprites"]["Furniture/" + name] = {"pivot": pivot, "ppu": 256, "outline": True}
            entry = dict(p)
            entry["file"] = "Furniture/" + name
            furn_pts["assets"][name] = entry
        else:
            raise SystemExit(f"no file for {name}")
        n += 1
    json.dump(furn_pts, open(furn_pts_path, "w"), indent=1)
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    print(f"{n} shop pieces imported")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
