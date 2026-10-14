# Brief for Astra: trees, nature, buildings, and brighter ground

Attach these files from `Docs/astra-refs/nature/`:
- `character_reference.png`
- `current_nature.png` (the colours to keep)
- `current_buildings.png`
- `ground_v3_contact_sheet.png`
- `ingame_now.png`

---

```
Hi Astra, the ground and blocks are in the game and look great. Next set, in the same style. Kevin's direction: KEEP THE VIBRANT COLOURS OF THE CURRENT TREES, and the ground you made is a little too dull, so part 1 is a quick colour pass on it.

STYLE (all parts)
- Same as your ground/blocks and the store owner: flat colours, one shadow tone plus at most one highlight per shape, chunky simple shapes, no texture noise, no gradients.
- NO painted outlines. The game draws one consistent outline around trees, bushes, rocks and buildings itself. Leave at least 12 px of fully transparent margin around every object, with clean hard edges (no glow, no semi-transparent haze).
- Light from the top-left, as on the blocks.
- COLOUR: vibrant and saturated, like current_nature.png. Keep these greens: dark #2D6747, mid #5B9347, light #92B342, and warm trunk browns #976432 and #6E4E30. These colours are the target; only the outlines and texture go.

PART 1: GROUND COLOUR PASS (same 29 files, same shapes)
Re-colour your ground tiles and blocks to be livelier: about +25% saturation and +8% brightness on the top faces. Grass moves from #94A16E toward a fresh green between #8DB04E and #9DBA5A. Path from #D2BC91 toward a warmer #DDBF85. Keep the face multipliers (1.00 / 0.82 / 0.66), the shapes, the canvases, the tiling and the names exactly. The trees must still be the most saturated green on screen, and the character must still stand out.

PART 2: NATURE (17 files, one PNG each, these names)
pine_small, pine_medium, pine_tall, pine_bent, leafy_tree, bush_small, bush_big, bush_herb (a bush with small pickable leaves/flowers), fern, grass_tuft, flowers, boulder, rocks_small, stump, log_fallen, log_piece, log_stack
- Same silhouettes and proportions as current_nature.png, redrawn flat.
- Canvas: twice the current size (pine_tall 438 x 660, leafy_tree 512 x 600, bush_big 288 x 240; double each current canvas).
- Mark the ground point (bottom centre of the trunk or object) in points.json.
- Trees: a clear trunk base on the ground; the canopy reads in three tone bands (dark, mid, light).

PART 3: BUILDINGS (9 files)
bld_store, bld_workbench, bld_forge, bld_smelter, bld_lumbermill, bld_apothecary, bld_trader_pad, bld_mine_mouth, bld_forest_gate
- The same footprints and layout as current_buildings.png (same isometric angle, same footprint size on the ground).
- Redraw in the new flat style with warm wood, stone and cloth colours that sit well next to the store owner's browns. One accent colour per building (forge: ember orange; apothecary: herb green; store: the awning colour, etc.).
- Canvas: twice the current size. points.json gives each building's ground point: the centre of its footprint.

DELIVER
Ground/ and Blocks/ (part 1), Nature/ (part 2), Buildings/ (part 3), points.json, and a contact sheet showing the character standing among the trees, bushes and buildings on the new ground.

CHECK BEFORE SENDING
[ ] Tree colours as vibrant as current_nature.png; ground livelier but still less saturated than the trees.
[ ] No painted outlines; 12 px clear margin and clean edges on every object.
[ ] Exact file names; canvases at twice the current size; ground points in points.json.
[ ] Ground and blocks keep their shapes, canvases and tiling.
[ ] The same flat style as the store owner and your blocks.
```

---

**For Claude when it lands.**
- **Nature and buildings** import at 256 px per unit, with the pivot at the ground point in points.json. They get the game outline: `"outline": true` in `sprite_meta.json`. Their `.meta` files and GUIDs are kept.
- **Ground and blocks** are re-imported with `import_world_tiles.py`.
- **Trees** are drawn by `ForestGridView`, which takes its sprites from `Painted/Nature`. Their sorting points (the pivot) must stay at the trunk base.
