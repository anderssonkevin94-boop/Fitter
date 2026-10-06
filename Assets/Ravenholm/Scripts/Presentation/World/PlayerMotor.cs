using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Kinematic character motor on the 2D simulation plane (GDD 3): joystick vector × walk speed,
    /// collide-and-slide against footprint colliders on the Blocker layer, clamped to the town.
    /// Collision uses a small circle at the feet, independent of building roof art.
    /// </summary>
    public sealed class PlayerMotor : MonoBehaviour
    {
        public const string BlockerLayerName = "Blocker";
        private const float Skin = 0.01f;

        [Tooltip("Child holding the sprites; bobs and flips without moving the sort pivot.")]
        [SerializeField] private Transform visual;

        private GameRoot _root;
        private int _blockerMask;
        private float _bobPhase;
        private Vector3 _visualBase;

        public void EditorSetup(Transform visualRoot) => visual = visualRoot;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _blockerMask = LayerMask.GetMask(BlockerLayerName);
            if (visual != null) _visualBase = visual.localPosition;
            var s = _root.Services.Sandbox.State;
            transform.position = new Vector3(s.PlayerX, s.PlayerY, 0f);
        }

        private void Update()
        {
            var move = _root.InputRouter.MoveVector;
            if (move.sqrMagnitude > 0.0001f)
            {
                var balance = _root.Config.Balance;
                var delta = move * (balance.walkSpeed * Time.deltaTime);
                var p = Slide(transform.position, delta, balance.playerFootprintRadius, _blockerMask);
                var b = _root.Config.SandboxSettings.plotBounds;
                p.x = Mathf.Clamp(p.x, b.xMin + 0.4f, b.xMax - 0.4f);
                p.y = Mathf.Clamp(p.y, b.yMin + 0.4f, b.yMax - 0.4f);
                transform.position = new Vector3(p.x, p.y, 0f);
                _root.Services.Sandbox.ReportPlayerPosition(p.x, p.y);

                _bobPhase += Time.deltaTime * 14f * move.magnitude;
                if (visual != null && Mathf.Abs(move.x) > 0.15f)
                {
                    var sc = visual.localScale;
                    sc.x = Mathf.Abs(sc.x) * (move.x < 0 ? -1f : 1f);
                    visual.localScale = sc;
                }
            }
            else
            {
                _bobPhase = Mathf.MoveTowards(_bobPhase, Mathf.Round(_bobPhase / Mathf.PI) * Mathf.PI, Time.deltaTime * 10f);
            }

            if (visual != null)
                visual.localPosition = _visualBase + new Vector3(0f, Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.06f, 0f);
        }

        /// <summary>Safe "unstuck": moves the player without touching what they carry.</summary>
        public void Teleport(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, 0f);
            if (_root != null) _root.Services.Sandbox.ReportPlayerPosition(position.x, position.y);
        }

        /// <summary>
        /// Moves a circle by <paramref name="delta"/>, sliding along anything it hits (up to 3 contacts).
        /// Casts ignore colliders the circle already overlaps, so a bad spawn can always walk out.
        /// </summary>
        public static Vector2 Slide(Vector2 position, Vector2 delta, float radius, int mask)
        {
            var startIn = Physics2D.queriesStartInColliders;
            var hitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesStartInColliders = false;
            Physics2D.queriesHitTriggers = false;
            try
            {
                for (var i = 0; i < 3 && delta.sqrMagnitude > 1e-10f; i++)
                {
                    var dist = delta.magnitude;
                    var dir = delta / dist;
                    var hit = Physics2D.CircleCast(position, radius, dir, dist + Skin, mask);
                    if (!hit)
                    {
                        position += delta;
                        break;
                    }
                    var travel = Mathf.Max(0f, hit.distance - Skin);
                    position += dir * travel;
                    var remaining = delta - dir * travel;
                    delta = remaining - Vector2.Dot(remaining, hit.normal) * hit.normal;
                }
                return position;
            }
            finally
            {
                Physics2D.queriesStartInColliders = startIn;
                Physics2D.queriesHitTriggers = hitTriggers;
            }
        }
    }
}
