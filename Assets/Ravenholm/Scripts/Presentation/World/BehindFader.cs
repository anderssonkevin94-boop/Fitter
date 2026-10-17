using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Fades a whole object (every sprite under it) while the supplier stands behind it and it covers him, as the trees
    /// and the market stall do. The fourth playtest (2026-10-17): the walk-in wagon hid him digging behind it; the fifth:
    /// the weapon rack and the dummy hid him chopping behind them. A walk-in wagon doesn't fade while he is inside it
    /// (he is framed by it there). The ox never fades. Presentation only.
    /// </summary>
    public sealed class BehindFader : MonoBehaviour
    {
        // The pieces stack (the wagon's back, front and roof over one dark silhouette), so lower than one sprite would need.
        [Range(0.1f, 1f)] [SerializeField] private float fadedAlpha = 0.3f;
        [SerializeField] private float fadeSpeed = 6f;

        private WalkInCart _cart;
        private readonly List<SpriteRenderer> _all = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _parts = new List<SpriteRenderer>();
        private float _alpha = 1f;
        private float _groundY;

        /// <summary>Faded now (tests).</summary>
        public bool Faded => _alpha < 0.99f;

        private void Start()
        {
            _cart = GetComponent<WalkInCart>();
            if (_cart != null && _cart.Footprint != null && _cart.Footprint.Count > 0)
            {
                var sum = 0f;
                foreach (var q in _cart.Footprint) sum += q.y;
                _groundY = sum / _cart.Footprint.Count;   // the floor's middle, relative to the wagon
            }
        }

        private void LateUpdate()
        {
            var c = SandboxSceneController.Current;
            if (c == null || c.Player == null) return;
            // Stock on furniture comes and goes: the pieces are gathered again each frame (the silhouette included).
            GetComponentsInChildren(false, _all);
            _parts.Clear();
            foreach (var sr in _all) if (sr.GetComponentInParent<OxFigure>() == null) _parts.Add(sr);
            var p = (Vector2)c.Player.position;
            var behind = false;
            if (p.y - transform.position.y > _groundY && (_cart == null || !_cart.Sample(p, out _, out _)))
            {
                Vector2[] points = { p + new Vector2(0f, 0.2f), p + new Vector2(0f, 0.55f), p + new Vector2(0f, 0.9f) };
                foreach (var sr in _parts)
                {
                    if (!sr.enabled || sr.sprite == null) continue;
                    foreach (var q in points)
                        if (sr.bounds.Contains(new Vector3(q.x, q.y, sr.bounds.center.z))) { behind = true; break; }
                    if (behind) break;
                }
            }
            var target = behind ? fadedAlpha : 1f;
            var moving = !Mathf.Approximately(_alpha, target);
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * fadeSpeed);
            if (!moving && _alpha >= 0.999f) return;   // solid and settled: leave the colours alone
            foreach (var sr in _parts)
            {
                var col = sr.color;
                if (Mathf.Approximately(col.a, _alpha)) continue;
                col.a = _alpha;
                sr.color = col;
            }
        }
    }
}
