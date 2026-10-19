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

## 2026-10-15: the castle posts the jobs; adventurers come to the shop to be kitted out (Kevin)

Kevin's choices: the shop lends its gear and takes a cut; adventurers arrive with a job already chosen. The supplier no longer posts jobs. This changes the GDD's Job Board (sections 10–11) at Kevin's request.

- **The castle** (`JobBoardConfig.CastlePosts`, on through `BalanceProfile.castlePostsJobs`).
  - **Timing:** the first offer comes 15 s into a new game, then one every 45 s. Dispatching or turning someone away restarts that timer; nothing is offered while both expedition slots are in use.
  - **Choice order:** the campaign's next job first, then the other open jobs in a seeded order, then the safe survey last. With no weapon or armour in stock at all, the survey comes first, since it needs no gear. So the company can never get stuck.
  - **Who comes:** the first job that someone suits and that the shop can actually kit them out for is offered: the job's gear and counter-potion requirements are met, and it isn't suicidal with the best gear on hand. This rule came from the castle bot test: before it, the castle offered the finale 2,861 times to a shop without a Power-13 weapon.
  - **What you see:** one adventurer per offer. They walk up from the castle (shown on the road) and wait at the shop.
- **Turning away.** The adventurer is sent off, the castle's next offer is a different job, and someone else comes (`TurnAway`).
- **Kitting out.** It reuses the loadout panel, forecast and atomic dispatch; there is no fee and no gold check.
  - The shop's share (`shopSharePercent`, 60, a balance starting point) is fixed on the expedition at dispatch. On return the shop gets that share of the castle's gold.
  - Materials, blueprints, reputation and the gear (unless they die) come back as before. The survey still pays its full 8.
- **Timing is exact.** Offers are stamped with their scheduled time, and offline catch-up steps to each offer. Online play and an absence give identical saves (tested). An overdue offer waits for the slot that frees it.
- **Saves.** New optional fields:
  - `nextCastleOfferMs`, `castleOffers`, `castleSkipOnce`;
  - `posting.byCastle`;
  - `expedition.shopShare`. It is -1 for older expeditions, which keep the old payout.
- **World.** A placeholder castle (`Tools/ArtImport/make_castle_placeholder.py`) stands where the road leaves town (Astra to draw it). The Job Board becomes "The Castle's Jobs": the jobs to read, who is coming, and Turn them away.
- **Tutorial.** Steps 6–8 are reworded. Step 3 is now a tip to read, since its site decision went away with the site panels and could have stalled the tutorial.
- **Tests.** The original Job Board's tests switch the castle off explicitly; its rules still run for a posting made by hand. New tests:
  - `CastleTests`, including a castle bot that runs a fresh game through the whole campaign: 129 expeditions in 3.6 simulated hours, nobody turned away.
  - `CastlePlayModeTests`.

## 2026-10-15: Astra's modular shop, step 2 in the game

- **Import.** `import_shop_modular.py` brings in her step-2 pack: 149 sprites, at 256 px per unit with her pivots.
  - **Shell pieces** go to `Shop/`; the placement overlays also go to `Shop/`, and the icons to `UI/Icons/`.
  - **Furniture** goes to `Furniture/`, with both facings or both walls; `potion_shelf_3` is the shorter redraw.
  - **Item display views** (A and B for every item) go to `Items/`, with points in `Items/points.json`.
- **Shell.**
  - Three floor variants.
  - A wall that furniture hangs on uses `wall_rail`. One stockroom door sits on the free back-right wall edge furthest along it. Every third other full wall gets a window.
  - The doorway is two threshold-only pieces, with no post in the middle.
- **Furniture.** Setup builds each type from her art (`<key>_<n>` / `_B`, or `_left` / `_right`), with anchors from each sprite's pivot and her slot boxes. Two new types (prices are balance starting points):
  - the bow and staff rack (`long_rack`, a wall piece, `FurnitureDisplay.Long`);
  - the pedestal (one drawing serves both facings).

  The weapon rack's placeholder is replaced, and it now leaves bows and staffs to the long rack. Facing B is drawn for everything, so nothing is mirrored any more.
- **Items on display.** `ItemDefinition.DisplaySprite(facingB)`: view A on left-facing pieces, view B on right-facing ones. Each is scaled uniformly by its visible bounds and stands on the slot's bottom centre. All 22 crafted items have views. Items without them fall back to the stack sprite.
- **HUD.** The Build button shows her `ic_build` icon.
- **Not yet used.** The grip and carry anchors on the item views (for items held in the hand), the views of materials, and the other build-mode icons.

## 2026-10-15: the blond citizen; every NPC is him for now (Kevin)

- **Import.** `Tools/ArtImport/import_citizen.py` brings in Astra's `ravenholm-citizen-angular-v3`. Its five combined head-and-torso bodies go to the character library at `Art/Placeholder/Characters/Citizens/<id>/`, with its points.json and library.json, at the store owner's 340 px per unit and ground pivot. The game outlines them.
- **Shared parts.** Hands and boots are the store owner's own sprites; Astra's copies are byte-identical, so they aren't imported again.
- **Definition.** `ProjectSetup.EnsureCitizen` builds `Content/Config/Citizens/<id>.asset` by copying every tunable from the store owner's definition (walk speed, cadence, stride, swing, springs, foot roll, sizes), so he walks exactly like the supplier. It then adds the citizen's own:
  - bodies, shoulder and hip anchors, and wrist targets;
  - arm colour #DA875B and trouser colour #6D594B;
  - no tools, and no procedural sleeves (`HoseCharacterDefinition.sleeves`): his shirt sleeves are painted on.

  It is rebuilt each run, so it follows any later change to the supplier's tuning.
- **NPCs.** `CitizenFigure.Create` builds a figure from the supplier's `SupplierAnimator` and `HoseRig`; the animator takes the walk, facing and mirroring from how the figure moves.
  - Every adventurer is the citizen: coming up the road, waiting at the shop, leaving and returning. They walk at the supplier's speed.
  - The Trader at the stall is the citizen too.
  - Citizens appear and disappear at the castle gate rather than fading, since the rig's limbs are meshes.
- **Dropped.** The old figures' greeting bob.
- **Tests.** `CitizenPlayModeTests` checks all eight directions (the three west-facing ones are mirrored), the walk pose, the shared parts and his tuning, and that adventurers are citizens.

## 2026-10-15: the journey (Kevin), the wagon brief, and the valley view in the game

- **Direction (agreed in outline; design document to follow, nothing built yet).**
  - The game becomes a journey to the castle, made in camps.
  - Your shop folds out of a wagon pulled by your ox: carpet, furniture, a tarp roof. Everything is visible, and wagon upgrades make it more luxurious.
  - Stations pack into kits and are set up again in each camp's small starting clearing.
  - Tools clear tougher terrain to open the way forward. The valley seen from each camp is where the next camp lies.
  - Reaching the castle is the goal.
  - Crafting structure (agreed, on hold):
    - processing: Smelter, Sawmill, later a Tannery;
    - crafting: Forge (metal), Workshop (wood, leather, cloth), Apothecary, Kitchen;
    - loadout slots: weapon, off-hand, head, body, and two consumables (potion or food).
- **Brief.** The ox and wagon brief for Astra is in `Docs/ART_BRIEF_WAGON.md`.
- **The valley view, as camp 1's overlook.**
  - **Import:** `Tools/ArtImport/import_view.py` brings in Astra's `ravenholm-view-v2` to `Art/Placeholder/View/`: 256 px per unit (the composition's scale), her pivots, textures up to 4096, and no game outline. It writes a bottom-row fill strip for each layer and a top-row strip for the sky; the game stretches them so the view never ends in a gap.
  - **`ParallaxBackdrop`:** five layers repeat sideways behind the ground, using Astra's factors from parallax.json (screen offset = −camera displacement × factor; her composition is aligned when the camera sits 1.5 units above the cliff tip). The castle is fixed to the far ridge and never repeats. A tiny traveller walks the valley road toward the castle on each dispatch.
  - **The cliff:** for now it is fitted to the town plot's north tip. Lip pieces run down both north edges; the left edge stops where the Forest strip joins. The corner piece caps the tip, and blockers stop the walk at the lip.
  - **Adventurers:** they come and go by Astra's cliff path; her stair's disappear point is now the crowd's departure point. The placeholder castle at the road's end is removed.
  - **Interim:** until the world is reworked into camps, the Forest strip still pokes past the cliff line in places.
- **Tests.** `OverlookPlayModeTests` checks the view, that far moves slower than near and that the castle stays on its ridge, and saves the standing and walking frames (Captures/overlook_*). The outline test hides the view so it measures on a plain background.

## 2026-10-15: camp 1, Meadow's Edge (journey outline step 1; awaiting Kevin's approval)

Kevin approved the journey outline with all its recommendations ("go with your recommendations, start with camp 1").

- **The camp.** A `CampDefinition` (`Content/Camps/camp_meadows_edge.asset`) holds:
  - the clearing: the old town plot, cells (−20, −16) to (28, 21);
  - a forest depth of 20 cells;
  - bands 7 cells wide;
  - a flat margin of 3 cells.

  `ContentCatalogue.CurrentCamp` is the first camp for now; `SandboxSettings.camp` points at it, and the world bounds come from it.
- **The layout.** Forest and hills wrap the clearing's two low sides (south-west and south-east) and go around its side tips. The cliff runs along both high sides across the whole camp, and its lip arms run the full width. The forest grid is in "ring" mode (`ForestGrove.Ring`): a tree is named by its map cell (area = x, slot = y).
- **Bands by distance.** A band is set by how far a cell lies from the clearing: `1 + (d − 1) / 7`. It drives the wood species, toughness and rare rolls. Each 3 cells further out within a band is one more continuation. The ground is flat for 3 cells beside the clearing, then the hills rise (the old noise, capped at 2 blocks per cell past the margin).
- **The Mine is removed as a place:**
  - no quarry, dig spot, outcrops or rock faces in the clearing;
  - the old frontier strip is gone.

  The Mine site remains as the hidden earth under the hills: ore veins, broken blocks and layers. Its dig slab has no plot. `SiteSimulator.SurfaceBandAt` maps a hill cell to the Mine layer by the same band rule, so coal and the deeper ores lie further out.
- **Deep forest beyond the edges.** A separate, unwalkable `Beyond` tilemap runs 26 cells past the camp's low edges. The forest view draws its trees and hills as scenery: they can't be cut or dug. The cliff lip runs on along it, so you never see past the edge of the world.
- **The view follows the lip.** The parallax now looks from the lip's height under the camera, not the tip's: the lip drops 0.5 per unit sideways. Without this, walking along the long arms raised the layers until their fills showed.
- **Saves.** Each site's save records its camp. A save from the old town or frontier forgets its cut trees, stumps and broken rock: those cells no longer exist. A saved position that is no longer walkable returns to the plaza.
- **Ring coordinates can be negative.** The forest view and chop spot now track "found" with flags rather than `area >= 0`.
- **Tips** no longer mention the Mine.
- **Stations** stand where they were (step 2 places them).

## 2026-10-15: the journey runs forward: a winding ridge, the old road, the horizon (Kevin)

Kevin, on the first camp 1 layout: in this camera, going outward to the south means working behind yourself. Progress must always run forward, toward the castle. His words: "think of it as a looong wide winding mountain top", "not pinched, but not 4 minutes side to side". He chose my recommendations and dropped the cliff overlook. The "going down" is a feel, not a real descent.

**The ridge.** The land is now `RidgeLayout` (Domain, engine-free), replacing the ring layout:
- **Centre line:**
  - authored stretches, alternating up-right (+x) and up-left (+y);
  - Chaikin-smoothed into bends;
  - plus a seeded sideways wobble (4 cells).
