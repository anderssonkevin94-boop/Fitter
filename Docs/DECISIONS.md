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

## Open blockers

- **iOS build:** the Unity → Xcode device export succeeded headlessly on 2026-10-06 (0 errors, 1 warning, IL2CPP, 114 s). The Xcode compile/sign/install step has not run yet: the startup disk had only ~2.5 GB free, too little for Xcode intermediates. To do: free disk space, export again (Ravenholm > Build > iOS Xcode Project (Device)), open `Builds/iOS-Device/Unity-iPhone.xcodeproj`, pick a signing team, then Run on the iPhone.

## Playtest feedback

- 2026-10-06: Phase 1 approved by the user ("proceed"). No playtest notes given yet; the Phase 1 tuning questions (joystick comfort, camera, pickup cadence, capacity, walk speed, trip length, interior) carry over.
- Phases 2 and 3: awaiting the user's playtest (both implemented under the overnight authorization below).
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
