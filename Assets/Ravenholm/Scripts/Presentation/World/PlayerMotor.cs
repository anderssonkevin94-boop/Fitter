using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Kinematic character motor on the 2D simulation plane (GDD 3): joystick vector × walk speed,
    /// collide-and-slide against footprint colliders on the Blocker layer, clamped to the town and its ground tiles.
    /// Collision uses a small circle at the feet, independent of building roof art.
    ///
    /// The Mine is walkable too (GDD 6): push down at the ladder to climb into the shaft, up and down along it like a
    /// corridor, sideways at a level to step off onto its ledge or the pit floor, and walk its tunnels. Underground the
    /// player moves on the same plane coordinates as the town, drawn one level lower per level.
    /// </summary>
    public sealed class PlayerMotor : MonoBehaviour
    {
        public const string BlockerLayerName = "Blocker";
        private const float Skin = 0.01f;

        [Tooltip("Child holding the sprites; bobs and flips without moving the sort pivot.")]
        [SerializeField] private Transform visual;

        private const float ClimbSpeed = 2.4f;   // levels per second

        private GameRoot _root;
        private int _blockerMask;
        private Vector2 _plane;
        private float _drop;
        private float _climb;
        private int _climbTarget = -1;

        /// <summary>0 in town; otherwise the Mine level the player stands on (or last left from the ladder).</summary>
        public int MineLevel { get; private set; }
        public bool OnLadder { get; private set; }
        /// <summary>Levels below the town while on the ladder.</summary>
        public float ClimbHeight => _climb;
        public bool Underground => OnLadder || MineLevel > 0;
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
            _plane = transform.position;
        }

        private void Update()
        {
            var move = _root.InputRouter.MoveVector;
            var mine = MineDigView.Current;
            // Solo play: on a routine the supplier is out working it (drawn by the routine's avatar); moving takes over.
            if (_root.Services.Sites.RoutineSiteId != null)
            {
                if (move.sqrMagnitude > 0.01f) TakeOverFromRoutine(mine);
                else { SetHidden(true); return; }
            }
            SetHidden(false);
            if (mine == null && Underground) { OnLadder = false; MineLevel = 0; }
            if (OnLadder) Climb(move, mine);
            else if (MineLevel > 0) WalkMine(move, mine);
            else if (move.sqrMagnitude > 0.0001f) WalkTown(move, mine);

            if (move.sqrMagnitude > 0.0001f)
            {
                _bobPhase += Time.deltaTime * 14f * move.magnitude * (OnLadder ? 0.5f : 1f);
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
            visual.localScale = new Vector3(facing * (2f - breath), breath, 1f);   // unit scale; the sign is the facing
        }

        private float _idlePhase;
        private float _reach;

        // ------------------------------------------------------------------ town

        private void WalkTown(Vector2 move, MineDigView mine)
        {
            var balance = _root.Config.Balance;
            Vector2 old = transform.position;
            // The ladder is entered by pushing down at it (the shaft is a corridor below the town).
            if (mine != null && mine.DeepestFloor >= 1 && IsVertical(move, -1) && Vector2.Distance(old, mine.LadderPlane) < 0.7f)
            {
                StartClimb(0f);
                return;
            }
            var delta = move * (balance.walkSpeed * Time.deltaTime);
            var p = Slide(old, delta, balance.playerFootprintRadius, _blockerMask);
            var b = _root.Config.SandboxSettings.WorldBounds;
            p.x = Mathf.Clamp(p.x, b.xMin + 0.4f, b.xMax - 0.4f);
            p.y = Mathf.Clamp(p.y, b.yMin + 0.4f, b.yMax - 0.4f);
            // Only the town's ground tiles are walkable: not the open shaft or the notch in front of it.
            if (mine != null && !mine.SurfaceWalkable(p) && mine.SurfaceWalkable(old))
                p = SlideOnCells(old, p - old, mine.SurfaceWalkable);
            transform.position = new Vector3(p.x, p.y, 0f);
            _plane = p;
            _root.Services.Sandbox.ReportPlayerPosition(p.x, p.y);
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
            foreach (var axis in SlideAxes)
            {
                var d = axis * Vector2.Dot(delta, axis);
                if (d.sqrMagnitude > bestLen && ok(from + d)) { best = from + d; bestLen = d.sqrMagnitude; }
            }
            return best;
        }

        private static bool IsVertical(Vector2 move, int sign) => move.y * sign > 0.5f && Mathf.Abs(move.y) > Mathf.Abs(move.x);

        // ------------------------------------------------------------------ mine

        /// <summary>Climbs (from wherever the player is at the ladder) to a level, then steps off there.</summary>
        public void ClimbTo(int level)
        {
            var mine = MineDigView.Current;
            if (mine == null || !mine.IsStop(level)) return;
            if (!OnLadder) StartClimb(MineLevel);
            _climbTarget = level;
        }

        private void StartClimb(float h)
        {
            OnLadder = true;
            _climb = h;
            _climbTarget = -1;
        }

        private void Climb(Vector2 move, MineDigView mine)
        {
            var deepest = Mathf.Max(0, mine.DeepestFloor);
            var dt = Time.deltaTime;
            if (Mathf.Abs(move.y) > 0.3f)
            {
                _climbTarget = -1;
                _climb = Mathf.Clamp(_climb - move.y * ClimbSpeed * dt, 0f, deepest);
            }
            else if (_climbTarget >= 0)
            {
                _climb = Mathf.MoveTowards(_climb, Mathf.Min(_climbTarget, deepest), ClimbSpeed * dt);
                if (Mathf.Approximately(_climb, Mathf.Min(_climbTarget, deepest))) { StepOff(Mathf.Min(_climbTarget, deepest), mine); return; }
            }
            else _climb = Mathf.MoveTowards(_climb, NearestStop(_climb, mine), ClimbSpeed * dt);   // settle on a level when let go

            var stop = Mathf.RoundToInt(_climb);
            if (Mathf.Abs(_climb - stop) < 0.05f && mine.IsStop(stop))
            {
                if (stop == 0 && IsVertical(move, 1)) { StepOff(0, mine); return; }
                if (stop >= 1 && Mathf.Abs(move.x) > 0.5f && Mathf.Abs(move.x) > Mathf.Abs(move.y)) { StepOff(stop, mine); return; }
            }
            transform.position = mine.LadderWorld(_climb);
        }

        private static int NearestStop(float h, MineDigView mine)
        {
            var best = 0;
            for (var l = 0; l <= Mathf.Max(0, mine.DeepestFloor); l++)
                if (mine.IsStop(l) && Mathf.Abs(l - h) < Mathf.Abs(best - h)) best = l;
            return best;
        }

        private void StepOff(int level, MineDigView mine)
        {
            OnLadder = false;
            _climbTarget = -1;
            MineLevel = level;
            if (level == 0)
            {
                _plane = mine.LadderPlane + new Vector2(-0.25f, 0.125f);   // the ladder's top cell
                transform.position = _plane;
                _root.Services.Sandbox.ReportPlayerPosition(_plane.x, _plane.y);
                return;
            }
            _plane = mine.LadderPlane + new Vector2(0.25f, -0.125f);       // the shaft cell at the ladder's foot
            _drop = mine.FloorDrop(level, _plane);
            transform.position = _plane - new Vector2(0f, _drop);
        }

        private void WalkMine(Vector2 move, MineDigView mine)
        {
            var level = MineLevel;
            if (move.sqrMagnitude > 0.0001f)
            {
                var target = _plane + move * (_root.Config.Balance.walkSpeed * Time.deltaTime);
                if (Vector2.Distance(_plane, mine.LadderPlane) < 0.7f && Mathf.Abs(move.y) > 0.5f && Mathf.Abs(move.y) > Mathf.Abs(move.x) &&
                    !mine.Walkable(level, target))
                {
                    StartClimb(level);
                    return;
                }
                // Standing where a block was just dug away: any step back into the shaft is allowed.
                if (!mine.Walkable(level, _plane) && mine.IsPitCell(mine.CellAt(target))) _plane = target;
                else _plane = SlideOnCells(_plane, target - _plane, p => mine.Walkable(level, p));
            }
            _drop = Mathf.MoveTowards(_drop, mine.FloorDrop(level, _plane), Time.deltaTime * 2f);
            transform.position = _plane - new Vector2(0f, _drop);
            // A reload puts the player back at the ladder top in town.
            var top = mine.LadderPlane + new Vector2(-0.25f, 0.125f);
            _root.Services.Sandbox.ReportPlayerPosition(top.x, top.y);
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

        /// <summary>The supplier steps back in: the routine ends where their avatar was (in town, or at the ladder top).</summary>
        private void TakeOverFromRoutine(MineDigView mine)
        {
            var avatar = WorkerView.PlayerAvatar;
            var at = avatar != null ? (Vector2)avatar.transform.position : (Vector2)transform.position;
            var underground = avatar != null && avatar.InShaft;
            _root.Services.Sites.EndRoutine();
            if (mine != null && (underground || !mine.SurfaceWalkable(at))) at = mine.LadderPlane + new Vector2(0.25f, 0.125f);
            else if (mine == null && avatar == null) at = transform.position;
            Teleport(at);
            SandboxSceneController.Current?.SnapPlayerToSavedPosition();
        }
        public bool Reaching => _reach > 0f;

        /// <summary>Safe "unstuck": moves the player without touching what they carry.</summary>
        public void Teleport(Vector2 position)
        {
            OnLadder = false;
            MineLevel = 0;
            _climbTarget = -1;
            _plane = position;
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
