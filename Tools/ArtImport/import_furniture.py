#!/usr/bin/env python3
"""
Imports Astra's shop furniture pack (ravenholm-shop-furniture-native-v2, 2026-10-15): the 12 sprites in Furniture/
(armor_stand, potion_shelf, display_table, desk; levels 1-3) into Art/Placeholder/Furniture/, at 256 px per unit with
the pivots from points.json and the game's outline. points.json is copied alongside: setup reads the ground points and
item-slot boxes from it into the furniture configs (ProjectSetup.EnsureShopFurniture).

Usage: python3 -I import_furniture.py <pack folder> <Assets/Ravenholm/Art/Placeholder>
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
    out = os.path.join(art, "Furniture")
    os.makedirs(out, exist_ok=True)
    n = 0
    for name, p in sorted(pts["assets"].items()):
        shutil.copyfile(os.path.join(pack, p["file"]), os.path.join(out, name))
        meta["sprites"]["Furniture/" + name] = {"pivot": [round(p["pivot"]["x"], 5), round(p["pivot"]["y"], 5)], "ppu": 256, "outline": True}
        n += 1
    shutil.copyfile(os.path.join(pack, "points.json"), os.path.join(out, "points.json"))
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    print(f"{n} furniture sprites imported")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
