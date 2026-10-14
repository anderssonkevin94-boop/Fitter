# Ravenholm Supply Co: art asset list for Astra (2026-10-14)

Everything in the game that is still placeholder art, in the order to make it: what players see most comes first.

## Rules for every asset

- **Style.** Match the approved supplier and the painted ground, blocks and trees: bold, even dark-brown outlines; flat colours with one shadow tone and a small highlight; chunky shapes; no fine detail. Everything must read on a phone.
- **Camera.** Isometric, 2:1. A ground tile is a diamond 256 px wide and 128 px tall. The light comes from the top-left.
- **Delivery.**
  - Transparent PNG, one file per asset (or one sheet per set with magenta #FF00FF between items), plus a contact sheet to review.
  - Name files exactly as listed below.
  - Give a ground origin (the pixel where the object touches the floor) for anything that stands on the ground, the way the dwarf package did.
- **Scale.** The supplier is about 210 px tall. A building's footprint is given in tiles.
- **One set at a time.** Deliver a set, wait for approval, then start the next.

---

## Set 1: the supplier's work animations (highest priority)

He only has a walk and a standing pose. Everything else is a code-driven lean.

- Use the same rig, 8 directions and origin (128, 222) as the approved walk, with 256 × 256 cells.
- Each clip lists its frame count and the time each frame shows (ms).

| Clip | Frames | ms | Notes |
|---|---|---|---|
| `carry_walk` | 5 | 90 | Arms forward like forklift arms, holding a pile in front of his chest. Mark the point where the pile sits on his forearms. |
| `carry_idle` | 1 | n/a | The same arms, standing. Give the same pile point. |
| `mine` | 4 | 80 | Pickaxe swung at a wall in front of him: wind-up, strike, impact, recover. |
| `mine_down` | 4 | 80 | Pickaxe swung at the ground in front of his feet. |
| `mine_up` | 4 | 80 | Pickaxe swung up at a block above head height. |
| `chop` | 4 | 80 | Axe swung at a trunk in front of him, waist height. |
| `pickup` | 3 | 100 | Bends and lifts something from the floor into his arms. |
| `climb` | 4 | 120 | Climbing up a block one step high: hands on top, knee up, stand. Only N, NE, NW and E or W are needed. |
| `celebrate` | 4 | 120 | A small fist pump, for finished deliveries. |

The pickaxe and axe are drawn in the frames, in the same style.

## Set 2: town buildings (9)

These are what players look at the most after the supplier. Each one needs:
- one building image;
- an "upgraded" version (level 2 and level 3) for the stations that upgrade;
- a small working effect, like a smoking chimney or a spinning saw, as a 4-frame loop where noted.

| File | Footprint (tiles) | Notes |
|---|---|---|
| `bld_workbench` | 2 × 2 | An open-air workbench with a canopy. L2 adds a vice and tool rack. |
| `bld_forge` | 2 × 2 | Stone hearth and anvil. L2 and L3. 4-frame fire glow. |
| `bld_smelter` | 2 × 2 | A tall clay furnace and chimney. L2 and L3. 4-frame smoke. |
| `bld_lumbermill` | 3 × 2 | Saw bench, log ramp. L2 and L3. 4-frame saw. |
| `bld_apothecary` | 2 × 2 | A potion stall with herbs drying. L2. |
| `bld_store` | 3 × 3 | The supply store: counter, shelves, sign (no text). |
| `bld_trader_pad` | 2 × 2 | A travelling trader's cart and awning. |
| `bld_mine_mouth` | 2 × 2 | Timber frame and sign post at the quarry's edge. |
| `bld_forest_gate` | 2 × 1 | A wooden arch into the forest. |

## Set 3: carried goods (19)

These are the pieces in the pile the supplier carries, and they are also shown on shelves and pallets.

- **Format:** a 128 × 96 px chunky icon, drawn so it stacks (it sits flat, seen at the isometric angle). The pile holds up to 12, so they must be readable when small.
- **Names:** `stack_<id>`.

| Group | Pieces |
|---|---|
| Logs | cedar, maple and ironwood (colour-coded: reddish, golden, dark) |
| Lumber | cedar, maple and ironwood planks |
| Ore | copper, iron, coal, moonsilver |
| Ingots | copper, iron, steel, moonsilver |
| Other | gem, herb, fur, rope, arcane dust |

## Set 4: crafted goods (22)

These are the weapons, armour and potions players make and sell. Each needs a `stack_<id>` icon (same format as set 3) and an item icon for the UI (256 × 256).

| Group | Items |
|---|---|
| Weapons | iron dagger, iron sword, steel sword, steel stiletto, steel maul, moonsilver blade, cedar bow, maple longbow, ironwood greatbow, cedar staff, copper ward staff, moonsilver focus |
| Armour | fur brigandine, riveted coat, steel coat, moonsilver mail |
| Potions | antidote, recovery tonic, strong tonic, warding draught, trapsense draught, pathfinder draught |

## Set 5: the town's people (5 contractors and 4 adventurer classes)

- **Contractors:** Bram Flint, Ivo Reed, Wren Tallow, Elin Moss, Nessa Quick. Each gets a portrait (512 × 512, head and shoulders) and a standing figure.
- **Adventurer classes:** warrior, ranger, rogue, mage. Each gets a standing figure, plus a walk in the dwarf's format if they move around town.
- Every one needs a distinct silhouette and one signature colour that isn't the supplier's blue and orange.

## Set 6: forest and quarry details

| Asset | Notes |
|---|---|
| `tree_cedar`, `tree_maple`, `tree_ironwood` | Three tree types players can tell apart at a glance, each with a stump and a felled log. |
| Stump stages | Fresh stump, a stump growing back (sapling), and a cleared stump hole. |
| `rock_copper`, `rock_iron`, `rock_stone` | Surface outcrop blocks matching the painted blocks, with ore flecks. |
| `bush_herb` | A herb bush players can pick. |
| `prop_scaffold` | A wooden scaffold you build and stand on, one block in size. |
| Block cracks `fx_crack_1..3` | Three crack overlays for half-dug blocks, transparent, fitting the block face. |
| Dust and chips | 4-frame puffs: rock dust and wood chips. |

## Set 7: town props

`prop_bell`, `prop_lamp`, `prop_pennant`, `prop_output_pad` (where finished goods appear), `receiver_zone` (a drop-off mat), `trail_marker`, `prop_beacon_trophy`, ground piles (`pile_logs`, `pile_lumber`, `pile_ore`, `pile_ingots`, small, medium and large), and display racks (`rack_weapons`, `rack_armor`, `rack_potions`, each empty and full).

## Set 8: UI kit

- Buttons, round buttons, panels (light and dark), chips, and the joystick ring and knob, all as 9-slice-friendly PNGs.
- Icons: coins, stars (reputation), the bag (carry), map, settings, a hand (interact), axe, pickaxe and build.
- A speech bubble (`ui_bubble`) for requests over buildings.
