# Brief for Astra: the equipment pack's missing views

The handoff of 2026-10-16 (`ravenholm-claude-handoff`) is in the game. It covers four armours, four headwear items, three shields and the neckless mannequin. Three things are still missing:
1. **The other facing of the stand (B).** Every stand can be turned in the shop. In facing B it still shows the old mannequin with a neck, and helmets and shields there show as piles.
2. **The citizen's other views.** Headwear was fitted only to the blond citizen's SE view. Walking any other way, they show no helmet.
3. **The stand's upgrade finishes.** The armour stand has three levels, bought with gold and materials. The old art showed each level as its own colour: a cream torso on a wooden base, a steel-blue torso on an iron-capped base, a royal-blue torso on a gold-capped base. The new mannequin came only in cream. Levels 2 and 3 now show the cream mannequin on their own bases, so an upgrade no longer changes the torso.

Paste the block below to Astra.

---

```
Hi Astra, your equipment pack is in the game and looks right: the neckless mannequin, the four armours with their collar heads, the four headwear items and the three shields, all stacked on your shared 512 x 512 canvas exactly as your README described. Three follow-ups, same rules as before: keep every approved design exactly as it is (no redrawing, no added detail, no squash or stretch, uniform scale only), flat v4 style, one shadow tone, no painted outlines, light from the top-left, transparent PNGs, no characters in any stand file, no visible neck on the mannequin, blank featureless cream head.

1) THE STAND'S OTHER FACING (B)
In the shop a stand can be turned to face the other way. The game's facing-B stand is:
  /Users/kevinandersson/Desktop/RavenholmSupplyCo/Assets/Ravenholm/Art/Placeholder/Furniture/armor_stand_1_B.png
(512 x 512, the same ground point as view A: x 256, y 420 from the top-left). Please make the B view of the whole pack on that canvas, matching that file's angle (the mannequin turned to face the other side, not mirrored):
- stand/armor_stand_neckless_B.png (bare mannequin with head), stand/base-only_B.png, stand/head-overlay_B.png
- stand/head-for-<key>_B.png for riveted_coat, fur_brigandine, steel_coat, moonsilver_mail
- armor/item_<key>_B.png for the same four armours (seated on the B mannequin, as the A ones are)
- headwear/stand/<name>_B.png for leather-cap, iron-helm, steel-helm, moonsilver-circlet
- shields/<name>-stand-overlay_B.png for wooden-shield, iron-bound-shield, steel-kite-shield (beside the mannequin, chest left visible)
All on the one shared canvas so they stack at (0,0) in the same order as A: base, armour, collar head, headwear, shield.

2) THE CITIZEN'S OTHER VIEWS
Headwear was fitted to citizen_blond_blue/body_SE.png only. Please fit the four headwear items to the other painted views of the same citizen:
  /Users/kevinandersson/Desktop/RavenholmSupplyCo/Assets/Ravenholm/Art/Placeholder/Characters/Citizens/citizen_blond_blue/body_S.png, body_E.png, body_NE.png, body_N.png
Each is a 512 x 512 canvas with the feet at x 256.5, y 444.5 from the top-left. Deliver headwear/character-<VIEW>/<name>.png on the same canvas as that view's body (S, E, NE, N; SW, W and NW are the game mirroring SE, E and NE). Keep the eyes and face clear in S, E and NE. In N (seen from behind) the headwear covers the back of the head. Same designs as your SE overlays, only turned.

3) THE STAND'S UPGRADE FINISHES (levels 2 and 3)
The armour stand has three levels. The old art showed each as its own finish:
  level 1: cream torso, wooden base (your neckless mannequin now)
  level 2: steel-blue torso, iron-capped base     -> see .../Furniture/armor_stand_2.png
  level 3: royal-blue torso, gold-capped base      -> see .../Furniture/armor_stand_3.png
Please make the neckless mannequin in the level 2 and level 3 finishes, same shape and canvas as level 1 and with the head in the level's colour:
  stand/armor_stand_neckless_A_lv2.png, stand/armor_stand_neckless_A_lv3.png
  stand/base-only_lv2.png, stand/base-only_lv3.png
  stand/head-overlay_lv2.png, stand/head-overlay_lv3.png
  stand/head-for-<key>_lv2.png and _lv3.png for the four armours
and the same for facing B (item 1) if you do both at once (suffix _B_lv2, _B_lv3).

DELIVER: one zip with the PNGs in those folders, a short README listing the files, and a contact sheet showing:
- every B state stacked (empty, each armour, each headwear, each shield, and one full set);
- the citizen in each view with each headwear;
- the three stand finishes side by side, empty and with the steel coat.

CHECK BEFORE SENDING
[ ] Every positioned layer is 512 x 512 and stacks at (0,0) on its base.
[ ] B views match armor_stand_1_B.png's angle and ground point.
[ ] No neck on any mannequin; the head sits in each collar.
[ ] Citizen overlays line up with each body view's own head; face clear (except N).
[ ] Designs are unchanged from the approved A views; uniform scale only.
```

---

**For Claude when it lands:**
- **Import.** Extend `Tools/ArtImport/import_armour_handoff.py`: B layers go to `Items/item_<key>_B.png`, `Items/stand_head_<key>_B.png` and `Furniture/armor_stand_<n>_B.png` (keep existing GUIDs), plus `armor_stand_base_<n>_B.png`.
- **Facing B.** Mark the B views as layered. `FurnitureView.LayeredStand` must then allow facing B when `facingB.armored` is set.
- **Citizen views.** Overlays become `headwear_<key>_<VIEW>.png` beside the bodies. `ProjectSetup.CitizenHeadwear` already picks them up per view.
- **Level finishes.** They replace the level 2 and 3 composites the import builds today (`with_base`/`base_only`). Each level then needs its own collar heads: add a per-level head to `ItemDefinition`, or pick the head by the stand's level in `FurnitureView`.
