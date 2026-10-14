RAVENHOLM — GROUND AND BLOCKS

29 production sprites: Ground/ contains 12 PNGs (256x152), Blocks/ contains 17 PNGs (256x260). Exact requested basenames. contact_sheet.png shows each ground type in a 4x4 patch, each block in a 3x3x2 stack, and the approved character reference at the same scale. A mixed-ground panel is included.

IMPORT
Replace the matching PNG files under Painted/Ground and Painted/Blocks. Preserve existing Unity .meta files / GUIDs. Use 256 pixels per unit and disable the game outline. Block pivot (0.5, 0.7519230769230769); ground pivot (0.5, 0.575657894736842). Both correspond to image coordinate (128,64.5). Do not auto-trim. Point filtering, no compression and no mipmaps are suitable for inspecting pixel seams; retain project settings if runtime requires otherwise.

GEOMETRY
Alpha masks copied exactly from the supplied PNG templates, including their inclusive boundary pixels and transparent bottom rows. No added padding. Grid step (128,64) or (-128,64) pixels; stack step 128px vertically. Ground lip 24px. Red anchor markers removed.

STYLE
Flat limited palettes; no outlines, gradients or texture grain. Shared RGB face multipliers top 1.00 / left 0.82 / right 0.66. Topsoil sides on block_grass; ground grass/forest/moss use soil-coloured lips. Plain grass and path have no markings. Rock base values become darker and cooler down the supplied sequence.

TILING
At least 8 image pixels inside every diamond edge contain only the respective base colour. Repeats and same-material variants meet without a line. Different materials intentionally meet at a colour boundary; these are not blended transition tiles. 288 ordered material/edge checks and 4x4 coverage validation passed.

The reference character in the contact sheet retains its existing game outline; none is painted onto the new terrain assets. No game project files or runtime settings were modified.

Built-in image_gen was used for the artwork. The complete prompt set, exact import settings and machine-readable validation are included.
