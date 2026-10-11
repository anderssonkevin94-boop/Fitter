using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The ground under the town as one block grid (GDD 6: "visibly stacked geological layers").
    ///
    /// Every cell of the town's isometric grid is a column of ground blocks, one miner tall per level. Level 1 is
    /// the town's own ground (its tops are the town tiles); each level below is one Tilemap on the same grid, offset
    /// one level down. A block is drawn only when the camera can see it: a front neighbour is open (off the town's
    /// edge, or dug) or the cell above is open. So the town's exposed edges show as a cliff of layers, and whatever
    /// the miners dig opens into it:
    ///   - the shaft: a 4×4 pit at the town's front edge, open to the camera through a notch, dug one level per
    ///     section (blocks clear front first); a vertical corridor with a ladder on its back wall;
    ///   - a dirt ledge (a thin slab of ground left in place) along the shaft's back walls at every level below the
    ///     town's ground, joining the ladder to both tunnel mouths;
    ///   - tunnels: one per level and side, dug block by block along the town's exposed front, each on a slab floor,
    ///     so neighbouring levels stay separated by dirt.
    /// Levels are tinted by the layer they were dug in; undug levels darken with depth. Only levels near the camera
    /// exist, so depth is unlimited. Presentation only: what is dug comes from the site simulation.
    /// </summary>
    public sealed class MineDigView : MonoBehaviour
    {
        public const string SiteId = "site.mine";
        public const int Size = 4;              // pit cells per side
        public const float LevelHeight = 1f;    // one level = one miner tall (world units)
        public const float SlabRise = 0.25f;    // ledge and tunnel floors: the slab of dirt left between levels
        public const int Preview = 3;
        public const int Blocks = Size * Size;
        private const int MaxTunnelCells = 96;

        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap ground;
        [SerializeField] private Vector3Int pitOrigin;   // front cell of the pit (lowest x and y)
        [SerializeField] private TileBase[] blockTiles = new TileBase[0];
        [SerializeField] private TileBase slabTile;
        [SerializeField] private Sprite ladderSprite;
        [SerializeField] private Material spriteMaterial;

        public static MineDigView Current { get; private set; }

        private GameRoot _root;
        private Camera _camera;
        private readonly Dictionary<int, Tilemap> _levels = new Dictionary<int, Tilemap>();
        private readonly Dictionary<int, Tilemap> _slabs = new Dictionary<int, Tilemap>();
        private readonly Dictionary<int, string> _levelKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, SpriteRenderer> _ladders = new Dictionary<int, SpriteRenderer>();
        private readonly List<Vector3Int> _order = new List<Vector3Int>();
        private readonly HashSet<Vector3Int> _removed = new HashSet<Vector3Int>();
        private readonly List<Vector3Int>[] _walks = { new List<Vector3Int>(), new List<Vector3Int>() };
        private readonly Dictionary<Vector3Int, (TunnelSide side, int index)> _walkIndex = new Dictionary<Vector3Int, (TunnelSide, int)>();
        private readonly List<Vector3Int> _ledge = new List<Vector3Int>();
        private readonly HashSet<Vector3Int> _ledgeSet = new HashSet<Vector3Int>();
        private readonly List<Vector3Int> _candidates = new List<Vector3Int>();
        private readonly Dictionary<(int level, TunnelSide side), int> _dug = new Dictionary<(int, TunnelSide), int>();
        private TextMeshPro _workingLabel, _nextLabel;
        private string _labelKey = "";
        private bool _geometryBuilt;

        public void EditorSetup(Grid g, Tilemap groundMap, Vector3Int origin, TileBase[] tiles, TileBase slab, Sprite ladder, Material mat)
        {
            grid = g;
            ground = groundMap;
            pitOrigin = origin;
            blockTiles = tiles;
            slabTile = slab;
            ladderSprite = ladder;
            spriteMaterial = mat;
        }

        // ------------------------------------------------------------------ what the view shows (tests, workers, player)

        public int WorkingLevel { get; private set; } = 1;
        public int BlocksRemoved { get; private set; }
        public int LevelsBuilt => _levels.Count;
        /// <summary>When true the camera looks at the working level until the player moves (Mine panel "Look down").</summary>
        public bool ViewingDig { get; set; }

        /// <summary>Pit cells in dig order: front first, so the cut is visible from the camera.</summary>
        public IReadOnlyList<Vector3Int> DigOrder => _order;

        /// <summary>Cells a tunnel on that side digs, in order, along the town's exposed front.</summary>
        public IReadOnlyList<Vector3Int> TunnelPath(TunnelSide side)
        {
            EnsureGeometry();
            return _walks[(int)side];
        }

        /// <summary>Blocks a tunnel on that side can reach before the town's edge.</summary>
        public int TunnelCapacityCells(TunnelSide side) => TunnelPath(side).Count;

        public IReadOnlyList<Vector3Int> LedgeCells
        {
            get { EnsureGeometry(); return _ledge; }
        }

        public bool IsPitCell(Vector3Int c) => c.x >= pitOrigin.x && c.x < pitOrigin.x + Size && c.y >= pitOrigin.y && c.y < pitOrigin.y + Size;

        /// <summary>Part of the town's ground columns (the pit included; the notch in front of it is open).</summary>
        public bool InPlot(Vector3Int c) => IsPitCell(c) || (ground != null && ground.HasTile(c));

        /// <summary>The ring of ground behind the pit that forms its back walls.</summary>
        public bool IsWallCell(Vector3Int c) =>
            (c.x == pitOrigin.x + Size && c.y >= pitOrigin.y && c.y <= pitOrigin.y + Size) ||
            (c.y == pitOrigin.y + Size && c.x >= pitOrigin.x && c.x <= pitOrigin.x + Size);

        private Vector3 Centre(Vector3Int c) => grid.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));
        private Vector3 CellTop(Vector3Int c, int level) => Centre(c) + new Vector3(0f, -(level - 1) * LevelHeight, 0f);

        /// <summary>The level whose blocks form the pit floor now.</summary>
        public int FloorLevel => BlocksRemoved >= Blocks ? WorkingLevel + 1 : WorkingLevel;

        /// <summary>Deepest level the player can stand at: the pit floor (0 while nothing is dug).</summary>
        public int DeepestFloor => FloorLevel - 1;

        /// <summary>Where a miner stands: on the next block to clear (or on the cleared floor while a choice waits).</summary>
        public Vector3 StandSpot(int index)
        {
            if (_order.Count == 0) BuildOrder();
            if (BlocksRemoved >= Blocks)
                return CellTop(new Vector3Int(pitOrigin.x + 1 + (index % 2), pitOrigin.y + 1, 0), WorkingLevel + 1);
            var i = Mathf.Min(BlocksRemoved + index, Blocks - 1);
            return CellTop(_order[i], WorkingLevel);
        }

        /// <summary>Town cell at the top of the ladder, and the shaft cell at its foot on every level.</summary>
        public Vector3Int LadderTopCell => new Vector3Int(pitOrigin.x + 1, pitOrigin.y + Size, 0);
        public Vector3Int LadderFootCell => new Vector3Int(pitOrigin.x + 1, pitOrigin.y + Size - 1, 0);

        /// <summary>Middle of the edge between the ladder's top cell and the shaft (the plane point the ladder stands on).</summary>
        public Vector2 LadderPlane => Centre(LadderTopCell) + new Vector3(0.25f, -0.125f, 0f);

        private static readonly Vector3 LadderLean = new Vector3(0.08f, -0.06f, 0f);

        /// <summary>A body on the ladder, <paramref name="h"/> levels below the town surface.</summary>
        public Vector3 LadderWorld(float h) => (Vector3)LadderPlane + LadderLean + new Vector3(0f, -h * LevelHeight, 0f);

        private Vector3 LadderAt(float level) => LadderWorld(level - 1);

        public Vector3 LadderTop => LadderWorld(0);
        public Vector3 LadderBottom => LadderWorld(DeepestFloor);

        /// <summary>Middle of the pit at the working floor (camera focus for "Look down the shaft").</summary>
        public Vector3 DigFocus => Centre(new Vector3Int(pitOrigin.x + 2, pitOrigin.y + 2, 0)) + new Vector3(0f, -(FloorLevel - 1) * LevelHeight - 0.5f, 0f);

        public Vector3 PitTopCentre => Centre(new Vector3Int(pitOrigin.x + 2, pitOrigin.y + 2, 0));

        /// <summary>World height of the deepest place anyone can stand (camera clamp).</summary>
        public float DeepestFloorY => PitTopCentre.y - Mathf.Max(DeepestFloor, 0) * LevelHeight;

        // ------------------------------------------------------------------ walking (player)

        public Vector3Int CellAt(Vector2 plane) => grid.WorldToCell(plane);

        /// <summary>True where the town's own ground can be walked on (not the pit, not the open notch).</summary>
        public bool SurfaceWalkable(Vector2 plane)
        {
            if (ground == null) return true;
            var c = CellAt(plane);
            return ground.HasTile(c) && !IsPitCell(c);
        }

        /// <summary>A slab floor (ledge or dug tunnel) at this level.</summary>
        public bool IsSlab(Vector3Int c, int level)
        {
            if (level < 2) return false;
            EnsureGeometry();
            if (_ledgeSet.Contains(c)) return level <= DeepestFloor;
            return _walkIndex.TryGetValue(c, out var w) && TunnelDug(level, w.side) > w.index;
        }

        /// <summary>Whether the player can stand at this plane point on a level below the town (≥ 1).</summary>
        public bool Walkable(int level, Vector2 plane)
        {
            var c = CellAt(plane);
            if (IsSlab(c, level)) return true;
            return level == DeepestFloor && IsPitCell(c) && Solid(c, level + 1);
        }

        /// <summary>How far below its plane point a body standing at this level is drawn.</summary>
        public float FloorDrop(int level, Vector2 plane) => level * LevelHeight - (IsSlab(CellAt(plane), level) ? SlabRise : 0f);

        /// <summary>Levels the ladder lets the player off at: the town (0), every ledge, and the pit floor.</summary>
        public bool IsStop(int level) => level == 0 || (level >= 1 && level == DeepestFloor) || (level >= 2 && level <= DeepestFloor);

        /// <summary>World point of a body standing on cell <paramref name="c"/> at <paramref name="level"/> (≥ 1).</summary>
        public Vector3 FloorPoint(Vector3Int c, int level) => Centre(c) + new Vector3(0f, -level * LevelHeight + (IsSlab(c, level) ? SlabRise : 0f), 0f);

        // ------------------------------------------------------------------ routes (workers)

        /// <summary>
        /// Points a miner walks from the ladder top: down the ladder and to their spot. Shaft miners stand on the block
        /// they clear; tunnel miners walk the ledge to the tunnel mouth and along the tunnel to its face.
        /// </summary>
        public void RouteTo(SiteSimulator sim, WorkerRecord w, int index, List<Vector3> route)
        {
            EnsureGeometry();
            route.Clear();
            route.Add(LadderTop);
            var t = sim.Tunnel(w.Face);
            if (t == null)
            {
                route.Add(LadderBottom);
                route.Add(StandSpot(index));
                return;
            }
            var level = t.Level;
            route.Add(LadderWorld(level) + new Vector3(0f, SlabRise, 0f));
            var foot = LadderFootCell;
            route.Add(FloorPoint(foot, level));
            var mouth = t.Side == TunnelSide.Left ? 0 : _ledge.Count - 1;
            var from = _ledge.IndexOf(foot);
            var step = mouth > from ? 1 : -1;
            for (var i = from; i != mouth; i += step) route.Add(FloorPoint(_ledge[i + step], level));
            var path = _walks[(int)t.Side];
            var dug = Mathf.Min(TunnelDug(level, t.Side), path.Count);
            var stand = Mathf.Max(-1, dug - 1 - index);
            for (var i = 0; i <= stand; i++) route.Add(FloorPoint(path[i], level));
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null || grid == null || blockTiles.Length == 0) { enabled = false; return; }
            EnsureGeometry();
            _workingLabel = Label("WorkingLabel");
            _nextLabel = Label("NextLabel");
        }

        private void EnsureGeometry()
        {
            if (_geometryBuilt || grid == null) return;
            _geometryBuilt = true;
            BuildOrder();

            // The ledge: the shaft's back row and back column, from the left mouth round to the right mouth.
            _ledge.Clear();
            for (var x = 0; x < Size; x++) _ledge.Add(new Vector3Int(pitOrigin.x + x, pitOrigin.y + Size - 1, 0));
            for (var y = Size - 2; y >= 0; y--) _ledge.Add(new Vector3Int(pitOrigin.x + Size - 1, pitOrigin.y + y, 0));
            _ledgeSet.Clear();
            foreach (var c in _ledge) _ledgeSet.Add(c);

            _walkIndex.Clear();
            Walk(TunnelSide.Left, new Vector3Int(pitOrigin.x, pitOrigin.y + Size - 1, 0));
            Walk(TunnelSide.Right, new Vector3Int(pitOrigin.x + Size - 1, pitOrigin.y, 0));

            // Blocks that can ever be seen: the town's exposed edge, the pit and its walls, and around the tunnels.
            var set = new HashSet<Vector3Int>();
            if (ground != null)
            {
                var b = ground.cellBounds;
                for (var x = b.xMin - 1; x <= b.xMax; x++)
                for (var y = b.yMin - 1; y <= b.yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (InPlot(c) && (!InPlot(c + Vector3Int.left) || !InPlot(c + Vector3Int.down))) set.Add(c);
                }
            }
            for (var x = 0; x <= Size; x++)
            for (var y = 0; y <= Size; y++)
                set.Add(new Vector3Int(pitOrigin.x + x, pitOrigin.y + y, 0));
            foreach (var path in _walks)
                foreach (var c in path)
                {
                    set.Add(c);
                    set.Add(c + Vector3Int.right);
                    set.Add(c + Vector3Int.up);
                }
            _candidates.Clear();
            foreach (var c in set) if (InPlot(c)) _candidates.Add(c);
        }

        private bool OffFront(Vector3Int c) => !InPlot(c) || IsPitCell(c);

        /// <summary>
        /// A tunnel's cells: from the shaft's side, step by step away from it along the exposed front, preferring cells
        /// that face the open air (so the camera sees in) and, of those, the lowest on screen (the very edge of the town).
        /// </summary>
        private void Walk(TunnelSide side, Vector3Int start)
        {
            var path = _walks[(int)side];
            path.Clear();
            var cur = start;
            var a = side == TunnelSide.Left ? Vector3Int.left : Vector3Int.right;   // −x: left-down, +x: right-up
            var b = side == TunnelSide.Left ? Vector3Int.up : Vector3Int.down;      // +y: left-up, −y: right-down
            for (var i = 0; i < MaxTunnelCells; i++)
            {
                Vector3Int? best = null;
                var bestScore = float.MaxValue;
                foreach (var n in new[] { cur + a, cur + b })
                {
                    if (!InPlot(n) || IsPitCell(n) || _walkIndex.ContainsKey(n)) continue;
                    var exposed = OffFront(n + Vector3Int.left) || OffFront(n + Vector3Int.down);
                    var score = Centre(n).y + (exposed ? 0f : 100f);
                    if (score < bestScore) { bestScore = score; best = n; }
                }
                if (best == null) break;
                cur = best.Value;
                _walkIndex[cur] = (side, path.Count);
                path.Add(cur);
            }
        }

        private void BuildOrder()
        {
            _order.Clear();
            for (var x = 0; x < Size; x++)
                for (var y = 0; y < Size; y++)
                    _order.Add(new Vector3Int(pitOrigin.x + x, pitOrigin.y + y, 0));
            _order.Sort((a, b) =>
            {
                var sa = a.x + a.y;
                var sb = b.x + b.y;
                return sa != sb ? sa.CompareTo(sb) : a.x.CompareTo(b.x);
            });
        }

        private TextMeshPro Label(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = 2.2f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Right;
            t.color = new Color32(250, 242, 226, 255);
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(59, 42, 32, 255);
            t.rectTransform.sizeDelta = new Vector2(4.5f, 0.5f);
            t.rectTransform.pivot = new Vector2(1f, 0.5f);   // right-aligned against the pit's left side
            t.sortingLayerID = SortingLayer.NameToID("Overlay");
            return t;
        }

        private void Update()
        {
            var sim = _root.Services.Sites.Get(SiteId);
            if (sim == null) return;
            if (_camera == null) _camera = Camera.main;
            var s = sim.Record;
            WorkingLevel = s.SectionsCompleted + 1;
            var awaiting = s.Status == SiteStatus.AwaitDecision;
            var progress = s.SectionDurabilityMicro > 0 ? (double)s.SectionDoneMicro / s.SectionDurabilityMicro : 0;
            BlocksRemoved = awaiting ? Blocks : Mathf.Clamp((int)(progress * Blocks), 0, Blocks);
            _removed.Clear();
            for (var i = 0; i < BlocksRemoved; i++) _removed.Add(_order[i]);
            _dug.Clear();
            foreach (var t in s.Tunnels) _dug[(t.Level, t.Side)] = Mathf.Min(sim.TunnelCellsDug(t), _walks[(int)t.Side].Count);
            if (ViewingDig && _root.InputRouter != null && _root.InputRouter.MoveVector.sqrMagnitude > 0.01f) ViewingDig = false;

            // Levels from the town's ground (1) down, limited to what the camera can see.
            int first = 1, last = WorkingLevel + Preview;
            if (_camera != null)
            {
                var top = _camera.transform.position.y + _camera.orthographicSize + 2f;
                var bottom = _camera.transform.position.y - _camera.orthographicSize - 2f;
                var surface = PitTopCentre.y;
                first = Mathf.Max(1, Mathf.FloorToInt((surface - top) / LevelHeight));
                last = Mathf.Max(first, Mathf.CeilToInt((surface - bottom) / LevelHeight) + 2);
            }
            foreach (var level in new List<int>(_levels.Keys))
                if (level < first || level > last) Release(level);
            for (var level = first; level <= last; level++) EnsureLevel(level, sim);
            UpdateLabels(sim);
            KeepOnScreen(_workingLabel);
            KeepOnScreen(_nextLabel);
        }

        /// <summary>Right-aligned labels slide right just enough to stay fully on screen on narrow views.</summary>
        private void KeepOnScreen(TextMeshPro label)
        {
            if (_camera == null || label == null) return;
            var leftEdge = _camera.transform.position.x - _camera.orthographicSize * _camera.aspect + 0.15f;
            var p = label.transform.position;
            var wanted = _labelAnchorX.TryGetValue(label, out var x) ? x : p.x;
            p.x = Mathf.Max(wanted, leftEdge + label.preferredWidth);
            label.transform.position = p;
        }

        private readonly Dictionary<TextMeshPro, float> _labelAnchorX = new Dictionary<TextMeshPro, float>();

        // ------------------------------------------------------------------ the block grid

        public int TunnelDug(int level, TunnelSide side) => _dug.TryGetValue((level, side), out var n) ? n : 0;

        private bool Dug(Vector3Int c, int level)
        {
            if (IsPitCell(c)) return level < WorkingLevel || (level == WorkingLevel && _removed.Contains(c));
            return level >= 2 && _walkIndex.TryGetValue(c, out var w) && TunnelDug(level, w.side) > w.index;
        }

        private bool Solid(Vector3Int c, int level) => InPlot(c) && !Dug(c, level);
        private bool Open(Vector3Int c, int level) => !InPlot(c) || Dug(c, level);

        private bool Visible(Vector3Int c, int level) =>
            Open(c + Vector3Int.left, level) || Open(c + Vector3Int.down, level) ||
            (level == 1 ? ground == null || !ground.HasTile(c) : Open(c, level - 1));

        private string PitState(int level) => level < 1 ? "" : level < WorkingLevel ? "all" : level == WorkingLevel ? BlocksRemoved.ToString() : "-";

        private void Release(int level)
        {
            Destroy(_levels[level].gameObject);
            _levels.Remove(level);
            if (_slabs.TryGetValue(level, out var slab)) { Destroy(slab.gameObject); _slabs.Remove(level); }
            _levelKeys.Remove(level);
            if (_ladders.TryGetValue(level, out var l)) { Destroy(l.gameObject); _ladders.Remove(level); }
        }

        private Tilemap NewMap(string name, float y, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(grid.transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var tm = go.AddComponent<Tilemap>();
            var tr = go.AddComponent<TilemapRenderer>();
            tr.mode = TilemapRenderer.Mode.Individual;
            tr.sortingLayerName = "Ground";
            tr.sortingOrder = order;
            if (spriteMaterial != null) tr.sharedMaterial = spriteMaterial;
            return tm;
        }

        private void EnsureLevel(int level, SiteSimulator sim)
        {
            var s = sim.Record;
            var depth = DepthOfLevel(s, level);
            var ledge = level >= 2 && level <= DeepestFloor;
            var key = $"{PitState(level)}|{PitState(level - 1)}|{TunnelDug(level, TunnelSide.Left)},{TunnelDug(level, TunnelSide.Right)}|" +
                      $"{TunnelDug(level - 1, TunnelSide.Left)},{TunnelDug(level - 1, TunnelSide.Right)}|{ledge}|{depth}|{level - WorkingLevel}|{s.Revealed.Count}";
            if (_levelKeys.TryGetValue(level, out var k) && k == key) return;
            _levelKeys[level] = key;

            if (!_levels.TryGetValue(level, out var tm))
            {
                // Deeper levels behind; the town surface (order 0) above all. A level's slabs sit just above its blocks.
                tm = NewMap($"MineLevel{level}", -(level - 1) * LevelHeight, -2 * level);
                _levels[level] = tm;
                _slabs[level] = NewMap($"MineSlabs{level}", -(level - 1) * LevelHeight - (LevelHeight - SlabRise), -2 * level + 1);
            }
            tm.ClearAllTiles();
            foreach (var c in _candidates)
                if (Solid(c, level) && Visible(c, level)) SetBlock(tm, c);

            var slabs = _slabs[level];
            slabs.ClearAllTiles();
            if (level >= 2 && slabTile != null)
            {
                if (ledge) foreach (var c in _ledge) slabs.SetTile(c, slabTile);
                foreach (var side in new[] { TunnelSide.Left, TunnelSide.Right })
                {
                    var path = _walks[(int)side];
                    var n = Mathf.Min(TunnelDug(level, side), path.Count);
                    for (var i = 0; i < n; i++) slabs.SetTile(path[i], slabTile);
                }
            }
            // The town's own layer matches the town's ground; the layers below carry their band's tint.
            var tint = level == 1 ? Color.white : Tint(depth, level - WorkingLevel);
            tm.color = tint;
            slabs.color = tint;
            UpdateLadder(level);
        }

        private void SetBlock(Tilemap tm, Vector3Int cell)
        {
            var h = (cell.x * 73856093) ^ (cell.y * 19349663);
            tm.SetTile(cell, blockTiles[Mathf.Abs(h) % blockTiles.Length]);
        }

        private void UpdateLadder(int level)
        {
            var needed = ladderSprite != null && level <= DeepestFloor;
            _ladders.TryGetValue(level, out var sr);
            if (!needed)
            {
                if (sr != null) { Destroy(sr.gameObject); _ladders.Remove(level); }
                return;
            }
            if (sr == null)
            {
                var go = new GameObject($"Ladder{level}");
                go.transform.SetParent(transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ladderSprite;
                sr.sortingLayerName = "Ground";
                sr.sortingOrder = -2 * level + 1;
                if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
                _ladders[level] = sr;
            }
            sr.transform.position = LadderAt(level + 1) + new Vector3(0f, 0.02f, 0f);   // pivot at the segment's foot
        }

        /// <summary>Which depth band a level was (or will be) dug in.</summary>
        public static int DepthOfLevel(SiteRecord s, int level)
        {
            if (level >= 1 && level <= s.SectionDepths.Count) return s.SectionDepths[level - 1];
            return s.Depth;   // the working level, and below (the layer continues unless you dig deeper)
        }

        private static readonly Color[] Bands =
        {
            Color.white, new Color(0.96f, 0.86f, 0.78f), new Color(0.86f, 0.84f, 0.88f),
            new Color(0.98f, 0.92f, 0.80f), new Color(0.84f, 0.80f, 0.78f), new Color(0.92f, 0.86f, 0.90f),
        };

        /// <summary>The layer's earth tint (its ore mix over a base that alternates by depth), darker below the dig.</summary>
        public Color Tint(int depth, int belowWorking)
        {
            var sim = _root != null ? _root.Services.Sites.Get(SiteId) : null;
            var col = Bands[(Mathf.Max(1, depth) - 1) % Bands.Length];
            if (sim != null && sim.Record.Revealed.TryGetValue(depth, out var comp) && comp != null)
            {
                var ore = Color.black;
                foreach (var w in comp.Weights)
                    ore += (_root.Services.Catalogue.TryGet<ResourceDefinition>(w.Key, out var r) ? r.MapTint : Color.gray) * (float)w.Value;
                col = Color.Lerp(col, Color.Lerp(Color.white, ore, 0.6f), 0.5f);
            }
            if (belowWorking > 0) col = Color.Lerp(col, new Color(0.35f, 0.3f, 0.32f), Mathf.Clamp01(0.35f + 0.18f * belowWorking));
            col.a = 1f;
            return col;
        }

        private void UpdateLabels(SiteSimulator sim)
        {
            var s = sim.Record;
            var key = $"{WorkingLevel}|{s.Depth}|{s.K}|{sim.CanGoDeeper}|{s.Status}";
            if (key == _labelKey) return;
            _labelKey = key;
            // Labels sit left of the shaft, over the notch's open side (the right of the screen holds the buttons).
            var leftCorner = Centre(new Vector3Int(pitOrigin.x, pitOrigin.y + Size - 1, 0)) + new Vector3(-0.5f, 0f, 0f);
            Vector3 FaceMid(int level) => leftCorner + new Vector3(-0.15f, -(level - 1) * LevelHeight - LevelHeight * 0.5f, 0f);
            _workingLabel.transform.position = FaceMid(WorkingLevel);
            _labelAnchorX[_workingLabel] = FaceMid(WorkingLevel).x;
            _labelAnchorX[_nextLabel] = FaceMid(WorkingLevel + 1).x;
            _workingLabel.text = $"Depth {s.Depth} · level {WorkingLevel}" + (s.Status == SiteStatus.AwaitDecision ? " · cleared" : "");
            _nextLabel.transform.position = FaceMid(WorkingLevel + 1);
            _nextLabel.text = sim.CanGoDeeper ? $"below: depth {s.Depth} or {s.Depth + 1}" : "below: this layer continues";
            _nextLabel.fontSize = 1.6f;
        }
    }
}
