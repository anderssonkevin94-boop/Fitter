# Decision log

Locked GDD mechanics are not repeated here. This log records versions, technical defaults
chosen to fill gaps, deviations, and playtest feedback. Newest entries at the bottom of each section.

## Pinned versions (Phase 0, 2026-10-06)

| Item | Version | Note |
|---|---|---|
| Unity Editor | 6000.4.3f1 | already installed; same as SeaSick |
| URP | 17.4.0 | 2D Renderer, custom-axis sort (0,1,0) |
| Input System | 1.19.0 | active input handling = Input System only |
| uGUI / TextMesh Pro | 2.0.0 | TMP essentials imported into `Assets/TextMesh Pro` |
| Newtonsoft JSON | 3.2.2 | save serialization and migrations |
| Test Framework | 1.6.0 | |
| 2D Sprite / Tilemap | 1.0.0 | |
| com.unity.pipeline | 0.7.0-exp.1 | dev-only CLI bridge (same as SeaSick); remove before release |
| Xcode (host) | 27.0 (27A266a) | |

Unity's new-project defaults (ads, purchasing, analytics, multiplayer, visual scripting) were removed:
the GDD excludes monetization and analytics.

## Technical defaults (Phase 0)

- **Render pipeline:** URP 2D Renderer from Unity's bundled 2D template. Sprites use Sprite-Unlit-Default (no 2D lights yet, cheap on mobile).
- **World convention:** simulation plane = Unity XY; screen-up = +Y (joystick up moves up the screen). Art is 2:1 isometric. 128 px per unit. Ground is an isometric Tilemap (cell 1 × 0.5). Town plot 24 × 18 units.
- **Sorting:** sorting layers Ground < Default < Overlay. Sprites sort by pivot (feet / footprint centre) along +Y. Multi-sprite objects use SortingGroup. See `Docs/ART_CONVENTIONS.md`.
- **Camera:** orthographic, width-fit (same world width at every portrait aspect). Default 8.5 units wide. The debug panel can cycle 7 / 8.5 / 10 for comparison only; the game has no zoom.
- **UI:** uGUI built in code from a `UiTheme` asset. Canvas scaler reference 1179 × 2556, width match, safe-area root. Minimum touch target 132 ref px (≈44 pt).
- **Device floor (provisional, unverified on hardware):** iOS 15.0, iPhone only, portrait only. Reference phone: iPhone 16 Pro. The supported-device floor still needs confirming on real hardware.
- **Bundle ID:** `com.kevinandersson.ravenholmsupplyco`. No signing team stored in the project; choose it in Xcode.
- **Architecture:** one composition root (`GameRoot`, Bootstrap scene) builds plain C# services (`GameServices`). Commands go in; typed C# events come out after commit. UI never edits state directly.
- **Namespaces:** the Application layer uses namespace `Ravenholm.Services` (the assembly is still `Ravenholm.Application`) so it never shadows `UnityEngine.Application`.
- **Content IDs:** `<kind>.<name>`, lowercase `[a-z0-9_.]`, max 64 chars (e.g. `resource.iron_ore`). Validation runs from the menu, in tests, and before every build.
- **Saves:** JSON DTO envelope (schema 2), header line with byte length + SHA-256 (truncation/corruption detection, not anti-cheat). Write sequence: temp → flush → verify → `File.Replace`, keeping the previous valid file as backup. Corrupt files are renamed `*.corrupt-<utc>`, never deleted. A generation counter rejects stale writes. Last-accepted UTC is monotonic.
- **Save profiles:** one folder per profile under `persistentDataPath/Saves/`. The sandbox uses `sandbox`; production will use its own profile.
- **Migration fixture:** schema 1 is a synthetic flat format kept only to exercise the migration pipeline (`Tests/Fixtures/save_v1_fixture.json`).
- **RNG:** PCG32 (verified against reference vectors), separate named streams per world seed (`RandomStreamId`).
- **Save cadence (sandbox):** "critical" commands save immediately; cheap changes (position) save after a 3 s debounce; pause and focus loss flush. Phase 0 writes synchronously; an async write queue is deferred until a phase needs it.
- **Placeholder art:** original procedural sprites from `Tools/PlaceholderArt/generate_placeholders.py`; provenance in `Art/Placeholder/PROVENANCE.md`.

## Phase 0 scope notes / deviations

- **Walking in Phase 0:** the GDD allows a "navigable or tappable" test room. To judge scale and camera, the sandbox has a bare debug walker (`DebugWalker`, labeled fixture) driven by the Phase 0 touch-input plumbing. Not built yet (Phase 1): visible floating joystick, lower-screen movement area, collisions/kinematic motor, camera dead zone and damping, roof fading.
- The context button (nearest inspectable, with hysteresis) exists only to test HUD layout and thumb reach; real station interaction is Phase 1.
- HUD gold / reputation / carry chips show fixed layout values from `UiTheme`, not systems.
- Characters and buildings carry "Planned: Phase N" notes when inspected, so the playtester knows what is placeholder.

## Phase 1 decisions (2026-10-06)

- **Ownership model:** `LogisticsSystem` (engine-free domain) is the single owner of physical goods. Owners are world containers (raw piles, output pads), the carry, the shared material ledger and the Store. Each transfer moves one unit and fully commits or changes nothing; typed events fire after the commit. A conservation audit (debug panel and tests) checks totals and that every item has exactly one owner.
- **Pickup rules:** reach = container radius + 0.8. Output pads win near-ties (0.05 units), then stable container ID. Inside a container, items come first, then materials by resource ID. One unit per 0.08 s, with no burst after idling. A full carry warns once per approach (toast + badge pulse + sound). These numbers live in `BalanceProfile`.
- **Delivery:** standing in a receiver zone delivers the most recently stacked accepted unit, one per interval. Unaccepted goods stay carried. Raw materials go to the shared ledger; finished items go to the Store (Phase 3 adds racks and processing).
- **Acceptance is data-driven:** `ResourceDefinition.acceptedReceiver` decides materials, and any `ItemDefinition` goes to the Store. There are no name checks in code.
- **Fixtures:** piles and pads in the sandbox carry authored fixture contents. They are seeded only when absent from the save, so a restart never duplicates. "Refill test piles and pads" tops up to the fixture. Materials and items are created only through these fixture calls (the approved sources for Phase 1).
- **Movement:** a floating joystick anchored where the touch starts, only in the lower 72 % of the screen (dead zone 8 pt, full speed at 60 pt; the anchor trails the finger). Taps still inspect anywhere. A collide-and-slide circle motor at the feet (radius 0.22) runs against footprint colliders on the `Blocker` layer (layer 8), plus a town boundary.
- **Footprints:** isometric diamond footprints independent of roof art. The Store is open-front, so only its back walls and counter block. Trees, rocks and characters use small circles.
- **Camera:** dead zone 0.45 × 0.7 units, SmoothDamp 0.18 s, still width-fit and clamped.
- **Readability:** receivers show a dashed ground marker and an icon bubble before entry. Carried pieces the nearby receiver accepts bob. Buildings and trees fade while the player stands behind them.
- **Input robustness:** the router adopts an untracked touch even if its one-frame "Began" was missed, judging it by its start position. Touches held across a modal stay ignored until lifted.
- **Saves:** schema 3 adds the `logistics` section (carry, containers, shared ledger, Store items, item definitions, created-material totals, next instance number). The v2→v3 migration is tested with `Tests/Fixtures/save_v2_fixture.json`. One `SaveScheduler` gives every section the same cadence: critical commands save immediately, hauling and movement are debounced 1.5 s, and lifecycle events flush.
- **Station interaction:** the context button shows "Open <Station>" for receiver buildings and opens a panel with what the station accepts and what is in shared inventory or the Store. Processing and crafting remain Phase 3.
- **Audio:** placeholder blips are synthesized at runtime, rate-limited and pitch-stepped. Haptics are not implemented yet.
- **Measured layout distances (straight line, not yet playtested):** Forge pad → Store strip ≈ 8.4 units (≈ 5 s round trip at 3.5 u/s); Mine staging → Smelter ≈ 3.9 units (≈ 2.3 s round trip). This is shorter than the GDD's 8–12 s target for the longest common haul, so it's a tuning question for the playtest.

