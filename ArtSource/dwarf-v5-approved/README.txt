DWARF SUPPLIER — APPROVED DESIGN

Eight directions: N, NE, E, SE, S, SW, W, NW.
Five walking frames per direction, 90 ms each, 450 ms per loop.
Transparent PNG assets, matching standing poses, editable leg parts.

START HERE
Open preview.html to see all directions and test direction changes.
Give CLAUDE_IMPORT.txt and this complete folder to your game developer.

FILES
• dwarf-walk.png: 1280 x 2048 atlas; five columns, eight rows.
• dwarf-walk.json: frame rectangles and animation tags.
• sprite-data.json: timing, direction order, ground origin and movement.
• dwarf-idle.png: eight 256 x 256 standing cells in one row.
• frames/, sheets/, standing/: individual PNG alternatives.
• animations/: looping GIF and transparent animated WebP per direction.
• eight-directions.png: standing overview.
• parts-preview.png: separated parts overview.
• rig-parts/: seven full-canvas 512 x 512 pieces per direction.
• cropped-parts/: the same 56 pieces, cropped; offsets are in rig.json.
• rig.json: rest joints, offsets, direction and draw order.
• motion.json: five posed frames per direction in 512-pixel coordinates.
• source/: approved design and new back/leg artwork.
• checks/: contact sheets and validation records.

PARTS
Each direction has one upper body, two thighs, two shins and two feet.
The upper body includes the head, arms and hands; these are not separate
arm rigs. Keeping the approved shoulders intact avoids reattaching them
at incorrect angles. A small whole-body sway accompanies the steps.
Side and diagonal legs bend through rigid rotations. Front/back views
use overlapping rigid pieces to represent knee flexion into depth.
Feet stay level. There is no animated mesh warping or stretching.
W mirrors E, NW mirrors NE, and SW mirrors SE, including hat details.

CHECKS
All 40 frames were reviewed on light and dark backgrounds. Automated
checks cover alpha, clipping, silhouette continuity, atlas equality,
loop timing, mirrors, joint reach and planted-foot frame positions.
Five held frames remain deliberately stepped animation. Matching root
movement is necessary for foot planting; arbitrary game speeds will slide.

REBUILD (Python 3, Pillow and NumPy)
Run prepare.py, extract_parts.py, render.py, build_preview.py, validate.py.
Inspect changed output before refreshing checks/visual-review.json.
Then run package.py. Packaging checks the review hash and verifies every
archive member against SHA256SUMS.json after extraction.
