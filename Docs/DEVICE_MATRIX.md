# Device compatibility matrix

| Device | iOS | Build | Install/launch | Interruption test | Performance | Notes |
|---|---|---|---|---|---|---|
| iPhone 16 Pro (iPhone17,1) | user's phone | 0.9.0 (9) release | OK | OK (16 forced kills, saves valid) | pending (PerfProbe) | reference device |
| Oldest supported (iOS 15 class, e.g. iPhone 8/SE 2) | 15.x | — | not tested | not tested | not tested | no hardware available; set the floor after testing |

Minimum iOS is 15.0 (PlayerSettings). Treat it as provisional until a low-end device is tested (GDD 24: 30 fps floor on the lowest supported device).
