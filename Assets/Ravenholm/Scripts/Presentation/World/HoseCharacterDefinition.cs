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
        [Tooltip("Tube widths in units.")]
        [Min(0.005f)] public float armWidth = 0.053f, legWidth = 0.059f;
        [Tooltip("An unlit sprite material (vertex colours) for the tubes.")]
        public Material limbMaterial;

        [Header("Motion")]
        [Tooltip("Stride (ground covered per two steps) at walking speed, in units; it grows a little with speed.")]
        [Min(0.05f)] public float stride = 0.62f;
        [Tooltip("How high a swinging boot lifts, in units.")]
        [Min(0f)] public float footLift = 0.06f;
        [Tooltip("The body's bounce over each step, in units.")]
        [Min(0f)] public float bounce = 0.025f;
        [Tooltip("How far the hands swing forward and back while walking, in units.")]
        [Min(0f)] public float armSwing = 0.07f;
        [Tooltip("How far a hose bows when its ends come closer than its length (0 = straight, 1 = a half circle).")]
        [Range(0f, 1f)] public float bow = 0.55f;

        public View Get(string name)
        {
            foreach (var v in views) if (v != null && v.name == name) return v;
            return views.Length > 0 ? views[0] : null;
        }
    }
}
