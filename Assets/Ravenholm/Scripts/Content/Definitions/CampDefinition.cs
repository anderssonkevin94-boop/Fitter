using System;
using System.Collections.Generic;
using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// A camp on the journey to the castle (Docs/JOURNEY_OUTLINE.md, Kevin 2026-10-15): a small clearing on a long, wide,
    /// winding mountain top that runs forward (up the screen) toward the castle. An old cobble road wanders along it,
    /// mostly overgrown; a gate across the whole ridge (old trees, or a rock wall) needs a better tool; the next camp's
    /// clearing lies beyond. Everything is tougher further forward. The shape is authored here; the details are seeded.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Camp", fileName = "camp_")]
    public sealed class CampDefinition : ContentDefinition
    {
        [Serializable]
        public struct Stretch
        {
            [Tooltip("Up-right on screen (+x); otherwise up-left (+y).")]
            public bool upRight;
            [Min(1)] public int length;
        }

        [Serializable]
        public struct GateSpec
        {
            [Tooltip("Cells ahead of the camp along the road.")]
            [Min(1)] public int ahead;
            [Min(1)] public int thickness;
            [Tooltip("The tool tier needed (2 = iron).")]
            [Min(1)] public int tier;
            [Tooltip("A rock wall (the pickaxe) rather than old trees (the axe).")]
            public bool rock;
        }

        [Min(1)] [SerializeField] private int order = 1;
        [Header("The camp's clearing (cells: x right-up, y left-up)")]
        [SerializeField] private Vector2 clearingCentre = new Vector2(-6, 3);
        [SerializeField] private Vector2 clearingRadii = new Vector2(17, 23);
        [Header("The ridge: its centre line from behind the camp, forward")]
        [SerializeField] private Vector2Int start = new Vector2Int(-6, -13);
        [SerializeField] private List<Stretch> stretches = new List<Stretch>();
        [Min(4)] [SerializeField] private float halfWidth = 20f;
        [Min(0)] [SerializeField] private float widthWobble = 5f;
        [Min(0)] [SerializeField] private float edgeNoise = 3f;
        [Min(0)] [SerializeField] private float lineWobble = 4f;
        [Min(0)] [SerializeField] private float roadWobble = 4f;
        [Tooltip("Cells along the road per band (each band: the next wood, and the Mine's next layer in the hills).")]
        [Min(4)] [SerializeField] private int bandLength = 45;
        [Tooltip("Flat cells beside a clearing before the hills rise.")]
        [Min(0)] [SerializeField] private int flatMargin = 3;
        [SerializeField] private List<GateSpec> gates = new List<GateSpec>();
        [Header("The next camp's clearing")]
        [Min(10)] [SerializeField] private int nextCampAhead = 95;
        [Min(4)] [SerializeField] private float nextCampRadius = 9f;
        [SerializeField] private int seed = 1;

        [NonSerialized] private RidgeLayout _ridge;

        public override string IdKind => "camp";
        public int Order => order;
        public Vector2 ClearingCentre => clearingCentre;
        public IReadOnlyList<GateSpec> Gates => gates;
        public int StretchCount => stretches.Count;
        public int NextCampAhead => nextCampAhead;

        /// <summary>The camp's land (built once from these settings; derived data, the asset is never changed).</summary>
        public RidgeLayout Ridge
        {
            get
            {
                if (_ridge != null) return _ridge;
                var r = new RidgeLayout
                {
                    StartX = start.x, StartY = start.y, HalfWidth = halfWidth, WidthWobble = widthWobble, EdgeNoise = edgeNoise,
                    LineWobble = lineWobble, RoadWobble = roadWobble, BandLength = bandLength, FlatMargin = flatMargin, Seed = seed,
                };
                foreach (var st in stretches) r.Stretches.Add(new RidgeLayout.Stretch { AlongX = st.upRight, Length = st.length });
                var campS = r.Locate(Mathf.RoundToInt(clearingCentre.x), Mathf.RoundToInt(clearingCentre.y)).s;
                r.CampS = campS;
                r.Clearings.Add(new RidgeLayout.Clearing
                {
                    S = campS, HasCentre = true, CX = clearingCentre.x, CY = clearingCentre.y, RX = clearingRadii.x, RY = clearingRadii.y,
                });
                r.Clearings.Add(new RidgeLayout.Clearing { S = campS + nextCampAhead, RX = nextCampRadius, RY = nextCampRadius });
                foreach (var g in gates) r.Gates.Add(new RidgeLayout.Gate { S = campS + g.ahead, Thickness = g.thickness, Tier = g.tier, Rock = g.rock });
                return _ridge = r;
            }
        }

        /// <summary>Every cell that may be land (the ridge's extent).</summary>
        public RectInt Bounds
        {
            get
            {
                var e = Ridge.Extent;
                return new RectInt(e.minX, e.minY, e.maxX - e.minX + 1, e.maxY - e.minY + 1);
            }
        }

        private void OnValidate() => _ridge = null;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (stretches.Count == 0) errors.Add($"{Describe()}: no ridge stretches");
            if (clearingRadii.x < 4 || clearingRadii.y < 4) errors.Add($"{Describe()}: clearing too small");
        }

#if UNITY_EDITOR
        public void EditorSetup(int campOrder, Vector2 centre, Vector2 radii, Vector2Int lineStart, List<Stretch> line, List<GateSpec> gateList,
            int nextAhead, float nextRadius)
        {
            order = campOrder;
            clearingCentre = centre;
            clearingRadii = radii;
            start = lineStart;
            stretches = line;
            gates = gateList;
            nextCampAhead = nextAhead;
            nextCampRadius = nextRadius;
            _ridge = null;
        }
#endif
    }
}
