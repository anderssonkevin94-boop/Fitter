# Brief for Astra: the approved walk-in cart, as game pieces

Kevin approved the cart (2026-10-16, `cart_approved_reference.png` in `ravenholm-cart-approved-handoff`):
- compact walk-in shop;
- wooden roof supports on both ends;
- a rectangular doorway with the curtain rolled up;
- steps up at the left end;
- the long side as an open counter.

The game needs it as separate, empty pieces with anchors, so the player can climb the steps, stand inside behind the counter and serve, with everything drawing in the right order.

How it sits in the game today, unchanged:
- The cart parks behind the shop's carpet, its long side (the counter) facing the carpet, down-right on screen, exactly as in the reference.
- The shafts point up-right, up the road, where the ox is hitched.
- The carpet's back row runs along the counter, and that is where customers stand.

---

```
Hi Astra, the walk-in cart is approved: lovely work. Please keep the design exactly as in cart_approved_reference.png (proportions, timber, cream patched canvas with the brown bands and blue patch, blue metal fittings, both wooden roof supports, the rectangular doorway with the rolled curtain, the steps, the long open counter, the shafts). This pack turns it into game pieces. No characters in any file.

VIEW AND SCALE
- Same view as the reference: long side (the counter) facing down-right, shafts pointing up-right, steps and doorway at the left end.
- Game scale: 256 px per unit; the character is 274 px tall (the merchant in the reference is about 1.3x too big, so scale the cart down to match). One floor tile is a 256 x 128 diamond.
- The cart body should cover about 3 tiles along its length and 1.5 tiles deep; the steps add about 1 tile at the left end; the shafts reach about 2 tiles up-right. Please line the body's base up with the tile grid (its edges along the two diamond directions).
- v4 style: flat colours, one shadow tone, no painted outlines (the game adds them), light from the top-left.

LAYERS (all on the SAME canvas size and position, transparent PNGs, so they stack exactly)
1. cart_back: everything behind someone standing inside: the far wall and canvas inside, the chest, the back posts, the inside of the far end, the interior floor boards.
2. cart_front: everything in front of someone standing inside: the counter (top and front panel), the front posts below the counter, the near frame, the wheels on the near side, the near end below the doorway.
3. cart_roof: the canopy, the scalloped edge, the rolled curtain and both roof supports. It draws above the player's head; keep the space under the scallops open so the player inside stays visible.
4. cart_steps: the steps on their own (they sort by their own foot on the ground).
5. cart_shafts: the two shafts on their own (the ox is hitched there).
6. wheel_near_front, wheel_near_back: each wheel on its own small canvas, centred, so the game can turn them on the drive (the far wheels can stay painted in cart_back).
7. cart_travelling (optional): the same cart closed for the road: counter folded up, curtain down, steps stowed. One flattened PNG is fine for this one.

POINTS.JSON (pixel coordinates on the shared canvas, y down)
- ground_origin: the middle of the cart body's base on the ground.
- footprint: the body's base on the ground, 4 corners (for collision; the player walks round the outside).
- floor_polygon: the walkable inside floor, 4 corners at floor height.
- floor_height_px: how far above the ground the inside floor is.
- steps_bottom, steps_top: where the steps meet the ground and the floor (the player walks up between them).
- door: the middle of the doorway on the floor line.
- serve_spot: where the merchant stands behind the counter (feet).
- customer_spot: where a customer stands in front of the counter (feet, on the ground).
- counter_top_line: two points along the counter's top edge (items can be laid there later).
- hitch: where the ox's harness meets the shafts.
- wheel_near_front, wheel_near_back: the wheels' centres on the shared canvas.

DELIVER: the PNGs, points.json, and a contact sheet: the layers stacked (cart_back + steps + shafts + wheels + cart_front + cart_roof) with a 274 px character standing inside at serve_spot and another at customer_spot, plus the anchors drawn as small marks.

CHECK BEFORE SENDING
[ ] No characters in any layer; the cart is empty.
[ ] The stacked layers match the approved reference exactly (only the characters are gone).
[ ] A character standing at serve_spot shows from the waist up above the counter (as the merchant does now) and is not hidden by the roof.
[ ] All layers share one canvas size and position; anchors are exact.
[ ] Game scale: 256 px per unit, character 274 px tall.
```

---

**For Claude when it lands:**
- **Replacing the placeholder.** The cart replaces the wagon placeholder (`ShopView.RebuildWagon`), drawn as cart_back, steps, shafts, wheels, cart_front and cart_roof.
- **Walking in.** The player climbs from steps_bottom to steps_top onto the raised floor (`floor_height_px`) and walks inside floor_polygon. Sorting: behind cart_front and in front of cart_back while inside, under cart_roof.
- **Serving.** Standing at serve_spot is "serving": the counter's Store opens, and the Store's marker moves to customer_spot.
- **The drive.** cart_travelling plus turning wheels.
- **The ox** is a separate pack.
