# Brief for Astra: the supplier, redesigned for rubber-hose animation

Attach with this brief:
- a game screenshot (town and forest);
- `Captures/hose_sheet.png` (Claude's preview: your dwarf with noodle legs);
- `ArtSource/dwarf-v5-approved/dwarf-idle.png` (the current dwarf).

## Why we are changing

- The walk has never felt right with 5 pre-drawn frames: the feet slide, and there are too many steps or too few.
- We are switching to **rubber-hose limbs**: arms and legs are bendy tubes that the game draws live every frame, so the walk is smooth at any speed and the feet stay planted.
- Kevin isn't sold on the current design either, so this is also a chance to redesign the character.

**How the work is split:**
- You design how he looks and draw the static body pieces.
- The game draws the arms and legs and does all the animation. You do not animate anything.

## This task, phase 1 only: three concepts

Deliver one image with **3 concept characters** side by side, then stop and wait for Kevin's pick.

1. **Evolved dwarf:** the current supplier (orange beanie, white beard, blue shirt, cream apron) reworked for rubber hose.
2. **Fresh concept A:** a new supply-shop character of your choice.
3. **Fresh concept B:** another new character, as different from A as possible (shape, age, species, mood).

For each concept, show:
- a front three-quarter view, standing;
- a side view, walking mid-stride, so we can see the noodle legs bend;
- a front view, arms out holding a stack of crates in front of the chest (the carrying pose).

## Design rules for all three

**Style.**
- Match the world: bold, even dark-brown outlines, flat colours with one shadow tone, chunky simple shapes, no fine detail.
- Rubber hose means 1930s cartoon construction: round, simple body shapes, long bendy limbs with no elbows or knees, and big hands and boots as the expressive shapes.

**Proportions.**
- A big head, about a third of his height.
- A simple, solid body: bean, pear or barrel shape.
- **Visible legs, at least a quarter of his height.** The hose has to show; the current dwarf's legs are hidden by his apron.
- **Big gloves and big boots.** These are what players read from far away.

**Readability on a phone.**
- He is about 100 px tall on screen.
- One loud signature colour the world doesn't use (the world is green, brown, tan and grey), plus one light area.
- No brown or green as a main colour.
- The silhouette must read as a solid shape against both grass and dirt.

**Keep it simple.** No pouches, keys, straps, buckles or texture. Anything under about 10 px at game size disappears.

## Technical limits to design within

Phase 1 is concepts only; these limits are here so the designs can be built.

- **Limbs are drawn by the game,** as smooth tubes with round ends and a dark outline matching yours. Each limb is **one flat colour**, plus an optional thin highlight stripe.
- **No sleeve or trouser patterns on the limbs,** and no cuffs or rolled-up legs in the middle of a limb.
- **Cuffs and boot tops go on the pieces you draw:**
  - a sleeve or shoulder ends at the torso;
  - a sock or boot top belongs to the boot;
  - a glove cuff belongs to the hand.
- **The arms must be separate from the body.** In the current dwarf they are painted into the body, which is why the preview only has noodle legs.

## Phase 2 (later, after Kevin picks a concept): the parts list

For your planning only; don't make these yet.

- **Views:**
  - 5 drawn views: S (toward camera), SE, E (side), NE, N (away).
  - The other 3 are mirrored by the game, so the design should work mirrored; nothing asymmetric that matters, like text.
- **Body:** one piece per view, containing the torso and head (or head as its own piece, so it can bob), with the attachment points marked: left and right shoulder, left and right hip, and the neck if the head is separate.
- **Hands (gloves):** open, gripping a handle (for the pickaxe and axe) and pointing, each in 3 views (front, side, back) at most.
- **Boots:** one per view, flat on the ground, with the ankle point marked.
- **Format:**
  - transparent PNGs on a 512 × 512 canvas;
  - the ground point at (256, 444) on every piece;
  - the same scale for every piece;
  - attachment points as pixel coordinates in a JSON file.
- **Tools:** a pickaxe and an axe, as separate pieces drawn in the same style.

## Acceptance checklist for phase 1

- [ ] One image with 3 distinct concepts, each shown in the 3 poses above.
- [ ] The world's outline and flat-colour style.
- [ ] Bendy limbs with no elbows or knees, one colour per limb, and big gloves and boots.
- [ ] Legs clearly visible (at least a quarter of his height).
- [ ] A loud signature colour and a light area; no brown or green as main colours.
- [ ] No small details.