## Phase 2 decisions (2026-10-06)

- **Simulation:** `SiteSimulator` (engine-free) steps from event to event in integer milliseconds and micro-damage (damage × 1000). The same total time gives identical results regardless of frame sizes (tested with random partitions). This is the code path Phase 6 offline catch-up will reuse. There is no per-frame replay.
- **Workers:** one named worker per site (Dagna Holt, miner; Tobin Ash, woodcutter). They are authored `WorkerDefinition`s with starting tool and pack, and have no XP, wages or needs (locked).
  - **State machine:** walk to work → work → (pack full or section done) return → unload 0.6 s → repeat. Pauses: `StagingFull` (keeps what it carries and retries when space opens) and `AwaitDepthChoice`.
  - **Not modelled:** `RouteBlocked`. Routes are authored, not physical, so nothing can block them.
- **Mine:** a shared face. Each section releases 24 primary units at evenly spaced damage thresholds.
  - Primary units are weighted draws; the gem is an independent roll per primary unit. Earned units go to working miners in rotation; overflow (including rare drops at a full pack) waits on the face.
  - Damage is clamped at the section's durability. The site waits for a decision only once the face holds nothing; workers still bring back packs they already earned.
- **Forest:** a 6-tree clearing. Each tree's species and its 4 yields plus herb bonuses are precomputed from the yield stream. Trees are claimed exclusively; a worker with space hops to the next tree (0.8 s).
- **Curves (balance starting points, GDD 6/7):**
  - Mine: 240 × 1.22^(d−1), rounded to 10.
  - Forest: 40 × 1.18^(a−1), rounded to 5.
  - Continuation: 1 + 0.18k + 0.04k² (Mine) and 1 + 0.15k + 0.03k² (Forest), with k capped at 6 only at the terminal band.
  - All of these are fields on the `SiteDefinition` and band assets.
- **Generation:** band composition is seeded from (world seed, site ID, band index) on its own RNG stream. Weights get ±8 % jitter and are then normalized, with a 35 % baseline share for iron (Mine) or cedar (Forest). It is generated once and saved; reloading cannot reroll it.
- **Travel per leg (balance starting points):** Mine 2.5 / 3.0 / 3.5 s by depth; Forest 2 / 3 / 4 s by area (GDD 7 table).
- **Staging:** staging piles are the Phase 1 world containers with a capacity of 96. Workers deposit into them; the player hauls from them. They now start empty (the Phase 1 fixtures were removed).
- **Ownership:** extraction is an approved source (`RecordCreated`). Worker packs and pending yield are external holders included in the conservation audit. Deposits move units; they never create them.
- **Decisions:** player only, through the site panel (context button "Open Mine/Forest").
  - "Continue Here" shows the effort increase and the same yield. "Dig Deeper" / "Push Deeper" shows the expected tier and "composition unknown", and needs a second tap because it is permanent.
  - At the frontier the button reads "More depths not yet charted".
  - A "Choose next section/area" marker floats over the entrance; a worker waiting on full staging shows a warning bubble.
