# Dwarf v5: the approved redesign (2026-10-14)

Astra's approved supplier: orange beanie, blue shirt, cream apron. This is her rig with separate thigh, shin and foot pieces and two-bone knee bends. Her import notes are in `CLAUDE_IMPORT.txt` and `README.txt`; her original walk sheet (stride 40) is `astra-dwarf-walk-stride40.png`.

**The game's walk is her rig re-rendered with a longer stride.** At stride 40, planted feet move only about 0.6 units/s, and Kevin found that "too slow with too many steps".

- `render_knee.py` is her `render.py`; the only change is that `STRIDE` and `LIFT` come from the environment.
- The game uses `STRIDE=64 LIFT=20`, so the feet travel 1.6× hers. Run it in a copy of her package folder.
- At stride 80 the knees reach full extension and the body sinks about 16 px, so 64 is the limit for her leg lengths.
- The standing poses (`dwarf-idle.png`) are hers, unchanged.
