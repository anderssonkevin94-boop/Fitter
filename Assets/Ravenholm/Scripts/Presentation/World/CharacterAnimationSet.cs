using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A character's animation clips (the supplier's, cut from the painted strips by
    /// Tools/ArtImport/import_character_strips.py). The art faces left: "front" clips face the bottom-left and "back"
    /// clips the top-left; the animator mirrors them for the right. Carry frames carry the point on the forearms where
    /// the pile sits, in units from the feet.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Presentation/Character Animation Set", fileName = "CharacterAnimations")]
    public sealed class CharacterAnimationSet : ScriptableObject
    {
        [Serializable]
        public sealed class Clip
        {
            public string name;
            public float fps = 8f;
            public bool loop = true;
            public Sprite[] frames = new Sprite[0];
            [Tooltip("Seconds each frame shows (key poses held for their own time). Empty: every frame lasts 1 / fps.")]
            public float[] durations = new float[0];
            [Tooltip("Per frame: where the carried pile sits (units from the feet; x toward the art's facing). Empty if none.")]
            public Vector2[] carry = new Vector2[0];
            [Tooltip("A walk's ground velocity at its painted timing (units/s, y up) that keeps the feet planted. Zero if none.")]
            public Vector2 velocity;
        }

        public List<Clip> clips = new List<Clip>();

        [Tooltip("How much faster than painted the walk plays at full stick. Movement speed scales with it (the longer-stride walk, 2026-10-13: 1.3, a 346 ms loop).")]
        [Min(0.1f)] public float walkPlaybackRate = 1.3f;
        [Tooltip("Ground covered over what keeps the feet planted (1 = planted; 1.25: a quarter more, a little slide for a brisker walk).")]
        [Min(0.1f)] public float walkGroundFactor = 1.25f;

        /// <summary>
        /// The walk's full-stick speeds at painted timing (units/s): sideways (x) and up or down the screen (y), from the
        /// painted velocities (they lie on an ellipse). Zero when the walks carry no velocities.
        /// </summary>
        public Vector2 WalkAxes
        {
            get
            {
                var axes = Vector2.zero;
                foreach (var c in clips)
                {
                    if (c == null || !c.name.StartsWith("walk")) continue;
                    axes.x = Mathf.Max(axes.x, Mathf.Abs(c.velocity.x));
                    axes.y = Mathf.Max(axes.y, Mathf.Abs(c.velocity.y));
                }
                return axes;
            }
        }

        private Dictionary<string, Clip> _byName;

        public Clip Get(string name)
        {
            if (_byName == null)
            {
                _byName = new Dictionary<string, Clip>();
                foreach (var c in clips) _byName[c.name] = c;
            }
            return _byName.TryGetValue(name, out var clip) ? clip : null;
        }

        private void OnValidate() => _byName = null;
    }
}
