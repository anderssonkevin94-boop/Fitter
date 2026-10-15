# Brief for Astra: the ox and the wagon shop

Kevin's new direction (2026-10-15):
- The game is a journey to the castle in the distance, made in camps.
- Your shop travels on a wagon pulled by your trusty ox.
- At each camp the wagon parks, and the shop folds out of it: a rolled-out carpet, tables, racks and armour stands on it, and a tarp roof.
- Upgrading the wagon makes the fold-out more luxurious.
- Everything must stay visible, with nothing to hide (no walls or roofs fading out), and every structure should look complete.

This replaces the modular walled shop for the shop itself. The shop's furniture art carries over.

---

```
Hi Astra, Kevin has a new heart for the game: the whole game is a journey to the castle in the distance, travelled in stages. Your shop lives on a WAGON pulled by your trusty OX. At each camp the wagon parks and the shop FOLDS OUT of it: a carpet rolls out, tables and racks stand on it, and a tarp roof stretches over part of it. Upgrading the wagon makes the fold-out shop more luxurious. Please design the ox and the wagon shop.

You have full visual freedom in your v4 style: flat colours, one shadow tone, no painted outlines (the game adds them), light from the top-left, 256 px per unit (the character is 274 px tall). The functional requirements are below.

THE ONE BIG RULE: EVERYTHING STAYS VISIBLE
The camera looks down from the south (isometric). The player must always see the whole carpet, all the furniture and the goods on it, without the game hiding or fading any walls or roofs. So:
- No walls. The wagon stands at the back of the shop; the open market is in front of it, toward the camera.
- The tarp roof only covers the back strip of the shop (along the wagon side), slopes up and away, or is held high on poles so it frames the market instead of covering it. Design it so it never hides the carpet, furniture or the character.
- Every piece looks complete on its own: a finished wagon, a finished stall. Nothing is a cutaway.

A) THE OX
- A big, gentle, trusty ox: the player's companion. Chunky, warm and characterful.
- Views:
  - harnessed to the wagon, walking, seen from the front-right (moving up-right, away from the camera);
  - standing unhitched at camp, seen from the front-left.
- Animations, as frame strips (each frame its own PNG, on the same canvas and anchor):
  - walk: 8 frames, harnessed;
  - idle: 4 frames (breathing, tail flick, ear flick);
  - graze: 6 frames (head down, chewing);
  - happy: 4 frames (head toss, for when you return).
- Ground point (between the front and back hooves) in points.json.

B) THE WAGON, travelling
- A covered wagon in 3 upgrade levels:
  1. a rough cart with a patched canvas;
  2. sturdy, painted, with iron fittings and lanterns;
  3. a merchant's pride: carved, brass trim, a striped canvas, banners.
- Travelling view: moving up-right (away from the camera), with the ox hitched in front. Wheels as separate sprites (wheel_front, wheel_back per level) so the game can turn them.
- Footprint about 1.5 x 3 tiles. Ground point in points.json; the hitch point where the ox's harness meets the shaft.

C) THE WAGON, parked and folded out (the shop)
- The parked wagon, seen from the front (its long side facing the camera), at each level. Its near side folds DOWN into a SERVICE COUNTER: this is where customers do business (it replaces the separate desk), with a ledger, a bell and scales painted on.
- A short fold-out sequence: 3 stages from closed to fully open (side lowered, awning lifting, goods out). The game plays it when you make camp.
- Tarp roof pieces that attach to the wagon and to poles: tarp_segment_left and tarp_segment_right (one tile edge wide, repeating seamlessly, like the shop's wall pieces), tarp_pole, and tarp_corner. Levels 1-3: plain canvas, then dyed with fringe, then a striped awning with tassels and lanterns.
- Optional decoration that makes it feel like a market: bunting between poles, hanging lanterns, a sign board on a post, crates and sacks by the wagon.

D) THE CARPET (the shop floor, laid in front of the wagon)
- On the same tile grid as before (one floor tile = a 256 x 128 diamond). The carpet grows as the wagon is upgraded and the player clears more space.
- Pieces at each of 3 levels (1 a plain woven rug, 2 a patterned rug with fringe, 3 a rich carpet with a border and tassels):
  - carpet_centre (tiles seamlessly);
  - carpet_edge_left and carpet_edge_right (the fringed border along the two front edges, in both directions);
  - carpet_corner.
- The carpet lies flat on the ground; no lip or height.

E) FURNITURE THAT STANDS FREE
The old wall shelves need floor-standing versions (there are no walls now), in the same 3 material levels as your v2 furniture:
- weapon_stand (a freestanding rack for blades and maces): 1 x 1 tile, slots 3 / 4 / 5, kind "weapon".
- long_stand (bows and staffs, upright): 1 x 1 tile, slots 2 / 3 / 4, kind "long".
- potion_shelf_standing (a small standing shelf or crate stack): 1 x 1 tile, slots 4 / 6 / 9, kind "potion".
Each in facing A and facing B, drawn empty with every item spot visible, slots in points.json as before.
Your existing tables, armour stands and pedestals stay as they are.

POINTS.JSON: ground points; ox and wagon hitch points; wheel centres; counter top, where customers stand; tarp attachment points on the wagon; slots for furniture.

PLEASE DO THIS IN TWO STEPS
Step 1, a sample for Kevin's approval:
- the ox: idle (4 frames) and walk (8 frames);
- the level-1 wagon travelling, with the ox hitched;
- the level-1 wagon parked and fully folded out;
- level-1 carpet pieces and tarp pieces;
- a contact sheet: the folded-out level-1 shop on a 6 x 4 carpet with your existing tables and armour stand, plus a weapon_stand, the character standing in it and the ox resting beside it.
Step 2, after approval: everything else.

CHECK BEFORE SENDING
[ ] From the game's camera, the whole carpet, all furniture and the character are visible; the tarp never hides them.
[ ] The structures look complete (no cutaways), and the upgrade levels clearly read as richer.
[ ] Repeating pieces (carpet, tarp) join seamlessly; anchors are exact.
[ ] v4 style, 256 px per unit, no painted outlines.
```

---

**For Claude when it lands.**
- **Camp shop.** The camp's shop becomes the wagon at the back plus the carpet grid in front. It reuses the shop grid (carpet tiles, furniture placement and build mode). The tarp replaces walls, and there's nothing to fade.
- **Counter.** The wagon's counter takes the desk's role (it opens the Store; customers stand at it).
- **Ox.** A sprite animator; moving camp is a short drive scene.
