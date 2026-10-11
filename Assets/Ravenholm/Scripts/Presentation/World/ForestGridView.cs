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
    /// The Forest on the frontier (GDD 7, as directed): a dense grid of trees, one per tile, on the ground left of the
    /// town, directly above the Mine's tunnels. It has the same two directions as the Mine:
    ///   - left is progression: each area (band) is a column of tiles further left ("Push Deeper");
    ///   - up is staying in an area: each clearing ("Continue Here") is the next trees up the same column.
    /// Trees are cut in order from the bottom of a column, so the woodcutters (and the supplier) physically cut their
    /// way in: only cut tiles can be walked. The current clearing's trees are the simulation's trees (species, felled);
    /// earlier clearings are stumps; ground not yet reached stays forest, darker in areas not yet entered.
    /// Only trees near the camera are drawn. Presentation only.
    /// </summary>
    public sealed class ForestGridView : MonoBehaviour
    {
        [SerializeField] private string siteId = "site.forest";
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap ground;
        [Tooltip("World x of the town's left edge: the frontier starts here.")]
        [SerializeField] private float townLeft = -12f;
        [Tooltip("Open trail between the town and the first area (world units).")]
        [SerializeField] private float trailWidth = 1.25f;   // keeps column edges between tile centres
        [Tooltip("Width of one area's column of trees (world units).")]
        [SerializeField] private float columnWidth = 2f;
        [SerializeField] private float treeScale = 0.78f;
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
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _shown = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<int, Coroutine> _shakes = new Dictionary<int, Coroutine>();
        private string _key = "";
        private bool _built;

        public void EditorSetup(Grid g, Tilemap groundMap, float left, Sprite cedar, Sprite maple, Sprite stump, List<ResourceDefinition> broadleaf,
            List<SpeciesSprite> species, Material mat)
        {
            grid = g;
            ground = groundMap;
            townLeft = left;
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

        private Vector3 Centre(Vector3Int c) => grid.GetCellCenterWorld(c);

        /// <summary>Right edge (world x) of area <paramref name="area"/>'s column (1-based).</summary>
        public float ColumnRight(int area) => townLeft - trailWidth - (area - 1) * columnWidth;

        private void Build()
        {
            if (_built || grid == null || ground == null) return;
            _built = true;
            var b = ground.cellBounds;
            var byArea = new SortedDictionary<int, List<Vector3Int>>();
            for (var x = b.xMin; x < b.xMax; x++)
            for (var y = b.yMin; y < b.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (!ground.HasTile(c)) continue;
                var p = Centre(c);
                var right = townLeft - trailWidth;
                if (p.x >= right) continue;
                var area = Mathf.FloorToInt((right - p.x) / columnWidth) + 1;
                if (!byArea.TryGetValue(area, out var list)) byArea[area] = list = new List<Vector3Int>();
                list.Add(c);
            }
            _columns.Clear();
            _columns.Add(new List<Vector3Int>());   // areas are 1-based
            for (var a = 1; byArea.ContainsKey(a); a++)
            {
                var list = byArea[a];
                list.Sort((p, q) =>
                {
                    var dy = Centre(p).y.CompareTo(Centre(q).y);
                    return dy != 0 ? dy : Centre(q).x.CompareTo(Centre(p).x);   // bottom row first, nearest the town first
                });
                for (var i = 0; i < list.Count; i++) _slotOf[list[i]] = (a, i);
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
            var bottom = Centre(Column(1).Count > 0 ? Column(1)[0] : TreeCell(treeIndex)).y;
            route.Add(new Vector3(townLeft - trailWidth * 0.5f, bottom, 0f));
            // The previous area's first clearing (always cut before pushing deeper) borders this one along the bottom.
            if (s.Depth > 1)
            {
                var prev = Column(s.Depth - 1);
                var edge = prev[0];
                for (var i = 0; i < Mathf.Min(sim.Config.TreesPerClearing, prev.Count); i++)
                    if (Centre(prev[i]).x < Centre(edge).x) edge = prev[i];
                route.Add(Centre(edge));
            }
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
            return State(at.area, at.slot, out _) != TreeState.Cut;
        }

        private enum TreeState
        {
            Standing,
            Current,
            Cut,
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
            var key = $"{Mathf.Round(cam.x * 2f)}|{Mathf.Round(cam.y * 2f)}|{s.Depth}|{s.K}|{s.SectionDepths.Count}|{felled}|{s.Trees.Count}";
            if (key == _key) return;
            _key = key;
            Redraw(sim, cam);
        }

        private void Redraw(SiteSimulator sim, Vector2 cam)
        {
            var halfH = _camera != null ? _camera.orthographicSize + 2.5f : 12f;
            var halfW = _camera != null ? halfH * _camera.aspect + 1.5f : 8f;
            var keep = new HashSet<Vector3Int>();
            for (var a = 1; a < _columns.Count; a++)
            {
                var right = ColumnRight(a);
                if (right < cam.x - halfW || right - columnWidth > cam.x + halfW) continue;
                var col = _columns[a];
                for (var slot = 0; slot < col.Count; slot++)
                {
                    var c = col[slot];
                    var p = Centre(c);
                    if (Mathf.Abs(p.y - cam.y) > halfH || Mathf.Abs(p.x - cam.x) > halfW) continue;
                    keep.Add(c);
                    if (!_shown.TryGetValue(c, out var sr)) _shown[c] = sr = Take(p);
                    var state = State(a, slot, out var tree);
                    sr.sprite = state == TreeState.Cut ? stumpSprite : SpriteFor(SpeciesAt(sim, a, tree));
                    sr.color = AreaTint(sim, a);
                    sr.name = tree >= 0 ? $"Tree{tree}" : "ForestTree";
                }
            }
            var drop = new List<Vector3Int>();
            foreach (var kv in _shown) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var c in drop) { Give(_shown[c]); _shown.Remove(c); }
        }

        private string SpeciesAt(SiteSimulator sim, int area, int tree)
        {
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

        /// <summary>Areas further left are darker and cooler; areas not entered yet are darker still.</summary>
        public Color AreaTint(SiteSimulator sim, int area)
        {
            var depth = Mathf.Clamp01((area - 1) * 0.09f);
            var col = Color.Lerp(Color.white, new Color(0.5f, 0.6f, 0.62f), depth);
            if (area > sim.Record.Depth) col = Color.Lerp(col, new Color(0.3f, 0.38f, 0.36f), 0.35f);
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
