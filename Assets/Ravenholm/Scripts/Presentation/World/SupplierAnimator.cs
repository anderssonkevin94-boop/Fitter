using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Plays the supplier's painted animations on their body sprite (presentation only).
    ///
    /// The clip follows what they are doing: idle or walking (carry versions while carrying), mining (at a wall, down or
    /// up), chopping, climbing a ladder, falling, and a pick-up flourish. The view follows where they head: walking up
    /// the screen shows their back, down the screen their front; the art faces left and is mirrored for the right. In
    /// the Mine the view follows the way they face along the cliff.
    ///
    /// On the player it reads the motor, the Mine and the carry; a routine's figure drives it through <see cref="Work"/>
    /// and its own movement.
    ///
    /// Eight-direction sets (the dwarf, 2026-10-13: clips named walk_N .. walk_NW, idle_N ..) are painted for every
    /// direction and never mirrored. Actions the set has no painted clip for (mining, chopping, carrying, climbing) play
    /// the standing or walking pose of the facing direction with a procedural motion: a wind-up and a strike for tools,
    /// a dip for a pick-up. The pile then rides on the head (<see cref="HeadTop"/>).
    /// </summary>
    public sealed class SupplierAnimator : MonoBehaviour
    {
        public enum Activity { None, Mine, Chop }

        [SerializeField] private CharacterAnimationSet set;
        [SerializeField] private SpriteRenderer body;

        public CharacterAnimationSet Set => set;

        public void EditorSetup(CharacterAnimationSet animations, SpriteRenderer bodyRenderer)
        {
            set = animations;
            body = bodyRenderer;
        }

        /// <summary>For a routine's figure: what it is doing at its work spot (the player's is read from the game).</summary>
        public Activity Work { get; set; }

        /// <summary>The clip shown (an eight-direction set: e.g. walk_SE).</summary>
        public string Clip { get; private set; } = "";
        /// <summary>What the character is doing, by the clip it asked for (e.g. mine_down_front), painted or not.</summary>
        public string Requested { get; private set; } = "";
        /// <summary>The facing of eight (N, NE, E, SE, S, SW, W, NW; screen directions, north up the screen).</summary>
        public string Dir { get; private set; } = "S";
        public int Frame { get; private set; }
        public bool Back { get; private set; }
        public bool Mirrored { get; private set; }

        /// <summary>Where the carried pile sits, relative to the character's root (null when this frame has none).</summary>
        public Vector2? CarryAnchor { get; private set; }

        /// <summary>The top of the head, in the character root's space (where a pile rides when the arms do not hold it).</summary>
        public float HeadTop
        {
            get
            {
                if (_rig != null) return _rig.HeadTop;
                if (body == null || body.sprite == null) return 1f;
                var bt = body.transform;
                return transform.localPosition.y + (bt.localPosition.y + body.sprite.bounds.max.y * bt.localScale.y) * Mathf.Abs(transform.localScale.y);
            }
        }

        private GameRoot _root;
        private PlayerMotor _motor;
        private Transform _rootTransform;
        private Vector3 _lastPos;
        private Vector2 _heading = new Vector2(0f, -1f);   // facing the camera (S; the front view, unmirrored)
        private float _time, _pickup, _moving;
        private bool _subscribed;

        private void Start()
        {
            _root = GameRoot.Instance;
            _motor = GetComponentInParent<PlayerMotor>();
            _rig = GetComponent<HoseRig>();
            _rootTransform = transform.parent != null ? transform.parent : transform;
            _lastPos = _rootTransform.position;
            if (_motor != null && _root != null)
            {
                _root.Services.Logistics.System.PickedUp += OnPickedUp;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && _root != null && _root.Services != null) _root.Services.Logistics.System.PickedUp -= OnPickedUp;
        }

        private void OnPickedUp(string from, Ravenholm.Domain.Logistics.CarryUnit unit) => _pickup = 0.3f;

        private void LateUpdate()
        {
            if (set == null || body == null) return;
            var dt = Time.deltaTime;
            var pos = _rootTransform.position;
            var delta = (Vector2)(pos - _lastPos);
            _lastPos = pos;
            // Moving: a little hysteresis so a stop of one frame does not flicker to idle.
            if (dt > 0f && delta.magnitude / dt > 0.08f)
            {
                _moving = 0.12f;
                _heading = delta;
            }
            else _moving = Mathf.Max(0f, _moving - dt);
            // The supplier faces where the stick points while it is held, even pushing into (or sliding along) a wall or
            // the wagon's counter: they can stand facing out over the counter (Kevin, 2026-10-16).
            var stick = _motor != null && _root != null ? _root.InputRouter.MoveVector : Vector2.zero;
            if (stick.sqrMagnitude > 0.04f) _heading = new Vector2(stick.x, stick.y * 0.5f);   // the stick on screen, as walked
            // Smoothed ground speed (the walk's playback rate follows it).
            if (dt > 0f) _speed = Mathf.Lerp(_speed, delta.magnitude / dt, Mathf.Clamp01(dt * 12f));
            var moving = _moving > 0f;
            Face(_heading);

            if (_motor != null && MineSlabView.Current != null && MineSlabView.Current.Riding) moving = false;   // riding is not walking
            var name = _motor != null ? PlayerClip(moving, out var advance) : FigureClip(moving, out advance);
            Play(name, advance ? dt : 0f);
            if (_rig != null)
            {
                // The rubber-hose character draws itself from what the animator decided (pose, facing, ground covered).
                var carrying = _motor != null && _root != null && _root.Services.Logistics.System.CarryCount > 0;
                _rig.Drive(Dir, PoseOf(Requested, moving), delta.magnitude, dt, transform.localPosition, Mathf.Abs(transform.localScale.y), carrying);
                CarryAnchor = _rig.CarryAnchor;
            }
        }

        private static readonly string[] Dirs = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

        /// <summary>
        /// Faces a direction on screen. The eight directions split the circle evenly once the iso squash is undone (y
        /// doubled), so moving along the grid's axes faces a diagonal (NE, NW, SE, SW) and straight up the screen faces N.
        /// </summary>
        private void Face(Vector2 screen)
        {
            if (screen.sqrMagnitude < 1e-8f) return;
            Back = screen.y > 0.02f;
            Mirrored = screen.x > 0f;
            var i = Mathf.RoundToInt(Mathf.Atan2(screen.y * 2f, screen.x) / (Mathf.PI / 4f));
            Dir = Dirs[(i % 8 + 8) % 8];
        }

        /// <summary>The clip for an action in the current view: the eight-direction one if painted, else front or back.</summary>
        private string View(string baseName)
        {
            var eight = baseName + "_" + Dir;
            return set.Get(eight) != null ? eight : baseName + (Back ? "_back" : "_front");
        }

        /// <summary>Faces a direction on the Mine's grid (x into the cliff, u along it): up the screen is the back view.</summary>
        private void FaceAlongCliff(Vector2Int f) => Face(f.x * new Vector2(0.5f, 0.25f) + f.y * new Vector2(-0.5f, 0.25f));

        private string PlayerClip(bool moving, out bool advance)
        {
            advance = true;
            var slab = MineSlabView.Current;
            var inside = slab != null && slab.PlayerInside;
            var carrying = _root != null && _root.Services.Logistics.System.CarryCount > 0;
            if (inside && !moving) FaceAlongCliff(slab.Facing);
            _pickup = Mathf.Max(0f, _pickup - Time.deltaTime);

            if (inside && slab.Riding)
            {
                // On the elevator's platform: standing, facing along the cliff, carried up or down.
                FaceAlongCliff(slab.Facing);
                return View(carrying ? "carry_idle" : "idle");
            }
            if (inside && slab.Falling) return "fall_front";
            if (_motor.Swinging)
            {
                if (slab != null && slab.CanDigNow && slab.Target.HasValue && (inside || slab.PressDigging))
                {
                    var t = slab.Target.Value;
                    var feet = inside ? slab.Feet : 0;
                    if (inside) FaceAlongCliff(slab.Facing);
                    if (t.Row >= feet) return "mine_down_front";
                    if (t.Row <= feet - 3) return "mine_up_back";
                    return View("mine");
                }
                FaceContextTarget();
                var c = SandboxSceneController.Current;
                if (c != null && (c.ContextTarget is ForestRockSpot)) return View("mine");   // rock above ground
                return View("chop");
            }
            if (_pickup > 0f) return "pickup_front";
            if (carrying) return View(moving ? "carry_walk" : "carry_idle");
            return View(moving ? "walk" : "idle");
        }

        /// <summary>Turn toward what is being worked (the tree being chopped).</summary>
        private void FaceContextTarget()
        {
            var c = SandboxSceneController.Current;
            if (c == null || c.ContextTarget == null) return;
            var d = (Vector2)(c.ContextTarget.transform.position - _rootTransform.position);
            if (d.sqrMagnitude < 0.0001f) return;
            Face(d);
        }

        private string FigureClip(bool moving, out bool advance)
        {
            advance = true;
            if (moving) return View("walk");
            switch (Work)
            {
                case Activity.Mine: return View("mine");
                case Activity.Chop: return View("chop");
                default: return View("idle");
            }
        }

        private enum Motion { None, Swing, Dip }

        private void Play(string name, float dt)
        {
            Requested = name;
            var clip = set.Get(name);
            var motion = Motion.None;
            if (clip == null)
            {
                // Not painted for this character: the standing (or walking) pose of the facing, moved procedurally.
                var walking = name.Contains("walk");
                if (name.StartsWith("mine") || name.StartsWith("chop")) motion = Motion.Swing;
                else if (name.StartsWith("pickup")) motion = Motion.Dip;
                clip = set.Get(View(walking ? "walk" : "idle")) ?? set.Get(View("idle"));
            }
            if (clip == null || clip.frames.Length == 0) return;
            if (clip.name != Clip)
            {
                // Turning while walking keeps the step's phase (the walks share their timing); anything else starts over.
                if (!(clip.name.Contains("walk") && Clip.Contains("walk"))) _time = 0f;
                Clip = clip.name;
            }
            _time += dt * WalkRate(clip);
            // A swing survives a frame or two without the button (slow frames): it restarts only after a real pause.
            if (motion != Motion.None) { _motionTime += dt; _motionGap = 0f; }
            else if ((_motionGap += dt) > 0.3f) _motionTime = 0f;
            var n = clip.frames.Length;
            float progress;   // 0..1 through the current frame
            if (clip.durations.Length == n)
            {
                var total = 0f;
                foreach (var d in clip.durations) total += d;
                var t = clip.loop ? Mathf.Repeat(_time, total) : Mathf.Min(_time, total - 0.0001f);
                Frame = 0;
                while (Frame < n - 1 && t >= clip.durations[Frame]) { t -= clip.durations[Frame]; Frame++; }
                progress = Mathf.Clamp01(t / Mathf.Max(0.0001f, clip.durations[Frame]));
            }
            else
            {
                var f = _time * clip.fps;
                var i = Mathf.FloorToInt(f);
                Frame = clip.loop ? i % n : Mathf.Min(i, n - 1);
                progress = f - i;
            }
            body.sprite = clip.frames[Frame];
            var eight = clip.name.EndsWith("_" + Dir);
            body.flipX = !eight && Mirrored;   // eight-direction art is painted for every side
            // A little life on top of the painted poses: each step lifts him, and standing he breathes. A painted
            // multi-frame walk already moves, so it gets only a small lift.
            var bob = 0f;
            var breath = 1f;
            var lean = 0f;
            var squash = 1f;
            if (!Feel.ReducedMotion)
            {
                // An eight-direction set has its sway baked into the frames: nothing is added to it.
                if (eight) { }
                else if (clip.name.Contains("walk")) bob = Mathf.Sin(progress * Mathf.PI) * (clip.name.StartsWith("carry") ? 0.02f : 0.035f);
                else if (clip.name.Contains("idle")) breath = 1f + Mathf.Sin(Time.time * 2.2f) * 0.012f;
                if (motion == Motion.Swing)
                {
                    // A wind-up held (leaning back, rising), then a fast strike (leaning in, squashed), as the painted
                    // tailor's key poses did: 0.26 s and 0.14 s.
                    var t = Mathf.Repeat(_motionTime, 0.4f);
                    var side = Dir.Contains("E") ? -1f : Dir.Contains("W") ? 1f : 0f;   // lean toward the facing
                    if (t < 0.26f)
                    {
                        var k = Mathf.SmoothStep(0f, 1f, t / 0.26f);
                        lean = -side * 7f * k;
                        bob = 0.03f * k;
                        squash = 1f + 0.04f * k;
                    }
                    else
                    {
                        var k = Mathf.Sin((t - 0.26f) / 0.14f * Mathf.PI);
                        lean = side * 9f * k;
                        squash = 1f - 0.07f * k;
                    }
                }
                else if (motion == Motion.Dip) squash = 1f - 0.1f * Mathf.Sin(Mathf.Clamp01(_motionTime / 0.3f) * Mathf.PI);
            }
            body.transform.localPosition = new Vector3(0f, bob, 0f);
            body.transform.localRotation = Quaternion.Euler(0f, 0f, lean);
            body.transform.localScale = new Vector3((2f - breath) * (2f - squash), breath * squash, 1f);
            if (Frame < clip.carry.Length)
            {
                var a = clip.carry[Frame];
                var s = transform.localScale;
                CarryAnchor = (Vector2)transform.localPosition + new Vector2((Mirrored ? -a.x : a.x) * Mathf.Abs(s.x), a.y * Mathf.Abs(s.y));
            }
            else CarryAnchor = null;
        }

        private float _motionTime, _motionGap;
        private HoseRig _rig;

        /// <summary>The rubber-hose rig drawing this character, if it has one.</summary>
        public HoseRig Rig => _rig;

        /// <summary>Whether the carried pile is drawn behind the body (on his back, his back turned away).</summary>
        public bool PileBehind => _rig != null ? _rig.PileBehind : Back;

        private static HoseRig.Pose PoseOf(string requested, bool moving)
        {
            if (requested.StartsWith("mine_down")) return HoseRig.Pose.MineDown;
            if (requested.StartsWith("mine_up")) return HoseRig.Pose.MineUp;
            if (requested.StartsWith("mine")) return HoseRig.Pose.Mine;
            if (requested.StartsWith("chop")) return HoseRig.Pose.Chop;
            if (requested.StartsWith("carry")) return HoseRig.Pose.Carry;
            if (requested.StartsWith("pickup")) return HoseRig.Pose.Pickup;
            if (requested.StartsWith("fall")) return HoseRig.Pose.Fall;
            if (requested.StartsWith("climb")) return HoseRig.Pose.Climb;
            return moving && requested.Contains("walk") ? HoseRig.Pose.Walk : HoseRig.Pose.Idle;
        }
        private float _speed;

        /// <summary>
        /// How fast a walk plays: the ground covered over the painted velocity that plants the feet (scaled by the drawn
        /// size), so full stick plays at <see cref="CharacterAnimationSet.walkPlaybackRate"/> and a half push walks with
        /// slower steps. Other clips play at their painted timing.
        /// </summary>
        /// <summary>The playback rate of the current clip (a walk: ground covered over its painted planted-feet speed).</summary>
        public float PlaybackRate { get; private set; } = 1f;

        private float WalkRate(CharacterAnimationSet.Clip clip) => PlaybackRate = ComputeWalkRate(clip);

        private float ComputeWalkRate(CharacterAnimationSet.Clip clip)
        {
            if (!clip.name.Contains("walk")) return 1f;
            var painted = clip.velocity.magnitude * set.walkGroundFactor * Mathf.Abs(transform.lossyScale.y);
            if (painted <= 0f) return set.walkPlaybackRate;
            if (_speed < 0.02f) return set.walkPlaybackRate;
            return Mathf.Clamp(_speed / painted, 0.25f, set.walkPlaybackRate * 1.5f);
        }
    }
}
