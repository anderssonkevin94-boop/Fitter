# Ravenholm Supply Co (working title)

iOS-first, portrait fantasy supply-company management game in Unity.
Design authority: *Ravenholm Supply Co Unity GDD* (design baseline 1.0, 6 Oct 2026).

**Current state: Phase 1 checkpoint (town movement and physical interaction). Phase 0 approved 2026-10-06. Awaiting Phase 1 playtest approval; Phase 2 has not started.**

## Open and run

1. Unity Hub → **Add project from disk** → `~/Desktop/RavenholmSupplyCo` (Unity **6000.4.3f1**).
2. Open `Assets/Ravenholm/Scenes/Bootstrap.unity` (or `DebugSandbox.unity`; it routes through Bootstrap automatically).
3. Game view: switch to **Simulator** and pick an iPhone (e.g. iPhone 16 Pro), then an older 16:9 iPhone (SE) to compare.
4. Press **Play**.

Controls: touch and drag in the lower ~70 % of the screen for the floating joystick (mouse drag in the editor), or WASD/arrows.
Tap things to inspect them. The round button bottom-right acts on the nearest thing ("Open Smelter", "Inspect …"). The gear (top right) opens the debug panel.

Phase 1 loop: walk next to a pile or output pad and goods jump onto your stack automatically (capacity 12). Then stand on a receiver marker:
ore, coal and gems go to the Smelter; logs to the Lumbermill; herbs to the Apothecary; finished weapons, armor and potions to the Store's receiving strip (walk in through the open front).
Debug panel: refill piles/pads, unload carry, unstuck, ownership audit.

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
