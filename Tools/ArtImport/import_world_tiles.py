#!/usr/bin/env python3
"""
Imports Astra's ground tiles and blocks (ravenholm-world-tiles-v3, 2026-10-14) over the painted ones, keeping their
Unity .meta files (GUIDs): Painted/Blocks/block_*.png (256 x 260) and Painted/Ground/ground_*.png (256 x 152), plus the
town's tile copies (Tiles/tile_<key>.png, the same mapping as import_painted_sheets.py). All at 256 px per unit with the
pivot at image (128, 64.5) - the top face's centre, the same world size and anchor as before; no game outline.

Usage: python3 -I import_world_tiles.py <pack folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import shutil
import sys

TOWN = [("grass_a", "grass_plain"), ("grass_b", "grass_flowers"), ("grass_c", "grass_tufts"),
        ("forest_floor", "forest_grass"), ("forest_floor_b", "soil_mossy"),
        ("path", "path_plain"), ("path_b", "path_pebbles"), ("path_c", "path_footprints"),
        ("dirt", "dirt_dug"), ("plaza", "cobble")]


def main(pack, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    block_pivot = [0.5, round(1 - 64.5 / 260, 5)]
    ground_pivot = [0.5, round(1 - 64.5 / 152, 5)]
    n = 0
    for folder, pivot in (("Blocks", block_pivot), ("Ground", ground_pivot)):
        for f in sorted(os.listdir(os.path.join(pack, folder))):
            if not f.endswith(".png"):
                continue
            dst = os.path.join(art, "Painted", folder, f)
            if not os.path.exists(dst):
                raise SystemExit(f"no existing sprite to replace: {dst}")
            shutil.copyfile(os.path.join(pack, folder, f), dst)
            meta["sprites"][f"Painted/{folder}/{f}"] = {"pivot": pivot, "ppu": 256}
            n += 1
    for key, name in TOWN:
        shutil.copyfile(os.path.join(pack, "Ground", f"ground_{name}.png"), os.path.join(art, "Tiles", f"tile_{key}.png"))
        meta["sprites"][f"Tiles/tile_{key}.png"] = {"pivot": ground_pivot, "ppu": 256}
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    print(f"{n} painted sprites and {len(TOWN)} town tiles replaced")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
