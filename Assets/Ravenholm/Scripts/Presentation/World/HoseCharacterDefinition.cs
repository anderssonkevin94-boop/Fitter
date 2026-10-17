using System.Collections.Generic;
using System;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A rubber-hose character (2026-10-14): static painted parts (a body per view, boots, hands and tools) plus anchor
    /// points, with the arms and legs drawn by the game as bendy tubes (<see cref="HoseRig"/>). Built by setup from the
    /// art's points.json (Astra's General Store Owner, Phase 2). Positions are in units from the ground point under the
    /// character, y up; "left" and "right" are the character's own.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Presentation/Hose Character", fileName = "HoseCharacter")]
    public sealed class HoseCharacterDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class View
        {
            public string name;   // S, SE, E, NE, N (SW, W, NW are mirrors)
            public Sprite body, bootLeft, bootRight;
            public Vector2 shoulderLeft, shoulderRight, hipLeft, hipRight, ankleLeft, ankleRight, wristLeft, wristRight;
            [Tooltip("Headwear fitted to this view's body (same canvas and pivot), by item id. Views without one show none.")]
            public List<Headwear> headwear = new List<Headwear>();
        }

        [Serializable]
        public sealed class Headwear
        {
            public string item;
            public Sprite sprite;
        }

        /// <summary>The headwear drawn over a view's body for an item (null: not fitted to this view).</summary>
        public Sprite HeadwearFor(string view, string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            foreach (var v in views)
                if (v != null && v.name == view && v.headwear != null)
                    foreach (var h in v.headwear) if (h != null && h.item == itemId) return h.sprite;
            return null;
        }

        public View[] views = new View[0];

        [Header("Hands (the wrist is the pivot)")]
        public Sprite handOpenFront, handOpenSide, handOpenBack;
        public Sprite handGripFront, handGripSide, handGripBack;
        public Sprite handPointFront, handPointSide, handPointBack;

        [Header("Tools (the grip is the pivot; handle drawn at 45 degrees)")]
        public Sprite pickaxe, axe;

        [Header("Limbs")]
        public Color armColor = new Color32(0xDA, 0x87, 0x5B, 255);
        public Color legColor = new Color32(0x4F, 0x54, 0x58, 255);
        [Tooltip("Hose widths in units at the shoulder and hip; they taper to the End widths at the wrist and ankle.")]
        [Min(0.005f)] public float armWidth = 0.1f, legWidth = 0.11f;
        [Min(0.005f)] public float armWidthEnd = 0.062f, legWidthEnd = 0.07f;

        [Tooltip("The far arm in the diagonal and profile views (arm, sleeve and hand) drawn smaller; 1 = full size (Kevin preferred a shorter far arm to a thinner one, 2026-10-14).")]
        [Range(0.5f, 1f)] public float farLimbScale = 1f;

        [Header("Sleeves (the top of each arm: the body art has none, so they move with the arm)")]
        public Color sleeveColor = new Color32(0xE9, 0xD2, 0xB3, 255);
        [Tooltip("Draw procedural sleeves over the arm roots (off when the body art paints its own sleeves, as the citizen's does).")]
        public bool sleeves = true;
        [Tooltip("Sleeve width at the shoulder and at its hem, in units.")]
        [Min(0.005f)] public float sleeveWidth = 0.13f, sleeveWidthEnd = 0.118f;
        [Tooltip("How much of the arm the sleeve covers, from the shoulder.")]
        [Range(0.1f, 0.8f)] public float sleeveLength = 0.36f;

        [Header("Pack (an L: a bar up his back and a shelf the carried pile rests on)")]
        public Color packColor = new Color32(0x8B, 0x5E, 0x3C, 255);
        [Min(0.005f)] public float packWidth = 0.04f;
        [Tooltip("How far the shelf sticks out behind his back, in units.")]
        [Min(0.02f)] public float shelfDepth = 0.17f;
        [Tooltip("An unlit sprite material (vertex colours) for the tubes.")]
        public Material limbMaterial;

        [Header("Motion")]
        [Tooltip("Stride (ground covered per two steps) at walking speed, in units; it grows a little with speed.")]
        [Min(0.05f)] public float stride = 1.1f;
        [Tooltip("How high a swinging boot lifts, in units.")]
        [Min(0f)] public float footLift = 0.025f;
        [Tooltip("The body's bounce over each step, in units.")]
        [Min(0f)] public float bounce = 0.022f;
        [Tooltip("How far the hands swing forward and back while walking, in units.")]
        [Min(0f)] public float armSwing = 0.1f;
        [Tooltip("How far a hose bows when its ends come closer than its length (0 = straight, 1 = a half circle).")]
        [Range(0f, 1f)] public float bow = 0.55f;

        [Header("Walk feel (the bouncy cartoon walk, 2026-10-14)")]
        [Tooltip("Share of the cycle each foot is on the ground (0.5: no double support, a springy gait).")]
        [Range(0.4f, 0.7f)] public float duty = 0.5f;
        [Tooltip("The longest a leg may stretch, as a share of its length. His legs are short, so at full stride the feet glide a little rather than the legs stretching further (Kevin, 2026-10-14).")]
        [Range(1f, 2f)] public float maxLegStretch = 1.3f;
        [Tooltip("Walking speed at full stick, ground units per second (the spec: 2.04).")]
        [Min(0.1f)] public float walkSpeed = 2.04f;
        [Tooltip("Where the hand holds each tool: units along the handle from the art's pivot toward its end (the pivot is the handle's middle; Kevin, 2026-10-17: hold it near the end).")]
        public float axeGrip = 0.156f, pickaxeGrip = 0.168f;
        [Tooltip("Steps per second at full speed, the same in every direction (the spec: 3.7).")]
        [Min(0.5f)] public float cadence = 3.7f;
        [Tooltip("How far a foot reaches ahead of or behind the hip, ground units (the spec: 0.16).")]
        [Min(0.02f)] public float maxFootReach = 0.16f;
        [Tooltip("How much lower his hips ride while walking (a slight knee bend): the legs reach further without stretching.")]
        [Min(0f)] public float walkCrouch = 0.025f;
        [Tooltip("How far the body shifts toward the supporting foot, in units.")]
        [Min(0f)] public float sway = 0.02f;
        [Tooltip("Squash at the bottom of each step and stretch at the top (fraction of height).")]
        [Range(0f, 0.15f)] public float squash = 0.025f;
        [Tooltip("Forward lean while walking (side views), and side-to-side tilt head-on, in degrees.")]
        [Min(0f)] public float walkLean = 5f, walkTilt = 3f;
        [Tooltip("How much the boots roll: toe up at heel strike, heel up at push-off (degrees). Unused since the heel-toe roll.")]
        [Min(0f)] public float footRoll = 16f;
        [Tooltip("Heel-toe roll (a real walk, 2026-10-14): the heel peels up this far at push-off, pivoting on the toe...")]
        [Min(0f)] public float heelUpAngle = 30f;
        [Tooltip("... and the toe tips up this far before the heel lands, pivoting on the heel (degrees).")]
        [Min(0f)] public float toeUpAngle = 15f;
        [Tooltip("The boot's toe ahead of and heel behind the ankle, and the ankle above the sole (units, from the side-view art).")]
        [Min(0f)] public float toeLength = 0.14f, heelLength = 0.065f, ankleHeight = 0.12f;
        [Tooltip("How quickly the hands follow their swing (spring frequency, Hz) and how bouncy (damping ratio, 1 = none).")]
        [Min(1f)] public float armSpringHz = 3.2f;
        [Range(0.2f, 1f)] public float armSpringDamping = 0.55f;

        public View Get(string name)
        {
            foreach (var v in views) if (v != null && v.name == name) return v;
            return views.Length > 0 ? views[0] : null;
        }
    }
}
