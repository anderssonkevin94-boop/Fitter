# Art prompts for Astra: the supplier (player character)

Three steps:
1. **Design.** Three options on one sheet; Kevin picks one.
2. **Model sheet** of the picked design. This is the approval step, and it becomes the reference for every frame.
3. **Animation strips.** Run these only after the model sheet is approved.

Attach the style reference (the cozy isometric forest-and-mine screenshot) to every prompt. From step 2 on, also attach the approved model sheet. Save everything to `art-staging/ravenholm-character-v1/`, using the file names given below.

## Who the character is

The supplier runs a small supply company in a cozy fantasy frontier town. They mine ore and chop wood themselves, carry goods on their back, and supply the heroes and heroines who come asking. **They never fight**: no weapons, no armour. They are practical, friendly and hard-working.

- **Look:**
  - sturdy work clothes: a tunic or shirt with rolled sleeves, a leather vest or work apron, trousers and boots;
  - a red neck scarf as the signature colour;
  - a small leather backpack.

  No hat, so the hair and face read at small size.
- **Proportions:** chibi, like the boy in the reference, about 2.5 heads tall: big head and readable face, short sturdy body, big hands and boots.
- **Size in game:** the character must read at only **128 px tall**. Use bold shapes, few small details, and a strong silhouette.

## Rules to paste at the start of every prompt

> Character art for a 2D isometric mobile game. Match the art style of the attached style reference exactly: cozy hand-painted cartoon, clean dark-brown outlines about 3 px thick, soft cel shading, warm earthy palette, light from the top-left. Chibi proportions, about 2.5 heads tall.
>
> Background: flat solid pure #FF00FF magenta everywhere, with no gradient, texture, ground, shadow or scenery. No text, labels, numbers or watermark. Every figure is complete and uncropped, with wide empty margins; nothing touches another figure or the image edge.

## Step 1: three design options (`design-options.png`)

> [rules above] One sheet, 3 columns × 1 row: three different design options for the same game character, each standing in a relaxed neutral pose, full body, facing three-quarter front toward the bottom-left of the image (the isometric camera angle). The character is a friendly young supplier/merchant who mines and chops wood: practical work clothes with rolled sleeves, a leather vest or apron, sturdy boots, a red neck scarf, and a small leather backpack. No weapons, no armour, no hat.
>
> Option 1: a young man with messy brown hair. Option 2: a young woman with a short ponytail. Option 3: an androgynous character with a bob haircut and freckles. Same height and scale for all three, feet on the same baseline.

**Kevin picks one** (or a mix: "option 2's face with option 1's clothes"). Then go to step 2.

## Step 2: model sheet of the chosen design (`model-sheet.png`), the approval step

> [rules above] Attach design-options.png. A character model sheet of option [N] [plus any changes], 4 columns × 1 row, all the same height and scale, feet on one shared baseline:
> 1. three-quarter FRONT view facing the bottom-left (toward the viewer, isometric angle);
> 2. three-quarter BACK view facing the top-left (away from the viewer, isometric angle);
> 3. full side view facing left;
> 4. a large head close-up, three-quarter front, with a friendly expression.
>
> Below, as separate small objects with space between them: the pickaxe, the woodcutting axe and the backpack, each drawn alone.

When Kevin approves the model sheet, it is the reference for every frame.

**Approved by Kevin (2026-10-12): `model-sheet.png`.** The design is the older tailor:
- swept-back brown hair with grey temples and a short beard;
- a terracotta coat with cream cuffs, an oatmeal bib apron and sheathed scissors at the hip;
- dark trousers and tan shoes.

It replaces the young-supplier description above. **No backpack in any animation.**

## Step 3: animation strips (after approval)

**One strip per file**: a single horizontal row of frames for one animation in one facing. A strip like this keeps the frames consistent with each other.

> [rules above] Attach model-sheet.png and say: "This exact character, same design, colours, proportions and scale in every frame." An animation strip: [FRAMES] frames in ONE horizontal row of equal-width cells, left to right in playback order, showing [ANIMATION] in the [FACING] view.
>
> Critical for the game engine: every frame has the character at exactly the same scale, with the feet on exactly the same baseline, and the body centred on the same vertical line in every cell (only the limbs and tool move). Leave enough room in each cell for the full tool swing; nothing is cropped. No motion lines, no dust and no effects (the game adds those).