- **Width:**
  - half-width 20 cells, ±5 as it winds; at walk speed 2.6 that's about three screens, 8–10 s edge to edge on open ground;
  - ragged edges (±3 cells of noise);
  - the ridge widens into a bay at each camp.
- **The old road:**
  - wanders ±4 cells inside the ridge;
  - only a quarter of its cells grow a young tree;
  - shown as cobble, worn earth and forest floor mixed;
  - flat, with no hills on it.
- **Bands** are counted along the road from the camp, 45 cells each. Every 15 cells is one more continuation. The hills follow the same rule for the Mine's layers.

**Camp 1, Meadow's Edge:**
- **The clearing** is an ellipse round the old town, centred on cell (−6, 3) with radii 17 × 23.
- **The line:** 16 cells behind the camp, then 34 / 38 / 30 / 34 / 30 forward.
- **A gate of old trees** sits 55 cells ahead, 4 cells thick, and needs the tier-2 (Iron) tool. Across the gate there are no gaps, and the trees are drawn bigger and darker. The button reads "Needs iron tool", and a tap explains. `SitesService.ToolTooWeak` blocks both chopping and digging there; a rock-wall gate (pickaxe) is supported for later camps.
- **Camp 2's clearing** is 95 cells ahead, radius 9.

There is one tool for now (axe and pickaxe together); splitting them is a separate change if wanted.

**The horizon.** `ParallaxBackdrop` is now a band across the top of the screen, sorted over the land on Overlay order −400, under every label:
- Astra's layers at 0.45 scale (sky, far, mid, near; the dark front treetops are dropped), sliding sideways by their factors;
- the castle centred straight ahead on the far ridge, 1.45 times its painted scale;
- a haze band blending the near layer's bottom into the land 2.2 units below.

The camera look-ahead is raised from 0.6 to 2.4, so the supplier sits low with the road ahead on screen.

**Adventurers** come and go 26 cells up the road, toward the castle.

**Removed:**
- the cliff overlook, its lip pieces and blockers;
- the ring layout;
- the deep-forest border;
- the town's three background trees.

**Playtest fixes, the same day (Kevin):**
- **Lag and red console errors.** Unity reported about 21,500 renderers against its 4,095 limit. Every one of the ridge's long, ragged edges drew a column of earth blocks down the whole screen. The face at the land's edge is now 5 blocks deep (`MineSlabView.EdgeFaceRows`). A test now counts the sprites drawn at an edge and requires fewer than 3,000.
- **No fade over the land.** Kevin: "it ends naturally with the edge"; the castle needn't always be in view. The view band now sorts behind the land (Ground −30000), so it shows only where the ridge ends. The camera's background past the edges is the valley green, and the band's bottom edge fades into it over 1.4 units.
- **The terrain is back.** On the ridge, ground height and tree density are the forest-mountains prototype's again (Kevin's favourite, 2026-10-13):
  - hills up to 7 blocks from two layers of noise;
  - trees cover 42% of the valleys, 20% of the slopes and 8% of the peaks.

  The ridge only keeps the clearings, the road and gates flat. Away from them the hills may rise two blocks a cell, and the road keeps three-quarters of its cells open.

## 2026-10-15: journey step 2, placing stations (awaiting Kevin's approval)

Kevin: "start on step 2, placing stations". Defaults chosen from the approved outline:
- **Where stations start.** They stand where the camp was laid out; nothing moves unless you move it.
- **Moving one.** In build mode (the hammer button), tap a station to pick it, then tap cleared, level ground; the footprint is centred under the finger. The footprint is outlined in green, or red where the ground isn't ready.
- **Footprints**, in map cells (`StationDefinition.footprint`): the Lumbermill is 3 × 2, the rest 2 × 2.
- **Cleared, level ground** (`StationsService.GroundProblem`) is:
  - on the camp's land;
  - not the shop's lot or another building;
  - either the clearing, or forest ground with no tree, no stump, and every hill block dug away.

  The hint says what's missing: "Clear the trees first", "Pull the stumps out first", "Dig the ground level first".
- **The build.** Setting up takes a short build: `BalanceProfile.stationBuildSeconds`, 8 s of real time. Meanwhile the building shows a scaffold and a countdown, its delivery marker takes nothing, and it can't be tapped to open.
- **What moves with it.** The delivery marker and output pad move with the station and are re-registered with logistics (`LogisticsService.MoveContainer`, `ReceiverView.Reregister`).
- **Kits.** A station packs into its kit (only with its output pad empty). It then disappears and takes nothing. Build mode lists the kits ("Station kits"), so each can be set up again; step 4 (moving on) will use this.
- **Processing** carries on wherever a station is.
- **Saving.** Placements save in a new `stations` section, only for stations that were moved or packed, keyed by camp.
- **Not yet:** the Trader's pad and the shop stay fixed. The shop becomes the wagon in step 3.

## 2026-10-15: journey step 3, the wagon shop (placeholder art; awaiting Kevin's approval)

Kevin: "start on step 3, the wagon shop". Built to the approved outline, with placeholders until Astra's ox and wagon land (`Tools/ArtImport/make_wagon_placeholders.py`):
- **The carpet is the shop floor** (`ShopLayoutConfig.Open`):
  - no walls, posts or doorway;
  - customers step on from its front edges;
  - furniture is placed, turned, stored and upgraded in build mode as before.
- **The wagon** parks behind the carpet's back-left edge, with its long side facing the camera and folded down into the counter:
  - it takes the desk's job: open the Store, the bell, the Store's delivery marker on the carpet's back row;
  - it blocks walking (you can walk round it);
  - a placement must always leave a way to the counter ("Keep a way to the counter").
- **Reachability is now per piece.** A piece needs open floor somewhere along it; previously every tile of it did, which wrongly refused a 2-tile table pushed between two pieces.
- **Wagon levels** (`ShopDefinition.carpetSizes`, `wagonCosts`) are bought in build mode with "Upgrade the wagon":

  | Level | Carpet | Price |
  |---|---|---|
  | 1 | 4 × 4 | start |
  | 2 | 6 × 4 | 100 gold, 12 cedar lumber |
  | 3 | 6 × 6 | 200 gold, 10 maple lumber, 6 iron ingot |
  | 4 | 8 × 6 | 320 gold, 16 maple lumber, 6 steel ingot |

  The carpet art steps 1 / 2 / 2 / 3, and the wagon's paint with it. The carpet only grows onto cleared ground that no station stands on (`StationsService.LandProblem`).
- **Free-standing furniture:**
  - **Stands:** Weapon Stand, Bow and Staff Stand and Potion Stand (2 × 1 tiles; placeholders: the wall art on legs). Each is the wall piece's `standingVersion`, and its prices copy the wall piece's.
  - **No longer sold:** the wall pieces.
  - **Retired:** the desk (`retiredByWagon`).
- **The start:** a 4 × 4 carpet with a weapon stand, a display table, an armour stand and a potion stand, keeping the counter's row open.
- **Older saves** move onto the wagon:
  - a 6 × 6 floor becomes level 3, and a bought block makes it level 4;
  - the desk is removed, with its upgrades refunded (gold and materials);
  - wall pieces become their stands at the same level and are set out on the carpet.
- **Not yet:**
  - the tarp, the fold-out animation and the ox;
  - wagon storage for travelling, which belongs to step 4.

## 2026-10-15: journey step 4, moving on (placeholder art; awaiting Kevin's approval)

Kevin: "start on step 4, moving on". Built on the continuous ridge (one road, camps as clearings along it):
- **Breaking through** is physical. The gate of old trees before River Bend needs the Iron tool, and reaching River Bend's clearing proves the way is open. There is no separate unlock.
- **The sign.** River Bend's clearing has a sign (`MakeCampSpot`) offering "Make camp". It opens the moving-on panel (`MoveOnPanel`), which shows what happens, the wagon's load against its storage, and what's in the way.
- **The rules** (`CampService`, the `camp` save section):
  - every station's output pad must be empty;
  - the load (Store goods plus materials in stock) must fit the wagon: 60 / 100 / 160 / 240 units by wagon level (`ShopDefinition.wagonCargo`). "Leave the extras behind" drops materials, the biggest stacks first.

  Making camp then packs every station into its kit and moves the company to the next camp.
- **The camp's group moves rigidly.** The wagon shop (furniture as you had it), the Store's marker at the counter, the Trader's pad, the applicants' spots and paths, the trophy and the leftover piles all keep the places they had at camp 1. The group's middle (cell 5, 2 at camp 1) is set on the new clearing's centre; the group is tagged `CampAnchored`, and the shop's origin and the applicants follow `CampService.Offset`.

  What was cleared and dug stays where it is, in the world.
- **The drive** (`DriveView`, placeholder ox): the wagon and ox follow the old road from the old camp to the new one at 7 units/s. The camera follows and the supplier rides along, then steps down in front of the shop, and a toast says to set the stations up from their kits.
- **The castle on the horizon** grows 25% per camp up the road.
- **Content:**
  - River Bend is 95 cells ahead, with a clearing of radius 13.
  - The land runs on beyond it to a second gate at 150 ahead: a rock wall needing the Tempered tool (tier 3). It ends short of camp 3, which isn't built yet.
  - River Bend is the last camp for now.
- **Not yet:**
  - the summons and the camp tier of jobs (step 5);
  - choosing what to leave behind item by item;
  - Astra's ox and wagon art.

## 2026-10-16: journey step 5, job tiers and summons per camp (awaiting Kevin's approval)

Kevin: "start on step 5, job tiers and summons". Following the outline's mapping (Frontier Paths for camps 1–2, the Hollow March for 3–4, the Crownward Ruins for 5–6):
- **Each job belongs to a camp** (`QuestDefinition.camp`). The castle posts only the jobs of the company's camp and earlier camps (`JobBoardSystem.PostedHere`, from `CampPosition`, which `GameServices` keeps in step with `CampService`):

  | Camp | Jobs |
  |---|---|
  | Meadow's Edge | survey, Roadside Patrol, Briar Track, Old Shrine |
  | River Bend | Quarry Road, Fen Path, Fallen Watchtower, Shrine Chamber |
  | 3 | Fur Trappers' Camp, Bandit Ford, Hollow Warren, Mire Crossing |
  | 4 | Witchlight Fen, Sunken Archive |
  | 5 | Crownward Gate, Ironwood Hunt |
  | 6 | Ashen Barracks, the Beacon jobs, the challenges |

  Until camps 3–6 exist, their jobs are posted at the last camp, so the campaign can still be finished. A job unlocked early but belonging to a later camp waits for that camp, and the job board lists only the jobs posted here.
- **One summons per camp** (`QuestDefinition.summons`): Old Shrine at Meadow's Edge, Shrine Chamber at River Bend.
  - The castle sends for the summons first once it is unlocked.
  - The job board shows it under "The castle's summons for <camp>", and the posting is tagged with it.
  - Making camp at the next clearing needs the summons answered (at least one success) as well as the way through.
- **Unchanged:** the order of the rest, which is the campaign objective first, then shuffled; prerequisites and reputation gates; and adventurer levels, which still scale with reputation.
- **Tests:**
  - The castle bot now moves the company on as soon as each camp's summons is answered, and still completes the campaign.
  - New tests cover the tiers and the summons gate.

## 2026-10-16: journey step 6a, the loadout's new slots, helmets and shields (Kevin's choices)

Kevin: "start on step 6, the crafting expansion". It changes GDD 8's item categories and the loadout (locked), so I asked first. His answers:
- helmets and shields get **their own stats** (not added to armour);
- **mauls, bows and staves are two-handed** (the recommendation);
- food is a **cheap consumable** (the recommendation; built in 6c);
- crafting is **long, Shop Titans style** (6b).

