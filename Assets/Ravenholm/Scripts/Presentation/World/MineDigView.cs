using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The ground under the town and the frontier as one block grid (GDD 6: "visibly stacked geological layers").
    ///
    /// Every cell of the isometric grid is a column of ground blocks, one miner tall per level. Level 1 is the town's
    /// own ground (its tops are the town tiles); each level below is one Tilemap on the same grid, offset one level down.
    /// A block is drawn only when the camera can see it: a front neighbour is open (off the edge, or dug) or the cell
    /// above is open. So the exposed edges show as a cliff of layers, and whatever the miners dig opens into it.
    ///
    /// The Mine has two directions (GDD 6, as directed):
    ///   - down is progression: the shaft, a 4×4 pit at the town's bottom-left edge, sinks through one layer (three
    ///     levels) per "Dig Deeper" section, blocks clearing top level first and front first;
    ///   - left is staying in a layer: each level has its own tunnel ("Continue Here"), running left along the bottom
    ///     edge under the Forest. Tunnels are recessed one row behind the cliff face: the front blocks are cut down to a
    ///     low dirt sill and the tunnel floor is a slab of dirt left between levels.
    /// A dirt ledge along the shaft's back wall joins the ladder (right wall) to the tunnel mouths at every level.
    /// Layers are tinted by their band; undug ground darkens with depth. Only levels near the camera exist, so depth and
    /// length are unlimited. Presentation only: what is dug comes from the site simulation.
    /// </summary>
    public sealed class MineDigView : MonoBehaviour
    {
        public const string SiteId = "site.mine";
        public const int Size = 4;              // pit cells per side
        public const float LevelHeight = 1f;    // one level = one miner tall (world units)
        public const float SlabRise = 0.25f;    // ledge and tunnel floors: the slab of dirt left between levels
        public const float SillRise = 0.5f;     // the cut-down front blocks in front of a tunnel
        public const int Blocks = Size * Size;
        private const int Preview = 3;
        private const int MaxTunnelCells = 400;

        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap ground;
        [SerializeField] private Vector3Int pitOrigin;   // front cell of the pit (lowest x and y), on the bottom row
        [SerializeField] private TileBase[] blockTiles = new TileBase[0];
        [SerializeField] private TileBase slabTile;
        [SerializeField] private Sprite ladderSprite;
        [SerializeField] private Material spriteMaterial;

        public static MineDigView Current { get; private set; }

        private GameRoot _root;
        private Camera _camera;
        private SiteSimulator _sim;
        private readonly Dictionary<int, Tilemap> _levels = new Dictionary<int, Tilemap>();
        private readonly Dictionary<int, Tilemap> _slabs = new Dictionary<int, Tilemap>();
        private readonly Dictionary<int, Tilemap> _sills = new Dictionary<int, Tilemap>();
        private readonly Dictionary<int, string> _levelKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, SpriteRenderer> _ladders = new Dictionary<int, SpriteRenderer>();
        private readonly List<Vector3Int> _order = new List<Vector3Int>();
        private readonly HashSet<Vector3Int> _removed = new HashSet<Vector3Int>();
        private readonly List<Vector3Int> _path = new List<Vector3Int>();
        private readonly Dictionary<Vector3Int, int> _pathIndex = new Dictionary<Vector3Int, int>();
        private readonly Dictionary<Vector3Int, int> _sillIndex = new Dictionary<Vector3Int, int>();
        private readonly List<Vector3Int> _ledge = new List<Vector3Int>();
        private readonly HashSet<Vector3Int> _ledgeSet = new HashSet<Vector3Int>();
        private readonly List<Vector3Int> _candidates = new List<Vector3Int>();
        private readonly Dictionary<int, int> _dug = new Dictionary<int, int>();   // level → tunnel cells dug
        private TextMeshPro _workingLabel, _nextLabel;
        private string _labelKey = "";
        private bool _geometryBuilt;
        private int _fullLevels;       // levels 1.._fullLevels: every pit block dug
        private int _partialLevel;     // the level the shaft is clearing now (0 = none)

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

        /// <summary>The level the shaft is clearing now (or the first level below the dug shaft while it waits).</summary>
        public int WorkingLevel => _partialLevel > 0 ? _partialLevel : _fullLevels + 1;
        /// <summary>Blocks of the working level cleared so far.</summary>
        public int BlocksRemoved => _partialLevel > 0 ? _removed.Count : 0;
        public int LevelsBuilt => _levels.Count;
        /// <summary>When true the camera looks at the working level until the player moves (Mine panel "Look down").</summary>
        public bool ViewingDig { get; set; }

        /// <summary>Pit cells in dig order: front first, so the cut is visible from the camera.</summary>
        public IReadOnlyList<Vector3Int> DigOrder => _order;

        /// <summary>The cells a tunnel digs, in order, from the shaft leftwards (every level uses the same path).</summary>
        public IReadOnlyList<Vector3Int> TunnelPath
        {
            get { EnsureGeometry(); return _path; }
        }

        /// <summary>Blocks a tunnel can reach before the end of the charted frontier.</summary>
        public int TunnelCapacityCells => TunnelPath.Count;

        public IReadOnlyList<Vector3Int> LedgeCells
        {
            get { EnsureGeometry(); return _ledge; }
        }

        public bool IsPitCell(Vector3Int c) => c.x >= pitOrigin.x && c.x < pitOrigin.x + Size && c.y >= pitOrigin.y && c.y < pitOrigin.y + Size;

        /// <summary>Part of the ground columns (the pit included; the notch in front of it is open).</summary>
        public bool InPlot(Vector3Int c) => IsPitCell(c) || (ground != null && ground.HasTile(c));

        private Vector3 Centre(Vector3Int c) => grid.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));
        private Vector3 CellTop(Vector3Int c, int level) => Centre(c) + new Vector3(0f, -(level - 1) * LevelHeight, 0f);

        /// <summary>Deepest level the player can stand at: on top of the first undug level (0 while nothing is dug).</summary>
        public int DeepestFloor => _fullLevels;

        /// <summary>Where a shaft miner stands: on the next block to clear, or on the floor while the shaft waits.</summary>
        public Vector3 StandSpot(int index)
        {
            if (_order.Count == 0) BuildOrder();
            if (_partialLevel <= 0)
                return FloorPoint(new Vector3Int(pitOrigin.x + 1 + (index % 2), pitOrigin.y + 1, 0), Mathf.Max(1, _fullLevels));
            var i = Mathf.Min(_removed.Count + index, Blocks - 1);
            return CellTop(_order[i], _partialLevel);
        }

        /// <summary>The ladder stands on the shaft's right back wall; its top cell is town ground.</summary>
        public Vector3Int LadderTopCell => new Vector3Int(pitOrigin.x + Size, pitOrigin.y + 2, 0);
        public Vector3Int LadderFootCell => new Vector3Int(pitOrigin.x + Size - 1, pitOrigin.y + 2, 0);

        /// <summary>Middle of the edge between the ladder's top cell and the shaft (the plane point the ladder stands on).</summary>
        public Vector2 LadderPlane => Centre(LadderTopCell) + new Vector3(-0.25f, -0.125f, 0f);

        private static readonly Vector3 LadderLean = new Vector3(-0.08f, -0.06f, 0f);

        /// <summary>A body on the ladder, <paramref name="h"/> levels below the town surface.</summary>
        public Vector3 LadderWorld(float h) => (Vector3)LadderPlane + LadderLean + new Vector3(0f, -h * LevelHeight, 0f);

        public Vector3 LadderTop => LadderWorld(0);
        public Vector3 LadderBottom => LadderWorld(DeepestFloor);

        /// <summary>Middle of the pit at the working floor (camera focus for "Look down the shaft").</summary>
        public Vector3 DigFocus => Centre(new Vector3Int(pitOrigin.x + 2, pitOrigin.y + 2, 0)) + new Vector3(0f, -(WorkingLevel - 1) * LevelHeight - 0.5f, 0f);

        public Vector3 PitTopCentre => Centre(new Vector3Int(pitOrigin.x + 2, pitOrigin.y + 2, 0));

        /// <summary>World height of the deepest place anyone can stand (camera clamp).</summary>
        public float DeepestFloorY => PitTopCentre.y - Mathf.Max(DeepestFloor, 0) * LevelHeight;

        // ------------------------------------------------------------------ walking (player)

        public Vector3Int CellAt(Vector2 plane) => grid.WorldToCell(plane);

        /// <summary>True where the town's own ground can be walked on (not the pit or the notch; not a standing tree).</summary>
        public bool SurfaceWalkable(Vector2 plane)
        {
            if (ground == null) return true;
            var c = CellAt(plane);
            if (!ground.HasTile(c) || IsPitCell(c)) return false;
            return ForestGridView.Current == null || !ForestGridView.Current.HasStandingTree(c);
        }

        /// <summary>A slab floor (ledge or dug tunnel) at this level.</summary>
        public bool IsSlab(Vector3Int c, int level)
        {
            if (level < 2) return false;
            EnsureGeometry();
            if (_ledgeSet.Contains(c)) return level <= DeepestFloor;
            return _pathIndex.TryGetValue(c, out var i) && TunnelDug(level) > i;
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
                route.Add(LadderWorld(Mathf.Max(DeepestFloor, _partialLevel > 0 ? _partialLevel - 1 : DeepestFloor)));
                route.Add(StandSpot(index));
                return;
            }
            var level = t.Level;
            route.Add(LadderWorld(level) + new Vector3(0f, SlabRise, 0f));
            for (var i = _ledge.Count - 1; i >= 0; i--) route.Add(FloorPoint(_ledge[i], level));
            var dug = Mathf.Min(TunnelDug(level), _path.Count);
            var stand = Mathf.Max(-1, dug - 1 - index);
            for (var i = 0; i <= stand; i++) route.Add(FloorPoint(_path[i], level));
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

            // The ledge, from the tunnel mouth along the back row to the ladder's foot on the right wall.
            _ledge.Clear();
            for (var x = 0; x < Size; x++) _ledge.Add(new Vector3Int(pitOrigin.x + x, pitOrigin.y + Size - 1, 0));
            _ledge.Add(LadderFootCell);
            _ledgeSet.Clear();
            foreach (var c in _ledge) _ledgeSet.Add(c);

            // The tunnel: left from the shaft's back-left cell, one row behind the bottom edge's front row, zigzagging on
            // the grid (left-down, then left-up) so it runs straight left on screen.
            _path.Clear();
            _pathIndex.Clear();
            var cur = new Vector3Int(pitOrigin.x - 1, pitOrigin.y + Size - 1, 0);
            for (var i = 0; i < MaxTunnelCells && InPlot(cur); i++)
            {
                _pathIndex[cur] = _path.Count;
                _path.Add(cur);
                cur += i % 2 == 0 ? Vector3Int.left : Vector3Int.up;
            }
            // Sills: whatever stands in front of a tunnel cell (down to the cliff face) is cut down to a low lip.
            _sillIndex.Clear();
            for (var i = 0; i < _path.Count; i++)
            {
                var frontier = new List<Vector3Int> { _path[i] };
                for (var step = 0; step < 3; step++)
                {
                    var next = new List<Vector3Int>();
                    foreach (var c in frontier)
                        foreach (var f in new[] { c + Vector3Int.left, c + Vector3Int.down })
                        {
                            if (!InPlot(f) || IsPitCell(f) || _pathIndex.ContainsKey(f) || _sillIndex.ContainsKey(f)) continue;
                            _sillIndex[f] = i;
                            next.Add(f);
                        }
                    frontier = next;
                }
            }

            // Blocks that can ever be seen: the exposed edge, the pit and its walls, and around the tunnels.
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
            for (var x = -1; x <= Size; x++)
            for (var y = -1; y <= Size; y++)
                set.Add(new Vector3Int(pitOrigin.x + x, pitOrigin.y + y, 0));
            foreach (var c in _path)
            {
                set.Add(c);
                set.Add(c + Vector3Int.right);
                set.Add(c + Vector3Int.up);
                set.Add(c + Vector3Int.right + Vector3Int.up);
            }
            foreach (var c in _sillIndex.Keys) set.Add(c);
            _candidates.Clear();
            foreach (var c in set) if (InPlot(c)) _candidates.Add(c);
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
            t.alignment = TextAlignmentOptions.Left;
            t.color = new Color32(250, 242, 226, 255);
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(59, 42, 32, 255);
            t.rectTransform.sizeDelta = new Vector2(4.5f, 0.5f);
            t.rectTransform.pivot = new Vector2(0f, 0.5f);   // left-aligned against the shaft's right side (the open notch)
            t.sortingLayerID = SortingLayer.NameToID("Overlay");
            return t;
        }

        private void Update()
        {
            var sim = _root.Services.Sites.Get(SiteId);
            if (sim == null) return;
            _sim = sim;
            if (_camera == null) _camera = Camera.main;
            ReadShaft(sim);
            _dug.Clear();
            foreach (var t in sim.Record.Tunnels) _dug[t.Level] = Mathf.Min(sim.TunnelCellsDug(t), _path.Count);
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

        private readonly Dictionary<TextMeshPro, float> _labelAnchorX = new Dictionary<TextMeshPro, float>();

        /// <summary>Left-aligned labels slide left just enough to stay fully on screen on narrow views.</summary>
        private void KeepOnScreen(TextMeshPro label)
        {
            if (_camera == null || label == null) return;
            var rightEdge = _camera.transform.position.x + _camera.orthographicSize * _camera.aspect - 0.15f;
            var p = label.transform.position;
            var wanted = _labelAnchorX.TryGetValue(label, out var x) ? x : p.x;
            p.x = Mathf.Min(wanted, rightEdge - label.preferredWidth);
            label.transform.position = p;
        }

        /// <summary>Which shaft levels are dug: the layers already sunk through, and the one being sunk now, top level first.</summary>
        private void ReadShaft(SiteSimulator sim)
        {
            var s = sim.Record;
            _removed.Clear();
            if (sim.ShaftDone)
            {
                _fullLevels = sim.OpenLevels;
                _partialLevel = 0;
                return;
            }
            var lo = s.Depth == 1 ? 1 : sim.FirstLevelOf(s.Depth);
            var hi = sim.FirstLevelOf(s.Depth) + sim.LevelsPerBand - 1;
            var progress = s.SectionDurabilityMicro > 0 ? (double)s.SectionDoneMicro / s.SectionDurabilityMicro : 0;
            var blocks = Mathf.Clamp((int)(progress * Blocks * (hi - lo + 1)), 0, Blocks * (hi - lo + 1));
            _fullLevels = lo - 1 + blocks / Blocks;
            _partialLevel = _fullLevels + 1;
            if (_partialLevel > hi) { _partialLevel = 0; return; }
            for (var i = 0; i < blocks % Blocks; i++) _removed.Add(_order[i]);
        }

        // ------------------------------------------------------------------ the block grid

        public int TunnelDug(int level) => _dug.TryGetValue(level, out var n) ? n : 0;

        private bool PitDug(Vector3Int c, int level) => level <= _fullLevels || (level == _partialLevel && _removed.Contains(c));

        private bool Dug(Vector3Int c, int level)
        {
            if (IsPitCell(c)) return PitDug(c, level);
            if (level < 2) return false;
            var n = TunnelDug(level);
            if (n <= 0) return false;
            return (_pathIndex.TryGetValue(c, out var i) && n > i) || (_sillIndex.TryGetValue(c, out var j) && n > j);
        }

        private bool Solid(Vector3Int c, int level) => InPlot(c) && !Dug(c, level);
        private bool Open(Vector3Int c, int level) => !InPlot(c) || Dug(c, level);

        private bool Visible(Vector3Int c, int level) =>
            Open(c + Vector3Int.left, level) || Open(c + Vector3Int.down, level) ||
            (level == 1 ? ground == null || !ground.HasTile(c) : Open(c, level - 1));

        private string PitState(int level) =>
            level < 1 ? "" : level <= _fullLevels ? "all" : level == _partialLevel ? _removed.Count.ToString() : "-";

        private void Release(int level)
        {
            Destroy(_levels[level].gameObject);
            _levels.Remove(level);
            if (_slabs.TryGetValue(level, out var slab)) { Destroy(slab.gameObject); _slabs.Remove(level); }
            if (_sills.TryGetValue(level, out var sill)) { Destroy(sill.gameObject); _sills.Remove(level); }
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
            var band = DepthOfLevel(sim, level);
            var ledge = level >= 2 && level <= DeepestFloor;
            var key = $"{PitState(level)}|{PitState(level - 1)}|{TunnelDug(level)}|{TunnelDug(level - 1)}|{ledge}|{band}|{level - WorkingLevel}|{sim.Record.Revealed.Count}";
            if (_levelKeys.TryGetValue(level, out var k) && k == key) return;
            _levelKeys[level] = key;

            if (!_levels.TryGetValue(level, out var tm))
            {
                // Deeper levels behind; the town surface (order 0) above all. A level's slabs and sills sit above its blocks.
                var top = -(level - 1) * LevelHeight;
                tm = NewMap($"MineLevel{level}", top, -3 * level);
                _levels[level] = tm;
                _slabs[level] = NewMap($"MineSlabs{level}", top - (LevelHeight - SlabRise), -3 * level + 1);
                _sills[level] = NewMap($"MineSills{level}", top - (LevelHeight - SillRise), -3 * level + 2);
            }
            tm.ClearAllTiles();
            foreach (var c in _candidates)
                if (Solid(c, level) && Visible(c, level)) SetBlock(tm, c);

            var slabs = _slabs[level];
            var sills = _sills[level];
            slabs.ClearAllTiles();
            sills.ClearAllTiles();
            if (level >= 2 && slabTile != null)
            {
                if (ledge) foreach (var c in _ledge) slabs.SetTile(c, slabTile);
                var n = Mathf.Min(TunnelDug(level), _path.Count);
                for (var i = 0; i < n; i++) slabs.SetTile(_path[i], slabTile);
                foreach (var kv in _sillIndex)
                    if (kv.Value < n)
                    {
                        slabs.SetTile(kv.Key, slabTile);   // the sill's lower half
                        sills.SetTile(kv.Key, slabTile);   // and its lip
                    }
            }
            // The town's own layer matches the town's ground; the layers below carry their band's tint.
            var tint = level == 1 ? Color.white : Tint(band, level - Mathf.Max(DeepestFloor, WorkingLevel));
            tm.color = tint;
            slabs.color = tint;
            sills.color = tint * new Color(1.06f, 1.06f, 1.06f, 1f);
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
                sr.flipX = true;   // on the right back wall, facing down-left
                sr.sortingLayerName = "Ground";
                sr.sortingOrder = -3 * level + 2;
                if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
                _ladders[level] = sr;
            }
            sr.transform.position = LadderWorld(level) + new Vector3(0f, 0.02f, 0f);   // pivot at the segment's foot
        }

        /// <summary>Which band (layer) a level belongs to: layers are a fixed number of levels tall.</summary>
        public static int DepthOfLevel(SiteSimulator sim, int level) => sim.BandOfLevel(level);

        private static readonly Color[] Bands =
        {
            new Color(0.96f, 0.86f, 0.78f), new Color(0.86f, 0.84f, 0.88f), new Color(0.98f, 0.92f, 0.80f),
            new Color(0.84f, 0.80f, 0.78f), new Color(0.92f, 0.86f, 0.90f), new Color(0.90f, 0.90f, 0.82f),
        };

        /// <summary>The layer's earth tint (its ore mix over a base that alternates by band), darker below the dig.</summary>
        public Color Tint(int depth, int belowDug)
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
            else if (belowDug <= 0) belowDug = 1;   // an unrevealed layer is unknown ground
            if (belowDug > 0) col = Color.Lerp(col, new Color(0.35f, 0.3f, 0.32f), Mathf.Clamp01(0.3f + 0.12f * belowDug));
            col.a = 1f;
            return col;
        }

        private void UpdateLabels(SiteSimulator sim)
        {
            var s = sim.Record;
            var key = $"{WorkingLevel}|{s.Depth}|{sim.ShaftDone}|{sim.CanGoDeeper}|{s.Tunnels.Count}";
            if (key == _labelKey) return;
            _labelKey = key;
            // Labels sit right of the shaft, over its open notch.
            var rightCorner = Centre(new Vector3Int(pitOrigin.x + Size - 1, pitOrigin.y, 0)) + new Vector3(0.6f, 0f, 0f);
            Vector3 FaceMid(int level) => rightCorner + new Vector3(0.15f, -(level - 1) * LevelHeight - LevelHeight * 0.5f, 0f);
            var layerTop = s.Depth == 1 ? 2 : sim.FirstLevelOf(s.Depth);
            _workingLabel.transform.position = FaceMid(layerTop);
            _labelAnchorX[_workingLabel] = FaceMid(layerTop).x;
            _labelAnchorX[_nextLabel] = FaceMid(layerTop + sim.LevelsPerBand).x;
            _workingLabel.text = sim.ShaftDone ? $"Depth {s.Depth} · levels {layerTop}–{layerTop + sim.LevelsPerBand - 1}" : $"Sinking to depth {s.Depth}";
            _nextLabel.transform.position = FaceMid(layerTop + sim.LevelsPerBand);
            _nextLabel.text = sim.CanGoDeeper ? $"below: depth {s.Depth + 1}" : "below: not yet charted";
            _nextLabel.fontSize = 1.6f;
        }
    }
}
