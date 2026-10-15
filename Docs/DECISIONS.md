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

## Solo play, stage 1 (2026-10-11)

Direction from the user: the supplier is the only character working; they cut and mine. When going AFK, they put themselves on one job. Plan: `Docs/SOLO_ROADMAP.md`.

- **No hired crew.**
  - Content: starting workers, recruits, crew-expansion upgrades and their assets are removed (`ProjectSetup.ApplySoloPlay`, idempotent).
  - Older saves: hired workers are taken off their sites on load. What they carried stays at the face or tree to collect.
- **Working by hand.**
  - Hold the context button to work: `Inspectable.Holdable` and `OnHold`, driven by the HUD's `HoldSensor`.
  - Mine: the shaft is worked from its rim or floor (`MineActionSpot.Kind.Shaft`), and a tunnel at its face. Forest: the nearest tree of the current clearing (`ForestChopSpot`).
  - It is the same section and tree simulation as before (`SiteSimulator.PlayerMine` and `PlayerChop`): units release at the same thresholds and go straight into the carry (`ISiteLedger.TryCarry`). With a full carry they wait at the face or stump; nothing is lost.
  - With nobody working a site, the Mine waits for you only once nothing is left to dig.
  - Work targets win the context button over plain inspection (`ContextPriority`).
- **Routines.**
  - `SitesService.StartRoutine(site, face)` adds the supplier to the site as its one worker (`worker.player`), so the existing simulation, offline catch-up and 8-hour cap apply unchanged.
  - The supplier's figure (`WorkerView.PlayerAvatar`) does the work on screen while the player is hidden, and the camera follows it.
  - Moving takes over: the routine ends, and what it carried returns to the carry, else staging.
  - Routines are set from the site panel ("While you're away").
- **Gear.** Tools and packs are the supplier's own (`playerGear` save field), bought with `EconomyService.BuyPlayerEquipment`. Site equipment levels unlock tiers, using the best level across sites. The tool's damage per second sets mining and chopping speed.
- **Debug.** "Complete section" puts the finished section's units in staging when nobody works the site.
- **Tutorial.** Wording updated to match.

## Solo play, stage 2: prospecting (2026-10-12)

- **Veins** (`Domain/Sites/MineVeins`). Every block of the Mine's grid has fixed contents from the world seed.
  - Each non-baseline ore of a layer has a smooth noise field, stretched along the tunnel direction and across levels.
  - A block holds the ore whose field clears that ore's threshold by the most, or else it is plain rock. Thresholds come from the layer mix × `Density` (0.85), so a layer still yields its mix overall, but in streaks: the next block along is usually the same ore.
  - Pure and deterministic, and tested.
