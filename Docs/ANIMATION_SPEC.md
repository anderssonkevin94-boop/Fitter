# Supplier animation spec (2026-10-14; Kevin left the speed to Claude: 1.7 units/s)

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
| Shoulder joints | under the top of each sleeve; in the diagonals the far arm hangs from the torso's back edge (`rig_calibration.json`) |
| Resting wrists | just outside his outline at hand height, the arms angled out along the apron's flare (front, back and diagonal views); at his side in profile (tested) |
| Limb widths | arms 0.10 units at the shoulder tapering to 0.062 at the wrist; legs 0.11 to 0.07; sleeves 0.13 |

## 3. Walk

| | Value | Test |
|---|---|---|
| Ground speed at full stick | 2.04 units/s (`walkSpeed`; 1.7 until Kevin asked for +20%, 2026-10-15), the stick's direction taken on the ground | ✓ |
| Cadence at full speed | **3.7 steps/s in all 8 directions** | equal to within 5% |
| Cadence vs speed | scales with speed (a half push gives about 1.85 steps/s) | ✓ |
| Foot reach ahead of or behind the hip | at most ±0.16 ground units (`maxFootReach`) | ✓ |
| Leg stretch | at most 1.3 × rest length | ✓ |
| Supporting foot | planted for the first part of the stance, then glides the rest (Kevin's choice: glide rather than over-long legs) | glide ≤ 50% of the ground covered |
| Body bob | 2 per cycle (one per step), 0.02 units: lowest just after heel strike, highest at passing | ✓ |
| Squash and stretch | ±2.5% height, in step with the bob | by eye |
| Weight shift | 0.02 units toward the supporting foot | by eye |
| Lean | 5° forward in the side views; 3° side-to-side tilt head-on | by eye |
| Foot roll | heel-toe roll: the heel peels up 30° at push-off pivoting on the toe; the toe tips up 15° before heel strike pivoting on the heel; the leg ends at the rolled ankle (side and diagonal views) | by eye |
| Swing foot lift | 0.025 units (a real walk barely lifts the foot; the roll does the work) | by eye |
| Arm swing | opposite the legs, ±0.1 ground units forward and back, through a spring (3.2 Hz, damping 0.55) | swings opposite the same-side leg |
| Head-on arm swing | toward and away from the camera: the arm is drawn straight and foreshortened, never bowing out sideways; a hand coming forward moves 8% toward his middle and passes in front of his body when coming toward the camera (no size change) | by eye |
| Hand angle | relaxed hands follow the end of their arm, so the wrist never kinks | by eye |
| Far arm | in the diagonals the far arm is full width but a little shorter (its resting hand 16 px higher, `rig_calibration.json`) | tested (hand off the body) |
| Start and stop | 0.1 s to full speed; the walk eases in over about 0.13 s and settles to standing | by eye |

**Why the feet must glide.** He covers ground fast for his size: 1.6 body heights a second, where a person walking covers about 0.8. With 0.17-unit legs, fully planted feet would need about 6 steps a second (a scurry) or legs stretched to about 2× (they look too long). The spec takes 3.4 steps a second with a short reach (planted for about 60% of each step), and the feet glide the difference.

**Chosen: 1.7 units/s.** It is calmer than 2.0, and the feet stay planted for about 60% of each step at 3.4 steps a second. Stride (two steps) = 2 × 1.7 / 3.4 = 1.0 ground unit. Each number is a field on `StoreOwner.asset`.

## 4. Hands

| Situation | Sprite | Thumb |
|---|---|---|
| Relaxed, front (S) and back (N) | `hand_open_side` (thumb edge) | toward his centre line, both hands |
| Relaxed, side and diagonal views | `hand_open_back` (back of the hand) | the way he faces |
| Gripping a tool | `hand_grip_*` | the right hand as painted; the left hand mirrored |

Tested per view.

## 5. Draw order (inside his sorting group, back to front)

- **Diagonal and side views (front or back three-quarter):** far-side limbs → body → near-side limbs. The near arm is always in front of the body.
- **Head-on:** sleeves and arms are drawn behind the body, so the apron straps lie over the sleeves as painted; a hand swinging toward the camera passes in front.
- **Pack and pile:** behind the body, except when his back faces the camera (N, NE, NW).
- **Tools:** follow the tool's real depth.

## 6. Review before install

For every change:
- a contact sheet of all 8 directions at the key poses (contact, down, passing, up);
- a GIF at real speed next to the previous version.

Nothing goes to the phone without passing the tests above.
