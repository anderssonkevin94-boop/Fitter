# Brief for Astra: the modular shop (Shop Titans style)

Kevin's direction (2026-10-15):
- The player places furniture freely.
- The shop starts at 4 × 6 tiles and grows by buying 2 × 2 blocks.
- No roof for now.
- Astra has full visual freedom, as long as the pieces work.

This brief replaces the roof-lift brief (`ART_BRIEF_SHOP.md`).

**Attach** (from `Docs/astra-refs/shop-modular/`, all `template_*.png`). They show size and anchor limits only, not style. Do **not** attach `shop_modular_layout.png`.

---

```
Hi Astra, Kevin has redesigned the shop to work like Shop Titans: the player places their furniture wherever they want on a tile grid, and grows the shop by buying more floor. So I need the shop as MODULAR pieces that the game snaps together, plus your v2 furniture turned to its second facing. Your v2 furniture art stays (same style, levels and slots); thank you, it looks great in the game.

YOU HAVE FULL VISUAL FREEDOM. Design the shop however you think looks best in your style: materials, colours, beams, plaster, stone, details. The only hard requirements are the functional ones below: sizes, anchors, seamless joins, and the right set of pieces. The attached templates are NOT a style reference. They are plain grey shapes that show the exact size, angle and anchor point each piece must fit. Please don't copy their look.

HOW THE SHOP WORKS IN THE GAME
- The shop is a floor of isometric tiles. It starts as 4 x 6 tiles. The player buys 2 x 2 blocks next to the existing floor, so the shop can become L-, T- or U-shaped, not just a rectangle.
- There is NO roof: the shop is always seen from above as an open room, like Shop Titans.
- The game assembles the shop from one-tile pieces:
  - FLOOR: one tile each.
  - BACK WALLS (full height): on every edge that faces away from the camera with no floor behind it. You see their inside faces.
  - LOW WALLS (cut short like a slice of cake, so you can see the wall's thickness): on every edge that faces the camera. The game also uses them in place of a back wall wherever a full wall would hide floor behind it (inside corners of L-shapes).
  - POSTS: at every corner where wall pieces meet (outside corners and inside corners).
  - The ENTRANCE is a gap in one low wall at the front.
- The player places furniture on whole tiles, and can rotate floor furniture between two facings. Wall furniture hangs on full-height back walls.

GRID, SCALE AND LIGHT (exact; the templates show these)
- 256 px per unit. One floor tile is a 256 x 128 px diamond. The character is 274 px tall.
- Full walls are 2.2 units tall (563 px). Low walls are 0.3 units (77 px).
- Each wall or low-wall piece covers exactly ONE tile edge (128 px across, 64 px up or down). Any length of wall is a row of identical-sized pieces, so neighbouring pieces must join with no visible seam, and must also join seamlessly with a post at either end.
- Light comes from the top-left, as on all your pieces. So the walls on the left side and on the right side are separate drawings, NOT mirror images. Likewise, facing B of floor furniture is its own drawing.
- Style rules: the same as your v2 furniture and v4 buildings. Flat colours, one shadow tone, no painted outlines (the game adds them), no drop shadows. Transparent PNGs with hard edges; at least 12 px clear margin on furniture.

PIECES TO DELIVER

A) SHELL (one PNG each)
1. Floors (template_floor: the diamond plus a 24 px lip, which shows the floor's thickness along the shop's outer front edges):
   - floor_1, floor_2, floor_3: variants that tile seamlessly in any arrangement.
   - floor_plot: unbuilt ground the player can buy. It must read clearly as "you can build here".
2. Full back walls, one tile edge each, in a _left and a _right version (template_wall_left and template_wall_right):
   - wall_plain
   - wall_window
   - wall_door: the stockroom door, closed.
   - wall_rail: a wall that wall furniture hangs on. It can be the same as wall_plain if hanging works on it.
3. Low walls, one tile edge each, in a _left and a _right version (template_stub_left and template_stub_right):
   - stub_plain
   - stub_entrance: the gap customers walk through, with a threshold.
4. Posts:
   - post_tall: where full walls meet, and where a full wall meets a low wall (template_post_back).
   - post_low: where low walls meet (template_post_front).
5. plot_sign: a small sign or stake standing on a buyable 2 x 2 block.

B) YOUR V2 FURNITURE, SECOND FACING
Facing A is your v2 drawing (long front face toward the bottom-left). Facing B is the same piece turned 90 degrees (long front face toward the bottom-right).
- Floor pieces, facing B: desk_1..3_B, display_table_1..3_B, armor_stand_1..3_B.
- The wall piece for the right-hand wall: potion_shelf_1..3_right.
- Footprints are whole tiles, given as a x b: "a" counts tiles along the edge that rises to the right (like the desk's long side), "b" along the edge that rises to the left. Facing B swaps the two numbers. Each piece must stay inside its footprint's diamond (see the template_footprint_* files):
  - desk: 2 x 1
  - display_table: 1 x 1 at level 1, 1 x 2 at level 2, 1 x 3 at level 3 (its long side runs the other way to the desk's). Your v2 lengths already fit.
  - armor_stand: 1 x 1
- Wall furniture spans exactly 2 wall edges (template_wallpiece_left_2seg).

C) NEW FURNITURE (3 levels each: 1 plain wood, 2 wood with iron and cloth, 3 dark wood with brass and blue velvet, like v2; drawn EMPTY, with every item spot visible when empty)
- weapon_rack_1..3: wall piece in _left and _right versions, 2 edges. Blades and maces upright. Slots 3 / 4 / 5, kind "weapon".
- long_rack_1..3: wall piece in _left and _right versions, 2 edges. Bows and staffs. Slots 2 / 3 / 4, kind "long".
- pedestal_1..3: floor piece, 1 x 1, round or square so one facing is enough. One featured item. Slot 1, kind "any".

D) PLACEMENT UI
- tile_ok and tile_blocked: 256 x 128 diamond overlays shown under a piece while it is being placed. Clearly good and clearly bad; semi-transparent fill is allowed here.
- Icons, 128 x 128, matching the game's UI icons (flat, chunky, dark outline): ic_move, ic_rotate, ic_store (put away), ic_cancel, ic_expand, ic_build.

POINTS.JSON (top-left pixel origin; "pivot" also given normalised bottom-left, as in v2)
- Shell pieces: "anchor", the red-dot point of the template.
- Floor furniture: "ground" (the centre of the footprint), "footprint" {"a": n, "b": n}, "facing" "A" or "B", and "slots" as in v2.
- Wall furniture: "anchor" (the floor point at the middle of its span), "edges": 2, "wall": "left" or "right", and "slots".

TWO STEPS, PLEASE
Step 1, a sample for Kevin's approval:
- floor_1, floor_plot
- wall_plain_left, wall_plain_right, wall_window_left
- stub_plain_left, stub_plain_right, stub_entrance_left
- post_tall, post_low
- desk_1_B
- a contact sheet: a 4 x 6 shop with one 2 x 2 block added at the back (making an L-shape), built from these pieces, with your v2 furniture placed in both facings and the character standing inside.
Step 2, after approval: everything else, plus contact sheets of the 4 x 6 start and of a larger irregular shop using every piece.

CHECK BEFORE SENDING
[ ] Walls, low walls and posts join seamlessly at any length, and at outside AND inside corners.
[ ] Floor tiles join seamlessly in any arrangement; the lip only shows along the outer front edges.
[ ] Left and right walls, and facings A and B, are each drawn for top-left light, not mirrored.
[ ] Every floor piece fits its footprint; every wall piece spans exactly its edges.
[ ] points.json is complete; 256 px per unit; no outlines.
[ ] It looks like YOUR design: the templates only set the size and anchor.
```

---

**For Claude when it lands.**
- **Domain and save.** `Domain/Shop` holds the grid: the owned 2 × 2 blocks and the furniture placements (furniture ID, level, tile, facing). The save stores IDs only.
- **Shell.** It is built from the floor's edges:
  - a full wall goes on a back edge with no floor diagonally behind it;
  - otherwise a low wall goes there;
  - posts go at the joins.
- **Placement and expansion.** Build mode: move, rotate, put away and upgrade furniture. Expansion: 2 × 2 blocks bought with resources (content).