Built:
- **The loadout** (`Loadout`) is weapon, off-hand, head, body armour and two consumables. The panel shows the slots in that order. A two-handed weapon empties the off-hand and offers no shield.
- **Item categories** (`ItemCategory` and `ItemKind`, appended in step so saved values stay put) add Helmet and Shield. `ItemDefinition` has two new fields: `twoHanded` (weapons) and `block` (shields, in percent).
- **Helmets guard against one threat**, using the counter system: the helmet's counter is never used up. A helmet adds no armour.

  | Helmet | Guards against | Made at |
  |---|---|---|
  | Iron Helm | heavy blows +3 | Forge 1 |
  | Leather Cap | traps +3 | Workbench 1 |
  | Steel Helm | heavy blows +5 | Forge 2 |
  | Moonsilver Circlet | arcane +5 | Forge 3 |
- **Shields block.** If an expedition would end in death, the shield has its block chance of turning it into a wounded retreat (a Failure). `Resolver` only draws that extra roll when a shield is carried, so every other outcome is identical. `Resolution.Blocked` records it, and the result explains it.

  | Shield | Blocks | Made at |
  |---|---|---|
  | Wooden Shield | 15% | Workbench 1 |
  | Iron-bound Shield | 25% | Forge 1 |
  | Steel Kite Shield | 35% | Forge 2 |

  The block chance is saved in the expedition's snapshot (`block`).
- **Two-handed weapons:** Steel Maul, every bow, Cedar Staff, Copper Ward Staff. The Moonsilver Focus stays one-handed: with staves two-handed, an off-hand focus would never be usable, so a mage with the focus may carry a shield instead.
- **The best kit picks** the strongest weapon, then the best helmet (highest guard), the best shield (if one-handed) and the best body armour.
- **Display:** the Store's armour tab and the armour stands show helmets and shields.
- **Art and recipes:**
  - Placeholder icons and pile sprites come from `Tools/ArtImport/make_armoury_placeholders.py`, until Astra's art.
  - The seven recipes are owned at the start, gated by station level, with balance starting points.
  - Recipes number 29 of the V1 ceiling of 30.

## 2026-10-16: journey step 6b, timed crafting (Kevin: long, Shop Titans style)

- **Craft slots.** Crafting is no longer instant. Each station has craft slots that work at the same time: 2 at level 1, and 1 more per level above (`ProductionSystem.CraftSlots`). Starting a craft pays the materials and fills a slot (`CraftJob`).
- **Finishing.** When the time is up the item lands on the station's output pad. If the pad is full it waits in its slot ("done, waiting for room on the pad") until goods are carried away. A full pad no longer stops a craft from starting.
- **Times** (`RecipeDefinition.craftSeconds`), as balance starting points at the recipe's own station level; each level above it is quicker by the station's batch factor, as for processing:

  | Recipe | Time |
  |---|---|
  | the five starter weapons and armour | 5 min |
  | the starter tonic | 2 min |
  | other level-1 pieces | 10 min |
  | level-2 potions | 10 min |
  | level-2 pieces | 30 min |
  | level 3 | 2 h |
- **Time away.** Crafts carry on while you're away, within the usual 8-hour window for production. They are saved in the production section (`crafts`).
- **The station panel** shows each recipe's time on its card and button, plus the slots: what is being made and how long is left.
- **Tests:**
  - new ones cover paying at the start and finishing on time, the slots per level, waiting for a full pad, and saving across a restart;
  - PlayMode tests that need goods at once call `DebugFinishCrafts`.
- **Not yet:**
  - the away report doesn't list finished crafts;
  - there's no paying to hurry.

## 2026-10-16: journey step 6c, the Workshop, the Kitchen, foraging and food

- **The Workshop.** The Workbench is renamed the Workshop; its id stays `station.workbench`. It adds two light armours, both made at Workshop level 1:
  - Leather Jerkin: protection 5, traps +2, from fur and rope;
  - Padded Gambeson: protection 4, exploration +2, from rope and cedar lumber.
- **The Kitchen** (`station.kitchen`, max level 2, output pad `container.kitchen_pad`, delivery marker `receiver.kitchen`):
  - it arrives as a kit (`StationDefinition.startsAsKit`), so the player sets it up in build mode, as at a new camp;
  - it takes berries and mushrooms (new `ReceiverKind.Kitchen` and `ResourceCategory.Food`);
  - Kitchen 2 costs 50 gold, 4 iron ingots and 4 maple lumber.
- **Foraging** (`SiteDefinition.fellForage` and `stumpForage`). A felled tree drops wild berries 12% of the time; a pulled stump gives forest mushrooms 35% of the time. Each roll is fixed per tree spot by the seed. The content graph counts foraging as a source.
- **Food** (`ItemCategory.Food`), as Kevin chose: a cheap consumable in the potion slots.
  - It is always eaten on the trip, whatever the outcome.
  - It adds to survival (forecast and resolver) and to readiness ("well fed").
  - The same dish twice does not stack.

  | Dish | Survival | Readiness | Made from | Time |
  |---|---|---|---|---|
  | Trail Rations | +2 | +1 | 2 berries | 3 min |
  | Mushroom Stew | +3 | +2 | 2 mushrooms, 1 berry | 3 min |
  | Hearty Pie (Kitchen 2) | +4 | +3 | berries, mushrooms, a herb | 10 min |
- **Display.** Food shows in the Store's potions tab and on potion stands.
- **Saving.** Food is saved with an expedition: its stats in the snapshot, and a `food` flag on what was carried.
- **Art.** Placeholders: the Kitchen is the Apothecary's building with a red roof; icons and piles come from `Tools/ArtImport/make_kitchen_placeholders.py`.
- **Content ceiling.** Recipes number 34; the V1 ceiling in `CampaignTests` is raised from 30 to 40 for the expansion.

## 2026-10-16: journey step 7, camps 3–6 and the finale (placeholder art; awaiting Kevin's approval)

- **The road.** Six camps, set in `ProjectSetup.Road.cs` (`EnsureRoad`). Each "ahead" figure is road length from camp 1, and each camp has its own ground tint (`StopSpec.groundTint`):

  | Camp | Ahead | Radius | Blocker after it | At |
  |---|---|---|---|---|
  | 1 Meadow's Edge | 0 | – | old trees, tier 2 (iron tool) | 55 |
  | 2 River Bend | 95 | 13 | a rockslide, tier 3 (tempered tool) | 150 |
  | 3 Hollow Wood | 190 | 13 | **ravine: build the Bridge** (12 ironwood lumber, 8 rope) | 245 |
  | 4 Witchlight Fen | 285 | 13 | **flood: build the Causeway** (12 maple lumber, 6 steel ingots) | 340 |
  | 5 Crown Highlands | 380 | 13 | the sealed gate, tier 4 (moonsilver tool) | 435 |
  | 6 Castle Approach | 475 | 14 | – (the finale) | – |

  - The ridge has 21 stretches. Chaikin smoothing cuts corners, so the summed stretch lengths overstate the walkable length. The land runs at least 30 past Castle Approach, which `RoadTests` checks. Setup rewrites the road whenever it falls short.
  - Band length is 80: one band of wood and ore per camp.
- **Build projects** (`GateSpec.project`/`projectCost`, `RidgeLayout.Gate.Project`):
  - A project gate is a gap in the land (`InProjectGap`) with no trees and no tool tier.
  - A sign 5 cells before it (`GateProjectSpot`) offers "Build bridge" or "Build causeway". `CampService.Build` pays from stock.
  - Once built, the gap is filled with the `bridge` tile and can be walked.
  - Built projects are saved in the "camp" section (`built`).
- **The tier 4 tool.**
  - Moonsilver tool: 160 gold and 6 moonsilver ingots.
  - `upgrade.mine_equipment_4`: 150 gold, 6 moonsilver and 6 steel. It needs Mine equipment 3 and 8 successes.
- **Summons.**
  - Camps 1 and 2 keep their summons.
  - New summons: camp 3 Bandit Ford, camp 4 Sunken Archive, camp 5 Crownward Gate.
  - The finale, renamed "The Castle's Last Great Job", is posted at Castle Approach (camp 6).
  - The Beacon wording is retired: "Chart the Castle Road", "Hold the Castle Pass", "Journey complete".
- **Tests.**
  - In-band effort is now three steps per band (`ContinuationAt`), scaled to the band length; at one step per 15 cells, an 80-long band's last trees outgrew the next band's first.
  - EditMode `RoadTests`: the six camps and their blockers, the land's length, one band per camp, tints, building the bridge from stock and saving it, and the finale and summons.
  - PlayMode `RoadPlayModeTests`: captures of camps 2–6; the ravine blocks until the bridge is built at its sign, then can be stood on.
- **Not yet:** Art for each camp, the bridge and the causeway (all placeholders), plus Astra's walk-in cart.

- **Debug camp jump** (Kevin, 2026-10-16). Developer tools has "Jump to next camp" (`CampService.DebugJumpTo`). It answers the summons on the way, builds the projects behind and drives up one camp. At the last camp it jumps back to camp 1 with no drive.

## 2026-10-16: the red text and lag on the phone; the tree highlight (Kevin's playtest)

- **Cause.** The ground tilemap drew in Individual mode, so each of its 26,000 tiles (six camps of road) was sorted separately. That is far over Unity's limit of 4,095 renderers and sorting groups. Unity then switched off the sorting groups (the red text), and sorting every tile each frame caused the lag. A probe that loaded Kevin's phone save found only 799 active sprites, all camera-windowed.
- **Fix.** The ground now draws in Chunk mode: one chunk for the whole road (1024×1024), drawn top-right first. Its tile art is packed into one atlas (`Tiles/GroundTiles.spriteatlasv2`, `ProjectSetup.EnsureGroundAtlas`), which isometric chunk sorting needs.
- **Region streaming.** Kevin asked for regions more than one away not to load. I didn't stream them: as one mesh, the ground costs little far away, and unloading tiles would mean re-applying dug holes, built bridges and walkability at each border. Streaming is the fallback if the phone still lags.
- **Tree highlight.** The tree or stump the button would chop gets a bright line round its silhouette, outside its outline, pulsing like the block highlight (`ForestGridView.UpdateTreeHighlight`). The outline shader gained `_HighlightPx` and `_HighlightColor`; ordinary outlines are unchanged.
- **Tests.** `CampPlayModeTests` checks the highlight and that the ground draws in Chunk mode.
- **The red text came back: an outline leak** (found later the same day).
  - Cause: the Trader, an outlined figure, stands inside the outlined Trader's stall. Each `SpriteOutline` outlined the other's outline copies every frame without end. At load Kevin's save had 2,395 copies of copies, and about 20 more were added each frame, which drove the renderer count back over the limit and cost time every frame.
  - Fix: an outline now skips outline copies (renderers using the outline materials) and the pieces of a nested outlined object.
  - Result: Kevin's save loads with 765 active renderers, and the count stays flat (765–1,114) across all six camps and back.
  - Test: `OutlinePlayModeTests.AnOutlinedObjectInsideAnotherIsNotOutlinedOverAndOver`.

## 2026-10-16: each camp looks over the drop to the castle (Kevin)

Kevin loved a screenshot taken by the ridge's drop-off, with the castle in the distance. He asked for the road to swing out to the edge near camps, and for the first camp to sit close to the edge.

- **A bay at every camp** (`RidgeLayout.InCove`/`BayEdge`; `CampDefinition.edgeGap` 2.5, `bayHeight` 5):
  - The land ends 2.5 world units right of where the camp's group stands. The default view is 7 wide, so the drop is on screen.
  - Above the group the cliff runs up and left for 5 units, then turns back. That opens the top of the view, where the horizon, the valley and the castle show. A cove beside the camp alone showed only the valley green.
  - Below the group the cliff falls away down and right. What stands there keeps its ground: camp 1's Trader and Workshop, and the Trader's pad at every camp (`groupSpots`, set by `EnsureRoad`).
  - The bay cuts through camp 1's town clearing, so camp 1 sits right at the edge.
