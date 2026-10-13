# Dwarf walk v4: the legs swing from the hip (2026-10-13, Kevin)

In v3 the rigid legs slid back and forth. Here each leg rotates about a pivot at its hip. The image is Astra's leg part, from her rig.

- The foot travels the same stride as v3 (`STRIDE=80`), so the planted-feet speeds in `sprite-data.json` are unchanged.
- The planted foot stays on its floor point while the leg rotates.
- In profile the leg tilts in the screen plane. Facing toward or away from the camera, it foreshortens: the forward part of the swing projects as half its length up or down the screen.
- The hips ride on the supporting leg: they are highest over a vertical leg and dip at the ends of the stride.
- The swinging leg shortens by 12% at mid-swing (a small knee bend) so the boot clears the floor.
- Arms swing at the shoulder as before, and the torso keeps her sway.

`render_swing.py` is her `render.py` with a new `pose()`. Run it in a copy of her package folder with `STRIDE=80 LIFT=18 ARM=1.8`. `motion.json` records each leg's hip pivot and angle per frame.
