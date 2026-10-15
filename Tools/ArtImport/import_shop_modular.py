#!/usr/bin/env python3
"""
Imports Astra's modular shop pieces (ravenholm-shop-modular-step1 and later steps): Shell/*.png into
Art/Placeholder/Shop/ (256 px per unit, pivots from points.json; no game outline, the pieces join seamlessly) and
Furniture/*.png into Art/Placeholder/Furniture/ (with the game outline), merging their points into that folder's
points.json so setup can read ground points and slots (e.g. desk_1_B, a second facing).

Full walls were drawn 563 px tall (2.2 units); Kevin had them made shorter (1.4 units, 358 px, 2026-10-15). Pieces
drawn at the old height (700 px canvases) are shortened here by cutting rows of plain, vertically uniform plaster
(above and below a window where there is one), keeping the cap, the base and the anchor's distance from the bottom.

Usage: python3 -I import_shop_modular.py <pack folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import shutil
import sys
from PIL import Image

OLD_CANVAS = 700
CUT = 563 - 358   # rows to remove from a full-height piece


def shorten(path, anchor_y):
    """Removes CUT rows of vertically uniform pixels (whole rows identical to their neighbours). Returns the new anchor y."""
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    clear = [0 < y < h - 1 and all(px[x, y] == px[x, y - 1] == px[x, y + 1] for x in range(w)) for y in range(h)]
    runs, start = [], None
    for y in range(h):
        if clear[y] and start is None:
            start = y
        if not clear[y] and start is not None:
            runs.append((start, y - 1))
            start = None
    cut, need = set(), CUT
    for a, b in sorted(runs, key=lambda r: r[1] - r[0], reverse=True):
        take = min(need, b - a + 1)
        cut.update(range(a, a + take))
        need -= take
        if need == 0:
            break
    if need:
        raise SystemExit(f"{path}: not enough plain rows to shorten by {CUT}")
    if max(cut) >= anchor_y:
        raise SystemExit(f"{path}: a cut would move the anchor")
    out = Image.new("RGBA", (w, h - CUT), (0, 0, 0, 0))
    y2 = 0
    for y in range(h):
        if y in cut:
            continue
        out.paste(im.crop((0, y, w, y + 1)), (0, y2))
        y2 += 1
    out.save(path)
    return anchor_y - CUT


def main(pack, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    pts = json.load(open(os.path.join(pack, "points.json")))
    assert pts["pixels_per_unit"] == 256
    furn_pts_path = os.path.join(art, "Furniture", "points.json")
    furn_pts = json.load(open(furn_pts_path))
    items_dir = os.path.join(art, "Items")
    os.makedirs(items_dir, exist_ok=True)
    item_pts_path = os.path.join(items_dir, "points.json")
    item_pts = json.load(open(item_pts_path)) if os.path.exists(item_pts_path) else {"pixels_per_unit": 256, "assets": {}}
    n = 0
    for name, p in sorted(pts["assets"].items()):
        pivot = [round(p["pivot"]["x"], 5), round(p["pivot"]["y"], 5)]
        # Step 2 (2026-10-15): build-mode UI (placement overlays to Shop/, icons to UI/Icons/) and item display views.
        if os.path.exists(os.path.join(pack, "UI", name)):
            sub = "Shop/" if name.startswith("tile_") else "UI/Icons/"
            shutil.copyfile(os.path.join(pack, "UI", name), os.path.join(art, sub + name))
            meta["sprites"][sub + name] = {"pivot": pivot, "ppu": 256} if sub == "Shop/" else {"pivot": pivot}
            n += 1
            continue
        if os.path.exists(os.path.join(pack, "Items", name)):
            shutil.copyfile(os.path.join(pack, "Items", name), os.path.join(items_dir, name))
            meta["sprites"]["Items/" + name] = {"pivot": pivot, "ppu": 256, "outline": True}
            entry = dict(p)
            entry["file"] = "Items/" + name
            item_pts["assets"][name] = entry
            n += 1
            continue
        if os.path.exists(os.path.join(pack, "Shell", name)):
            dst = os.path.join(art, "Shop", name)
            shutil.copyfile(os.path.join(pack, "Shell", name), dst)
            if p["canvas"]["h"] == OLD_CANVAS:   # a full-height piece drawn at the old wall height
                ay = shorten(dst, p["anchor"]["y"])
                h = OLD_CANVAS - CUT
                pivot = [round(p["anchor"]["x"] / p["canvas"]["w"], 5), round(1 - ay / h, 5)]
            meta["sprites"]["Shop/" + name] = {"pivot": pivot, "ppu": 256}
        elif os.path.exists(os.path.join(pack, "Furniture", name)) or "alias_of" in p or p.get("file", "").startswith("Furniture/"):
            src = os.path.join(pack, p.get("file", "Furniture/" + name))
            shutil.copyfile(src, os.path.join(art, "Furniture", name))
            meta["sprites"]["Furniture/" + name] = {"pivot": pivot, "ppu": 256, "outline": True}
            entry = dict(p)
            entry["file"] = "Furniture/" + name
            furn_pts["assets"][name] = entry
        else:
            raise SystemExit(f"no file for {name}")
        n += 1
    json.dump(furn_pts, open(furn_pts_path, "w"), indent=1)
    json.dump(item_pts, open(item_pts_path, "w"), indent=1)
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    print(f"{n} shop pieces imported")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
