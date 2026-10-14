RAVENHOLM — V4 TERRAIN, NATURE AND BUILDINGS

55 sprites: Ground 12, Blocks 17, Nature 17, Buildings 9.

Ground and Blocks retain V3 canvases, alpha masks, pattern geometry and names. Top colours have approximately 1.25x HSV saturation and 1.08x HSV value, with explicit grass/path overrides. All side colours are derived using 1.00 / 0.82 / 0.66. Grass base #9DBA5A; path #DDBF85. Nature foliage uses exactly #2D6747, #5B9347, #92B342, with trunks #976432 / #6E4E30 and optional highlight.

All nature/building canvases are exactly twice their current source dimensions. All have 12px or more fully transparent margin, binary alpha, no baked drop shadow, and fixed flat palettes. Objects were redrawn from their individual source references. Building source content bounds and pivots guide export placement; the new illustrations preserve the major layout and footprint.

IMPORT
Use 256 PPU for this entire pack. Old nature/building sprites were 128 PPU, so double their PPU when replacing them with these doubled canvases to retain world size. Terrain was already 256 PPU and remains so. Preserve Unity .meta GUIDs, then update their size/pivot settings using points.json. Ground and blocks receive NO outline; nature and buildings receive the game's runtime outline only.

points.json gives top-left pixel ground points and bottom-left normalized Unity pivots. Building anchors retain their original footprint-centre pivots. Nature anchors use the visible trunk/object ground contact; use these updated pivots. Do not auto-trim.

contact_sheet.png places every new object and the approved character on the actual recoloured ground. asset_catalog.png labels all 26 objects. ground_comparison.png compares V3 and V4. validation.json checks inventory, geometry, dimensions, margins and transparency. The prompt set records built-in image_gen artwork and export processing.

Source: /Users/kevinandersson/Desktop/RavenholmSupplyCo/Docs/astra-refs/nature/
No live game files or Unity import settings have been modified.