- **The road.** It swings out along the right-hand edge into and out of each camp (`EdgePull`, `RoadOffsetAt`) and never runs through a bay. The project signs stand on the road where it actually runs. The bridge sign beats a tree within about a unit (priority 2).
- **The groups at camps 2–6** stand toward the right of their clearing (`anchorInset` 6 cells inside its edge). Camp 1's group sits on the right of its town too.
- **Edges near camps** are less ragged, so the drop stays where it was set.
- **Tests.**
  - `RoadPlayModeTests.EachCampStandsByTheDropWithItsGroupOnLand`: at all six camps everything the camp brings stands on land, and the drop is 1.5–3.4 units right of the camp. It writes the captures `edge_camp1..6`.
  - `CampTests` allows narrower land beside a bay: 14 cells or more, still wider than the view.
  - Tree-finding tests skip trees the tool can't cut.
  - The forest toughness test compares against band 1's base. Late band-1 trees were always tougher than early band-2 ones (in-band steps ×1.18 and ×1.42, against a band step of 45/40), so I changed the test, not the balance.

## 2026-10-16: Astra's equipment pack: the neckless mannequin, armour, headwear and shields

- **Import.** `Tools/ArtImport/import_armour_handoff.py` is repeatable and uses the pack in `ravenholm-claude-handoff`.
  - Every layer stays on the stand's own 512 × 512 canvas, with the existing stand import: 256 PPU, ground pivot (0.5, 0.17969). The pack's torso layer matches `armor_stand_1.png` exactly.
  - Nothing was redrawn, trimmed or rescaled.
  - Overwritten in place, so their GUIDs are kept: `item_<armour>_A.png`, `armor_stand_1..3.png` and the three shield icons.
  - New files: `stand_head_<armour>.png`, `item_<helm|shield>_A.png`, `armor_stand_base_<n>.png`, and the citizen's `headwear_<key>_SE.png`.
- **Cleanups.**
  - The bare mannequin's faint stray pixels (alpha ≤ 40, more than 2 px from the shape) were cleared, because the outline drew them as specks.
  - Levels 2 and 3 use the cream neckless mannequin on their own iron- and gold-capped bases. A short wooden pole shows above the darker base.
- **The stand, facing A.** `FurnitureView.ShowEquipment` and `ItemDefinition.StandLayer`/`StandHeadA` draw the layers back to front:
  1. the support alone under body armour (the torso hidden), or the bare mannequin otherwise;
  2. the armour;
  3. the head in that armour's collar;
  4. headwear;
  5. a shield beside the mannequin.

  All layers are unscaled at the stand's ground point, inside one SortingGroup with one outline.
- **Sets on one stand** (Kevin). A dressed stand shows one body armour, one helmet and one shield, each with its own count badge. `ShopView.Restock` fills it with the next of each kind, tracking items per furniture kind as before.
  - Armour without stand art (Padded Gambeson, Leather Jerkin) keeps its fitted view.
  - Facing B keeps the old views: only view A was supplied.
- **Citizens.** `HoseCharacterDefinition.View.headwear` and `HoseRig.HeadwearItem` add an overlay on the body.
  - It follows the body's lean, sway and squash, at order 7.
  - It shows only in the SE view and its SW mirror; other views show none.
  - Adventurers wear the helmet from their loadout when they set off, and wear it home if it came back.
- **Starting layout.** The armour stand moves to the carpet's front-right corner (3, 0), with the table at (2, 1), because the weapon stand hid the mannequin and its shield. The shop's content version is now 5 (it only marks new shops). Existing saves keep their layout.
- **Tests.**
  - `ArmourStandPlayModeTests`: every stand state, sets, facing B, layout, and the citizen headwear by view while walking. It writes `Captures/armour_stand_contact_sheet.png` and `citizen_headwear_contact_sheet.png`.
  - `ShopTests` places its bought stand at (1, 1).
  - `KitchenTests` searches further along the road.
- **Brief for Astra.** `Docs/ART_BRIEF_EQUIPMENT_VIEWS.md` asks for facing B, the citizen's S/E/NE/N views, and the stand's level 2 and 3 finishes.

## 2026-10-16: Astra's walk-in wagon (approved v5, parked view)

- **Import** (`Tools/ArtImport/import_walkin_cart.py`, from the `wagon-walk-in` pack):
  - The seven layers go to `Art/Placeholder/Wagon/WalkIn/` unchanged, with the pack's points, assembly and README.
  - All share one 1536 × 1024 canvas, the pivot (779, 826) and 256 PPU, imported full-rect.
  - `cart_assembled.png` is not imported.
  - `cart_silhouette.png` is derived: the union of the layers' alpha, so the outline is drawn once round the whole wagon. An outline per layer would draw seams at the cuts.
- **Drawing** (`WalkInCart`). Each layer sorts at its own depth on the town's height-on-screen order, in a SortingGroup whose position is that depth, with the art drawn back at the shared pivot:
  - the back and shafts at the far wall (floor top − 8 px), with the outline behind them;
  - the steps just above the stair top;
  - the near wheels, the counter/front and the roof just in front of where a figure inside may stand, and just behind the customer spot (customer y − 4 px).

  A figure inside therefore falls between the back and the counter. The near wheels draw with the counter (the pack put them behind the figure): the floor's far corner reaches over the right wheel, where the counter doesn't hide the feet.
- **The stairs.** On the lower steps (below 80% of the climb) the supplier draws in front of the door frame (player SortingGroup order 1). Above that, and outside, ordinary sorting applies (order 0).
- **Walking in.**
  - The physics root stays on the ground. `PlayerMotor.CartElevation` raises only the visual rig: it ramps from 0 to 150 px (0.586 units) up the steps and holds that on the floor. Ground feet = visual feet + 150 px.
  - The walls (EdgeCollider2D on the blocker layer) stand one footprint radius outside where the feet may stand:
    - the floor, kept just behind the counter's sorting row, so a figure inside always draws behind the counter and customers in front;
    - both sides of the steps.
  - The doorway on the left end stays open. Its posts step a radius back so it is as wide for the feet as it is drawn.
- **Parking.** The customer spot sits at the carpet's back-row middle, moved back until no wall or step is on the carpet. The approved perspective isn't the carpet's 2:1 grid, so the counter is not parallel to the carpet's edge.
- **Anchors.**
  - The Store's marker is at `customer_spot`.
  - The counter's tap area covers `counter_top_line`.
  - The bell is at its right end. It isn't painted on the new wagon, so it is a tap area only.
  - The ox rides at `hitch` on the drive. The drive moves a copy of the whole look (parked layers hidden, mirrored when heading left; the wheels don't turn).
- **Wagon levels.** There is one look at every level. Upgrades still grow the carpet and the cargo, as before.
- **Test.** `WalkInCartPlayModeTests` walks the supplier with the stick: up the steps, through the door, to the serving spot, to the far end, against the counter, back out, and the customer side can't get in. It checks the lift, sorting, walls, anchors and that nothing is on the carpet, and writes `Captures/walk_in_cart_contact_sheet.png`.

## 2026-10-16: the road becomes a switchback pass (Kevin; waiting on Astra's art)

Kevin's playtest of the camp bays: the flat valley green past the edge makes the land read as floating, and the perspective breaks. His sketch is a switchback mountain pass: long legs zigzag across the screen toward the castle, each leg looking lower than the last, though the playable ground stays flat.

- **Kevin's choices:**
  - The player walks round the hairpins; the drop between legs is eased visually.
  - Camps sit mid-leg at the outer edge, and the hairpins are the gates between camps.
  - The distant regions wait for Astra's painted art (no game-art placeholders first).
- **The plan.**
  - Each leg is drawn 4 block rows (2 units) lower than the one before; the shift is visual only and physics stays flat.
  - Only the current and next leg are real ground. Further legs are Astra's distance strips, and a valley floor replaces the flat green.
  - Brief: `Docs/ART_BRIEF_SWITCHBACK_PASS.md`. The layout work starts when her art arrives.
- The camp bays (shipped earlier today) stay until then.
- **Playtest fixes** (Kevin, 2026-10-16).
  - The wagon is solid on every side but the door. The walls follow its whole footprint, plus the player's radius, open only at the doorway.
  - Inside, a rail just behind the counter's sorting row keeps the feet behind the counter.
  - The ground under the shafts is walled: the pack has no outline for it, so it is approximated from the shafts' drawing lowered to the ground.
  - On the steps and in the doorway (left of the near corner post), the supplier draws in front of the door frame, because the post cut through them there. This replaces the pack's 80% switch.
  - Tests:
    - the wagon test now probes from 16 directions, shafts included, so no way in but the door;
    - Phase 1's slide check pushes along the wagon's back wall.
- **Second wagon playtest** (Kevin, 2026-10-16).
  - Pressing into the counter rail let the feet cross the counter's sorting row by a few pixels, drawing the supplier (and a carried pile) in front of the counter and roof. The rail now stops 16 px short of that row (`WalkInCart.RailMarginPx`).
  - While the stick is held, the supplier faces where it points, even when blocked or sliding along a wall (`SupplierAnimator`). Behind the counter they can face the customers.
  - The test holds the stick into the counter and checks they stay behind it and face S/SE/SW.

## 2026-10-16: the switchback pass, step 1: the layout (Astra's pass pack; Kevin: medium legs)

- **The pass.** `RidgeLayout.Switchback` builds the line from straight legs alternating along +x and +y, joined by Astra's quarter-circle turns: road radius 16 cells, left from +x to +y and right from +y to +x. The legs and turns are recorded (`LegSpans`, `TurnSpans`).
  - The land is 8 cells either side of the line with straight edges, like the paintings (no wobble, no edge noise), plus the camps' clearings.
  - The first leg runs from 12 behind camp 1's town, through it, and 40 on (`firstLegLength` 69).
  - The other legs are 40 long (`legLength`, Kevin: "start with medium but we might increase it later"). The last runs 24 further toward the castle (`FinalStretch`).
  - The road wanders ±1.5 cells.
- **Camps.** Camps 2–6 sit mid-leg on a ledge, a clearing pushed out over the side facing the next leg (up the screen). The camp's group stands toward that side. Camp 1 keeps its town, the wide plateau at the foot of the pass.
- **Gates and bands.**
  - The gates sit at the turns' middles (`CampDefinition.GateAhead`): trees, rock, Bridge, Causeway, the sealed gate. Their authored `ahead` values are unused on the pass.
  - One band per leg: a turn belongs to the leg after its middle. In-band effort takes three steps across a leg.
- **Removed on the pass.** The camp bays and coves are off (the legs below replace them).
- **Content.** Each leg has less forest than before (a leg is about 16 × 40 with the camp ledge in its middle). Leg length is the knob to turn.
- **Tests.** The width checks use the legs' 16 cells. Gate positions come from `GateAhead`. The bays' drop check applies only off the pass.
- **Step 2 next.** Each leg's world drawn 2 units lower than the last (eased through the turns), with one cell↔world mapping for everything; Astra's paintings for legs two or more away; the valley floor; the castle.

## 2026-10-16: the switchback pass, step 2: the look (Astra's pass pack)

- **Each leg drawn lower.** Each leg's whole world lies 2 units lower than the leg before (Astra's 512 px cliff).
  - The drop eases (3t² − 2t³) across each turn and 7 cells into the legs either side (`RidgeLayout.DropAt`, `DropRampIntoLegs`). On a turn's inside the cells are packed tight, and the steeper ramp of the paintings alone drew neighbouring cells over each other, so the mapping wasn't one to one. The camps' ledges, mid-leg, stay flat.
  - The ground stays one plane to walk on: the drop is simply part of where a cell lies.
- **One mapping.** `RidgeLayout.CellToWorld`/`WorldToCell` and the presentation's `IsoMap` serve everything that draws or walks: the ground tiles (each tile's transform matrix lowered by its drop), trees, hills, cliffs, stations and their build marks, camps (`CampService.CentreOf`), the drive's path, and world bounds (extended by the lowest leg's drop).
  - `WorldToCell` finds which leg's level a point is drawn at, then settles on its cell.
  - The camera follows the supplier as before.
