# Brief for Astra: the shop, a complete building with a roof that lifts

Reference files are in `Docs/astra-refs/shop/`:
- `shop_outside.png`: the building seen from the street.
- `shop_inside.png`: the same building when the player walks in.
- `shop_layout_spots.json`: the coordinates.

---

```
Hi Astra, next: the SHOP, the player's home base. Kevin wants a complete, good-looking building in town that still lets the player see a Shop Titan-style showroom inside: a clear service desk where customers do business, and readable furniture that displays the weapons, armour and potions the player has crafted.

HOW IT WORKS IN THE GAME
- From the street it is a complete, closed building (shop_outside.png): walls, windows, a front door, a sign, a pitched roof, and a covered porch with two display tables.
- When the player walks in through the front door, the ROOF fades out and the two FRONT walls are swapped for low wall stubs (shop_inside.png), Sims "walls down" style, so the whole interior is visible. Walking out brings them back.
So the building is drawn as separate LAYERS the game can show or hide.

All reference files are here (please open them from this folder):
/Users/kevinandersson/Desktop/RavenholmSupplyCo/Docs/astra-refs/shop/
shop_outside.png and shop_inside.png are exact layout drawings (rough blocks: redraw them nicely, but keep every position, size and opening); shop_layout_spots.json has the coordinates.

STYLE: the same as your v4 buildings: flat colours, one shadow tone, chunky simple shapes, NO painted outlines (the game adds them), light from the top-left. A warm, inviting merchant's shop: timber frame and plaster, a terracotta or slate roof, a cheerful awning. The roof colours are your choice, but they must differ from the other town buildings.

LAYERS TO DELIVER (each its own PNG, all aligned on the same canvas size and origin, so they stack exactly)
1. shop_floor.png: interior floor (wood planks), plus the porch deck outside along the front-left wall.
2. shop_backwalls.png: the two back walls, INSIDE faces: beams, plaster, a shop sign with an icon (crossed sword and potion, no text), and the stockroom door (closed) at the STOCK spot.
3. shop_front_left.png: the full front-left wall, outside face, with the big shop window above the porch.
4. shop_front_right.png: the full front-right wall, outside face, with the window, the front door opening and a hanging sign above the door.
5. shop_door.png: the front door leaf (closed), separate so the game can swing it open.
6. shop_stubs.png: both front walls cut down to 0.3 units high (the top edge shows the wall's thickness, like a cut cake), with the door gap.
7. shop_roof.png: the roof, including its overhang and the gable.
8. shop_porch_awning.png: the porch awning and its two posts (always visible).

THE SERVICE DESK (the yellow DESK spot inside, facing the front door; the customer stands at the orange spot just inside the door)
- On it: a ledger book, a small brass bell, coin scales. Behind it: a stool.
- 3 upgrade levels: plain plank counter; panelled with iron-banded edges; carved with brass trim and a small display window in its front.
- Files: desk_1.png, desk_2.png, desk_3.png.

DISPLAY FURNITURE (each in 3 upgrade levels; higher levels look richer and hold MORE items: 1 plain wood, 2 wood with iron bands and cloth, 3 dark wood with brass trim and blue velvet)
Wall-mounted (red W spots; draw them for the back-LEFT wall; the game mirrors them for the right wall, so nothing one-sided):
1. weapon_rack: blades and maces upright. Item spots: 3 / 4 / 5.
2. long_rack: bows and staffs. Item spots: 2 / 3 / 4.
3. potion_shelf: bottles in rows. Item spots: 4 / 6 / 9.
Floor-standing (green F spots inside and blue P spots on the porch; each fits 0.8 x 0.8 tiles; faces the camera):
4. armor_stand: a dummy on a stand holding ONE armour piece, shown big (1 / 1 / 1; only the looks improve).
5. display_table: small items laid flat (daggers, the focus, gems). Item spots: 2 / 3 / 4.
6. pedestal: the featured item, raised (1 / 1 / 1; stone block, carved column, column under a small glass dome).
Files: weapon_rack_1..3, long_rack_1..3, potion_shelf_1..3, armor_stand_1..3, display_table_1..3, pedestal_1..3.

ITEMS ARE NOT IN THE ART
The game places the player's actual crafted items on the furniture (about 166 px tall, 0.65 units). Draw the furniture EMPTY, but make every item spot visible when empty (pegs, hooks, cradles, cushions, bottle rings), so the player can see there is room.

TECHNICAL
- 256 px per unit (a tile is a 256 x 128 diamond; walls 2.2 units = 563 px; the character is 274 px tall).
- Transparent PNGs, at least 12 px of clear margin around each object, hard clean edges, no outlines, no drop shadows.
- points.json:
  - for the building layers, the pixel of tile corner (0,0) (the front corner of the floor) on the shared canvas;
  - for every furniture and desk file, "ground" (the pixel of the footprint centre on the floor; for wall pieces, where the piece's centre meets the floor at the wall) and "slots": a list of {"x","y","w","h","kind"} boxes, top-left origin, kind = weapon / long / armor / small / potion / any.
- Two contact sheets:
  (a) OUTSIDE: the complete building (floor, back walls, front walls, door, roof, porch with a table on each P spot showing example items) with the character on the street.
  (b) INSIDE: the same with the roof and front walls hidden and the stubs shown, desk level 3 and every W and F spot filled with level-3 furniture holding example items, the character standing inside.
  Plus a sheet with every furniture piece at levels 1, 2 and 3 side by side.

CHECK BEFORE SENDING
[ ] Outside it looks like a complete, attractive shop; inside, everything is readable.
[ ] Layout exactly as shop_outside.png and shop_inside.png (door, windows, desk, porch, spots).
[ ] 8 aligned building layers, 3 desk levels, 6 furniture types x 3 levels, drawn empty with visible item spots.
[ ] points.json complete; v4 style; no outlines; 12 px margins.
```

---

**For Claude when it lands.**
- **Layers.** The shop becomes stacked layers. The roof and front walls fade out, and the stubs fade in, while the player is inside its footprint. The door swings open as he passes.
- **Furniture.** `RackView` becomes general furniture that reads its slot boxes and kind from points.json. Spots and levels unlock with store upgrades (a store-furniture definition in content).
- **Customer and stock.** The customer spot takes over from today's bell point; the stockroom door from the receiving strip.
- **Town layout.** The 5×4 footprint means re-laying out the plaza and the buildings next to the shop.
