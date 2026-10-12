# Art prompts for Astra: ground, cliff blocks and trees

These are written for an image generator. Attach the reference image (the cozy isometric forest-and-mine screenshot) to every prompt, so the style matches.

Each prompt asks for **one sprite sheet**: separate objects in a grid, on a flat background, never touching. That is what lets us cut them into individual game assets. If Astra offers a transparent background, use it. Otherwise ask for flat pure magenta (#FF00FF), which is easy to key out cleanly.

## Rules to paste at the start of every prompt

> Sprite sheet for a 2D isometric mobile game. Match the art style of the attached reference exactly: cozy hand-painted cartoon, clean dark-brown outlines about 3 px thick, soft cel shading, warm earthy palette, light coming from the top-left. True 2:1 isometric angle (every diamond top is exactly twice as wide as it is tall), same camera angle as the reference.
>
> Layout: a strict grid of equal cells, one object per cell, centred, with wide empty margins. Nothing touches or overlaps a cell edge. No text, numbers, labels, watermark or UI. No drop shadows on the background, and no scenery or ground around objects unless asked. Background: flat solid #FF00FF magenta (or transparent), with no gradient and no texture. Every object is complete and uncropped. High resolution.

## Prompt 1: ground tiles (the tops you walk on)

> [rules above] 4 columns × 3 rows. Each cell holds ONE flat isometric ground tile: a diamond seen from above at the 2:1 isometric angle, twice as wide as tall, with a thin earthy edge underneath (just a few pixels, like the edge of a board game tile). The textures must tile seamlessly when the diamonds are placed edge to edge.
>
> Row 1: lush short grass (plain); grass with a few small white and yellow flowers; grass with darker tufts; slightly darker forest-floor grass with fallen needles.
> Row 2: packed sandy dirt path (plain); dirt path with small pebbles; dirt path with faint footprints; dry brown dirt (freshly dug ground).
> Row 3: grey cobblestone paving; worn cobblestone with grass between the stones; wooden plank floor; dark mossy soil.

## Prompt 2: cliff blocks (the ground going down, and the Mine)

These are the most important. The Mine is built from small isometric cubes stacked on a grid. Each cube shows a **top** and **two side faces** (left face and right face), and is exactly as tall as its top diamond is wide ÷ 2. In other words, a perfect isometric cube.

> [rules above] 4 columns × 4 rows. Each cell holds ONE isometric cube block (a perfect isometric cube, 2:1 angle: a diamond top plus a left face and a right face, all three visible, the left face slightly lighter than the right). Chunky and readable like the cliff edge in the reference, with a subtle rounded bevel on the edges. Each block's side faces show layered earth or rock texture.
>
> Row 1 (surface): grass-topped dirt block (green grass top with a slight grass overhang, brown soil sides with small roots); plain brown topsoil block; topsoil block with small pebbles; sandy dirt block.
> Row 2 (layers going deeper): warm sandstone block with horizontal strata; red clay block; cool grey slate block with cracks; mossy green-grey stone block.
> Row 3 (deep layers): dusky purple rock block; ochre/amber rock block; dark basalt block; pale limestone block.
> Row 4 (ore blocks, plain rock with visible ore): grey stone with rusty orange iron streaks; stone with green-blue copper veins; stone with black coal chunks; stone with small sparkling gold flecks.

Optional follow-up, same rules, 4 columns × 1 row: a dark "tunnel back wall" version of the brown topsoil, sandstone, red clay and grey slate blocks (the same block in deep shadow, as if seen inside a cave).

## Prompt 3: trees and nature props

> [rules above] 4 columns × 4 rows. Each cell holds ONE object standing on nothing (no ground tile under it), seen from the same isometric angle as the reference. The base of each object is at the bottom-centre of its cell. Add only a very soft small oval contact shadow directly under the base.
>
> Row 1: tall pine tree (like the big pines in the reference); medium pine tree; small young pine; round leafy deciduous tree.
> Row 2: pine tree slightly bent; dense round bush; small bush; fern clump.
> Row 3: cut tree stump with rings; fallen log; stack of three cut logs; a single chopped log piece.
> Row 4: grey boulder; small rock pair; grass tuft; cluster of small white flowers.
>
> Scale between cells: a tall pine is about 3 times as tall as a person; a bush is about knee to waist height; a stump is knee height. Keep the same scale across the whole sheet.

## Tips for Astra

- If a sheet comes back with uneven spacing or objects touching, ask: *"Same sheet, but strictly gridded with more empty space around every object."*
- If the isometric angle drifts, ask: *"Use an exact 2:1 isometric angle for every object, matching the reference."*
- Generate each sheet a few times and keep the best one; mixing sheets from different runs can drift in style.
- Download the largest PNG available.

## What happens with the results

Put the sheets in a folder and tell me where. I will:
- cut each object out;
- remove the background;
- scale everything to the game's grid (128 px per tile width: ground diamond 128 × 64, cube block 128 × 128);
- set each pivot (tile centre, or the base of trees and props);
- swap them in for the placeholder art.