Facings:
- **FRONT** is three-quarter front, facing the bottom-left.
- **BACK** is three-quarter back, facing the top-left.

The game mirrors these for the right-hand directions, so never draw right-facing versions.

| File | Animation (what to write for [ANIMATION]) | Facing | Frames |
|---|---|---|---|
| `idle_front.png` | standing idle, gentle breathing, a blink in frame 3 | FRONT | 4 |
| `idle_back.png` | standing idle, gentle breathing | BACK | 4 |
| `walk_front.png` | a full walk cycle (contact, down, passing, up, for each leg) | FRONT | 6 |
| `walk_back.png` | a full walk cycle | BACK | 6 |
| `mine_front.png` | swinging the pickaxe at a wall in front of them, at chest height: wind-up overhead, swing, impact, recoil, back to ready | FRONT | 5 |
| `mine_back.png` | the same pickaxe swing at a wall in front of them | BACK | 5 |
| `mine_down_front.png` | swinging the pickaxe straight down at the ground at their feet: wind-up, swing, impact, recover | FRONT | 4 |
| `mine_up_back.png` | striking the pickaxe straight up at the rock above their head: crouch, strike up, impact, recover | BACK | 4 |
| `chop_front.png` | chopping a tree beside them with the axe, a horizontal swing: wind-up, swing, impact, recoil, ready | FRONT | 5 |
| `chop_back.png` | the same horizontal axe chop | BACK | 5 |
| `climb_back.png` | climbing a ladder, seen from behind; hands and feet alternate on the rungs (no ladder drawn) | BACK | 4 |
| `pickup_front.png` | bending to scoop something up from the ground with both hands and straightening into the carry pose (arms out in front, forklift-style) | FRONT | 3 |
| `carry_idle_front.png` | the carry pose standing: both forearms held straight out in front at waist height, palms up, like a forklift, leaning back slightly as if under a heavy load; breathing | FRONT | 4 |
| `carry_idle_back.png` | the same carry pose standing | BACK | 4 |
| `carry_walk_front.png` | a walk cycle in the carry pose: arms locked out in front like a forklift, leaning back slightly, short heavy steps | FRONT | 6 |
| `carry_walk_back.png` | the same carry walk | BACK | 6 |
| `fall_front.png` | frame 1: falling (arms up, legs bent); frame 2: landing crouch | FRONT | 2 |

That is 77 frames in 17 strips.

**Carrying.** In the carry strips the hands stay **empty**: the game draws the pile of goods resting on his forearms, and the pile grows with what he carries (up to a huge stack). So keep the forearms level and clearly visible, at the same height in every frame. Do not draw anything on them.

## Tips for Astra

- Run each strip a few times and keep the most consistent one. Reject a strip if the character's size or face drifts between frames.
- If the feet do not line up, ask: *"Same strip, but the feet stand on exactly the same horizontal line in every frame, and the character is the same size in every frame."*
- If the tool is cropped, ask: *"Same strip with wider cells, so the whole pickaxe/axe swing fits inside every frame."*
- Keep the strips' resolution consistent across files.

## What happens with the results

Claude will:
- cut each strip into frames and key out the magenta;
- line the frames up on a shared feet point, correcting any drift;
- move the game's carry pile from on top of his head onto his outstretched forearms;
- scale to the game's size and build the animations: the town walk, mining in the Mine, chopping, the ladder;
- replace today's single bobbing sprite.

## Redo: the four walk strips (2026-10-12)

The first walk strips did not alternate legs (the same foot led in most frames), so the game plays only two of their poses with a bounce for now. To redo them, use one strip per file, **4 frames**, with every pose spelled out. Attach `model-sheet.png` and paste the rules block.

