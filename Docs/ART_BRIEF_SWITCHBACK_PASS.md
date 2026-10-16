# Brief for Astra: the switchback pass, seen from above

Kevin, 2026-10-16. The road to the castle becomes a switchback pass. Long legs zigzag left and right across the screen, climbing the screen toward the castle at the top. The legs turn at hairpins, and the hairpins are the gates between camps. Each camp sits mid-leg at the outer edge.

The playable ground stays at one height. Each leg is only **drawn** lower than the one before, so from the edge you look down onto the next leg of the road, then the one after, down toward the castle. As you walk round a hairpin, the game eases the view down onto the next leg.

How the levels are drawn:
- **The leg you're on and the one just below** are the real game ground (tiles, trees, blocks), shifted down one step.
- **Two or more legs away, and the valley floor beyond the cliffs,** are painted distance views. They replace today's flat green, which makes the land read as floating. These are what Astra supplies.

Paste the block below to Astra.

---

```
Hi Astra, a new piece for Ravenholm: the road to the castle is becoming a switchback mountain pass, and we need the far parts of it painted as seen from above. Kevin's sketch: long legs of road zigzag left and right across the screen, climbing the screen toward the castle at the top; the player stands on one leg and, looking over the cliff edge, sees the next legs of the pass lower down, smaller in the distance, all the way down to the castle. Same game view and v4 style as everything else: our 2:1 isometric view (one floor tile is a 256 x 128 diamond, 256 px per unit), flat colours, one shadow tone, no painted outlines, light from the top-left, transparent PNGs, no characters.

THE SIX REGIONS (one leg each, in order down the pass)
1 Meadow's Edge: meadow grass, mixed broadleaf and pine woods
2 River Bend: a river running along the leg
3 Hollow Wood: dense, dark old forest
4 Witchlight Fen: wet ground, dark pools, faint blue-green witchlights
5 Crown Highlands: rocky, sparse pines, grey stone
6 Castle Approach: an old cobbled road, the castle's outer walls
The old cobble road (mostly overgrown) runs along every leg.

WHAT TO PAINT, per region
a) A leg strip in each of the two directions the legs run:
   distant_<region>_leg_upright.png (the leg running up and to the right, along the tiles' x axis)
   distant_<region>_leg_upleft.png  (running up and to the left, along the tiles' y axis)
   Each a section 8 tiles long and 16 tiles wide, tileable end to end along the leg, so several strips join into a long leg with no seam. On top: the region's ground, its woods as simplified canopy masses (individual trees are not needed at this distance), the road. Along the side facing the camera: the cliff face dropping to the next leg below, 512 px tall (four of our 128 px block rows), in the region's rock.
b) The two hairpin ends of a leg: distant_<region>_turn_left.png and distant_<region>_turn_right.png (the leg turning back on itself, the road going round, the cliff face continuing).

ALSO
c) The valley floor far below the whole pass: valley_floor.png, a seamless tile of about 1024 x 512 (fields, woods, a river, at a great distance). This replaces the flat green seen past the cliffs today.
d) The castle at the bottom of the pass, seen from above at the same distance as the last leg: distant_castle.png. Please match the castle already in the game's horizon (Art/Placeholder/View/castle.png) in design.

POINTS.JSON (pixel coordinates on each canvas, y down)
- leg strips: the leg's centre line where a strip starts and ends (start, end; strips join there), and the top edge of the cliff (two points)
- hairpin ends: where the leg's centre line enters and leaves, and the cliff's top edge
- the ground plane's height on the canvas (the leg surface's y at the centre line)

DELIVER: the PNGs, points.json, and a contact sheet assembling the pass the way Kevin drew it: a leg in front, two legs zigzagging below it, the castle at the bottom, the valley floor round it all.

CHECK BEFORE SENDING
[ ] Our isometric view and 256 px per unit; the leg strips tile end to end without a seam.
[ ] Every cliff face is 512 px tall, facing the camera.
[ ] Each region reads at a glance in its colours (meadow, river, dark wood, fen, highland rock, castle road).
[ ] Flat v4 style, light from the top-left, no outlines, no characters.
```

---

**For Claude when it lands (decided with Kevin, 2026-10-16):**
- **Layout.** Rework `RidgeLayout`: long legs alternating along the two iso axes with hairpin turns. The six camps sit mid-leg at the outer edge, keeping the bay idea (each camp looks over the drop). The hairpins are the gates: trees, rock, Bridge, Causeway, the sealed gate.
- **Drawing lower.** Leg *k* is drawn 4 block rows (2 world units) lower per leg. All of a leg's tiles, trees, blocks and props are shifted on screen, and physics stays flat (the same trick as the wagon's raised floor). Sort a leg below the legs before it.
- **Walking round a hairpin** eases the player's drawn position and the camera down one step.
- **What renders as real ground:** only the current leg and the next one. Legs two or more away use Astra's strips, hazed toward the valley colour by distance. The valley floor replaces the flat green behind everything.
