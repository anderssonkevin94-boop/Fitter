#!/usr/bin/env python3
"""
Cuts the painted sprite sheets (Docs/ART_PROMPTS_ASTRA.md) into individual game sprites.

Each sheet is a grid of objects on flat magenta. For every object: key out the magenta (soft edge, colour spill
removed), crop, scale to the game's grid (128 px per tile width) and record its pivot in sprite_meta.json.

  ground tiles   -> 128 px wide; the top diamond is masked to exactly 128 x 64 (no outline, so tiles join), the
                    earthy edge below is kept; pivot = diamond centre.
  cliff blocks   -> 128 px wide isometric half-block cubes; pivot = centre of the top diamond.
  trees, props   -> scaled by height; pivot = base centre.

Usage: import_painted_sheets.py <sheet folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys

import numpy as np
from PIL import Image

MAGENTA = np.array([255, 0, 255], dtype=np.float32)


def key_out(img):
    """RGBA with magenta removed: alpha from the distance to magenta, and its spill taken out of edge colours."""
    rgb = np.asarray(img.convert("RGB"), dtype=np.float32)
    d = np.linalg.norm(rgb - MAGENTA, axis=2)
    a = np.clip((d - 60.0) / (170.0 - 60.0), 0.0, 1.0)
    safe = np.maximum(a, 1e-3)[..., None]
    col = (rgb - (1.0 - a[..., None]) * MAGENTA) / safe
    col = np.clip(col, 0, 255)
    out = np.dstack([col, a * 255.0]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def objects(rgba, rows, cols):
    """Every object on the sheet (connected pixels; specks merged into the object they sit by), in reading order."""
    from scipy import ndimage
    alpha = np.asarray(rgba)[..., 3] > 24
    # Close small gaps so an object with separate bits (petals, a leaf) is one piece.
    joined = ndimage.binary_dilation(alpha, iterations=6)
    labels, n = ndimage.label(joined)
    boxes = ndimage.find_objects(labels)
    sizes = ndimage.sum(np.ones_like(labels), labels, index=range(1, n + 1))
    big = [(sl, sz) for sl, sz in zip(boxes, sizes) if sz > 0.002 * alpha.size]
    if len(big) != rows * cols:
        raise SystemExit(f"expected {rows * cols} objects, found {len(big)}")
    items = []
    for sl, _ in big:
        y0, y1, x0, x1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
        sub = alpha[y0:y1, x0:x1]
        ys, xs = np.nonzero(sub)
        box = (x0 + xs.min(), y0 + ys.min(), x0 + xs.max() + 1, y0 + ys.max() + 1)
        items.append(((y0 + y1) / 2, (x0 + x1) / 2, box))
    # Rows: sort by centre y, split into equal rows, then sort each row by x.
    items.sort(key=lambda t: t[0])
    ordered = []
    for r in range(rows):
        row = sorted(items[r * cols:(r + 1) * cols], key=lambda t: t[1])
        ordered.extend(row)
    out = []
    for _, _, box in ordered:
        crop = rgba.crop(box)
        # Keep only this object's pixels (a neighbour's edge may poke into the box).
        out.append(crop)
    return out


def scale_to_width(im, width):
    h = max(1, round(im.height * width / im.width))
    return im.resize((width, h), Image.LANCZOS)


def scale_to_height(im, height):
    w = max(1, round(im.width * height / im.height))
    return im.resize((w, height), Image.LANCZOS)


def ground_tile(im):
    """Top diamond exactly 128 x 64 (outline cropped away so neighbours join), earthy edge kept below it."""
    big = scale_to_width(im, 136)                       # the outline (about 4 px a side) falls outside 128
    canvas = Image.new("RGBA", (128, big.height - 4), (0, 0, 0, 0))
    canvas.paste(big.crop((4, 4, 132, big.height)), (0, 0))
    a = np.asarray(canvas).copy()
    h = a.shape[0]
    for y in range(h):
        for x in range(128):
            fx = abs((x + 0.5) - 64.0) / 64.0           # 0 centre .. 1 edge
            top = 32.0 * fx                             # upper diamond edges
            if y + 0.5 < top:
                a[y, x, 3] = 0
    out = Image.fromarray(a, "RGBA")
    pivot_y = (h - 32.0) / h
    return out, [0.5, round(pivot_y, 4)]


def block(im):
    """Isometric half-block, 128 wide; pivot at the centre of the top diamond (a quarter of the width below its top)."""
    out = scale_to_width(im, 128)
    pivot_y = (out.height - 32.0) / out.height
    return out, [0.5, round(pivot_y, 4)]


def standing(im, height):
    """A tree or prop by height; pivot at the base centre (a few px above the lowest pixel)."""
    out = scale_to_height(im, height)
    base = min(6, out.height // 10)
    return out, [0.5, round(base / out.height, 4)]


def silver(im):
    """Gold flecks recoloured to moonsilver (pale blue-white)."""
    a = np.asarray(im).astype(np.float32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    gold = (r > 150) & (g > 110) & (b < 110) & (r - b > 70)
    lum = (0.3 * r + 0.59 * g + 0.11 * b)
    a[..., 0] = np.where(gold, lum * 0.85, r)
    a[..., 1] = np.where(gold, lum * 0.95, g)
    a[..., 2] = np.where(gold, np.minimum(255, lum * 1.2 + 30), b)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def main(src, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    written = []

    def save(rel, pair):
        im, pivot = pair
        path = os.path.join(art, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        im.save(path)
        meta["sprites"][rel] = {"pivot": pivot}
        written.append((rel, im.size))

    # Ground tiles: 4 x 3.
    g = objects(key_out(Image.open(os.path.join(src, "ground-tiles.png"))), 3, 4)
    ground_names = ["grass_plain", "grass_flowers", "grass_tufts", "forest_grass",
                    "path_plain", "path_pebbles", "path_footprints", "dirt_dug",
                    "cobble", "cobble_grass", "planks", "soil_mossy"]
    for name, im in zip(ground_names, g):
        save(f"Painted/Ground/ground_{name}.png", ground_tile(im))
    # The tiles the game already uses.
    for key, name in [("grass_a", "grass_plain"), ("grass_b", "grass_flowers"), ("grass_c", "grass_tufts"),
                      ("forest_floor", "forest_grass"), ("forest_floor_b", "soil_mossy"),
                      ("path", "path_plain"), ("path_b", "path_pebbles"), ("path_c", "path_footprints"),
                      ("dirt", "dirt_dug"), ("plaza", "cobble")]:
        save(f"Tiles/tile_{key}.png", ground_tile(g[ground_names.index(name)]))

    # Cliff blocks: 4 x 4.
    b = objects(key_out(Image.open(os.path.join(src, "cliff-blocks.png"))), 4, 4)
    block_names = ["grass", "topsoil", "topsoil_pebbles", "sandy",
                   "sandstone", "red_clay", "slate", "mossy",
                   "purple", "ochre", "basalt", "limestone",
                   "ore_iron", "ore_copper", "ore_coal", "ore_gold"]
    for name, im in zip(block_names, b):
        save(f"Painted/Blocks/block_{name}.png", block(im))
    save("Painted/Blocks/block_ore_moonsilver.png", block(silver(b[block_names.index("ore_gold")])))

    # Trees and props: 4 x 4.
    t = objects(key_out(Image.open(os.path.join(src, "trees-and-props.png"))), 4, 4)
    prop_names = ["pine_tall", "pine_medium", "pine_small", "leafy_tree",
                  "pine_bent", "bush_big", "bush_small", "fern",
                  "stump", "log_fallen", "log_stack", "log_piece",
                  "boulder", "rocks_small", "grass_tuft", "flowers"]
    heights = {"pine_tall": 330, "pine_medium": 270, "pine_small": 200, "leafy_tree": 300, "pine_bent": 310,
               "bush_big": 120, "bush_small": 80, "fern": 90, "stump": 72, "log_fallen": 70, "log_stack": 90,
               "log_piece": 48, "boulder": 120, "rocks_small": 56, "grass_tuft": 44, "flowers": 48}
    for name, im in zip(prop_names, t):
        save(f"Painted/Nature/{name}.png", standing(im, heights[name]))
    # The nature sprites the game already uses (each resource keeps its own silhouette).
    for key, name in [("tree_cedar", "pine_tall"), ("tree_maple", "leafy_tree"), ("tree_ironwood", "pine_bent"),
                      ("tree_stump", "stump"), ("bush_herb", "bush_small"), ("rock_stone", "boulder")]:
        save(f"Nature/{key}.png", standing(t[prop_names.index(name)], heights[name]))

    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    for rel, size in written:
        print(f"{rel}  {size[0]}x{size[1]}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