- **Paintings** (`PassView`, art in `Art/Placeholder/Pass/`, imported by `Tools/ArtImport/import_switchback_pass.py`).
  - The leg the supplier is on and the next are real ground. From the turn after the next leg, the pass is Astra's paintings: 8-cell strips by their start anchors, turns by their entry anchors, hazed toward the valley by distance. The real tiles, trees and cliffs there are hidden.
  - This moves on whenever the supplier enters another leg.
  - At the normal view, the legs two or more ahead mostly sit off screen (18+ units up); they show zoomed out and in the overview.
  - The paintings are capped at 2048 px and compressed (the README suggested no lossy compression at first), because the phone's memory couldn't hold them full size uncompressed.
- **The valley floor** (`valley_floor.png`, repeating) fills the view behind everything, drifting at 60% of the land's speed. It replaces the flat green past every edge, and the camera's background takes the valley colour.
- **The castle** stands with its gate on the road's end.
- **Tests.**
  - `RoadPlayModeTests.ThePassDropsALegAtEveryTurnAndWalksSmoothly` checks the drops, the tiles drawn at their drop, the mapping both ways, and walking round a turn without a jump.
  - `PassPlayModeTests` checks what is painted from camp 1 and from leg 2, and that the real ground is hidden behind the paintings. It writes wide captures; `world_overview.png` shows the whole pass.
  - The outline test hides the valley while it measures.

## 2026-10-16: Astra's ox, eight-direction walk V12 (Kevin approved)

- **Import** (`Tools/ArtImport/import_ox_v12.py`; `Art/Placeholder/Ox/`):
  - Each direction's standing pose and six assembled walk frames, on their full 1280 × 1280 canvases.
  - Every sprite's pivot is its direction's `ground` anchor, so turning never shifts him.
  - One scale for all directions: 720 px per unit, so his side view is about 1.6 units long and as tall as the supplier. No per-direction scaling (V12 has it baked in).
  - E, NE and SE are the pack's own mirrors and are never flipped again.
  - Filtering is point, as the pack asks. Images are capped at 512 px (still above his size on screen) for the phone's memory. The importer now applies filter and wrap through the settings it saves (earlier, a rule's filter or wrap was overwritten).
- **Animation** (`OxFigure`). The assembled frames play at 5 fps (the 1.2-second loop, the last frame looping to the first); the standing pose shows when idle. The rig's part transforms aren't used, as the pack says not to combine the two. He has the game's outline.
- **In the game.**
  - On the drive he pulls the wagon, facing the way it goes (`DriveView`, scaled with the wagon). The old single-sprite ox is only a fallback.
  - At the parked wagon he stands hitched past the shaft tips facing up the road (NE), with a small blocker so no one walks through him (`ShopView.Ox`).
  - Open question: he stands past the shafts rather than between them. Real harnessing needs a hitched-pose anchor from Astra.
- **Tests.** `OxPlayModeTests` checks all eight directions on one ground point and scale, never flipped; the walk advancing and looping in 1.2 s; the parked ox standing and solid; and the ox on the drive. It writes `Captures/ox_eight_directions_ingame.png`.
- **Kitchen brief.** `Docs/ART_BRIEF_KITCHEN.md`: levels 1 and 2 and the kit, with ground, pad, delivery and chimney points.

## 2026-10-16: the view from the pass (Kevin: "a nice view")

- **What went wrong.** From a camp, the next leg's real ground (only 2 units lower and further up the screen) stood behind the camp as a tan cliff block in front of the valley. Kevin's point all along: the descending cliffs are scenery, not layout.
- **Now.**
  - Only the road the supplier is on is real ground. The stretch past the next hairpin (ground, trees, cliffs) is hidden until the supplier is within 10 cells of the hairpin, then shown to walk onto (`PassView`).
  - Astra's layered valley view (`ParallaxBackdrop`) fills the screen behind the land: all five layers including `view_front`, each extended down by its bottom-edge fill, softened sideways so it doesn't streak. About 1950 of its 4096 composition pixels span the phone's width (no taller on wider screens).
  - The castle is on the far ridge at the pack's own scale, 8 % bigger per camp (was 1.45× and 25 % per camp).
  - The whole view sits 12 % of the screen's height lower than the screen's top, so the castle clears the HUD and the camp's arrival tip (Kevin).
  - Retired: Astra's painted far legs and turns, the tiled valley floor and the castle at the road's end (they floated over the view). The art stays in the project, unused.
- **Unchanged:** the road's layout, the 40-cell legs (the 24-cell experiment was dropped: camp 3's stalls fell off its ledge), camps, gates and gameplay.
- **Tests.** `PassPlayModeTests` checks what is hidden at camp 1, near the first hairpin, along leg 2 and at camp 3, with captures `pass_view_camp1`, `pass_view_hairpin` and `pass_view_camp3`.

## 2026-10-16: the parked ox ambles about (Kevin)

- `OxWander`: he rests 3 to 8 seconds, then walks to a free spot within 1.8 × 0.9 units of his place at the shafts (one time in three, back to it), at 0.45 units a second with his walk playing.
- He keeps to walkable ground, skips spots with a stall, wall or the supplier in the way, and stops if the supplier steps in front of him. His blocker moves with him. He stands still during the drive.
- Test: `OxPlayModeTests.TheParkedOxAmblesAboutNearTheWagon` (capture `ox_wander`).

## 2026-10-16: gated levels, step 1: level 1 is a plateau (Kevin)

- **Kevin's direction.** The parallax view doesn't work with the long road. Each region becomes its own level; when you get far enough you pass through a gate and the next level loads. That makes room for bigger, wider forest and mine areas, with no need to join them up.
- **Kevin's choices:**
  - 60 × 60 tiles per level;
  - the gate is a build project;
  - one way only;
  - the whole camp packs up and comes along;
  - a cliff with the valley view past the edges;
  - through the gate, the wagon drive.
- **The plan.**
  1. Level 1 as a plateau.
  2. Through the gate: drive, next level, saves.
  3. All six levels, each with its own ground, toughness and gate project; the castle at the end; the old road code and tests removed.
