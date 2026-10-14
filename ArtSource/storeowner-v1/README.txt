Ravenholm — General Store Owner, Phase 2

26 sprite parts + preview.png + points.json.
Preview order, left to right: S, SE, E, NE, N. Each view occupies a 512x512 cell.

All coordinates are top-left origin, zero-based pixel centres; LEFT and RIGHT mean the character's own left and right. Every boot sole has its lowest opaque pixels at y=444. Body spans y=80..352; the 92px below the apron are reserved for procedural legs and boots.

Use one pixels-per-unit value for ALL sprites, including the 256x256 hand canvases. Do not size sprites by canvas width. Body and boot canvases share the ground pivot (256,444). Convert a pixel anchor to Unity normalized bottom-left coordinates using ((x+0.5)/width, 1-(y+0.5)/height).

Hands: wrist anchor is (128,128) for every pose. Align that anchor to the arm endpoint. Front means palm view; back means dorsal view; side means thumb-edge profile. Grip coordinates place a tool behind the hand. For the opposite hand, mirror the image AND its anchors with x'=width-1-x.

Draw legs and boots behind the apron. Draw arms behind the body so sleeves cover their roots; place hands at wrist endpoints. In profile, the far limb can be hidden by the near limb and torso. The standing preview uses the exact points.json wrist targets with straight, constant-width tubes.

SE/E/NE can be mirrored to SW/W/NW. The original reference's mislabeled forward-looking NE was corrected to a rear three-quarter view. No decoration with meaningful asymmetry was added.

Palette was sampled from the supplied original JPEG and reduced to solid flat colours with supporting shadow tones. Every final alpha pixel is 0 or 255. No outlines are baked in. The game supplies its own outline.

validation.json records dimensions, bounds, margins, colours and anchors for every piece. generation-prompts.json records the built-in image generation prompts.
