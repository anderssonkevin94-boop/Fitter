# Brief for Astra: carried-item stack sprites (Sets 3 and 4, 41 sprites)

Every item is marked with one of three tags:
- **[GAME]** means the game's code does this today, and the art has to work with it.
- **[SUGGESTED]** is my recommendation. Change it if you have a better, consistent rule.
- **[OPEN]** is undecided. Please propose a specification.

Attached: `brief_carry_pile_ingame.png`, the current placeholder pile on the approved dwarf.

## Style

You already have it: chunky 2D isometric shapes, bold dark-brown outlines and simple flat colours, matching the approved dwarf and the town. Everything has to read when small; see "Placement" for the on-screen size.

## 1. Files

- **[GAME]** One PNG per item, not an atlas. Each item has its own sprite, and I wire each file to its item.
- **[GAME]** Filenames are `stack_<id>.png`, with the 41 ids below. They go in `Art/Placeholder/Stacks/`; send them in a flat folder and I'll place them.
- **[GAME]** The current placeholders are 64 × 48 px at 128 px per world unit. That puts each piece at 0.5 × 0.375 units, before the pile scales it up 1.25×.
- **[SUGGESTED]** Draw on a **256 × 192 px** transparent canvas, 4× the old size, for sharp phones. I import at 512 px per unit, so the in-game size stays the same. Keep the same canvas for all 41.
- **[SUGGESTED]** Also send one contact sheet of all 41 on a mid-grey background, for review.

**Raw materials (19):** `arcane_dust`, `cedar_log`, `cedar_lumber`, `coal`, `copper_ingot`, `copper_ore`, `fur`, `gem`, `herb` (Meadow Herb), `iron_ingot`, `iron_ore`, `ironwood_log`, `ironwood_lumber`, `maple_log`, `maple_lumber`, `moonsilver_ingot`, `moonsilver_ore`, `rope`, `steel_ingot`.

**Crafted items (22):**
- **Weapons:** `iron_dagger`, `iron_sword`, `steel_sword`, `steel_stiletto`, `steel_maul`, `moonsilver_blade`, `cedar_bow`, `maple_longbow`, `ironwood_greatbow`, `cedar_staff`, `copper_ward_staff`, `moonsilver_focus`.
- **Armour:** `fur_brigandine`, `riveted_coat`, `steel_coat`, `moonsilver_mail`.
- **Potions:** `antidote`, `recovery_tonic`, `strong_tonic`, `warding_draught`, `trapsense_draught`, `pathfinder_draught`.

## 2. Placement

- **[GAME] Pivot:** bottom centre, 1/12 of the height above the canvas bottom (pivot 0.5, 0.0833). Each piece "sits" on that point. Please keep the bottom of the object on that line.
- **[GAME] Size on screen:** a piece is about 0.62 units wide. The dwarf is about 1 unit tall, so a piece is roughly two thirds of his shoulder width, or about 80 px on a phone screen.
- **[GAME] How the pile is built:**
  - One piece per unit carried. Each piece sits 0.16 units above the one below; that's about a third of a piece's height, so pieces overlap heavily.
  - Pieces alternate slightly left and right (±0.02 units) so the pile looks hand-stacked.
  - Up to 8 pieces are drawn. The carry holds 12, and a number badge above the pile shows the count.
  - **This means the top third of every sprite is what shows in a pile.** Each item needs a recognisable top edge and silhouette, and a flat-ish footprint so mixed piles stack tidily.
- **[GAME] Where the pile sits now:** on top of his head, just above the beanie, because the approved dwarf has no carrying pose yet.
- **[GAME] Layering:** pieces draw in front of his body, each later piece over the one below.
- **[SUGGESTED]** When the carry walk from Set 1 exists, the pile moves onto his forearms in front of his chest. The animation marks the anchor point for each frame. Walking away from the camera, the pile goes behind his body. The game already supports both. Design the pieces to work in either place.
- **[GAME] Small motions:** when he's near a drop-off that accepts an item, those pieces bounce a little and grow 10%. On delivery, a piece flies in an arc to its target, shrinking to 60%.

## 3. Directions

- **[GAME]** One sprite per item for every facing: never mirrored, never rotated. Draw each piece in a neutral isometric view that looks right whichever way he faces (seen from the front-left, lying flat or upright as fits the item).

## 4. Quantities

- **[GAME]** One fixed sprite per item, with no stack-size variants: the pile is made by repeating the sprite once per unit.
- Big ground piles (`pile_logs`, `pile_ore` and so on, in small, medium and large) are separate props in Set 7, not part of this task.

## 5. Current implementation (summary)

From `CarryStackView.cs`:

```csharp
public const int MaxVisiblePieces = 8;                       // up to 8 drawn; the count badge shows the rest
[SerializeField] private float pieceStep = 0.13f;            // height between pieces (before the 1.25 scale)
[SerializeField] private float pieceScale = 1.25f;           // the whole pile is drawn 1.25x
// each frame, per piece:
t.localPosition = new Vector3(i % 2 == 0 ? -0.02f : 0.02f, i * pieceStep + bob, 0f);
t.localScale = Vector3.one * (match ? 1.1f : 1f);            // a drop-off that accepts it: a little bigger
// where the pile sits: the forearm anchor of a carry pose if the frame has one, else just above the head
_stackRoot.localPosition = onArms ? _animator.CarryAnchor.Value : new Vector2(0f, _animator.HeadTop - 0.03f);
```

## 6. Other uses of the same sprites

- **[GAME]** The same `stack_<id>` sprite is also used:
  - **on the display racks** in town (crafted items, at 0.9× scale, set in slots);
  - **in containers and staging piles** (rows of pieces, the same 0.13 step);
  - **in the pack of the supplier's figure** when a routine runs a site;
  - **as the flying piece** on delivery.
- **[OPEN]** Inventory and UI icons: the game has no item icons in its menus yet. A 256 × 256 square icon per item could share this art (re-framed and centred). Please propose whether to make them separate or derived from the stack art, but deliver only the stack sprites in this task.

## Acceptance checklist

- [ ] 41 PNGs named exactly `stack_<id>.png`, on identical transparent canvases.
- [ ] The object stands on the pivot line (bottom centre, 1/12 up), with nothing below it.
- [ ] Each item is recognisable from its top third alone, and logs, lumber, ore and ingots are told apart by colour and shape.
- [ ] They match the dwarf's outline weight and flat colour.
- [ ] A contact sheet is included.
