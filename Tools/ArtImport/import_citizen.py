#!/usr/bin/env python3
"""
Imports one of Astra's citizen body sets (e.g. ravenholm-citizen-angular-v3, 2026-10-15) into the character library:
Art/Placeholder/Characters/Citizens/<character_id>/body_{S,SE,E,NE,N}.png plus its points.json and library.json.
Bodies use the store owner's pixels per unit and ground pivot (256,444); the game outlines them. Hands and boots are
shared with the store owner (Astra's copies are byte-identical) and are not imported again.

Usage: python3 -I import_citizen.py <pack folder> <Assets/Ravenholm/Art/Placeholder>
"""
import json
import os
import shutil
import sys


def main(pack, art):
    lib = json.load(open(os.path.join(pack, "library.json")))
    cid = lib["character_id"]
    out = os.path.join(art, "Characters", "Citizens", cid)
    os.makedirs(out, exist_ok=True)
    meta_path = os.path.join(art, "sprite_meta.json")
    meta = json.load(open(meta_path))
    owner = meta["sprites"]["Characters/StoreOwner/body_S.png"]
    for view, name in lib["body_set"].items():
        shutil.copyfile(os.path.join(pack, name), os.path.join(out, name))
        meta["sprites"][f"Characters/Citizens/{cid}/{name}"] = {"pivot": owner["pivot"], "ppu": owner["ppu"], "outline": True}
    for f in ("points.json", "library.json"):
        shutil.copyfile(os.path.join(pack, f), os.path.join(out, f))
    json.dump(meta, open(meta_path, "w"), indent=1, sort_keys=True)
    print(f"{cid}: {len(lib['body_set'])} bodies imported")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