> [rules above] This exact character from the attached model sheet, same scale in every frame, feet on one baseline, head at the same height and over the same spot in every frame (only the legs and arms move). One horizontal strip of 4 equal cells, a classic 4-pose walk cycle, [FACING] view:
> 1. CONTACT: LEFT foot forward, heel touching the ground; RIGHT foot behind on its toes; RIGHT arm forward, LEFT arm back.
> 2. PASSING: LEFT foot flat under the body taking the weight; RIGHT knee lifted, passing beside it; arms by the sides.
> 3. CONTACT: RIGHT foot forward, heel touching the ground; LEFT foot behind on its toes; LEFT arm forward, RIGHT arm back (the mirror of pose 1 in the legs).
> 4. PASSING: RIGHT foot flat under the body; LEFT knee lifted, passing beside it; arms by the sides.

Files:
- `walk_front.png` (FRONT) and `walk_back.png` (BACK);
- `carry_walk_front.png` and `carry_walk_back.png`: the same four poses, but both forearms stay held straight out in front, level and empty (forklift), with a slight lean back.

### Pose guides for the walk redo

`ArtSource/guides/walk_cycle_guide.png` and `ArtSource/guides/carry_walk_cycle_guide.png` are stick-figure guides, made for this project, of the four walk poses. They use the same 4 equal cells, a green ground line, a blue head-top line and a red body-centre line. Blue limbs are his LEFT and orange his RIGHT. Attach the right guide together with `model-sheet.png` to each walk strip.

---

## Redesign v2: a readable supplier (2026-10-14)

Why: the painted dwarf melts into the world. His brown leather, olive trousers and brown boots match the dirt and tree trunks, and his soft painterly rendering doesn't match the world's flat, outlined style.

Attach a game screenshot (forest and town) as the style reference with prompt 1. Iterate on prompt 1 until the design is right, then run prompt 2 with the chosen sheet attached.

### Prompt 1: design sheet

> Character design sheet for a cozy isometric mobile game. Match the art style of the attached screenshot exactly: bold, even, dark-brown outlines; flat colours with one soft shadow tone and a small highlight; chunky, simple shapes; no texture noise, no fine detail. The character must stay readable when he is only 100 pixels tall.
>
> **Character:** a stocky, cheerful older dwarf who runs a supply business: mining, chopping wood, hauling goods.
>
> **Proportions:** a big head (about a third of his height), broad shoulders, a barrel chest, short sturdy legs, big boots and big gloved hands.
>
> **Clothes and colours:**
> - A rolled-up knit miner's beanie in bright orange (#F0672E).
> - A big fluffy white beard (#F7F3EA), bushy white eyebrows and a round rosy nose.
> - A cobalt-blue short-sleeved work tunic (#3D72D6).
> - A cream canvas apron with one big front pocket (#F4E3B5).
> - A dark-brown belt with one chunky gold buckle.
> - Charcoal trousers, chocolate-brown boots and tan leather gloves.
>
> No brown or green clothing: he must pop against green grass, tan dirt and dark forest.
>
> **Layout:** a plain light-cream background with the same character shown 4 times in a row, all full body, the same size, feet on one baseline:
> 1. front three-quarter, facing down-right
> 2. side profile, facing right
> 3. back three-quarter, facing up-right
> 4. straight front
>
> No tools, no backpack, no text, no labels, no ground shadow.

### Prompt 2: eight-direction turnaround (once the design is chosen)

> Using the attached character sheet as the exact design (same proportions, colours and outline weight), draw the character standing in 8 directions in one row, on a flat magenta (#FF00FF) background. Order: facing down (toward camera), down-right, right, up-right, up (away from camera), up-left, left, down-left. The camera is elevated, looking down at about 30°, like an isometric game. Every pose has the same height and scale, with feet on one shared baseline, arms relaxed at the sides and legs slightly apart, so each leg reads separately from hip to boot. No shadows, no text.

### Prompt 3: walk parts (for the bending-knee walk)

> Using the same character, draw only his parts, separated with space between them, on flat magenta (#FF00FF), for the side profile facing right: torso with head (no arms, no legs), near arm, far arm, near thigh, near shin with boot, far thigh, far shin with boot. Each part must overlap its neighbour at the joint by a little (round joint caps at the hip, knee and shoulder) so they can be rotated in a game engine. Same style, scale and outline as the sheet.

Notes:
- Ask for changes one at a time ("make the beanie smaller", "the beard longer") and keep re-attaching the best sheet. Image generators drift when asked to change several things at once.
- When something is good, send me the files: I import the turnaround, rig the parts and build the walk.
