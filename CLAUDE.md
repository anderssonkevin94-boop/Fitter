# Ravenholm Supply Co (working title)

- Design authority: the *Ravenholm Supply Co Unity GDD* (Kevin's copy: `~/Documents/Codex/2026-10-06/referenced-chatgpt-conversation-this-is-an/outputs/Ravenholm_Supply_Co_Unity_GDD.docx`). Read it before gameplay work. Never reopen LOCKED mechanics without asking.
- **Phase gate:** work one GDD phase at a time. Each phase ends at a playable checkpoint; stop and wait for Kevin's explicit approval. Silence or passing tests is not approval. Current status is in `Docs/DECISIONS.md`.
- Unity 6000.4.3f1, URP 2D. Editor binary: `/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity`. Batch mode cannot open this project while an editor has it open.
- Layers: Domain/Infrastructure are engine-free; Application layer namespace is `Ravenholm.Services`; UI submits commands and never mutates state; one owner per field; never mutate ScriptableObjects at runtime; saves store IDs, not assets.
- Tunables and content live in ScriptableObjects under `Assets/Ravenholm/Content/`. No item/quest-name branches in code.
- Verify before claiming: run EditMode and PlayMode tests headless (commands in README). PlayMode writes visual captures to `Captures/`.
- Record version pins, defaults and deviations in `Docs/DECISIONS.md`. Commit verified milestones on `main` with small commits.
