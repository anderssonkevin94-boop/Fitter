using System.Collections;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    [System.Serializable]
    public struct SpeciesSprite
    {
        public ResourceDefinition resource;
        public Sprite sprite;
    }

    /// <summary>
    /// The Forest on the frontier (GDD 7, as directed): a dense grid of trees, one per tile, on the frontier beyond the
    /// town's upper-left edge, directly above the Mine's tunnels. It has the same two directions as the Mine, on the grid:
    ///   - left is progression: each area (band) is the next few tiles along the cliff edge (up-left, "Push Deeper");
    ///   - up is staying in an area: each clearing ("Continue Here") is the next rows back from the edge (up-right).
    /// Trees are cut in order from the cliff edge back, so the woodcutters (and the supplier) physically cut their way in:
    /// only cut tiles can be walked. They walk the trail along the town, then the cut edge row out to their area. The current clearing's trees are the simulation's trees (species, felled);
    /// earlier clearings are stumps; ground not yet reached stays forest, darker in areas not yet entered.
    /// Only trees near the camera are drawn. Presentation only.
    /// </summary>
    public sealed partial class ForestGridView : MonoBehaviour
    {
        [SerializeField] private string siteId = "site.forest";
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap ground;
        [Tooltip("The cliff-edge cell where the first area starts (x = the frontier's front edge, y = past the trail).")]
        [SerializeField] private Vector2Int start;
        [Tooltip("Tiles along the cliff edge per area.")]
        [Min(1)] [SerializeField] private int areaLength = 3;
        [Tooltip("The open trail between the town and the first area: where woodcutters enter the frontier.")]
        [SerializeField] private Vector2Int trailCell;
        [SerializeField] private float treeScale = 0.62f;   // the prototype's size (2026-10-15)
        [SerializeField] private List<SpeciesSprite> speciesSprites = new List<SpeciesSprite>();
        [SerializeField] private Sprite cedarSprite;
        [SerializeField] private Sprite mapleSprite;
        [SerializeField] private Sprite stumpSprite;
        [SerializeField] private List<ResourceDefinition> broadleafSpecies = new List<ResourceDefinition>();
        [SerializeField] private Material spriteMaterial;

        public static ForestGridView Current { get; private set; }

        private GameRoot _root;
        private Camera _camera;
        private readonly List<List<Vector3Int>> _columns = new List<List<Vector3Int>>();   // per area: cells bottom-up
        private readonly Dictionary<Vector3Int, (int area, int slot)> _slotOf = new Dictionary<Vector3Int, (int, int)>();
        private readonly List<(Vector3Int cell, int area, int slot)> _all = new List<(Vector3Int, int, int)>();   // every tree tile
        private bool _ring;   // a camp's ridge: a tree's (area, slot) is its tile's (x, y)
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _shown = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<int, Coroutine> _shakes = new Dictionary<int, Coroutine>();
        private string _key = "";
        private bool _built;

        public void EditorSetup(Grid g, Tilemap groundMap, Vector2Int firstCell, Vector2Int trail, Sprite cedar, Sprite maple, Sprite stump,
            List<ResourceDefinition> broadleaf, List<SpeciesSprite> species, Material mat)
        {
            grid = g;
            ground = groundMap;
            start = firstCell;
            trailCell = trail;
            cedarSprite = cedar;
            mapleSprite = maple;
            stumpSprite = stump;
            broadleafSpecies = broadleaf;
            speciesSprites = species;
            spriteMaterial = mat;
        }

        public int ShownDepth { get; private set; } = -1;

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null && _root.Services != null) _root.Services.Sites.UnitEarned -= OnUnitEarned;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _root.Services.Sites.UnitEarned += OnUnitEarned;
            Build();
        }

        // ------------------------------------------------------------------ geometry

        private Vector3 Centre(Vector3Int c) => IsoMap.World(c);


        /// <summary>The ground was laid anew (another level, gated levels 2026-10-16): the trees' cells from it again.</summary>
        public void Rebuild()
        {
            _built = false;
            _slotOf.Clear();
            _key = "";
            Build();
        }

        private void Build()
        {
            if (_built || grid == null || ground == null) return;
            var sim = Sim;
            if (sim == null) return;   // the layout comes from the Forest's grove
            _built = true;
            var b = ground.cellBounds;
            _columns.Clear();
            _columns.Add(new List<Vector3Int>());   // areas are 1-based
            _all.Clear();
            var grove = sim.Config.Grove;
            _ring = grove != null && grove.Ridge != null;
            if (_ring)
            {
                // A camp (2026-10-15): every ground tile of the ridge's forest is a tree tile, named by its own cell.
                for (var x = b.xMin; x < b.xMax; x++)
                for (var y = b.yMin; y < b.yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (!ground.HasTile(c) || !grove.InGrove(x, y)) continue;
                    _slotOf[c] = (x, y);
                    _all.Add((c, x, y));
                }
                return;
            }
            var byArea = new SortedDictionary<int, List<Vector3Int>>();
            for (var x = b.xMin; x < b.xMax; x++)
            for (var y = b.yMin; y < b.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (!ground.HasTile(c) || x < start.x || y < start.y) continue;
                var area = (y - start.y) / areaLength + 1;
                if (!byArea.TryGetValue(area, out var list)) byArea[area] = list = new List<Vector3Int>();
                list.Add(c);
            }
            for (var a = 1; byArea.ContainsKey(a); a++)
            {
                var list = byArea[a];
                // The cliff-edge row first, then each row further back; along a row, nearest the town first.
                list.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
                for (var i = 0; i < list.Count; i++) { _slotOf[list[i]] = (a, i); _all.Add((list[i], a, i)); }
                _columns.Add(list);
            }
        }

        public int AreaCount => _columns.Count - 1;

        /// <summary>The tile tree <paramref name="index"/> of the current clearing stands on.</summary>
        public Vector3Int TreeCell(int index)
        {
            Build();
            var sim = Sim;
            if (sim == null) return Vector3Int.zero;
            var s = sim.Record;
            var col = Column(s.Depth);
            if (col.Count == 0) return Vector3Int.zero;
            var slot = s.K * sim.Config.TreesPerClearing + index;
            return col[Mathf.Clamp(slot, 0, col.Count - 1)];
        }

        private List<Vector3Int> Column(int area) => area >= 1 && area < _columns.Count ? _columns[area] : _columns.Count > 0 ? _columns[0] : new List<Vector3Int>();

        /// <summary>Where a woodcutter stands to chop tree <paramref name="index"/>: just in front of it, on cut ground.</summary>
        public Vector3 WorkSpot(int index) => Centre(TreeCell(index)) + new Vector3(0.1f, -0.32f, 0f);

        /// <summary>
        /// The trail from staging to a tree, over cut ground only: out along the bottom edge (every earlier area's first
        /// clearing), then either up the cut edge of the previous area (a new area's first trees) or up this area's own
        /// cut clearings to the trees being cut now.
        /// </summary>
        public void RouteTo(Vector3 staging, int treeIndex, List<Vector3> route)
        {
            route.Clear();
            route.Add(staging);
            var sim = Sim;
            if (sim == null) { route.Add(WorkSpot(treeIndex)); return; }
            Build();
            var s = sim.Record;
            var spot = WorkSpot(treeIndex);
            // Into the frontier by the trail, then out along the cut cliff-edge row (every earlier area's first clearing).
            route.Add(Centre((Vector3Int)trailCell));
            route.Add(Centre(new Vector3Int(start.x, trailCell.y, 0)));
            if (s.Depth > 1) route.Add(Centre(new Vector3Int(start.x, start.y + (s.Depth - 1) * areaLength - 1, 0)));
            var col = Column(s.Depth);
            var cut = Mathf.Min(s.K * sim.Config.TreesPerClearing, col.Count);
            if (cut > 0)
            {
                route.Add(Centre(col[0]));
                route.Add(Centre(col[cut - 1]));
            }
            route.Add(spot);
        }

        /// <summary>True where a tree still stands (frontier tiles only); the trail and cut tiles are open.</summary>
        public bool HasStandingTree(Vector3Int c)
        {
            Build();
            if (!_slotOf.TryGetValue(c, out var at)) return false;
            var state = State(at.area, at.slot, out _);
            return state == TreeState.Standing || state == TreeState.Current;
        }

        private enum TreeState
        {
            Standing,
            Current,
            Cut,
            None,   // no tree: bare rock, or a stump pulled out
        }

        private SiteSimulator Sim => _root != null ? _root.Services.Sites.Get(siteId) : null;

        private int ClearingsDone(int area)
        {
            var s = Sim.Record;
            if (area == s.Depth) return s.K;
            var n = 0;
            foreach (var d in s.SectionDepths) if (d == area) n++;
            return n;
        }

        private TreeState State(int area, int slot, out int treeIndex)
        {
            treeIndex = -1;
            var sim = Sim;
            if (sim == null) return TreeState.Standing;
            if (sim.GroveMode)
            {
                // The grid: any tree. Bare rock has none; a pulled-out stump leaves open ground for good.
                if (sim.IsRock(area, slot) || sim.IsCleared(area, slot)) return TreeState.None;
                return sim.IsFelled(area, slot) ? TreeState.Cut : TreeState.Standing;
            }
            var s = sim.Record;
            if (area > s.Depth) return TreeState.Standing;
            var per = sim.Config.TreesPerClearing;
            var done = ClearingsDone(area) * per;
            if (slot < done) return TreeState.Cut;
            if (area < s.Depth) return TreeState.Standing;
            var i = slot - done;
            if (i >= per || i >= s.Trees.Count) return TreeState.Standing;
            treeIndex = i;
            return s.Trees[i].Felled ? TreeState.Cut : TreeState.Current;
        }

        // ------------------------------------------------------------------ drawing

        private void Update()
        {
            var sim = Sim;
            if (sim == null) return;
            if (_camera == null) _camera = Camera.main;
            var s = sim.Record;
            ShownDepth = s.Depth;
            var felled = 0;
            foreach (var t in s.Trees) if (t.Felled) felled++;
            var cam = _camera != null ? (Vector2)_camera.transform.position : Vector2.zero;
            UpdateSupplier();
            UpdateHighlight();
            FadeTrees();
            var dug = Mine != null ? Mine.Record.SurfaceBroken.Count : 0;
            var key = $"{Mathf.Round(cam.x * 2f)}|{Mathf.Round(cam.y * 2f)}|{s.Depth}|{s.K}|{s.SectionDepths.Count}|{felled}|{s.Trees.Count}|{sim.FelledVersion}|{s.Revealed.Count}|{dug}";
            if (key == _key) return;
            _key = key;
            Redraw(sim, cam);
        }

        // ------------------------------------------------------------------ trees in front of the supplier

        /// <summary>How see-through a tree is while the supplier stands behind it (as the buildings fade).</summary>
        public const float FadedTreeAlpha = 0.25f;

        private readonly Dictionary<Vector3Int, float> _treeAlpha = new Dictionary<Vector3Int, float>();
        private UnityEngine.Rendering.SortingGroup _playerGroup;
        private readonly List<Vector3Int> _fadeDone = new List<Vector3Int>();

        /// <summary>Whether a tree is faded now (tests).</summary>
        public bool TreeFaded(Vector3Int c) => _treeAlpha.TryGetValue(c, out var a) && a < 0.99f;

        /// <summary>
        /// Trees fade while the supplier is behind them (the playtester, 2026-10-17: pines in front hid him almost
        /// completely; the market stall fades but the trees didn't): a tree standing lower on the screen than him whose
        /// picture covers his middle eases to 40 %, and back when he steps out.
        /// </summary>
        private void FadeTrees()
        {
            var c = SandboxSceneController.Current;
            if (c == null || c.Player == null) return;
            var p = (Vector2)c.Player.position;
            // Drawn over him: on his layer (the Ground, where he is sorted among the Forest's blocks and trees) after him.
            // Judged by the draw order, not the screen height: a tree on a hill, or in his own row, can be drawn over him
            // while standing "behind" him on screen (the third playtest: solid pines still hid him).
            if (_playerGroup == null) _playerGroup = c.Player.GetComponent<UnityEngine.Rendering.SortingGroup>();
            var amongTrees = _playerGroup != null && _playerGroup.sortingLayerName == "Ground";
            var playerOrder = _playerGroup != null ? _playerGroup.sortingOrder : int.MaxValue;
            // Any of him covered: his feet, his middle, his head, a little either side (2026-10-17, second playtest: in
            // thick forest only the tree over his middle faded and he was lost behind the rows in front).
            Vector2[] points = { p + new Vector2(0f, 0.15f), p + new Vector2(0f, 0.5f), p + new Vector2(0f, 0.85f),
                                 p + new Vector2(-0.18f, 0.5f), p + new Vector2(0.18f, 0.5f) };
            var step = Time.deltaTime * 6f;
            foreach (var kv in _shown)
            {
                var sr = kv.Value;
                if (sr == null || !sr.enabled) continue;
                var at = (Vector2)sr.transform.position;
                var behind = false;
                if (amongTrees && sr.sortingOrder >= playerOrder && Mathf.Abs(at.x - p.x) < 1.8f)
                    foreach (var q in points)
                        if (sr.bounds.Contains(new Vector3(q.x, q.y, sr.bounds.center.z))) { behind = true; break; }
                var has = _treeAlpha.TryGetValue(kv.Key, out var a);
                if (!has && !behind) continue;
                if (!has) a = 1f;
                a = Mathf.MoveTowards(a, behind ? FadedTreeAlpha : 1f, step);
                _treeAlpha[kv.Key] = a;
                SetAlpha(sr, a);
                if (!behind && a >= 1f) _fadeDone.Add(kv.Key);
            }
            foreach (var k in _fadeDone) _treeAlpha.Remove(k);
            _fadeDone.Clear();
        }

        private static void SetAlpha(SpriteRenderer sr, float a)
        {
            var col = sr.color;
            col.a = a;
            sr.color = col;
        }

        private void Redraw(SiteSimulator sim, Vector2 cam)
        {
            var halfH = _camera != null ? _camera.orthographicSize + 2.5f : 12f;
            var halfW = _camera != null ? halfH * _camera.aspect + 1.5f : 8f;
            var keep = new HashSet<Vector3Int>();
            var cells = new HashSet<Vector3Int>();
            foreach (var (c, a, slot) in _all)
            {
                {
                    var p = Centre(c);
                    if (Mathf.Abs(p.y - cam.y) > halfH + 4f || Mathf.Abs(p.x - cam.x) > halfW) continue;
                    cells.Add(c);
                    var state = State(a, slot, out var tree);
                    if (state == TreeState.None) continue;
                    keep.Add(c);
                    var top = SurfaceTop(c);
                    if (!_shown.TryGetValue(c, out var sr)) _shown[c] = sr = Take(p);
                    // On the ground's top, among its blocks: the Ground layer, sorted by cell and level.
                    sr.transform.position = SurfacePoint(c, top);
                    sr.sortingLayerName = "Ground";
                    sr.sortingOrder = TerrainOrder(c, top + 1) + 2;
                    sr.sprite = state == TreeState.Cut ? stumpSprite : SpriteFor(SpeciesAt(sim, a, tree, slot));
                    var gate = _ring && sim.Grove.GateTierAt(a, slot) > 0;
                    sr.color = gate ? GateTint : AreaTint(sim, sim.BandOf(a, slot));
                    sr.transform.localScale = Vector3.one * treeScale * (gate ? 1.25f : 1f);   // a gate's old trees: bigger, darker
                    sr.name = tree >= 0 ? $"Tree{tree}" : "ForestTree";
                }
            }
            var drop = new List<Vector3Int>();
            foreach (var kv in _shown) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var c in drop) { Give(_shown[c]); _shown.Remove(c); _treeAlpha.Remove(c); }
            foreach (var kv in _treeAlpha) if (_shown.TryGetValue(kv.Key, out var t)) SetAlpha(t, kv.Value);   // the redraw reset the tint
            RedrawTerrain(cells);
        }

        private string SpeciesAt(SiteSimulator sim, int area, int tree, int slot)
        {
            if (sim.GroveMode) return sim.SpeciesAt(area, slot);   // each tree is what it is, before it is cut
            var s = sim.Record;
            if (tree >= 0 && tree < s.Trees.Count) return s.Trees[tree].Species;
            // Trees not yet in a clearing show the area's main species once known.
            if (s.Revealed.TryGetValue(area, out var comp) && comp != null && comp.Weights.Count > 0)
            {
                var best = comp.Weights[0];
                foreach (var w in comp.Weights) if (w.Value > best.Value) best = w;
                return best.Key;
            }
            return null;
        }

        private static readonly Color GateTint = new Color(0.46f, 0.62f, 0.5f);   // a deeper, cooler green (the fourth playtest: the old olive-brown looked muddy)

        /// <summary>Bands further out are darker and cooler; bands not entered yet are darker still.</summary>
        public Color AreaTint(SiteSimulator sim, int band)
        {
            var depth = Mathf.Clamp01((band - 1) * 0.09f);
            var col = Color.Lerp(Color.white, new Color(0.5f, 0.6f, 0.62f), depth);
            if (!_ring && band > sim.Record.Depth) col = Color.Lerp(col, new Color(0.3f, 0.38f, 0.36f), 0.35f);
            col.a = 1f;
            return col;
        }

        private SpriteRenderer Take(Vector3 at)
        {
            SpriteRenderer sr;
            if (_pool.Count > 0)
            {
                sr = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                sr.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("ForestTree");
                go.transform.SetParent(transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.spriteSortPoint = SpriteSortPoint.Pivot;   // sorted by the trunk's foot, like everything standing in the world
                go.AddComponent<SpriteOutline>().OverlapFree = true;   // the game's outline, never across a neighbouring tree
                if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
                go.transform.localScale = Vector3.one * treeScale;
            }
            sr.transform.position = at;
            sr.transform.localRotation = Quaternion.identity;
            return sr;
        }

        private void Give(SpriteRenderer sr)
        {
            sr.gameObject.SetActive(false);
            _pool.Add(sr);
        }

        public Sprite SpriteFor(string resourceId)
        {
            foreach (var s in speciesSprites) if (s.resource != null && s.resource.Id == resourceId && s.sprite != null) return s.sprite;
            foreach (var r in broadleafSpecies) if (r != null && r.Id == resourceId) return mapleSprite;
            return cedarSprite;
        }

        // ------------------------------------------------------------------ the grid of trees (cut any tree in reach)

        /// <summary>The tile of the grid's tree (area, slot), or false past the frontier.</summary>
        public bool CellOf(int area, int slot, out Vector3Int cell)
        {
            Build();
            cell = default;
            if (_ring)
            {
                cell = new Vector3Int(area, slot, 0);
                return _slotOf.ContainsKey(cell);
            }
            var col = area >= 1 && area < _columns.Count ? _columns[area] : null;
            if (col == null || slot < 0 || slot >= col.Count) return false;
            cell = col[slot];
            return true;
        }

        /// <summary>The grid's tree on a tile, if the tile is in the Forest.</summary>
        public bool TreeAt(Vector3Int c, out int area, out int slot)
        {
            Build();
            var found = _slotOf.TryGetValue(c, out var at);
            area = at.area;
            slot = at.slot;
            return found;
        }

        /// <summary>Where the supplier stands to cut the tree on a tile: just in front of it.</summary>
        public Vector3 WorkSpotOf(Vector3Int c) => Centre(c) + new Vector3(0.1f, -0.32f, 0f);
        public Vector3 TreeCentre(Vector3Int c) => Centre(c);

        /// <summary>The standing tree nearest a point whose cutting spot is within <paramref name="reach"/>.</summary>
        public bool NearestStanding(Vector2 me, float reach, out int area, out int slot, out Vector3Int cell)
        {
            Build();
            area = slot = -1;
            cell = default;
            var found = false;   // (area, slot) are cell coordinates in a camp's ring: they can be negative
            var sim = Sim;
            if (sim == null) return false;
            var at = IsoMap.Cell(me);
            var best = reach;
            for (var dx = -3; dx <= 3; dx++)
            for (var dy = -3; dy <= 3; dy++)
            {
                var c = new Vector3Int(at.x + dx, at.y + dy, 0);
                if (!_slotOf.TryGetValue(c, out var t)) continue;
                var st = State(t.area, t.slot, out _);
                if (st != TreeState.Standing && st != TreeState.Current) continue;
                if (Mathf.Abs(SurfaceTop(c) - _feet) > 1) continue;   // not a tree up on a cliff, or down below
                var d = Vector2.Distance(me, WorkSpotOf(c));
                if (d >= best) continue;
                best = d;
                area = t.area;
                slot = t.slot;
                cell = c;
                found = true;
            }
            return found;
        }

        /// <summary>The stump nearest a point within reach, at about the supplier's level (to pull out).</summary>
        public bool NearestStump(Vector2 me, float reach, out int area, out int slot, out Vector3Int cell)
        {
            Build();
            area = slot = -1;
            cell = default;
            var found = false;
            var at = IsoMap.Cell(me);
            var best = reach;
            for (var dx = -3; dx <= 3; dx++)
            for (var dy = -3; dy <= 3; dy++)
            {
                var c = new Vector3Int(at.x + dx, at.y + dy, 0);
                if (!_slotOf.TryGetValue(c, out var t) || State(t.area, t.slot, out _) != TreeState.Cut) continue;
                if (Mathf.Abs(SurfaceTop(c) - _feet) > 1) continue;
                var d = Vector2.Distance(me, WorkSpotOf(c));
                if (d >= best) continue;
                best = d;
                area = t.area;
                slot = t.slot;
                cell = c;
                found = true;
            }
            return found;
        }

        /// <summary>The way from staging to a tree of the grid: into the frontier by the trail, along the cliff edge, in.</summary>
        public void RouteToCell(Vector3 staging, Vector3Int c, List<Vector3> route)
        {
            route.Clear();
            route.Add(staging);
            route.Add(Centre((Vector3Int)trailCell));
            route.Add(Centre(new Vector3Int(start.x, Mathf.Max(trailCell.y, c.y), 0)));
            route.Add(WorkSpotOf(c));
        }

        /// <summary>The tree on a tile shakes (a swing landing).</summary>
        public void Shake(Vector3Int c)
        {
            if (!_shown.TryGetValue(c, out var sr)) return;
            var key = c.x * 100003 + c.y;
            if (_shakes.TryGetValue(key, out var co) && co != null) StopCoroutine(co);
            _shakes[key] = StartCoroutine(Shake(sr.transform));
        }

        public SpriteRenderer RendererAt(Vector3Int c) => _shown.TryGetValue(c, out var sr) ? sr : null;

        /// <summary>The drawn tree of the current clearing (tests), or null when off screen.</summary>
        public SpriteRenderer TreeRenderer(int index) => _shown.TryGetValue(TreeCell(index), out var sr) ? sr : null;

        private void OnUnitEarned(string site, string worker, int tree, string resource)
        {
            if (site != siteId || tree < 0) return;
            var sr = TreeRenderer(tree);
            if (sr == null) return;
            if (_shakes.TryGetValue(tree, out var c) && c != null) StopCoroutine(c);
            _shakes[tree] = StartCoroutine(Shake(sr.transform));
        }

        private static IEnumerator Shake(Transform t)
        {
            const float duration = 0.25f;
            for (var e = 0f; e < duration; e += Time.deltaTime)
            {
                t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(e * 60f) * 4f * (1f - e / duration));
                yield return null;
            }
            t.localRotation = Quaternion.identity;
        }
    }
}
