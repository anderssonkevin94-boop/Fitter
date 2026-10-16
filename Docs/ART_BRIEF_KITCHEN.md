# Brief for Astra: the Kitchen

The Kitchen (journey step 6c) cooks the forest's berries and mushrooms into food that adventurers take on jobs: Trail Rations, Mushroom Stew, and at level 2 the Hearty Pie. Today it is a placeholder, the Apothecary's building with a red roof.

How it works in the game:
- It sits on a 2 × 2 tile footprint and has two levels.
- The player delivers berries and mushrooms to a basket at its front-right. Finished food appears on its output pad at the front-left.
- Like the other stations, it packs into a kit when the camp moves and is set up again at the next camp.

Paste the block below to Astra.

---

```
Hi Astra, a new building for Ravenholm: the Kitchen, a small camp cookhouse where forest berries and mushrooms are cooked into food for the adventurers (trail rations, mushroom stew, a hearty pie). It travels with the company from camp to camp, so it should feel like a sturdy, slightly portable field kitchen rather than a town house: a timber frame with a canvas or shingle roof, an open cooking side with a stone hearth or iron stove and a chimney with a little smoke, a cauldron or stew pot, a worktable with a chopping board, hanging herbs and pots, sacks and a crate or two. Warm and inviting; it should read as "food" at a glance next to the Forge, Workshop and Apothecary.

STYLE AND VIEW
Same as the game's other buildings (see Art/Placeholder/Buildings/bld_forge.png and bld_apothecary.png in the project): our 2:1 isometric view, the building's two visible walls facing down-left and down-right, flat v4 colours, one shadow tone, no painted outlines (the game adds them), light from the top-left, transparent PNG, no characters.

SIZE AND ANCHOR
- 256 px per unit. Its footprint is 2 x 2 tiles (one tile is a 256 x 128 diamond), so the base is a 512 x 256 diamond.
- Canvas about 640 x 600 px, the building filling it like bld_apothecary.png does (that file is 632 x 602).
- Ground point: the centre of the footprint diamond on the ground. Please give its pixel position.

WHAT TO PAINT
1) kitchen_1.png: the Kitchen at level 1, a simple field kitchen: hearth or stove, one pot, a worktable, a small chimney.
2) kitchen_2.png: level 2, the same building upgraded, with a bigger oven or a second pot, a little awning or more shelves, more food about. Same footprint and ground point; it should read as the same building, improved.
3) kitchen_kit.png (optional): the Kitchen packed for travel, a bundle of crates, the folded canvas and the pot strapped on. A footprint of about 1 x 1 tile, same anchor convention.

POINTS.JSON (pixel coordinates on each canvas, y down)
- ground: the footprint centre on the ground
- footprint: the 4 corners of the 2 x 2 base diamond
- pad: where finished food is set down, on the ground at the building's front-left corner (just outside the footprint)
- delivery: where berries and mushrooms are delivered, on the ground at its front-right corner (just outside the footprint)
- chimney_top: where smoke can rise from (the game may add a little puff)

DELIVER: the PNGs, points.json, and a contact sheet with level 1, level 2 and the kit side by side, next to bld_forge.png and bld_apothecary.png at the same scale, with the points marked.

CHECK BEFORE SENDING
[ ] 256 px per unit; the 2 x 2 footprint is a 512 x 256 diamond at the ground point.
[ ] Levels 1 and 2 share the footprint and ground point and read as the same building.
[ ] Flat v4 style, light from the top-left, no outlines, no characters.
[ ] It reads as "Kitchen" at a glance among the Forge, Workshop and Apothecary.
```

---

**For Claude when it lands:**
- **Art.** It replaces `Art/Placeholder/Buildings/bld_kitchen.png`, keeping the GUID. Level 2 and the kit art are new, wired like the other stations' level art.
- **Pivot and pads.** The pivot comes from `ground`. `pad` and `delivery` place `container.kitchen_pad` and `receiver.kitchen`, relative to the building.
- **Smoke.** The chimney point can carry a small smoke puff while it cooks.
