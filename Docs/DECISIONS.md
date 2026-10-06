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

## Open blockers

- **iOS build:** the Unity → Xcode device export succeeded headlessly on 2026-10-06 (0 errors, 1 warning, IL2CPP, 114 s). The Xcode compile/sign/install step has not run yet: the startup disk had only ~2.5 GB free, too little for Xcode intermediates. To do: free disk space, export again (Ravenholm > Build > iOS Xcode Project (Device)), open `Builds/iOS-Device/Unity-iPhone.xcodeproj`, pick a signing team, then Run on the iPhone.

## Playtest feedback

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
