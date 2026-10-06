# Art conventions (placeholder kit and future replacements)

- **Pixels per unit:** 128. One world unit ≈ the width of one ground tile.
- **Projection:** 2:1 isometric drawing; a ground tile diamond is 128 × 64 px (1 × 0.5 units).
- **Pivots** (stored in `Art/Placeholder/sprite_meta.json`, applied by `PlaceholderSpriteImporter`):
  - Buildings and props with a footprint: centre of the ground footprint diamond. For square footprints this sorts correctly against anything outside the footprint.
  - Characters, trees, rocks, piles, racks: feet / base centre.
  - Tiles: diamond centre (skirt hangs below).
  - Icons and UI: centre. UI panels carry 9-slice borders.
- **Sorting:** sorting layers Ground < Default < Overlay. SpriteRenderers use *Sprite Sort Point = Pivot*. Lower Y draws in front (URP 2D custom axis 0,1,0). Multi-part objects (characters, bell) use a SortingGroup on the root, so their parts sort as one.
- **Scale reference:** a character is about 1 unit tall. The Store footprint is 3 × 3 tiles. Small buildings are 1.5 to 2.5 tiles.
- **Style:** warm dark-brown outline (~3 px), chunky silhouettes, warm stone, painted timber, restrained saturated accents. Each resource differs by silhouette, not just colour.
- **No logic in file names.** IDs, collision and timing live in definitions and scenes. Swap art by changing the sprite referenced in a definition, `UiTheme`, or a scene object.
- **Provenance:** every placeholder is original procedural art (see `Art/Placeholder/PROVENANCE.md`). Record the source and licence of any future third-party asset in the same way.
