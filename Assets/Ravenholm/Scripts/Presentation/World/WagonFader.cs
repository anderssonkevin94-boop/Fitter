using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The walk-in wagon fades while the supplier stands behind it (the fourth playtest, 2026-10-17: digging the hill rock
    /// behind the wagon, only his hat showed), as the trees and the market stall do. Not while he is inside it (he is meant
    /// to be framed by it there), and never the ox. Presentation only.
    /// </summary>
    public sealed class WagonFader : MonoBehaviour
    {
        [Range(0.1f, 1f)] [SerializeField] private float fadedAlpha = 0.3f;   // its layers stack (back, front, roof)
        [SerializeField] private float fadeSpeed = 6f;

        private WalkInCart _cart;
        private readonly List<SpriteRenderer> _parts = new List<SpriteRenderer>();
        private float _alpha = 1f;
        private float _middleY;

        /// <summary>The wagon is faded now (tests).</summary>
        public bool Faded => _alpha < 0.99f;

        private void Start()
        {
            _cart = GetComponent<WalkInCart>();
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.GetComponentInParent<OxFigure>() == null) _parts.Add(sr);   // the silhouette too: a solid dark fill behind the pieces

            if (_cart != null && _cart.Footprint != null && _cart.Footprint.Count > 0)
            {
                var sum = 0f;
                foreach (var q in _cart.Footprint) sum += q.y;
                _middleY = sum / _cart.Footprint.Count;
            }
        }

        private void LateUpdate()
        {
            var c = SandboxSceneController.Current;
            if (c == null || c.Player == null || _cart == null) return;
            var p = (Vector2)c.Player.position;
            var local = p - (Vector2)transform.position;   // the cart's floor is kept relative to its position
            var behind = false;
            if (!_cart.Sample(p, out _, out _) && local.y > _middleY)
            {
                Vector2[] points = { p + new Vector2(0f, 0.2f), p + new Vector2(0f, 0.55f), p + new Vector2(0f, 0.9f) };
                foreach (var sr in _parts)
                {
                    if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy) continue;
                    foreach (var q in points)
                        if (sr.bounds.Contains(new Vector3(q.x, q.y, sr.bounds.center.z))) { behind = true; break; }
                    if (behind) break;
                }
            }
            var target = behind ? fadedAlpha : 1f;
            if (Mathf.Approximately(_alpha, target)) return;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * fadeSpeed);
            foreach (var sr in _parts)
            {
                if (sr == null) continue;
                var col = sr.color;
                col.a = _alpha;
                sr.color = col;
            }
        }
    }
}
