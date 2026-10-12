#!/usr/bin/env python3
"""
Cuts the supplier's animation strips (Docs/ART_PROMPTS_CHARACTER.md, step 3) into aligned game frames.

The strips come from an image generator: their scale, baseline and spacing drift. For every strip:
  1. key out the magenta (by hue: high red and blue, low green);
  2. split it into its frames (connected pixels; stray bits join the nearest frame);
  3. scale the whole strip so the character's painted area (tools left out) matches the idle reference, which keeps
     him the same size in every animation whatever the pose;
  4. anchor every frame on its feet (the lowest row of the body) and its torso (centre of the coat and apron), so the
     body stays put while the limbs and tools move; the climb anchors on the torso alone;
  5. for the carry frames, find where the forearms are (the pile sits there) as an offset from the anchor.

Writes Characters/Supplier/<clip>_<n>.png (2x: 256 px per unit), their pivots in sprite_meta.json, and
Characters/Supplier/animations.json (clips, frame rates, loop flags, carry anchors in units).

Usage: import_character_strips.py <strip folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

PPU = 256                 # 2x resolution: sharp on retina screens
BODY_UNITS = 1.0          # the idle character is one unit tall (two Mine blocks)

# clip: (frames, fps, loop, anchor mode)
CLIPS = {
    "idle_front": (4, 4, True, "feet"), "idle_back": (4, 4, True, "feet"),
    "walk_front": (6, 10, True, "feet"), "walk_back": (6, 10, True, "feet"),
    "carry_idle_front": (4, 4, True, "feet"), "carry_idle_back": (4, 4, True, "feet"),
    "carry_walk_front": (6, 8, True, "feet"), "carry_walk_back": (6, 8, True, "feet"),
    "mine_front": (5, 10, True, "feet"), "mine_back": (5, 10, True, "feet"),
    "mine_down_front": (4, 8, True, "feet"), "mine_up_back": (4, 8, True, "feet"),
    "chop_front": (5, 10, True, "feet"), "chop_back": (5, 10, True, "feet"),
    "climb_back": (4, 8, True, "torso"),
    "pickup_front": (3, 10, False, "feet"),
    "fall_front": (2, 1, False, "feet"),
}


def key(path):
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mag = np.minimum(r, b) - g                       # how magenta a pixel is
    alpha = np.clip((150.0 - mag) / 60.0, 0.0, 1.0)  # >= 150 fully keyed, <= 90 fully kept
    # Take the magenta spill out of edge pixels.
    m = np.array([255.0, 0.0, 255.0])
    a = np.maximum(alpha, 1e-3)[..., None]
    col = np.clip((rgb - (1.0 - alpha[..., None]) * m) / a, 0, 255)
    return col, alpha


def split(alpha, n):
    mask = alpha > 0.5
    lab, k = ndimage.label(ndimage.binary_dilation(mask, iterations=4))
    boxes = ndimage.find_objects(lab)
    sizes = ndimage.sum(mask, lab, range(1, k + 1))
    comps = sorted([(boxes[i], sizes[i], i + 1) for i in range(k) if sizes[i] > 50], key=lambda c: -c[1])
    big, small = comps[:n], comps[n:]
    if len(big) != n:
        raise SystemExit(f"expected {n} frames, found {len(big)}")
    centre = {c[2]: (c[0][1].start + c[0][1].stop) / 2 for c in big}
    groups = {c[2]: [c[2]] for c in big}
    for sl, _, label in small:
        cx = (sl[1].start + sl[1].stop) / 2
        groups[min(centre, key=lambda L: abs(centre[L] - cx))].append(label)
    return [np.isin(lab, groups[L]) & mask for L in sorted(groups, key=lambda L: centre[L])]


def classes(col):
    r, g, b = col[..., 0], col[..., 1], col[..., 2]
    mx, mn = col.max(axis=2), col.min(axis=2)
    lum = 0.3 * r + 0.59 * g + 0.11 * b
    tool = ((mx - mn) < 28) & (lum > 70) & (lum < 215)                 # grey iron of the pick and axe heads
    coat = (r > 140) & (r - g > 45) & (b < 110)                          # terracotta
    apron = (r > 190) & (g > 170) & (b > 120) & (r - b < 90)             # oatmeal linen
    return tool, coat | apron


def measure(col, m):
    tool, torso_px = classes(col)
    body = m & ~tool
    ys, xs = np.nonzero(body)
    area = body.sum()
    return body, torso_px & body, area


def anchor(body, torso, mode, ref_h):
    ys, xs = np.nonzero(body)
    top, bottom = ys.min(), ys.max()
    # Feet: the lowest row where the body is wide (shoes), not a thin handle.
    rows = body.sum(axis=1)
    feet = bottom
    for y in range(bottom, top, -1):
        if rows[y] >= 0.06 * ref_h:
            feet = y
            break
    band = torso.copy()
    lo, hi = int(feet - 0.70 * ref_h), int(feet - 0.30 * ref_h)
    band[:max(0, lo)] = False
    band[max(0, hi):] = False
    ty, tx = np.nonzero(band if band.sum() > 50 else torso)
    cx = float(np.median(tx))
    if mode == "torso":
        cy = float(np.median(ty)) + 0.5 * ref_h * 0.95   # where the feet would be under a steady torso
        return cx, cy
    return cx, float(feet)


def forearm(body, cx, feet, ref_h):
    """Where the pile sits: halfway out along the forearms (to the left of the torso), on their top."""
    h, w = body.shape
    lo, hi = int(feet - 0.75 * ref_h), int(feet - 0.25 * ref_h)
    region = body[max(0, lo):hi, :]
    cols = np.nonzero(region.any(axis=0))[0]
    tip = cols.min()                                     # the hands (they reach out to the left)
    x = int(round(tip + 0.35 * (cx - tip)))
    ys = np.nonzero(region[:, x])[0]
    y = (ys.min() if len(ys) else (hi - lo) // 2) + max(0, lo)
    return x, y


def main(src, art):
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    out_dir = os.path.join(art, "Characters", "Supplier")
    os.makedirs(out_dir, exist_ok=True)
    strips = {}
    for clip, (n, _, _, _) in CLIPS.items():
        col, alpha = key(os.path.join(src, clip + ".png"))
        strips[clip] = (col, alpha, split(alpha, n))
    # Reference: the idle front character's painted area and height.
    col, alpha, frames = strips["idle_front"]
    ref_area = np.median([measure(col, m)[2] for m in frames])
    ref_heights = []
    for m in frames:
        ys = np.nonzero(measure(col, m)[0])[0]
        ref_heights.append(ys.max() - ys.min())
    ref_h_px = float(np.median(ref_heights))
    target_h = BODY_UNITS * PPU

    manifest = {"ppu": PPU, "clips": {}}
    for clip, (n, fps, loop, mode) in CLIPS.items():
        col, alpha, frames = strips[clip]
        area = np.median([measure(col, m)[2] for m in frames])
        strip_ref_h = ref_h_px * np.sqrt(area / ref_area)        # this strip's idle-equivalent height
        scale = target_h / strip_ref_h
        entries = []
        for i, m in enumerate(frames):
            body, torso, _ = measure(col, m)
            ax, ay = anchor(body, torso, mode, strip_ref_h)
            ys, xs = np.nonzero(m)
            pad = 6
            x0, x1 = max(0, xs.min() - pad), xs.max() + pad + 1
            y0, y1 = max(0, ys.min() - pad), max(ys.max(), int(ay)) + pad + 1
            rgba = np.dstack([col, alpha * m * 255.0]).astype(np.uint8)
            crop = Image.fromarray(rgba[y0:y1, x0:x1], "RGBA")
            w, h = max(1, round(crop.width * scale)), max(1, round(crop.height * scale))
            crop = crop.resize((w, h), Image.LANCZOS)
            px, py = (ax - x0) * scale, (ay - y0) * scale         # anchor in the scaled crop (from top-left)
            name = f"{clip}_{i}.png"
            crop.save(os.path.join(out_dir, name))
            rel = f"Characters/Supplier/{name}"
            meta["sprites"][rel] = {"pivot": [round(px / w, 4), round(1.0 - py / h, 4)], "ppu": PPU}
            entry = {"sprite": rel}
            if clip.startswith("carry") or clip == "pickup_front" and i == n - 1:
                fx, fy = forearm(body, ax, ay, strip_ref_h)
                entry["carry"] = [round((fx - ax) * scale / PPU, 3), round((ay - fy) * scale / PPU, 3)]
            entries.append(entry)
        manifest["clips"][clip] = {"fps": fps, "loop": loop, "frames": entries}
        print(f"{clip}: scale {scale:.3f}, frames {[e['sprite'].split('/')[-1] for e in entries]}")
    json.dump(manifest, open(os.path.join(out_dir, "animations.json"), "w"), indent=1)
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
