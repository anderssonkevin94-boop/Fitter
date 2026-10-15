#!/usr/bin/env python3
"""
Imports Astra's valley view (ravenholm-view-v2, 2026-10-15) into Art/Placeholder/View/: the five horizontally repeating
parallax layers, the castle, the cliff lip pieces and the valley traveller, at 256 px per unit (the composition is drawn
at the town's scale) with her pivots, no game outline. For each layer it also writes a "<layer>_fill.png": 4 rows of the
layer's bottom row, which the game stretches downward so the layer never ends in a gap ("extend the final pixel row").
parallax.json and points.json are copied alongside for setup.

Usage: python3 -I import_view.py <pack folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import shutil
import sys
from PIL import Image


def main(pack, art):
    out = os.path.join(art, "View")
    os.makedirs(out, exist_ok=True)
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    pts = json.load(open(os.path.join(pack, "points.json")))["assets"]
    par = json.load(open(os.path.join(pack, "parallax.json")))
    n = 0
    for key, p in pts.items():
        name = p["file"]
        shutil.copyfile(os.path.join(pack, name), os.path.join(out, name))
        meta["sprites"]["View/" + name] = {"pivot": [round(v, 5) for v in p["pivot_normalized_bottom_left"]], "ppu": 256}
        n += 1
    for layer in par["layers"]:
        name = layer["file"]
        im = Image.open(os.path.join(pack, name)).convert("RGBA")
        w, h = im.size
        row = im.crop((0, h - 1, w, h))
        fill = Image.new("RGBA", (w, 4))
        for y in range(4):
            fill.paste(row, (0, y))
        if layer["order"] == 0:   # the sky also extends UP from its top row, so the screen above it is never empty
            top = Image.new("RGBA", (w, 4))
            for y in range(4):
                top.paste(im.crop((0, 0, w, 1)), (0, y))
            top_name = name.replace(".png", "_top.png")
            top.save(os.path.join(out, top_name))
            meta["sprites"]["View/" + top_name] = {"pivot": [0, 0], "ppu": 256}
            n += 1
        fill_name = name.replace(".png", "_fill.png")
        fill.save(os.path.join(out, fill_name))
        meta["sprites"]["View/" + fill_name] = {"pivot": [0, 1], "ppu": 256}
        n += 1
    for f in ("parallax.json", "points.json"):
        shutil.copyfile(os.path.join(pack, f), os.path.join(out, f))
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    print(f"{n} view sprites imported")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
