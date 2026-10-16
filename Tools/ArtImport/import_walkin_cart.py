"""Imports Astra's approved walk-in wagon (2026-10-16, the "wagon-walk-in" pack): seven registered layers on one
1536 x 1024 canvas with a shared pivot (779, 826), and the pack's geometry (points.json, assembly.json).

Usage: python -I import_walkin_cart.py <pack dir (holding points.json)> <Assets/Ravenholm/Art/Placeholder>

The layers are copied unchanged: no trimming, recentring or resizing. cart_assembled.png is a reference only and is not
imported. One file is derived: cart_silhouette.png, the union of the seven layers' alpha in white, so the game's outline
can be drawn once around the whole wagon (an outline per layer would draw false seams at the internal cuts).
"""
import json
import os
import shutil
import sys

from PIL import Image, ImageChops

LAYERS = ["cart_back", "cart_shafts", "cart_steps", "wheel_near_left", "wheel_near_right", "cart_front", "cart_roof"]
PIVOT = [779 / 1536, 1 - 826 / 1024]


def main(src, art):
    points = json.load(open(os.path.join(src, "points.json")))
    assert points["canvas"] == [1536, 1024] and points["ground_origin"] == [779, 826], "unexpected pack geometry"
    dst = os.path.join(art, "Wagon/WalkIn")
    os.makedirs(dst, exist_ok=True)
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    union = Image.new("L", (1536, 1024), 0)
    for name in LAYERS:
        im = Image.open(os.path.join(src, "Sprites", name + ".png")).convert("RGBA")
        assert im.size == (1536, 1024), name
        shutil.copyfile(os.path.join(src, "Sprites", name + ".png"), os.path.join(dst, name + ".png"))
        union = ImageChops.lighter(union, im.split()[3])
        meta["sprites"][f"Wagon/WalkIn/{name}.png"] = {"pivot": PIVOT, "ppu": 256}
    silhouette = Image.merge("RGBA", (Image.new("L", union.size, 255),) * 3 + (union,))
    silhouette.save(os.path.join(dst, "cart_silhouette.png"))
    meta["sprites"]["Wagon/WalkIn/cart_silhouette.png"] = {"pivot": PIVOT, "ppu": 256}
    for name in ("points.json", "assembly.json", "README.txt"):
        shutil.copyfile(os.path.join(src, name), os.path.join(dst, name))
    open(meta_path, "w").write(json.dumps(meta, indent=1))   # the file's own format: 1-space indent, no final newline
    print("imported", len(LAYERS), "layers, the silhouette and the geometry into", dst)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
