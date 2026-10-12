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
        }

        public List<Clip> clips = new List<Clip>();

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