- **Step 1 (now).**
  - `RidgeLayout.Plateau`: one square plateau from `levelOrigin` (-24, -21), 60 cells, edges ragged by ±1.2 cells, cliffs all round.
  - The road runs straight from camp 1's clearing (the town, unchanged) to 3 cells short of the far corner. The level's gate stands across the far corner, 4 short of the road's end.
  - Left of the road (screen left) is flat forest. Right of it are the hills (the Mine's ground).
  - One band per level, toughening in three steps toward the gate. The other levels' gates and camps aren't laid out.
  - `CampDefinition.plateau`/`levelSize`/`levelOrigin`, set by `ProjectSetup.Road` (the switchback is off). `CampCentre(i)` is the same front spot on every level.
  - Until step 2, the "make camp" sign is hidden.
- **Open for step 3.** Today only gates 3 and 4 are build projects (the bridge, the causeway). Gates 1, 2 and 5 still need a better tool (old trees, a rockslide, the sealed gate). Kevin chose build projects for gates: decide each level's project and its cost then.
- **Tests.**
  - New level checks are in `CampTests` (square, camp at the front, gate at the far corner, one band, tougher toward the gate). `CampPlayModeTests` was updated, and the berry test fells up to 80 trees.
  - Ignored until steps 2 and 3, each with the reason written on it: `MovingOnTests`/`MovingOnPlayModeTests` (moving on), `RoadTests` six camps and bridge, `RoadPlayModeTests`, `PassPlayModeTests`, `SiteTests.DeeperRevealPersistsThroughReload` (band 2 is level 2's). The ox's drive check is skipped.
  - `world_overview.png` hides the HUD.
- **Later the same day (Kevin): an organic outline, no triangle at the north.** The level's outline is drawn on screen. At the front and sides it is an ellipse (40 % × 20 % of the size), swelling and dipping in three broad lobes. Toward the castle it squares off into a broad, gently bowed rim. The edge is a little ragged. The camp's clearing sits toward the front. The road runs straight up the screen to 2 cells short of the rim, and the gate is a band 7 cells either side of the road where it meets the rim.

## 2026-10-17: faster walking (Kevin)

- The supplier's walk is 40 % faster: 2.86 ground units a second (was the spec's 2.04), stepping 4.4 times a second (was 3.7), so the feet keep up. The townsfolk share the walk, so they're faster too. `StoreOwner.asset` and `citizen_blond_blue.asset`.

## 2026-10-17: gated levels, step 2: through the gate (Kevin)

- **The way on.**
  - A sign at the road's end, just past the level's gate on the north rim, offers "Through the gate to River Bend".
  - The moving-on panel keeps today's rules: the summons answered, the pads empty, the wagon's load. It says there is no way back.
- **The levels.**
  - `CampDefinition.RidgeFor(level)` gives each camp its own plateau: a seed per level for its outline, woods and hills; the camp in the same place at its front; that level's own gate (`gates[level]`) on its rim. Its band is the level's: level 2 is band 2.
  - `CampService.Ridge` is the company's level. `Levels`, `ExitWorld` and `AtNext` mean being within 2.2 units of the exit.
- **One way.**
  - On arrival, `SitesService.ApplyLevel` puts the Forest and the hills on the new level and forgets the last level's cut trees and broken rock.
  - The earned goods, the revealed layers and the stations (in their kits) come along.
  - Sites save their `level`. A load fixes up the level only after every section has read (`SaveScheduler.Loaded`).
- **Ground.** The scene keeps level 1's ground. `LevelView` lays another level's ground when the company gets there or a save loads there (the town kept as built), then rebuilds the Forest's trees and the Mine's cliffs (`ForestGridView.Rebuild`, `MineSlabView.RefreshGround`). `GroundKeys` holds the ground choice, shared with the scene builder.
- **The drive.** The wagon and ox come up over the new level's front edge, straight below the camp, and drive in.
- **The faster walk's fixes.**
  - Steps are 5.2 a second, so the feet stay planted.
  - The motor backs the supplier out of a wall he already touches before moving (a fast step could end in the counter rail's contact margin and then walk through it, drawing him in front of the counter). That push isn't counted as walking.
  - Let go of the stick and he keeps the facing he had.
- **Tests.** `MovingOnTests` covers the gate, one way, and level 2 remembered with its own cut tree; and each level its own land. `MovingOnPlayModeTests` covers the sign, the panel, level 2's ground cell for cell, the drive and arrival (captures `gate_*`). The ox's drive check is back. Still ignored until step 3: `RoadTests` six camps and bridge, `RoadPlayModeTests`, `PassPlayModeTests`, `SiteTests.DeeperRevealPersistsThroughReload`.
- **Open for step 3.** Levels 3 and 4's gates are build projects (the bridge, the causeway) and don't have their signs or gap fill on a level yet. Levels 1 and 2 play through.

## 2026-10-17: gated levels, step 3: all six levels (Kevin)

- **Gates (Kevin: keep the mix).**
  - Level 1: old trees, the iron tool. Level 2: a rockslide, the tempered tool. Level 3, Hollow Wood: a ravine, build the bridge. Level 4, Witchlight Fen: a flood, build the causeway. Level 5: the sealed gate, the moonsilver tool. Level 6, Castle Approach: no gate; the castle is beyond its rim, and the finale job is there as before.
  - Each level's gate stands across the road where it meets the north rim.
- **The project sign.** One `GateProjectSpot` follows the company's level (`EditorSetupLevels`). On a project level it stands on the road 5 cells before the gap. Built, the gap fills with the bridge's planks or the causeway's stones and the Mine's cliffs there go. `LevelView` places it again after laying a level.
- **Each level's ground** takes its stop's tint (`CampDefinition.LevelTint`); the town keeps its colours. Level 1 is as painted.
- **Retired:** `PassView` and its test. The scene builder no longer builds the pass.
- **Tests.**
  - `RoadTests`: six levels and their gates, each level its own band and three steps, the tints, the bridge built on level 3 and remembered.
  - `RoadPlayModeTests`: every level laid cell for cell with the camp and the wagon on land, captures `level{n}_camp` and `level{n}_overview`; the ravine, the bridge built and walked, the causeway's sign, the debug jump back to level 1.
  - `SiteTests.DeeperRevealPersistsThroughReload` is still ignored: its hill helper walks level 1's road, so band 2's reveal surviving a reload isn't tested on a level yet. `MovingOnTests` only checks that level 2 is band 2 and that its cut trees are remembered.

## 2026-10-17: a full carry stops the swing (Kevin's playtest)

- **The bug.** With the carry full, chopping or digging went on, and the units went onto a waiting pile at the site. The next swing at any tree or block pulled the whole pile into the carry at once, so the first chop of a tree jumped the carry to 12/12.
- **Now.**
  - A full carry stops the swing: no progress on the tree or block until the carry is unloaded. The toast reads "Carry full: take the logs to the Lumbermill, then come back for more."
  - Units already waiting (older saves, or a block's units past the carry's last space) come back one at a time: each unit gathered by hand brings at most one waiting unit with it.
  - `ISiteLedger.CarryFull`.
- **Tests.** `HarvestTests`: a full carry stops the swing and nothing piles up; after unloading, a tree gives at most one log per swing; waiting logs come back one at a time. `EconomyTests`: no digging with a full carry.

## 2026-10-17: the swing, the wagon's doorway, helmets and shields, instant crafts (Kevin's playtest)

- **The swing (Kevin: it clipped through the body at every angle).** `HoseRig`:
  - One hand on the tool (his right). The other stays at his side and braces a little, no reach across his body.
  - The hand holds the tool near the end of its handle (`HoseCharacterDefinition.axeGrip`/`pickaxeGrip`, from the art's pivot at the handle's middle).
  - The blade leads the swing: the tool is turned over when the strike's direction is on the handle's left on screen.
  - Chopping (Kevin: side to side, a swing then the pull back) is a level swing at chest height: cocked back past his right side (100°), a fast strike across to forward-left (-28°), a beat at impact, then a slower pull back.
  - Mining stays overhead, its plane tipped 24° out to his right, settling at his side (125°), not between his legs.
  - `HoseRigPlayModeTests.SwingFrames` draws the mining and the chop in five views (`hose_swing_sheet`).
  - Facing the camera, the chop's strike comes toward the viewer and the axe looks short over his chest. A painted front chop from Astra would fix that.
- **The wagon's doorway (Kevin: in beside the door by the service desk).**
  - The doorway is now set to the steps' top corners. It already was in practice, so this changed nothing measurable.
  - `WalkInCartPlayModeTests.OnlyTheStepsLeadThroughTheDoor` walks at the door from all round (starts outside the walls' cushion) and finds no way in but the steps. It draws the walls, the floor and the doorway (`cart_walls_probe`).
  - Kevin's spot isn't reproduced yet; waiting on a screenshot.
- **Helmets and shields (Kevin: they can't be crafted).** Their recipes and blueprints were there (owned at start), but a save begun before the armour pack never learned them: starting blueprints were given only to a new company. Now every starting blueprint is known on load. `CraftingTests`.
- **Instant crafts (Kevin: temporarily, for playtesting).** `BalanceProfile.playtestInstantCrafts` (on) makes every craft finish at once in the game. Tests, with their own save directory, keep the real times. Turn it off when the playtest is over.

## 2026-10-17: Astra's four item views; the carried pile turns with the supplier

- **The pack.** Astra's four item views v1 (`Tools/ArtImport/import_item_views.py`). For 41 items and materials, A and B are the views already in `Items/` (unchanged; armour's A stays its armour-stand layer), plus the new front and side views. All are 256 px per unit with the pivot on the display anchor (128, 228), and use the game's outline. Their points are merged into `Items/points.json`, and `Items/views.json` is kept.
- **A and B are named for the furniture facing, not the item's lie,** so the import measures which way each item's A view runs (`a_runs_up_right`, from the opaque pixels' principal axis). Cedar's A climbs to the right; maple's, ironwood's and the iron ingot's climb to the left.
- **Content.** `ObjectViews` (a, front, b, side, aRunsUpRight) on `ItemDefinition.Views` and `ResourceDefinition.Views`, set by setup from `views.json`.
- **The carried pile** (Kevin: only one angle was used). Each piece shows the view that lies across the supplier's way (`ObjectViews.Crosswise`):
  - facing the camera or away (S, N): the side view, its long side across the screen;
  - facing left or right (E, W): the front view, end-on;
  - on the diagonals: the A or B view that runs along the other diagonal.
  - The views are drawn at half scale (256 px per unit against the single stack sprites' 512). Items without views keep their stack sprite.
- **Shop furniture** already shows A on left-facing pieces and B on right-facing ones; unchanged.
- **Tests.** `StackSpritePlayModeTests.TheCarriedPileTurnsWithTheSupplier` turns him through all eight directions with logs, an ingot, lumber, a sword and a coat, and checks each piece's view (captures `carry_views_*`).

## 2026-10-17: smaller yields; digging the wall in front (Kevin's playtest)

- **Yields (Kevin: "a tree should be 2 logs and a stone should be one").** A tree gives 2 logs (`site_forest` units per tree, was 4). A hill block gives 1 unit: the ore's 50 % bonus unit is gone for hill blocks. The rare find (a gem, a herb) still rolls.
- **The wall in front (Kevin: facing a wall, the stones right in front couldn't be selected).** `ForestGridView.RockTarget`:
  - A tree no longer hides the rock beneath it unless the tree is within reach (its ground no higher than a step). A tree on top of a wall left the wall undiggable, and the button offered the unreachable tree.
  - Facing between the grid's axes, the other neighbouring cell that way is tried too (`FacingCells`).

## 2026-10-17: the Blacksmith (Astra's modular pack; Kevin: one setup)

- **The art.** Astra's modular caravan Blacksmith (`Tools/ArtImport/import_blacksmith.py`, `Art/Placeholder/Blacksmith/`): the rug, the furnace and the crafting station at three levels each, their fire overlays, the delivery and pickup mats, and the kit. All are untrimmed canvases at 256 px per unit with the pack's pivots. The stations and the kit have the game's outline; the rug, the mats and the fires don't.
- **One setup (Kevin's choice).**
  - The Forge's crafting station leads a 4 × 3 camp setup (`station.forge` footprint 4 × 3, set-up name "Blacksmith"). The Smelter's furnace rides with it (`StationDefinition.ridesWith`).
  - `StationsService` keeps a rider on its leader's spot, mode and build time. A rider takes no cells of its own, packing or placing either one moves the whole setup, its leader's kit is the only kit listed, and a pad on any of them blocks packing.
  - Build mode picks the whole setup (`StationBuild.Select` takes the leader).
  - Each station keeps its own levels, recipes, processes and saves. Nothing in the economy changed.
- **In the scene.**
  - The rug at (-12.9, -1.9), where the Smelter stood, with the pack's parent-local positions. The furnace is "Smelter" (its `StationInspectable`, the progress bar at the art's point, a 1 × 1 blocker). The crafting station is "Forge" (a 0.8 × 1.5 blocker). The rug and mats are Ground-layer sprites under everyone, walked on.
  - `receiver.smelter` stands on the delivery mat and `container.forge_pad` on the pickup mat (its old pad art removed).
  - The old Smelter and Forge buildings are gone.
- **`StationArt`.** Each station shows its level's sprite and fire (all nine pairings). The fire flickers while the station works (a smelting batch in flight; a craft on it, plus 2.5 s so an instant craft still flares) and is out when idle.
- **Tests.**
  - `BlacksmithPlayModeTests`: idle fires out, the aisle open, the stations solid, both fires at once, the furnace to 3 with the anvil at 1 then 2, upgrades in place (captures `blacksmith_*`).
  - `StationPlayModeTests` moves and packs the whole Blacksmith, the Smelter with it.
  - The haul and craft tests use the mats.
- **Not done.** The kit art isn't shown anywhere yet (kits are only listed in build mode). Smoke and sparks at the chimney and anvil points are not added.

## 2026-10-17: the playtester (Kevin: an agent that plays the game and reports)

- **The bot.** `AutoplayPlaytest` (PlayMode; `[Explicit]` and category Autoplay, so the suites skip it). It plays a fresh camp with only the stick (`ScriptedMove`) and the action button (`SandboxHud.ScriptedHold`/`ScriptedPress`):
  - it explores eight ways, chops, digs, delivers, visits the Forge and the Store, and walks to the gate;
  - it plans routes with A* over open ground (steps of one block, no blockers) and sidesteps when stuck;
  - it records snags with what was in the way, the button's label at each job, times, errors, frame times and screenshots, into `Captures/Autoplay/report.md`, `timeline.txt` and `Captures/autoplay_*.png`.
- **The agent.** `.claude/agents/playtester.md` runs the bot, reads the report, timeline and screenshots (and recent suite captures), and writes `Docs/Playtests/<date-time>.md`: broken, could be better, looks fine. It never changes the game.
- **First report.** `Docs/Playtests/2026-10-17-2033.md`.
- **Fixed from it: the Forge's button.** Standing at the Forge, the button offered "Inspect Forge Output" (the pickup mat lies nearer than the Forge's pivot), and tapping opened an info card. Stations now get a 0.6 priority over the pads and markers by them (`StationInspectable.ContextPriority`), so the Forge's crafting panel opens. Checked in `BlacksmithPlayModeTests`.

## 2026-10-17: the playtester's first findings, fixed (Kevin: 3, 5 and 7)

- **Trees fade in front of the supplier** (pines hid him almost completely; the market stall fades but trees didn't). `ForestGridView.FadeTrees`: a tree standing lower on the screen than him, whose picture covers his middle, eases to 40 % and back when he steps out. Checked in `PlaytestFixesPlayModeTests`.
- **The message box fits its text** ("Camp made at …" ran over the debug banner and out of its box). The toast hangs below the banner (its top 236 below the screen's top), 820 wide, grows down to fit its wrapped text, and stays up 1.4 to 4 s by length. Checked in `PlaytestFixesPlayModeTests`.
- **Saving** (81 saves in two minutes of walking). `SaveScheduler.MarkDrifted`: changes that happen all the time save every 20 s at most, and on pause or quit. These are time passing (the sites, production, the simulation clock, the job board), walking, and a swing's progress until something is gathered. Real events still save at once or after the 1.5 s debounce: a tree felled, a block broken, a delivery, a completed expedition, a purchase. Checked in `SaveRateTests`.

## 2026-10-17: the second playtest's fixes (Kevin: 1, 2 and the Forge panel)

- **Digging beside a tree** (facing a hill block at the forest's edge, the button chopped the tree next to it).
  - The chop target was the nearest tree in reach whichever way he faced, and it outranks digging. Now, facing a diggable block with the nearest tree not in front, the rock wins (`ForestChopSpot`).
  - `FacingCells` counts the side cell only when facing more than about 20° off a grid line. Facing straight along one, a tree in the side cell had counted as "in front".
  - Checked in `PlaytestFixesPlayModeTests` (three runs, three world seeds).
- **Trees in thick forest.** Every tree in front covering any part of him (feet, middle, head, either side) fades, now to 25 % (was 40 %, middle only).
- **The Forge panel** (the last craft button was cut off, Close scrolled out of sight). Close is pinned to the card's foot, the list scrolls above it, and a "More below ▼" hint shows while it runs on. This applies to every station panel. Checked in `PlaytestFixesPlayModeTests`.
- **Later (third playtest: the fade still didn't show).** Two causes, both fixed.
  - **The tree shader ignored the renderer's colour.** In Unity 6 a sprite renderer's colour reaches the shader as `unity_SpriteColor`, not only in the vertex colour, so `SpriteUnlitStencilMark` (the trees) drew every tree fully opaque and untinted. It now multiplies by `unity_SpriteColor` (Core2D). Side effect: the trees' designed tints show for the first time: the gates' old trees darker, each later band a touch cooler and darker.
  - **"In front" is the draw order now, not the screen height.** On the Ground layer, a tree drawn after the supplier's sorting group that covers him fades. A tree on a hill, or in his own row, could be drawn over him while standing "behind" him on screen.
  - Checked by looking at the captures (the supplier visible through the faded trees), not just by the fade's number.

## 2026-10-17: the fourth playtest's fixes (Kevin: 1, 3 and 4)

- **Facing a rock beside a stump digs it.** With no standing tree in reach, the button pulled the nearest stump whichever way he faced. Stumps now yield to a faced diggable block as trees do, unless the stump itself is the cell faced (`ForestChopSpot`). The bot had walked to a tree up on a cliff (a tree above his own height can't be chopped), so its tree search now skips trees more than one step above or below him. Checked in `PlaytestFixesPlayModeTests`.
- **The wagon fades with the supplier behind it** (digging behind it, only his hat showed). `WagonFader` eases every wagon piece to 30 % while he stands outside it, above its floor on the screen, and covered at his feet, middle or head. Not while he is inside it, and not the ox. The wagon's single outline is a solid dark silhouette drawn behind its pieces, so it fades with them; left opaque, it hid him completely. The pieces stack (back, front, roof), so 30 % rather than the 40 % one sprite would use. Checked in `PlaytestFixesPlayModeTests` and by looking at the capture.
- **The gate's old trees are a deeper, cooler green** (0.46, 0.62, 0.5). The old olive-brown (0.52, 0.5, 0.42) read as muddy once the tree shader started honouring tints.

## 2026-10-17: the fifth playtest's fixes (Kevin: 1, 2 and 3)

- **The bot's way to the gate.** The first diagnosis ("every cell round him touched the wagon") was wrong; the sixth playtest still failed the same way. The real cause: the gate's band of old trees (the better axe's trees) runs across the way, and ordinary trees close the gaps at its ends. So with the starting axe the road's end is usually shut on foot. On some worlds a gap is left round the band's end, which is why some trials reached it. Aiming at the road's end, the bot found "no route" and walked straight into the wagon. It now walks to the nearest point in front of the old trees and notes that the gate is shut. Bot only (`AutoplayPlaytest`); the game didn't change. **Open question for Kevin:** on worlds with that gap, a player can walk round the gate without the better axe.
- **Camp furniture fades with the supplier behind it** (the weapon rack and the dummy hid him chopping behind them). The wagon's fader became `BehindFader` and now goes on every furniture piece as well. It fades all of the piece's sprites, its stock included, to 30 % while he stands behind it and it covers his feet, middle or head. It skips the wagon while he is inside it, and never fades the ox. Checked in `PlaytestFixesPlayModeTests` and in the capture.
- **The Trader's stall keeps him out.** Its blocker was a 2 × 2 diamond (1.84 wide) under art 2.47 wide, so from the back or the side he walked about 0.3 in among the posts. With the stall faded, he looked like he was inside it. It is now 2.65 × 2.65 (about 2.44 wide), matching the posts. Checked in `PlaytestFixesPlayModeTests`.
- **Astra's workshop UI** (her work, committed alongside: `WorkshopUi`, `WorkshopHeroPreview`, the reworked `LoadoutPanel` and `StationPanel`, and her `WorkshopUiPlayModeTests`). Shipped as she left it, after the full suites passed.

## 2026-10-18: Astra's UI, second pass

- Astra restyled the rest of the HUD and panels in the workshop look:
  - the HUD's top-bar buttons (gold chips), the info card (taller, with auto-fitting text) and the debug panel;
  - the away report, job board, loadout, move-on, result, settings, shop build, Store, tip and Trader panels;
  - `UiFactory` and `WorkshopUi`, plus her `WorkshopScreensPlayModeTests`.
- Committed as she left it, after the full suites passed.
- Two tests updated for it:
  - The joystick test hides the tips first: her taller tip card covers the middle of the 640 × 480 headless screen, where the test starts its drag. On the phone it sits at the top.
  - The quest map's "not bunched" threshold is now 250: the nodes spread 296 in her narrower panel, against the old 300.

## 2026-10-18: the display picker (Kevin: choose what tables and stands show, like equipping a hero)

- **Tapping a display piece** (a table, armour stand, weapon stand or shelf) now opens a picker instead of an info card; the button's verb is "Arrange" (`DisplayInspectable`, `DisplayPanel`). It is built like Equip Hero:
  - the piece's spots on top, each showing its item ("Auto · …" dimmed when the racks chose it);
  - a swipe strip of the Store stock that fits the selected spot, with counts, plus **Auto**;
  - Done and × close it.
- **The choice is a display preference only**, as the GDD's stocking and display rules require: nothing moves or is reserved, and Store inventory is untouched.
  - `Placement.Shown` holds one item definition id per spot ("" = Auto). It is saved as `shown` (ids, not assets); ids no longer in the content fall back to Auto.
  - `ShopService.SetShown(placementId, spot, itemId)` refuses items the piece can't show, with a reason.
- **Racks**:
  - A chosen spot shows its item while one is in stock, and falls back to Auto when it's gone.
  - Auto spots fill in the racks' order (pinned, strongest, newest) and never repeat an item chosen elsewhere on that kind of piece.
  - A dressed armour stand's spots are Body, Head and Shield, each limited to its category.
- Checked in `DisplayPickerPlayModeTests` and by looking at the capture.

## 2026-10-18: camps by the north rim, with the castle view (Kevin: "locate the campsites with a view of the parallax"; gate on a side rim)

- **Each level's camp stands just below its north rim.** The castle backdrop is anchored to the screen, about a third of the way down, so it only shows where the land's edge is lower on screen than that. In practice that means the edge has to be a few steps up the screen from where you stand. The plateau is now laid with its rim about 7 world units above the camp's centre (`SetLevelAround`: centre 0.42 × the half-height below the camp). That puts the rim just behind the wagon, so standing at the wagon's counter the castle and valley show over the edge (`level*_view.png`).
- **The overlook** (`RidgeLayout.InOverlook`): between the camp and the rim, open grass with no trees and no hills. The hills also stay flat within 5 cells of the north rim, so nothing stands up into the view. The camp's clearing never adds land past the rim.
- **The gate is on a side rim** (Kevin's pick): east on levels 1, 3 and 5, west on 2, 4 and 6 (`PlateauSide`). The road runs from the camp down and across to a point a quarter of the way down that side; the gate's old trees stand across it near the rim. The wagon arrives on a new level from the opposite side (`DriveView.ArrivalEdge`).
- Checked:
  - `CampTests`: the rim within 20 cells up the screen from the camp but more than 8, and the gate at least 12 world units across the screen.
  - `RoadPlayModeTests`: the wagon's floor is on land on every level, plus a capture from the counter.
- Two test helpers (`DigTestKit.HillBlocks`, `CampPlayModeTests.TreeAhead`) searched across the road with a fixed screen direction, which only works for a road straight up the screen. They now use `PointBeside`, so they search across the road whichever way it runs.

## 2026-10-18: modular characters (Astra's ravenholm-modular-characters-v1: one head, outfits that swap)

- **Imported** by `Tools/ArtImport/import_modular_characters.py` into `Art/Placeholder/Characters/Modular/ravenholm_standard_512_v1/`: the heads, torsos and skin masks, plus `points.json` and `library.json`. The previews are not imported.
  - The script checks that the pack's shared hands and boots are byte-identical to the store owner's, which the game already has.
  - Heads and torsos use the store owner's ground pivot (pixel 256, 444) at 340 PPU. They are untrimmed and uncompressed, so they meet exactly.
  - Masks are data: linear, point filtered, uncompressed, and no colour is bled into their transparent pixels.
- **The citizen is modular** (`ProjectSetup.ApplyModular`, from the pack's `library.json`): head set blond, everyday outfit citizen_blue, and `item.steel_coat` worn as steel_armour. Other armour keeps the everyday outfit.
  - `HoseCharacterDefinition` gains a head and skin mask per view, outfits (a torso and mask per view), worn armour (item → outfit), hand masks, and the skin's base and shadow.
  - The painted bodies stay as the fallback: a definition without heads draws them as before. The supplier is unchanged.
- **The rig** (`HoseRig`):
  - The torso is drawn in the body's place, and the head is its child at zero offset, so they share the lean, bob, sway, squash and mirror. There are no per-outfit nudges.
  - The view is chosen once for head, torso and boots. Mirrored directions flip the whole rig about the ground pixel, which is the pack's "+1 px" rule.
  - `ArmourItem` picks the outfit (`WornOutfit`). A swap changes only the torso: pose, phase, feet, hands and head stay put, which is tested against an unswapped twin.
  - Draw order: the head sits over its torso, and everything at 7 and above moves up one.
  - At rest and walking, the near arm hangs from under the torso's painted shoulder cap, and its hand still passes in front. The pack's rule is "the shoulder cap remains above the arm root".
- **Skin**: the `Ravenholm/SpriteSkinTint` shader (`SkinTint.mat`) replaces only masked pixels with the character's skin base or shadow, on the head, the neck and the hands; the arms use the skin base. `SkinBase` and `SkinShadow` on the rig set a second palette.
- **Wearing it**:
  - Applicants leaving or returning with a loadout wear its body armour as well as its helmet (`ApplicantCrowdView.Wear`).
  - The Equip Hero portrait shows the steel outfit instead of laying the item picture over the body. Unmapped armour still uses the overlay, and the head stays its own part.
- **Outline**: the game's outline copies sit behind the whole figure, so the neck covers the head's lower outline and only the assembled silhouette is outlined.
- Checked in `ModularCharacterPlayModeTests`:
  - all eight directions, idle and walking, swapping the coat on and off mid-stride;
  - carrying in every direction;
  - a second skin on the head, torso and hands.
  - It also writes the sheet `Captures/modular_characters.png`, which I looked at: no gaps, no head jumps, and the outline only on the outside.
- The Equip Hero portrait reports armour worn as an outfit as shown (`WorkshopHeroPreview.ArmorShown`); Astra's portrait test checks every armour.
- The tree-fade test now picks a tree he can stand behind among the trees: on some worlds since the camp moved, the first tree stood at the camp's edge, where he is drawn above the forest and nothing covers him.

## 2026-10-18: armour v2, helmets, and weapons carried visibly (Astra's ravenholm-modular-armour-v2; Kevin: "carry weapons and shields visibly when equipped")

- **Imported** the v2 pack with the same script, which now handles `Helmets/`. Every v1 sprite and anchor in it is byte-identical, which I checked.
  - 35 torsos: the everyday clothes and six body armours.
  - 20 helmet overlays: four sets in five views each.
- **Mappings** come from `library.json`:
  - `equipment_to_outfit`: `item.leather_jerkin`, `padded_gambeson`, `riveted_coat`, `fur_brigandine`, `steel_coat` → steel_armour, and `moonsilver_mail`.
  - `equipment_to_headwear`: `item.leather_cap`, `iron_helm`, `steel_helm` and `moonsilver_circlet`.
  - Head identity, skin, outfit and headwear are independent (`HoseCharacterDefinition.headwearSets` and `wornHeadwear`, `HeadwearForItem`, `HelmetFor`).
- **Helmets** (`HoseRig`, modular): the helmet is a child of the head at zero offset and draws over it, in all five views and their mirrors. It replaces the old SE-only headwear, which non-modular characters still use. `WornHeadwear` reports what is worn; with none, the hair shows as drawn.
- **Weapons and shields carried** (`HoseRig.HeldWeapon` and `HeldShield`):
  - Sized and gripped as the Equip Hero portrait holds them: the item's display art, its grip at the hand, the hand switched to its grip pose, drawn just under the fingers.
  - The weapon goes in his right hand and the shield in his left. In the mirrored views the rig's reflection swaps the drawn hand parts, so `WeaponHand` is the left part there and the anatomical hand stays the same.
  - A two-handed weapon leaves no hand for a shield. Heavy two-handed weapons (the maul) hang head-down and trail at 140°, three-quarters length: held upright, the maul's head covered his face. Nothing is drawn while working.
  - Applicants leaving or returning with a loadout carry its weapon and shield (`ApplicantCrowdView.Wear`).
- Checked in `ModularCharacterPlayModeTests.EveryArmourHelmetAndHeldItem`:
  - eight directions while walking, with the armour and helmet swapped on their own schedules mid-stride, against an unswapped twin: no head jump, no sole jump, no size change;
  - the helmet on the head and over it;
  - the weapon in the right hand in every direction, and the shield in the other;
  - a two-handed weapon drops the shield; unequipping empties the hands.
  - The sheet `Captures/modular_armour.png` (every armour with a helmet and an item in hand, the last row a darker skin) was looked at: eyes visible under every helmet, no hair through the crowns, no neck seams.
- `ArmourStandPlayModeTests` now expects helmets in the E view too: v2 supplied every view.
- Two more tests made independent of the world's layout: `StationsTests` finds its hill across the road whichever way it runs, and the walk-spec test walks from open ground it finds rather than from the spawn.

## 2026-10-18: the backdrop (Kevin: the parallax too extreme, the stretched bottom looked off, mountains behind the castle)

- **Gentler parallax:** `ParallaxBackdrop.parallaxStrength` = 0.35 scales the pack's factors (front 0.6 → 0.21, near 0.35 → 0.12, and so on). Each layer keeps its relative depth; all of them slide about a third as much.
- **Below the painting:**
  - The nearest full-width layer (view_near) continues as Astra's seamless valley floor (`Pass/valley_floor`, made to repeat), tiled down under it, in place of its stretched bottom row.
  - The front bushes stop at their edge instead of streaking down.
  - These are set by `fill_mode` in our copy of `View/parallax.json` (floor or none). The far layers keep their fills, which are hidden.
- **Mountains behind the castle:** `View/castle_mountains.png` is drawn by `Tools/ArtImport/make_castle_mountains.py`. It is a stand-in until Astra paints one: three angular snow-capped peaks in the far layer's haze palette, with one shadow face each, paler peaks behind, and a mist band at the foot in the far layer's own colour.
  - It is a child of the castle, so it moves and grows with it, with its foot on the castle's ground, centred on the castle's art.
  - It draws between the far layer and the castle.
- Checked in the captures: the castle view from the wagon's counter, and the level overviews (the valley floor below the cliffs, no streaks).

## 2026-10-19: horseshoe levels (Kevin's drawing in the Camp Planner)

- **The shape:** every level is a horseshoe.
  - Wide land is open to the north in the middle: **the bay**, a drop with the valley and the castle beyond. It reaches past the north rim, so the view is never closed.
  - The camp sits at the bottom of the U. The bay's edge is about seven world units above the camp's centre, just behind the wagon, so the castle shows from the counter.
  - **Arms** either side reach north. Forest and hills mix all over ("a healthy landscape"): the old rule that kept one side of the road flat is gone. Only the overlook in front of the camp stays flat and treeless.
  - Code: `RidgeLayout.SetLevelAround`, `InOutline`, `InBay`, `InOverlook`. Proportions vary per level by its seed (outline ±6 %, bay ±10–12 %, the bay's centre ±2.5 units).
- **The road** runs from the camp west below the bay, up the west arm, to the north-west tip (`BuildPlateau`, corners rounded). The wagon arrives on a new level from the south edge.
- **The pass:**
  - The last 16 cells of road run between **ridges** five blocks high that no tool breaks (`InPassRidge`, `UnbreakableTier` = 99; the button reads "Needs a way through the gate"). They cover only the west arm's tip, within 16 cells of the road.
  - The corridor between them (`InPassCorridor`, 2.6 cells each side of the road) and the way out past it stay clear of trees.
- **Gates are things to fix** (Kevin: "we are unlocking the old path to the castle, so we're fixing something at each stage"). There are no tool gates any more. The five levels in order: repair the gate (cedar lumber 8, iron 4), a bridge (maple lumber 12, rope 6), a causeway (ironwood lumber 12, steel 6), the sealed gate (steel 8, ironwood lumber 8), and the last bridge (moonsilver 6, rope 10).
  - A gate project is a **barrier** (`Gate.Barrier`), not a gap: the ground stays, and a placeholder gate stands in the pass with a wall across it.
  - Repairing it from the sign ("Repair the gate") opens the doors and drops the wall (`GateProjectSpot`).
  - The placeholder art is `Tools/ArtImport/make_gate_placeholder.py` → `Props/gate_closed.png` and `gate_open.png`; Astra will replace it.
- **Tool check on the current level:** `SitesService.RequiredToolTier` reads the level the company is on, not level 1's layout. It always read level 1 before.
- Tests updated to the horseshoe and checked by eye in the level overviews:
  - `CampTests`: the bay above the camp and open north, both arms, the gate north-west, a ridge no tool breaks.
  - `RoadTests`: the five projects.
  - `RoadPlayModeTests`: repair the gate and walk through, then build and cross the bridge.

## 2026-10-19: more heads and everyday clothes (Astra's head library v2 and civilian outfits v1)

- **Imported and merged.** `import_modular_characters.py` now upserts `library.json` and `points.json` by id instead of overwriting them, so the armour, headwear and blond entries stay. That adds 12 head identities (60 heads and their masks) and 12 everyday outfits (tunic, waistcoat, apron and coat, three colours each; 60 torsos and their masks).
- **Head identities:** `HoseCharacterDefinition.headSets`, built by setup from `library.json`.
  - Each head has its five views and masks and its own default skin, which the neck, hands and arms take; the rig uses `SkinOf(head)` unless a skin is set.
  - Each head has its own helmet nudges (`headwear_fit_offsets`: pixels on the canvas, applied to the helmet under the head, mirrored with the rig).
  - `HoseRig.HeadId` picks the head; empty means the blond.
- **Everyday clothes:** `civilianOutfits` is every outfit that isn't armour: the citizen's blue plus the 12.
  - `HoseRig.CivilianOutfit` is the character's civilian_outfit_id. Mapped armour overrides it while worn; taking the armour off restores it.
  - Changing clothes or head keeps the pose, stride, direction, helmet and skin: tested against an unchanged twin.
- **Per contractor, saved:**
  - `ContractorRecord.HeadId` and `OutfitId` are saved as `head` and `outfit`.
  - Empty means picked from the contractor's id with a stable FNV hash (`CitizenAppearance`): the same on every load and device, and varied across the crowd.
  - `JobsService.SetAppearance` (a command) sets either.
  - The crowd, the heroes leaving and returning, and the Equip Hero portrait all show that look.
- **Choosing:** Equip Hero's caption under the portrait is now a "Clothes ›" button. It cycles the hero's everyday outfits, saves the choice, and the hero standing at the wagon changes at once.
- Checked in `ModularCharacterPlayModeTests.HeadsAndEverydayClothes`:
  - all 13 heads in eight directions while walking, with clothes changed mid-stride;
  - the head's skin on the head, neck and hands;
  - armour restoring the chosen clothes;
  - the helmet kept through changes, with its nudge;
  - carrying, and the look kept through a save and reload.
  - The sheet `Captures/modular_people.png` (13 heads by 8 directions, mixed clothes, helmets every fourth row) was looked at: necks match faces, no seams.
- Astra's Equip Hero test now counts the six slot buttons by name; the Clothes button sits beside them.

## 2026-10-19: arms follow Astra's rig; in profile the near arm hangs in front (replaces "arms hang from the shoulders")

- **Cause:** the arms are drawn by code and the sleeves are painted on the torso, with one draw order between them. Every attempt that moved the arm's root (trimmed to the sleeve hem, shoulder joints with a sleeve cap) showed the seam somewhere.
- **Fix:** Astra's own rig (`points.json` `rest_draw_order`): legs, then arms and hands, then the torso, then the head.
  - The arms hang from her shoulder anchors **behind** the torso to her wrist targets, so they show below the sleeves with the hands beside the body, as in her walk preview.
  - The swing is scaled by `modularSwingScale` (0.3), so the hands never cross the body.
  - The shoulder joints and the head-on hand-inward move are removed for modular characters.
- **Exception, the profile (E/W):** the pack hangs the near arm behind the back, where it read as an arm on his back.
  - It hangs in front of the body instead, from a root inside the short sleeve, canvas px (215, 268), straight down to (213, 336). These are set by `ProjectSetup.NearArmPx` into `View.nearArmRoot` and `View.nearArmWrist`.
  - The torso is drawn again over the arm's top, cut to a circle round that root (`profileCapRadius` 0.059 units = 20 px; the `SleeveCap` overlay using `_CapA`), so the arm comes out of the sleeve's opening. The sleeves end at y 284-291 on every torso.
  - Known small overlap: the padded gambeson's pad ends at y ~300, so in profile the arm covers its lower edge.
- Checked on renders of all eight directions over four stride moments (coat, waistcoat on dark skin, riveted armour), plus every outfit and armour (19) in E and W. Kevin said to install it.

## 2026-10-19: Astra's approved shop displays (weapon cradle, potion display)

- **Art:** from `ravenholm-shop-displays-approved` (points.json schema 2), in `Art/Placeholder/Furniture/`.
  - Files: `weapon_cradle_1_A`, `weapon_cradle_1_A_front` and `potion_display_1_A`.
  - Import: 512 canvases, 256 PPU, pivot (0.5, 0.1797), point filtering, uncompressed, full rect. The tip rail is not outlined.
  - The pack's assets are merged into `Furniture/points.json`; their pivots are in `sprite_meta.json`.
- **Facing:** Astra's approved angle (long rail descending to the right) is the game's **facing B**. The art replaces level 1 facing B of the Weapon Stand and the Potion Stand (`ProjectSetup` `approvedB`). Facing A and levels 2-3 keep their placeholders. Both stands start in facing B at the wagon.
- **Renderer (schema 2):**
  - `ShopFurnitureDefinition.Art` gains `poses`: per slot, the anchor, the clockwise turn, the order, a tip anchor or bottom-centre anchor, and the weapon kind it seats.
  - It also gains `foreground` / `foregroundOrder` (the tip rail at 30, over the items at 10-15) and `visible`, the art's bounds. The tap target is sized from `visible`, because the full empty canvas covered the display table's tap.
  - `FurnitureView.Posed` draws the item's front view. The view's visible pixels are fitted uniformly, after turning, into the slot box, then turned, and its anchor point is set on the slot's anchor. That point is the blade's tip (the mean of the top opaque row, upright) or the visible bottom centre.
  - `ObjectViews.frontBounds` / `frontTip` are measured from the PNGs at setup (`MeasureFront`).
- **Seating:** Astra validated only straight swords. The cradle's slots seat blades (`WeaponTag.Blade`): the maul and other weapons are not stocked there or offered in its picker (`FurnitureView.Seats`); they wait for another display. Potions and food fill the six pads, back row (slots 1-3) before front.
- **Checks:** `ShopDisplaysPlayModeTests` checks:
  - tip and foot on the anchors;
  - turn and fit;
  - orders and rail;
  - the maul kept off;
  - footprints blocking, with room at the wagon's steps;
  - captures in front of and behind the displays.
- **Floor mats:** the wagon shop's patterned carpet is left as is, pending Kevin's answer on whether it is the "floor mat" to remove. The teal cloth is part of the potion display's art.
