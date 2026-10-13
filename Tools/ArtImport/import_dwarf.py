#!/usr/bin/env python3
"""
Imports Astra's eight-direction dwarf as the supplier's animations (v2, 2026-10-13: the longer stride with a gentle
body sway and arm swing; ArtSource/dwarf-v2-swing).

The package is already clean: dwarf-walk.png is five frames (columns) by eight directions (rows N NE E SE S SW W NW)
of 256 x 256 cells, dwarf-idle.png one standing pose per direction (columns, same order), all on one ground origin
(128, 222). So nothing is keyed, scaled or re-anchored: each cell is cropped to its painted area (plus a margin) and its
pivot set at the origin, so the feet stay planted on the same point in every frame. The frame time comes from
sprite-data.json.

sprite-data.json also gives each direction's screen velocity (px/s at the painted timing) that keeps the feet planted;
it is written per walk clip in units/s (px / PPU, y up), so the game moves the dwarf at exactly that speed times its
playback rate.

Writes Characters/Supplier/dwarf_walk_<dir>_<n>.png and dwarf_idle_<dir>.png, their pivots in sprite_meta.json, and
Characters/Supplier/animations.json with the clips walk_<dir> and idle_<dir>. The work poses (mining, chopping,
climbing, carrying) are not painted yet: the animator plays the standing pose with a procedural swing for those.

Usage: python3 -I import_dwarf.py <package folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys

from PIL import Image

ORIGIN = (128, 222)       # the package's common ground point, from the top-left of a cell
PPU = 240                 # the dwarf stands about 0.85 units tall: a little shorter than the tailor (1.0)
PAD = 4


def crop(cell, out_path):
    box = cell.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
    x0, y0 = max(0, box[0] - PAD), max(0, box[1] - PAD)
    x1, y1 = min(cell.width, box[2] + PAD), min(cell.height, max(box[3], ORIGIN[1]) + PAD)
    cell.crop((x0, y0, x1, y1)).save(out_path)
    w, h = x1 - x0, y1 - y0
    return [round((ORIGIN[0] - x0) / w, 4), round(1.0 - (ORIGIN[1] - y0) / h, 4)]


def main(src, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    out_dir = os.path.join(art, "Characters", "Supplier")
    os.makedirs(out_dir, exist_ok=True)
    data = json.load(open(os.path.join(src, "sprite-data.json")))
    assert data["origin"] == list(ORIGIN), data["origin"]
    size = data["frameSize"][0]
    seconds = data["frameDurationMs"] / 1000.0
    walk = Image.open(os.path.join(src, "dwarf-walk.png")).convert("RGBA")
    idle = Image.open(os.path.join(src, "dwarf-idle.png")).convert("RGBA")
    manifest = {"ppu": PPU, "clips": {}}
    for d in data["directionOrder"]:
        a = data["animations"][d]
        row = a["row"]
        frames = []
        for i in range(data["frameCount"]):
            cell = walk.crop((i * size, row * size, (i + 1) * size, (row + 1) * size))
            name = f"dwarf_walk_{d}_{i}.png"
            rel = f"Characters/Supplier/{name}"
            meta["sprites"][rel] = {"pivot": crop(cell, os.path.join(out_dir, name)), "ppu": PPU}
            frames.append({"sprite": rel, "dur": seconds})
        vx, vy = a["screenVelocityPerSecond"]                       # px/s, image y down
        manifest["clips"][f"walk_{d}"] = {"fps": round(1.0 / seconds, 3), "loop": True, "frames": frames,
                                          "velocity": [round(vx / PPU, 5), round(-vy / PPU, 5)]}
        cell = idle.crop((row * size, 0, (row + 1) * size, size))
        name = f"dwarf_idle_{d}.png"
        rel = f"Characters/Supplier/{name}"
        meta["sprites"][rel] = {"pivot": crop(cell, os.path.join(out_dir, name)), "ppu": PPU}
        manifest["clips"][f"idle_{d}"] = {"fps": 1.0, "loop": True, "frames": [{"sprite": rel, "dur": 1.0}]}
        print(d, "velocity", manifest["clips"][f"walk_{d}"]["velocity"])
    json.dump(manifest, open(os.path.join(out_dir, "animations.json"), "w"), indent=1)
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
