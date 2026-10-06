# Ravenholm Supply Co (working title)

iOS-first, portrait fantasy supply-company management game in Unity.
Design authority: *Ravenholm Supply Co Unity GDD* (design baseline 1.0, 6 Oct 2026).

**Current state: Phase 9 checkpoint (balance pass, release hardening, release docs). Version 0.9.0. Phases 0–1 approved; Phases 2–9 built under the user's authorization to keep going. Awaiting the user's sign-off of the playable build. Nothing has been submitted anywhere.**

## Open and run

1. Unity Hub → **Add project from disk** → `~/Desktop/RavenholmSupplyCo` (Unity **6000.4.3f1**).
2. Open `Assets/Ravenholm/Scenes/Bootstrap.unity` (or `DebugSandbox.unity`; it routes through Bootstrap automatically).
3. Game view: switch to **Simulator** and pick an iPhone (e.g. iPhone 16 Pro), then an older 16:9 iPhone (SE) to compare.
4. Press **Play**.

Controls: touch and drag in the lower ~70 % of the screen for the floating joystick (mouse drag in the editor), or WASD/arrows.
Tap things to inspect them. The round button bottom-right acts on the nearest thing ("Open Smelter", "Inspect …"). The gear (top right) opens the debug panel.

Phase 1 loop: walk next to a pile or output pad and goods jump onto your stack automatically (capacity 12). Then stand on a receiver marker:
ore, coal and gems go to the Smelter; logs to the Lumbermill; herbs to the Apothecary; finished weapons, armor and potions to the Store's receiving strip (walk in through the open front).
Debug panel: refill piles/pads, unload carry, unstuck, ownership audit, site sim speed (×1/×5/×20), complete Mine section / Forest clearing, reset throughput.

Phase 2: Dagna mines and Tobin chops on their own and drop goods at the staging piles (capacity 96); you haul from there.
When a section or clearing is worked out, a "Choose next section" marker appears. Open the Mine or Forest (context button) to Continue Here or Dig/Push Deeper.
Tool and pack tiers can be swapped in the site panel (DEBUG, no cost yet) to compare throughput.

Phase 3: delivered ore and logs are processed automatically (Smelter, Lumbermill; open them to pick the recipe, reserve, see progress).
Open the Forge, Workbench or Apothecary to craft instantly from starter blueprints; the item appears on the station's output pad. Carry it to the Store's receiving strip; the racks fill, and "Open Store" lists everything (Weapons / Armor / Potions / All / Materials / Blueprints).
Debug panel adds: grant the test blueprint (Copper ward staff), add test materials, recent transactions.

Phase 4: open the Job Board (board button top right, or Store → Job Board), post a job, and wait ~10–30 s for applicants to gather at the Store.
Tap an applicant (or use their card on the board) to meet them, set their loadout, read the forecast, and Send. Expeditions stop at a TEST DEPARTURE until Phase 5.
Debug panel adds: return test departures (refund), set gold, set reputation.

Phase 5: expeditions now resolve when their timer ends (Roadside Patrol 90 s; use Site sim speed ×20 to wait less).
A toast announces the return; open the Job Board to read the result card (gold, materials, gear back or lost, injuries, reputation, new knowledge, why).
Successes unlock new jobs on the map and first-success blueprints. Deaths are permanent and lose the carried gear.
Debug panel adds: force next outcome, finish expeditions now. The loadout shows a small DEBUG line with exact odds.

Phase 6: after your first ordinary quest returns, the Trader arrives at the Trader Pad a minute later. Visits last 5 minutes every 20 minutes; a toast announces each arrival. Buy materials (iron and cedar are always on offer) or sell Store goods cheaply.
Building panels show their next upgrade with its exact cost and prerequisite. The Mine and Forest panels show site equipment, crew expansion, and tool and pack purchases for each worker.
Close the app and come back later: sites and facilities keep working for up to 8 hours, expeditions finish, and a "While you were away" card sums it up.
Debug panel adds: simulate time away (+1 h / +8 h / +24 h), summon the Trader now.

Phase 7: the Job Board map is grouped into regions (Frontier Paths, Hollow March, Crownward Ruins). The next region shows as a rumour, and "Next for the company" points at the next main-chain job.
The Mine and Forest have depths and areas 4–6 (moonsilver, ironwood). Smelter 3, Lumbermill 3, Forge 3 and Workbench 2 unlock the advanced recipes that quests teach.
The finale is the Beacon Expedition. First chart the Beacon Road and hold the Beacon Pass (each succeeds once and permanently removes a threat). Then send a level 6 contractor at reputation 70 with Power 13+ weapon, Protection 10+ armor and a counter potion for the Pale Flame. Success puts a trophy in town; repeatable contracts continue afterwards.

Phase 8: the gear button opens Settings: music, effects, haptics, reduced motion, text size, joystick side and tips. Developer tools are inside Settings. In release builds, tap the version line in Settings five times to show them.
A tip card under the top bar walks through the first hour; "Got it" and "Hide tips" dismiss it, and Settings can show the tips again.
Buildings fly a pennant per upgrade level. Developer tools add "Record performance (60 s)", which writes a JSON report to the app's Perf folder.

## Reset the test save

Debug panel → **Reset sandbox save…** (tap twice to confirm). Only the `sandbox` profile is affected.
Files live in `Application.persistentDataPath/Saves/sandbox/` (the debug panel shows the exact folder).
Tests never touch it: they use throwaway temp folders.

## Project layout

```
Assets/Ravenholm/
  Scripts/Domain          engine-free rules: content IDs, clock, deterministic RNG, sandbox state
  Scripts/Infrastructure  engine-free saves: envelope DTO, checksummed file codec, atomic store, migrations
  Scripts/Content         ScriptableObject definitions + ContentCatalogue + validation
  Scripts/Application     plain C# services (namespace Ravenholm.Services): SaveCoordinator, SandboxSession, GameServices
  Scripts/Presentation    MonoBehaviours: GameRoot (composition root), input, world views, HUD
  Scripts/Editor          setup, scene builder, sprite importer, content validation, iOS build
  Content/                authored definitions and config assets (edit these in the Inspector)
  Art/Placeholder/        generated placeholder kit + PROVENANCE.md + sprite_meta.json
  Scenes/                 Bootstrap, DebugSandbox
  Tests/                  EditMode, PlayMode, Fixtures
Tools/PlaceholderArt/     generate_placeholders.py (regenerates the whole kit)
Docs/                     DECISIONS.md (decision log), ART_CONVENTIONS.md
```

## Editor menu (Ravenholm)

- **Validate Content**: checks IDs, duplicates, missing references, item field consistency. Also runs before every build and fails it.
- **Setup → Run Phase 0 Setup**: idempotent; creates only missing assets and never overwrites Inspector edits.
- **Setup → Rebuild Sandbox Scenes**: regenerates Bootstrap/DebugSandbox (overwrites manual edits to those two scenes).
- **Build → iOS Xcode Project (Device / Simulator)**: exports to `Builds/` (git-ignored).

## Headless commands (editor must be closed for this project)

```bash
U="/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity"
"$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults /tmp/rsc-editmode.xml
"$U" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults /tmp/rsc-playmode.xml   # writes Captures/*.png
"$U" -batchmode -nographics -projectPath "$PWD" -buildTarget iOS -executeMethod Ravenholm.EditorTools.BuildTools.BuildIosFromCommandLine -rsc-sdk=device -rsc-dev
```

Regenerate placeholder art: `python3 Tools/PlaceholderArt/generate_placeholders.py`, then **Run Phase 0 Setup**.
