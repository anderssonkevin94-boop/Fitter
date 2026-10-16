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
            [Tooltip("A build project instead (a bridge over a ravine, a causeway over a flood): the land is a gap until it is built.")]
            public string project;
            public List<ResourceCost> projectCost;
        }

        [Serializable]
        public struct StopSpec
        {
            public string name;
            [Tooltip("Cells ahead of camp 1 along the road.")]
            [Min(10)] public int ahead;
            [Min(4)] public float radius;
            [Tooltip("The ground's tint around this camp (its own land: a darker wood, a misty fen, grey uplands).")]
            public Color groundTint;
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
        [Header("Gated levels (Kevin, 2026-10-16)")]
        [Tooltip("Each camp is its own square level: a plateau with the camp at the front, forest and hills, the gate at the far corner.")]
        [SerializeField] private bool plateau;
        [Min(20)] [SerializeField] private int levelSize = 60;
        [Header("The switchback pass (Kevin, 2026-10-16; Astra's pass pack)")]
        [Tooltip("The road as a switchback pass: legs alternating along the two axes, joined by quarter-circle turns. Camps sit mid-leg, the gates at the turns.")]
        [SerializeField] private bool switchback;
        [Tooltip("Cells along each leg after the first (Kevin: medium, 40, maybe longer later).")]
        [Min(8)] [SerializeField] private int legLength = 40;
        [Tooltip("The first leg: from behind camp 1's town, through it, and on to the first turn.")]
        [Min(8)] [SerializeField] private int firstLegLength = 69;
        [Tooltip("The turns' road radius in cells (the paintings' 16 tiles).")]
        [Min(4)] [SerializeField] private float turnRadius = 16f;
        [Tooltip("Where the first leg starts, behind camp 1's clearing centre (cells along -x).")]
        [Min(0)] [SerializeField] private int behindTown = 12;
        [Header("The camps further on (the journey's stops after this camp)")]
        [SerializeField] private List<StopSpec> stops = new List<StopSpec>();
        [Tooltip("At camp 1, the middle of the camp's group (shop, Trader, applicants): it is set down on each later clearing's centre.")]
        [SerializeField] private Vector2Int layoutAnchor = new Vector2Int(5, 2);
        [Header("Each camp at the edge (Kevin, 2026-10-16: the camps look over the drop to the castle)")]
        [Tooltip("World units on screen from where a camp's group stands to the land's edge on its right (the drop, the castle beyond); the view is 7 wide.")]
        [Min(0)] [SerializeField] private float edgeGap = 2.5f;
        [Tooltip("World units above a camp's group the bay's cliff runs up and left, opening the top of the view to the castle.")]
        [Min(0)] [SerializeField] private float bayHeight = 5f;
        [Tooltip("Cells (from the layout anchor) where the camp's group has things standing (the Trader's pad): the land's edge stays clear of them at every camp.")]
        [SerializeField] private List<Vector2Int> groupSpots = new List<Vector2Int>();
        [Tooltip("At the later camps, how far inside its clearing's right-hand edge the group stands (cells).")]
        [Min(0)] [SerializeField] private float anchorInset = 6f;
        [Min(10)] [SerializeField] private int nextCampAhead = 95;   // (older assets: the first stop, before stops existed)
        [Min(4)] [SerializeField] private float nextCampRadius = 9f;
        [SerializeField] private int seed = 1;

        [NonSerialized] private RidgeLayout _ridge;
        [NonSerialized] private readonly Dictionary<int, RidgeLayout> _levels = new Dictionary<int, RidgeLayout>();

        public override string IdKind => "camp";
        public int Order => order;
        public Vector2 ClearingCentre => clearingCentre;
        public IReadOnlyList<GateSpec> Gates => gates;
        public int StretchCount => stretches.Count;
        public int NextCampAhead => stops.Count > 0 ? stops[0].ahead : nextCampAhead;
        public IReadOnlyList<StopSpec> Stops => stops;

        /// <summary>Camps on the road: 0 is this camp, then each stop.</summary>
        public int CampCount => 1 + stops.Count;

        public string CampName(int index) => index <= 0 ? DisplayName : stops[Mathf.Min(index, stops.Count) - 1].name;

        /// <summary>A camp's centre cell: camp 1's layout anchor (where its group stands), else the stop's clearing centre.</summary>
        public Vector2 CampCentre(int index)
        {
            if (index <= 0 || plateau) return layoutAnchor;   // every level's camp stands at its front, in the same place
            var r = Ridge;
            var st = stops[Mathf.Min(index, stops.Count) - 1];
            if (switchback)
            {
                // Mid-leg, toward the side facing the next leg (up the screen): left of travel on an x leg, right on a y leg.
                var leg = r.LegSpans[Mathf.Min(index, r.LegSpans.Count - 1)];
                var side = leg.AlongX ? 1f : -1f;
                var (sx, sy) = r.PointBeside(r.LegMid(index), side * Mathf.Max(0f, st.radius - anchorInset));
                return new Vector2((float)sx, (float)sy);
            }
            // The group stands toward the clearing's right-hand side, the land's edge a few cells beyond it (as at camp 1).
            var (x, y) = r.PointBeside(r.CampS + st.ahead, -Mathf.Max(0f, st.radius - anchorInset));
            return new Vector2((float)x, (float)y);
        }

        public float CampRadius(int index) => index <= 0 ? Mathf.Min(clearingRadii.x, clearingRadii.y) : stops[Mathf.Min(index, stops.Count) - 1].radius;

        /// <summary>The camp's ahead distance from camp 1 along the road.</summary>
        public int CampAhead(int index) => index <= 0 || plateau ? 0
            : switchback ? Mathf.RoundToInt((float)(Ridge.LegMid(Mathf.Min(index, stops.Count)) - Ridge.CampS))
            : stops[Mathf.Min(index, stops.Count) - 1].ahead;

        public bool Switchback => switchback;
        public bool Plateau => plateau;
        public int LevelSize => levelSize;

        /// <summary>A gate's distance ahead of camp 1: on the pass, the middle of its turn.</summary>
        public int GateAhead(int index) => plateau ? Mathf.RoundToInt((float)(RidgeFor(index).Gates[Mathf.Min(index, gates.Count - 1)].S - RidgeFor(index).CampS))
            : switchback && index < Ridge.TurnSpans.Count
            ? Mathf.RoundToInt((float)(Ridge.TurnSpans[index].Mid - Ridge.CampS)) : gates[index].ahead;

        /// <summary>The ground's tint at a distance along the road: each camp's land from halfway to it, blending in.</summary>
        public Color GroundTintAt(double ahead)
        {
            var tint = Color.white;
            if (plateau) return tint;   // level 1 (each level's own ground comes with step 3)
            var prevAhead = 0;
            for (var i = 0; i < stops.Count; i++)
            {
                var st = stops[i];
                var at = CampAhead(i + 1);
                // On the pass a region starts at the turn before its camp; elsewhere halfway between camps.
                var from = switchback && i < Ridge.TurnSpans.Count ? (float)(Ridge.TurnSpans[i].Mid - Ridge.CampS) : (prevAhead + at) * 0.5f;
                if (ahead >= from)
                {
                    var c = st.groundTint.a > 0 ? st.groundTint : Color.white;
                    tint = Color.Lerp(tint, c, Mathf.Clamp01((float)(ahead - from) / 20f));
                }
                prevAhead = at;
            }
            return tint;
        }

        /// <summary>The camp's land (built once from these settings; derived data, the asset is never changed).</summary>
        public RidgeLayout Ridge => RidgeFor(0);

        /// <summary>
        /// The land of a level (gated levels, 2026-10-16): each camp's own plateau, its outline and woods its own (a seed
        /// per level), its camp in the same place at its front and its own gate on the north rim. Without levels, the
        /// one road (any level gives it). Derived data, built once per level; the asset is never changed.
        /// </summary>
        public RidgeLayout RidgeFor(int level)
        {
            if (!plateau) level = 0;
            if (level == 0 && _ridge != null) return _ridge;
            if (level > 0 && _levels.TryGetValue(level, out var known)) return known;
            {
                var r = new RidgeLayout
                {
                    StartX = start.x, StartY = start.y, HalfWidth = halfWidth, WidthWobble = widthWobble, EdgeNoise = edgeNoise,
                    LineWobble = lineWobble, RoadWobble = roadWobble, BandLength = bandLength, FlatMargin = flatMargin, Seed = seed,
                };
                if (plateau)
                {
                    r.Plateau = true;
                    r.LevelSize = levelSize;
                    r.Level = level;
                    r.Seed = seed + 977 * level;
                    r.SetLevelAround(clearingCentre.x, clearingCentre.y);
                    r.StartX = clearingCentre.x;
                    r.StartY = clearingCentre.y;
                }
                else if (switchback)
                {
                    r.Switchback = true;
                    r.StartX = clearingCentre.x - behindTown;
                    r.StartY = clearingCentre.y;
                    r.Legs = CampCount;
                    r.LegLength = legLength;
                    r.FirstLegLength = firstLegLength;
                    r.TurnRadius = turnRadius;
                }
                else foreach (var st in stretches) r.Stretches.Add(new RidgeLayout.Stretch { AlongX = st.upRight, Length = st.length });
                var campS = r.Locate(Mathf.RoundToInt(clearingCentre.x), Mathf.RoundToInt(clearingCentre.y)).s;
                r.CampS = campS;
                r.Clearings.Add(new RidgeLayout.Clearing
                {
                    S = campS, HasCentre = true, CX = clearingCentre.x, CY = clearingCentre.y, RX = clearingRadii.x, RY = clearingRadii.y,
                });
                if (stops.Count == 0 && !plateau) r.Clearings.Add(new RidgeLayout.Clearing { S = campS + nextCampAhead, RX = nextCampRadius, RY = nextCampRadius });
                for (var i = 0; i < stops.Count && !plateau; i++)   // a level has only its own camp
                {
                    var st = stops[i];
                    var at = switchback ? r.LegMid(i + 1) : campS + st.ahead;
                    var clearing = new RidgeLayout.Clearing { S = at, RX = st.radius, RY = st.radius };
                    if (switchback)
                    {
                        // The camp's ledge: a clearing pushed out over the side facing the next leg.
                        var side = r.LegSpans[Math.Min(i + 1, r.LegSpans.Count - 1)].AlongX ? 1.0 : -1.0;
                        var (cx, cy) = r.PointBeside(at, side * (st.radius - anchorInset) * 0.5);
                        clearing.HasCentre = true;
                        clearing.CX = cx;
                        clearing.CY = cy;
                    }
                    r.Clearings.Add(clearing);
                }
                for (var i = 0; i < gates.Count; i++)
                {
                    var g = gates[i];
                    // A level's own gate stands across its far corner; the other levels' gates are elsewhere (never met here).
                    var at = plateau ? (i == level ? r.Length - 4 : r.Length + 1000 + 100 * i)
                        : switchback && i < r.TurnSpans.Count ? r.TurnSpans[i].Mid : campS + g.ahead;
                    r.Gates.Add(new RidgeLayout.Gate { S = at, Thickness = g.thickness, Tier = g.tier, Rock = g.rock, Project = !string.IsNullOrEmpty(g.project) });
                }
                if (level == 0) _ridge = r;
                else _levels[level] = r;
                // Each camp at the edge (Kevin, 2026-10-16): a cove in the land's right-hand edge, edgeGap world units right of
                // where the camp's group stands; the road swings out along the edge toward it (about two cells across the
                // line per world unit across the screen).
                if (edgeGap > 0 && !switchback && !plateau)   // the pass shows the legs below instead
                    for (var i = 0; i < CampCount && i < r.Clearings.Count; i++)
                    {
                        var c = r.Clearings[i];
                        var a = CampCentre(i);
                        c.AX = a.x;
                        c.AY = a.y;
                        c.CoveGap = edgeGap;
                        c.BayHeight = bayHeight;
                        var edge = -r.Locate(Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y)).off + 2 * edgeGap + 2;
                        foreach (var g in groupSpots)   // and clear of what the group sets down (with room round it)
                            edge = Math.Max(edge, -r.Locate(Mathf.RoundToInt(a.x) + g.x, Mathf.RoundToInt(a.y) + g.y).off + 5);
                        c.EdgeRight = edge;
                    }
                return r;
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

        private void OnValidate()
        {
            _ridge = null;
            _levels.Clear();
        }

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (stretches.Count == 0 && !switchback && !plateau) errors.Add($"{Describe()}: no ridge stretches");
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
            _levels.Clear();
        }

        public int GroupSpotCount => groupSpots.Count;

        public void EditorSetGroupSpots(List<Vector2Int> spots)
        {
            groupSpots = spots;
            _ridge = null;
            _levels.Clear();
        }

        /// <summary>The switchback pass (Kevin, 2026-10-16): its legs and turns, the legs' straight width, the road's wander.</summary>
        /// <summary>Gated levels (Kevin, 2026-10-16): each camp its own square plateau; the switchback off.</summary>
        public void EditorSetPlateau(bool on, int size)
        {
            plateau = on;
            levelSize = size;
            if (on) switchback = false;
            edgeGap = 0f;
            _ridge = null;
            _levels.Clear();
        }

        public void EditorSetSwitchback(bool on, int leg, int firstLeg, float turn, int behind, float half, float road)
        {
            switchback = on;
            legLength = leg;
            firstLegLength = firstLeg;
            turnRadius = turn;
            behindTown = behind;
            halfWidth = half;
            widthWobble = 0f;
            edgeNoise = 0f;
            roadWobble = road;
            edgeGap = 0f;
            _ridge = null;
            _levels.Clear();
        }

        public void EditorSetStops(List<StopSpec> list, List<Stretch> line, List<GateSpec> gateList, int bands)
        {
            stops = list;
            stretches = line;
            gates = gateList;
            bandLength = bands;
            _ridge = null;
            _levels.Clear();
        }
#endif
    }
}
