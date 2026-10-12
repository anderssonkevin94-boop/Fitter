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
    /// </summary>
    public sealed class SupplierAnimator : MonoBehaviour
    {
        public enum Activity { None, Mine, Chop }

        [SerializeField] private CharacterAnimationSet set;
        [SerializeField] private SpriteRenderer body;

        public void EditorSetup(CharacterAnimationSet animations, SpriteRenderer bodyRenderer)
        {
            set = animations;
            body = bodyRenderer;
        }

        /// <summary>For a routine's figure: what it is doing at its work spot (the player's is read from the game).</summary>
        public Activity Work { get; set; }

        public string Clip { get; private set; } = "";
        public int Frame { get; private set; }
        public bool Back { get; private set; }
        public bool Mirrored { get; private set; }

        /// <summary>Where the carried pile sits, relative to the character's root (null when this frame has none).</summary>
        public Vector2? CarryAnchor { get; private set; }

        private GameRoot _root;
        private PlayerMotor _motor;
        private Transform _rootTransform;
        private Vector3 _lastPos;
        private Vector2 _heading = new Vector2(-1f, -0.5f);   // down-left: the front view, unmirrored
        private float _time, _pickup, _moving;
        private bool _subscribed;

        private void Start()
        {
            _root = GameRoot.Instance;
            _motor = GetComponentInParent<PlayerMotor>();
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
            if (dt > 0f && delta.magnitude / dt > 0.25f)
            {
                _moving = 0.12f;
                _heading = delta;
            }
            else _moving = Mathf.Max(0f, _moving - dt);
            var moving = _moving > 0f;
            Back = _heading.y > 0.02f;
            Mirrored = _heading.x > 0f;

            var name = _motor != null ? PlayerClip(moving, out var advance) : FigureClip(moving, out advance);
            Play(name, advance ? dt : 0f);
        }

        private string View(string baseName) => baseName + (Back ? "_back" : "_front");

        /// <summary>Faces along the cliff in the Mine: +u is up-left (back), −u down-right (front, mirrored).</summary>
        private void FaceAlongCliff(int du)
        {
            Back = du > 0;
            Mirrored = du < 0;
        }

        private string PlayerClip(bool moving, out bool advance)
        {
            advance = true;
            var slab = MineSlabView.Current;
            var inside = slab != null && slab.PlayerInside;
            var carrying = _root != null && _root.Services.Logistics.System.CarryCount > 0;
            if (inside && !moving) FaceAlongCliff(slab.Facing.y);
            _pickup = Mathf.Max(0f, _pickup - Time.deltaTime);

            if (inside && slab.OnLadderPose)
            {
                Back = true;
                Mirrored = false;
                advance = slab.ClimbMoving;
                return "climb_back";
            }
            if (inside && slab.Falling) { Mirrored = false; return "fall_front"; }
            if (_motor.Swinging)
            {
                if (slab != null && slab.CanDigNow && slab.Target.HasValue && (inside || slab.PressDigging || DiggingAtTheMine()))
                {
                    var t = slab.Target.Value;
                    var feet = inside ? slab.Feet : 0;
                    if (inside) FaceAlongCliff(slab.Facing.y);
                    if (t.Row >= feet) { Back = false; return "mine_down_front"; }
                    if (t.Row <= feet - 3) { Back = true; return "mine_up_back"; }
                    return View("mine");
                }
                FaceContextTarget();
                return View("chop");
            }
            if (_pickup > 0f) { Back = false; return "pickup_front"; }
            if (carrying) return View(moving ? "carry_walk" : "carry_idle");
            return View(moving ? "walk" : "idle");
        }

        private static bool DiggingAtTheMine()
        {
            var c = SandboxSceneController.Current;
            return c != null && c.ContextTarget is MineDigSpot;
        }

        /// <summary>Turn toward what is being worked (the tree being chopped).</summary>
        private void FaceContextTarget()
        {
            var c = SandboxSceneController.Current;
            if (c == null || c.ContextTarget == null) return;
            var d = (Vector2)(c.ContextTarget.transform.position - _rootTransform.position);
            if (d.sqrMagnitude < 0.0001f) return;
            Back = d.y > 0.02f;
            Mirrored = d.x > 0f;
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

        private void Play(string name, float dt)
        {
            var clip = set.Get(name) ?? set.Get(View("idle"));
            if (clip == null || clip.frames.Length == 0) return;
            if (clip.name != Clip)
            {
                Clip = clip.name;
                _time = 0f;
            }
            _time += dt;
            var n = clip.frames.Length;
            var i = Mathf.FloorToInt(_time * clip.fps);
            Frame = clip.loop ? i % n : Mathf.Min(i, n - 1);
            if (Feel.ReducedMotion && clip.name.StartsWith("idle")) Frame = 0;   // no idle breathing with reduced motion
            body.sprite = clip.frames[Frame];
            body.flipX = Mirrored;
            if (Frame < clip.carry.Length)
            {
                var a = clip.carry[Frame];
                var s = transform.localScale;
                CarryAnchor = (Vector2)transform.localPosition + new Vector2((Mirrored ? -a.x : a.x) * Mathf.Abs(s.x), a.y * Mathf.Abs(s.y));
            }
            else CarryAnchor = null;
        }
    }
}
