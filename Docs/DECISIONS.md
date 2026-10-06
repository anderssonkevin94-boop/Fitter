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

## Open blockers

- **iOS build:** the Unity → Xcode device export succeeded headlessly on 2026-10-06 (0 errors, 1 warning, IL2CPP, 114 s). The Xcode compile/sign/install step has not run yet: the startup disk had only ~2.5 GB free, too little for Xcode intermediates. To do: free disk space, export again (Ravenholm > Build > iOS Xcode Project (Device)), open `Builds/iOS-Device/Unity-iPhone.xcodeproj`, pick a signing team, then Run on the iPhone.

## Playtest feedback

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