- **Digging by hand reads the block** (`SiteSimulator.PlayerMine(face, ms, dps, blockOre, plainRock)`).
  - The section rules are unchanged: same thresholds, same 24 units.
  - Each unit released is the block's ore, or the layer's baseline ore for plain rock, instead of a draw from the mix.
  - Each unit from an ore block has a `HandBonusChance` (0.5) of a bonus unit. Following a vein therefore pays about ×1.5 on those blocks; straight digging is about ×1.1–1.2.
  - Routines keep the plain average draw, so active play beats being away without being required.
  - The block being dug comes from the view (`MineDigView.CurrentBlock`), and its ore from `SitesService.OreAt` (world seed, plus the level's layer mix, revealed or generated).
- **Signs** (`MineProspectView`).
  - Flecks on exposed block faces within the lamp's light (`LampRadius` 2.6): strong in the ore's colour on ore blocks, faint beside a vein.
  - The prospector's Tap button, shown near the Mine with a 5 s cooldown: ore blocks within 4 cells and ±1 level ring with echo rings in the ore's colour, even behind the face, and a toast names what answered.
  - Blocks crack in three stages while dug.
- **Not yet built.** Lamp upgrades, the survey sketch, branching (choosing which block to dig), hard pockets and rare finds come in stage 4.

## Prototype: option A digging (2026-10-12)

This is a self-contained prototype (`MinePrototype` scene, `MiningPrototype`), opened from a signpost by the Mine or from the debug panel. It is for judging the feel before any big change, and it never touches the company save.

- **Scale.** Blocks are half a level tall, so the supplier is two blocks tall. A tunnel is 2 high × 1 deep: the face row is dug, and the row behind is its back wall.
- **Digging.** The stick aims: sideways at body height, up, down, or diagonally down. Holding Dig mines the highlighted block, which cracks as it goes.
- **Moving.** Single blocks are stepped up; holes are dropped into.
- **Ore and signs.** The real veins (world seed and the Mine's layer mixes) and flecks within the lamp's light. A tally shows what was dug.
- **Next.** If it is adopted, the real Mine moves to this model and stage 4 (branching) folds into it.

## The Mine is a dig slab (2026-10-12)

The option-A prototype is now the real Mine. The prototype scene, its signpost and the old shaft, ladder, tunnel and prospecting views are removed.

- **Where.** A slab of ground under the Mine's corner and the frontier (`DigSlab`: 8 thick, 120 long, 600 rows; 6 rows = one layer, three levels of 2 blocks). Its front is the open cliff face.
  - The ground's surface can be broken only at the 4×4 entry by the corner.
  - Topsoil elsewhere, including under the Forest's trees, cannot be dug.
- **Digging is the choice.** There are no Deeper or Continue buttons.
  - Going down reaches the next layer, which is revealed the first time it is dug into. It has new ores and harder rock.
  - Staying in a layer costs a little more every `BlocksPerContinuation` (8) blocks further along the cliff.
  - Each block is one unit of a section's work at that layer and continuation, so the economy keeps the section curve. Ore blocks are 25% harder.
- **By hand** (`MineSlabView`, `MineDigSpot`). The supplier digs into the entry, falls into holes and steps up single blocks. Stepping up onto the ground leaves the Mine; stairs are the way out, and ladders come later.
  - Targeting: the wall walked into or stood against (head height first, then legs). Otherwise, while standing still, the block underfoot.
  - Walking on with Dig held never digs the floor.
  - A tap picks any reachable block, and pressing and holding on a block digs it.
  - Units go straight into the carry.
  - Flecks, the Tap button and cracks carry over from prospecting.
- **Routines** carry on the tunnel last dug by hand (`DigFace`), two blocks high and straight ahead, hauling to staging.
  - A routine stops at the slab's edge, at ground it cannot dig, or at a drop.
  - With nothing dug yet, the routine button is not offered.
- **Saves.** Mine layout 3: dug blocks, the block in progress, the last dig and the routine's face. Older Mines convert to an untouched slab.
- **Drawing.** Every visible block is a pooled sprite sorted by x + y + row, and the supplier is sorted among them as the cube of their lower body. Marks on the surface's top blocks sort above the town's ground tiles.
- **Known.** Inside a tunnel behind the cliff face the supplier is hidden by the rock in front, because nothing is cut away (by request). A silhouette is a candidate follow-up.

## Prototype: the Mine as one slice of the cliff face (2026-10-12)

This is an experiment for Kevin to try; the real Mine (the dig slab) is unchanged. It is a self-contained `MinePrototype` scene (`CliffPrototype`), opened from the signpost by the Mine, and it never touches the save.

- **Why.** In the isometric view, one block down looks the same as one block back and one sideways, so the 8-thick slab hid depth and position. On a single slice, down on screen is always deeper, and nothing is ever in front of the supplier.
- **Controls.**
  - Left and right walk along the cliff; screen left goes on along it, as the real Mine does.
  - Down aims at the block underfoot and up at the block overhead.
  - Dig takes the wall walked into (head height, then legs), or, standing still, the block underfoot. Taps and presses still pick a block.
- **Ladders.** Digging straight down two or more blocks leaves a ladder in the shaft; a one-block dip is just stepped up. Up and down climb it, and its top holds the supplier, who walks off it sideways.
- **Readability.**
  - The tunnel's back wall is in shadow.
  - The side wall seen inside a hole gets a dark overlay (a runtime parallelogram sprite), and the ladder hangs on that wall.
  - A depth line reads "Depth N blocks · layer K".
  - The supplier is drawn at 0.85 scale so their head stays under the rock overhead.

## The Mine is the cliff-face slice (2026-10-12)

Kevin preferred the cliff-face prototype, so the real Mine now works that way, and the prototype scene, signpost and test are removed.

- **Domain.** `DigSlab.Thickness` is 1 and `EntryThickness` is 1, so the entry is the strip of dirt along the cliff edge (u 0..3).
  - Blocks dug straight down, from standing on them, are recorded in `SiteRecord.DugDown`.
  - `SiteSimulator.Ladder` holds where a shaft is two or more deep; a single dip is stepped out of.
  - Saves: `dugDown` triples, and Mine layout 4. Layout-3 slabs reset to untouched ground; revealed layers and earnings are kept.
- **Moving** (`MineSlabView`).
  - Left and right on the stick walk along the cliff at 3.2 blocks/s; up and down climb a ladder at 5 blocks/s, otherwise they aim.
  - A rung holds the supplier, except right after digging the block underfoot, when they go down to the new floor.
  - Stepping off a ladder's top onto the ground leaves the Mine.
  - The supplier is drawn at 0.85 scale underground.
- **Ladders.** Each unbroken run of ladder blocks is drawn as one ladder: a mesh of two rails and two rungs per block, lying flat on the shaft's visible wall (the side of the next block along).
- **Drawing.** The rock right behind the slice is drawn as the tunnels' back wall, in shadow. The side wall seen inside a hole gets a dark overlay. Floors and the face stay lit.
- **Later: the ore elevator.** When it is built it is a single track in its shaft, not ladder-like (Kevin, 2026-10-12).

## Painted art v1: ground, cliff blocks, trees (2026-10-12)

Three painted sheets from Kevin (brief: `Docs/ART_PROMPTS_ASTRA.md`) are cut into sprites by `Tools/ArtImport/import_painted_sheets.py`. To rerun it, use a Python environment with numpy, scipy and Pillow: `python Tools/ArtImport/import_painted_sheets.py ArtSource/painted-sheets-v1 Assets/Ravenholm/Art/Placeholder`.

- **Ground tiles.** The top diamond is masked to exactly 128 × 64, with the outline cropped so neighbouring tiles join; the earthy edge shows only at drops. They replace grass, flowered grass, forest floor, path, dug dirt and the plaza.
- **The Mine.** Painted blocks per row, untinted (`MineSlabView.EditorSetupBlocks`):
  - the grass-topped block for the top row and topsoil for row 1;
  - then one rock per layer: sandstone, red clay, slate, mossy, purple, ochre, basalt, limestone. The cycle repeats 15% darker each time past the eighth layer.
  - Unrevealed layers stay dimmed, and back walls stay in shadow.
- **Ore in the lamp's light** shows as that ore's painted block (`ResourceDefinition.mineBlock`: iron, copper, coal, moonsilver), sorted like the rock it replaces. Ore without a block (gem) keeps the flecks.
- **Trees and props.** Cedar is the tall pine, maple the leafy tree, ironwood the bent pine; the stump, herb bush and stone rock are replaced too. The extra props (ferns, logs, flowers, small pines) are cut and ready but not used yet.
- **Not yet.** Seamless joins between different ground tiles are not verified; repetition is visible on large grass areas.
- **Ground variety (2026-10-12).** The scene builder mixes the painted variants so the tile never reads as a grid:
  - grass is mostly plain, with 14% tufts and flowers in small patches (value noise, 5-cell scale);
  - paths get pebbles (18%) and footprints (8%);
  - the forest floor gets mossy soil (12%).

  The new tile keys are `grass_c`, `path_b`, `path_c` and `forest_floor_b`.
- **Character brief.** `Docs/ART_PROMPTS_CHARACTER.md` runs in three steps: design options, then a model sheet that needs Kevin's approval, then 13 animation strips (57 frames, left-facing only; the game mirrors them).
- **A flush cliff (2026-10-12, Kevin).**
  - The Mine's corner moved to the town's left edge (`SandboxSettings.mineCell` is now -20, 17, was -24), so the cliff is one straight face from the town along the frontier. Previously a 1-thick entry strip stood out with a gap behind it, and the player could get stuck there.
  - The entry is the dirt strip on that edge; dug blocks keep their slab coordinates.
  - Ladders now lie parallel to the cliff face (the blocks' lit left faces), in the shaft's opening.
  - Standing over air (an old save) puts the supplier back beside the entry when they move.

## The supplier's painted animations (2026-10-12)

The character Kevin approved (the older tailor in an apron, `ArtSource/character-v1/model-sheet.png`) replaces the placeholder body and its bob.

- **Import.** `Tools/ArtImport/import_character_strips.py` writes `Characters/Supplier/<clip>_<n>.png` at 256 px per unit (the importer now honours a sprite's own `ppu` in `sprite_meta.json`), plus `animations.json`. `ProjectSetup` rebuilds `Content/Config/SupplierAnimations.asset` (`CharacterAnimationSet`) from it.
- **Clips.** 17 clips:
  - idle and walk, front and back;
  - carry idle and walk, forklift-style, front and back;
  - mining: at a wall (front and back), down (front) and up (back);
  - chop, front and back;
  - climb (back), pick-up (front) and fall (front).
- **The art faces left.** "Front" faces the bottom-left and "back" the top-left; the animator mirrors them for the right.
- **Choosing a clip** (`SupplierAnimator`, on the body sprite):
  - The view follows the way he moves (up the screen shows the back). In the Mine it follows his facing along the cliff.
  - Working: mining is picked by the target's row (down, up or at a wall); chopping turns toward the tree.
  - The climb plays only while he moves on the ladder; the fall plays while dropping; the pick-up plays after a pick-up.
  - Carrying swaps idle and walk for the carry clips.
- **The motor** only sizes the body now (0.9 underground, since the art is one unit tall).
- **The carry pile** sits on the forearms in the carry poses: in front when he faces the camera, behind his body when he faces away. In other poses it rides on his head as before.
- **The routine figure** for the supplier uses the same animations: walking when it moves, mining or chopping at its work spot.
- **Sorting.** At ground level (the top of a ladder) the supplier sorts above the ground tiles, so he is no longer hidden by them.
- **Known.** The generator's walk cycles repeat some leg poses (see the handoff notes); they read fine at game speed, and a cleanup pass could redo them later.
- **Fixes after the first playtest (2026-10-12).**
  - **Forest layering.** Trees are made at runtime, and their renderers now sort by their pivot (the trunk's foot). Taller painted trees used to sort by their centre, so stumps and the supplier drew over trees in front of them.
  - **Leaving the Mine.**
    - Up on the stick at the top of a ladder (or in a dip one block deep) climbs out onto the ground behind the shaft, else beside it.
    - A bug placed the supplier back at their underground spot in the same frame they left, which could strand them in front of the cliff.
    - From the ground, only the Mine's entry blocks can be targeted.
  - **Animations.** The generated strips are not clean cycles (the legs do not alternate, and the tools jump sides between frames), so each clip plays key poses with timing:
    - idle: one pose that breathes;
    - walk: two steps with a bounce, aligned on the head;
    - mining and chopping: a held wind-up, then a fast strike;
    - climb: two alternating holds.

    A redo prompt for proper 4-pose walk cycles is in `Docs/ART_PROMPTS_CHARACTER.md`.

## The Forest is a grid of trees: cut any tree in reach (2026-10-12)

Kevin's call: every tree in reach can be cut with the main button. Cutting is the choice, as digging is in the Mine, so the clearings and the Continue / Push Deeper choices are gone. A routine keeps cutting near your last tree.

- **Domain** (`SiteSimulator.Grove.cs`, `ForestGrove`, `GroveCell`).
  - A tree is an (area, slot): areas run left along the cliff (`AreaLength` 3 tiles each), and slots count back from the cliff edge, row by row. This matches `ForestGridView`'s layout.
  - Each tree's species comes from the area's mix, drawn once per tree from the world seed, so a tree is what it is before it is cut.
  - Effort: a tree's durability is a clearing tree's. The continuation rises every `TreesPerContinuation` (6) slots back, plus one per area past the charted ones.
  - Each tree gives `UnitsPerTree` logs as it is cut, each with the area's rare (herb) roll, also seeded per tree.
  - The first tree cut in an area reveals it.
- **By hand.** The logs go straight into the carry; overflow waits at the site and comes along with the next swing. The Chop button offers the nearest standing tree whose cutting spot is within reach (1.6). Standing trees still block walking, so you cut your way in.
- **Routines.** A routine cuts the last tree if it still stands, else the nearest standing tree within 8 grid steps. Ties stay in the same area, so a routine never walks into a new area on its own; then the cliff edge, then toward the town. It hauls to the forest staging and works on face 0, like the Mine's routine.
- **Saves.** `chopped` holds (area, slot, micro-damage) triples, plus `lastChop`; the Forest layout is now 2. Older saves convert: earlier clearings become stumps (six slots per clearing), the current clearing's trees keep their progress, and their waiting logs are kept.
- **Old model kept for tests.** The clearings model is still in the domain when a config has no `Grove`.

## The elevator and scaffolding replace ladders (2026-10-13)

Kevin's calls: no more ladders. The Mine has an elevator shaft from the start, its platform always at your floor; scaffolding (1 lumber each, from a Build button) lets you climb and bridge anywhere else.

- **Elevator.**
  - The shaft is `DigSlab.ElevatorU` (0, the cliff corner by the town), opened by `SiteSimulator.EnsureElevatorShaft` on new games and older saves. It runs down through the topsoil and the first layer (`ElevatorStartRows` 8), and opening it gives no ore.
  - Digging the shaft's floor extends it (`ElevatorBottom`).
  - In the shaft the platform holds the supplier at any floor, and up and down ride it (6 floors/s). Up at the top steps out onto the ground. While riding, the supplier stands; there is no climb pose.
  - The platform is drawn under the supplier in the shaft. Otherwise it glides to the floor they are on, or to the top when they are above ground. A pulley frame stands over the shaft with its rope.
- **Scaffolding.**
  - `SiteRecord.Scaffolds` maps each scaffold to the material it used. A scaffold goes only in open space, never in the elevator shaft, and not where the supplier's body is.
  - It is stood on and stepped up like rock (`Standable`), but it is not dug and does not hide what is behind it.
  - It costs `SiteDefinition.scaffoldCost` (1) of any of `scaffoldMaterials` (cedar, maple or ironwood lumber) from the town's stock. Taking one down refunds it.
  - Controls: the Build toggle (above Tap, in the Mine) outlines open spaces in reach in green and scaffolds in amber; a tap places or takes down.
  - A routine treats scaffolds as blocking, and as floor to walk on.
- **Ladders removed.** `DugDown` is still recorded and saved, but unused. Shafts dug elsewhere have no ladder: build scaffolds to climb out.
- **Art.** The scaffold, pulley and platform are procedural placeholders (`Props/prop_scaffold`, `prop_pulley`, `prop_lift_platform`).
- **Digging right (2026-10-13, Kevin).** The cliff can be dug to the right of the elevator corner for a limited stretch (`DigSlab.MinU` = −12), under the town's own edge. Effort rises the same either way along the cliff (|u| per continuation). The surface there stays town ground: the entry is still only its four blocks (u 0..3).

## Surface outcrops: mining above ground (2026-10-13, Kevin)

- **Layout.** The scene builder places two outcrops of rock on open town grass, one on each side of town (18 columns now). Each is a blob of 7–10 columns, up to three blocks high, tallest in the middle. Spots are searched near preferred points; every cell needs grass on all sides and no collider within 1.1. The layout lives in the scene (`SurfaceOutcropView.columns`).
- **Domain.** The outcrops are the Mine's first-layer rock with their own ore veins (`SiteSimulator.SurfaceOreAt`). They are broken by hand like a Mine block (`PlayerBreakSurface`), with the same effort and ore bonus, straight into the carry. Broken blocks are saved as `surfaceBroken` (x, y, −level).
- **Play.** Within reach, the main button offers "Mine" (`OutcropSpot`), which breaks the top block first and plays the pick swing; cracks show while a block is dug. Ore blocks show their painted ore. A column with blocks left can't be walked through.

## Prototype: forest mountains (2026-10-13)

This is an experiment for Kevin: mountains above ground to climb, or to cut through to reach more forest. It is a self-contained `MountainPrototype` scene, opened from the signpost by the forest gate, and it never touches the save.

- **The plot.** A 20 × 20 grid of half-block voxels (bedrock floor at level 0). A ridge of rock runs across it, rising seven blocks above the floor, mostly in steps of two; at rows 4–5 a gentler pass climbs one block per step. Forest stands on both sides (leafy trees beyond the ridge) and thinner on the slopes; ore shows in the rock.
- **Moving.** The supplier steps up one block at a time and drops down any height. A step of two or more, or a tree, is a wall.
- **The button.** It chops the tree faced. Otherwise it digs the wall faced (head height, then legs), or, standing still, the block underfoot. What is gathered is only tallied (no economy).
- **Seeing.** Blocks and trees drawn in front of the supplier that overlap them on screen turn see-through (35%); nothing is hidden.
- **Animation.** It uses the supplier's painted clips (walk, idle, mine, mine down, chop).
- **Bigger plot and stumps (2026-10-13, Kevin; still the prototype, ahead of replacing the real forest).**
  - **The plot.** 48 × 48 cells of mountains from value noise: peaks up to seven blocks, slopes of one and of two, with forest valleys between them and an open, flat start. Only the cells in the screen's diamond (plus room for tall columns) are drawn.
  - **Stumps.** A felled tree leaves a stump. Facing it, the button clears it (one log), and a cleared spot never grows back. A stump left standing grows back into the same kind of tree after `RegrowSeconds` (60 s in the prototype), but not while the supplier stands on it.

## The Forest has mountains, shadows and regrowing stumps (2026-10-13, Kevin)

The forest-mountains prototype is now the real Forest; the prototype scene, its signpost and its test are removed.

- **Terrain** (`ForestGrove.TerrainHeight`, from the world seed). Kept practical: most ground is flat, with small hills of 1–3 blocks.
  - Every 24 tiles along the cliff (from tile 14) a ridge runs back from the edge. It is walls of two blocks rising to six, bare rock above four, where no tree grows (`IsRock`).
  - By the cliff edge each ridge has a pass of one block a step (top at three), so the way along the edge can always be climbed.
  - The cliff edge's own row has no hills.
- **Digging the rock.** The Forest's blocks are the Mine's surface rock (`PlayerBreakSurface`), with its ore veins, straight into the carry. `ForestRockSpot` targets the wall faced (head, then legs), or the block underfoot when standing still; never under a tree or stump. Trees and stumps in reach usually win the button.
- **Moving** (`ForestGridView.CanEnter` / `Commit`). The supplier steps up one block, drops any height, and is stopped by walls of two. They are drawn up on the blocks (`PlayerMotor.Elevation`) while the walking plane stays the ground; a teleport into rock puts them on top.
- **Drawing.** The Forest's blocks, its trees and the supplier inside it sort on the Ground layer, above the town's tiles, by cell and level (`TerrainOrder` = 20000 + 4·(level − x − y)). Only the tiles in view are drawn.
- **Shadows** (to read where blocks are). A surface just past a taller neighbour toward the light (up-left on screen) gets a soft shadow, stronger the taller the neighbour. Ground under an overhang (rock left above a dug space) gets a dark one.
- **Stumps.**
  - A felled tree's stump grows back into the same tree after `SiteDefinition.treeRegrowSeconds` (300 s); this runs in simulation time, so it also happens while you are away.
  - Facing a stump with no tree in reach, the button offers "Clear": 0.8 s for a log, and that spot never grows back (`Cleared`).
  - Saves keep `stumps` (felled-at times) and `cleared`.
  - A routine cuts only standing trees; regrown ones count again.
- **A sparser forest (2026-10-13, Kevin).** About a third of the Forest's spots (`ForestGrove.OpenShare` 0.33, scattered from the world seed) are open ground with no tree, so there is room to move. The cliff edge's row stays wooded. Open ground counts like bare rock (`IsRock`): no tree, no stump, nothing for a routine to cut.

## The Mine is an open-pit quarry (2026-10-13, Kevin)

The underground cliff-face tunnel and the elevator are gone. The Mine is now a plot of ground at the cliff corner, dug down from above.

- **The plot.** `DigSlab` is 5 tiles back from the cliff edge (`Thickness`) by 6 tiles along it (`MinU` −2 to `Length` − 1 = 3), as deep as the layers go. Its whole surface can be broken into (dirt tiles mark it), and the Mine's sign stands beside it. Layer progression (new ores, harder rock) still comes with depth. Mine layout 5: older Mines reset to untouched ground, keeping revealed layers.
- **Moving.** In the pit the stick walks on the grid as in town, in all four directions. The supplier steps up one block, drops any height, and climbs out by stepping up onto the ground (steps they leave, or scaffolds). Walking off the cliff's face is never possible.
- **Seeing in.** Down in the pit (two or more blocks), rock drawn in front of the supplier that overlaps them turns see-through. The pit's far walls get a shadowed face, as its side walls do.
- **Scaffolds** work as before. A tap where two open spaces line up on screen picks the deeper one.
- **Kept but off.** The elevator code stays (`ElevatorStartRows` 0 opens none), and so does routine digging, which carries on the last cut, two high, straight ahead.
- **Known.** Cuts dug under the surface (with ground left overhead) are seen only through the see-through rock.

## The supplier is the dwarf (2026-10-13, Kevin)

Astra's dwarf (`ArtSource/dwarf-v1`, imported by `Tools/ArtImport/import_dwarf.py`) replaces the painted tailor.

- **What is painted.** He has eight directions (N, NE, E, SE, S, SW, W, NW): a five-frame walk at 150 ms a frame and one standing pose per direction. All are anchored on one ground point, so the feet stay planted, and none are mirrored. He is 0.85 units tall, at 240 px per unit.
- **Facing.** The way he moves or works picks one of the eight, split evenly once the iso squash is undone. Moving along the grid's axes therefore faces a diagonal, and straight up the screen faces N.
- **Not painted yet: carry, mine, chop, climb, pick-up.**
  - These play the standing or walking pose of his facing, with a procedural motion. Tools get a wind-up (0.26 s) and then a strike (0.14 s); a pick-up is a dip.
  - The pile rides on his head (`SupplierAnimator.HeadTop`).
  - The tailor's frames stay in the project, unused. `import_character_strips.py` can bring them back.
- **Body clearance (from Kevin's screenshot).** Walking only checked the feet's own cell, so the wide body could stand with its feet on a cell's edge, half inside a cliff or a trunk. The feet now keep `PlayerMotor.BodyClearance` (0.3 of a cell along the grid's axes) from any cell they cannot enter. A move may never press further in than the body already does, so he can always walk away after a teleport. Holes into the quarry stay open for this check.
- **Walk speed and steps (2026-10-13, Kevin).**
  - The walk is a little slower: `BalanceProfile.walkSpeed` goes from 3.5 to 2.6 units/s, and the quarry's from 3.2 to 2.4 blocks/s.
  - The dwarf's painted shuffle plants its feet at only about 0.19 units/s, so its playback now follows the ground actually covered: one cycle per `CharacterAnimationSet.walkUnitsPerCycle` (1 unit). At full speed that is about twice the painted timing (0.38 s a cycle). A half push walks with slower steps.
  - Astra is asked separately for a longer-stride walk, which would plant the feet fully.
- **The swing walk with planted feet (2026-10-13, Kevin; supersedes the walk-speed note above).**
  - Astra's v2 walk (`ArtSource/dwarf-v2-swing`) has a longer stride, with the body sway and arm swing baked into the frames: 5 frames of 90 ms, a 450 ms loop. Nothing is added on top: no bob, rotation or warp. Idle uses `dwarf-idle.png` and keeps the last facing.
  - **Speed comes from the art.** Each walk clip carries its painted planted-feet velocity from `sprite-data.json` (px/s ÷ 240 px per unit). That is 0.62 units/s sideways and 0.31 up or down: an ellipse, because the camera is elevated.
  - The supplier moves at that velocity × `CharacterAnimationSet.walkPlaybackRate` × drawn size (`PlayerMotor.WalkVelocity`). Kevin chose **2×**: 45 ms frames, about 1.23 units/s sideways and 0.62 up or down. The quarry turns the same velocity into grid steps.
  - `BalanceProfile.walkSpeed` (2.6) is now only the fallback for a character without painted speeds.
  - A diagonal push on the stick moves along a grid axis and faces the diagonal clip.
  - Playback follows the ground actually covered: a half push plays half as fast, so the feet stay planted. Turning while walking keeps the step's phase.
  - **Known:** the feet are exact at frame boundaries. Between them a held frame glides with the continuous movement (by Astra's note).
- **The longer stride (2026-10-13, Kevin: "too slow with too many steps"; supersedes the 2× note above).**
  - Astra's v2 steps cover only about 0.14 units. Planted feet at a decent speed meant about 9 steps a second.
  - The walk is re-rendered from her cutout rig with twice the stride, a higher lift and more arm swing (`ArtSource/dwarf-v3-stride`, `render_v.py` with `STRIDE=80 LIFT=18 ARM=1.8`). Her standing poses are unchanged.
  - Playback is 1.3× (69 ms frames, about 5.8 steps a second).
  - `walkGroundFactor` 1.25 lets the ground run a quarter ahead of the planted feet. That gives about 2.0 units/s sideways and 1.0 up or down.
  - Both factors are tunable on `SupplierAnimations.asset`.
- **The legs swing from the hip (2026-10-13, Kevin: sliding rigid legs back and forth "is obviously not ok").**
  - Walk v4 (`ArtSource/dwarf-v4-hip-swing`, `render_swing.py`) rotates each leg about a hip pivot.
  - Seen from the side, the leg tilts in the screen plane. Facing toward or away from the camera, it foreshortens.
  - The planted foot stays on its floor point, and the hips dip at the ends of the stride (the pendulum).
  - A small knee bend at mid-swing (the leg is 12% shorter) lets the boot clear the floor.
  - The stride, speeds and timing are unchanged from v3.

## The redesigned supplier (2026-10-14, Kevin approved Astra's design)

The painted dwarf was unreadable in game, so he is replaced by Astra's approved redesign (`ArtSource/dwarf-v5-approved`): an orange beanie, blue shirt, cream apron, flat colours and thick outlines, matching the world.

- **Walk.** Her rig has separate thigh, shin and foot pieces, with two-bone knee bends in the side and diagonal views and depth overlap in the front and back views. It is re-rendered with 1.6× her stride (`STRIDE=64`). 2× overextends the knees.
- **Size.** He is drawn about one unit tall (210 px per unit, about 15% bigger than before) so he reads on a phone.
- **Speed.** `walkPlaybackRate` 1.4 (about 6 steps a second) and `walkGroundFactor` 1.25 give about 2.0 units/s sideways and 1.0 up or down, as before.
- **Unchanged.** Idle keeps the last facing. Work poses are still the procedural lean on the standing pose. The pile rides on his head.
- **A closer camera and hip-swinging legs (2026-10-14, Kevin).**
  - The camera's view widths are now 6, 7 (the default) and 8.5 units, instead of 7, 8.5 and 10. The default is about 20% closer, so the supplier is a bigger part of the screen at the same scale as the world.
  - The walk (`ArtSource/dwarf-v6-hip-swing`) swings each leg from the hip, built from Astra's separate thigh, shin and foot parts, with a knee bend on the swing.
  - The legs walk on a narrower track in the front and back views, so he no longer straddles.

## Carried-item art and pile spacing (2026-10-14)

- **Art.** Astra's 41 stack sprites (`ArtSource/stacks-v1`) replace the placeholders under the same filenames, so every material and crafted item keeps its mapping. They are 256 × 192 canvases at 512 px per unit (the same size on screen as before), pivot (0.5, 1/12), full-rect mesh, bilinear, clamp, no mipmaps, compressed HQ. The same sprites show in the carried pile, on the racks, in containers and staging, in the routine figure's pack and on delivery arcs.
- **Per-item spacing.**
  - Each definition has a `stackStep`, filled by setup from `Stacks/stack_spacing.json` (Astra's measured values; bulky pieces 0.13, planks about 0.09, blades about 0.08, bows down to 0.048).
  - Pile heights accumulate. Each piece sits on top of the one below, as high as that lower piece's step.
  - Astra's suggested starting rule, the smaller of the two steps, was tried in game. It sank bulky pieces into thin ones (a coat on a bow) and thin pieces into bulky ones, so it was replaced.
- **Unchanged.** One sprite per unit, the 8-piece display limit, the count badge, the ±0.02 offsets, the 1.25 pile scale, delivery arcs and the 10% drop-off enlargement. The pile still rides on the head until a carrying pose exists.

## The game-drawn outline (2026-10-14, Kevin)

New art is drawn without outlines. The game draws one outline around everything, so its thickness is the same across all assets.

- **How.**
  - `SpriteOutline` (on an object with a sorting group) keeps, behind each outlined piece, a copy drawn with `Ravenholm/SpriteOutlineSilhouette`: the piece's silhouette grown by the outline's thickness, in one colour.
  - All the copies sit at the very back of the object's sorting group, so the pieces cover each other's inner edges. Only the object's outer silhouette is lined, even for a character made of a body, boots, hands and rubber-hose limbs.
- **Settings** (`OutlineSettings`, on `SandboxSettings`): colour (the world's dark brown, converted for the linear colour space) and thickness in screen pixels.
  - The thickness is 4 px on a 2622 px tall screen, scaled with the world camera's pixel height.
  - It is the same on every sprite whatever its resolution or size; the PlayMode test measures this.
- **Switched on per sprite.** Only sprites flagged `"outline": true` in `sprite_meta.json` are outlined (setup collects them into the settings). Today none are; the existing art keeps its painted lines until outline-free versions replace it. The supplier and his routine figure already carry the component, for the coming character.
- **Art requirements.** Outline-free art needs a transparent margin around each piece (Astra's brief asks for 12 px) and a full-rect mesh, because the line is drawn inside the sprite's rectangle.
- **Known.** Where two pieces of one object overlap with soft (anti-aliased) edges, a hairline seam can show faintly.

## The rubber-hose supplier (2026-10-14, Kevin chose Astra's General Store Owner)

The supplier is now Astra's General Store Owner (`ArtSource/storeowner-v1`): flat colours, no painted outlines. The game draws his arms and legs live as bendy hoses (`HoseRig`, `HoseCharacterDefinition`, `Content/Config/StoreOwner.asset`, built by setup from `points.json`).

- **Parts.**
  - 5 painted views (S, SE, E, NE, N; SW, W and NW are mirrors), 10 boots, 9 hands, a pickaxe and an axe. All share one scale of 340 px per unit, so he stands about 1.07 units tall.
  - Pivots: the ground point (256, 444) for bodies and boots, the wrist for hands, the grip for tools.
  - All of them get the game's outline. The hoses draw their own outline at the same thickness, at the back of his sorting group, so only his outer silhouette is lined.
- **Walking.**
  - The phase follows the ground actually covered, so the supporting foot stays planted at any speed (the PlayMode test measures it).
  - The stride is 0.62 units, scaled 0.75–1.15× with speed. At 2 units/s that is about 6 steps a second.
  - His legs are short (0.17 units from hip to ankle), so the hoses stretch a little at full stride, the rubber-hose way, and bow (knees forward, elbows back) when compressed.
  - The body bounces over each step, and the hands swing against the feet.
- **Poses.**
  - Carry: hands out at belly height; the pile rests on them through `CarryAnchor`, the existing anchor path.
  - Mining and chopping: the tool swings from the near hand (a 0.26 s wind-up, then a 0.14 s strike).
  - Also pick-up, fall and climb.
- **Integration.** `SupplierAnimator` still decides the pose and facing (its clip names, timing and tests are unchanged) and drives the rig. The old frame body is kept hidden for that bookkeeping. The routine figure uses the same rig.
- **Known.**
  - The hands are small natural hands, not the big gloves the brief asked for.
  - A full 8-piece pile hides his face from the front.
  - The hoses' bow and stretch, and the poses, are first-pass tuning.
- **The pile on his back, and tapered hoses (2026-10-14, Kevin).**
  - The carried pile rests on a minimal L pack he always wears: a bar up his back and a shelf behind him at hip height, with a crosspiece. All of it is drawn as straight outlined tubes in wood brown (`packColor`, `packWidth`, `shelfDepth`).
  - His hands stay free, so he walks, swings and gathers while carrying.
  - The pile draws behind his body, except when his back faces the camera (N and NE and their mirrors).
  - The hoses are wider and taper. Arms go from 1.45× the art's width at the shoulder to 0.85× at the wrist (26 to 15 px), legs from 1.5× at the hip to 0.85× at the ankle (30 to 17 px). Setup applies this once, and later edits are kept.
- **Playtest fixes (2026-10-14, Kevin).**
  - Relaxed hands hang thumb edge on in every view; palms no longer face out.
  - The pack shows only while he carries something.
  - The side view's arms come out of the painted sleeve's opening and hang straight down, a touch forward. Her anchors hung the near arm behind his back; setup overrides them for the E view.
  - Mining and chopping are a two-handed overhead swing: the near hand at the tool's grip, the other further down the handle.
    - The tool is raised up behind his head (held 0.26 s), with enough reach that its head clears his hat from the front, then brought down in front in a fast strike (0.14 s): to the wall, to the floor (mining down) or up at a block.
    - The pick's point and the axe's blade lead the downward swing.
    - The swing draws in front of him except while the tool is raised behind his head. Seen from behind, it is the other way round: the wind-up comes toward the camera and stays visible.
- **The swing, rebuilt from references (2026-10-14, Kevin: "the swing motion doesn't make sense").**
  - **The bug.** The old arc went from raised-behind down through hanging-down to the front: an underhand pendulum, not an overhead chop.
  - **The arc.** The angle is now measured from straight up, so the strike passes over his head. It rests low in front (150°), is lifted up behind his head (-35°), then comes over the top to the wall (95°), the floor (135°) or a block above (55°).
  - **3D swing plane.** The swing runs in his own space (forward, his right, up) and is projected into the view. The tool foreshortens along its handle as it points toward or away from the camera, instead of spinning flat on screen (the LPC review's point).
    - It draws in front of his body when its head is nearer the camera than his middle.
    - Chopping tips the plane 50° toward his right: a diagonal stroke from over the shoulder into the trunk.
  - **Timing** (a 0.46 s cycle; "put your back into it"):
    - wind-up 0.2 s, leaning back;
    - strike 0.07 s;
    - hold at impact 0.07 s, with a small shake and a dip;
    - recovery 0.12 s.
  - **Body.** It leans back 6° on the wind-up and into the strike, about the hips (side views).
  - **References.**
    - OpenGameArt, "LPC improved animations for axe, pickaxe, warhammer": chop is a top-to-bottom smash; the tool needs 3D perspective, not 2D rotation; facing the camera is the hard direction.
    - Aseprite community, a mining-animation critique: lean back, swing down and forward, add recovery.
    - Slynyrd Pixelblog 9, melee attacks: a slower wind-up for heavy tools, and the follow-through held a beat.
- **Sleeves are part of the arms (2026-10-14, Kevin).**
  - The painted sleeves are taken off the body pieces (`Tools/ArtImport/strip_sleeves.py`): cream outside each view's torso core is removed. In profile, where the sleeve was painted over his back, it is flattened into plain shirt. Astra's originals stay in `ArtSource/storeowner-v1`.
  - Each arm draws its sleeve as the top 36% of the hose: wider, in shirt cream, outlined (`sleeveColor`, `sleeveWidth`, `sleeveLength`). The sleeve moves with the arm.
  - Head-on and from behind, the arms hang beside his body, over its edges, instead of behind it. The hands no longer stick out of a painted sleeve with no arm attached.
- **The bouncy cartoon walk (2026-10-14, Kevin: the walk felt "stiff and unnatural", he chose bouncy cartoon).**
  - **Poses.** The walk is built on the four classic poses (contact, down, passing, up).
  - **Gait.** Each foot is down half the cycle (`duty` 0.5) and stays planted (the phase follows the ground covered). The stride is 0.9 units (scaled 0.8–1.1× with speed), about 4.4 steps a second at walking speed instead of 6.
  - **Body.**
    - A smooth double bob (`bounce` 0.035): lowest just after each heel strike, highest at passing, with squash and stretch (`squash` 4%).
    - Weight shifts toward the supporting foot (`sway`).
    - A 5° forward lean in the side views, and a 3° side-to-side tilt head-on.
  - **Feet.** The boots roll: toe up at heel strike, heel up at push-off (`footRoll` 16°).
  - **Arms.** Bigger arcs (`armSwing` 0.1) that rise at the ends, following springs (3.2 Hz, damping 0.55) so they trail and overshoot.
  - **Legs.** The hose bow grows linearly as the ends come together, so it no longer snaps as a leg straightens.
  - **Starts, stops and turns.** He eases into and out of the walk (the rig settles in about 0.13 s). The motor accelerates at 20 units/s² and stops at 70% of that. A quick squash covers a change of view.
- **Shorter-looking legs, fewer bounces (2026-10-14, Kevin; he chose to let the feet glide a little).**
  - A leg may stretch to at most 1.3× its length (`maxLegStretch`). Beyond that the feet reach no further and glide a little; before, they stretched to about 1.6×.
  - His hips ride slightly lower while walking (`walkCrouch` 0.025, a small knee bend), so the legs reach further without stretching.
  - The stride is 1.1 units: about 3.6 steps (and bobs) a second at walking speed instead of 4.4. The bob is gentler too (`bounce` 0.022, `squash` 2.5%).
  - The planted-feet test now checks that the supporting foot glides less than half as far as he walks.
- **The walk rebuilt on the spec (2026-10-14; `Docs/ANIMATION_SPEC.md`; Kevin left the speed to Claude: 1.7 units/s).**
  - **Ground space.** All motion is defined on the ground and projected once, so cadence, step length and bob are the same in every direction. This fixes the slow bob and huge steps walking up and down, and the short shuffling steps sideways.
  - **Gait.** 3.4 steps a second at full speed, a 1.0-unit ground stride, feet reaching at most 0.15 units from the hip.
  - **Motor.** The motor uses the rig's `walkSpeed`, with the stick's direction taken on the ground.
  - **Tests.** A spec test checks speed, cadence, reach and stretch in all 8 directions. A hand-rule test and a key-pose review sheet were added. The old frame-walk speed test is retired.
- **Heel-toe roll (2026-10-14, Kevin: "tilt the foot so he doesn't raise his knee so far").**
  - The swing lift drops from 0.06 to 0.025.
  - The boots now roll about the sole: the heel peels up 30° at push-off, pivoting on the toe, and the toe tips up 15° before the heel lands, pivoting on the heel.
  - The leg ends at the rolled ankle.
  - Before this, the boots rotated about the character's ground point, so the roll barely showed.
- **Arm fixes (2026-10-14, Kevin).**
  - Relaxed hands follow the end of their arm, so the wrist no longer kinks in the diagonals. The far arm in the diagonals hangs from the torso's back edge, with only the hand peeking out.
  - Head-on, the arms swing toward and away from the camera: drawn straight and foreshortened, the hand moving in and growing as it comes forward. Before, the hoses bowed out sideways, so the elbows stuck out.
  - Head-on, the shoulders sit just outside the apron straps, so the sleeves no longer cover the suspenders.
- **Ground-block brief for Astra** (`Docs/ART_BRIEF_BLOCKS.md`): 12 ground tiles and 17 blocks in the store owner's flat style, with no outlines and exact templates.
- **Front and back views reworked (2026-10-14, Kevin: the moved shoulders detached and looked like a princess dress, and the hands shouldn't grow).**
  - The shoulders are back at the shoulder line. Head-on, the sleeves and arms draw behind the body, so the apron straps lie over the sleeves as in the painting.
  - The hand-size change is removed. The swing moves the hand 8% toward his middle, and the hand passes in front of the body only when it comes toward the camera.

## Ground tiles and blocks in the store owner's style (2026-10-14)

Astra's 29 sprites (`ArtSource/world-tiles-v3`, imported by `Tools/ArtImport/import_world_tiles.py`) replace the painted ground and blocks under the same names. The .meta files and GUIDs are kept.

- **Scale.** 256 px per unit, pivot at the top face's centre (image 128, 64.5): the same world size and anchor as before.
- **Mesh.** Full rect, so neighbours meet without gaps.
- **No outline.** None of them get the game outline.
- **Town tiles.** The town's tile copies (grass, path, forest floor, dirt, plaza) take the same art.
- **Test.** `WorldTileTests` checks size, scale, pivot, mesh and that they are not outlined.
- **Still in the old painted style:** trees, bushes, flower decals and buildings.
- **Far arm smaller in the diagonals (2026-10-14, Kevin).** The far arm, its sleeve and hand are drawn at 78% (`farLimbScale`) in the diagonal and profile views: they are further from the camera.
- **Far arm: shorter, not smaller (2026-10-14, Kevin: the thinner far arm looked wrong).** `farLimbScale` is back to 1. The far arm in the diagonals keeps its full width and hand size; its resting hand sits 16 px higher, so the arm is a little shorter.
- **Faster walk (2026-10-15, Kevin: +20%).** `walkSpeed` 1.7 → 2.04 units/s; `cadence` 3.4 → 3.7 steps/s (a 1.1-unit stride), foot reach 0.15 → 0.16 so the feet still glide under half the ground covered.

## Nature, buildings and brighter ground (2026-10-15, Astra's v4)

`ArtSource/nature-buildings-v4`, imported by `Tools/ArtImport/import_nature_buildings.py`. 70 sprites are replaced under their existing names; the .meta files and GUIDs are kept.

- **What was replaced.**
  - The recoloured ground, blocks and town tiles.
  - The 16 nature sprites in `Painted/Nature`, plus the game's nature copies: cedar = tall pine, maple = leafy tree, ironwood = bent pine, the stump, the herb bush and the stone face.
  - The 9 buildings.
- **Import.** All at 256 px per unit with Astra's pivots. Canvases doubled, so world sizes are unchanged. Nature and buildings use a full-rect mesh.
- **Outline.** Nature and buildings get the game outline (57 outlined sprites). `SpriteOutline` now also works on a single sprite without a sorting group: the outline sorts just behind its own piece. Forest trees, buildings and props carry it.
- **Still old placeholders:** `rock_iron` and `rock_copper`.

## The forest looks like the forest-mountains prototype again (2026-10-15, Kevin)

Kevin preferred the 48×48 prototype (commit 2e44f70) to what the real forest became. Its generation is restored in `ForestGrove`.

- **Hills.** Organic hills from two layers of noise (scales 11 and 5, weights 0.65 and 0.35), mapped to 0–7 blocks (`MaxHill`). The straight ridges every 24 tiles are gone.
- **Trees.** Trees grow by height: 42% of flat spots, 20% on slopes (≤3 blocks), 8% higher up (`TreeChance*`). They are thick in the valleys and thin up the hills, with no bare-rock rule (`RockHeight` 99).
- **Tree size.** 0.62, as in the prototype (was 0.78).
- **Kept.** The cliff-edge row stays flat and wooded. The first 4 tiles by the gate are flat. Tree species (which logs) are unchanged.
- **Tests.** The ridge tests are replaced by an organic-hills test: peaks of 5 or more, mostly small steps, a flat edge and entrance, fewer trees up high. The climb-and-dig PlayMode test now searches the generated terrain for a slope and a wall, whatever the seed.
- **Saves.** Existing saves keep their dug cells and stumps by position. Where the new hills differ, old digs simply land on different ground.
- **No forest arch, no ore lamp, a block highlight (2026-10-15, Kevin).**
  - The wooden forest arch is removed. A "Forest" signpost (like the Mine's) stands in its place, carrying the Forest's panel and marker.
  - The Mine's lamp no longer shows ore in the rock near the supplier (`MineSlabView.ShowOreInLamp` = false). The "Tap" prospecting ring still works.
  - The block the main button will dig is highlighted (`BlockHighlight`, `Props/fx_block_highlight`: a warm outline with a faint, gently pulsing tint) in the quarry, on forest rock and on the town outcrops. It sorts with its block, so the supplier in front covers it.

## 2026-10-15: no AFK gathering; routines, signposts and site panels removed

Kevin: the supplier is the only one gathering, by hand; nothing is gathered while away.
- **Routines removed.** `SitesService.StartRoutine/EndRoutine/RoutineSiteId` are gone, along with the routine figures (`WorkerView`, `WorkerInspectable`) and the motor's hide-and-take-over logic. Loading a save ends any old routine or crew; what they carried stays in the site's staging pile.
- **Site panels removed.** The Mine and Forest signposts (yellow flags), their markers (`SiteMarkerView`) and the site panel (`SitePanel`, strata/depth diagram, tunnels, routine buttons, gear) are deleted. The "keep digging tunnel" bug (a second figure drawn over the player) went with them.
- **Staging piles hidden.** `container.mine_staging` and `container.forest_staging` still exist logically (old saves, leftovers). They draw nothing while empty, show no capacity badge and can't be tapped.
- **Tools moved to the Forge** (Kevin's choice). `StationDefinition.sellsSupplierTools` (on for the Forge) adds a "Your tools" section: each site's next equipment upgrade plus the next tool tier. Packs are no longer offered.
- **Domain left in place.** Worker simulation in `SiteSimulator` is unused, but kept so old saves still load.
- **Tests.** The routine tests were replaced by checks that nothing is gathered while away and that an old routine ends on load. Phase 6 now buys the iron tool at the Forge. EditMode 235, PlayMode 46, all passing.

## 2026-10-15: ore hidden everywhere; Astra's shop furniture in the Store

- **Ore hidden in forest rock and town outcrops** (Kevin). They draw plain rock, as the Mine does; the ore is found by breaking the block.
- **Shop furniture** (Astra's `ravenholm-shop-furniture-native-v2`): `Tools/ArtImport/import_furniture.py` copies 12 sprites to `Art/Placeholder/Furniture/` at 256 px per unit, with points.json pivots and the game outline.
  - **Levels.** A new `station.store` ("Service Desk", levels 1 to 3), raised by `upgrade.store_2` (60 gold, 10 cedar lumber, 4 iron ingots) and `upgrade.store_3` (150 gold, 8 maple lumber, 4 steel ingots, 2 copper ingots; needs level 2). These costs are balance starting points. Tap the desk to buy them.
  - **Configs.** One `StoreFurnitureDefinition` per piece (`Content/Config/Furniture/`). Setup (`EnsureShopFurniture`) builds them from points.json: sprites, ground points and slot boxes per level, plus floor footprints (the display tables get longer).
  - **Display.** `FurnitureView` swaps the sprite to match the level and fills the slot boxes with stock, in the racks' order (pinned, strongest, newest). Each item is scaled uniformly to fit its box; potions stand on the box's bottom.
  - **Capacity.** Armour stand 1/1/1, potion shelves 4/6/9, display table 2/3/4. The desk shows no stock.
  - **Small pieces.** `ItemDefinition.smallDisplay` marks items that go on the display table instead of the weapon rack: iron dagger, steel stiletto, moonsilver focus.
  - **Weapon rack.** There is no new weapon-rack art yet, so the old rack is mirrored against the back-right wall, behind the table.
- **Interim placement.** The building layers aren't delivered, so the furniture stands in today's 3×3 Store room:
  - the desk along the front-right edge, in place of the painted counter (erased from `bld_store.png`);
  - the potion shelf on the back-left wall;
  - the armour stand near the back corner;
  - the display table against the back-right wall.

  The receiving strip moved to the middle of the room. The bell tap target now sits on the bell painted on the desk. Re-place everything when the compact Stage 1 building arrives.
- **Sorting.** The Store room now sorts from its sprite's centre, near its back corner, so its furniture and anyone inside draw over it. Tap picking uses the same point.
- **Tutorial.** The time-away tip no longer mentions routines.

## 2026-10-15: the fixed shop layout is not approved; the shop becomes modular (Kevin)

- **Direction.** Kevin turned down the fixed furniture placement. The shop will work like Shop Titans: the player places furniture freely on a tile grid and expands the building strip by strip with resources. It has no roof and is always seen as an open room.
- **Brief.** The modular art brief is `Docs/ART_BRIEF_SHOP_MODULAR.md`, which replaces the roof-lift brief. Its templates and layout mock-up are in `Docs/astra-refs/shop-modular/`, made by `Tools/ArtImport/make_shop_templates.py`. The art comes in two steps: a sample for approval first.
- **Interim.** The current furniture placement stays in the game until the modular shop is built. The v2 furniture art and its configs carry over.

## 2026-10-15: the modular shop, first playable (placeholders)

Kevin: 4 × 6 to start, 2 × 2 add-ons, no roof for now, and Astra has visual freedom as long as the pieces work.

- **Roof.** None. Add-ons make L, T and U shapes, and a pitched roof over any shape would need many fragile pieces. Shop Titans has no roof either. Per-tile roof pieces that fade out could come later without changing the system.
- **Domain** (`Domain/Shop/ShopModel.cs`, engine-free). `ShopGrid` covers:
  - **Floor:** tiles from the owned 2 × 2 blocks.
  - **Edges:**
    - back edges with no floor behind them get full walls;
    - a full wall that would hide floor diagonally behind it becomes a low wall (inside corners);
    - front edges get low walls;
    - the entrance is a gap.
  - **Posts:** wherever a run turns, ends or changes height.
  - **Placement rules:**
    - floor pieces sit on free floor, never on the entrance tile;
    - every piece must border a tile you can walk to from the entrance;
    - wall pieces hang across 2 full-wall edges, without overlapping;
    - bought blocks must join the shop inside the 5 × 5-block lot.
- **Content.**
  - `ShopDefinition` (`shop.main`): start blocks, lot, entrance and the escalating block prices (80 gold and 10 cedar lumber for the first, up to 360 gold, 18 maple lumber and 8 steel ingots; the last price repeats).
  - `ShopFurnitureDefinition` (`furniture.*`): per level, a footprint, art for both facings or both walls, slots, and a price to buy or upgrade.
  - Desk (2 × 1, not for sale), display table (1 × 1, 1 × 2, 1 × 3 along B), armour stand, potion shelves (wall) and weapon rack (wall; the old rack art as a placeholder).
  - All prices are balance starting points. Furniture levels are now per piece; the fixed shop's Service Desk station and its upgrades are retired.
- **Service** (`ShopService`, save section `shop`): place, move, turn, put away, upgrade, buy furniture and buy blocks. Every command validates first, then pays and applies. Buying a block puts away any piece that no longer fits. Older saves start with the default shop.
- **Presentation.**
  - `ShopView` draws the floor, walls and posts (placeholder art from `Tools/ArtImport/make_shop_placeholders.py`, on Astra's template canvases and anchors).
  - **Sorting:** full walls sort 0.2 behind their edge, so wall furniture and anyone inside draw in front. Full walls fade while you stand behind them.
  - **Stock:** each piece shows its slice of the stock; pieces of the same kind take consecutive slices.
  - **Facing B:** mirrors facing A until Astra draws it.
  - **Build mode:** the Build button opens a bottom panel and shows the buyable plots. Taps pick, move and place pieces; while a piece is selected, a tap counts only what stands on the tapped tile.
  - **Desk:** it opens the Store inventory and carries the bell's tap target. The store receiver stands on the tile inside the entrance.
- **Not yet.** Astra's shell pieces, facing-B art, the new furniture (long rack, pedestal), a drag-to-move preview, customers walking the shop, and re-laying the town around the larger lot.

## 2026-10-15: 6 × 6 shop, a 2-tile entrance, overlap-free tree outlines (Kevin)

- **Bigger shop.** It starts at 6 × 6 tiles (blocks 0..2 × 0..2), with a 2-tile entrance on the front-right edge (`ShopDefinition.entranceWidth`).
  - The starting furniture is re-laid for the bigger room.
  - The content asset moves to content version 2.
  - Saved shops at layout 1 gain the new start blocks; pieces that no longer fit go back to where a new shop has them.
  - Two entrance tiles must stay clear, and reachability starts from both.
- **Tree outlines.** Where trees overlap, the outline no longer draws across the tree behind.
  - Trees draw with `SpriteUnlitStencilMark` (marks their opaque pixels, stencil bit 64).
  - Their outline copies use `OutlineSilhouetteOverlapFree`, which skips pixels that are already marked.
  - This is switched on per object with `SpriteOutline.OverlapFree` (forest trees, town trees).
  - Other outlines are unchanged.
- **Build button.** It now sits under the settings button; at its first spot it covered the carry counter.

## 2026-10-15: Astra's modular shop, step 1 sample in the game (awaiting Kevin's approval)

- **Import.** `Tools/ArtImport/import_shop_modular.py` brings in her step-1 pack:
  - **Shell pieces** to `Art/Placeholder/Shop/` (256 px per unit, her pivots, no outline);
  - **desk_1_B** to `Furniture/`, with its points merged into `Furniture/points.json`.
  - The builder uses her pieces where they exist and the placeholders otherwise.
  - Setup reads `<sprite>_B` and `<sprite>_right` entries as a second facing.
- **Windows.** Every third full wall along A gets a window, except where furniture hangs on it.
- **Doorway.** The entrance edges draw `stub_entrance_left` (or `_right`).
- **Seen in the game:**
  - walls, floors and the L's inside corner join seamlessly;
  - two doorway pieces side by side leave an upright in the middle of the 2-tile door;
  - desk_1_B's dark panels don't match level-1 desk_1's plain oak;
  - she built the sample at 4 × 6 (the brief before the change to 6 × 6).
- **Tests.** The EditMode test `SuccessReturnsGearRewardsUnlocksAndSurvivesRestart` failed once and passed on the rerun with no change (intermittent; not investigated yet).

## 2026-10-15: shop walls lowered to 1.4 units (Kevin: "way too tall")

- **Height.** Full walls are 1.4 units (358 px, about 1.3 × the character), down from 2.2 (563 px).
- **Astra's step-1 pieces.** `import_shop_modular.py` shortens pieces drawn at the old height by cutting 205 rows of plain plaster; the window wall is cut above and below its window. The cap, the base and each anchor's distance from the bottom are kept.
- **Inside corners.** `ShopLayoutConfig.WallDepthSteps` is now 6, since a lower wall hides fewer tiles behind it.
- **Everything else.** Placeholders, templates (`*_176x495`, `*_64x495`, `*_304x560`) and the brief now use the new height. The placeholder posts are renamed `post_*_placeholder`, so Astra's posts no longer overwrite them.
- **Wall furniture.** It must fit under the wall's top. Her level-3 potion shelf (about 1.6 units) is taller and will need a lower version in step 2.
