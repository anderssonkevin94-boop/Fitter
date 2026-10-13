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

            if (_motor != null && MineSlabView.Current != null && MineSlabView.Current.Riding) moving = false;   // riding is not walking
            var name = _motor != null ? PlayerClip(moving, out var advance) : FigureClip(moving, out advance);
            Play(name, advance ? dt : 0f);
        }

        private string View(string baseName) => baseName + (Back ? "_back" : "_front");

        /// <summary>Faces a direction on the Mine's grid (x into the cliff, u along it): up the screen is the back view.</summary>
        private void FaceAlongCliff(Vector2Int f)
        {
            var screen = f.x * new Vector2(0.5f, 0.25f) + f.y * new Vector2(-0.5f, 0.25f);
            Back = screen.y > 0f;
            Mirrored = screen.x > 0f;
        }

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
            if (inside && slab.Falling) { Mirrored = false; return "fall_front"; }
            if (_motor.Swinging)
            {
                if (slab != null && slab.CanDigNow && slab.Target.HasValue && (inside || slab.PressDigging || DiggingAtTheMine()))
                {
                    var t = slab.Target.Value;
                    var feet = inside ? slab.Feet : 0;
                    if (inside) FaceAlongCliff(slab.Facing);
                    if (t.Row >= feet) { Back = false; return "mine_down_front"; }
                    if (t.Row <= feet - 3) { Back = true; return "mine_up_back"; }
                    return View("mine");
                }
                FaceContextTarget();
                var c = SandboxSceneController.Current;
                if (c != null && (c.ContextTarget is OutcropSpot || c.ContextTarget is ForestRockSpot)) return View("mine");   // rock above ground
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
            body.flipX = Mirrored;
            // A little life on top of the painted poses: each step lifts him, and standing he breathes.
            var bob = 0f;
            var breath = 1f;
            if (!Feel.ReducedMotion)
            {
                if (clip.name.Contains("walk")) bob = Mathf.Sin(progress * Mathf.PI) * (clip.name.StartsWith("carry") ? 0.02f : 0.035f);
                else if (clip.name.Contains("idle")) breath = 1f + Mathf.Sin(Time.time * 2.2f) * 0.012f;
            }
            body.transform.localPosition = new Vector3(0f, bob, 0f);
            body.transform.localScale = new Vector3(2f - breath, breath, 1f);
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
