RAVENHOLM — SWITCHBACK PASS ART PACK
26 final PNGs, placement data, source paintings, assembly examples

START HERE
assets/ contains the 26 importable images. sources/ contains the original
paintings, not runtime sprites. previews/ contains review images, not sprites.
points.json is the authoritative placement data. preview-layout.json records
the exact source-pixel placement used for the three-leg assembly.
The PNGs were created with the built-in image generation tool, then registered,
projected, palette-reduced and assembled deterministically. Prompt records are
in generation-prompts.json. No live game files were changed.

CONTENTS
Six regions: meadows_edge, river_bend, hollow_wood, witchlight_fen,
crown_highlands, castle_approach.
Each has leg_upright, leg_upleft, turn_left and turn_right PNGs.
Plus valley_floor.png and distant_castle.png.

SCALE / COORDINATES
One source tile projects to a 256 x 128 diamond, 256 pixels per Unity unit.
The isometric projection before visual lowering is:
  screen x = 128 * (tile x - tile y)
  screen y = -64 * (tile x + tile y)
points.json uses top-left-origin pixel coordinates, y down. Coordinates refer
to geometric pixel edges, so a point may equal the canvas width or height.
All straight sprites are 3072 x 2048, exactly 8 tiles long and 16 tiles wide.
Upright advances along +x. Upleft advances along +y.
The visible cliff face extends 512 screen pixels vertically below its top.

STRAIGHT REPEATS
Place copies by end-to-start alignment, never by their rectangular bounds:
  next_top_left = current_top_left + current.end - next.start
For identical strips the repeat steps are (1024,-512) upright and
(-1024,-512) upleft. Blank parts of the rectangular canvas are intentional.
Do not set the leg PNG itself to Repeat and stretch it over a rectangle.
The source terrain and cliff repeat borders match exactly. The repeat preview
shows three source sprites joined along each axis.

TURNS AND THE VISUAL DROP — IMPORTANT
turn_left accepts an upright strip and exits to an upleft strip.
turn_right accepts an upleft strip and exits to an upright strip.
These are left/right turns while travelling toward the castle; the left turn
bulges to the right of the screen, and the right turn bulges left.
They are 90-degree turns in tile space, making the requested screen zigzag.

Each turn is a quarter-annulus: inner radius 8 tiles, road radius 16 tiles,
outer radius 24 tiles. This keeps the whole 16-tile-wide incoming and outgoing
cross-sections intact. Turns are larger than a single straight section.

The turn's 512-pixel visual descent is ALREADY PAINTED into the sprite, easing
from 0 at entry to 512 at exit with h(t)=512*(3*t*t-2*t*t*t).
Physics remains flat. Keep the current leg's incoming draw offset on the turn.
Align the following strip's start to the turn's exit; do NOT add another 512px
when using those already-lowered anchor coordinates.
If calculating from flat tile positions instead, apply 512px * leg_index to
each leg, and the turn's local easing between them, exactly once.

Unshifted tile-space turn definitions, relative to that turn's origin:
  left:  centre (0,24), entry (0,8), exit (16,24)
         road(t) = (16*sin(pi*t/2), 24-16*cos(pi*t/2))
         next strip origin = (8,24), next axis +y
  right: centre (24,0), entry (8,0), exit (24,16)
         road(t) = (24-16*cos(pi*t/2), 16*sin(pi*t/2))
         next strip origin = (24,8), next axis +x
The flat entry-to-exit screen delta is (0,-2048). With the baked drop it is
(0,-1536). This is also directly checkable from the PNG anchor coordinates.

points.json includes 33 centerline samples, both full-width connection edges,
and the separate visible outer/inner cliff polylines. The scalar ground-plane
y is the road midpoint; use the sampled centerline for the changing turn y.
cliff_top_edge is an endpoint summary, not a straight line across a curved
turn. Use cliff_top_polylines to trace the actual visible curved edges.

UNITY IMPORT / ASSEMBLY
Sprite (2D and UI), Single, PPU 256, Full Rect mesh, no automatic trimming.
Max Size 4096 preserves every supplied final sprite. Initially use no lossy
compression and Bilinear filtering. Keep module textures Clamp; preserve alpha.
Atlas them only with edge extrusion/padding so filtering does not create gaps.
Valley can use Repeat as a background texture; its full canvas is opaque.

A pivot at a start/entry anchor is (anchor.x/width, 1-anchor.y/height).
When using a different pivot, convert the recorded top-left coordinates before
placing it. Do not recenter trimmed turns by eye. Their JSON coordinates already
account for their individual crop offsets.

Render the valley behind everything. Render farther legs before nearer legs.
Only the current and next leg should be real game tiles/props; substitute these
painted modules for legs two or more away. The assembly preview uses painted
modules on all three legs to demonstrate the delivered art and its joins.
Apply distance haze toward the valley color after selecting the proper region.
The sample assembly uses Meadow, River and Castle Approach; the separate
all-regions sheet shows all six regions and all 24 modules.

Castle: use gate_anchor at the foot of the entrance ramp to meet the road.
The cream walls, slate-blue roofs and orange banners follow the existing game
castle. It is a landmark painting; no castle collision or playable interior is
provided. ground_anchor is the front foundation corner, not the gate.

CHECKED / REMAINING
26 RGBA final PNGs; exact straight-section dimensions; exact matching terrain
and cliff repeat borders; seamless valley borders; all turn connection edges
match a 16-tile strip; 512px cliff geometry and baked turn descent.
Review previews/strip-repeat-check.jpg, both joins images, pass-assembly.jpg,
all-regions.jpg and valley-repeat-check.jpg. validation.json records the checks.
The live Unity scene, camera easing, physics, sort settings and transitions
between different biome paintings still need Claude's integration and playtest.
No Unity runtime validation has been performed here.
