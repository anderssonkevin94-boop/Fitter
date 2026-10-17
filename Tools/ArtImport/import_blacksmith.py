"""Astra's modular caravan Blacksmith (2026-10-17): the rug, the furnace and the crafting station at three levels each,
their fire overlays, the delivery and pickup mats, and the packed kit.

Usage: python -I import_blacksmith.py <pack dir (holding points.json, assets/)> <Assets/Ravenholm/Art/Placeholder>

Copies the 16 production PNGs into Blacksmith/ untrimmed (Full Rect), 256 px per unit, each with the pack's pivot (its
ground point). The stations and the kit use the game's outline; the rug, the mats and the fire overlays don't (the rug
and mats lie flat on the ground; the fires sit on their station). points.json is kept beside them.
"""
import json
import os
import shutil
import sys

PPU = 256
OUTLINED = ("furnace_1", "furnace_2", "furnace_3", "crafting_1", "crafting_2", "crafting_3", "blacksmith_kit")


def main(src, art):
    pts = json.load(open(os.path.join(src, "points.json")))
    dst = os.path.join(art, "Blacksmith")
    os.makedirs(dst, exist_ok=True)
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))

    def pivot_of(name):
        a = pts["assets"].get(name)
        if a and "unity_pivot" in a:
            return [round(a["unity_pivot"][0], 5), round(a["unity_pivot"][1], 5)]
        if name.endswith("_glow"):
            return pivot_of(name[:-len("_glow")])
        if name == "blacksmith_rug":
            return [0.5, 0.3]
        if name.endswith("_mat"):
            return [0.5, 0.375]
        return [0.5, 0.21875]   # the stations' and the kit's 512 canvas, ground (256, 400)

    n = 0
    for f in sorted(os.listdir(os.path.join(src, "assets"))):
        if not f.endswith(".png"):
            continue
        name = f[:-4]
        shutil.copyfile(os.path.join(src, "assets", f), os.path.join(dst, f))
        entry = {"pivot": pivot_of(name), "ppu": PPU}
        if name in OUTLINED:
            entry["outline"] = True
        meta["sprites"]["Blacksmith/" + f] = entry
        n += 1
    shutil.copyfile(os.path.join(src, "points.json"), os.path.join(dst, "points.json"))
    open(meta_path, "w").write(json.dumps(meta, indent=1))
    print(f"imported {n} sprites")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
