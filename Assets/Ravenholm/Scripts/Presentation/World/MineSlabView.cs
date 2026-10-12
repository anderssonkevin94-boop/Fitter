using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The Mine as a dig slab (dig anywhere, 2026-10-12): the ground under the Mine's corner and the frontier, dug block by
    /// block by the supplier. Blocks are half a level tall, so the supplier is two blocks tall and a tunnel is 2 high.
    ///
    /// Drawing: every block the camera can see (an open neighbour in front, or open ground above) is a sprite, sorted
    /// front to back with the supplier (half-blocks are true isometric cubes, so x + y + row orders them exactly). The
    /// town's exposed edges are drawn the same way, so the whole cliff shares one grid and one set of layer colours.
    ///
    /// Playing: walk onto the Mine's corner and dig down; underground the stick walks the slab like the town (up goes
    /// into the cliff), single blocks are stepped up, holes are dropped into, and stepping up onto the ground leaves the
    /// Mine. Digging: the wall walked into (head height first, then legs), else the block underfoot; a tap picks the
    /// nearest outlined block, and pressing and holding on one digs it. Ore shows as flecks within the lamp's light, the
    /// Tap button makes nearby ore ring, and blocks crack as they are dug. What is dug comes from the site simulation.
    /// </summary>
    public sealed class MineSlabView : MonoBehaviour
    {
        public const string SiteId = "site.mine";
        public const float RowHeight = 0.5f;
        public const float LampRadius = 2.6f;
        public const int TapReach = 4;
        public const float TapCooldown = 5f;
        private const float TapRadius = 0.9f;
        private const int BaseOrder = -6000;   // all ground below the town's surface tiles (order 0)

        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap ground;
        [SerializeField] private Vector2Int origin;   // the slab's (x 0, u 0) cell: the front corner of the Mine's entry
        [SerializeField] private TileBase[] blockTiles = new TileBase[0];
        [SerializeField] private TileBase fleckTile;
        [SerializeField] private Sprite highlightSprite;
        [SerializeField] private Sprite[] crackSprites = new Sprite[0];
        [SerializeField] private Sprite ringSprite;
        [SerializeField] private Material spriteMaterial;

        public static MineSlabView Current { get; private set; }

        private GameRoot _root;
        private Camera _camera;
        private Transform _player;
        private PlayerMotor _motor;
        private SortingGroup _playerSorting;
        private readonly Dictionary<Vector3Int, SpriteRenderer> _blocks = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _outlines = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _flecks = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private readonly List<Vector3Int> _edgeColumns = new List<Vector3Int>();
        private readonly Dictionary<Vector3Int, string> _oreCache = new Dictionary<Vector3Int, string>();
        private readonly List<(SpriteRenderer sr, float born)> _rings = new List<(SpriteRenderer, float)>();
        private Transform _root3;
        private SpriteRenderer _highlight, _crack;
        private string _drawKey = "", _outlineKey = "", _fleckKey = "";

        public void EditorSetup(Grid g, Tilemap groundMap, Vector2Int slabOrigin, TileBase[] blocks, TileBase fleck, Sprite highlight, Sprite[] cracks,
            Sprite ring, Material mat)
        {
            grid = g;
            ground = groundMap;
            origin = slabOrigin;
            blockTiles = blocks;
            fleckTile = fleck;
            highlightSprite = highlight;
            crackSprites = cracks;
            ringSprite = ring;
            spriteMaterial = mat;
        }

        private SiteSimulator Sim => _root != null ? _root.Services.Sites.Get(SiteId) : null;
        public DigSlab Slab => Sim?.Slab;

        // ------------------------------------------------------------------ coordinates

        public Vector3Int CellOf(int x, int u) => new Vector3Int(origin.x + x, origin.y + u, 0);

        /// <summary>World point of the top of slab block (x, u, row): its sprite pivot.</summary>
        public Vector3 Top(float x, float u, float row)
        {
            float cx = origin.x + x, cy = origin.y + u;
            return new Vector3((cx - cy) * 0.5f, (cx + cy) * 0.25f + 0.25f - row * RowHeight, 0f);
        }

        private static Vector3 TopOfCell(Vector3Int cell, int row) =>
            new Vector3((cell.x - cell.y) * 0.5f, (cell.x + cell.y) * 0.25f + 0.25f - row * RowHeight, 0f);

        /// <summary>Slab coordinates (x, u) of a point on the town's plane (continuous).</summary>
        public Vector2 SlabCoords(Vector2 plane)
        {
            var sum = (plane.y - 0.25f) * 4f;   // cell x + y
            var diff = plane.x * 2f;            // cell x − y
            return new Vector2((sum + diff) * 0.5f - origin.x, (sum - diff) * 0.5f - origin.y);
        }

        public bool InSlabColumn(int x, int u) => Slab != null && Slab.InSlab(x, u, 0);

        /// <summary>Front-to-back order of a block (nearer = drawn later): smaller cell x + y and higher rows are nearer.</summary>
        public static int OrderFor(Vector3Int cell, int row) => BaseOrder - 4 * (cell.x + cell.y + row);

        public int OrderFor(int x, int u, int row) => OrderFor(CellOf(x, u), row);

        /// <summary>Order for marks on a block (outline, cracks, flecks): over the town's ground tiles when it is the ground's top.</summary>
        private int MarkOrder(int x, int u, int row, int plus) =>
            row == 0 && ground != null && ground.HasTile(CellOf(x, u)) ? plus : OrderFor(x, u, row) + plus;

        // ------------------------------------------------------------------ the ground

        private bool SlabCell(Vector3Int cell, out int x, out int u)
        {
            x = cell.x - origin.x;
            u = cell.y - origin.y;
            return InSlabColumn(x, u);
        }

        /// <summary>Solid ground at a cell and row: the slab as dug, the town's and frontier's ground columns, else air.</summary>
        public bool SolidCell(Vector3Int cell, int row)
        {
            if (row < 0) return false;
            if (SlabCell(cell, out var x, out var u)) return Sim.Solid(x, u, row);
            return ground != null && ground.HasTile(cell);
        }

        private bool VisibleCell(Vector3Int cell, int row) =>
            !SolidCell(cell + Vector3Int.left, row) || !SolidCell(cell + Vector3Int.down, row) ||
            (row > 0 ? !SolidCell(cell, row - 1) : ground == null || !ground.HasTile(cell));

        /// <summary>True where the town's own ground can be walked on (not a dug hole, not a standing tree).</summary>
        public bool SurfaceWalkable(Vector2 plane)
        {
            if (ground == null) return true;
            var c = grid.WorldToCell(plane);
            if (!ground.HasTile(c)) return false;
            return ForestGridView.Current == null || !ForestGridView.Current.HasStandingTree(c);
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null && _root.InputRouter != null) _root.InputRouter.Tapped -= OnTapped;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null || grid == null || Sim == null || !Sim.DigMode) { enabled = false; return; }
            _root3 = new GameObject("SlabBlocks").transform;
            _root3.SetParent(transform, false);
            _highlight = MakeRenderer("Target", highlightSprite);
            _crack = MakeRenderer("Crack", null);
            _root.InputRouter.Tapped += OnTapped;
            _root.Services.Sites.Reloaded += () => { _oreCache.Clear(); _drawKey = ""; };
            CollectEdges();
            SyncGroundTiles();
        }

        /// <summary>Columns that can show a cliff: ground cells with open ground in front (the slab is added on top).</summary>
        private void CollectEdges()
        {
            _edgeColumns.Clear();
            if (ground == null) return;
            var b = ground.cellBounds;
            for (var x = b.xMin - 1; x <= b.xMax; x++)
            for (var y = b.yMin - 1; y <= b.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (!ground.HasTile(c) || SlabCell(c, out _, out _)) continue;
                if (!ground.HasTile(c + Vector3Int.left) || !ground.HasTile(c + Vector3Int.down)) _edgeColumns.Add(c);
            }
        }

        /// <summary>The ground's surface tiles follow the slab's row 0: dug blocks leave a hole in the ground.</summary>
        private void SyncGroundTiles()
        {
            var sim = Sim;
            foreach (var d in sim.Record.Dug)
                if (d.Row == 0) ground.SetTile(CellOf(d.X, d.U), null);
        }

        private SpriteRenderer MakeRenderer(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = "Ground";
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            sr.enabled = false;
            return sr;
        }

        private void Update()
        {
            var sim = Sim;
            if (sim == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_player == null && SandboxSceneController.Current != null)
            {
                _player = SandboxSceneController.Current.Player;
                _motor = _player.GetComponent<PlayerMotor>();
                _playerSorting = _player.GetComponent<SortingGroup>();
            }
            Draw(sim);
            if (_player == null) return;
            PressAndHold();
            Aim();
            Outlines();
            Flecks();
            Crack(sim);
            UpdateRings();
            UpdatePlayerSorting();
        }

        // ------------------------------------------------------------------ drawing

        private static readonly Color Topsoil = new Color(0.80f, 0.64f, 0.46f);
        private static readonly Color InsideShade = new Color(0.5f, 0.48f, 0.5f);

        private static readonly Color[] Bands =
        {
            new Color(0.86f, 0.71f, 0.50f), new Color(0.76f, 0.48f, 0.37f), new Color(0.53f, 0.56f, 0.65f),
            new Color(0.58f, 0.67f, 0.47f), new Color(0.62f, 0.49f, 0.60f), new Color(0.82f, 0.63f, 0.31f),
        };

        private Color RowColour(SiteSimulator sim, int row)
        {
            if (row < sim.Slab.TopsoilRows) return Topsoil;
            var layer = sim.Slab.LayerOfRow(row);
            var col = Bands[(layer - 1) % Bands.Length] * Mathf.Pow(0.9f, (layer - 1) / Bands.Length);
            if (!sim.Record.Revealed.ContainsKey(Mathf.Min(layer, sim.Config.Bands.Count))) col = Color.Lerp(col, new Color(0.35f, 0.3f, 0.32f), 0.45f);
            col.a = 1f;
            return col;
        }

        private void Draw(SiteSimulator sim)
        {
            if (_camera == null) return;
            var cam = (Vector2)_camera.transform.position;
            var halfH = _camera.orthographicSize + 1.5f;
            var halfW = halfH * _camera.aspect + 1f;
            var key = $"{Mathf.Round(cam.x)}|{Mathf.Round(cam.y)}|{sim.Record.Dug.Count}|{sim.Record.Revealed.Count}";
            if (key == _drawKey) return;
            _drawKey = key;
            var keep = new HashSet<Vector3Int>();
            void Column(Vector3Int cell)
            {
                var top = TopOfCell(cell, 0);
                if (Mathf.Abs(top.x - cam.x) > halfW) return;
                // Rows whose block faces fall inside the view.
                var first = Mathf.Max(0, Mathf.FloorToInt((top.y - (cam.y + halfH)) / RowHeight));
                var last = Mathf.CeilToInt((top.y - (cam.y - halfH)) / RowHeight);
                if (last < 0) return;
                for (var row = first; row <= last; row++)
                {
                    if (!SolidCell(cell, row) || !VisibleCell(cell, row)) continue;
                    var k = new Vector3Int(cell.x, cell.y, row);
                    keep.Add(k);
                    var h = (cell.x * 73856093) ^ (row * 19349663) ^ (cell.y * 83492791);
                    var tile = blockTiles[Mathf.Abs(h) % blockTiles.Length] as Tile;
                    var inside = SlabCell(cell, out var x, out var u) &&
                                 (sim.IsDug(x - 1, u, row) || sim.IsDug(x, u - 1, row) || row > 0 && sim.IsDug(x, u, row - 1));
                    var colour = RowColour(sim, row) * (inside ? InsideShade : Color.white);
                    colour.a = 1f;
                    Put(_blocks, k, tile != null ? tile.sprite : null, TopOfCell(cell, row), OrderFor(cell, row), colour);
                }
            }
            foreach (var c in _edgeColumns) Column(c);
            var slab = sim.Slab;
            // Slab columns near the view (their blocks show wherever the slab has been dug, or at its edges).
            var centre = SlabCoords(cam);
            var reachU = Mathf.CeilToInt(halfW * 2f + halfH * 4f);
            for (var u = Mathf.Max(0, Mathf.FloorToInt(centre.y) - reachU); u <= Mathf.Min(slab.Length - 1, Mathf.CeilToInt(centre.y) + reachU); u++)
            for (var x = 0; x < slab.Thickness; x++)
                Column(CellOf(x, u));
            foreach (var k in new List<Vector3Int>(_blocks.Keys))
                if (!keep.Contains(k)) Remove(_blocks, k);
            _outlineKey = "";
            _fleckKey = "";
        }

        private SpriteRenderer Take()
        {
            SpriteRenderer sr;
            if (_pool.Count > 0)
            {
                sr = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                sr.gameObject.SetActive(true);
                return sr;
            }
            var go = new GameObject("Block");
            go.transform.SetParent(_root3, false);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Ground";
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }

        private void Put(Dictionary<Vector3Int, SpriteRenderer> map, Vector3Int key, Sprite sprite, Vector3 at, int order, Color colour)
        {
            if (!map.TryGetValue(key, out var sr)) map[key] = sr = Take();
            sr.sprite = sprite;
            sr.transform.position = at;
            sr.sortingOrder = order;
            sr.color = colour;
        }

        private void Remove(Dictionary<Vector3Int, SpriteRenderer> map, Vector3Int key)
        {
            if (!map.TryGetValue(key, out var sr)) return;
            sr.gameObject.SetActive(false);
            _pool.Add(sr);
            map.Remove(key);
        }

        /// <summary>Tests: whether a slab block is drawn, and its order (blocks in front of the supplier sort after them).</summary>
        public bool IsDrawn(int x, int u, int row) => _blocks.ContainsKey(new Vector3Int(origin.x + x, origin.y + u, row));
        public int DrawnOrder(int x, int u, int row) => _blocks.TryGetValue(new Vector3Int(origin.x + x, origin.y + u, row), out var sr) ? sr.sortingOrder : int.MinValue;
        /// <summary>On screen: drawn as a block, or the top of the ground (the town's own tile draws it).</summary>
        public bool Shown(int x, int u, int row) => IsDrawn(x, u, row) || row == 0 && ground != null && ground.HasTile(CellOf(x, u));

        public int PlayerOrder => _playerSorting != null ? _playerSorting.sortingOrder : 0;
        public int DrawnBlocks => _blocks.Count;

        // ------------------------------------------------------------------ the supplier underground

        private bool _inside;
        private float _x, _u;
        private int _feet;
        private float _drawFeet;
        private Vector2Int _facing = new Vector2Int(0, 1);   // (dx, du)
        private float _fallTimer;

        /// <summary>Underground: the slab moves the supplier (the town's motor waits).</summary>
        public bool PlayerInside => _inside;
        public float X => _x;
        public float U => _u;
        public int Feet => _feet;
        public Vector2Int Facing => _facing;
        private int Lane => Mathf.RoundToInt(_x);
        private int Column => Mathf.RoundToInt(_u);

        /// <summary>Walking onto a hole in the ground (dug row 0) drops the supplier into the Mine.</summary>
        public bool TryEnter(Vector2 plane)
        {
            var sim = Sim;
            if (sim == null || _inside) return false;
            var s = SlabCoords(plane);
            var x = Mathf.RoundToInt(s.x);
            var u = Mathf.RoundToInt(s.y);
            if (!InSlabColumn(x, u) || sim.Solid(x, u, 0)) return false;
            _inside = true;
            _x = Mathf.Clamp(s.x, 0f, sim.Slab.Thickness - 1);
            _u = s.y;
            _feet = 0;
            _drawFeet = 0;
            return true;
        }

        /// <summary>The supplier is put back on the ground (after a routine, or an unstuck): the town's motor takes over.</summary>
        public void Leave() => _inside = false;

        /// <summary>Moves the supplier through the slab: walk, step up single blocks, drop into holes, climb out.</summary>
        public void UpdatePlayer(Vector2 move)
        {
            var sim = Sim;
            if (!_inside || sim == null) return;
            Walk(sim, move);
            Fall(sim);
            _drawFeet = Mathf.MoveTowards(_drawFeet, _feet, Time.deltaTime * 10f);
            _player.position = Top(_x, _u, _drawFeet);
            // Stepping up onto the ground leaves the Mine: the town's motor takes over.
            if (_feet <= 0 && sim.Solid(Lane, Column, 0) && _drawFeet <= 0.01f)
            {
                _inside = false;
                _motor.Teleport(Top(_x, _u, 0));
            }
        }

        private bool Blocks(SiteSimulator sim, int x, int u, int row) => x < 0 ? row >= 0 : sim.Solid(x, u, row);

        private void Walk(SiteSimulator sim, Vector2 move)
        {
            if (move.sqrMagnitude < 0.04f) return;
            // Screen = du·(−0.5, 0.25) + dx·(0.5, 0.25)  ⇒  du = −m.x + 2·m.y, dx = m.x + 2·m.y.
            var du = -move.x + 2f * move.y;
            var dx = move.x + 2f * move.y;
            var len = Mathf.Sqrt(du * du + dx * dx);
            if (len < 0.01f) return;
            var speed = _root.Config.Balance.walkSpeed * Mathf.Min(1f, move.magnitude) * Time.deltaTime / len;
            du *= speed;
            dx *= speed;
            _facing = Mathf.Abs(du) >= Mathf.Abs(dx) ? new Vector2Int(0, du > 0 ? 1 : -1) : new Vector2Int(dx > 0 ? 1 : -1, 0);
            TryStep(sim, _u + du, _x);
            TryStep(sim, _u, _x + dx);
        }

        private void TryStep(SiteSimulator sim, float u, float x)
        {
            x = Mathf.Max(0f, x);
            var cu = Mathf.RoundToInt(u + Mathf.Sign(u - _u) * 0.3f * (Mathf.Abs(u - _u) > 0.0001f ? 1f : 0f));
            var cx = Mathf.RoundToInt(x + Mathf.Sign(x - _x) * 0.3f * (Mathf.Abs(x - _x) > 0.0001f ? 1f : 0f));
            if (cu == Column && cx == Lane) { _u = u; _x = x; return; }
            if (!Blocks(sim, cx, cu, _feet - 1) && !Blocks(sim, cx, cu, _feet - 2)) { _u = u; _x = x; return; }
            // A single block in the way, with headroom above it and above the supplier: step up onto it.
            if (Blocks(sim, cx, cu, _feet - 1) && !Blocks(sim, cx, cu, _feet - 2) && !Blocks(sim, cx, cu, _feet - 3) && !Blocks(sim, Lane, Column, _feet - 3))
            {
                _feet -= 1;
                _u = u;
                _x = x;
                return;
            }
            _u = Mathf.Clamp(_u, Column - 0.3f, Column + 0.3f);
            _x = Mathf.Clamp(_x, Lane - 0.3f, Lane + 0.3f);
        }

        private void Fall(SiteSimulator sim)
        {
            if (Blocks(sim, Lane, Column, _feet)) { _fallTimer = 0f; return; }
            _fallTimer += Time.deltaTime;
            if (_fallTimer < 0.07f) return;
            _fallTimer = 0f;
            _feet += 1;
        }

        /// <summary>Underground the supplier is sorted among the blocks (as the cube of their lower body); in town, normally.</summary>
        private void UpdatePlayerSorting()
        {
            if (_playerSorting == null) return;
            if (_inside)
            {
                _playerSorting.sortingLayerName = "Ground";
                _playerSorting.sortingOrder = OrderFor(Lane, Column, _feet - 1) + 2;
            }
            else
            {
                _playerSorting.sortingLayerName = "Default";
                _playerSorting.sortingOrder = 0;
            }
        }

        /// <summary>Where the supplier stands in slab terms (inside, or on the ground over the slab), for aiming.</summary>
        private bool Standing(out int x, out int u, out int feet)
        {
            if (_inside) { x = Lane; u = Column; feet = _feet; return true; }
            feet = 0;
            var s = SlabCoords(_player.position);
            x = Mathf.RoundToInt(s.x);
            u = Mathf.RoundToInt(s.y);
            var move = _root.InputRouter.MoveVector;
            if (move.sqrMagnitude > 0.04f)
            {
                var du = -move.x + 2f * move.y;
                var dx = move.x + 2f * move.y;
                _facing = Mathf.Abs(du) >= Mathf.Abs(dx) ? new Vector2Int(0, du > 0 ? 1 : -1) : new Vector2Int(dx > 0 ? 1 : -1, 0);
            }
            return x >= -1 && x <= Slab.Thickness && u >= -1 && u <= Slab.Length;
        }

        // ------------------------------------------------------------------ what to dig

        private DigCell? _selected;
        private DigCell? _target;
        private Vector3Int _aimFrom = new Vector3Int(-999, -999, -999);
        private bool _pressDigging;

        public DigCell? Target => _target;
        public DigCell? Selected => _selected;

        /// <summary>
        /// The block to dig. A tapped or pressed block wins while the supplier stays put. Otherwise: the wall walked into
        /// (or stood against), head height first, then legs; with nothing in the way and standing still, the block underfoot.
        /// </summary>
        private void Aim()
        {
            var sim = Sim;
            _target = null;
            if (_motor != null && _motor.HiddenForRoutine || !Standing(out var x, out var u, out var feet)) { _highlight.enabled = false; return; }
            var here = new Vector3Int(x, u, feet);
            if (here != _aimFrom) { _aimFrom = here; _selected = null; }
            if (_selected.HasValue && (!sim.CanDig(_selected.Value.X, _selected.Value.U, _selected.Value.Row) || !InReach(_selected.Value))) _selected = null;
            _target = _selected;
            if (!_target.HasValue)
            {
                int ax = x + _facing.x, au = u + _facing.y;
                if (sim.CanDig(ax, au, feet - 2)) _target = new DigCell(ax, au, feet - 2);
                else if (sim.CanDig(ax, au, feet - 1)) _target = new DigCell(ax, au, feet - 1);
                // The block underfoot only while standing still: walking on through an open tunnel never digs the floor.
                else if (_root.InputRouter.MoveVector.sqrMagnitude < 0.04f && sim.CanDig(x, u, feet)) _target = new DigCell(x, u, feet);
            }
            _highlight.enabled = _target.HasValue;
            if (!_target.HasValue) return;
            var t = _target.Value;
            _highlight.sprite = highlightSprite;
            _highlight.transform.position = Top(t.X, t.U, t.Row);
            _highlight.sortingOrder = MarkOrder(t.X, t.U, t.Row, 3);
            _highlight.color = _selected.HasValue ? new Color(1f, 0.86f, 0.3f, 0.7f) : new Color(1f, 0.92f, 0.45f, 0.4f);
        }

        /// <summary>Within reach: up to two blocks around, from one under the feet to one above the head.</summary>
        public bool InReach(DigCell b) =>
            Standing(out var x, out var u, out var feet) && Mathf.Abs(b.X - x) <= 2 && Mathf.Abs(b.U - u) <= 2 && b.Row >= feet - 3 && b.Row <= feet + 1;

        /// <summary>The blocks the supplier can dig now: in reach, diggable and drawn.</summary>
        public List<DigCell> Reachable()
        {
            var list = new List<DigCell>();
            var sim = Sim;
            if (sim == null || !Standing(out var x, out var u, out var feet)) return list;
            for (var row = feet - 3; row <= feet + 1; row++)
            for (var du = -2; du <= 2; du++)
            for (var dx = -2; dx <= 2; dx++)
            {
                if (!sim.CanDig(x + dx, u + du, row) || !Shown(x + dx, u + du, row)) continue;
                list.Add(new DigCell(x + dx, u + du, row));
            }
            return list;
        }

        /// <summary>Reachable blocks nearest a point (exact overlaps on screen take turns), within a thumb's reach.</summary>
        public List<DigCell> NearestReachable(Vector2 world)
        {
            var scored = new List<(float d, DigCell b)>();
            foreach (var b in Reachable())
            {
                var d = Vector2.Distance(world, (Vector2)Top(b.X, b.U, b.Row) + new Vector2(0f, -0.25f));
                if (d <= TapRadius) scored.Add((d, b));
            }
            scored.Sort((a, c) => a.d.CompareTo(c.d));
            var list = new List<DigCell>();
            foreach (var s in scored)
                if (s.d - scored[0].d < 0.05f) list.Add(s.b);
            return list;
        }

        /// <summary>A tap picks the reachable block nearest the finger; tapping again steps through exact overlaps, then clears.</summary>
        public void OnTapped(Vector2 screen)
        {
            if (_camera == null || _motor == null || _motor.HiddenForRoutine) return;
            var world = (Vector2)_camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            var near = NearestReachable(world);
            if (near.Count == 0) return;
            var i = _selected.HasValue ? near.IndexOf(_selected.Value) : -1;
            _selected = i < 0 ? near[0] : i + 1 < near.Count ? near[i + 1] : (DigCell?)null;
        }

        /// <summary>A finger held still on a reachable block selects it and digs it, without the button.</summary>
        private void PressAndHold()
        {
            _pressDigging = false;
            if (_camera == null || !_root.InputRouter.StillPress(out var pixels, out var seconds) || seconds < 0.25f) return;
            var near = NearestReachable(_camera.ScreenToWorldPoint(new Vector3(pixels.x, pixels.y, 10f)));
            if (near.Count == 0) return;
            if (!_selected.HasValue || !near.Contains(_selected.Value)) _selected = near[0];
            _pressDigging = true;
            Dig(Time.deltaTime);
        }

        public bool CanDigNow => _target.HasValue;

        /// <summary>Digs the target for a moment (the context button held, or a finger pressed on a block).</summary>
        public void Dig(float deltaSeconds)
        {
            if (!_target.HasValue || !Standing(out var x, out var u, out var feet)) return;
            var t = _target.Value;
            var from = new DigFace { X = x, U = u, Feet = feet, DX = _facing.x, DU = _facing.y };
            var broke = _root.Services.Sites.PlayerDig(t.X, t.U, t.Row, deltaSeconds, from);
            _motor?.PlaySwing();
            if (!broke) return;
            if (t.Row == 0) ground.SetTile(CellOf(t.X, t.U), null);   // a hole in the ground
            if (!_inside && t.Row == 0 && t.X == x && t.U == u) TryEnter(_player.position);
            _drawKey = "";
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
        }

        public bool PressDigging => _pressDigging;

        // ------------------------------------------------------------------ outlines, cracks, flecks

        private void Outlines()
        {
            var sim = Sim;
            Standing(out var x, out var u, out var feet);
            var key = $"{x}|{u}|{feet}|{sim.Record.Dug.Count}|{_inside}|{_motor != null && _motor.HiddenForRoutine}";
            if (key == _outlineKey) return;
            _outlineKey = key;
            foreach (var k in new List<Vector3Int>(_outlines.Keys)) Remove(_outlines, k);
            if (_motor != null && _motor.HiddenForRoutine) return;
            foreach (var b in Reachable())
                Put(_outlines, new Vector3Int(b.X, b.U, b.Row), highlightSprite, Top(b.X, b.U, b.Row), MarkOrder(b.X, b.U, b.Row, 3),
                    new Color(1f, 1f, 0.85f, 0.16f));
        }

        public int OutlinedBlocks => _outlines.Count;

        private void Crack(SiteSimulator sim)
        {
            var t = sim.Record.DigTarget;
            if (!t.HasValue || crackSprites.Length == 0) { _crack.enabled = false; return; }
            var p = sim.DigProgress(t.Value.X, t.Value.U, t.Value.Row);
            _crack.enabled = p > 0.05f;
            if (!_crack.enabled) return;
            _crack.sprite = crackSprites[Mathf.Clamp((int)(p * crackSprites.Length), 0, crackSprites.Length - 1)];
            _crack.transform.position = Top(t.Value.X, t.Value.U, t.Value.Row);
            _crack.sortingOrder = MarkOrder(t.Value.X, t.Value.U, t.Value.Row, 3);
        }

        public bool CrackShowing => _crack != null && _crack.enabled;

        /// <summary>What a block holds (cached; contents never change).</summary>
        public string OreAt(int x, int u, int row)
        {
            var key = new Vector3Int(x, u, row);
            if (_oreCache.TryGetValue(key, out var ore)) return ore;
            ore = Sim.OreAt(x, u, row, out _);
            _oreCache[key] = ore;
            return ore;
        }

        private Color OreColour(string ore) => _root.Services.Catalogue.TryGet<ResourceDefinition>(ore, out var r) ? r.MapTint : Color.white;

        public int StrongFlecks { get; private set; }

        /// <summary>Within the lamp's light, ore blocks show specks in the ore's colour (faint beside a vein).</summary>
        private void Flecks()
        {
            var sim = Sim;
            if (!Standing(out var px, out var pu, out var feet)) return;
            var key = $"{px}|{pu}|{feet}|{sim.Record.Dug.Count}";
            if (key == _fleckKey) return;
            _fleckKey = key;
            foreach (var k in new List<Vector3Int>(_flecks.Keys)) Remove(_flecks, k);
            StrongFlecks = 0;
            var me = (Vector2)_player.position + new Vector2(0f, 0.5f);
            var fleck = fleckTile is Tile ft ? ft.sprite : null;
            for (var row = Mathf.Max(0, feet - 7); row <= feet + 5; row++)
            for (var u = pu - 7; u <= pu + 7; u++)
            for (var x = 0; x < sim.Slab.Thickness; x++)
            {
                if (!sim.Slab.InSlab(x, u, row) || !sim.Solid(x, u, row) || !Shown(x, u, row)) continue;
                if (Vector2.Distance((Vector2)Top(x, u, row) + new Vector2(0f, -0.25f), me) > LampRadius) continue;
                var ore = OreAt(x, u, row);
                var strong = ore != null;
                if (!strong)
                    foreach (var (dx, du, dr) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                        if ((ore = OreAt(x + dx, u + du, row + dr)) != null) break;
                if (ore == null) continue;
                var col = OreColour(ore);
                col.a = strong ? 1f : 0.4f;
                Put(_flecks, new Vector3Int(x, u, row), fleck, Top(x, u, row), MarkOrder(x, u, row, 1), col);
                if (strong) StrongFlecks++;
            }
        }

        // ------------------------------------------------------------------ the prospector's tap

        private float _lastTap = -100f;
        public int LastTapRings { get; private set; }
        public string LastTapNote { get; private set; } = "";

        /// <summary>In the Mine, or at its entry, and not on a routine.</summary>
        public bool CanTap => _motor != null && !_motor.HiddenForRoutine && (_inside || Standing(out var x, out var u, out _) && InSlabColumn(x, u));

        public float TapCooldownLeft => Mathf.Max(0f, _lastTap + TapCooldown - Time.time);

        /// <summary>Every block within reach (this level and the ones above and below) that holds ore rings, even behind rock.</summary>
        public int Tap()
        {
            var sim = Sim;
            if (!CanTap || TapCooldownLeft > 0f || !Standing(out var px, out var pu, out var feet)) return 0;
            _lastTap = Time.time;
            var counts = new SortedDictionary<string, int>();
            var rings = 0;
            for (var row = Mathf.Max(0, feet - 4); row <= feet + 2; row++)
            for (var u = pu - TapReach; u <= pu + TapReach; u++)
            for (var x = 0; x < sim.Slab.Thickness; x++)
            {
                if (!sim.Slab.InSlab(x, u, row) || !sim.Solid(x, u, row)) continue;
                var ore = OreAt(x, u, row);
                if (ore == null) continue;
                counts[ore] = counts.TryGetValue(ore, out var n) ? n + 1 : 1;
                Ring(Top(x, u, row) + new Vector3(0f, -0.25f, 0f), OreColour(ore), rings * 0.015f);
                rings++;
            }
            LastTapRings = rings;
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{UnitVisuals.DisplayName(kv.Key).ToLowerInvariant()} ×{kv.Value}");
            LastTapNote = rings == 0 ? "Tap… dull rock all round." : "Tap… it rings: " + string.Join(", ", parts) + " nearby.";
            _root.Hud.ShowToast(LastTapNote);
            FeedbackAudio.Play(FeedbackAudio.Cue.Discovery);
            return rings;
        }

        private readonly List<SpriteRenderer> _ringPool = new List<SpriteRenderer>();

        private void Ring(Vector3 at, Color colour, float delay)
        {
            SpriteRenderer sr;
            if (_ringPool.Count > 0)
            {
                sr = _ringPool[_ringPool.Count - 1];
                _ringPool.RemoveAt(_ringPool.Count - 1);
                sr.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("TapRing");
                go.transform.SetParent(transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ringSprite;
                sr.sortingLayerName = "Overlay";
                if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            }
            sr.transform.position = at;
            sr.color = colour;
            _rings.Add((sr, Time.time + delay));
        }

        private void UpdateRings()
        {
            for (var i = _rings.Count - 1; i >= 0; i--)
            {
                var (sr, born) = _rings[i];
                var age = Time.time - born;
                if (age > 2.5f)
                {
                    sr.gameObject.SetActive(false);
                    _ringPool.Add(sr);
                    _rings.RemoveAt(i);
                    continue;
                }
                var c = sr.color;
                c.a = age < 0f ? 0f : 1f - Mathf.Clamp01(age / 2.5f);
                sr.color = c;
                sr.transform.localScale = Vector3.one * (Feel.ReducedMotion ? 1f : 0.6f + 0.8f * Mathf.Sin(Mathf.Clamp01(age * 2f) * Mathf.PI * 0.5f));
            }
        }

        // ------------------------------------------------------------------ for others

        /// <summary>Town ground just beside the Mine's entry (where the supplier reappears after a routine underground).</summary>
        public Vector2 BesideEntry => Top(Slab.EntryThickness, 1f, 0f);

        /// <summary>The Mine's entry on the ground (where the routine's figure goes in).</summary>
        public Vector3 EntryTop => Top(1f, 1f, 0f);

        /// <summary>Where a routine's figure stands: at the tunnel it carries on.</summary>
        public Vector3 FacePoint(DigFace f) => f == null ? EntryTop : Top(f.X, f.U, f.Feet);

        /// <summary>The lowest point the camera may need to show: the supplier, or the deepest block dug.</summary>
        public float LowestY
        {
            get
            {
                var y = _player != null ? _player.position.y : 0f;
                var sim = Sim;
                if (sim == null) return y;
                foreach (var d in sim.Record.Dug) y = Mathf.Min(y, Top(d.X, d.U, d.Row + 1).y);
                return y;
            }
        }
    }
}
