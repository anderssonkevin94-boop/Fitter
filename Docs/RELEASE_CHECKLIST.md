# Release checklist (0.9.0 line)

Submitting to the App Store or releasing publicly needs the user's explicit authorization and access to their Apple account. Nothing here has been submitted.

## Build
- [x] Release (non-development) iOS build exports and signs (0 errors).
- [x] Bundle id `com.kevinandersson.ravenholmsupplyco`, version 0.9.0 (9), iPhone only, iOS 15.0+.
- [ ] Final title and app icon (the title is still a working title, GDD 29).
- [ ] Final art (Phase 8 open decision) and audio assets (currently synthesized placeholders).
- [ ] Privacy policy and App Store privacy answers (the game collects nothing; confirm).
- [ ] Store listing, screenshots, age rating.

## Quality gates (automated)
- [x] EditMode 183/183 and PlayMode 26/26 pass.
- [x] Content validation: all content reachable, no rare-drop walls (`ProgressionGraph`).
- [x] Balance harness rules hold (`BalanceTests`, `Docs/BALANCE_REPORT.md`).
- [x] Campaign bot finishes from a fresh save.
- [x] Soak test: no duplication or loss across crashes and time jumps.
- [x] Every historical save version (v1–v8) loads, runs and round-trips.
- [x] Usability: text at 130 %, touch targets, occlusion, haptics and reduced-motion toggles.

## Device checks (manual or semi-manual)
- [x] Forced-kill interruptions: save and backup valid (iPhone 16 Pro).
- [ ] PerfProbe report on the reference phone (60 fps target, 30 fps floor, catch-up near 1 s).
- [ ] 20-minute session for thermal behaviour.
- [ ] Suspend/resume during a quest return and during a Trader visit (unlocked phone).
- [ ] Lowest supported device: not available; see DEVICE_MATRIX.md.

## Sign-off
- [ ] User plays the build: fresh first hour, then several returns over multiple days (GDD 28 Phase 9).
