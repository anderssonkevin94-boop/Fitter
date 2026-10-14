# Dwarf v6: the approved dwarf, legs swinging from the hip (2026-10-14, Kevin)

Kevin's feedback on v5: the walk looked awkward again, especially from the front, where the gap between the legs was wide.

`render_hip.py` is Astra's `render.py` (in `../dwarf-v5-approved`) with a new `pose()` that uses her separate thigh, shin and foot parts.

- **Hip swing.** Each leg swings as one piece from its hip, with the planted foot held on the floor. The hips ride on the supporting leg.
- **Knee bend.** The swinging leg folds at the knee in profile (`KNEE` 30° at the top of the swing). Toward or away from the camera, the shin foreshortens instead.
- **A narrower walking track** (`NARROW` 0.35). In the front and back views each leg is moved 35% of the way toward the middle, so he no longer straddles.
- **Unchanged.** The stride (`STRIDE=64 LIFT=20`), speeds and standing poses are as in v5.

Run it in a copy of her package folder: `STRIDE=64 LIFT=20 python3 render_hip.py`.
