# Brief for Astra: the Blacksmith (smelting and forging under one roof)

Kevin, 2026-10-17: one building where you can smelt and craft at the same time, in place of the separate Smelter and Forge.

How it works in the game today, which this building has to carry:
- **Smelting** runs by itself. You deliver raw ore, coal and gems to the Smelter, and it turns them into ingots: copper, iron, moonsilver, and steel from iron. Its progress shows as a bar over the building.
- **Forging** is crafting from those ingots: swords, daggers, mail, helmets and shields. Finished pieces appear on its output pad for you to collect.
- Both have three levels and pack into a kit when the camp moves on.
- Placeholders today: `bld_smelter.png` (a kiln with a tall chimney) and `bld_forge.png`, each on a 2 × 2 footprint.

Paste the block below to Astra.

---

```
Hi Astra, a new building for Ravenholm: the Blacksmith, a working smithy where ore is smelted into ingots AND those ingots are forged into weapons and armour, both at the same time. It replaces two separate buildings (the Smelter and the Forge), so it should read as one sturdy workshop with two clear working halves:

- THE SMELTING HALF (left as you look at it): a squat stone furnace or bloomery with a glowing mouth, a tall stone chimney with a little smoke, a bellows, and a heap of ore and a coal bin beside it where deliveries are dumped. Ingots cooling on a stone ledge.
- THE FORGING HALF (right): an open-fronted timber bay with the forge hearth (glowing coals), an anvil on a stump in front of it, a quench barrel, hammers and tongs on a rack, and a few finished pieces on display (a sword, a shield, a helmet) on a rack or table at the front-right.

The two halves share one roof line (timber frame, shingle or slate) and read as a single building at a glance, the furnace's chimney the tallest point. It travels with the company from camp to camp, so it is solid and practical rather than grand. Warm firelight is the mood: it should read as "metal and fire" at a glance next to the Kitchen, Workshop and Apothecary.

STYLE AND VIEW
Same as the game's other buildings (see Art/Placeholder/Buildings/bld_forge.png, bld_smelter.png and bld_apothecary.png in the project): our 2:1 isometric view, the building's two visible walls facing down-left and down-right, flat v4 colours, one shadow tone, no painted outlines (the game adds them), light from the top-left, transparent PNG, no characters.

SIZE AND ANCHOR
- 256 px per unit. Its footprint is 3 x 2 tiles (one tile is a 256 x 128 diamond): 3 tiles along the down-right wall, 2 along the down-left wall, so the base is a 640 x 320 diamond.
- The smelting half sits on the left (back) end of the long wall, the forging half on the right (front) end.
- Canvas about 900 x 900 px, the building filling it the way bld_apothecary.png fills its canvas; the chimney may rise near the top.
- Ground point: the centre of the footprint diamond on the ground. Please give its pixel position.

WHAT TO PAINT
1) blacksmith_1.png: level 1, a modest smithy. A small furnace with a short chimney, a simple hearth, an anvil on a stump, and a lean-to roof over the forging half.
2) blacksmith_2.png: level 2, the same building improved. A taller furnace with a big bellows, a proper roofed forge bay, a tool rack, more ore and finished goods about.
3) blacksmith_3.png: level 3, the master smithy. A double furnace or a crucible, a grand chimney, a second anvil, a display rack of fine pieces, and banners or metal trim. Same footprint and ground point as levels 1 and 2; it should read as the same building, grown.
4) blacksmith_kit.png: the Blacksmith packed for travel. The anvil, a crate of tools, the bellows folded and bricks stacked. A footprint of about 1 x 1 tile, same anchor convention.
5) Fire overlays, so the game can make the fires flicker while it works. For each level, two small separate PNGs on the same canvas and anchor as that level: blacksmith_{level}_furnace_glow.png (the furnace mouth's glow) and blacksmith_{level}_hearth_glow.png (the forge hearth's coals). Each one only the bright part, transparent everywhere else.

POINTS.JSON (pixel coordinates on each canvas, y down; per level where they move)
- ground: the footprint centre on the ground
- footprint: the 4 corners of the 3 x 2 base diamond
- delivery: where ore, coal and gems are delivered, on the ground at the smelting half's front corner (just outside the footprint, down-left)
- pad: where finished pieces are set down, on the ground at the forging half's front corner (just outside the footprint, down-right)
- progress_bar: a point above the furnace where the game can hang the smelting progress bar
- chimney_top: where smoke rises from
- anvil: the anvil's striking face (the game may add sparks while crafting)

DELIVER: the PNGs, points.json, and a contact sheet with levels 1-3 and the kit side by side, next to bld_forge.png and bld_smelter.png at the same scale, with the points marked.

CHECK BEFORE SENDING
[ ] 256 px per unit; the 3 x 2 footprint is a 640 x 320 diamond at the ground point.
[ ] Smelting half on the left, forging half on the right; one building, one roof line.
[ ] Levels 1-3 share the footprint and ground point and read as the same building.
[ ] The glow overlays line up exactly with their level's furnace mouth and hearth.
[ ] Flat v4 style, light from the top-left, no outlines, no characters.
```

---

**For Claude when it lands:**
- **Design decision first (ask Kevin):**
  - One combined station, or one building housing the two stations (smelting line and craft slots) as today? The second keeps every recipe, process, save and level as it is and only changes the building and its art.
  - The levels too: does the Blacksmith's level count for both, or do smelting and forging level separately?
- **Art.** New files under `Art/Placeholder/Buildings/`. The Smelter's and Forge's GUIDs stay until the decision above is made. The pivot comes from `ground`.
- **Points.**
  - `delivery` places `receiver.smelter` and `pad` places `container.forge_pad`.
  - `progress_bar` places the smelting `ProcessorBar`.
  - `chimney_top` can carry smoke, and `anvil` sparks while a craft is in progress.
- **Glow overlays.** They flicker gently while the smelting line or a craft is running, and are off when idle.
- **Footprint.** It grows from two 2 × 2 buildings to one 3 × 2: check the camp layout (`groupSpots`, station placement) and the kit's size.
