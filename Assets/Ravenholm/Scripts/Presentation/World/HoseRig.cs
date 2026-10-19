using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Draws a rubber-hose character (<see cref="HoseCharacterDefinition"/>): the painted body, boots, hands and tool,
    /// with the arms and legs as bendy tubes computed every frame (presentation only).
    ///
    /// Walking: the walk's phase advances with the ground actually covered, so a supporting foot stays planted at any
    /// speed; the stride grows a little with speed. Hoses keep their length by bowing (knees forward, elbows back) and
    /// stretch a little at full stride. The body bounces over each step; the hands swing against the feet. Carrying holds
    /// the hands out in front (the pile sits on them); mining and chopping swing the tool from the near hand.
    ///
    /// The SE, E and NE views are painted; SW, W and NW mirror them. The tubes carry their own outline (a wider dark
    /// tube at the back of the character's sorting group); the painted parts get the game's outline (SpriteOutline).
    /// </summary>
    public sealed class HoseRig : MonoBehaviour
    {
        public enum Pose { Idle, Walk, Carry, Mine, MineDown, MineUp, Chop, Pickup, Fall, Climb }

        [SerializeField] private HoseCharacterDefinition definition;

        public void EditorSetup(HoseCharacterDefinition def) => definition = def;

        public HoseCharacterDefinition Definition => definition;

        /// <summary>The painted view shown (S, SE, E, NE, N) and whether it is mirrored (SW, W, NW).</summary>
        public string ViewName { get; private set; } = "S";
        public bool Mirrored { get; private set; }
        public Pose Current { get; private set; }
        /// <summary>Where the carried pile sits: on his pack's shelf (in the animator's parent space).</summary>
        public Vector2? CarryAnchor { get; private set; }
        /// <summary>Whether the pile is behind his body (his back turned from the camera) or in front of it.</summary>
        public bool PileBehind { get; private set; } = true;
        /// <summary>The top of the head (hat), in the animator's parent space.</summary>
        public float HeadTop { get; private set; } = 1f;
        /// <summary>The walk's phase (0..1 for two steps), and the tool's angle (degrees) while working.</summary>
        public float Phase => _phase;
        public float ToolAngle { get; private set; }
        /// <summary>The walk's ground speed (units/s, smoothed), how far the feet reach from the hips (ground units) and the
        /// longest a leg is stretched this frame (times its rest length): for the spec's tests.</summary>
        public float GroundSpeed { get; private set; }
        public float FootReach { get; private set; }
        public float LegStretch { get; private set; }
        /// <summary>Two steps, in ground units: the spec's walk speed over half its cadence.</summary>
        public float Stride => definition != null ? 2f * definition.walkSpeed / Mathf.Max(0.1f, definition.cadence) : 1f;
        /// <summary>The swing's angle (degrees) from straight up: negative raised behind his head, positive brought forward and down.</summary>
        public float SwingAngle { get; private set; }
        /// <summary>The supporting boot's position this frame (rig space; for tests of planted feet).</summary>
        public Vector2 LeftFoot { get; private set; }
        public Vector2 RightFoot { get; private set; }

        private Transform _rig;
        private SpriteRenderer _body, _bootL, _bootR, _handL, _handR, _tool, _headwear, _head;

        /// <summary>
        /// Body armour worn (modular characters, Astra's standard rig v1, 2026-10-18): an item the definition maps to an
        /// outfit swaps the torso; anything else (or none) is the everyday outfit. Head, skin, facing and pose are kept.
        /// </summary>
        public string ArmourItem { get; set; }

        /// <summary>The headwear set drawn now (modular characters; null: none).</summary>
        public string WornHeadwear { get; private set; }

        /// <summary>
        /// A weapon and a shield carried in the hands (Kevin, 2026-10-18: heroes carry them visibly when equipped): the
        /// weapon in his right hand, the shield in his left, as the Equip Hero portrait holds them. In the mirrored views
        /// the rig's reflection swaps the drawn hands, so the same anatomical hands keep them. A two-handed weapon leaves
        /// no hand for a shield.
        /// </summary>
        public Ravenholm.Content.ItemDefinition HeldWeapon { get; set; }
        public Ravenholm.Content.ItemDefinition HeldShield { get; set; }
        private SpriteRenderer _weaponHeld, _shieldHeld;

        /// <summary>The hand holding the weapon now (tests): his right hand, drawn as the left part when mirrored.</summary>
        public SpriteRenderer WeaponHand => Mirrored ? _handL : _handR;
        public SpriteRenderer WeaponHeldPart => _weaponHeld;
        public SpriteRenderer ShieldHeldPart => _shieldHeld;

        /// <summary>The head identity (modular characters; empty: the definition's own head, the blond).</summary>
        public string HeadId { get; set; }

        /// <summary>
        /// The everyday clothes (civilian_outfit_id, Astra's civilian outfits, 2026-10-19): worn while no mapped armour is;
        /// taking armour off restores them. Empty or unknown: the definition's everyday outfit.
        /// </summary>
        public string CivilianOutfit { get; set; }

        /// <summary>The outfit drawn now (modular characters; null otherwise).</summary>
        public string WornOutfit { get; private set; }

        /// <summary>The skin's base and shadow (modular characters): head, neck, hands and arms; null: the definition's.</summary>
        public Color? SkinBase { get; set; }
        public Color? SkinShadow { get; set; }

        /// <summary>The head (modular characters), riding on the torso at zero offset.</summary>
        public SpriteRenderer HeadAttachment => _head;

        private Material _plainMaterial;
        private MaterialPropertyBlock _skinBlock;
        private static readonly int SkinMaskId = Shader.PropertyToID("_SkinMask");
        private static readonly int SkinBaseId = Shader.PropertyToID("_SkinBase");
        private static readonly int SkinShadowId = Shader.PropertyToID("_SkinShadow");

        /// <summary>
        /// The item worn on the head (Astra's equipment handoff, 2026-10-16): drawn over the body in the views it was fitted
        /// to (the blond citizen's SE, and its SW mirror); none in the others until they are painted.
        /// </summary>
        public string HeadwearItem { get; set; }

        /// <summary>Whether headwear is drawn now (tests).</summary>
        /// <summary>Painted-part anchors for the equipment portrait, available after Drive builds the rig.</summary>
        public SpriteRenderer BodyAttachment => _body;
        public SpriteRenderer LeftHandAttachment => _handL;
        public SpriteRenderer RightHandAttachment => _handR;

        public bool HeadwearShown => _headwear != null && _headwear.enabled;
        private Transform _toolPivot;
        private Vector2 _handLp, _handLv, _handRp, _handRv;
        private float _squash = 1f, _footRollL, _footRollR, _turn, _handScaleL = 1f, _handScaleR = 1f;
        private bool _handNearL, _handNearR;
        private Vector2 _armEndL, _armEndR;
        private string _lastView = "";

        /// <summary>A damped spring toward a target (snaps when far, e.g. after a big jump).</summary>
        private Vector2 Spring(ref Vector2 p, ref Vector2 vel, Vector2 target, float dt)
        {
            if ((p - target).sqrMagnitude > 0.09f || dt <= 0f) { p = target; vel = Vector2.zero; return p; }
            var w = 2f * Mathf.PI * definition.armSpringHz;
            var z = definition.armSpringDamping;
            var steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));   // stable at low frame rates
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                var acc = w * w * (target - p) - 2f * z * w * vel;
                vel += acc * h;
                p += vel * h;
            }
            return p;
        }
        private bool _toolFront;
        private float _lean;
        private Tube _armL, _armR, _legL, _legR, _packBar, _packShelf, _packLip, _sleeveL, _sleeveR;
        private float _phase, _work, _settle;
        private bool _built;

        // ------------------------------------------------------------------ building

        private sealed class Tube
        {
            public MeshRenderer Fill, Outline;
            public Mesh FillMesh, OutlineMesh;
        }

        private void Build()
        {
            if (_built || definition == null) return;
            _built = true;
            _rig = new GameObject("HoseRig").transform;
            _rig.SetParent(transform, false);
            _body = Part("Body");
            _bootL = Part("BootLeft");
            _bootR = Part("BootRight");
            _handL = Part("HandLeft");
            _handR = Part("HandRight");
            _tool = Part("Tool");
            _toolPivot = new GameObject("ToolPivot").transform;
            _toolPivot.SetParent(_rig, false);
            _tool.transform.SetParent(_toolPivot, false);
            _tool.transform.localRotation = Quaternion.Euler(0f, 0f, -45f);   // the handle is painted at 45 degrees: lay it along x
            _armL = MakeTube("ArmLeft");
            _armR = MakeTube("ArmRight");
            _legL = MakeTube("LegLeft");
            _legR = MakeTube("LegRight");
            _sleeveL = MakeTube("SleeveLeft");
            _sleeveR = MakeTube("SleeveRight");
            _packBar = MakeTube("PackBar");
            _packShelf = MakeTube("PackShelf");
            _packLip = MakeTube("PackLip");
        }

        private SpriteRenderer Part(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rig, false);
            return go.AddComponent<SpriteRenderer>();
        }

        private Tube MakeTube(string name)
        {
            var t = new Tube();
            (t.Fill, t.FillMesh) = TubeRenderer(name);
            (t.Outline, t.OutlineMesh) = TubeRenderer(name + "Outline");
            t.Outline.sortingOrder = SpriteOutline.OutlineOrder;
            return t;
        }

        private (MeshRenderer, Mesh) TubeRenderer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rig, false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = definition.limbMaterial;
            return (mr, mesh);
        }

        // ------------------------------------------------------------------ driving

        private static readonly Dictionary<string, (string view, bool mirror)> Views = new Dictionary<string, (string, bool)>
        {
            { "S", ("S", false) }, { "SE", ("SE", false) }, { "E", ("E", false) }, { "NE", ("NE", false) }, { "N", ("N", false) },
            { "SW", ("SE", true) }, { "W", ("E", true) }, { "NW", ("NE", true) },
        };

        /// <summary>The way the painted view faces, on screen (unmirrored), and its depth-squashed forward for limbs.</summary>
        private static Vector2 Facing(string view)
        {
            switch (view)
            {
                case "S": return new Vector2(0f, -1f);
                case "N": return new Vector2(0f, 1f);
                case "E": return new Vector2(1f, 0f);
                case "SE": return new Vector2(0.894f, -0.447f);
                default: return new Vector2(0.894f, 0.447f);   // NE
            }
        }

        /// <summary>
        /// Poses the character for this frame. <paramref name="dir8"/> is the facing (N .. NW), <paramref name="moved"/>
        /// the ground covered since the last frame (units), <paramref name="parentOffset"/> where the rig sits in the
        /// animator's parent space (for the carry anchor and head top).
        /// </summary>
        public void Drive(string dir8, Pose pose, float moved, float dt, Vector2 parentOffset, float parentScale, bool carrying = false)
        {
            Build();
            if (!_built) return;
            if (!Views.TryGetValue(dir8, out var vm)) vm = ("S", false);
            ViewName = vm.view;
            Mirrored = vm.mirror;
            Current = pose;
            _rig.localScale = new Vector3(Mirrored ? -1f : 1f, 1f, 1f);
            var v = definition.Get(ViewName);
            var f = Facing(ViewName);
            var gf = GroundFacing(ViewName);
            var depth = new Vector2(gf.x, gf.y * 0.5f);   // one ground unit forward, as drawn (the camera looks down)
            var side = ViewName == "S" || ViewName == "N";
            var back = ViewName == "N" || ViewName == "NE";

            // --- the walk (Docs/ANIMATION_SPEC.md). Everything is defined on the ground (x east, y north) and projected
            // once (y halved), so cadence, step length and bob are the same in every direction. The phase follows the
            // ground covered: the stride is fixed in ground units, so the cadence scales with speed.
            var walking = pose == Pose.Walk || pose == Pose.Carry && moved > 0.0001f;   // carrying walks like walking (the pile is on his back)
            var screenPerGround = depth.magnitude;                       // this view's forward, as drawn, per ground unit
            var groundMoved = moved / Mathf.Max(0.3f, screenPerGround);
            var stride = Stride;                                         // two steps, ground units
            if (walking) _phase = Mathf.Repeat(_phase + groundMoved / stride, 1f);
            GroundSpeed = dt > 0f ? Mathf.Lerp(GroundSpeed, groundMoved / dt, Mathf.Clamp01(dt * 10f)) : GroundSpeed;
            _settle = Mathf.MoveTowards(_settle, walking ? 1f : 0f, dt * (walking ? 8f : 5f));   // ease into and out of the walk
            var s = Mathf.SmoothStep(0f, 1f, _settle);
            var duty = definition.duty;
            // The stance foot would travel the whole step to stay planted; it reaches at most maxFootReach either side
            // of the hip (his legs are short) and glides the rest.
            var amp = Mathf.Min(duty * stride * 0.5f, definition.maxFootReach) * s;
            float Travel(float t, out float lift, out float roll)
            {
                t = Mathf.Repeat(t, 1f);
                if (t < duty)
                {
                    lift = 0f;
                    var k = t / duty;
                    roll = k < 0.2f ? 1f - k / 0.2f : k > 0.75f ? -(k - 0.75f) / 0.25f : 0f;   // toe up at strike, heel up at push-off
                    return 1f - 2f * k;
                }
                var u = (t - duty) / (1f - duty);
                lift = Mathf.Sin(Mathf.PI * u);
                roll = Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, u));
                return -Mathf.Cos(Mathf.PI * u);
            }
            var trL = Travel(_phase, out var liftL, out var rollL);
            var trR = Travel(_phase + 0.5f, out var liftR, out var rollR);
            FootReach = Mathf.Max(Mathf.Abs(trL), Mathf.Abs(trR)) * amp;   // ground units ahead of or behind the hip
            var footL = depth * (trL * amp) + Vector2.up * (liftL * definition.footLift * s);
            var footR = depth * (trR * amp) + Vector2.up * (liftR * definition.footLift * s);
            // One bob per step: lowest just after each heel strike, highest at passing; squash and stretch with it.
            var wave = -Mathf.Cos(4f * Mathf.PI * (_phase - 0.06f)) * s;   // -1 down .. +1 up
            var bodyY = definition.bounce * wave - definition.walkCrouch * s;   // a slight knee bend while walking
            _squash = 1f + definition.squash * wave;
            // Weight over the supporting foot (left during the first half), a forward lean, a side-to-side tilt.
            var g0 = GroundFacing(ViewName);
            var leftGround = new Vector2(-g0.y, g0.x);
            var leftScreen = new Vector2(leftGround.x, leftGround.y * 0.5f);
            var swayX = leftScreen * (definition.sway * s * Mathf.Cos(2f * Mathf.PI * (_phase - 0.25f)));
            var walkLean = -Mathf.Sign(g0.x) * definition.walkLean * s * Mathf.Abs(g0.x)
                           + definition.walkTilt * s * Mathf.Sin(2f * Mathf.PI * _phase) * (1f - Mathf.Abs(g0.x));
            // Heel-toe roll, seen in the side and diagonal views (head-on the foot pitches toward or away from the camera):
            // toe up (positive) pivots on the heel, heel up (negative) on the toe.
            float Pitch(float roll) => (roll > 0f ? roll * definition.toeUpAngle : roll * definition.heelUpAngle) * s * Mathf.Abs(g0.x);
            _footRollL = Pitch(rollL);
            _footRollR = Pitch(rollR);
            if (ViewName != _lastView) { _turn = 1f; _lastView = ViewName; }   // a quick squash covers the turn
            _turn = Mathf.MoveTowards(_turn, 0f, dt * 8f);

            // --- work and other poses
            Vector2 wristL = v.wristLeft, wristR = v.wristRight;
            var working = pose == Pose.Mine || pose == Pose.MineDown || pose == Pose.MineUp || pose == Pose.Chop;
            _work = working ? _work + dt : 0f;
            Sprite handSpriteL = HandOpen(), handSpriteR = HandOpen();
            _tool.enabled = false;
            CarryAnchor = null;
            var modularArms = definition.Modular;
            var shoulderL = (modularArms ? definition.ModularShoulder(v, true) : v.shoulderLeft) + Vector2.up * bodyY + swayX;
            var shoulderR = (modularArms ? definition.ModularShoulder(v, false) : v.shoulderRight) + Vector2.up * bodyY + swayX;
            _lean = working ? 0f : walkLean;
            if (working)
            {
                // The swing (after references on swing animation: a held wind-up leaning back, a fast strike, a short
                // hold at impact, then recovery). It happens in his own 3D space - forward, his right, up - and is
                // projected into the view, so the tool foreshortens as it points toward or away from the camera instead
                // of spinning flat on the screen. Mining is an overhead swing down and forward; chopping comes
                // diagonally from over his right shoulder into the trunk.
                var g = GroundFacing(ViewName);                       // facing on the ground (x east, y north)
                var right = new Vector2(g.y, -g.x);                   // his right, on the ground
                var t = Mathf.Repeat(_work, SwingCycle);
                Vector2 Project(Vector2 gr, float u) => new Vector2(gr.x, gr.y * 0.5f + u);   // the camera looks down
                var chop = pose == Pose.Chop;
                // The handle's direction from the hand at a moment of the swing (on the ground, and up).
                float tilt = 24f * Mathf.Deg2Rad;
                (Vector2 gr, float u) HandleAt(float deg)
                {
                    var r = deg * Mathf.Deg2Rad;
                    // Chopping (Kevin's playtest, 2026-10-17): a level swing side to side at chest height, into the trunk
                    // in front: the angle runs from straight ahead (0) round to his right and behind (positive).
                    if (chop) return (g * Mathf.Cos(r) + right * Mathf.Sin(r), 0.18f);
                    // Mining: overhead, in the plane of forward and up, tipped a little out to his right so it passes
                    // beside him: the angle from straight up, forward positive.
                    return (g * Mathf.Sin(r) + right * (Mathf.Sin(tilt) * Mathf.Cos(r)), Mathf.Cos(r) * Mathf.Cos(tilt));
                }
                float a, impact = 0f, lean01;
                if (chop)
                {
                    // Cocked back past his right side, the strike sweeps across into the trunk, a beat at impact, then the
                    // pull back to cocked for the next.
                    // Ending out to his left front, not dead ahead, so it reads as a sweep across in every view (dead ahead
                    // it points straight at the camera from the front and shrinks to a stub).
                    const float cocked = 100f, hit = -28f;
                    if (t < 0.06f) { a = cocked; lean01 = -1f; }                                                  // cocked
                    else if (t < 0.14f) { a = Mathf.Lerp(cocked, hit, EaseIn((t - 0.06f) / 0.08f)); lean01 = 1f; } // strike
                    else if (t < 0.22f) { a = hit; impact = 1f - (t - 0.14f) / 0.08f; lean01 = 1f; }               // impact
                    else
                    {
                        var k = Mathf.SmoothStep(0f, 1f, (t - 0.22f) / 0.24f);                                     // pull back
                        a = Mathf.Lerp(hit, cocked, k);
                        lean01 = Mathf.Lerp(1f, -1f, k);
                    }
                }
                else
                {
                    // The handle's angle from straight up, forward positive: resting out at his side (125), lifted up
                    // behind his head (-35), then over the top and down in front: to the wall (95), the floor (135) or a
                    // block above (55). Passing over the top (0) makes it an overhead swing, not a pendulum.
                    float rest = 125f, raised = -35f;
                    var end = pose == Pose.MineDown ? 135f : pose == Pose.MineUp ? 55f : 95f;
                    if (t < 0.2f) a = Mathf.Lerp(rest, raised, EaseOut(t / 0.2f));                    // wind-up
                    else if (t < 0.27f) a = Mathf.Lerp(raised, end, EaseIn((t - 0.2f) / 0.07f));       // strike
                    else if (t < 0.34f) { a = end; impact = 1f - (t - 0.27f) / 0.07f; }                // hold at impact
                    else a = Mathf.Lerp(end, rest, Mathf.SmoothStep(0f, 1f, (t - 0.34f) / 0.12f));     // recover
                    lean01 = t < 0.2f ? -EaseOut(t / 0.2f) : t < 0.34f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.34f) / 0.12f);
                }
                SwingAngle = a;
                var (ground, up) = HandleAt(a);
                var handle = Project(ground, up);
                var reach = 0.26f;
                // The swinging shoulder sits out at his right side (on the ground), so the arc clears his body.
                var pivot = shoulderR + Vector2.up * (chop ? -0.04f : 0.02f) + Project(right * 0.06f, 0f);
                wristR = pivot + handle * reach;                       // one hand on the tool, at its grip
                // The other hand stays at his side, bracing a touch against the swing (no reach across his body).
                wristL = v.wristLeft + Vector2.up * bodyY + swayX + Project(-g * 0.03f * lean01, 0f);
                handSpriteR = HandGrip();
                _tool.enabled = true;
                _tool.sprite = chop ? definition.axe : definition.pickaxe;
                var len = handle.magnitude;
                var ang = len > 0.0001f ? Mathf.Atan2(handle.y, handle.x) * Mathf.Rad2Deg : -90f;
                ToolAngle = ang;
                _toolPivot.localPosition = wristR;
                _toolPivot.localRotation = Quaternion.Euler(0f, 0f, ang);
                // Held near the end of the handle, not its middle (Kevin, 2026-10-17).
                _tool.transform.localPosition = new Vector3(chop ? definition.axeGrip : definition.pickaxeGrip, 0f, 0f);
                // The blade leads the swing (Kevin, 2026-10-17): it is painted on the handle's right; turned over when the
                // way the strike goes (toward the trunk or the rock) is on the handle's left on screen.
                var (g2, u2) = HandleAt(a + (chop ? -2f : 2f));
                var ahead = Project(g2, u2) - handle;
                var bladeSide = new Vector2(handle.y, -handle.x);   // the handle's right on screen
                var flip = Vector2.Dot(ahead, bladeSide) < 0f ? -1f : 1f;
                _toolPivot.localScale = new Vector3(Mathf.Max(0.25f, len), flip, 1f);   // foreshortened along the handle
                // In front of his body when the tool head is nearer the camera than his middle (south on the ground),
                // or out to his near side.
                var headGround = ground + right * 0.25f;
                _toolFront = headGround.y < 0.05f;
                // The body leans back on the wind-up and into the strike, and dips at impact.
                _lean = lean01 * 6f * -Mathf.Sign(f.x == 0f ? 0f : f.x);
                bodyY += lean01 > 0f ? -0.015f * lean01 : 0.01f * -lean01;
                if (impact > 0f && !Feel.ReducedMotion) bodyY += Mathf.Sin(impact * 40f) * 0.004f * impact;
            }
            else if (pose == Pose.Pickup)
            {
                bodyY -= 0.04f;
                wristL = v.wristLeft + depth * 0.1f + Vector2.down * 0.03f;
                wristR = v.wristRight + depth * 0.1f + Vector2.down * 0.03f;
            }
            else if (pose == Pose.Fall || pose == Pose.Climb)
            {
                wristL = v.shoulderLeft + new Vector2(-0.05f, 0.14f);
                wristR = v.shoulderRight + new Vector2(0.05f, 0.14f);
            }
            else
            {
                // Big arm arcs against the legs (rising at the ends of the swing), followed through springs so they
                // trail the body and overshoot a little. Standing, a slow breath.
                var breath = Mathf.Sin(Time.time * 2.2f) * 0.006f * (1f - s);
                var swingL = -Mathf.Cos(2f * Mathf.PI * (_phase - 0.04f)) * s;
                var swingR = -swingL;
                Vector2 Arm(Vector2 rest, float sw) => rest + swayX + Vector2.up * (bodyY + breath)
                    + depth * (sw * definition.armSwing) + Vector2.up * (sw * sw * definition.armSwing * 0.35f);
                wristL = Spring(ref _handLp, ref _handLv, Arm(v.wristLeft, swingL), dt);
                wristR = Spring(ref _handRp, ref _handRv, Arm(v.wristRight, swingR), dt);
                if (side)
                {
                    // Head-on the swing goes toward and away from the camera: a hand coming forward moves in a touch
                    // toward his middle and passes in front of his body when it comes toward the camera (spec section 3).
                    var centre = (v.hipLeft.x + v.hipRight.x) * 0.5f;
                    var toward = back ? -1f : 1f;
                    wristL.x += (centre - wristL.x) * 0.08f * Mathf.Max(0f, swingL);
                    wristR.x += (centre - wristR.x) * 0.08f * Mathf.Max(0f, swingR);
                    _handNearL = swingL * toward > 0.15f;
                    _handNearR = swingR * toward > 0.15f;
                }
                bodyY += breath;
            }
            if (working || pose == Pose.Pickup || pose == Pose.Fall || pose == Pose.Climb)
            {
                _handLp = wristL; _handRp = wristR; _handLv = _handRv = Vector2.zero;   // posed directly; springs pick up from here
            }

            // --- place the painted parts
            _body.sprite = v.body;
            var modular = definition.Modular;
            if (modular)
            {
                // A head over a worn outfit, on one canvas: the head rides on the torso at zero offset (its head mount is
                // every torso's), so it shares the torso's lean, sway, bob, squash and mirror; no per-outfit nudges.
                WornOutfit = definition.OutfitForArmour(ArmourItem)
                             ?? (!string.IsNullOrEmpty(CivilianOutfit) && definition.TorsoFor(CivilianOutfit, ViewName).torso != null ? CivilianOutfit : definition.defaultOutfit);
                var (torso, torsoMask) = definition.TorsoFor(WornOutfit, ViewName);
                _body.sprite = torso;
                if (_head == null)
                {
                    _head = new GameObject("Head").AddComponent<SpriteRenderer>();
                    _head.transform.SetParent(_body.transform, false);
                }
                var (headSprite, headMask) = definition.HeadFor(HeadId, v);
                _head.sprite = headSprite;
                Skin(_body, torsoMask);
                Skin(_head, headMask);
            }
            // Headwear rides on the body (its lean, sway and squash; mirrored with the rig), on the body's own canvas.
            // Modular (v2): a helmet set over the head, on its canvas at zero offset, riding on it.
            WornHeadwear = modular ? definition.HeadwearForItem(HeadwearItem) : null;
            var hat = modular ? definition.HelmetFor(WornHeadwear, ViewName) : definition.HeadwearFor(ViewName, HeadwearItem);
            if (hat != null && _headwear == null)
            {
                _headwear = new GameObject("Headwear").AddComponent<SpriteRenderer>();
                _headwear.transform.SetParent(modular && _head != null ? _head.transform : _body.transform, false);
                _headwear.sharedMaterial = _body.sharedMaterial;
            }
            if (_headwear != null)
            {
                _headwear.sprite = hat;
                _headwear.enabled = hat != null;
                // Each head's own small fit for the helmet in this view (pixels on the canvas; mirrored with the rig).
                var fit = modular ? definition.HelmetOffsetPx(HeadId, WornHeadwear, ViewName) : Vector2.zero;
                var ppu = hat != null ? hat.pixelsPerUnit : 340f;
                _headwear.transform.localPosition = new Vector3(fit.x / ppu, -fit.y / ppu, 0f);
            }
            // Lean about his hips (side views; head-on a lean would point at the camera).
            var hipsAt = (v.hipLeft + v.hipRight) * 0.5f;
            var leanRot = Quaternion.Euler(0f, 0f, _lean);
            _body.transform.localRotation = leanRot;
            _body.transform.localPosition = (Vector3)(hipsAt - (Vector2)(leanRot * hipsAt)) + new Vector3(swayX.x, bodyY + swayX.y, 0f);
            var turnSquash = 1f - 0.08f * Mathf.Sin(_turn * Mathf.PI);
            _body.transform.localScale = new Vector3((2f - _squash) * turnSquash, _squash, 1f);
            _bootL.sprite = v.bootLeft;
            _bootR.sprite = v.bootRight;
            var ankleL = PlaceBoot(_bootL, v.ankleLeft, footL, _footRollL, gf.x);
            var ankleR = PlaceBoot(_bootR, v.ankleRight, footR, _footRollR, gf.x);
            LeftFoot = ankleL;
            RightFoot = ankleR;
            // Holding a weapon or a shield: those hands grip (not while working: the tool's own grip).
            var shieldOn = HeldShield != null && !(HeldWeapon != null && HeldWeapon.TwoHanded);
            var gripR = !working && (Mirrored ? shieldOn : HeldWeapon != null);
            var gripL = !working && (Mirrored ? HeldWeapon != null : shieldOn);
            if (gripR) handSpriteR = HandGrip();
            if (gripL) handSpriteL = HandGrip();
            _handL.sprite = handSpriteL;
            _handR.sprite = handSpriteR;
            if (modular)
            {
                Skin(_handL, definition.HandMaskFor(handSpriteL));
                Skin(_handR, definition.HandMaskFor(handSpriteR));
            }
            _handL.transform.localPosition = wristL;
            _handR.transform.localPosition = wristR;
            _handL.transform.localScale = Vector3.one * _handScaleL;
            _handR.transform.localScale = Vector3.one * _handScaleR;
            _handScaleL = _handScaleR = 1f;
            // Which way each hand's thumb points (the spec's rule; the art is a right hand, its thumb to screen right):
            // relaxed, head-on, both thumbs point toward his centre line; in the side views the hands show their back
            // with the thumb pointing the way he faces (the rig's mirroring turns that for SW, W, NW).
            var centreX = (v.hipLeft.x + v.hipRight.x) * 0.5f;
            if (handSpriteL == HandOpen() && side)
            {
                _handL.flipX = wristL.x > centreX;
                _handR.flipX = wristR.x > centreX;
            }
            else if (handSpriteL == HandOpen())
            {
                _handL.flipX = false;
                _handR.flipX = false;
            }
            else
            {
                _handL.flipX = true;   // gripping: the left hand is the right hand's mirror
                _handR.flipX = false;
            }
            if (gripL) _handL.flipX = true;
            if (gripR) _handR.flipX = false;

            // --- the hoses
            var outline = OutlineWidth();
            var hipL = v.hipLeft + Vector2.up * bodyY + swayX;
            var hipR = v.hipRight + Vector2.up * bodyY + swayX;
            var legLen = Vector2.Distance(v.hipLeft, v.ankleLeft);
            var armLen = Vector2.Distance(v.shoulderLeft, v.wristLeft);
            var kneeDir = side ? Vector2.zero : f;   // knees bow forward; head-on they bow slightly outward
            LegStretch = Mathf.Max(Vector2.Distance(hipL, ankleL), Vector2.Distance(hipR, ankleR)) / Mathf.Max(0.001f, legLen);
            DrawTube(_legL, hipL, ankleL, legLen, side ? Vector2.right * Mathf.Sign(v.hipLeft.x) : kneeDir, definition.legWidth, definition.legWidthEnd, definition.legColor, outline);
            DrawTube(_legR, hipR, ankleR, legLen, side ? Vector2.right * Mathf.Sign(v.hipRight.x) : kneeDir, definition.legWidth, definition.legWidthEnd, definition.legColor, outline);
            var elbowDir = side ? Vector2.zero : -f;
            // Head-on an arm swinging toward or away from the camera foreshortens: drawn straight, never bowing out sideways.
            var restArmL = side ? Vector2.Distance(shoulderL, wristL) : armLen;
            var restArmR = side ? Vector2.Distance(shoulderR, wristR) : armLen;
            // In the diagonal and profile views his left arm is the far one: smaller with distance (arm, sleeve, hand).
            var far = side ? 1f : definition.farLimbScale;
            // On a modular torso the arm's top is under its sleeve cap: start it a little way in, so its rounded root (and
            // that root's outline) never peeks past the sleeve's edge.
            var hidden = modular ? definition.sleeveCapRadius * 0.45f : 0f;
            DrawTube(_armL, shoulderL, wristL, restArmL, side ? Vector2.zero : elbowDir, definition.armWidth * far, definition.armWidthEnd * far, ArmColor, outline, hidden);
            _armEndL = _path[_path.Count - 1] - _path[_path.Count - 2];
            DrawSleeve(_sleeveL, outline, far);
            if (!side) _handL.transform.localScale = Vector3.one * far;
            DrawTube(_armR, shoulderR, wristR, restArmR, side ? Vector2.zero : elbowDir, definition.armWidth, definition.armWidthEnd, ArmColor, outline, hidden);
            _armEndR = _path[_path.Count - 1] - _path[_path.Count - 2];
            DrawSleeve(_sleeveR, outline);
            // Relaxed hands follow the end of their arm (the art hangs straight down), so the wrist never kinks.
            if (!working)
            {
                float Align(Vector2 end) => end.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(end.y, end.x) * Mathf.Rad2Deg + 90f;
                _handL.transform.localRotation = Quaternion.Euler(0f, 0f, Align(_armEndL));
                _handR.transform.localRotation = Quaternion.Euler(0f, 0f, Align(_armEndR));
            }
            else
            {
                _handL.transform.localRotation = Quaternion.identity;
                _handR.transform.localRotation = Quaternion.identity;
            }

            // --- the pack: an L on his back, a bar from the hips to the shoulders and a shelf sticking out behind
            // at the bottom. The carried pile rests on the shelf, so his hands stay free to work.
            var hipMid = (hipL + hipR) * 0.5f;
            var shoulderMid = (shoulderL + shoulderR) * 0.5f;
            var backward = -depth;   // behind him, as drawn
            var barBottom = new Vector2(hipMid.x, hipMid.y - 0.03f) + backward * 0.21f;   // just outside his back in profile
            var barTop = new Vector2(barBottom.x, shoulderMid.y + 0.03f);
            var shelfEnd = barBottom + backward * definition.shelfDepth;
            DrawTube(_packBar, barBottom, barTop, 0f, Vector2.zero, definition.packWidth, definition.packWidth, definition.packColor, outline);
            DrawTube(_packShelf, barBottom, shelfEnd, 0f, Vector2.zero, definition.packWidth, definition.packWidth, definition.packColor, outline);
            // The shelf's crosspiece across his back (seen end-on in profile, full width from the front or back), so
            // the shelf reads as a little platform from every side.
            var lateral = new Vector2(-depth.y * 2f, depth.x * 0.5f) * (1f - 0.85f * Mathf.Abs(f.x));   // end-on in profile
            var seat = barBottom + backward * (definition.shelfDepth * 0.5f);
            DrawTube(_packLip, seat - lateral * 0.1f, seat + lateral * 0.1f, 0f, Vector2.zero, definition.packWidth, definition.packWidth, definition.packColor, outline);
            seat += Vector2.up * (definition.packWidth * 0.5f);
            foreach (var part in new[] { _packBar, _packShelf, _packLip }) { part.Fill.enabled = carrying; part.Outline.enabled &= carrying; }   // worn only while carrying
            CarryAnchor = parentOffset + new Vector2(Mirrored ? -seat.x : seat.x, seat.y) * parentScale;
            PileBehind = !back;   // his back faces the camera only in the N and NE views (and their mirrors)

            // --- draw order inside the character's sorting group (a pile in front sits above, from 10; behind, below)
            var armsFront = false;
            Order(_packBar, back ? 7 : -3);
            Order(_packShelf, back ? 7 : -3);
            Order(_packLip, back ? 7 : -3);
            if (side)
            {
                // Head-on the arms hang beside his body, behind it, so the apron straps lie over the sleeves as painted
                // (the hands rest just outside his outline, so they show). A hand swinging toward the camera passes
                // in front of the body.
                Order(_armL, 0); Order(_armR, 0);
                _handL.sortingOrder = _handNearL ? 9 : 1; _handR.sortingOrder = _handNearR ? 9 : 1;
                if (modular)
                {
                    // A modular torso: both arms hang in front of it, coming out from under its sleeves (the caps, below).
                    Order(_armL, 7); Order(_armR, 7);
                    _handL.sortingOrder = 8; _handR.sortingOrder = 8;
                }
                _handNearL = _handNearR = false;
                Order(_legL, 2); Order(_legR, 2);
                _bootL.sortingOrder = 3; _bootR.sortingOrder = 3;
            }
            else
            {
                // Painted SE/E/NE face right: his right side is nearer the camera.
                Order(_armL, armsFront ? 7 : 0); _handL.sortingOrder = armsFront ? 8 : 1;
                Order(_legL, 2); _bootL.sortingOrder = 3;
                Order(_legR, 4); _bootR.sortingOrder = 5;
                // His right arm is the near one in every painted diagonal and profile (SE, E, NE): always in front of
                // the body, back three-quarter view included (the far arm is the one behind).
                Order(_armR, 7); _handR.sortingOrder = 9;
            }
            _body.sortingOrder = 6;
            if (_headwear != null) _headwear.sortingOrder = 7;   // over the head; the hands (8, 9) still pass in front
            _tool.sortingOrder = 8;
            Order(_sleeveL, _armL.Fill.sortingOrder + 1);
            Order(_sleeveR, _armR.Fill.sortingOrder + 1);
            if (working)
            {
                // The swing shows: in front of him, except while the tool is raised behind his head. Seen from behind,
                // the other way round: the wind-up comes toward the camera, the strike goes away from it.
                // Only the swinging arm and the tool change sides; the free hand keeps its place at his side.
                var front = _toolFront;
                Order(_armR, front ? 7 : 0);
                Order(_sleeveR, front ? 8 : 1);
                _tool.sortingOrder = front ? 8 : -1;
                _handR.sortingOrder = front ? 9 : 1;
            }

            if (_head != null)
            {
                // The head draws over its torso and under whatever passes in front of the body (7 and up move one up).
                foreach (var r in _rig.GetComponentsInChildren<Renderer>())
                    if (r != _head && r != _body && r.sortingOrder >= 7 && r.sortingOrder < 100) r.sortingOrder++;
                _head.sortingOrder = 7;
                _head.enabled = modular;
            }
            ShoulderCaps(modular, v);
            Held(ref _weaponHeld, "WeaponHeld", working ? null : HeldWeapon, Mirrored ? _handL : _handR, false);
            Held(ref _shieldHeld, "ShieldHeld", working || !shieldOn ? null : HeldShield, Mirrored ? _handR : _handL, true);
            var topSprite = modular && v.head != null ? v.head : v.body;
            var top = topSprite != null ? topSprite.bounds.max.y : 1f;
            HeadTop = parentOffset.y + (bodyY + top) * parentScale;
        }

        public const float SwingCycle = 0.46f;

        /// <summary>
        /// Draws an item held in a hand, sized and angled as the Equip Hero portrait holds it (the item's display art, its
        /// grip at the hand), just under the hand's fingers.
        /// </summary>
        private void Held(ref SpriteRenderer part, string name, Ravenholm.Content.ItemDefinition item, SpriteRenderer hand, bool shield)
        {
            if (item == null) { if (part != null) part.enabled = false; return; }
            if (part == null)
            {
                part = new GameObject(name).AddComponent<SpriteRenderer>();
                part.sharedMaterial = _bootL.sharedMaterial;
            }
            if (part.transform.parent != hand.transform) part.transform.SetParent(hand.transform, false);
            part.enabled = true;
            part.sprite = WorkshopUi.ItemSprite(item);
            if (part.sprite == null) { part.enabled = false; return; }
            float height;
            if (shield) height = .46f;
            else if (item.WeaponTag == Ravenholm.Content.WeaponTag.LightBlade) height = .34f;
            else if (item.WeaponTag == Ravenholm.Content.WeaponTag.Staff) height = .92f;
            else if (item.WeaponTag == Ravenholm.Content.WeaponTag.Bow) height = .72f;
            else height = item.TwoHanded ? .78f : .61f;
            var hs = hand.transform.localScale.x;
            var scale = height / Mathf.Max(.01f, part.sprite.bounds.size.y) / Mathf.Max(.01f, hs);
            part.transform.localScale = new Vector3(hand.flipX ? -scale : scale, scale, 1f);
            // A heavy two-handed weapon (a maul) hangs head-down from the hand: held upright its head covered his face.
            var hangs = !shield && item.TwoHanded && item.WeaponTag != Ravenholm.Content.WeaponTag.Staff && item.WeaponTag != Ravenholm.Content.WeaponTag.Bow;
            if (hangs) height *= 0.75f;   // and hangs short of the ground
            var angle = shield ? 0f : item.WeaponTag == Ravenholm.Content.WeaponTag.Bow ? 8f : hangs ? 140f : 24f;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, hand.flipX ? -angle : angle);
            // Upright blades and staves are gripped near the base; bows and shields at their middle.
            var gripY = shield || item.WeaponTag == Ravenholm.Content.WeaponTag.Bow ? .5f : .18f;
            part.transform.localPosition = part.transform.localRotation * new Vector3(0f, (.5f - gripY) * height / Mathf.Max(.01f, hs), 0f);
            part.sortingOrder = hand.sortingOrder - 1;
        }

        /// <summary>The arms' colour: the skin's base on modular characters (the neck and hands match), else the definition's.</summary>
        private Color ArmColor => definition.Modular ? SkinBase ?? definition.SkinOf(HeadId).skin : definition.armColor;

        /// <summary>Tints a part's skin-masked pixels with this character's skin (nothing else changes); no mask: as painted.</summary>
        private void Skin(SpriteRenderer sr, Texture2D mask)
        {
            if (_plainMaterial == null) _plainMaterial = _bootL.sharedMaterial;
            if (mask == null || definition.skinMaterial == null)
            {
                if (sr.sharedMaterial != _plainMaterial) { sr.sharedMaterial = _plainMaterial; sr.SetPropertyBlock(null); }
                return;
            }
            if (sr.sharedMaterial != definition.skinMaterial) sr.sharedMaterial = definition.skinMaterial;
            _skinBlock ??= new MaterialPropertyBlock();
            sr.GetPropertyBlock(_skinBlock);
            _skinBlock.SetTexture(SkinMaskId, mask);
            var (headSkin, headShadow) = definition.SkinOf(HeadId);
            _skinBlock.SetColor(SkinBaseId, SkinBase ?? headSkin);
            _skinBlock.SetColor(SkinShadowId, SkinShadow ?? headShadow);
            sr.SetPropertyBlock(_skinBlock);
        }

        /// <summary>
        /// Places a boot moved by <paramref name="offset"/> and pitched by <paramref name="angle"/> (degrees, toe up
        /// positive) about its heel (toe up) or toe (heel up) on the sole, and returns where its ankle ends up (the leg's
        /// end). <paramref name="forwardX"/> is the view's forward on screen (1 profile, 0.7 diagonal, 0 head-on).
        /// </summary>
        private Vector2 PlaceBoot(SpriteRenderer boot, Vector2 ankle, Vector2 offset, float angle, float forwardX)
        {
            var sole = ankle.y - definition.ankleHeight;
            var pivot = angle >= 0f
                ? new Vector2(ankle.x - definition.heelLength * forwardX, sole)
                : new Vector2(ankle.x + definition.toeLength * forwardX, sole);
            var rot = Quaternion.Euler(0f, 0f, angle);
            // The boot's art is placed from the character's ground point (the rig's origin): rotate it about the pivot.
            boot.transform.localRotation = rot;
            boot.transform.localPosition = (Vector3)(offset + pivot - (Vector2)(rot * pivot));
            return offset + pivot + (Vector2)(rot * (ankle - pivot));
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
        private static float EaseIn(float x) => x * x;

        /// <summary>The way a painted view faces, on the ground (x east, y north; shown with y halved).</summary>
        private static Vector2 GroundFacing(string view)
        {
            switch (view)
            {
                case "S": return new Vector2(0f, -1f);
                case "N": return new Vector2(0f, 1f);
                case "E": return new Vector2(1f, 0f);
                case "SE": return new Vector2(0.7071f, -0.7071f);
                default: return new Vector2(0.7071f, 0.7071f);   // NE
            }
        }

        /// <summary>Relaxed hands: thumb edge on from the front and back, the back of the hand from the side.</summary>
        private Sprite HandOpen() => ViewName == "S" || ViewName == "N" ? definition.handOpenSide : definition.handOpenBack;
        private Sprite HandGrip() => ViewName == "E" ? definition.handGripSide : ViewName == "N" || ViewName == "NE" ? definition.handGripBack : definition.handGripFront;

        private static void Order(Tube t, int order) => t.Fill.sortingOrder = order;

        /// <summary>The game's outline thickness, in this rig's units.</summary>
        private float OutlineWidth()
        {
            var cam = SandboxSceneController.Current != null ? SandboxSceneController.Current.WorldCamera : Camera.main;
            var settings = GameRoot.Instance != null ? GameRoot.Instance.Config.SandboxSettings.outlines : null;
            if (cam == null || settings == null || cam.pixelHeight <= 0) return 0f;
            var worldPerPixel = 2f * cam.orthographicSize / cam.pixelHeight;
            var scale = Mathf.Abs(_rig.lossyScale.y);
            return settings.PixelsFor(cam.pixelHeight) * worldPerPixel / Mathf.Max(0.0001f, scale);
        }

        // ------------------------------------------------------------------ tubes

        private readonly List<Vector2> _path = new List<Vector2>(16);
        private readonly List<Vector3> _verts = new List<Vector3>(64);
        private readonly List<Color32> _colors = new List<Color32>(64);
        private readonly List<int> _tris = new List<int>(192);

        /// <summary>
        /// A hose from a to b: as long as it is at rest it bows sideways (toward <paramref name="bowDir"/>) to keep its
        /// length; pulled further it stretches straight.
        /// </summary>
        private void DrawTube(Tube t, Vector2 a, Vector2 b, float restLength, Vector2 bowDir, float width, float widthEnd, Color color, float outline, float trim = 0f)
        {
            var d = Vector2.Distance(a, b);
            var along = d > 0.0001f ? (b - a) / d : Vector2.down;
            var normal = new Vector2(-along.y, along.x);
            if (bowDir.sqrMagnitude > 0.0001f && Vector2.Dot(normal, bowDir) < 0f) normal = -normal;
            // (A rest length of 0 draws it straight: the pack.)
            // The bow grows smoothly as the ends come together (no snap as the hose straightens); a stretched hose keeps a
            // slight curve, the rubber-hose look.
            var compress = restLength > 0f ? Mathf.Clamp01(1f - d / restLength) : 0f;
            var bow = bowDir.sqrMagnitude > 0.0001f ? restLength * (definition.bow * 0.9f * compress + 0.07f) : restLength * 0.04f;
            var c1 = a + (b - a) / 3f + normal * bow * 1.33f;
            var c2 = a + (b - a) * (2f / 3f) + normal * bow * 1.33f;
            _path.Clear();
            const int n = 10;
            for (var i = 0; i <= n; i++)
            {
                var s = i / (float)n;
                var u = 1f - s;
                _path.Add(u * u * u * a + 3f * u * u * s * c1 + 3f * u * s * s * c2 + s * s * s * b);
            }
            TrimStart(trim);
            Fill(t.FillMesh, width, widthEnd, color);
            Fill(t.OutlineMesh, width + 2f * outline, widthEnd + 2f * outline, GameRoot.Instance != null && GameRoot.Instance.Config.SandboxSettings.outlines != null
                ? GameRoot.Instance.Config.SandboxSettings.outlines.color : Color.black);
            t.Outline.enabled = outline > 0f;
        }

        private SpriteRenderer _caps;
        private static readonly int CapAId = Shader.PropertyToID("_CapA");
        private static readonly int CapBId = Shader.PropertyToID("_CapB");

        /// <summary>
        /// The shoulder caps (Kevin, 2026-10-19: the arms looked stuck on). An arm in front of a modular torso is drawn from
        /// the shoulder, and then the torso again over it, cut to a circle round that shoulder: the sleeve covers the arm's
        /// top and the arm comes out from under it, as Astra's rig asks ("the shoulder cap remains above the arm root").
        /// Drawn under the hands.
        /// </summary>
        private void ShoulderCaps(bool modular, HoseCharacterDefinition.View v)
        {
            var torso = _body.sprite;
            var frontL = modular && _armL.Fill.sortingOrder > _body.sortingOrder;
            var frontR = modular && _armR.Fill.sortingOrder > _body.sortingOrder;
            if (!(frontL || frontR) || torso == null || definition.skinMaterial == null)
            {
                if (_caps != null) _caps.enabled = false;
                return;
            }
            if (_caps == null)
            {
                _caps = new GameObject("ShoulderCaps").AddComponent<SpriteRenderer>();
                _caps.transform.SetParent(_body.transform, false);
            }
            _caps.enabled = true;
            _caps.sprite = torso;
            _caps.sharedMaterial = definition.skinMaterial;
            var top = Mathf.Max(frontL ? _armL.Fill.sortingOrder : 0, frontR ? _armR.Fill.sortingOrder : 0);
            _caps.sortingOrder = top + 1;
            foreach (var hand in new[] { _handL, _handR })
                if (hand.sortingOrder > _body.sortingOrder && hand.sortingOrder <= _caps.sortingOrder) hand.sortingOrder = _caps.sortingOrder + 1;
            // Circles in the sprite's own pixels (from its bottom-left), round each front arm's shoulder.
            var ppu = torso.pixelsPerUnit;
            var r = definition.sleeveCapRadius * ppu;
            Vector4 Circle(Vector2 shoulder, bool on) => new Vector4(torso.pivot.x + shoulder.x * ppu, torso.pivot.y + shoulder.y * ppu, r, on ? 1f : 0f);
            _skinBlock ??= new MaterialPropertyBlock();
            _body.GetPropertyBlock(_skinBlock);   // the torso's skin and mask
            _skinBlock.SetVector(CapAId, Circle(definition.ModularShoulder(v, true), frontL));
            _skinBlock.SetVector(CapBId, Circle(definition.ModularShoulder(v, false), frontR));
            _caps.SetPropertyBlock(_skinBlock);
            _skinBlock.SetVector(CapAId, Vector4.zero);
            _skinBlock.SetVector(CapBId, Vector4.zero);
        }

        /// <summary>
        /// Drops the path's first <paramref name="length"/> units (an arm in front of a modular torso starts at its sleeve's
        /// hem, Kevin 2026-10-19: started at the shoulder its root showed over the cap; hidden behind the torso, the arm
        /// came out of the body's side halfway down).
        /// </summary>
        private void TrimStart(float length)
        {
            if (length <= 0f || _path.Count < 3) return;
            var left = length;
            while (_path.Count > 2)
            {
                var seg = Vector2.Distance(_path[0], _path[1]);
                if (seg > left) { _path[0] = Vector2.Lerp(_path[0], _path[1], left / seg); return; }
                left -= seg;
                _path.RemoveAt(0);
            }
        }

        /// <summary>The sleeve: the first stretch of the arm just drawn (its path is still in _path), wider, in shirt cream.</summary>
        private void DrawSleeve(Tube t, float outline, float scale = 1f)
        {
            if (!definition.sleeves)
            {
                t.Fill.enabled = t.Outline.enabled = false;
                return;
            }
            t.Fill.enabled = true;
            var keep = Mathf.Clamp(Mathf.RoundToInt((_path.Count - 1) * definition.sleeveLength) + 1, 2, _path.Count);
            _path.RemoveRange(keep, _path.Count - keep);
            Fill(t.FillMesh, definition.sleeveWidth * scale, definition.sleeveWidthEnd * scale, definition.sleeveColor);
            Fill(t.OutlineMesh, definition.sleeveWidth * scale + 2f * outline, definition.sleeveWidthEnd * scale + 2f * outline,
                GameRoot.Instance != null && GameRoot.Instance.Config.SandboxSettings.outlines != null ? GameRoot.Instance.Config.SandboxSettings.outlines.color : Color.black);
            t.Outline.enabled = outline > 0f;
        }

        /// <summary>A strip along the path, tapering from one width to the other, with round caps at both ends.</summary>
        private void Fill(Mesh mesh, float width, float widthEnd, Color color)
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
            // Mesh colours are not converted for the linear colour space by Unity (sprite textures are): convert here.
            Color32 c = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            for (var i = 0; i < _path.Count; i++)
            {
                var h = Mathf.Lerp(width, widthEnd, i / (float)(_path.Count - 1)) * 0.5f;
                var prev = _path[Mathf.Max(0, i - 1)];
                var next = _path[Mathf.Min(_path.Count - 1, i + 1)];
                var tan = (next - prev).normalized;
                var nrm = new Vector2(-tan.y, tan.x) * h;
                _verts.Add(_path[i] + nrm);
                _verts.Add(_path[i] - nrm);
                _colors.Add(c);
                _colors.Add(c);
                if (i > 0)
                {
                    var k = i * 2;
                    _tris.Add(k - 2); _tris.Add(k); _tris.Add(k - 1);
                    _tris.Add(k - 1); _tris.Add(k); _tris.Add(k + 1);
                }
            }
            Cap(_path[0], width * 0.5f, c);
            Cap(_path[_path.Count - 1], widthEnd * 0.5f, c);
            mesh.Clear();
            mesh.SetVertices(_verts);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
        }

        private void Cap(Vector2 centre, float r, Color32 c)
        {
            const int seg = 12;
            var start = _verts.Count;
            _verts.Add(centre);
            _colors.Add(c);
            for (var i = 0; i <= seg; i++)
            {
                var ang = i * Mathf.PI * 2f / seg;
                _verts.Add(centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                _colors.Add(c);
                if (i > 0) { _tris.Add(start); _tris.Add(start + i); _tris.Add(start + i + 1); }
            }
        }
    }
}
