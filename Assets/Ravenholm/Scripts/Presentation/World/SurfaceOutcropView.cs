using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Rock outcrops on the town's surface (2026-10-13): small stacks of the Mine's first-layer rock standing on the
    /// ground, up to three blocks high, to mine above ground. The layout is this scene's (set by the scene builder);
    /// what has been broken comes from the Mine's simulation. Each column sorts with the people around it by its foot;
    /// a column with blocks left can't be walked through. The top block is the one mined first.
    /// </summary>
    public sealed class SurfaceOutcropView : MonoBehaviour
    {
        public const float Reach = 1.15f;

        [Tooltip("Columns: cell x, cell y, and how many blocks high (1–3).")]
        [SerializeField] private List<Vector3Int> columns = new List<Vector3Int>();
        [SerializeField] private Grid grid;
        [SerializeField] private Sprite[] rockByLevel = new Sprite[0];
        [SerializeField] private Sprite[] crackSprites = new Sprite[0];
        [SerializeField] private Material spriteMaterial;

        public static SurfaceOutcropView Current { get; private set; }

        public void EditorSetup(Grid g, List<Vector3Int> cols, Sprite[] rock, Sprite[] cracks, Material mat)
        {
            grid = g;
            columns = cols;
            rockByLevel = rock;
            crackSprites = cracks;
            spriteMaterial = mat;
        }

        private GameRoot _root;
        private readonly Dictionary<Vector2Int, List<SpriteRenderer>> _stacks = new Dictionary<Vector2Int, List<SpriteRenderer>>();
        private readonly Dictionary<Vector2Int, int> _height = new Dictionary<Vector2Int, int>();
        private SpriteRenderer _crack;
        private int _seen = -1;

        public IReadOnlyList<Vector3Int> Columns => columns;
        private SiteSimulator Sim => _root != null ? _root.Services.Sites.Get("site.mine") : null;

        private void Awake() => Current = this;
        private void OnDestroy() { if (Current == this) Current = null; }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null || grid == null) { enabled = false; return; }
            foreach (var c in columns)
            {
                var key = new Vector2Int(c.x, c.y);
                var go = new GameObject($"Outcrop {c.x},{c.y}");
                go.transform.SetParent(transform, false);
                go.transform.position = grid.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));
                go.AddComponent<SortingGroup>();   // the column sorts with people by its foot
                var list = new List<SpriteRenderer>();
                for (var level = 1; level <= c.z; level++)
                {
                    var b = new GameObject($"Level{level}");
                    b.transform.SetParent(go.transform, false);
                    b.transform.localPosition = new Vector3(0f, level * MineSlabView.RowHeight, 0f);   // its top: half a level per block
                    var sr = b.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = level * 2;
                    if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
                    list.Add(sr);
                }
                _stacks[key] = list;
                _height[key] = c.z;
            }
            _crack = new GameObject("Crack").AddComponent<SpriteRenderer>();
            _crack.transform.SetParent(transform, false);
            if (spriteMaterial != null) _crack.sharedMaterial = spriteMaterial;
            _crack.enabled = false;
            Refresh();
        }

        /// <summary>Blocks left standing in a column (0 = cleared).</summary>
        public int Remaining(int x, int y)
        {
            var sim = Sim;
            if (sim == null || !_height.TryGetValue(new Vector2Int(x, y), out var h)) return 0;
            var n = 0;
            for (var level = 1; level <= h; level++) if (!sim.IsSurfaceBroken(x, y, level)) n++;
            return n;
        }

        /// <summary>The top block left in a column (0 = none).</summary>
        public int TopLevel(int x, int y)
        {
            var sim = Sim;
            if (sim == null || !_height.TryGetValue(new Vector2Int(x, y), out var h)) return 0;
            for (var level = h; level >= 1; level--) if (!sim.IsSurfaceBroken(x, y, level)) return level;
            return 0;
        }

        /// <summary>A cell with outcrop blocks still on it can't be walked through.</summary>
        public bool Blocked(Vector3Int cell) => _height.ContainsKey(new Vector2Int(cell.x, cell.y)) && Remaining(cell.x, cell.y) > 0;

        /// <summary>The column nearest a point with blocks left, within reach: its top block is the one to mine.</summary>
        public bool Nearest(Vector2 me, out Vector3Int target)
        {
            target = default;
            var best = Reach;
            foreach (var c in columns)
            {
                var top = TopLevel(c.x, c.y);
                if (top == 0) continue;
                var d = Vector2.Distance(me, grid.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0)));
                if (d >= best) continue;
                best = d;
                target = new Vector3Int(c.x, c.y, top);
            }
            return best < Reach;
        }

        public Vector3 BlockTop(int x, int y, int level) => grid.GetCellCenterWorld(new Vector3Int(x, y, 0)) + new Vector3(0f, level * MineSlabView.RowHeight, 0f);

        private void Update()
        {
            var sim = Sim;
            if (sim == null) return;
            var broken = sim.Record.SurfaceBroken.Count;
            if (broken != _seen) { _seen = broken; Refresh(); }
            var t = sim.Record.DigTarget;
            if (t.HasValue && t.Value.Row < 0 && crackSprites.Length > 0)
            {
                var level = -t.Value.Row;
                var p = sim.SurfaceProgress(t.Value.X, t.Value.U, level);
                _crack.enabled = p > 0.05f;
                if (_crack.enabled)
                {
                    _crack.sprite = crackSprites[Mathf.Clamp((int)(p * crackSprites.Length), 0, crackSprites.Length - 1)];
                    _crack.transform.position = BlockTop(t.Value.X, t.Value.U, level);
                    if (_stacks.TryGetValue(new Vector2Int(t.Value.X, t.Value.U), out var list) && level - 1 < list.Count)
                    {
                        _crack.sortingLayerID = list[level - 1].sortingLayerID;
                        _crack.transform.SetParent(list[level - 1].transform.parent, true);
                        _crack.sortingOrder = level * 2 + 1;
                    }
                }
            }
            else _crack.enabled = false;
        }

        private void Refresh()
        {
            var sim = Sim;
            foreach (var kv in _stacks)
            {
                for (var i = 0; i < kv.Value.Count; i++)
                {
                    var level = i + 1;
                    var sr = kv.Value[i];
                    var standing = !sim.IsSurfaceBroken(kv.Key.x, kv.Key.y, level);
                    sr.enabled = standing;
                    if (!standing) continue;
                    var ore = sim.SurfaceOreAt(kv.Key.x, kv.Key.y, level);
                    Sprite sprite = null;
                    if (ore != null && _root.Services.Catalogue.TryGet<ResourceDefinition>(ore, out var res)) sprite = res.MineBlock;
                    sr.sprite = sprite != null ? sprite : rockByLevel[Mathf.Clamp(i, 0, rockByLevel.Length - 1)];
                }
            }
        }
    }
}
