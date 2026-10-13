# Dwarf walk v3: the longer stride (2026-10-13)

This is Astra's v2 swing walk (`../dwarf-v2-swing`), re-rendered from her own cutout rig with a longer stride. Kevin found v2 too slow, with too many steps.

- `render_v.py` is her `render.py` made tunable. The only changes: `STRIDE`, `LIFT` and the arm-swing factor `ARM` come from the environment.
- This render used `STRIDE=80 LIFT=18 ARM=1.8`. Hers was 40 / 12 / 1.
- Run it in a copy of her package folder (it reads `parts/` and `extraction.json`, and writes next to itself).
- The standing poses (`dwarf-idle.png`) are hers, unchanged.
- The planted-feet speed doubles with the stride: 296 px/s sideways and 148 up or down, at her 90 ms frames.
