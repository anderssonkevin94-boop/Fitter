# Brief for Astra: the valley view, the castle and the cliff edge

Kevin's direction (2026-10-15):
- The town sits in a clearing, with forest and hills all around it.
- The north edge of the land is a cliff. Past it you look out over a deep valley to a big, majestic castle in the distance.
- Hills and trees in between move in parallax as you walk.
- Adventurers climb a path up the cliff into town, and you watch them head back down toward the castle.

Concept sketch (layout only, not style): `Docs/Concepts/world_layout_v1.png`.

---

```
Hi Astra, next set: the VIEW. Kevin wants the town to sit in a forest clearing with a cliff along its north edge. Past the cliff the land drops away into a deep valley, and far across it stands a big, majestic castle: the castle that posts the jobs the adventurers take. Layers of hills and trees between the cliff and the castle move at different speeds as the player walks (parallax), so the view feels deep. It should be the most beautiful thing in the game: a reason to stand at the edge and look.

You have full visual freedom in your v4 style: flat colours, one shadow tone, no painted outlines, light from the top-left. The functional requirements are below.

HOW THE GAME SHOWS IT
- The camera looks down at the town from the south (isometric), so "far away" is UP the screen. The cliff runs along the two top edges of the land (one rising to the left, one rising to the right), meeting at a point at the top.
- The cliff's rock face faces away from the camera, so the player sees the grassy LIP of the cliff and then, beyond it, the valley view.
- The view is a set of separate wide layers stacked from far to near. Each layer slides sideways and up and down by a different amount as the camera moves (far ones slowly, near ones faster).

A) VIEW LAYERS (each a separate transparent PNG, 4096 px wide, seamless left to right so the game can repeat it sideways)
1. view_sky: sky colour bands and a few flat clouds. Fully opaque. 4096 x 1024.
2. view_far: distant mountains or hills, pale and bluish (atmospheric distance), with a ridge the castle will stand on. Transparent above the silhouette. 4096 x 1024.
3. view_mid: the valley's farther hills and woods, with a winding road leading toward where the castle stands. Transparent above. 4096 x 1024.
4. view_near: the valley floor below the cliff: treetops, a river or fields, the bottom of the road. Darker and more saturated (closer). Transparent above. 4096 x 1024.
5. view_front: a few big, dark treetops or rocks that rise just past the cliff lip and frame the view. Mostly transparent. 4096 x 512.
Each layer should read on its own and also together, with clear value steps: far = pale, near = rich.

B) THE CASTLE (separate, not part of a layer, so the game can place it on the far ridge)
- castle: big and majestic, seen from far away: towers, banners, a gatehouse. In the far layer's palette (pale, slightly blue with distance) but clearly the focal point (a warm accent, e.g. banners or lit windows). About 1200 x 900 px, transparent, ground point (the bottom centre where it meets the ridge) in points.json.

C) CLIFF EDGE PIECES (on the same grid as the shop: 256 px per unit, one tile edge = 128 px across and 64 px up or down)
- cliff_lip_left and cliff_lip_right: the grassy brink along one tile edge, for the two north edges (rising to the right / rising to the left). Show the edge of the grass, the top of the drop, a few roots, rocks or tufts hanging over. They repeat seamlessly along the edge. Canvas 176 x 220, anchor at the edge's midpoint on the ground line (88,172), like the low-wall template.
- cliff_lip_corner: the north tip where the two edges meet. Canvas 128 x 220, anchor at the tip (64,172).
- cliff_path_top: the top of a path down the cliff along one edge (a few steps cut in the rock, a post or rope rail), on the left-edge canvas. Adventurers walk over it and disappear down it.
- Optional: 2-3 cliff_lip variants per direction (a boulder, a small shrub) so long edges don't look repeated.

D) A TINY TRAVELLER (optional)
- valley_walker: a small adventurer figure about 40 px tall, in the mid layer's palette. The game moves it along the painted valley road, so you see departing adventurers walk toward the castle.

DELIVER: PNGs, points.json (castle ground point, cliff anchors), and a contact sheet showing the town's cliff edge with the layers stacked behind it, the castle on the far ridge and the character standing at the lip.

CHECK BEFORE SENDING
[ ] Layers repeat seamlessly left to right; far is pale, near is rich; each is transparent above its silhouette.
[ ] The castle is clearly the focal point: majestic, but reading as far away.
[ ] Cliff lip pieces join seamlessly along an edge and at the corner; the anchors are exact.
[ ] v4 style: flat colours, no outlines, light from the top-left.
```

---

**For Claude when it lands.**
- **Layers.** A parallax backdrop parented to the camera: each layer offsets by its own fraction of the camera's position, and repeats sideways.
- **Castle.** It sits on the far layer.
- **Cliff.** Lip pieces line the north edges (they block walking); the path top marks where adventurers come and go.
- **Valley walker.** It follows a spline on the mid layer.
