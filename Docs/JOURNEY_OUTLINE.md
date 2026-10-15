# The Journey: design outline v1 (approved by Kevin, 2026-10-15)

2026-10-15. Approved with every recommendation in §13: six camps; no going back; wagon storage limit; a summons per camp; timed crafting; food from foraging first. Being built from step 1 (camp 1). Numbers are starting points for tuning.

## 1. The pitch

You run a travelling supply company. Your shop folds out of a wagon pulled by your trusty ox. Far across the valleys stands the castle, and getting there is the game.

You travel in **camps**. At each camp you:
- clear land;
- gather what you cleared;
- set up your stations;
- craft gear;
- kit out the adventurers who climb up from the valley with the castle's jobs.

Their success pays you, and the gold buys better tools and upgrades. Better tools break through the obstacle blocking the way down to the next camp.

Each camp's view shows the next camp below you, with the castle a little closer every time.

## 2. The loop at a camp

```
clear land (tools) ──> gather wood, ore, stone, herbs ──> stations process and craft
       ^                                                          |
       |                                                          v
better tools  <── gold, reputation <── adventurers return <── kit out adventurers
       |                                                       (the castle's jobs)
       v
break through the blocker ──> move on to the next camp
```

Two kinds of pressure keep the loop interesting:
- **Space.** Bigger stations and a bigger shop need cleared land, and clearing costs time and tool power.
- **The road ahead.** Each camp's blocker needs a tool tier, and often a build project, that you have to work towards.

## 3. The camps

There are six camps, two per region (the job regions that already exist), then the castle.

| # | Camp | Ground and new materials | Jobs (castle tier) | Blocker on the way down | To break through |
|---|---|---|---|---|---|
| 1 | Meadow's Edge | meadow and cedar woods, soft hills: cedar, copper, stone, herbs | Frontier Paths 1: patrols, surveys | Fallen giant cedars across the cliff path | Iron axe |
| 2 | River Bend | river valley, maple, clay banks: maple, iron, coal | Frontier Paths 2: watchtowers, the quarry road | A rockslide over the trail | Iron pickaxe, plus a timber retaining wall (lumber) |
| 3 | Hollow Wood | dense old forest, ironwood, fur, rope | Hollow March 1: warrens, trappers | A ravine | A bridge (ironwood lumber and rope) and a steel axe |
| 4 | Witchlight Fen | marsh and mist: fen herbs, arcane dust | Hollow March 2: fens, the sunken archive | A flooded causeway | A causeway (stone and lumber) and steel tools |
| 5 | Crown Highlands | rocky uplands: moonsilver, gems | Crownward Ruins 1: barracks, the gate | An ancient sealed gate | A moonsilver pickaxe |
| 6 | Castle Approach | the castle's foothills: everything | Crownward Ruins 2: the castle's great jobs | — | The finale: the castle's last, greatest job |

- **The finale.** Reaching the castle replaces today's Beacon campaign. The Beacon jobs become camp 6's great jobs.
- **Challenges.** The four challenge jobs stay as optional extras at their camps.
- **Variety.** Each camp introduces one or two new materials, a new tier of jobs and one new kind of terrain.

## 4. A camp's map

- **Layout.** Each camp is a hand-shaped map, about 40 × 40 tiles (to tune), with:
  - a small starting clearing where the wagon parks;
  - forest and hills all round it;
  - the overlook cliff to the north, with the path down (blocked);
  - the way up from the previous camp to the south, where you arrived.
- **Clearing.**
  - Chop trees (logs).
  - Dig through hills (stone, ore). This replaces the Mine, which is removed: digging the hills does its job.
  - Pull stumps.
  - Everything cleared becomes buildable ground.
- **Distance.** The further from the start, the tougher the ground: older trees and harder rock, which need better tools and give richer materials.
- **Seeded details.** The shape is authored, so every camp has its look. Where the ore veins run and which trees grow where is seeded, so it varies a little each game.

## 5. The wagon shop

- **On arrival.** The wagon parks in the clearing and the shop folds out. A carpet rolls out in front of the wagon, a tarp stretches over the back, and the wagon's side becomes the service counter.
- **The carpet grid.** It is the shop's floor, with furniture placed on it in build mode. This reuses the modular shop system: the grid, placement, turning, storage and upgrades.
- **No walls.** Nothing ever needs hiding: everything stays visible.
- **Wagon levels** (bought at the wagon) make the shop bigger and grander:
  - a larger carpet (4×4 → 6×4 → 6×6 → 8×6);
  - richer tarps;
  - more storage space for travelling (§7).
- **The carpet needs cleared ground**, so a bigger shop means clearing more at each camp.
- **What happens to the walled shop.** It is retired; its furniture moves onto the carpet. The wall-mounted racks get the free-standing versions in Astra's wagon brief.

## 6. Stations

