using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The parked ox ambles about near the wagon (Kevin, 2026-10-16): he stands a while, then walks a few steps to a spot
    /// close to his place at the shafts, now and then back to it. He keeps to walkable ground, goes round nothing (a spot
    /// with a stall, a wall or the supplier in the way is skipped) and stops if the supplier steps in front of him. His
    /// blocker moves with him. Presentation only.
    /// </summary>
    public sealed class OxWander : MonoBehaviour
    {
        [SerializeField] private float radiusX = 1.8f, radiusY = 0.9f;   // the iso-squashed circle round his place
        [SerializeField] private float speed = 0.45f;
        [SerializeField] private float standMin = 3f, standMax = 8f;
        [SerializeField] private float footprint = 0.38f;

        private OxFigure _ox;
        private Collider2D _body;
        private Vector2 _home;   // local to the wagon: he stays with it
        private Vector2? _target;
        private float _standUntil;
        private int _blockers;

        public bool Walking => _target != null;
        public Vector2 Home => transform.parent != null ? (Vector2)transform.parent.TransformPoint(_home) : _home;
        public float RadiusX => radiusX;
        public float RadiusY => radiusY;

        private void Start()
        {
            _ox = GetComponent<OxFigure>();
            _body = GetComponentInChildren<Collider2D>();
            _home = transform.localPosition;
            _blockers = LayerMask.GetMask(PlayerMotor.BlockerLayerName);
            _standUntil = Time.time + Random.Range(standMin, standMax);
        }

        /// <summary>Ends his rest now (tests).</summary>
        public void WalkNow() => _standUntil = 0f;

        private void Update()
        {
            if (_ox == null) return;
            if (DriveView.Current != null && DriveView.Current.Driving) { Stand(); return; }
            if (_target == null)
            {
                if (Time.time >= _standUntil && Pick(out var to)) { _target = to; _ox.Walking = true; }
                else if (Time.time >= _standUntil) _standUntil = Time.time + 1f;   // nowhere free now: try again soon
                return;
            }
            var at = (Vector2)transform.position;
            var d = _target.Value - at;
            var step = speed * Time.deltaTime;
            var player = Player();
            if (player != null && Vector2.Distance(at + d.normalized * 0.5f, player.Value) < 0.55f) { Stand(); return; }   // someone in front
            _ox.Face(d);
            if (d.magnitude <= step) { transform.position = _target.Value; Stand(); return; }
            transform.position = at + d.normalized * step;
        }

        private void Stand()
        {
            if (_target == null && !_ox.Walking) return;
            _target = null;
            _ox.Walking = false;
            _standUntil = Time.time + Random.Range(standMin, standMax);
        }

        private static Vector2? Player()
        {
            var p = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            return p != null ? (Vector2)p.position : (Vector2?)null;
        }

        /// <summary>A free spot near his place (one in three times his place itself), reachable in a straight line.</summary>
        private bool Pick(out Vector2 to)
        {
            var from = (Vector2)transform.position;
            for (var tries = 0; tries < 8; tries++)
            {
                to = Home;
                if (tries > 0 || Random.value > 0.33f || Vector2.Distance(from, Home) < 0.3f)
                {
                    var r = Random.insideUnitCircle;
                    to = Home + new Vector2(r.x * radiusX, r.y * radiusY);
                }
                var d = to - from;
                if (d.magnitude < 0.5f) continue;
                if (!Clear(from, to)) continue;
                return true;
            }
            to = from;
            return false;
        }

        private bool Clear(Vector2 from, Vector2 to)
        {
            var mine = MineSlabView.Current;
            var forest = ForestGridView.Current;
            var player = Player();
            var d = to - from;
            var steps = Mathf.CeilToInt(d.magnitude / 0.2f);
            for (var i = 1; i <= steps; i++)
            {
                var q = from + d * (i / (float)steps);
                if (mine != null && !mine.SurfaceWalkable(q)) return false;
                if (forest != null && !forest.CanEnter(from, q)) return false;
                if (player != null && Vector2.Distance(q, player.Value) < 0.7f) return false;
            }
            // Stalls, the wagon, walls (his own blocker aside, and any he already stands against, like the shafts).
            var start = Physics2D.OverlapCircleAll(from, footprint, _blockers);
            for (var i = 1; i <= steps; i++)
                foreach (var c in Physics2D.OverlapCircleAll(from + d * (i / (float)steps), footprint, _blockers))
                    if (c != _body && System.Array.IndexOf(start, c) < 0) return false;
            return true;
        }
    }
}
