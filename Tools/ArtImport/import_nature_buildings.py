#!/usr/bin/env python3
"""
Imports Astra's v4 pack (ravenholm-nature-buildings-v4, 2026-10-15) over the existing sprites, keeping their Unity
.meta files (GUIDs): the recoloured Ground/ and Blocks/ (as import_world_tiles.py), Nature/ over Painted/Nature/ and the
game's own nature copies (Nature/tree_cedar etc., the mapping of import_painted_sheets.py), and Buildings/. All at
256 px per unit with the pivots from points.json; nature and buildings get the game's outline ("outline": true).

Usage: python3 -I import_nature_buildings.py <pack folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import shutil
import sys

GAME_NATURE = [("tree_cedar", "pine_tall"), ("tree_maple", "leafy_tree"), ("tree_ironwood", "pine_bent"),
               ("tree_stump", "stump"), ("bush_herb", "bush_herb"), ("rock_stone", "boulder")]
TOWN = [("grass_a", "grass_plain"), ("grass_b", "grass_flowers"), ("grass_c", "grass_tufts"),
        ("forest_floor", "forest_grass"), ("forest_floor_b", "soil_mossy"),
        ("path", "path_plain"), ("path_b", "path_pebbles"), ("path_c", "path_footprints"),
        ("dirt", "dirt_dug"), ("plaza", "cobble")]


def main(pack, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    pts = json.load(open(os.path.join(pack, "points.json")))["assets"]

    def put(src_rel, dst_rel, key, outline):
        dst = os.path.join(art, dst_rel)
        if not os.path.exists(dst):
            raise SystemExit(f"no existing sprite to replace: {dst}")
        shutil.copyfile(os.path.join(pack, src_rel), dst)
        p = pts[key]
        assert p["pixels_per_unit"] == 256, key
        entry = {"pivot": [round(v, 5) for v in p["pivot_normalized_bottom_left"]], "ppu": 256}
        if outline:
            entry["outline"] = True
        meta["sprites"][dst_rel] = entry

    n = 0
    for folder in ("Ground", "Blocks"):
        for f in sorted(os.listdir(os.path.join(pack, folder))):
            if f.endswith(".png"):
                put(f"{folder}/{f}", f"Painted/{folder}/{f}", f[:-4], False); n += 1
    for key, name in TOWN:
        put(f"Ground/ground_{name}.png", f"Tiles/tile_{key}.png", f"ground_{name}", False); n += 1
    for f in sorted(os.listdir(os.path.join(pack, "Nature"))):
        if f.endswith(".png") and os.path.exists(os.path.join(art, "Painted/Nature", f)):   # bush_herb lives only in Nature/
            put(f"Nature/{f}", f"Painted/Nature/{f}", f[:-4], True); n += 1
    for key, name in GAME_NATURE:
        put(f"Nature/{name}.png", f"Nature/{key}.png", name, True); n += 1
    for f in sorted(os.listdir(os.path.join(pack, "Buildings"))):
        if f.endswith(".png"):
            put(f"Buildings/{f}", f"Buildings/{f}", f[:-4], True); n += 1
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    print(f"{n} sprites replaced")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
