"""Imports Astra's approved ox, eight-direction walk V12 (2026-10-16): the assembled standing pose and six walk frames per
direction, each on its full 1280 x 1280 canvas.

Usage: python -I import_ox_v12.py <pack dir (holding points.json)> <Assets/Ravenholm/Art/Placeholder>

Every sprite's pivot is its direction's ground anchor (points.json directions[d].ground, top-left pixels), so turning
never shifts the ox. One pixels-per-unit for every direction; no per-direction scaling (V12's corrections are baked in).
E, NE and SE are the pack's own mirrors: copied as they are, never mirrored again.
"""
import json
import os
import shutil
import sys

PPU = 720        # the ox's side view about 1.6 units long, as tall as the supplier
DIRS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]


def main(src, art):
    pts = json.load(open(os.path.join(src, "points.json")))
    w, h = pts["canvas"]
    dst = os.path.join(art, "Ox")
    os.makedirs(dst, exist_ok=True)
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    n = 0
    for d in DIRS:
        gx, gy = pts["directions"][d]["ground"]
        pivot = [gx / w, 1 - gy / h]
        files = [(os.path.join(src, "Standing", f"ox_{d}.png"), f"ox_{d}_stand.png")]
        for k in range(1, 7):
            files.append((os.path.join(src, "WalkFrames", d, f"ox_walk_{k:02d}.png"), f"ox_{d}_walk_{k}.png"))
        for path, name in files:
            shutil.copyfile(path, os.path.join(dst, name))
            meta["sprites"]["Ox/" + name] = {"outline": True, "pivot": pivot, "ppu": PPU}
            n += 1
    for name in ("points.json", "animation.json", "READ_ME_FOR_CLAUDE.txt"):
        shutil.copyfile(os.path.join(src, name), os.path.join(dst, name))
    open(meta_path, "w").write(json.dumps(meta, indent=1))   # the file's own format
    print("imported", n, "ox sprites into", dst)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
