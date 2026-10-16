"""Imports Astra's switchback pass pack (2026-10-16): 24 leg/turn paintings (six regions), the valley floor and the castle.

Usage: python -I import_switchback_pass.py <pack dir (holding points.json)> <Assets/Ravenholm/Art/Placeholder>

The PNGs are copied unchanged into Pass/. Pivots come from points.json, never from the images' bounds: a leg's start
anchor, a turn's entry anchor, the castle's gate anchor, the valley's centre. All at 256 px per unit.
"""
import json
import os
import shutil
import sys


def main(src, art):
    pts = json.load(open(os.path.join(src, "points.json")))
    assert pts["pixels_per_unit"] == 256
    dst = os.path.join(art, "Pass")
    os.makedirs(dst, exist_ok=True)
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    for name, a in pts["assets"].items():
        shutil.copyfile(os.path.join(src, "assets", name), os.path.join(dst, name))
        w, h = a["size"]
        if a["kind"] == "leg":
            px, py = a["start"]
        elif a["kind"] == "hairpin":
            px, py = a["entry"]
        elif a["kind"] == "castle":
            px, py = a["gate_anchor"]
        else:
            px, py = w / 2, h / 2
        meta["sprites"]["Pass/" + name] = {"pivot": [px / w, 1 - py / h], "ppu": 256}
    shutil.copyfile(os.path.join(src, "points.json"), os.path.join(dst, "points.json"))
    shutil.copyfile(os.path.join(src, "README.txt"), os.path.join(dst, "README.txt"))
    open(meta_path, "w").write(json.dumps(meta, indent=1))   # the file's own format
    print("imported", len(pts["assets"]), "pass sprites into", dst)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