- **Presentation:**
  - The miner walks between staging and the Mine mouth and is hidden while at the face (visible in the panel's strata column).
  - The woodcutter walks to claimed trees and swings; felled trees become stumps; trees shake on each reward.
  - Deeper Forest areas tint the pocket and add trail markers. Pocket-to-pocket transitions are not animated yet.
- **Debug (Phase 2 only):** tool and pack tiers are cycled from the site panel ("debug purchases"; the GDD costs are stored but not charged). Also: sim speed ×1/×5/×20, complete section/clearing, and a throughput readout (units/min to staging in sim time).
- **Saves:** schema 4 adds `sites` (depth, continuation, revealed compositions, RNG state, section/tree progress, pending yield, workers with their packs and phase timing). The v3→v4 migration is tested with `save_v3_fixture.json`.
- **Not done (deferred to Phase 6):** time away while the app is closed is not caught up. Sites resume exactly where the last save left them.

## Phase 3 decisions (2026-10-06)

- **Shared ledger:** delivered raw goods live in `LogisticsState.SharedMaterials` (one owner), usable at any eligible station. Processing and crafting debit through atomic ledger calls. `MaterialsConsumed` makes the conservation audit read *created = held + consumed* per resource. A bounded, unsaved transaction history (reasons: delivered, processing input/output, crafted, starting stock, debug grant) shows in the debug panel.
- **Processing (GDD 8):** one line per facility with automatic repetition. Inputs are debited when a batch starts; the output is credited to shared inventory on completion with no extra haul. In-flight batches are saved and resume exactly.
  - A recipe change requested mid-batch is queued until the batch ends. A per-input reserve defaults to 0 and is adjustable in the panel.
  - The panel shows the running batch and time left, or why the line is idle ("Missing 1× Iron Ore (reserving 4)", "Requires facility level 2").
  - Processing never reads staging. Event-driven integer ms, like the sites; frame-partition independence is tested.
  - Processes and timings follow the GDD. Steel and maple need level 2.
- **Crafting (GDD 9):** instant and atomic, checked in this order: blueprint → station level → output-pad space (6) → costs. Then debit, then one new instance on the station's output pad.
  - A full pad blocks crafting with a reason and spends nothing; a full carry does not block it.
  - Unknown (unowned) recipes are hidden, because discovery hints don't exist yet.
- **Content:** `StationDefinition` (output pad, starting/max level), `ProcessDefinition`, `RecipeDefinition`, `BlueprintDefinition` (owned-at-start flag), and `StartingState` (GDD 15 stock: 6 iron ingots, 6 cedar lumber, 1 copper ingot, 2 herbs). `ItemDefinition` gains a counter tag and strength (displayed now, used by the Phase 5 resolver).
- **Test unlock:** "Copper Ward Staff" (Workbench, 3 cedar lumber + 3 copper ingots, Power 10, arcane counter +4) is the one advanced recipe. The debug panel grants its blueprint; normally it comes from the Old Shrine (Phase 5).
- **Starting state:** granted when the `production` save section is absent, so a new company or a pre-Phase 3 save gets the starter blueprints and stock exactly once.
- **Store:** the open-front receiving strip stocks items into the Store ledger.
  - Racks show a representative subset: distinct items, strongest first, then newest, with a ×N badge. Inventory is never limited by rack slots; player pinning is not built yet.
  - The Store panel offers Weapons / Armor / Potions / All, plus separate Materials and Blueprints tabs. Item cards show name, stat line, compatibility hint, status and Trader value; class compatibility details arrive with Phase 4 class data.
- **Fix:** Store racks were drawn behind the Store's back walls since Phase 0. Their sort point now sits just in front of the Store pivot.
- **Output pads:** output-pad fixtures from Phase 1 were removed, since pads are now filled by crafting. Test-only fixtures create pad items directly.
- **Station panels:** every production building (and the Store) opens its panel via the context button. Each panel has a DEBUG level toggle for testing gates (upgrades are Phase 6).
- **Saves:** schema 5 adds `production` (sim clock, processors with in-flight batches, reserves and queued changes, facility levels, blueprints) and `logistics.materialsConsumed`. The v4→v5 migration is tested with `save_v4_fixture.json`.

## Phase 4 decisions (2026-10-06)

- **Company:** gold (integer) and reputation (tenths, displayed rounded) live in the `jobs` save section. A new company starts with 60 gold and reputation 20 (BalanceProfile). The HUD chips are live.
- **Classes and traits (GDD 10, data):** Warrior 8/8/3 fee 8 (blade, blunt); Ranger 5/7/8 fee 9 (bow, exploration +2); Rogue 7/4/8 fee 10 (light blade, traps +2, bonus rare roll flag); Mage 9/4/7 fee 13 (staff, arcane +4); +1 to all stats per level. Traits: Cautious (applies at Good or better), Bold (applies at Dangerous), Woodland Familiar (exploration +2), Poison Trained (poison +2).
- **Forecast:** one shared calculator (`ForecastCalculator`) implements GDD 12 exactly: readiness A, clamped context C, survival V excluding the roll, risk words from the mean margin, and one step worse when V < 0.
  - Counters add per threat. Reaching the required strength removes the unmet penalties ("countered"); below it the strength still adds ("helps").
  - Heavy blows use the armor threshold. Unknown threats are left out and flagged "Limited knowledge".
  - Tests cover the GDD worked example and monotonicity.
- **Fees:** the GDD 10 formula (tested against the GDD's example quotes 15/13/17/21). Quoted once per applicant per posting and saved.
- **Contractor pool:** the GDD's four example contractors (personality price 1.0) plus a sponsored contractor "Wren Tallow" and generated contractors up to 12. Generated names come from an editable `ContractorNamePool` (original names); levels scale with reputation (1 + rand(0..rep/30)); 60 % get one trait. Generation is deterministic per world.
- **Posting:** one open posting; free; at most one active expedition per quest.
  - **Applicants:** 2, then 3 at reputation 30 and 4 at 60. They arrive 10–30 s after posting. Screening uses the best compatible available gear with each contractor's willingness rule; Suicidal never applies.
  - **No reroll:** the candidate window seed advances only when a posting is filled, so cancelling and reposting gives the same applicants and quotes.
  - **Nobody applies:** a plain reason is given (availability, reputation or danger) and points to the Local Supply Survey.
- **Recovery contract:** the "Local Supply Survey" is flagged in data. Its single applicant is always the sponsored contractor (a replacement is generated if needed), and the patron pays the fee. It has no threats; its safe result comes in Phase 5.
- **Quest fixtures:** the eight GDD 17 quests are all postable now; prerequisites and unlocks come in Phase 5.
  - Known at start (my choice): Briar Track exploration, Old Shrine arcane, Fen Path poison, Shrine Chamber arcane.
  - Shown as "Threat not yet identified": Quarry Road heavy blows, Fallen Watchtower traps, Shrine Chamber exploration.
- **Loadout:**
  - Meeting an applicant (tap them at the Store, or via the Job Board) opens their card and four slots, prefilled with the best compatible weapon, best armor and one potion.
  - Slots cycle through available item types. Store items are reserved while the editor is open and released on Back or cancel. Reservations are transient and never saved.
  - **Review:** risk word, Limited knowledge, per-threat countered/helps/not countered, unidentified threats, potion triggers, fee or patron, and the "gear is permanently lost" line.
  - The very first dispatch needs a second tap.
- **Dispatch:** atomic. It re-validates posting, arrival, availability, slot limit (2), item custody and reservations, class compatibility, potion slots, refusal on a Suicidal actual loadout, and gold. Then it commits the fee, gear custody (Store → expedition), contractor status, timings, threat set, seed and a stat snapshot together.
  - Phase 4 expeditions stop at a labelled TEST DEPARTURE. A debug button returns gear, refunds fees and frees contractors.
- **Fix found by tests:** loadout-reserved items could be taken out of the Store by another caller. Only the reservation holder may take reserved items now.
- **Saves:** schema 6 adds `jobs` (gold, reputation, contractors, posting with quotes and seed, expeditions with snapshots). The v5→v6 migration is tested with `save_v5_fixture.json`.

## Phase 5 decisions (2026-10-06)

- **Resolver v1 (GDD 12, exact):** E = A − D + J, with J triangular on ±10; V = Survival + 0.8·Protection + tonic + survival context − 0.55·D + K, with K triangular on ±6. Outcome table: GS E ≥ 10, Success E ≥ 0, Partial + injury E ≥ −6; below that, Failure if V ≥ 0, else Death.
  - Hidden threats count in resolution even though the forecast left them out.
  - Counter potions fire whenever their threat is present. A Recovery Tonic is spent only when E < −6.
  - J and K come from each expedition's saved seed, so every outcome is reproducible.
  - Expeditions freeze difficulty, threat specs, potions, stats and the resolver version at dispatch.
- **Completion (GDD 13/23):** applied once, in completion-time then ID order, as one unit of work, and saved at once. The expedition ID is the idempotency key. It covers:
  - gold = floor(base × 1.25 / 1 / 0.4 / 0 / 0);
  - gear returned, consumed potions destroyed, or everything destroyed on death;
  - explicit material packages per outcome and a duplicate-safe first-success blueprint (substitute materials if already owned);
  - rare rolls (Great Success, plus the Rogue's bonus roll after a success);
  - 1 XP per success with a level-up at 3, a 10-minute injury, or death;
  - reputation +4/+3/0/−2/−7 with GDD 14 diminishing gains (tenths, clamped after each result);
  - knowledge, then unlocks.
- **Results:** acknowledging a result is presentation only. The last 30 are kept; a toast and badge announce returns.
- **Survey:** authored safe result (Success, 8 gold, +1 base reputation, no XP or loot). Forcing never applies to it.
- **Knowledge (my simplification of GDD 13):** a success reveals all of that quest's not-yet-identified threats; a partial reveals one; failure and death reveal nothing. Revealed threats feed forecasts and screening from then on.
- **Map and chain (GDD 17 fixtures):**
  - Start: Survey and Roadside Patrol.
  - Patrol unlocks Briar Track and Old Shrine.
  - Briar Track unlocks Quarry Road and Fen Path.
  - Old Shrine unlocks Fallen Watchtower, and Shrine Chamber (which also needs reputation 30, first-reached).
  - Unlocks never relock. The Job Board shows the map as tiers with lock reasons; locked jobs cannot be posted. Prerequisite cycles fail validation.
- **Rewards (GDD 17, balance starting points):**
  - Patrol: 2 iron ingots.
  - Briar Track: 2 maple lumber + Maple Longbow.
  - Old Shrine: 1 arcane dust + Copper Ward Staff.
  - Quarry Road: 3 iron ingots + Steel Coat.
  - Fen Path: 2 herbs + Antidote.
  - Fallen Watchtower: 2 steel ingots + Steel Sword.
  - Shrine Chamber: 2 arcane dust + 2 copper ingots + Warding Draught.
  - Great Success adds one more unit of the main material. The rare roll is a gem at 50 % (Patrol: herb at 25 %).
  - Simplification: the Patrol's "small chance of an extra herb" is modelled as its rare roll, which only happens on Great Success or with a Rogue.
- **New content:** six advanced items and recipes per GDD 9 (Steel Sword and Steel Coat need Forge 2; Antidote and Warding Draught need Apothecary 2; Maple Longbow needs maple lumber, which needs Lumbermill 2). Use the station panels' DEBUG level toggle until Phase 6 upgrades.
- **Result explanation:** two or three plain reasons ranked by size (threat countered or not, unidentified threat, a bad or good road, gear, tonic or armor on the retreat). There is no invented combat narrative.
- **Debug:** force the next outcome (marked on the result), finish expeditions now, and exact odds over 2000 seeds in the loadout panel (designer-only line, never player-facing percentages). Phase 4 test departures remain for old saves only.
- **Saves:** schema 7 adds quest progress, results and frozen expedition inputs. Older expeditions are rebuilt from content once (resolverVersion 0 → 1). The v6→v7 migration is tested with `save_v6_fixture.json`.

## Phase 6 decisions (2026-10-06)

- **One simulation clock.** `SimulationService.Advance(total, resource)` drives sites, processing and expeditions, both online (per frame) and offline (catch-up). It steps exactly to each quest completion, so processing and sites reach that moment before the expedition returns. A test checks that 12 minutes of frame-sized steps and one 12-minute absence give identical logistics, sites, production and jobs sections.
- **Offline catch-up (GDD 15):** at boot, after loading and before the session save, from the save's last accepted time to now. Resume from the background does the same. Gathering and processing are capped at **8 hours**; expeditions and the Trader schedule see the whole absence. No player decisions are made: no hauling, no depth choice, no purchases. A clock that went backwards adds nothing. Completion saves are deferred during catch-up and committed once, so an interrupted catch-up simply re-runs from the previous save and never applies a reward twice. Each catch-up gets an id (`catchup.<generation>`) stored in the save for diagnosis.
- **Away summary:** shown at boot or resume when the absence is at least 60 s. It covers deposits per site and why each site stopped, processed output, returned expeditions (results stay unread on the Job Board), recoveries from injury and the Trader's status.
- **Trader (GDD 16):** the first visit comes 60 s after the first ordinary (non-sponsored) quest return. After that, visits last 5 minutes every 20 minutes of game time.
  - The schedule is derived from an anchor, so missed visits never accumulate.
  - Each visit's stock comes from (world seed, visit index) and cannot be rerolled by reopening or reloading.
  - Remaining quantities are saved per visit.
  - Stock follows the GDD table. The iron ingot and cedar lumber bundles (3 g, ×6) appear on every visit. Arcane dust needs an Old Shrine success; the GDD says "Shrine discovery", interpreted here as the first success. "Common" entries use a 70 % chance. The 20 % side blueprint pool is empty until Phase 7 content. The artifact weapon is deferred (v1).
  - Selling takes unreserved Store items only, at `TraderBuyValue` (1–3 g). Reserved or away gear cannot be sold.
- **Upgrades (GDD 16 table, balance starting points):** apply instantly at the building or site panel. Gold and materials are debited together after all checks pass.
  - Smelter 2, Lumbermill 2, Forge 2, Apothecary 2, Mine/Forest Equipment 2, and crew expansion per site.
  - Batch time: each level above a process's own gate multiplies by 0.85, so iron smelts in 3.4 s at Smelter 2 while steel stays 6 s. The new speed applies to the next batch.
  - **Invented (not in the GDD):** Mine/Forest Equipment 3 at 90 g + 8 steel + 6 maple lumber, needing Equipment 2 and 5 ordinary successes. These gate the tier 3 tools and packs that already existed in content.
- **Worker tools and packs** are bought per worker from the site panel and applied directly to the worker. They are not crafted goods. Site equipment level only unlocks the next tier, and each purchase must be one step up.
- **Crew expansion:** recruits Mara Quill (Mine) and Osk Fennel (Forest). Their town figures exist in the scene but stay hidden until hired.
- **Saves:** schema 8 adds an `economy` section (Trader anchor, visit, remaining stock; upgrades; last catch-up id). Saves from v7 get the Trader scheduled from their earliest ordinary result. Tested with `save_v7_fixture.json`.
- **Balance guards (tests):** selling a whole starter kit (13 g) earns less than one Roadside Patrol (28 g). No recipe can be crafted from Trader goods and sold at a profit. Recovery from 0 gold, 0 reputation and an empty Store works through the sponsored Local Supply Survey plus the guaranteed Trader bundle.
- **Debug:** simulate time away (+1 h, +8 h, +24 h; same code path as a real absence) and summon the Trader now.
- **Test fix:** `FirstSuccessBlueprintIsGrantedOnceAndDuplicatesBecomeMaterials` was flaky. The applicant pool is random per world seed, and sometimes nobody would take Old Shrine. Its fixture now uses seasoned contractors, as the Quarry Road test already did.

## Phase 7 decisions (2026-10-06)

- **Scope against the v1 ceilings (GDD 1):**
  - 3 regions (Frontier Paths, Hollow March, Crownward Ruins).
  - 20 quests (the ceiling is 18–24).
  - 22 recipes (the ceiling is 24–30; I kept fewer, better ones).
  - 6 depth bands per site.
  - All values are balance starting points.
- **New materials:**
  - Moonsilver ore from Mine depths 4–6, smelted at Smelter 3.
  - Ironwood log from Forest areas 4–6, sawn at Lumbermill 3.
  - Every deep band keeps at least 35 % iron or cedar (GDD 6/7).
  - Forest travel time per leg is capped at 6 s.
- **New facility levels:** Smelter 3, Lumbermill 3, Forge 3 and Workbench 2. Their costs and prerequisites are invented balance starting points; the GDD table stops at level 2.
- **Ten new recipes:**
  - Steel maul (Warrior, blunt).
  - Steel stiletto (Rogue, Power 13 so a Rogue can meet the finale's weapon bar).
  - Fur-lined brigandine (armor, exploration +3).
  - Strong tonic (survival +7).
  - Pathfinder's and Trapsense draughts. These are optional: they come only from the Trader's 20 % side-blueprint slot.
  - Moonsilver focus (Mage, arcane +5).
  - Moonsilver mail (Protection 14).
  - Ironwood greatbow (Ranger).
  - Moonsilver blade (Warrior).
  - Every other new blueprint is a deterministic first-success reward.
- **New potion effects:** `TrapsCounter` and `ExplorationCounter`, appended to the enum so saved values stay stable. Counter potions now map to threat tags generically.
- **Regions** are definitions (`RegionDefinition`). The map lists reached regions with their jobs. The next region shows as a rumour silhouette; regions after that stay hidden.
- **Campaign breadcrumb:** each quest has an authored `campaignStep`. "Next for the company" names the earliest main-chain quest not yet succeeded, with its lock reason or status. The validator checks that steps follow prerequisites and that every prerequisite of the finale is on the chain.
- **The Beacon chain (GDD 14):**
  - **Chart the Beacon Road** (minimum level 4) and **Hold the Beacon Pass** (minimum level 4) are one-time legs.
  - On success, each leg removes one capstone threat for good through the data-driven `preparations` list. The uncharted approach and the pass wardens are deliberately harsh unprepared (−8 readiness, a survival penalty, and an armor threshold of 16 that nothing reaches).
  - **The Beacon Expedition** has difficulty 32, minimum level 6, current reputation 70 at dispatch, weapon Power 13+, armor Protection 10+, and a counter potion.
  - The Pale Flame needs 10 arcane counter: a mage or ward gear plus a Warding Draught.
  - Failure follows the ordinary rules, with no special fail state.
  - Success completes the campaign: a trophy appears in town, a result message is shown, and the breadcrumb switches to postgame text.
  - Repeatable contracts continue afterwards.
  - All of this is derived from saved quest progress, so no schema change was needed.
- **Dispatch requirements** (level, current reputation, weapon Power, armor Protection, counter potion) are generic quest fields, checked in `DispatchBlockedReason`. Applicants below a quest's minimum level do not apply.
- **Rule changes made in this phase, for review:**
  - **Screening includes potions.** Applicant screening now adds the best available counter potions to the best weapon and armor; the GDD's "best compatible available gear" covers them. Without this, almost nobody would apply for Pale Flame jobs.
  - **Newcomer levels scale with reputation.** Newcomers are level 1 at reputation 20, rising to at most level 6 at reputation 70. This formula is unchanged below reputation 40.
  - **Seasoned newcomers for elite postings.** If nobody alive meets an elite posting's minimum level and reputation supports it, the pool may add up to two newcomers at that level, to at most 16 contractors. Before this, finishing the campaign depended on one contractor happening to collect 15 successes; one bot run stalled for 700 expeditions.
- **Validation:** `ProgressionGraph` computes everything reachable from a fresh company and reports any recipe, quest, upgrade or process that ordinary play can't reach.
  - It counts only recurring sources: site yields and their per-unit bonus rolls, processing, Trader stock (including the side-blueprint slot, which rolls again every visit), deterministic quest rewards and first-success blueprints.
  - Quest rare rolls never count, so no required item can sit behind a rare drop.
  - It runs as part of catalogue validation.
- **Campaign bot (EditMode):**
  - Plays from a fresh save with the real resolver, gold, fees, applicants, injuries and upgrades.
  - Approximates production by topping up materials, and treats every band as revealed.
  - Results so far: 9 runs finished in 69–132 expeditions (about 6–12 h of quest time sent one at a time), with no deaths.
  - Gold piles up late (about 1,000–4,700). That's partly because the bot gets free materials and never buys worker gear. Tuning question for the playtest.
- **Class niches** (tested): each class is the best choice for at least one job.
  - Warrior: heavy blows.
  - Ranger: exploration.
  - Rogue: traps.
  - Mage: arcane, including the finale.
- **Tests updated:** two old tests asserted the Phase 2 and 3 content counts (15 resources, 3 bands per site). They now check 19 and 6.

## Phase 8 decisions (2026-10-06)

- **Final art is NOT done (open decision for the user).**
  - The GDD's first Phase 8 deliverable is final original art. I can't produce final illustrated art, and the source is the user's call: commission an artist, buy a licensed pack, or something else. It affects cost, licensing and provenance.
  - Every view still reads its sprite through definitions and prefabs, so art can be swapped without touching the simulation.
  - Phase 8 only extended the coherent placeholder kit: Phase 7 materials and items, a Beacon trophy and level pennants, all recorded in `PROVENANCE.md`.
- **Settings (GDD 19):**
  - Music and effects volume, haptics, reduced motion, text size (100/115/130 %), joystick side (anywhere, left half, right half), and first-hour tips.
  - Stored in `persistentDataPath/Settings/settings.json`, separate from game saves, so a profile reset never touches them.
  - A damaged settings file falls back to defaults.
  - The gear button now opens Settings; Developer tools sit inside.
- **Text scaling:** every UI text carries `ScaledText`, which rescales from its authored size when the setting changes. Fixed-size labels (HUD chips, banner, Mine strata labels) auto-fit instead. A PlayMode test opens every panel at 130 % and fails if any non-auto-sized text needs more height than its box.
- **Reduced motion:** removes carried-item arcs, the stack and carry-chip pulses, and the worker work swing. State feedback stays: colours, counts, bubbles, toasts.
- **Haptics:** a native iOS plugin (`Assets/Plugins/iOS/RavenholmHaptics.mm`) using UIImpactFeedbackGenerator and UINotificationFeedbackGenerator. Haptics play only for:
  - completed deliveries (rate-limited),
  - crafts,
  - dispatches,
  - discoveries,
  - returns,
  - a full carry stack.
  - They are off when the setting is off.
- **Audio:** still synthesized placeholders (no audio assets).
  - Grouped timber-knock and tone transfers.
  - Bell chimes for craft, dispatch, return (good or bad), discovery, coin and Trader arrival.
  - A calm 16-second town loop on the music channel.
- **First-hour prompts:** 12 data-driven `TutorialStepDefinition`s following the GDD 19 first-hour table. Each finishes from game state or events, or from "Got it" for information tips.
  - Finishing a later step finishes every earlier one.
  - Prompts never change odds, rewards or timing.
  - The danger tip comes after the first result and before the second normal dispatch.
  - The tip card sits under every panel and hides while one is open.
  - Saved as the schema 9 `onboarding` section. Migrated saves that already have quest results start with tips hidden ("optional for returning testers"). "Show tips again" lives in Settings.
- **Building upgrades in the town:** stations show a pennant per level above 1 (`StationLevelView`).
- **Usability checks (PlayMode):**
  - **Touch targets:** every HUD and panel button is at least about 26 pt in both dimensions. The rows are 132 canvas units (about 44 pt) tall; the small round buttons are the limit.
  - **Occlusion:** every enabled tap target is the frontmost hit somewhere inside its collider. This test found a bug I'd introduced in this phase: a stray plain `Inspectable` on the Store, Mine and Forest. Fixed.
- **Performance:**
  - `PerfProbe` (Developer tools, or a `perf_autorun` file in the app's Documents folder) records boot and scene-ready time, frame-time percentiles, GC count, memory, and an 8-hour catch-up. The catch-up is timed on a throwaway busy company in a temporary folder, never on the player's save.
  - Editor 8-hour busy catch-up: about 60–75 ms.
  - **Device results are still pending.** The Phase 8 build is installed on the iPhone 16 Pro, and the probe started (its autorun flag was consumed). The phone was locked, though, so iOS suspended the app straight after boot and no report has been written yet. It finishes after the app has been open in front for about a minute. Then pull it with `xcrun devicectl device copy from ... --source Documents/Perf`, or use Developer tools → Record performance.
  - Thermal behaviour over 20 minutes still needs a manual device session; there's no API for it without more native code.

## Phase 9 decisions (2026-10-06)

- **Balance harness (`BalanceTests`, writes `Docs/BALANCE_REPORT.md`):**
  - For every quest it runs 2,000 resolver seeds per class, with the gear a player would own when the job first appears and on repeat runs.
  - It reports outcome rates, fee, potion cost, expected gear loss and net gold.
  - **GDD 15 rule:** every repeatable contract reaches Good by level 8 on repeat runs, with positive expected net gold.
  - **No first-attempt walls:** some class reaches Risky within three levels of the expected level.
- **Balance revisions** are versioned (`ContentCatalogue.balanceRevision`); each one is applied once and bumps the content version of what it touches.
  - **Revision 1:** Fen Path difficulty 23→21, Fallen Watchtower 24→22, Hollow Warren 26→23.
  - **Revision 2:** iron dagger Power 7→8 (Rogue starter weapon matches the sword), Fallen Watchtower 22→21, Hollow Warren 23→22.
  - Reason: each tier-2/3 job's counter was its own reward, and the Rogue had no weapon to carry trap jobs.
  - Fen Path, Quarry Road and Hollow Warren first attempts are still Dangerous at the expected level, but reach Risky within three levels. This fits the GDD's "deliberate dangerous attempt"; playtest to confirm.
- **Recovery states (GDD 15):**
  - Normal start: a viable patrol right away.
  - After consecutive deaths: about 3 minutes of recovery surveys.
  - Emptied account: about 7.5 minutes, plus up to 20 minutes for the next Trader visit (the iron and cedar bundle is guaranteed every visit).
- **Soak test (`SoakTests`):** 3 seeds × 400 random steps (play, crashes by reload from disk, time jumps of up to a day, trades, upgrades, site choices, zero gold).
  - After every step it checks: ownership audit, gold and reputation bounds, results recorded once, expedition/contractor/item consistency, slots, non-negative stock.
  - Totals: 106 crashes, 111 time jumps, 73 trades, 0 violations.
- **Save compatibility:** fixtures v1–v8 each load into a full company, run an hour, and round-trip cleanly. Schema 9 is frozen for this release line.
- **Developer mode:** Developer tools show by default only in the editor and development builds. Release builds unlock them by tapping the Settings version line five times; the choice is stored in device settings.
- **Diagnostic export** (Developer tools): build, schema, balance revision, save status and generation, migration and clock flags, campaign state, ownership audit, last 40 transactions. Contains no personal data.
- **Version:** 0.9.0 (build 9). Minimum iOS 15.0, iPhone only.
- **Device interruption test** (iPhone 16 Pro, release build): 16 forced relaunches at random 0.3–4 s intervals, killing the app during boot and saves. The main save and backup both kept valid checksums afterwards, schema 9, saves 651 and 652. The test ran without warning the user first; future device tests will be announced.
- **Device performance (PerfProbe)** is still pending: it needs the app open in front for about a minute. See KNOWN_ISSUES.

## Post-roadmap polish, batch 1 (2026-10-06)

The user asked to "keep building without me playtesting". A read-only audit compared the GDD against the code, and this batch closes the most valuable gaps it found. It adds no new systems and leaves art sourcing open.

- **Developer gating leaks fixed (GDD 12/24).** These were visible to every player:
  - exact odds (which also exposed hidden threats) on the loadout card,
  - free station levels,
  - free worker tools and packs,
  - the debug banner,
  - the Phase 0 save readout.

  They now show only in developer mode (`Feel.DeveloperMode`). The five-tap unlock refreshes Settings immediately.
- **Debug use is recorded (GDD 23):** every debug command notes itself in the save (`sandbox.assisted`), and the diagnostic report lists them. Accelerated time counts.
- **Developer scratch save (GDD 24 profile switch):**
  - Developer tools can switch the next launch to a separate `debug` profile; the company save stays untouched.
  - The banner always shows when the scratch save is in use.
  - The setting only accepts the company or `debug` (no arbitrary folders).
- **While You Were Away (GDD 18):**
  - "Read the result" or "View all N results" button.
  - Long result lists grouped by outcome.
  - The latest summary can be reopened from the Job Board.
- **Contractor history (GDD 10):** latest three jobs, injuries, toughest success, discoveries and death record, all saved. Applicant and loadout cards show it, and the Job Board has a roster of everyone you've hired, including the fallen.
- **Quest cards show rewards (GDD 11):** success, great-success bonus and partial packages.
- **Blueprint reveal card (GDD 13/20):** item picture, stats and the station to craft it at.
- **Town signals:**
  - a craft flash on the output pad (still in reduced motion),
  - the Trader's countdown on the pad,
  - a "Report" marker at the Store while results are unread (the GDD's "report delivered" Store-marker option).
- **Knowledge stages (GDD 13):**
  - Second success: a reward clue.
  - Third success: a discovered route with a small readiness bonus (+1 or +2), applied to that location only. It counts in forecasts and is frozen into each expedition at dispatch.
  - First failure-and-return: one basic observation, if authored.
  - Authored as data (`QuestDefinition.knowledge`, `failureObservation`) for 16 locations; not every location uses every stage.
  - Partial successes still reveal one threat fact. The recovery contract teaches nothing.
  - Saves add optional fields only (`observed`, `routeBonus`, `facts`), so no schema change.

## Post-roadmap polish, batch 2 (2026-10-06)

- **Store pinning (GDD 4).** Store rows have Pin/Unpin; pinned item types show first on the racks. It's saved as an optional logistics field (`pinned`) and is a display preference only.
- **Colour is never the only signal (GDD 19).**
  - Threat lines carry `[+]` countered, `[~]` helps, `[!]` not countered and `[?]` unknown.
  - Each risk word has its own marker (`[++]` Excellent through `[!!]` Suicidal).
  - Stopped sites in the away summary carry `[!]`.
  - Text markers are used because the font lacks symbol glyphs; real icons can replace them with the final art.
- **Developer tools (GDD 24), developer mode only:**
  - reveal all threat knowledge,
  - grant 10 of a chosen material,
  - readiness and survival terms (A, C, route bonus, margin, V before the roll) in the loadout readout,
  - expedition seed, difficulty and route on Job Board lines,
  - per-site worker bottleneck share (staging full and waiting for a choice; session-only, like the throughput measure),
  - per-session transaction IDs (`#n`) in the history and diagnostic export.

  Every debug command is recorded as assistance.

## Post-roadmap polish, batch 3 (2026-10-06)

- **Forest depths (GDD 7):**
  - Ironwood gets its own tree (tall trunk, layered flat canopy), mapped by data (`TreePocketView.speciesSprites`).
  - Six area tints, darker and cooler with depth.
  - Trail markers for areas 4–6.
  - Pushing deeper fades the next pocket in; reduced motion switches at once.
- **Character animation (GDD 20), visual only:**
  - The player has an idle breath, a heavier carry walk (lower bob, slight lean back) and a short reach when using something.
  - Applicants breathe while waiting and hop to greet when they arrive.
  - Returning contractors walk back up the road to the Store; the dead do not return.
  - Reduced motion turns the extras off.
- **Drawn quest map (GDD 14):**
  - Nodes by region (rows) and tier (columns), with lines for prerequisites.
  - Every node has a marker so state never relies on colour: `[>]` next campaign job, `[+]` won, `[o]` open, `[x]` locked.
  - Reached regions show their jobs; the next region is a silhouette of "?" nodes; later regions stay hidden.
  - Tapping a node explains it. Hit areas meet the touch-target size.
  - The text map stays underneath as the accessible version.
  - The first version bunched every node at the left edge because the map was laid out before the panel had a width. The screenshot caught it; it's fixed, and the test now checks the spread.

## Post-roadmap batch 4: postgame and release groundwork (2026-10-06)

- **Postgame challenge contracts (GDD 14: "optional challenge variants using existing systems"):**
  - Fen Gauntlet, Quarry Siege, The Long Hunt and Shrine Vigil unlock after the Beacon.
  - They reuse existing threats in harder combinations, pay 115–130 g plus materials, and are repeatable.
  - Pure data: a `challenge` flag, validated to be repeatable and to require the finale.
  - Quest count is now 24, the top of the v1 ceiling.
- **Balance revisions 3 and 4.** As first authored, the challenges rated Good or Excellent with endgame gear (counters cap at +8). Tuned with the harness so each is Risky for a typical level-6 contractor and reaches Good at levels 7–10. Final difficulties: Long Hunt 34, Quarry Siege 35, Fen Gauntlet 40, Shrine Vigil 40. The harness allows challenges until level 10 (8 for ordinary jobs).
- **App icon:**
  - An original placeholder (a supply crate under the Beacon flame), generated by the art script and recorded in PROVENANCE.
  - Imported uncompressed; Unity derives every iOS size, including the 1024 px store icon.
  - Replace it with the final art.
- **Privacy manifest:** the export includes Unity's `PrivacyInfo.xcprivacy` files (they declare the engine's file-timestamp use). Game code uses no required-reason APIs, collects no data and does no tracking. The App Store privacy answers still need the user's confirmation.

## Mine dug into the ground (2026-10-11, user direction; GDD 6)

- **Superseded first attempt.** A procedural cutaway block pasted onto the town was rejected by the user, rightly: it ignored the town's own ground system. It is removed, together with its earthen-edge mesh and the custom shader both needed.
- **The user's decisions (asked before building):**
  - one level = one miner tall (1 world unit);
  - miners clear the level tile by tile;
  - the camera follows the dig down;
  - a 4×4 pit at the bottom-left edge.
- **How it works (`MineDigView`):**
  - The pit is part of the town's isometric grid. Its blocks are the ground tile with its own earthen skirt extended one level deep (`tile_block_0..2`, same palette as the ground tiles).
  - Each dug level is one Tilemap on the same grid, offset one level down.
  - The working level keeps its 16 pit blocks until cleared, front first so the cut faces the camera. Blocks removed = section progress × 16.
  - One section = one level: "Continue here" digs the next level in the same layer (same tint); "Dig deeper" digs the next level into the new layer.
  - The ring of ground behind the pit continues down as the walls, tinted by the layer each level was dug in. The open front exposes them.
  - The next three levels show darker as previews.
  - Only levels near the camera exist, so depth is unlimited (tested to level 31).
- **Simulation:** `SiteRecord.SectionDepths` records the depth of each completed section, saved as an optional field; older saves are inferred on load. No other rule changed.
- **Miners:** staging → ladder on the back wall → stand on the block being cleared (or the cleared floor while a choice waits) → back. They stay on the normal character layer; the open front means nothing occludes them.
- **Camera:** near the Mine it lowers as far as it can while keeping the supplier in view. The Mine panel's "Look down the shaft" frames the working level until the player moves. The view may extend below the town as far as the dig needs.
- **Labels:** "Depth N · level L" and what lies below sit in the open background left of the pit and slide to stay on screen.
- **Layout:** town tiles are removed only for the pit and the triangle in front of it and its walls; the Smelter and staging stay beside it.
- **Tests:** blocks clear one by one, front first; the miner is visible in the pit; continue and deeper each dig one level with the right tints; previews are darker; only nearby levels are built at level 31; the camera keeps the supplier in view and "look down" frames the dig; the pit blocks walking.

## Open blockers

- **iOS build:** the Unity → Xcode device export succeeded headlessly on 2026-10-06 (0 errors, 1 warning, IL2CPP, 114 s). The Xcode compile/sign/install step has not run yet: the startup disk had only ~2.5 GB free, too little for Xcode intermediates. To do: free disk space, export again (Ravenholm > Build > iOS Xcode Project (Device)), open `Builds/iOS-Device/Unity-iPhone.xcodeproj`, pick a signing team, then Run on the iPhone.

## Playtest feedback

- 2026-10-06: user: "next phase pls". Phase 9 built (balance, hardening, release docs). Release or submission needs separate explicit authorization. STOP for sign-off of the playable build.
- 2026-10-06: user: "keep going" after Phase 7 (no playtest notes yet). Phase 8 built except final art (needs the user's decision). STOP for presentation and usability approval.
- 2026-10-06: user: "keep going with next phase" (Phase 6 not yet playtested). Phase 7 built (world content and campaign). STOP for progression approval before Phase 8.
- 2026-10-06: user: "start next phase". Phase 6 built (Trader, economy, offline catch-up, recovery). This is the functional MVP boundary: STOP for playtest before Phase 7.
- 2026-10-06: Phase 1 approved by the user ("proceed"). No playtest notes given yet; the Phase 1 tuning questions (joystick comfort, camera, pickup cadence, capacity, walk speed, trip length, interior) carry over.
- Phases 2 and 3: built under the overnight authorization; installed on the user's iPhone 16 Pro on 2026-10-06 for playtesting.
- Phases 4 and 5: built 2026-10-06 under "keep going with the phases". STOP here: the GDD asks for an extended full-loop playtest (at least three 20–30 minute sessions) before Phase 6.
- 2026-10-06: user: "ok keep going with the phases" (no playtest notes yet). Plan: Phase 4, then Phase 5 (full core loop); stop after Phase 5 for the GDD's extended multi-session playtest.
- 2026-10-06 (night): the user explicitly authorized continuing through "one or two more phases" unattended ("feel free to do one or two more phases tonight while I sleep"). Plan: finish Phase 2, then Phase 3, each with its own checkpoint notes and commits; stop after Phase 3 at the latest. Playtest feedback for Phases 1 to 3 is still owed, and later tuning may redirect any of them.

- 2026-10-06: Phase 0 approved by the user ("keep going" → "Yes, start Phase 1"). No tuning notes given; the Phase 0 framing questions (view width, character scale, silhouettes, HUD footprint) carry over to the Phase 1 playtest.

## Evidence log template (GDD 29)

```
Build/commit:            Phase:            Device:
Save/seed:               Session length:
What the player tried:
Misunderstood:
Enjoyed:
Stalls / losses:
Proposed tuning changes:
Approval status:
```

## Mine: one block grid, walkable shaft, tunnels per level (2026-10-11)

Asked for: grids that line up, going down the ladder yourself, a camera that just follows, the shaft as a long corridor, and sideways digging at each level ("each level has its own", with dirt between the layers).

- **One block grid.** Every town cell is a column of ground blocks, one miner tall per level; level 1 is the town's own ground. `MineDigView` draws a block only when the camera can see it (an open front neighbour, or an open cell above), so the town's exposed edges now show as a cliff of layers on the same grid as the shaft. The pit's front cell sits on the town's bottom row, and the notch in front of it is cut on cell lines, so nothing overlaps.
- **Walkable shaft.** Push down at the ladder to climb on. On the ladder, up and down move along the shaft like a corridor; letting go settles on the nearest level, and sideways steps off. You can stop at the town (0), at every ledge (levels 2 and deeper), and at the pit floor. The context button offers "Climb down", which goes to the floor, and "Climb up", which goes to the town. Underground the player uses the same plane coordinates, drawn one level lower per level. A reload puts you back at the ladder top.
- **Dirt between layers.** At every open level below the town's ground, a dirt ledge (a slab 0.25 tall, using the town's own dirt tile) stays along the shaft's back walls, joining the ladder to both tunnel mouths. Each tunnel block stands on its own slab, so stacked tunnels stay separated by dirt.
- **Tunnels (changes a GDD default).** The GDD default is that older layers close once you dig deeper. Now each level from 2 down can have one tunnel per side, dug along the exposed front in that level's own layer, alongside the shaft.
  - A tunnel advances in stretches of 4 blocks, each worth 6 units, and each stretch is one decision ("Extend").
  - Effort is the same per block as a shaft section. It starts one continuation step above the level's own section, so a tunnel is never cheaper than continuing there, and rises by one step per stretch.
  - Miners are assigned per face: the shaft or one of the tunnels. Reassigning a working miner sends them up with their pack first.
  - Starting a tunnel in the mine sends a miner. The first choice is one whose tunnel is waiting, then a spare shaft miner, then the only shaft miner, with a toast saying the shaft waits.
  - The Mine panel lists the tunnels, can extend them, and can send miners between faces.
- **Camera.** The near-Mine framing rule is removed; the camera follows the player everywhere, and its bottom limit extends as deep as anyone can stand. "Look down the shaft" still frames the working level.
- **Saves.** These are optional fields (`tunnels`, worker `face`), so the schema is not bumped. Older saves load with no tunnels and every miner on the shaft.

## Directions: Mine down and left, Forest left and up (2026-10-11)

Direction from the user: down is progression and left is staying at a depth, for the Mine; left is progression and up is staying in an area, for the Forest. Both run in the same direction, so the two gathering sites read as one track. Each layer has three levels, and left is unlimited.

- **Mine, change to a locked mechanic (approved).**
  - Dig Deeper sinks the shaft through the next layer. A layer is `levelsPerBand` levels, 3 by default and set on SiteDefinition; level 1 is the town's own ground. The shaft section for depth 1 also opens level 1.
  - Continue Here digs left. A waiting tunnel goes one stretch further. Once the shaft is through a layer, it starts the tunnel on the layer's next free level and sends the shaft's miners there.
  - A tunnel stretch is one full section: same durability curve, 24 units. Each level's tunnel has its own continuation index, starting at k = 1. So a layer's three levels can be worked by three miners side by side, and older layers stay open.
  - The site waits for you only while some miner waits on a finished face. Nothing is ever decided automatically.
  - `SectionsCompleted` now means the number of layers the shaft has been sunk through.
- **Tunnels run left only.** They run along the bottom edge, recessed one row behind the cliff face. The blocks in front are cut down to a sill (`SillRise` 0.5), and the floor is a dirt slab.
  - The ladder moved to the shaft's right back wall. A ledge along the back row joins it to the tunnel mouths, and the notch is open on the shaft's right side only.
- **The frontier.** The town's ground continues `frontierLength` (48) units to the left on the same grid, with the same bottom edge. Camera and player bounds use `SandboxSettings.WorldBounds`. To keep going further, raise `frontierLength` as more is charted. Nothing in the code caps the number of layers or the tunnel length.
- **Forest (`ForestGridView`, replacing the tree pocket).** The frontier is a dense grid of trees, one per tile, directly above the tunnels.
  - Areas are 2-unit columns going left (Push Deeper). Clearings go up the column (Continue Here), cut from the bottom.
  - Standing trees block walking. Woodcutters walk only cut ground: out along the bottom edge, which every area's first clearing has cut, then up the cut column.
  - The Forest's own rules (6 trees, 4 units each, bands) are unchanged.
- **Relocations.** The Mine moved to the town's bottom-left corner. The Lumbermill (−8.6, 0.9) and Forest staging (−12.3, −4.9) moved next to the frontier, and the Forest gate is at the frontier trail.
- **Saves.** Mine saves from before this layout (`layout` < 2) convert on load:
  - the layer in progress becomes the shaft sinking through it;
  - old tunnels close, and their waiting units move to the shaft face;
  - every miner returns to the shaft.

  Nothing owned is lost. The schema is not bumped.

## Town on the grid, option A look (2026-10-11)

The user picked concept A (`Docs/Concepts/mine_layers_option_a.png`) and asked for the town to be set up so its angle works.

- **The town is a rectangle of grid cells** (`SandboxSettings.townCells`), so every edge is a straight cliff face.
  - The Mine's shaft (`mineCell`) stands out from the town's left corner.
  - The frontier (`frontierLengthCells` × `frontierWidthCells`) continues the shaft's left face beyond the town's upper-left edge.
  - Tunnels run straight along the grid (up-left on screen) one row in from that face, with a one-tile sill in front.
  - The Forest stands on the frontier right above them. Areas go along the edge ("left"), and clearings go back from it ("up"), with no town in the way.
  - Walkable space is decided by the tiles; `WorldBounds` is only the bounding box for the camera and the player.
- **Relocations.** The Smelter (−14.5, −1.5), Lumbermill (−11.6, 3.2), Forest gate (−15.5, 3.0) and both staging piles moved next to the shaft and the Forest trail. Two placeholder rock faces moved to the town's right side.
- **The look.**
  - Ground blocks are plain rock with no outlines, so solid earth reads as one mass.
  - Each layer has a clear colour, and a seam line marks each layer's first level. Level 1 is topsoil.
  - Everything inside the dig (shaft walls, tunnel back walls, floors) is shaded dark, so every opening reads at a glance.
  - Floors, ledges and sills are thin blocks of the same rock.
  - Tunnels are timbered at every stretch, with a lamp at the working face.
  - Ledges stay only where needed: levels with a tunnel, each opened layer's next free level, and the pit floor. The ladder stops at those levels.
