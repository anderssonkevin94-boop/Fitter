# Brief for Astra: the modular shop (Shop Titans style)

Kevin turned down the fixed shop (2026-10-15). The shop is now built from tiles: the player places furniture where they want it and expands the building one strip at a time, paying in resources. This brief replaces the roof-lift brief (`ART_BRIEF_SHOP.md`). The shop has no roof: it is always seen as an open room, as in Shop Titans.

Attach these files from `Docs/astra-refs/shop-modular/`:
- `shop_modular_layout.png`: how the pieces assemble (rough blocks).
- `template_floor_256x152.png`
- `template_wall_left_176x700.png` and `template_wall_right_176x700.png`
- `template_stub_left_176x220.png` and `template_stub_right_176x220.png`
- `template_post_back_64x700.png` and `template_post_front_64x220.png`
- `template_wallpiece_left_2seg_304x760.png`
- `template_footprint_*.png` (1x1, 2x1 facing A, 1x2 facing B, 3x1 facing A, 2x2)

Also attach `furnished_shop.png` and `furniture_levels.png` from Astra's own v2 pack, so she can see her furniture.

---

```
Hi Astra, Kevin has changed the shop: it is now built like Shop Titans. The player places the furniture wherever they want on a tile grid, and expands the shop one strip at a time with resources. There is no roof: the shop is always an open room seen from above. So I need the shop as MODULAR pieces that snap together on the grid, plus your v2 furniture in a second facing.

Your v2 furniture stays: same style, same levels, same slots, same points.json format. Thank you, it looks great in the game.

GRID AND SCALE (all pieces)
- 256 px per unit. One floor tile is a 256 x 128 diamond. The character is 274 px tall.
- The shop starts at 4 x 4 tiles and grows a strip at a time, up to about 8 x 8.
- Back walls stand on the two BACK edges. They are 2.2 units tall (563 px), and you see their inside faces.
- Low wall stubs (0.3 units, 77 px) run along the two FRONT edges, cut flat on top like a cake slice so you can see the wall's thickness.
- Every wall and stub piece covers exactly ONE tile edge, so any length of wall is a row of pieces. Neighbouring pieces must join with no visible seam.
- Light comes from the top-left, as on all your pieces. This means the left and right walls are separate drawings, NOT mirror images. The same goes for the two facings of floor furniture.
- Style: the same as your v2 furniture and v4 buildings. Flat colours, one shadow tone, chunky shapes, no painted outlines (the game adds them), no drop shadows. Transparent PNGs with hard edges.
- Use the templates exactly. The grey shape is the piece's footprint and silhouette limit. The red dot is the anchor: write it in points.json as "anchor" (top-left pixel origin).

A) SHOP SHELL (one PNG each)
1. Floor tiles on template_floor (the diamond plus a 24 px lip, which is the floor's thickness along the front edges):
   - floor_wood_1, floor_wood_2, floor_wood_3: plank variants that tile seamlessly with each other.
   - floor_plot: unbuilt expansion ground (packed earth with a stake and rope), shown where the shop can grow.
2. Back walls, one tile segment each, on template_wall_left and template_wall_right. Each name in both versions, _left and _right:
   - wall_plain: plaster and timber.
   - wall_beam: with a timber post at its far end.
   - wall_window
   - wall_door: the stockroom door, closed.
   - wall_shelfboard: plain, with a picture rail, for wall furniture to hang on.
3. Posts:
   - post_tall (template_post_back): for the back corner, and where a back wall meets a front stub.
   - post_low (template_post_front): for the front corner.
4. Front stubs, one tile segment each, on template_stub_left and template_stub_right:
   - stub_left, stub_right
   - stub_left_entrance, stub_right_entrance: the same segment with a gap and a threshold step. Customers walk in here.
5. expand_sign: a small wooden sign on a stake, with a hammer symbol. It stands on a plot that can be bought.

B) FURNITURE IN THE SECOND FACING
Facing A is your v2 drawing, with the long front face toward the bottom-left. Facing B is the same piece turned 90 degrees, with its long front face toward the bottom-right. The player rotates furniture between A and B.
- Floor pieces need both facings: desk_1..3_B, display_table_1..3_B, armor_stand_1..3_B.
- Wall pieces need a version for the back-right wall: potion_shelf_1..3_right.
- Footprints, in whole tiles (draw inside the matching template's diamond; facing B swaps the two numbers):
  - desk: 2 x 1
  - display_table: 1 x 1, 2 x 1 and 3 x 1 for levels 1, 2 and 3. Your v2 lengths already fit.
  - armor_stand: 1 x 1
- Wall pieces span exactly 2 wall segments (template_wallpiece_left_2seg). Their anchor is the floor point at the middle of the span.

C) NEW FURNITURE (3 levels each, the same material steps as v2; drawn empty, with the item spots visible)
- weapon_rack_1..3 (wall, _left and _right, 2 segments): blades and maces upright. Slots 3 / 4 / 5, kind "weapon".
- long_rack_1..3 (wall, _left and _right, 2 segments): bows and staffs. Slots 2 / 3 / 4, kind "long".
- pedestal_1..3 (floor, 1 x 1, symmetric, so one facing is enough): one featured item. Slot 1, kind "any".

D) PLACEMENT UI
- tile_ok and tile_blocked: 256 x 128 diamond overlays shown under a piece while it is being placed (green and red, semi-transparent fill allowed).
- Icons, 128 x 128, in the style of the game's UI icons: ic_move, ic_rotate, ic_store (put away), ic_cancel, ic_expand, ic_build.

POINTS.JSON
For every file, give "anchor" (or "ground" for floor furniture, meaning the centre of the footprint), "footprint" {"a","b"} in tiles for floor pieces, "segments" for wall pieces, "facing" A or B, "wall" left or right, and "slots" as in v2.

PLEASE DO THIS IN TWO STEPS
Step 1 (sample, for Kevin's approval):
- floor_wood_1
- wall_plain_left, wall_plain_right, wall_window_left
- stub_left, stub_right, stub_left_entrance
- post_tall, post_low
- desk_1_B
- a contact sheet: a 4 x 4 shop assembled from these pieces, with your v2 furniture placed in both facings and the character standing inside.
Step 2, after Kevin approves: everything else, plus contact sheets of a 4 x 4 shop and a 6 x 5 shop with every piece used.

CHECK BEFORE SENDING
[ ] Walls and stubs join seamlessly along an edge of any length, and at every corner post.
[ ] Floors tile seamlessly; the lip shows only along the shop's front edges.
[ ] Left and right walls, and facings A and B, are each drawn for top-left light, not mirrored.
[ ] Every floor piece fits inside its footprint template; every wall piece spans exactly its segments.
[ ] points.json is complete; 256 px per unit; no outlines; at least 12 px clear margin on furniture.
```

---

**For Claude when it lands.**
- **Shop grid.** The shop becomes a grid model in the Domain, saved as tile IDs and furniture placements: furniture ID, level, tile, facing.
- **Build mode.** Pick up, move, rotate and store furniture. Placement is checked against footprints, walls and the entrance path.
- **Shell.** It is drawn from the edge tiles.
- **Expansion.** Strips are bought with resources (UpgradeDefinitions in content), and the walls move out.
- **Furniture.** `FurnitureView` already reads levels and slots from points.json; it gains facings and wall sides.
