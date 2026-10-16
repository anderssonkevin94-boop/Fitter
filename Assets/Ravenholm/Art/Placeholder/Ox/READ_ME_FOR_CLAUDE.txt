RAVENHOLM FURRY OX — SCALE REVISION V12

Whole-view scale pass using SW/SE as the reference: NW/NE 1.15x, N 1.08x, E/W 0.98x, S and SW/SE 1.00x relative to V11. Each direction's complete rig, pivots, hoof points and animation offsets share exactly one uniform factor about its existing ground anchor. No independent part scaling, redrawing, palette changes, or runtime stretching. Angles and 1.2-second timing are preserved. See scale_adjustments.json.

Use the PNGs, points.json and animation.json from this pack together. Ground anchors are retained from V11; use them at the same world position for direction changes. Do not independently auto-fit sprite bounds, and do not reapply the documented factors at runtime: they are already baked into the art and metadata. Keep one pixels-per-unit setting for every direction.

The eight-view and NW/NE preview GIFs use a common 1536-square staging area, the same ground point, and a fixed-size reference diamond behind every ox. Preview staging is only for comparison. All delivered part/frame PNGs remain 1280 x 1280 with transparency; the ground marker is not baked into them.

This is a moderate perceptual scale correction. The views remain hand-drawn approximations, with different projected silhouettes; this pass does not claim calibrated 3D anatomical consistency. It is supplied for visual review and is not integrated into the game.

NW/NE preserve V11's original V9 artwork proportions. Their upper haunches remain on the body, while overlapping lower-leg pieces move behind it. This is why their leg pivots must not be replaced with earlier hip/shoulder anchors.

CONTENTS
Parts/{N,NE,E,SE,S,SW,W,NW}/: 6 transparent PNGs per direction (48 total).
Each direction has body, head, leg_front_near, leg_front_far, leg_hind_near, leg_hind_far.
Standing/: 8 assembled transparent rest poses.
WalkFrames/{direction}/: 6 assembled transparent poses per direction (48 total).
Previews/: looping GIF/MP4, standing turnaround, per-direction part sheets and six-frame strips.
points.json: exact pixel anchors, feet, bounds, ground point and draw order.
animation.json: the 1.2 second rigid-part loop, sampled at 24 poses, 50 ms apart.
validation.json: technical check results and their scope.
prompts.json: built-in image generation prompts used for the new artwork.

IMPORT AND ASSEMBLY
All part, rest and animation-frame PNGs are 1280 x 1280 with binary transparency. Retain the full canvas; do not independently trim/recenter each piece. Use point sampling and disable texture compression while checking the art. Keep the game's intended world scale; this pack does not prescribe a new pixels-per-unit value.

Coordinates use top-left pixels, x right and y down. In points.json, order is back-to-front, and varies by direction. Follow it exactly: NW/NE lower legs are behind the body and the separate head is above it; other directions retain their existing orders. Do not apply one common layer order to every direction. Near/far are part identifiers; use the supplied per-direction geometry and order rather than inferring the order from those names.

All parts share origin (0,0). To place the assembled ox at a world ground point, offset the entire rig using directions[direction].ground, the average of its four rest hoof points. These ground anchors differ between views and prevent the ox jumping when changing facing. For a sprite pivot in Unity, convert a pixel anchor to (x/1280, 1-y/1280). Do not rotate Unity's 2D camera to imitate the drawn elevation.

RUNTIME ANIMATION OPTION
For each part, rotate about its supplied pivot, then add the supplied offset. Angles are radians clockwise in image coordinates. Account for y-axis/sign conversion in Unity. Scale remains (1,1) throughout; no stretching or shearing. Interpolate consecutive poses, including the last-to-first transition. Frame 1 is time zero; the six PNG poses correspond to 0, 200, 400, 600, 800, 1000 ms.

Alternatively, play the six assembled WalkFrames at 5 fps for the same 1.2 second loop. Do not apply the rig motion a second time to those already assembled frames.

MIRRORS
E is the exact horizontal mirror of W; NE of NW; SE of SW. Both PNGs and mirrored metadata are included. Do not mirror them again at import. A mirrored part keeps its same logical limb name, pivot and phase after the recorded reflection.

VALIDATION LIMITS
Dimensions, hard alpha, margins, palette, mirror symmetry and attachment-root overlap are checked. Attachment overlap is a local geometric test; it is not proof of correct anatomy or an art-approval decision. The previews show the actual supplied parts and animation.
