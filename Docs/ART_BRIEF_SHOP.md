# Brief for Astra: the shop (a Shop Titan-style showroom)

Reference images are in `/Users/kevinandersson/Desktop/RavenholmSupplyCo/Docs/astra-refs/shop/`:
- `shop_layout_template.png`: the exact layout.
- `shop_layout_spots.json`: the spot coordinates.

Also use `Docs/astra-refs/nature/` for the style and colours.

---

```
Hi Astra, next: the SHOP, the player's home base. Kevin wants it to feel like Shop Titan: a clear service desk where customers do business, and a readable showroom full of furniture that displays the weapons, armour and potions the player has crafted. All reference files are in:
/Users/kevinandersson/Desktop/RavenholmSupplyCo/Docs/astra-refs/shop/
(shop_layout_template.png is the exact layout to follow; shop_layout_spots.json has every spot's coordinates.)
Style and colours: the same as your v4 buildings (/Users/kevinandersson/Desktop/RavenholmSupplyCo/Docs/astra-refs/nature/): flat colours, one shadow tone, chunky simple shapes, no painted outlines (the game adds them), light from the top-left. Warm wood, stone floor, cosy and inviting.

THE ROOM (follow shop_layout_template.png exactly)
- 5 x 4 tiles, open front: NO walls on the two front edges, so the camera always sees inside. A low step/threshold along the front edges is fine.
- Two tall back walls (2.2 units = 563 px high): the back-left wall carries a shop sign with an icon (crossed sword and potion, NO text) and simple beams/plaster; the back-right wall is plainer.
- A stockroom DOOR in the back-left wall near the corner (where the player walks in to stock).
- Floor: wooden planks or flagstones, readable as indoors but not busy (furniture and items must stand out against it).
- Deliver the room as SEPARATE layers so furniture can sit between them: shop_floor.png (floor plus its front lip), shop_wall_left.png, shop_wall_right.png (each with any wall decoration but WITHOUT the furniture), shop_door.png (the door, so the game can animate it opening).

THE SERVICE DESK (the yellow DESK spot, 2 tiles long along the front-right edge)
- A counter facing out of the open front, so customers stand outside at the orange "customer" spot.
- On it: a ledger book, a small brass bell, coin scales. Behind it on the floor: a stool.
- 3 upgrade levels: plain plank counter; panelled counter with an iron-banded edge; carved counter with a brass trim and a small display window in its front.
- Files: desk_1.png, desk_2.png, desk_3.png.

DISPLAY FURNITURE (each piece in 3 upgrade levels; higher levels hold MORE items and look richer: 1 plain wood, 2 wood with iron bands and cloth, 3 dark wood with brass trim and blue velvet)
Wall-mounted (the red W spots; draw them against the back-LEFT wall; the game mirrors them for the right wall, so no text or one-sided details):
1. weapon_rack: blades and maces stand upright in it. Item spots: 3 / 4 / 5 by level.
2. long_rack: bows and staffs hang horizontally or lean in it. Item spots: 2 / 3 / 4.
3. potion_shelf: bottles in rows. Item spots: 4 / 6 / 9.
Floor-standing (the green F spots, each fits in 0.8 x 0.8 tiles, faces the camera):
4. armor_stand (a mannequin/dummy with a stand): holds ONE armour piece, shown big. Item spots: 1 / 1 / 1 (the levels only get fancier: wood dummy, padded dummy, dummy on a brass-rimmed plinth).
5. display_table: small items laid out flat (daggers, the focus, gems). Item spots: 2 / 3 / 4.
6. pedestal: the featured item, raised and lit. Item spots: 1 / 1 / 1 (levels: stone block, carved column, column under a small glass dome).
Files: weapon_rack_1..3, long_rack_1..3, potion_shelf_1..3, armor_stand_1..3, display_table_1..3, pedestal_1..3.

ITEMS ARE NOT IN THE ART
The game draws the player's actual items on the furniture. Draw the furniture EMPTY, but make every item spot visibly readable when empty: pegs, hooks, a cradle, a cushion, a bottle ring, so the player can see there is room. Items appear about 166 px tall on screen (0.65 units), so size the spots for that.

TECHNICAL
- 256 px per unit for everything (a tile is a 256 x 128 diamond; the character is 274 px tall, shown on the template).
- One PNG per piece on a transparent canvas with at least 12 px of clear margin, hard clean edges, no outlines, no drop shadows.
- points.json, for every file:
  - "ground": pixel of the centre of the piece's footprint on the floor (top-left origin). For wall pieces, the point where the piece's centre meets the floor line at the wall.
  - "slots": a list of item spots, each {"x","y","w","h","kind"}: the box where an item sits (top-left origin, pixels), kind = weapon / long / armor / small / potion / any.
  - For the room layers: the pixel position of tile corner (0,0), the front corner, so all layers line up.
- contact_sheet.png: the full room assembled (floor, walls, door, desk level 3, every W and F spot filled with level-3 furniture), with the character standing inside and some example items placed in the slots, so we can judge readability. Plus a second sheet showing every furniture piece at levels 1, 2 and 3 side by side.

CHECK BEFORE SENDING
[ ] The room matches shop_layout_template.png: 5 x 4, open front, walls on the back edges only, door and desk in place.
[ ] Separate layer files; furniture drawn empty with visible item spots.
[ ] 3 levels for the desk and each of the 6 furniture types, clearly richer at each level and holding more items where specified.
[ ] Wall pieces drawn for the back-left wall, mirror-safe.
[ ] points.json with ground points and slot boxes for every file.
[ ] The v4 style: flat colours, no outlines, 12 px margins.
```

---

**For Claude when it lands.** The shop becomes a 5×4 room built from layers:
- floor on the Ground layer;
- walls behind everything;
- the door, animated;
- desk and furniture as outlined sprites on the fixed spots.

`RackView` generalises to any furniture piece, reading its slot boxes and kind from points.json. Furniture spots and levels unlock with store upgrades (content: a store-furniture definition). The customer spot replaces today's receiving point at the bell.
