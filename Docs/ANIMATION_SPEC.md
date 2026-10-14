# Supplier animation spec (draft for Kevin's sign-off, 2026-10-14)

This is the reference the walk is built and tested against. Every number below is checked by an automated test, unless it is marked as reviewed by eye.

## 1. Space

- **Motion is defined in ground space and projected once.** All motion (feet, hips, bob, arms) is computed in ground units: x east, y north, z up.
- **Projection.** The screen shows the ground with y halved: screen = (x, y × 0.5 + z).
- **Consequence.** A step is the same length on the ground in every direction. Walking up or down the screen, it looks half as long, which is correct.
- **Cadence is per ground distance.** Steps per second are the same in every direction at the same ground speed.

## 2. Body (calibrated, see `Captures/rig_calibration.png`)

| | Value |
|---|---|
| Height | 1.07 units (365 px at 340 px/unit) |
| Leg, hip to ankle | 0.17 units (a quarter of his height is below the apron) |
| Shoulder joints | under the top of each sleeve (`rig_calibration.json`) |
| Resting wrists | straight below the shoulders, at the apron's hem |

## 3. Walk

| | Value | Test |
|---|---|---|
| Ground speed at full stick | 2.0 units/s (1.9 body heights/s) | the existing speed test |
| Cadence at full speed | **3.4 steps/s in all 8 directions** | equal to within 5% |
| Cadence vs speed | scales with speed (a half push gives about 1.7 steps/s) | ✓ |
| Foot reach ahead of or behind the hip | at most ±0.12 ground units | ✓ |
| Leg stretch | at most 1.3 × rest length | ✓ |
| Supporting foot | planted for the first part of the stance, then glides the rest (Kevin's choice: glide rather than over-long legs) | glide ≤ 50% of the ground covered |
| Body bob | 2 per cycle (one per step), 0.02 units: lowest just after heel strike, highest at passing | ✓ |
| Squash and stretch | ±2.5% height, in step with the bob | by eye |
| Weight shift | 0.02 units toward the supporting foot | by eye |
| Lean | 5° forward in the side views; 3° side-to-side tilt head-on | by eye |
| Foot roll | toe up 16° at heel strike, heel up at push-off (side views only) | by eye |
| Arm swing | opposite the legs, ±0.1 ground units forward and back, through a spring (3.2 Hz, damping 0.55) | swings opposite the same-side leg |
| Start and stop | 0.1 s to full speed; the walk eases in over about 0.13 s and settles to standing | by eye |

**Why the feet must glide.** He covers ground fast for his size: 1.9 body heights a second, where a person walking covers about 0.8. With 0.17-unit legs, fully planted feet would need about 6 steps a second (a scurry) or legs stretched to about 2× (they look too long). The spec takes 3.4 steps a second with a short reach, and the feet glide the difference.

**The alternative.** A slower walk (1.4 units/s) would plant better and look calmer. It is Kevin's call; one number changes it.

## 4. Hands

| Situation | Sprite | Thumb |
|---|---|---|
| Relaxed, front (S) and back (N) | `hand_open_side` (thumb edge) | toward his centre line, both hands |
| Relaxed, side and diagonal views | `hand_open_back` (back of the hand) | the way he faces |
| Gripping a tool | `hand_grip_*` | the right hand as painted; the left hand mirrored |

Tested per view.

## 5. Draw order (inside his sorting group, back to front)

- **Front-facing views:** far-side limbs → body → near-side limbs.
- **Head-on:** both arms are drawn over the body's edges (the sleeves are part of the arms).
- **Pack and pile:** behind the body, except when his back faces the camera (N, NE, NW).
- **Tools:** follow the tool's real depth.

## 6. Review before install

For every change:
- a contact sheet of all 8 directions at the key poses (contact, down, passing, up);
- a GIF at real speed next to the previous version.

Nothing goes to the phone without passing the tests above.
