DWARF — EIGHT-DIRECTION 2D WALK WITH GENTLE SWING

START HERE
Open preview.html to see all directions, pause, step through frames, switch
backgrounds, test movement over a floor, and switch between walking and standing.
The preview reads the delivered PNG atlas directly. It works offline after extraction.

GAME ASSETS
  dwarf-walk.png       1280 x 2048 RGBA, five columns and eight rows
  dwarf-walk.json      Frame rectangles, durations, and named animation tags
  sprite-data.json     Common origin and movement speed for each direction
  dwarf-idle.png       2048 x 256 RGBA, eight matching standing poses
  frames/             40 individual transparent 256 x 256 PNGs
  sheets/             One 1280 x 256 transparent walk strip per direction
  standing/           One transparent standing PNG per direction
  animations/         GIF previews and transparent, lossless animated WebP loops

DIRECTION ORDER
N, NE, E, SE, S, SW, W, NW. This is the row order in the walk atlas and
column order in the standing atlas. Frame indices run 0 through 4.

PLAYBACK AND POSITION
Each frame lasts 90 ms. Repeat all five frames: the cycle lasts 450 ms.
This is one complete left/right cycle, with two alternating steps per cycle.
There is no duplicated first frame at the end.
Every cell is 256 x 256. The common ground origin is (128, 222), measured
from the top-left. To draw at a world position (x, y), draw the cell at
(x - 128, y - 222). Use this origin for every direction and for standing.

The frames are animated in place. sprite-data.json supplies the projected
movement per cycle and per second for the longer stride. Use those
values if you want ground movement to match the planted feet. Changing playback
speed should change movement speed by the same factor. These are screen-space
vectors for the elevated camera; north/south movement projects shorter than east/west.
At the exported 256 px size, E/W movement is 148.148 px/s and N/S is 74.074 px/s.
Scale these speeds with the sprite size. The stride is twice the previous shuffle.
The exported five poses lock planted feet at frame boundaries; with continuous
root movement, a held sprite frame can still show motion between those boundaries.

EDITABLE 2D RIG
  rig-parts/          Aligned 512 x 512 torso, left/right arms and legs
  cropped-parts/      The same parts cropped to their bounds
  rig.json            Cropped-part offsets, measured shoulder pivots and floor points
  motion.json         The five poses: rigid leg translations and arm rotations
  master-frames/      512 x 512 lossless rendered frames
  source/             Standing drawings, hidden torso paint and complete leg drawings

Left and right in part names mean the character's anatomical side. In the E/W
profile the far arm is fully occluded, but it is supplied as an editable part.
W is a mirror of E, NW of NE, and SW of SE. This mirrors the costume accessories too.

The leg drawings include clean hidden upper thighs. Their shape stays rigid
during animation: they translate without bending, stretching or mesh warping.
Arms use their matching facing and remain attached at the measured shoulder.
The torso shifts gently toward each supporting leg, with a slight lean.
The arms swing at their shoulder pivots and move with the torso.
Feet, stride length, movement speeds and the 450 ms cycle are unchanged.

REBUILD
Python 3, Pillow and NumPy are needed only to edit or rebuild the rig.
Run these scripts in order:
  extract_parts.py
  render.py
  build_preview.py
  validate.py
The PNGs and browser preview themselves do not need Python or any dependencies.

VERIFICATION
checks/validation.json records format, alpha, frame bounds, connected silhouettes,
atlas/individual-PNG equality, timing, mirroring and planted-foot movement checks.
checks/visual-review.json records the inspected atlas version. The enclosed
transparent gaps flagged in NE/NW are the space between the legs, below the hips.
They were inspected at full resolution; they are not missing shoulder or hip paint.
SHA256SUMS.json identifies the exact files included in this package.
