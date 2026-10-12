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
        [Tooltip("Painted blocks: the ground's top row, then the topsoil row, then one per layer (repeating, darker, past the last).")]
        [SerializeField] private Sprite surfaceBlock;
        [SerializeField] private Sprite[] topsoilBlocks = new Sprite[0];
        [SerializeField] private Sprite[] layerBlocks = new Sprite[0];

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

        public void EditorSetupBlocks(Sprite surface, Sprite[] topsoil, Sprite[] layers)
        {
            surfaceBlock = surface;
            topsoilBlocks = topsoil;
            layerBlocks = layers;
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

        /// <summary>There is ground at all under this point (town, frontier or the Mine's undug top), as opposed to air.</summary>
        public bool OnGround(Vector2 plane) => ground == null || ground.HasTile(grid.WorldToCell(plane)) || TryHole(plane);

        private bool TryHole(Vector2 plane)
        {
            var s = SlabCoords(plane);
            return InSlabColumn(Mathf.RoundToInt(s.x), Mathf.RoundToInt(s.y));
        }

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

        private bool Painted => layerBlocks != null && layerBlocks.Length > 0;

        /// <summary>A row's colour: with painted blocks only a shade (deeper repeats darker; unrevealed layers dimmer).</summary>
        private Color RowColour(SiteSimulator sim, int row)
        {
            var layer = sim.Slab.LayerOfRow(row);
            Color col;
            if (Painted)
                col = row < sim.Slab.TopsoilRows ? Color.white : Color.white * Mathf.Pow(0.85f, (layer - 1) / layerBlocks.Length);
            else
            {
                if (row < sim.Slab.TopsoilRows) return Topsoil;
                col = Bands[(layer - 1) % Bands.Length] * Mathf.Pow(0.9f, (layer - 1) / Bands.Length);
            }
            if (row >= sim.Slab.TopsoilRows && !sim.Record.Revealed.ContainsKey(Mathf.Min(layer, sim.Config.Bands.Count)))
                col = Color.Lerp(col, new Color(0.35f, 0.3f, 0.32f), 0.45f);
            col.a = 1f;
            return col;
        }

        /// <summary>A block's sprite: painted per row (grass-topped at the top, then topsoil, then the layer's rock).</summary>
        private Sprite BlockSprite(SiteSimulator sim, Vector3Int cell, int row, int hash)
        {
            if (!Painted) return blockTiles[Mathf.Abs(hash) % blockTiles.Length] is Tile t ? t.sprite : null;
            if (row == 0 && surfaceBlock != null) return surfaceBlock;
            if (row < sim.Slab.TopsoilRows && topsoilBlocks.Length > 0) return topsoilBlocks[Mathf.Abs(hash) % topsoilBlocks.Length];
            return layerBlocks[(sim.Slab.LayerOfRow(row) - 1) % layerBlocks.Length];
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
                    var sprite = BlockSprite(sim, cell, row, h);
                    // The back wall of a tunnel or shaft (the rock behind a dug block) is in shadow; floors and the face stay lit.
                    var back = !SlabCell(cell, out _, out _) && SlabCell(cell + Vector3Int.left, out var fx, out var fu) && sim.IsDug(fx, fu, row);
                    var colour = RowColour(sim, row) * (back ? InsideShade : Color.white);
                    colour.a = 1f;
                    Put(_blocks, k, sprite, TopOfCell(cell, row), OrderFor(cell, row), colour);
                }
            }
            foreach (var c in _edgeColumns) Column(c);
            var slab = sim.Slab;
            // Slab columns near the view (their blocks show wherever the slab has been dug, or at its edges).
            var centre = SlabCoords(cam);
            var reachU = Mathf.CeilToInt(halfW * 2f + halfH * 4f);
            for (var u = Mathf.Max(0, Mathf.FloorToInt(centre.y) - reachU); u <= Mathf.Min(slab.Length - 1, Mathf.CeilToInt(centre.y) + reachU); u++)
            for (var x = 0; x <= slab.Thickness; x++)   // and the rock right behind the slice: the tunnels' back wall
                Column(CellOf(x, u));
            foreach (var k in new List<Vector3Int>(_blocks.Keys))
                if (!keep.Contains(k)) Remove(_blocks, k);
            HoleSides(sim);
            Ladders(sim);
            _outlineKey = "";
            _fleckKey = "";
        }

        // ------------------------------------------------------------------ hole sides and ladders

        private readonly Dictionary<Vector3Int, SpriteRenderer> _sides = new Dictionary<Vector3Int, SpriteRenderer>();
        private static readonly Color HoleSide = new Color(0.18f, 0.13f, 0.12f, 0.62f);
        private Sprite _sideFace;

        /// <summary>
        /// The −u face of a half block (its right-hand side on screen): a white parallelogram whose pivot is the block's
        /// top centre. Tinted dark, it is the side wall seen inside a hole in the cliff face.
        /// </summary>
        private static Sprite MakeSideFace()
        {
            const int ppu = 128;
            int w = ppu / 2, h = ppu * 3 / 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float lx = (x + 0.5f) / ppu, ly = (y + 0.5f) / ppu - 0.75f;
                var inside = ly <= -0.25f + 0.5f * lx && ly >= -0.75f + 0.5f * lx;
                px[y * w + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0f, 1f), ppu);
        }

        /// <summary>Shadows the side wall of every hole: a block whose neighbour toward the town (u − 1) is dug.</summary>
        private void HoleSides(SiteSimulator sim)
        {
            if (_sideFace == null) _sideFace = MakeSideFace();
            var keep = new HashSet<Vector3Int>();
            foreach (var d in sim.Record.Dug)
            {
                if (!sim.Solid(d.X, d.U + 1, d.Row) || !IsDrawn(d.X, d.U + 1, d.Row)) continue;
                var k = new Vector3Int(d.X, d.U + 1, d.Row);
                keep.Add(k);
                Put(_sides, k, _sideFace, Top(d.X, d.U + 1, d.Row), OrderFor(d.X, d.U + 1, d.Row) + 1, HoleSide);
            }
            foreach (var k in new List<Vector3Int>(_sides.Keys))
                if (!keep.Contains(k)) Remove(_sides, k);
        }

        private readonly List<GameObject> _ladders = new List<GameObject>();
        private string _ladderKey = "";
        private static readonly Color LadderWood = new Color(0.55f, 0.36f, 0.2f);
        private static readonly Color LadderShade = new Color(0.36f, 0.22f, 0.12f);

        public int LadderCount => _ladders.Count;

        /// <summary>
        /// One ladder per shaft: each unbroken run of ladder blocks is a single ladder, its rails running the whole run and
        /// two rungs per block. It lies parallel to the cliff face (the blocks' lit left faces), in the shaft's opening.
        /// </summary>
        private void Ladders(SiteSimulator sim)
        {
            var key = $"{sim.Record.DugDown.Count}|{sim.Record.Dug.Count}";
            if (key == _ladderKey) return;
            _ladderKey = key;
            foreach (var go in _ladders) Destroy(go);
            _ladders.Clear();
            var runs = new SortedDictionary<(int x, int u), List<int>>();
            foreach (var c in sim.Record.DugDown)
                if (sim.Ladder(c.X, c.U, c.Row))
                {
                    if (!runs.TryGetValue((c.X, c.U), out var rows)) runs[(c.X, c.U)] = rows = new List<int>();
                    rows.Add(c.Row);
                }
            foreach (var kv in runs)
            {
                kv.Value.Sort();
                var start = kv.Value[0];
                for (var i = 1; i <= kv.Value.Count; i++)
                {
                    if (i < kv.Value.Count && kv.Value[i] == kv.Value[i - 1] + 1) continue;
                    _ladders.Add(BuildLadder(kv.Key.x, kv.Key.u, start, kv.Value[i - 1]));
                    if (i < kv.Value.Count) start = kv.Value[i];
                }
            }
        }

        /// <summary>A point on the slice in continuous coordinates: x into the cliff, u along it, level (0 = the ground's top).</summary>
        private Vector3 Point(float x, float u, float level)
        {
            float cx = origin.x + x, cy = origin.y + u;
            return new Vector3((cx - cy) * 0.5f, (cx + cy) * 0.25f + 0.25f - level * RowHeight, 0f);
        }

        private GameObject BuildLadder(int x, int u, int top, int bottom)
        {
            var go = new GameObject($"Ladder {u}:{top}-{bottom}");
            go.transform.SetParent(transform, false);
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            var plane = x - 0.45f;   // just inside the cliff face, so the whole ladder shows in the shaft's opening
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
            {
                var i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (var k = 0; k < 4; k++) cols.Add(col);
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            // A bar on the ladder's plane from (u0, l0) to (u1, l1), thickness t in screen units (perpendicular on screen).
            void Bar(float u0, float l0, float u1, float l1, float t, Color col)
            {
                var a = Point(plane, u0, l0);
                var b = Point(plane, u1, l1);
                var dir = (b - a).normalized;
                var n = new Vector3(-dir.y, dir.x, 0f) * (t * 0.5f);
                Quad(a - n, a + n, b + n, b - n, col);
            }
            float railA = u - 0.28f, railB = u + 0.28f;
            float lTop = top - 0.35f, lBottom = bottom + 1f;   // the rails stand a little proud of the top
            for (var l = top + 0.25f; l < lBottom - 0.05f; l += 0.5f) Bar(railA, l, railB, l, 0.045f, LadderWood);
            Bar(railA, lTop, railA, lBottom, 0.06f, LadderShade);
            Bar(railB, lTop, railB, lBottom, 0.06f, LadderShade);
            var mesh = new Mesh { name = go.name };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (spriteMaterial != null) mr.sharedMaterial = spriteMaterial;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", Texture2D.whiteTexture);
            mr.SetPropertyBlock(block);
            mr.sortingLayerName = "Ground";
            // In front of the shaft's walls and floor; the supplier in the shaft sorts in front of it.
            mr.sortingOrder = OrderFor(x, u, bottom) + 1;
            return go;
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
            _x = 0f;   // the slice
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

        /// <summary>The stick's up and down (climbing a ladder, or aiming up and down) rather than walking along the cliff.</summary>
        public static bool Vertical(Vector2 m) => Mathf.Abs(m.y) > 0.3f && Mathf.Abs(m.y) > Mathf.Abs(m.x);

        /// <summary>On a ladder: standing on a rung, or with the body in a shaft beside one.</summary>
        public bool OnLadder => _inside && Sim != null && (Sim.Ladder(Lane, Column, _feet) || Sim.Ladder(Lane, Column, _feet - 1));

        private float _climb;
        private bool _descending;

        /// <summary>Dropping through the air (not climbing): the supplier's fall pose.</summary>
        public bool Falling => _inside && _drawFeet < _feet - 0.05f && !OnLadder;

        /// <summary>Hanging on a ladder (not standing on a floor), and whether they are moving up or down it.</summary>
        public bool OnLadderPose => OnLadder && Sim != null && !Sim.Solid(Lane, Column, _feet);
        public bool ClimbMoving => OnLadderPose && Mathf.Abs(_drawFeet - _feet) > 0.02f;

        /// <summary>
        /// The Mine is one slice of the cliff face: left and right on the stick walk along it (screen left goes on along
        /// the cliff), up and down climb a ladder. Single blocks are stepped up; holes are dropped into.
        /// </summary>
        private void Walk(SiteSimulator sim, Vector2 move)
        {
            if (move.sqrMagnitude < 0.04f) { _climb = 0f; return; }
            if (Vertical(move))
            {
                if (!OnLadder) return;   // up and down only aim, unless on a ladder
                if (move.y > 0f) _descending = false;
                _u = Mathf.MoveTowards(_u, Column, Time.deltaTime * 4f);
                _climb += ClimbSpeed * Mathf.Min(1f, Mathf.Abs(move.y)) * Time.deltaTime;
                if (_climb < 1f) return;
                _climb = 0f;
                if (move.y > 0f && sim.Ladder(Lane, Column, _feet - 1) && !Blocks(sim, Lane, Column, _feet - 3)) _feet -= 1;
                else if (move.y < 0f && sim.Ladder(Lane, Column, _feet)) _feet += 1;
                return;
            }
            _climb = 0f;
            var du = -Mathf.Sign(move.x) * WalkSpeed * Mathf.Min(1f, Mathf.Abs(move.x) * 1.4f) * Time.deltaTime;
            _facing = new Vector2Int(0, du > 0f ? 1 : -1);
            TryStep(sim, _u + du);
        }

        /// <summary>Blocks per second up or down a ladder.</summary>
        public const float ClimbSpeed = 5f;

        /// <summary>Blocks per second along the cliff (as in the cliff-face prototype).</summary>
        public const float WalkSpeed = 3.2f;

        private void TryStep(SiteSimulator sim, float u)
        {
            u = Mathf.Clamp(u, 0f, sim.Slab.Length - 1);
            var cu = Mathf.RoundToInt(u + Mathf.Sign(u - _u) * 0.3f);
            if (cu == Column) { _u = u; return; }
            if (!Blocks(sim, Lane, cu, _feet - 1) && !Blocks(sim, Lane, cu, _feet - 2)) { _u = u; return; }
            // A single block in the way, with headroom above it and above the supplier: step up onto it.
            if (Blocks(sim, Lane, cu, _feet - 1) && !Blocks(sim, Lane, cu, _feet - 2) && !Blocks(sim, Lane, cu, _feet - 3) && !Blocks(sim, Lane, Column, _feet - 3))
            {
                _feet -= 1;
                _u = u;
                return;
            }
            _u = Mathf.Clamp(_u, Column - 0.3f, Column + 0.3f);
        }

        private void Fall(SiteSimulator sim)
        {
            // A rung holds the supplier, except straight after digging the block underfoot: then they go down to the new floor.
            if (Blocks(sim, Lane, Column, _feet)) { _fallTimer = 0f; _descending = false; return; }
            if (sim.Ladder(Lane, Column, _feet) && !_descending) { _fallTimer = 0f; return; }
            _fallTimer += Time.deltaTime;
            if (_fallTimer < 0.07f) return;
            _fallTimer = 0f;
            _feet += 1;
        }

        /// <summary>Underground the supplier is sorted among the blocks (as the cube of their lower body); in town, normally.</summary>
        private void UpdatePlayerSorting()
        {
            if (_playerSorting == null) return;
            // Underground among the blocks; standing at ground level (the top of a ladder) they are above the ground's tiles.
            if (_inside && _feet > 0)
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
            if (move.sqrMagnitude > 0.04f && Mathf.Abs(move.x) > 0.1f) _facing = new Vector2Int(0, move.x < 0f ? 1 : -1);
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
                var move = _root.InputRouter.MoveVector;
                var still = move.sqrMagnitude < 0.04f;
                int ax = x + _facing.x, au = u + _facing.y;
                // Down on the stick: the block underfoot; up: the block overhead.
                if (!still && Vertical(move))
                {
                    if (move.y < 0f && sim.CanDig(x, u, feet)) _target = new DigCell(x, u, feet);
                    else if (move.y > 0f && sim.CanDig(x, u, feet - 3)) _target = new DigCell(x, u, feet - 3);
                }
                else if (sim.CanDig(ax, au, feet - 2)) _target = new DigCell(ax, au, feet - 2);
                else if (sim.CanDig(ax, au, feet - 1)) _target = new DigCell(ax, au, feet - 1);
                // The block underfoot only while standing still: walking on through an open tunnel never digs the floor.
                else if (still && sim.CanDig(x, u, feet)) _target = new DigCell(x, u, feet);
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
            Standing(out var x, out var u, out var feet) && b.X == x && Mathf.Abs(b.U - u) <= 2 && b.Row >= feet - 3 && b.Row <= feet + 1;

        /// <summary>The blocks the supplier can dig now: in reach, diggable and drawn.</summary>
        public List<DigCell> Reachable()
        {
            var list = new List<DigCell>();
            var sim = Sim;
            if (sim == null || !Standing(out var x, out var u, out var feet)) return list;
            for (var row = feet - 3; row <= feet + 1; row++)
            for (var du = -2; du <= 2; du++)
            {
                if (!sim.CanDig(x, u + du, row) || !Shown(x, u + du, row)) continue;
                list.Add(new DigCell(x, u + du, row));
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
            if (_inside && t.X == x && t.U == u && t.Row == feet) _descending = true;   // dug underfoot: down to the new floor
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
                Put(_outlines, new Vector3Int(b.X, b.U, b.Row), highlightSprite, Top(b.X, b.U, b.Row), OrderFor(b.X, b.U, b.Row) + 3,
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
                var sprite = fleck;
                var order = MarkOrder(x, u, row, 1);
                // A painted ore block where the ore itself sits (shaded like its rock, sorted like it), flecks beside it.
                if (strong && _root.Services.Catalogue.TryGet<ResourceDefinition>(ore, out var res) && res.MineBlock != null)
                {
                    sprite = res.MineBlock;
                    col = RowColour(sim, row);
                    order = OrderFor(x, u, row) + 1;
                }
                Put(_flecks, new Vector3Int(x, u, row), sprite, Top(x, u, row), order, col);
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
