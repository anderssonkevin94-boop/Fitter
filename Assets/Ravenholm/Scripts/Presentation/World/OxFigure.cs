using System;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Astra's approved ox, eight-direction walk V12 (2026-10-16): per direction, his standing pose and six walk frames.</summary>
    [Serializable]
    public sealed class OxArt
    {
        [Serializable]
        public sealed class View
        {
            public string dir;
            public Sprite stand;
            public Sprite[] walk = Array.Empty<Sprite>();
        }

        public View[] views = Array.Empty<View>();
        [Tooltip("The walk's frames per second (the pack: six frames, a 1.2-second loop).")]
        public float fps = 5f;

        public bool Valid => views != null && views.Length == 8;

        public View Get(string dir)
        {
            foreach (var v in views) if (v != null && v.dir == dir) return v;
            return views.Length > 0 ? views[0] : null;
        }
    }

    /// <summary>
    /// The ox (Astra's V12 pack): the assembled frames of the direction he faces, every sprite pivoted on that direction's
    /// ground anchor, so turning never shifts him. Walking plays the six frames at 5 fps round the 1.2-second loop; standing
    /// shows the rest pose. One pixels-per-unit for all directions (no per-direction scaling: V12 has it baked in), and
    /// E, NE, SE are the pack's own mirrors (never flipped here). Presentation only.
    /// </summary>
    public sealed class OxFigure : MonoBehaviour
    {
        /// <summary>Eight directions round the circle, as the supplier faces (the iso squash undone: y doubled).</summary>
        private static readonly string[] Dirs = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

        [SerializeField] private OxArt _art;
        [SerializeField] private SpriteRenderer _sr;
        private float _time;

        public string Dir { get; private set; } = "NE";
        public bool Walking { get; set; }
        public SpriteRenderer Renderer => _sr;

        public static OxFigure Create(Transform parent, OxArt art, string sortingLayer, int order, Material material)
        {
            var go = new GameObject("Ox");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = order;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;   // his ground anchor
            if (material != null) sr.sharedMaterial = material;
            go.AddComponent<SpriteOutline>();   // the game's outline, as round everything else (his frames are outline-free)
            var ox = go.AddComponent<OxFigure>();
            ox._art = art;
            ox._sr = sr;
            ox.Apply();
            return ox;
        }

        /// <summary>Faces a direction on screen (any length; zero keeps the facing).</summary>
        public void Face(Vector2 screen)
        {
            if (screen.sqrMagnitude < 1e-8f) return;
            var a = Mathf.Atan2(screen.y * 2f, screen.x) * Mathf.Rad2Deg;
            var i = Mathf.RoundToInt(a / 45f);
            Dir = Dirs[(i % 8 + 8) % 8];
        }

        public void FaceDir(string dir) => Dir = dir;

        private void Update()
        {
            if (Walking) _time += Time.deltaTime;
            else _time = 0f;
            Apply();
        }

        private void Apply()
        {
            if (_art == null || _sr == null) return;
            var v = _art.Get(Dir);
            if (v == null) return;
            if (Walking && v.walk != null && v.walk.Length > 0)
            {
                var frame = Mathf.FloorToInt(_time * Mathf.Max(0.1f, _art.fps)) % v.walk.Length;   // the last frame loops to the first
                _sr.sprite = v.walk[frame];
            }
            else _sr.sprite = v.stand;
        }
    }
}
