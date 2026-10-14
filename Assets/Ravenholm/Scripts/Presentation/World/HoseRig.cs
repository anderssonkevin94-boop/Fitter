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
        /// <summary>Where the carried pile sits (in the animator's parent space, like its own anchor), while carrying.</summary>
        public Vector2? CarryAnchor { get; private set; }
        /// <summary>The top of the head (hat), in the animator's parent space.</summary>
        public float HeadTop { get; private set; } = 1f;
        /// <summary>The walk's phase (0..1 for two steps), and the tool's angle (degrees) while working.</summary>
        public float Phase => _phase;
        public float ToolAngle { get; private set; }
        /// <summary>The supporting boot's position this frame (rig space; for tests of planted feet).</summary>
        public Vector2 LeftFoot { get; private set; }
        public Vector2 RightFoot { get; private set; }

        private Transform _rig;
        private SpriteRenderer _body, _bootL, _bootR, _handL, _handR, _tool;
        private Tube _armL, _armR, _legL, _legR;
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
            _armL = MakeTube("ArmLeft");
            _armR = MakeTube("ArmRight");
            _legL = MakeTube("LegLeft");
            _legR = MakeTube("LegRight");
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
        public void Drive(string dir8, Pose pose, float moved, float dt, Vector2 parentOffset, float parentScale)
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
            var depth = new Vector2(f.x, f.y * 0.5f);   // forward on the ground, as drawn (the camera looks down)
            var side = ViewName == "S" || ViewName == "N";
            var back = ViewName == "N" || ViewName == "NE";

            // --- the walk: the phase follows the ground covered, so the supporting foot stays put
            var walking = pose == Pose.Walk || pose == Pose.Carry && moved > 0.0001f;
            var speed = dt > 0f ? moved / dt : 0f;
            var stride = definition.stride * Mathf.Clamp(0.75f + 0.125f * speed, 0.75f, 1.15f);
            if (walking) { _phase = Mathf.Repeat(_phase + moved / stride, 1f); _settle = 1f; }
            else _settle = Mathf.MoveTowards(_settle, 0f, dt * 6f);   // feet come back together when stopping
            var amp = 0.3f * stride * _settle;
            float Travel(float t, out float lift)
            {
                t = Mathf.Repeat(t, 1f);
                if (t < 0.6f) { lift = 0f; return 1f - t / 0.3f; }   // stance: from in front to behind, planted
                var u = (t - 0.6f) / 0.4f;
                lift = Mathf.Sin(Mathf.PI * u);
                return -Mathf.Cos(Mathf.PI * u);                    // swing: back to the front, lifted
            }
            var trL = Travel(_phase, out var liftL);
            var trR = Travel(_phase + 0.5f, out var liftR);
            liftL *= _settle;
            liftR *= _settle;
            var footL = f * (trL * amp) + Vector2.up * (liftL * definition.footLift);
            var footR = f * (trR * amp) + Vector2.up * (liftR * definition.footLift);
            var bounce = definition.bounce * _settle * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * _phase));
            var bodyY = bounce;

            // --- work and other poses
            Vector2 wristL = v.wristLeft, wristR = v.wristRight;
            var carrying = pose == Pose.Carry;
            var working = pose == Pose.Mine || pose == Pose.MineDown || pose == Pose.MineUp || pose == Pose.Chop;
            _work = working ? _work + dt : 0f;
            Sprite handSpriteL = HandOpen(), handSpriteR = HandOpen();
            _tool.enabled = false;
            CarryAnchor = null;
            var shoulderL = v.shoulderLeft + Vector2.up * bodyY;
            var shoulderR = v.shoulderRight + Vector2.up * bodyY;
            if (carrying)
            {
                // Hands out in front at belly height, a little apart: the pile rests on them.
                var chest = (v.shoulderLeft + v.shoulderRight) * 0.5f + Vector2.up * (bodyY - 0.15f);   // belly height: the pile clears his face
                var across = side ? 0.075f : 0.05f;
                wristL = chest + depth * 0.13f + new Vector2(side ? across : 0f, side ? 0f : 0.01f);
                wristR = chest + depth * 0.13f + new Vector2(side ? -across : 0f, side ? 0f : -0.01f);
                handSpriteL = handSpriteR = HandGrip();
                var mid = (wristL + wristR) * 0.5f + Vector2.up * 0.03f;
                CarryAnchor = parentOffset + new Vector2(Mirrored ? -mid.x : mid.x, mid.y) * parentScale;
            }
            else if (working)
            {
                // A wind-up held (0.26 s, the tool raised behind), then a fast strike (0.14 s) down in front.
                var t = Mathf.Repeat(_work, 0.4f);
                float start = -150f, end = pose == Pose.MineDown ? 130f : pose == Pose.MineUp ? 40f : 95f;
                var a = t < 0.26f ? Mathf.Lerp(-40f, start, Mathf.SmoothStep(0f, 1f, t / 0.26f))
                                  : Mathf.Lerp(start, end, Mathf.SmoothStep(0f, 1f, (t - 0.26f) / 0.14f));
                // The near hand (right in the painted views) holds the tool; the arm reaches from the shoulder.
                var reach = 0.2f;
                var dirArm = depth.normalized * Mathf.Sin(a * Mathf.Deg2Rad) * (depth.magnitude) + Vector2.down * Mathf.Cos(a * Mathf.Deg2Rad);
                wristR = shoulderR + dirArm * reach;
                wristL = Vector2.Lerp(v.wristLeft + Vector2.up * bodyY, wristR, 0.5f);   // the other hand helps
                handSpriteR = handSpriteL = HandGrip();
                _tool.enabled = true;
                _tool.sprite = pose == Pose.Chop ? definition.axe : definition.pickaxe;
                var ang = Mathf.Atan2(dirArm.y, dirArm.x) * Mathf.Rad2Deg;
                ToolAngle = ang;
                _tool.transform.localPosition = wristR;
                _tool.transform.localRotation = Quaternion.Euler(0f, 0f, ang - 45f);   // the handle is painted at 45 degrees
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
                // Hands swing against the feet; standing, a slow breath.
                var breath = walking ? 0f : Mathf.Sin(Time.time * 2.2f) * 0.006f;
                wristL = v.wristLeft + depth * (-trL * definition.armSwing * _settle) + Vector2.up * (bodyY + breath);
                wristR = v.wristRight + depth * (-trR * definition.armSwing * _settle) + Vector2.up * (bodyY + breath);
                bodyY += breath;
            }

            // --- place the painted parts
            _body.sprite = v.body;
            _body.transform.localPosition = new Vector3(0f, bodyY, 0f);
            _bootL.sprite = v.bootLeft;
            _bootR.sprite = v.bootRight;
            _bootL.transform.localPosition = footL;
            _bootR.transform.localPosition = footR;
            LeftFoot = v.ankleLeft + footL;
            RightFoot = v.ankleRight + footR;
            _handL.sprite = handSpriteL;
            _handR.sprite = handSpriteR;
            _handL.transform.localPosition = wristL;
            _handR.transform.localPosition = wristR;
            _handL.flipX = true;   // the hands are painted as right hands; the left one is their mirror

            // --- the hoses
            var outline = OutlineWidth();
            var hipL = v.hipLeft + Vector2.up * bodyY;
            var hipR = v.hipRight + Vector2.up * bodyY;
            var legLen = Vector2.Distance(v.hipLeft, v.ankleLeft);
            var armLen = Vector2.Distance(v.shoulderLeft, v.wristLeft);
            var kneeDir = side ? Vector2.zero : f;   // knees bow forward; head-on they bow slightly outward
            DrawTube(_legL, hipL, v.ankleLeft + footL, legLen, side ? Vector2.right * Mathf.Sign(v.hipLeft.x) : kneeDir, definition.legWidth, definition.legColor, outline);
            DrawTube(_legR, hipR, v.ankleRight + footR, legLen, side ? Vector2.right * Mathf.Sign(v.hipRight.x) : kneeDir, definition.legWidth, definition.legColor, outline);
            var elbowDir = side ? Vector2.zero : -f;
            DrawTube(_armL, shoulderL, wristL, armLen, side ? Vector2.right * Mathf.Sign(v.shoulderLeft.x) : elbowDir, definition.armWidth, definition.armColor, outline);
            DrawTube(_armR, shoulderR, wristR, armLen, side ? Vector2.right * Mathf.Sign(v.shoulderRight.x) : elbowDir, definition.armWidth, definition.armColor, outline);

            // --- draw order inside the character's sorting group (the carried pile sits above, from 10)
            var armsFront = carrying && !back;
            if (side)
            {
                Order(_armL, armsFront ? 7 : 0); Order(_armR, armsFront ? 7 : 0);
                _handL.sortingOrder = armsFront ? 8 : 1; _handR.sortingOrder = armsFront ? 9 : 1;
                Order(_legL, 2); Order(_legR, 2);
                _bootL.sortingOrder = 3; _bootR.sortingOrder = 3;
            }
            else
            {
                // Painted SE/E/NE face right: his right side is nearer the camera.
                Order(_armL, armsFront ? 7 : 0); _handL.sortingOrder = armsFront ? 8 : 1;
                Order(_legL, 2); _bootL.sortingOrder = 3;
                Order(_legR, 4); _bootR.sortingOrder = 5;
                Order(_armR, armsFront || working || !back ? 7 : 0); _handR.sortingOrder = armsFront || working || !back ? 9 : 1;
            }
            _body.sortingOrder = 6;
            _tool.sortingOrder = 8;

            var top = v.body != null ? v.body.bounds.max.y : 1f;
            HeadTop = parentOffset.y + (bodyY + top) * parentScale;
        }

        private Sprite HandOpen() => ViewName == "E" ? definition.handOpenSide : ViewName == "N" || ViewName == "NE" ? definition.handOpenBack : definition.handOpenFront;
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
        private void DrawTube(Tube t, Vector2 a, Vector2 b, float restLength, Vector2 bowDir, float width, Color color, float outline)
        {
            var d = Vector2.Distance(a, b);
            var along = d > 0.0001f ? (b - a) / d : Vector2.down;
            var normal = new Vector2(-along.y, along.x);
            if (bowDir.sqrMagnitude > 0.0001f && Vector2.Dot(normal, bowDir) < 0f) normal = -normal;
            var bow = bowDir.sqrMagnitude > 0.0001f ? definition.bow * Mathf.Sqrt(Mathf.Max(0f, restLength * restLength - d * d)) * 0.5f + restLength * 0.06f : restLength * 0.04f;
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
            Fill(t.FillMesh, width, color);
            Fill(t.OutlineMesh, width + 2f * outline, GameRoot.Instance != null && GameRoot.Instance.Config.SandboxSettings.outlines != null
                ? GameRoot.Instance.Config.SandboxSettings.outlines.color : Color.black);
            t.Outline.enabled = outline > 0f;
        }

        /// <summary>A strip along the path with round caps at both ends.</summary>
        private void Fill(Mesh mesh, float width, Color color)
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
            // Mesh colours are not converted for the linear colour space by Unity (sprite textures are): convert here.
            Color32 c = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            var h = width * 0.5f;
            for (var i = 0; i < _path.Count; i++)
            {
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
            Cap(_path[0], h, c);
            Cap(_path[_path.Count - 1], h, c);
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
