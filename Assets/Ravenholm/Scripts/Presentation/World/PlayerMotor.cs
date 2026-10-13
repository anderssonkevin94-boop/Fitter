using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Kinematic character motor on the 2D simulation plane (GDD 3): joystick vector × walk speed,
    /// collide-and-slide against footprint colliders on the Blocker layer, clamped to the town and its ground tiles.
    /// Collision uses a small circle at the feet, independent of building roof art.
    ///
    /// The Mine is dug, not entered (dig slab): walking onto a hole dug in the ground drops the supplier into it, and
    /// underground the slab moves them (<see cref="MineSlabView"/>) until they step back up onto the ground.
    /// </summary>
    public sealed class PlayerMotor : MonoBehaviour
    {
        public const string BlockerLayerName = "Blocker";
        private const float Skin = 0.01f;

        [Tooltip("Child holding the sprites; bobs and flips without moving the sort pivot.")]
        [SerializeField] private Transform visual;

        private GameRoot _root;
        private int _blockerMask;

        /// <summary>Underground in the Mine (the slab moves the supplier there).</summary>
        public bool Underground => MineSlabView.Current != null && MineSlabView.Current.PlayerInside;
        private float _bobPhase;
        private Vector3 _visualBase;

        public void EditorSetup(Transform visualRoot) => visual = visualRoot;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _blockerMask = LayerMask.GetMask(BlockerLayerName);
            if (visual != null) _visualBase = visual.localPosition;
            _animator = visual != null ? visual.GetComponent<SupplierAnimator>() : null;
            var s = _root.Services.Sandbox.State;
            transform.position = new Vector3(s.PlayerX, s.PlayerY, 0f);
        }

        private void Update()
        {
            var move = _root.InputRouter.MoveVector;
            if (move.sqrMagnitude > 0.04f) LastMove = move;
            var slab = MineSlabView.Current;
            // Solo play: on a routine the supplier is out working it (drawn by the routine's avatar); moving takes over.
            if (_root.Services.Sites.RoutineSiteId != null)
            {
                if (move.sqrMagnitude > 0.01f) TakeOverFromRoutine(slab);
                else { SetHidden(true); return; }
            }
            SetHidden(false);
            if (slab != null && slab.PlayerInside) slab.UpdatePlayer(move);
            else if (move.sqrMagnitude > 0.0001f) WalkTown(move, slab);

            if (visual != null && _animator != null)
            {
                // Painted animations (SupplierAnimator) show the walk, the work and the carry; the motor only sizes them.
                _swing = Mathf.MoveTowards(_swing, 0f, Time.deltaTime);
                _reach = Mathf.MoveTowards(_reach, 0f, Time.deltaTime * 4f);
                _size = Mathf.MoveTowards(_size, Underground ? UndergroundScale : 1f, Time.deltaTime * 2f);
                visual.localPosition = _visualBase + new Vector3(0f, Elevation, 0f);
                visual.localRotation = Quaternion.identity;
                visual.localScale = new Vector3(_size, _size, 1f);
                return;
            }
            if (move.sqrMagnitude > 0.0001f)
            {
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

            if (visual == null) return;
            // GDD 20 player poses: idle breath, a heavier carry walk (lower, slower bob and a slight lean back),
            // and a short reach when interacting. Visual only; reduced motion keeps the walk bob only.
            var carrying = _root.Services.Logistics.System.CarryCount > 0;
            var moving = move.sqrMagnitude > 0.0001f;
            var bob = Mathf.Abs(Mathf.Sin(_bobPhase)) * (carrying ? 0.035f : 0.06f);
            var facing = Mathf.Sign(visual.localScale.x);
            float lean = 0f, breath = 1f;
            if (!Feel.ReducedMotion)
            {
                if (moving && carrying) lean = 3f * facing;
                if (!moving) { _idlePhase += Time.deltaTime * 2.2f; breath = 1f + Mathf.Sin(_idlePhase) * 0.015f; }
                if (_reach > 0f)
                {
                    _reach = Mathf.MoveTowards(_reach, 0f, Time.deltaTime * 4f);
                    lean = -10f * facing * Mathf.Sin(_reach * Mathf.PI);
                }
                if (_swing > 0f)
                {
                    _swing = Mathf.MoveTowards(_swing, 0f, Time.deltaTime);
                    lean = Mathf.Sin(Time.time * 12f) * 12f * facing;   // a pick or axe swing while the button is held
                }
            }
            visual.localPosition = _visualBase + new Vector3(0f, bob, 0f);
            visual.localRotation = Quaternion.Euler(0f, 0f, lean);
            // Underground the supplier is drawn a little smaller, so two blocks of tunnel fit them, head and all.
            _size = Mathf.MoveTowards(_size, Underground ? UndergroundScale : 1f, Time.deltaTime * 2f);
            visual.localScale = new Vector3(facing * (2f - breath) * _size, breath * _size, 1f);   // the sign is the facing
        }

        /// <summary>How high the supplier stands on the Forest's blocks (drawn up there; the walking plane stays the ground).</summary>
        public float Elevation => !Underground && ForestGridView.Current != null ? ForestGridView.Current.PlayerDrawFeet * MineSlabView.RowHeight : 0f;

        /// <summary>The way the supplier last moved (stick direction on screen), for facing.</summary>
        public Vector2 LastMove { get; private set; } = new Vector2(-1f, -0.5f);

        private float _idlePhase;
        private float _size = 1f;
        private SupplierAnimator _animator;

        /// <summary>
        /// The supplier's ground velocity for a stick position (world units/s on screen). With a painted walk that carries
        /// its planted-feet speeds, it is those (sideways faster than up or down: the elevated camera), times the walk's
        /// playback rate and the drawn size, so the feet stay planted; otherwise the balance's walk speed.
        /// </summary>
        public Vector2 WalkVelocity(Vector2 move)
        {
            if (move.sqrMagnitude > 1f) move.Normalize();
            var set = _animator != null ? _animator.Set : null;
            var axes = set != null ? set.WalkAxes : Vector2.zero;
            if (axes.x <= 0f || axes.y <= 0f) return move * _root.Config.Balance.walkSpeed;
            var size = visual != null ? Mathf.Abs(visual.localScale.y) : 1f;
            return new Vector2(move.x * axes.x, move.y * axes.y) * (set.walkPlaybackRate * size);
        }
        /// <summary>Underground the supplier is drawn a little smaller so a two-block tunnel fits them (the art is one unit tall).</summary>
        public const float UndergroundScale = 0.9f;
        private float _reach;

        // ------------------------------------------------------------------ town

        private void WalkTown(Vector2 move, MineSlabView mine)
        {
            var balance = _root.Config.Balance;
            Vector2 old = transform.position;
            // Standing over thin air (an old save, or ground that moved): back to the Mine's entry rather than stuck.
            if (mine != null && !mine.OnGround(old)) { Teleport(mine.BesideEntry); return; }
            var delta = WalkVelocity(move) * Time.deltaTime;
            var p = Slide(old, delta, balance.playerFootprintRadius, _blockerMask);
            var b = _root.Config.SandboxSettings.WorldBounds;
            p.x = Mathf.Clamp(p.x, b.xMin + 0.4f, b.xMax - 0.4f);
            p.y = Mathf.Clamp(p.y, b.yMin + 0.4f, b.yMax - 0.4f);
            // Only the ground's tiles are walkable; a hole dug into the ground is stepped into (the Mine).
            if (mine != null && !mine.SurfaceWalkable(p))
            {
                if (mine.TryEnter(p)) return;
                if (mine.SurfaceWalkable(old)) p = SlideOnCells(old, p - old, mine.SurfaceWalkable);
            }
            // The Forest's ground has height: one block up at a time, drops are fine, walls of two stop the supplier.
            var forest = ForestGridView.Current;
            if (forest != null && !forest.CanEnter(old, p)) p = SlideOnCells(old, p - old, q => forest.CanEnter(old, q) && (mine == null || mine.SurfaceWalkable(q)));
            // The body is wide: keep the feet off the walls, trees and rock around them, not just out of their cells.
            bool Open(Vector2 at, Vector2 q) => mine == null || mine.SameCell(at, q)
                                                 || (mine.SurfaceWalkable(q) || mine.CanEnterAt(q)) && (forest == null || forest.CanEnter(at, q));
            var pressed = Pressing(old, Open);
            if ((Pressing(p, Open) & ~pressed) != 0)
                p = SlideOnCells(old, p - old, q => (mine == null || mine.SurfaceWalkable(q)) && (forest == null || forest.CanEnter(old, q))
                                                    && (Pressing(q, Open) & ~pressed) == 0);
            if (forest != null) forest.Commit(old, p);
            transform.position = new Vector3(p.x, p.y, 0f);
            _root.Services.Sandbox.ReportPlayerPosition(p.x, p.y);
        }

        /// <summary>
        /// How far (in cells, along the grid's axes) the feet keep from a cell they cannot enter. Only the feet's own
        /// cell used to be checked, so the wide painted body could stand with its feet on a cell's edge, half inside a
        /// cliff or a trunk.
        /// </summary>
        public const float BodyClearance = 0.3f;

        private static readonly Vector2 CellX = new Vector2(0.5f, 0.25f), CellY = new Vector2(-0.5f, 0.25f);

        /// <summary>
        /// Where the body presses into closed cells around a point: one bit per probe, at full and half clearance in the
        /// eight grid directions. A move may not press a probe that was free (so the supplier can always walk away from a
        /// wall it was put against, and freeing one side never pays for pressing into another).
        /// </summary>
        public static int Pressing(Vector2 at, System.Func<Vector2, Vector2, bool> open)
        {
            var n = 0;
            var bit = 1;
            for (var k = 1; k <= 2; k++)
            {
                var r = BodyClearance * k * 0.5f;
                for (var i = -1; i <= 1; i++)
                for (var j = -1; j <= 1; j++)
                {
                    if (i == 0 && j == 0) continue;
                    var diag = i != 0 && j != 0 ? 0.7071f : 1f;
                    if (!open(at, at + (i * CellX + j * CellY) * (r * diag))) n |= bit;
                    bit <<= 1;
                }
            }
            return n;
        }

        private static readonly Vector2[] SlideAxes =
        {
            new Vector2(2f, 1f).normalized, new Vector2(-2f, 1f).normalized,   // the grid's two cell axes (corridors run along them)
            Vector2.right, Vector2.up,
        };

        /// <summary>Moves by <paramref name="delta"/> if allowed, else slides along whichever grid or screen axis is open.</summary>
        public static Vector2 SlideOnCells(Vector2 from, Vector2 delta, System.Func<Vector2, bool> ok)
        {
            if (ok(from + delta)) return from + delta;
            var best = from;
            var bestLen = 1e-8f;
            // Pushing straight into a wall along a grid axis stops: the screen axes would slide it sideways along it.
            var len = delta.magnitude;
            var intoWall = len > 1e-6f && (Mathf.Abs(Vector2.Dot(delta, SlideAxes[0])) > 0.96f * len || Mathf.Abs(Vector2.Dot(delta, SlideAxes[1])) > 0.96f * len);
            for (var a = 0; a < SlideAxes.Length; a++)
            {
                if (intoWall && a >= 2) break;
                var axis = SlideAxes[a];
                var d = axis * Vector2.Dot(delta, axis);
                if (d.sqrMagnitude > bestLen && ok(from + d)) { best = from + d; bestLen = d.sqrMagnitude; }
            }
            return best;
        }


        /// <summary>A short reach toward what the player just used (context button or tap).</summary>
        public void PlayInteract() => _reach = 1f;

        /// <summary>Keeps the work swing going (called each frame mining or chopping is held).</summary>
        public void PlaySwing() => _swing = 0.15f;
        public bool Swinging => _swing > 0f;
        private float _swing;

        private bool _hidden;

        private void SetHidden(bool hidden)
        {
            if (_hidden == hidden) return;
            _hidden = hidden;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !hidden;
        }

        public bool HiddenForRoutine => _hidden;

        /// <summary>The supplier steps back in: the routine ends where their avatar was (in town, or beside the Mine's entry).</summary>
        private void TakeOverFromRoutine(MineSlabView mine)
        {
            var avatar = WorkerView.PlayerAvatar;
            var at = avatar != null ? (Vector2)avatar.transform.position : (Vector2)transform.position;
            var underground = avatar != null && avatar.InShaft;
            _root.Services.Sites.EndRoutine();
            if (mine != null && (underground || mine.PlayerInside || !mine.SurfaceWalkable(at))) at = mine.BesideEntry;
            Teleport(at);
            SandboxSceneController.Current?.SnapPlayerToSavedPosition();
        }
        public bool Reaching => _reach > 0f;

        /// <summary>Safe "unstuck": moves the player without touching what they carry.</summary>
        public void Teleport(Vector2 position)
        {
            MineSlabView.Current?.Leave();   // always onto the town's plane (out of the Mine)
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
