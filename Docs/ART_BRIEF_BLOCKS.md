# Brief for Astra: ground tiles and blocks, in the store owner's style

Attach these files from `Docs/astra-refs/blocks/`:
- `character_reference.png`: the approved character in game. **This is the style to match.**
- `current_blocks_and_ground.png`: everything being replaced, with the filenames.
- `template_block_256x260.png` and `template_ground_256x152.png`: the exact shapes and canvases.

---

```
Hi Astra, next set: the GROUND TILES and BLOCKS the world is built from, redrawn so they match the store owner you made.

STYLE (match character_reference.png, NOT the old blocks)
- Flat colours with ONE shadow tone and at most one highlight per face, like the store owner's hat, apron and shirt. No painterly texture, no noise, no gradients.
- NO outlines and NO dark edge lines: the blocks sit edge to edge, and lines would draw a grid over the whole world. Faces are told apart by value only.
- Chunky and simple: at most 3-5 simple shapes per face (a few pebbles, 2-3 grass tufts, a crack). Must read at 128 px wide on a phone.
- Light from the TOP-LEFT on every piece: top face lightest, left face middle, right face darkest. Use the same three-step value ratio on every block, so the world reads as one material set.
- Earthy palette that sits UNDER the character: slightly more muted and a little lighter than his browns, so he stays the most saturated thing on screen. His colours: hat #A57A57, apron #73533A, dark #5A4335, shirt #E9D2B3, skin #DA875B. Grass: a soft, warm green, not neon.

SHAPES (use the templates exactly; the red dot is the anchor)
A) BLOCK: a half-height isometric cube. Top face = diamond 256 x 128 centred at (128, 64); left and right faces drop 128 px straight down (bottom tip at y = 256). Canvas 256 x 260, transparent outside the shape. Fill the shape exactly to its edges: no margin, no soft edges. Neighbouring blocks must meet with no visible seam (the top face must tile across neighbours).
B) GROUND TILE: the same diamond (256 x 128, centre at (128, 64)) with a 24 px lip below. Canvas 256 x 152. Must tile seamlessly with itself and with every other ground tile: keep 8 px along every diamond edge plain base colour.

DELIVER (one PNG each, these exact names)
Ground tiles (12): ground_grass_plain, ground_grass_tufts, ground_grass_flowers, ground_forest_grass, ground_path_plain, ground_path_pebbles, ground_path_footprints, ground_cobble, ground_cobble_grass, ground_dirt_dug, ground_planks, ground_soil_mossy
Surface blocks (3): block_grass (grass top, soil sides), block_topsoil, block_topsoil_pebbles
Rock layers, getting darker and cooler with depth (9): block_sandy, block_sandstone, block_ochre, block_red_clay, block_limestone, block_slate, block_mossy, block_basalt, block_purple
Ore blocks (5): slate rock with 2-3 chunky ore nuggets on the top and side faces, the same colours as the ore items you made: block_ore_copper (orange), block_ore_iron (blue-grey), block_ore_coal (black), block_ore_gold (yellow), block_ore_moonsilver (icy blue)

Also deliver contact_sheet.png showing a 4 x 4 patch of every ground tile tiled together, a 3 x 3 x 2 stack of every block, and the character_reference standing on them.

CHECK BEFORE SENDING
[ ] Flat colours, no outlines, no texture noise; it reads as the same world as the store owner.
[ ] Exact template shapes and canvases; blocks fill to their edges.
[ ] Ground tiles tile seamlessly (shown in the contact sheet).
[ ] The same light direction and face-value steps on every block.
[ ] The character stands out against every tile.
[ ] 29 files with the exact names, plus contact_sheet.png.
```

---

**How the game uses them (for Claude).**
- Blocks import at 256 px per unit, with the pivot at the top-face centre (0.5, 1 − 64.5/260). Ground tiles use the same scale, pivot (0.5, 1 − 64.5/152).
- They replace `Painted/Blocks/*` and `Painted/Ground/*` under the same names, so the mine, the forest terrain, the outcrops and the town tiles pick them up.
- None of them get the game outline.