The structure is as agreed:
- **Processing:** Smelter (ore → ingots), Sawmill (logs → lumber), later a Tannery (hides → leather).
- **Crafting:**
  - Forge: metal weapons, tools, helmets, body armour, shields;
  - Workshop: bows, staves, wooden shields, leather and cloth armour, the focus;
  - Apothecary: potions;
  - Kitchen: food.

How they live at camp:
- **Kits.** Stations travel as kits on the wagon. At each camp you set one up on cleared ground (its footprint, see Astra's buildings), which takes a short build.
- **Levels and upgrades** belong to the station and travel with it.
- **New stations** are bought as kits when you first need them, for example the Kitchen once food matters.
- **Loadout slots:** weapon, off-hand, head, body, and two consumables (potion or food). Two-handed weapons lock the off-hand.

## 7. Moving on

1. **Break through.** Clear the blocker: tool tier plus any build project.
2. **Break camp.**
   - Stations pack back into kits; any batches still processing finish first or are paid out.
   - The shop folds up.
   - Stock and materials load onto the wagon, up to its storage limit. Anything left over can be sold to the Trader, or left behind.
3. **The drive.** The ox pulls the wagon down the path, in a short scene with the view sliding away.
4. **Arrive.** The new camp's clearing; the shop folds out; you set up your stations again.

**What comes with you:** gold, reputation, blueprints, station kits with their levels, the wagon and its upgrades, your tools, your adventurer roster, and stock and materials up to the wagon's storage limit.

**What stays behind:** cleared land, anything over the storage limit, and the camp itself.

**No going back.** Earlier camps' materials still reach you through the Trader. This keeps the journey moving forward and keeps saves simple.

## 8. The castle's jobs

- **As today:** the castle posts, an adventurer climbs the path to your shop, you lend the gear, and they pay you a share of the reward on return.
- **New:**
  - Each camp has its own job tier (its region and difficulty), so the closer you get, the bigger the jobs and their pay.
  - Each camp has one **summons**, a key job for the castle. Succeeding at it is the second condition for moving on, alongside the blocker. That makes jobs part of the way forward, not just income. (Optional, see open questions.)
  - Job and adventurer levels scale with the camp.

## 9. Time away

As today:
- processing continues for up to 8 hours;
- adventurers finish their jobs;
- nothing is gathered for you.

## 10. What changes, what stays

- **Kept:**
  - the supplier and the citizens;
  - gathering by hand: chopping and digging, ore hidden in the rock;
  - the forest and hills;
  - crafting and processing;
  - the castle's jobs: resolver, risk, forecasts, the share;
  - the Trader;
  - blueprints;
  - furniture art and the shop grid;
  - the valley view.
- **Removed:**
  - the Mine (the quarry and the earth block under the town);
  - the fixed town plot;
  - the walled shop;
  - the Beacon campaign wording (its jobs stay).
- **New:**
  - camps and moving between them;
  - the wagon shop;
  - station kits and placing stations;
  - blockers;
  - the camp tier of jobs and the summons;
  - wagon storage;
  - per-camp views;
  - the crafting expansion (slots, Workshop, Kitchen, food).

## 11. Building it, step by step (each step playable on your phone)

1. **Camp 1.** Replace today's town with camp 1:
   - the starting clearing, the forest and hills ring, the overlook and the blocked path;
   - remove the Mine; ore from the hills;
   - stations stand where they are for now.
2. **Placing stations.** Stations are placed on cleared ground (footprints, placement, building them).
3. **The wagon shop.** With placeholder art until Astra's arrives: the carpet grid, the wagon counter and wagon levels.
4. **Moving on.** The blocker, breaking camp, the drive and arriving at camp 2. Camp 2's map and view (the castle closer).
5. **Job tiers and summons per camp.** Rebalance the jobs across camps.
6. **The crafting expansion.** Slots, the Workshop and Kitchen, food, and timed crafting if wanted.
7. **Camps 3–6 and the finale.**

## 12. Art for Astra, in order

1. **The ox and the wagon:** already briefed.
2. **Camp terrain sets:** camp 1 mostly exists (v4 ground and nature). Each later camp needs its own ground tint, trees and rocks.
3. **Station buildings:** brief ready to write (one complete building per station, three levels, work front facing the camera).
4. **Blockers:** fallen giant cedars, a rockslide, the ravine and bridge stages, the flooded causeway, the sealed gate.
5. **A view per camp:** camps 2–6, with the castle closer each time. The sky and far mountains can be reused.

## 13. Questions for Kevin

1. **Six camps?** More camps make a longer journey and more art.
2. **No going back to earlier camps?** I recommend no.
3. **Wagon storage limit** on what you carry between camps? I recommend yes, upgradeable with the wagon.
4. **A summons per camp** (a key castle job) as well as the blocker, to move on? I recommend yes.
5. **Timed crafting** (items take time and queue at a station, like Shop Titans) or instant as today? I recommend timed.
6. **Food:** foraged in the forest first, gardens or hunting later?
