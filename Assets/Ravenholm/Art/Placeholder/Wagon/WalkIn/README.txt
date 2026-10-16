RAVENHOLM — APPROVED WAGON, WALK-IN ASSET PACK

WHAT CHANGED
Both baked-in people were removed using built-in image_gen. Hidden counter, wall, floor and wheel artwork was reconstructed. The cleaned empty master was then divided into seven registered layers. These seven layers recompose pixel-for-pixel to Source/empty-master.png.
This is the open, parked view. It preserves the approved v5 design and perspective.

WHAT IS INCLUDED
Sprites/cart_back.png — far wall, chest, visible interior floor and rear details.
Sprites/cart_front.png — counter, front body, doorway/frame occluders.
Sprites/cart_roof.png — canopy, scallops, rolled curtain, upper roof supports.
Sprites/cart_steps.png — visible steps.
Sprites/cart_shafts.png — visible hitch shafts.
Sprites/wheel_near_left.png, wheel_near_right.png — visible parked wheel layers.
Sprites/cart_assembled.png — empty visual reference only; do NOT add it alongside the seven layers.
points.json — newly authored visual/ground coordinates, walk area, entrance and serving locations.
assembly.json — shared canvas, pivot and layer order.
Previews/ — character entry GIF/MP4, stills, anchors and layer sheet.
Unity/ — an optional prefab builder and a raised-floor sampling component.
Source/ — the empty master, original generation and prompt.

SPRITE IMPORT
Every layered PNG is 1536 x 1024 RGBA on the SAME canvas. Never auto-trim or recenter individual layers.
Pixels per unit: 256.
Shared pixel pivot: (779,826), with image coordinates measured from top-left.
Unity normalized pivot: (779/1536, 1-826/1024) = (0.5071614583, 0.193359375).
Use Sprite / Single, Full Rect mesh, no mipmaps, uncompressed alpha, Clamp, and Bilinear filtering.
Use a maximum texture size of at least 2048.
Do not add the game's outline separately to every internal cut: that draws false seams. Use the project's combined/exterior outline approach.

DRAW ORDER
Use the same sorting layer for cart and visiting actor. Orders below are relative to the cart's world-depth base:
back 0, shafts 1, steps 2, both wheels 3, actor inside 10, front 20, actor on lower steps/outside 40, roof 50.
The preview switches the stair actor behind the frame near the upper stair section.
Restore ordinary world sorting when the actor leaves; do not leave their order fixed at 10.
The visible cutouts are intended to stay registered. Do not fade/remove the roof or rotate the wheel cutouts independently: concealed background artwork has not been reconstructed for those effects.

HOW WALKING INSIDE WORKS
The character has a physics/ground root and a separate visual rig. Keep movement/collisions in the ground plane. Raise only the visual rig on the steps and floor.
points.json floor_polygon and serve_spot are VISUAL foot coordinates on the raised floor.
Add floor_height_px (150) to their image y to obtain the corresponding GROUND foot coordinates.
Steps bottom: visual/ground (330,918).
Steps top / door: visual (460,662), ground (460,812).
Serving: visual (840,710), ground (840,860).
Ramp the visual elevation from 0 to 150px (0.5859375 world units at 256 PPU) on the stairs.
Do not move the physics root up by that amount as well.
Pixel to cart-local world: x=(px-779)/256, y=(826-py)/256.
The floor perimeter must leave the left doorway open. A single solid collider covering the entire cart will prevent entry.
The preview uses an existing game character assembled at 274px standing height. Preview people are not baked into or included in any cart sprite.

UNITY HELPER
Copy this complete pack under a chosen Assets folder in Unity, retaining the Unity/Editor subfolder.
Select the pack's root folder and choose Assets > Ravenholm > Build Walk-in Cart Prefab.
The builder imports the seven sprites, gives them a shared pivot, creates a prefab, adds entrance/floor triggers, perimeter edges with a doorway gap, and named anchors.
CartWalkInSurface.TrySample returns visual elevation and relative actor order for a given world ground-foot position.
Claude still needs to connect that method to the existing movement and character-rendering controller, assign the project's blocker layer/collision masks, and apply a consistent world-depth sorting base.
The generated perimeter edges are starter integration geometry; test them with the real player's collider radius.
Keep cart transform at unit scale for the supplied floorHeight convention, or adapt the visual offset for the parent's scale.

PROJECT INTEGRATION
Current ShopView.RebuildWagon in Assets/Ravenholm/Scripts/Presentation/World/ShopView.cs creates one SpriteRenderer plus a solid WagonBlocker. Replace that drawing with the layered prefab and replace the solid blocker with the doorway-aware boundary.
Keep the Store counter interaction, bell interaction, customer receiver and existing upgrade behavior; attach them to the matching new anchors.
Use ServeGround for navigation and ServeVisual for rendered placement. Move the customer receiver to CustomerGround.
Do not use the old v1 points.json or mix this pack with earlier wagon geometry.
The approved artwork is not a mathematically exact 2:1 isometric diamond. The authored polygon follows this picture; do not force the old rectangular tile footprint onto it or stretch the art.

VALIDATION / LIMITS
Validated: common dimensions, alpha, exactly registered recomposition, route points, layer ordering with a separately drawn character, and stair/door/serving previews.
The helper scripts have NOT been compiled or run in the live Unity project. This delivery does not itself wire the existing player controller or replace the game's assets.
Test physics, world sorting, input movement, outlines and counter interactions in Unity before shipping.
One parked angle only. No closed travelling version, ox, complete spinning-wheel sprites, alternate directions or roof-fade background plate are supplied.

