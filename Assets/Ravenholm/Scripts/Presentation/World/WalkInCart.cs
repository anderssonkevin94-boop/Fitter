using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>Astra's approved walk-in wagon (2026-10-16): its seven layers and the pack's geometry, in canvas pixels (top-left origin).</summary>
    [Serializable]
    public sealed class WalkInCartArt
    {
        public Sprite back, shafts, steps, wheelLeft, wheelRight, front, roof, silhouette;
        public Vector2 originPx = new Vector2(779f, 826f);
        public float ppu = 256f;
        public float floorHeightPx = 150f;
        [Tooltip("The wagon body's ground footprint (the raised floor's ground-foot polygon).")]
        public Vector2[] floorGroundPx = Array.Empty<Vector2>();
        [Tooltip("Where the steps stand on the ground (ground-foot polygon).")]
        public Vector2[] stairsGroundPx = Array.Empty<Vector2>();
        public Vector2 stepsBottomPx, stepsTopGroundPx, serveGroundPx, customerGroundPx, hitchPx, counterStartPx, counterEndPx;
        [Tooltip("The ground under the shafts (ground-foot polygon), walled so no one walks through them.")]
        public Vector2[] shaftsGroundPx = Array.Empty<Vector2>();
        [Tooltip("Left of this canvas column, a figure in the doorway or on the steps draws in front of the door frame.")]
        public float frameFrontPx = 494f;
        [Tooltip("The doorway on the footprint's left end (ground-foot points).")]
        public Vector2 doorGroundA, doorGroundB;

        public bool Valid => back != null && front != null && roof != null && floorGroundPx != null && floorGroundPx.Length >= 3
                             && stairsGroundPx != null && stairsGroundPx.Length == 4;
    }

    /// <summary>
    /// The walk-in wagon (Astra's pack, 2026-10-16). The player's physics root stays on the ground: walls along the wagon's
    /// footprint leave the left doorway open; only the drawn figure rises (up the steps, 150 px on the floor). Each layer
    /// sorts at its own depth on the town's height-on-screen order, so a figure inside falls between the back and the
    /// counter: back and shafts at the far wall; the near wheels with the counter; the steps just above the stair top; the counter and the roof
    /// just in front of where a figure inside may stand and just behind where customers stand. On the lower steps the
    /// figure stays in front of the door frame (on the steps and in the doorway: Kevin, 2026-10-16). One outline goes round the whole wagon.
    /// </summary>
    public sealed class WalkInCart : MonoBehaviour
    {
        public static WalkInCart Current { get; private set; }

        private WalkInCartArt _art;
        private readonly List<Vector2> _floor = new List<Vector2>();
        private readonly List<Vector2> _stairs = new List<Vector2>();
        private Vector2 _stairBottom, _stairTop;

        public WalkInCartArt Art => _art;
        public float FloorHeight => _art != null ? _art.floorHeightPx / _art.ppu : 0f;
        public IReadOnlyList<Vector2> Walkable => _walk;
        private readonly List<Vector2> _walk = new List<Vector2>();
        private readonly List<Vector2> _walkSlack = new List<Vector2>();

        /// <summary>World points of the pack's anchors.</summary>
        public Vector2 ServeGround => W(_art.serveGroundPx);
        public Vector2 ServeVisual => W(_art.serveGroundPx) + new Vector2(0f, FloorHeight);
        public Vector2 CustomerGround => W(_art.customerGroundPx);
        public Vector2 DoorGround => W(_art.stepsTopGroundPx);
        public Vector2 StepsBottom => W(_art.stepsBottomPx);
        public Vector2 Hitch => W(_art.hitchPx);

        /// <summary>The sorting depths (canvas pixel rows) the layers use; tests read them.</summary>
        public float BackRow { get; private set; }
        public float StepsRow { get; private set; }
        public float FrontRow { get; private set; }

        private Vector2 L(Vector2 px) => new Vector2((px.x - _art.originPx.x) / _art.ppu, (_art.originPx.y - px.y) / _art.ppu);
        private Vector2 W(Vector2 px) => (Vector2)transform.position + L(px);
        private float RowY(float py) => (_art.originPx.y - py) / _art.ppu;

        private void OnEnable() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Builds the wagon under this object (at the pack's ground origin). <paramref name="radius"/>: the player's footprint radius.</summary>
        public void Build(WalkInCartArt art, Material material, Material outline, int blockerLayer, float radius)
        {
            _art = art;
            Current = this;
            _floor.Clear();
            foreach (var p in art.floorGroundPx) _floor.Add(L(p));
            _stairs.Clear();
            foreach (var p in art.stairsGroundPx) _stairs.Add(L(p));
            _stairBottom = L(art.stepsBottomPx);
            _stairTop = L(art.stepsTopGroundPx);

            // Depths, in canvas rows: everything inside stands below the far wall's row and above the front's, customers below it.
            var minFloor = float.MaxValue;
            foreach (var p in art.floorGroundPx) minFloor = Mathf.Min(minFloor, p.y);
            var minStairs = float.MaxValue;
            foreach (var p in art.stairsGroundPx) minStairs = Mathf.Min(minStairs, p.y);
            BackRow = minFloor - 8f;
            StepsRow = minStairs - 10f;
            FrontRow = art.customerGroundPx.y - 4f;

            var backGroup = Group("Back", BackRow);
            if (art.silhouette != null && outline != null) Layer(backGroup, "Outline", art.silhouette, outline, -1);   // the one outline, behind all
            Layer(backGroup, "cart_back", art.back, material, 0);
            Layer(backGroup, "cart_shafts", art.shafts, material, 1);
            Layer(Group("Steps", StepsRow), "cart_steps", art.steps, material, 2);
            // The near wheels draw with the counter, in front of a figure inside: the floor's far corner reaches over the
            // right wheel, where the counter does not cover the feet (the pack's order put them behind; its route never goes there).
            var frontGroup = Group("Front", FrontRow);
            Layer(frontGroup, "wheel_near_left", art.wheelLeft, material, 3);
            Layer(frontGroup, "wheel_near_right", art.wheelRight, material, 3);
            Layer(frontGroup, "cart_front", art.front, material, 20);
            Layer(Group("Roof", FrontRow + 1f), "cart_roof", art.roof, material, 50);

            BuildWalls(blockerLayer, radius);
        }

        private Transform Group(string name, float row)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, RowY(row), 0f);   // its depth on screen; the art is drawn back at the origin
            go.AddComponent<SortingGroup>();
            return go.transform;
        }

        private void Layer(Transform group, string name, Sprite sprite, Material material, int order)
        {
            if (sprite == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(group, false);
            go.transform.localPosition = new Vector3(0f, -group.localPosition.y, 0f);   // every layer on the shared pivot
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            if (material != null) sr.sharedMaterial = material;
        }

        // ------------------------------------------------------------------ walls

        /// <summary>
        /// Solid on every side but the doorway (Kevin, 2026-10-16: "only enter and leave through the doors"). The walls
        /// stand a footprint radius outside the wagon's whole body, so no one stands in it from outside, with the left
        /// end's doorway open (its posts a radius back, so it is as wide for the feet as it is drawn). Inside, a rail just
        /// behind the counter's sorting row keeps the feet where a figure draws behind the counter. Both sides of the
        /// steps, and the ground under the shafts, are walled too.
        /// </summary>
        private void BuildWalls(int blockerLayer, float radius)
        {
            // 16 px short of the counter's sorting row: pressing into the rail (the physics' contact margin) never carries
            // the feet over it, where the figure would draw in front of the counter (Kevin's playtest, 2026-10-16).
            var clipY = RowY(FrontRow - RailMarginPx);
            _walk.Clear();
            _walk.AddRange(ClipBelow(_floor, clipY));   // y up: where the feet may stand inside
            if (_walk.Count < 3) return;
            var ccw = SignedArea(_floor) > 0f;
            _walkSlack.Clear();
            for (var i = 0; i < _walk.Count; i++)
                _walkSlack.Add(OffsetCorner(_walk[(i - 1 + _walk.Count) % _walk.Count], _walk[i], _walk[(i + 1) % _walk.Count], 0.06f, SignedArea(_walk) > 0f));

            // The body: round the whole footprint but the doorway.
            var n = _floor.Count;
            var doorA = L(_art.doorGroundA);
            var doorB = L(_art.doorGroundB);
            var doorEdge = 0;
            var best = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var d = DistanceToSegment(doorA, _floor[i], _floor[(i + 1) % n]) + DistanceToSegment(doorB, _floor[i], _floor[(i + 1) % n]);
                if (d < best) { best = d; doorEdge = i; }
            }
            var offset = new Vector2[n];
            for (var i = 0; i < n; i++) offset[i] = OffsetCorner(_floor[(i - 1 + n) % n], _floor[i], _floor[(i + 1) % n], radius, ccw);
            var start = _floor[doorEdge];
            var end = _floor[(doorEdge + 1) % n];
            var nearStart = Vector2.Distance(doorA, start) <= Vector2.Distance(doorB, start) ? doorA : doorB;
            var farDoor = nearStart == doorA ? doorB : doorA;
            var normal = Outward(start, end, ccw);
            var along = (end - start).normalized;
            var postNear = nearStart - along * radius + normal * radius;
            var postFar = farDoor + along * radius + normal * radius;
            var wall = new List<Vector2> { postFar };
            for (var k = 1; k <= n; k++) wall.Add(offset[(doorEdge + k) % n]);
            wall.Add(postNear);
            Edge(blockerLayer, "Walls", wall);
            DoorPostNear = postNear;
            DoorPostFar = postFar;

            // Inside: the rail behind the counter, along the cut of the floor, a radius in, running into the walls at both ends.
            var railA = new Vector2(float.MaxValue, clipY);
            var railB = new Vector2(float.MinValue, clipY);
            foreach (var q in _walk)
                if (Mathf.Abs(q.y - clipY) < 1e-4f) { if (q.x < railA.x) railA = q; if (q.x > railB.x) railB = q; }
            if (railA.x < railB.x)
            {
                var dir = (railB - railA).normalized;
                Edge(blockerLayer, "CounterRail", new List<Vector2>
                {
                    railA - dir * radius * 2f - new Vector2(0f, radius), railB + dir * radius * 2f - new Vector2(0f, radius),
                });
            }

            // Both sides of the steps, out from their middle, each running on to the nearer door post.
            var centre = (_stairs[0] + _stairs[1] + _stairs[2] + _stairs[3]) * 0.25f;
            var upper = Side(_stairs[0], _stairs[1], centre, radius);   // bottom-left -> top-left
            var lower = Side(_stairs[3], _stairs[2], centre, radius);   // bottom-right -> top-right
            var upperPost = Closest(upper.b, postNear, postFar);
            var lowerPost = upperPost == postNear ? postFar : postNear;
            Edge(blockerLayer, "StepsUpperSide", new List<Vector2> { upper.a, upper.b, upperPost });
            Edge(blockerLayer, "StepsLowerSide", new List<Vector2> { lower.a, lower.b, lowerPost });

            // The ground under the shafts, all round.
            if (_art.shaftsGroundPx != null && _art.shaftsGroundPx.Length >= 3)
            {
                var sh = new List<Vector2>();
                foreach (var p in _art.shaftsGroundPx) sh.Add(L(p));
                var sccw = SignedArea(sh) > 0f;
                var loop = new List<Vector2>();
                for (var i = 0; i < sh.Count; i++) loop.Add(OffsetCorner(sh[(i - 1 + sh.Count) % sh.Count], sh[i], sh[(i + 1) % sh.Count], radius, sccw));
                loop.Add(loop[0]);
                Edge(blockerLayer, "Shafts", loop);
            }
        }

        /// <summary>Canvas pixels between the counter rail's line and the counter's sorting row.</summary>
        public const float RailMarginPx = 16f;

        /// <summary>The doorway's posts (local), for tests.</summary>
        public Vector2 DoorPostNear { get; private set; }
        public Vector2 DoorPostFar { get; private set; }

        /// <summary>The wagon body's footprint (local units), for tests.</summary>
        public IReadOnlyList<Vector2> Footprint => _floor;

        private static Vector2 Closest(Vector2 to, Vector2 a, Vector2 b) => (to - a).sqrMagnitude <= (to - b).sqrMagnitude ? a : b;

        private static (Vector2 a, Vector2 b) Side(Vector2 a, Vector2 b, Vector2 centre, float r)
        {
            var d = (b - a).normalized;
            var nrm = new Vector2(-d.y, d.x);
            if (Vector2.Dot(nrm, (a + b) * 0.5f - centre) < 0f) nrm = -nrm;
            return (a + nrm * r, b + nrm * r);
        }

        private void Edge(int layer, string name, List<Vector2> points)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(transform, false);
            go.AddComponent<EdgeCollider2D>().SetPoints(points);
        }

        private static Vector2 Outward(Vector2 a, Vector2 b, bool ccw)
        {
            var d = (b - a).normalized;
            return ccw ? new Vector2(d.y, -d.x) : new Vector2(-d.y, d.x);
        }

        private static Vector2 OffsetCorner(Vector2 prev, Vector2 p, Vector2 next, float r, bool ccw)
        {
            var n1 = Outward(prev, p, ccw);
            var n2 = Outward(p, next, ccw);
            var bis = (n1 + n2).normalized;
            var cos = Mathf.Max(0.5f, Vector2.Dot(bis, n1));   // sharp corners: at most twice the radius out
            return p + bis * (r / cos);
        }

        private static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            var a = 0f;
            for (var i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        /// <summary>The polygon cut to y &gt;= <paramref name="y"/> (Sutherland-Hodgman, one half-plane).</summary>
        private static List<Vector2> ClipBelow(IReadOnlyList<Vector2> poly, float y)
        {
            var o = new List<Vector2>();
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                var ain = a.y >= y;
                var bin = b.y >= y;
                if (ain) o.Add(a);
                if (ain != bin) o.Add(Vector2.Lerp(a, b, (y - a.y) / (b.y - a.y)));
            }
            return o;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        public static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[j];
                var b = poly[i];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // ------------------------------------------------------------------ standing on it

        /// <summary>
        /// Where feet on the ground at <paramref name="world"/> stand on the wagon: how high the drawn figure rises
        /// (0 off it, ramping up the steps, the floor's height inside) and whether it is on the steps or in the doorway
        /// (drawn in front of the door frame). False off the wagon.
        /// </summary>
        public bool Sample(Vector2 world, out float elevation, out bool frontOfFrame)
        {
            elevation = 0f;
            frontOfFrame = false;
            if (_art == null) return false;
            var p = world - (Vector2)transform.position;
            if (Contains(_floor, p) || DistanceToPolygon(_floor, p) < 0.06f && Contains(_walkSlack, p))   // the walls' edge counts as floor
            {
                elevation = FloorHeight;
                // In the doorway (left of the near corner post) nothing of the wagon is in front of them: drawn over the
                // frame (Kevin, 2026-10-16: the post cut through them there).
                frontOfFrame = p.x < (_art.frameFrontPx - _art.originPx.x) / _art.ppu;
                return true;
            }
            if (!Contains(_stairs, p) && DistanceToPolygon(_stairs, p) > 0.08f) return false;
            var ramp = _stairTop - _stairBottom;
            var t = Mathf.Clamp01(Vector2.Dot(p - _stairBottom, ramp) / Mathf.Max(ramp.sqrMagnitude, 1e-6f));
            elevation = FloorHeight * t;
            frontOfFrame = true;   // on the steps, in front of the frame all the way up (the pack switched behind it at 80 %)
            return true;
        }

        public static float DistanceToPolygon(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            var best = float.MaxValue;
            for (var i = 0; i < poly.Count; i++) best = Mathf.Min(best, DistanceToSegment(p, poly[i], poly[(i + 1) % poly.Count]));
            return best;
        }

        // ------------------------------------------------------------------ the drive

        /// <summary>A copy of the wagon's look for the drive (the parked layers, outlined once), under <paramref name="parent"/>.</summary>
        public Transform MakeDrivingCopy(Transform parent, string sortingLayer, int order, Material material, Material outline)
        {
            var root = new GameObject("DrivingWagon").transform;
            root.SetParent(parent, false);
            void Add(Sprite s, int o, Material m)
            {
                if (s == null) return;
                var sr = new GameObject(s.name).AddComponent<SpriteRenderer>();
                sr.transform.SetParent(root, false);
                sr.sprite = s;
                sr.sortingLayerName = sortingLayer;
                sr.sortingOrder = order + o;
                if (m != null) sr.sharedMaterial = m;
            }
            if (outline != null) Add(_art.silhouette, 0, outline);
            Add(_art.back, 1, material);
            Add(_art.shafts, 2, material);
            Add(_art.steps, 3, material);
            Add(_art.wheelLeft, 4, material);
            Add(_art.wheelRight, 4, material);
            Add(_art.front, 5, material);
            Add(_art.roof, 6, material);
            return root;
        }
    }
}
