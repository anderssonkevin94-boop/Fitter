"""Astra's four item views v1 (2026-10-17): adds each item's new front and side views beside its A and B.

Usage: python -I import_item_views.py <pack dir (holding views.json, points.json)> <Assets/Ravenholm/Art/Placeholder>

A and B are byte-for-byte the views already in Items/ (checked, never overwritten). front and side are copied into Items/
with the same import as A and B: 256 px per unit, pivot on the display anchor (128, 228) of the 256 canvas, the game's
outline. Their entries in points.json (bounds, anchors, grip, kind) are merged into Items/points.json; views.json is
kept beside them.
"""
import hashlib
import json
import os
import shutil
import sys

PPU = 256


def digest(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def main(src, art):
    views = json.load(open(os.path.join(src, "views.json")))
    pts = json.load(open(os.path.join(src, "points.json")))
    items_dir = os.path.join(art, "Items")
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    live_pts_path = os.path.join(items_dir, "points.json")
    live_pts = json.load(open(live_pts_path))
    added = 0
    for key, v in sorted(views["items"].items()):
        for view in ("A", "B"):
            name = os.path.basename(v[view])
            live = os.path.join(items_dir, name)
            if os.path.exists(live) and digest(live) != digest(os.path.join(src, v[view])):
                # The armour pack (2026-10-16) replaced armour's A with its armour-stand layer: the live one stays.
                print(f"note: {name} differs from the live one (kept as it is)")
        for view in ("front", "side"):
            name = os.path.basename(v[view])
            shutil.copyfile(os.path.join(src, v[view]), os.path.join(items_dir, name))
            a = pts["assets"][name]
            pivot = [round(a["anchor"]["x"] / a["canvas"]["w"], 5), round(1 - a["anchor"]["y"] / a["canvas"]["h"], 5)]
            meta["sprites"]["Items/" + name] = {"outline": True, "pivot": pivot, "ppu": PPU}
            live_pts["assets"][name] = a
            added += 1
    # Which way each item's A view runs (2026-10-17): A and B are named for the furniture facing, not the item's lie, so
    # some items' long side runs up to the right in A, others up to the left. Measured from the opaque pixels' principal
    # axis; the carried pile picks the view that lies across the carrier's way.
    from PIL import Image
    for key, v in views["items"].items():
        im = Image.open(os.path.join(items_dir, os.path.basename(v["A"]))).convert("RGBA")
        px = im.load()
        pts = [(x, -y) for y in range(im.height) for x in range(im.width) if px[x, y][3] > 128]
        n = len(pts)
        mx = sum(p[0] for p in pts) / n
        my = sum(p[1] for p in pts) / n
        sxx = sum((p[0] - mx) ** 2 for p in pts) / n
        syy = sum((p[1] - my) ** 2 for p in pts) / n
        sxy = sum((p[0] - mx) * (p[1] - my) for p in pts) / n
        v["a_runs_up_right"] = sxy > 0   # covariance of x and up: positive when the long side climbs to the right
        v["a_elongation"] = round(max(sxx, syy) / max(1e-6, min(sxx, syy)), 2)
    json.dump(views, open(os.path.join(items_dir, "views.json"), "w"), indent=2)
    open(meta_path, "w").write(json.dumps(meta, indent=1))
    open(live_pts_path, "w").write(json.dumps(live_pts, indent=2))
    print(f"imported {added} views for {len(views['items'])} items")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
