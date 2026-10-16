"""Imports Astra's equipment handoff (2026-10-16): the neckless mannequin, four body armours, four headwear items and
three shields, as layers on the armour stand's shared 512 x 512 canvas (the existing stand import: 256 ppu, ground pivot
(0.5, 0.17969)), plus the blond citizen's SE headwear overlays (on body_SE's canvas: 340 ppu, its pivot).

Usage: python -I import_armour_handoff.py <handoff dir (the folder holding manifest.json)> <Assets/Ravenholm/Art/Placeholder>

No layer is redrawn, trimmed or rescaled; every positioned layer keeps its canvas and offset. Existing files are
overwritten in place (their .meta GUIDs are kept). Two cleanups only:
- the bare mannequin's faint stray pixels (alpha <= 40, more than 2 px from its solid shape) are cleared: the game's
  outline would draw them as specks;
- levels 2 and 3 of the stand keep their own base (the iron- and gold-capped supports) under the approved neckless
  mannequin: the rows from the base's top (y 338) down come from the existing level art.
"""
import json
import os
import shutil
import sys

from PIL import Image, ImageFilter

ARMOURS = ["riveted_coat", "fur_brigandine", "steel_coat", "moonsilver_mail"]
HEADWEAR = {"leather_cap": "leather-cap", "iron_helm": "iron-helm", "steel_helm": "steel-helm",
            "moonsilver_circlet": "moonsilver-circlet"}
SHIELDS = {"wooden_shield": "wooden-shield", "iron_shield": "iron-bound-shield",
           "steel_kite_shield": "steel-kite-shield"}
STAND_META = {"outline": True, "pivot": [0.5, 0.17969], "ppu": 256}
CITIZEN = "Characters/Citizens/citizen_blond_blue/"
BASE_TOP = 338   # the wooden support's top row on the shared canvas (stand/base-only.png)


def rgba(path):
    return Image.open(path).convert("RGBA")


def clean_strays(im):
    a = im.split()[3]
    solid = a.point(lambda v: 255 if v > 40 else 0).filter(ImageFilter.MaxFilter(5))   # the solid shape, grown 2 px
    px, keep = im.load(), solid.load()
    for y in range(im.height):
        for x in range(im.width):
            if px[x, y][3] and not keep[x, y]:
                px[x, y] = (0, 0, 0, 0)
    return im


def with_base(mannequin, level_art):
    out = mannequin.copy()
    out.paste((0, 0, 0, 0), (0, BASE_TOP, 512, 512))
    out.alpha_composite(level_art.crop((0, BASE_TOP, 512, 512)), (0, BASE_TOP))
    return out


def base_only(level_art):
    out = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    out.alpha_composite(level_art.crop((0, BASE_TOP, 512, 512)), (0, BASE_TOP))
    return out


def main(src, art):
    assert os.path.isfile(os.path.join(src, "manifest.json")), "not the handoff folder"
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    sprites = meta["sprites"]
    points_path = os.path.join(art, "Items/points.json")
    points = json.load(open(points_path))

    def put(rel, im, m):
        dst = os.path.join(art, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        im.save(dst)
        sprites[rel] = dict(m)

    def item_view(key, rel, kind, head=None):
        im = rgba(os.path.join(art, rel))
        bx = im.getbbox()
        entry = {
            "file": rel, "canvas": {"w": 512, "h": 512}, "anchor": {"x": 256, "y": 420},
            "pivot": {"x": 0.5, "y": 0.17969}, "bounds": {"x": bx[0], "y": bx[1], "w": bx[2] - bx[0], "h": bx[3] - bx[1]},
            "item_id": key, "facing": "A", "slot_kind": "armor", "pixels_per_unit": 256, "painted_outline": False,
            "stand_layer": kind, "source": "ravenholm-claude-handoff 2026-10-16",
        }
        if head:
            entry["stand_head"] = head
        points["assets"][os.path.basename(rel)] = entry

    # The stand: the approved neckless mannequin (empty), and the support alone (under armour), per level.
    neckless = clean_strays(rgba(os.path.join(src, "stand/armor_stand_neckless_A.png")))
    old = {n: rgba(os.path.join(art, f"Furniture/armor_stand_{n}.png")) for n in (1, 2, 3)}
    put("Furniture/armor_stand_1.png", neckless, STAND_META)
    put("Furniture/armor_stand_base_1.png", rgba(os.path.join(src, "stand/base-only.png")), STAND_META)
    for n in (2, 3):
        put(f"Furniture/armor_stand_{n}.png", with_base(neckless, old[n]), STAND_META)
        put(f"Furniture/armor_stand_base_{n}.png", base_only(old[n]), STAND_META)

    # Body armour (A view) and the head seated in each collar.
    for key in ARMOURS:
        put(f"Items/item_{key}_A.png", rgba(os.path.join(src, f"armor/item_{key}_A.png")), STAND_META)
        put(f"Items/stand_head_{key}.png", rgba(os.path.join(src, f"stand/head-for-{key}.png")), STAND_META)
        item_view(key, f"Items/item_{key}_A.png", "armor", f"Items/stand_head_{key}.png")
    put("Items/stand_head.png", rgba(os.path.join(src, "stand/head-overlay.png")), STAND_META)

    # Headwear: lowered onto the neckless head (stand); fitted to the blond citizen's SE body (character).
    for key, name in HEADWEAR.items():
        put(f"Items/item_{key}_A.png", rgba(os.path.join(src, f"headwear/stand/{name}.png")), STAND_META)
        item_view(key, f"Items/item_{key}_A.png", "head")
        body = sprites[CITIZEN + "body_SE.png"]
        put(CITIZEN + f"headwear_{key}_SE.png", rgba(os.path.join(src, f"headwear/character-SE/{name}.png")),
            {"outline": True, "pivot": body["pivot"], "ppu": body["ppu"]})

    # Shields: positioned beside the mannequin (stand); the isolated drawing becomes the item's icon (uniform scale).
    for key, name in SHIELDS.items():
        put(f"Items/item_{key}_A.png", rgba(os.path.join(src, f"shields/{name}-stand-overlay.png")), STAND_META)
        item_view(key, f"Items/item_{key}_A.png", "shield")
        shield = rgba(os.path.join(src, f"shields/{name}.png"))
        shield = shield.crop(shield.getbbox())
        k = 88.0 / max(shield.size)
        shield = shield.resize((max(1, round(shield.width * k)), max(1, round(shield.height * k))), Image.LANCZOS)
        icon = Image.new("RGBA", (96, 96), (0, 0, 0, 0))
        icon.alpha_composite(shield, ((96 - shield.width) // 2, (96 - shield.height) // 2))
        rel = f"Icons/Items/icon_{key}.png"
        icon.save(os.path.join(art, rel))   # its meta (centre pivot) is unchanged

    open(meta_path, "w").write(json.dumps(meta, indent=1))   # the files' own format: 1-space indent, no final newline
    open(points_path, "w").write(json.dumps(points, indent=1))
    shutil.copy(os.path.join(src, "manifest.json"), os.path.join(art, "Items/equipment_handoff_manifest.json"))
    print("imported", len(ARMOURS), "armours,", len(HEADWEAR), "headwear,", len(SHIELDS), "shields and the stand")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
