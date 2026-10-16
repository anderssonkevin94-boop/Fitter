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
        /// <summary>Whether ore shows in the rock within the lamp's light (off: Kevin, 2026-10-15).</summary>
        public static bool ShowOreInLamp = false;
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
            Scaffolds(sim);
            if (_player == null) return;
            DrawElevator(sim);
            PressAndHold();
            Aim();
            Outlines();
            Flecks();
            Crack(sim);
            UpdateRings();
            UpdatePlayerSorting();
            RevealInPit();
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>How many blocks deep the face at the land's edge is drawn.</summary>
        public const int EdgeFaceRows = 5;

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
                // The land's edge shows a short face of earth, then the valley far below (the ridge's edges are long:
                // a face down the whole screen at each of them was tens of thousands of blocks, 2026-10-15).
                if (!SlabCell(cell, out _, out _)) last = Mathf.Min(last, EdgeFaceRows - 1);
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
            for (var u = Mathf.Max(slab.MinU, Mathf.FloorToInt(centre.y) - reachU); u <= Mathf.Min(slab.Length - 1, Mathf.CeilToInt(centre.y) + reachU); u++)
            for (var x = 0; x <= slab.Thickness; x++)   // and the rock right behind the slice: the tunnels' back wall
                Column(CellOf(x, u));
            foreach (var k in new List<Vector3Int>(_blocks.Keys))
                if (!keep.Contains(k)) Remove(_blocks, k);
            HoleSides(sim);
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
        private static Sprite MakeSideFace(bool left = false)
        {
            const int ppu = 128;
            int w = ppu / 2, h = ppu * 3 / 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                // Local: the block's top centre at (0, 0); the right face spans x 0..0.5, the left face −0.5..0 (mirrored).
                float lx = (x + 0.5f) / ppu, ly = (y + 0.5f) / ppu - 0.75f;
                if (left) lx = 0.5f - lx;
                var inside = ly <= -0.25f + 0.5f * lx && ly >= -0.75f + 0.5f * lx;
                px[y * w + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(left ? 1f : 0f, 1f), ppu);
        }

        private Sprite _leftFace;

        /// <summary>Shadows the side wall of every hole: a block whose neighbour toward the town (u − 1) is dug.</summary>
        private void HoleSides(SiteSimulator sim)
        {
            if (_sideFace == null) _sideFace = MakeSideFace();
            if (_leftFace == null) _leftFace = MakeSideFace(true);
            var keep = new HashSet<Vector3Int>();
            foreach (var d in sim.Record.Dug)
            {
                // The pit's walls seen from inside: the side of the block further along the cliff, and of the block behind.
                if (sim.Solid(d.X, d.U + 1, d.Row) && IsDrawn(d.X, d.U + 1, d.Row))
                {
                    var k = new Vector3Int(d.X, d.U + 1, d.Row);
                    keep.Add(k);
                    Put(_sides, k, _sideFace, Top(d.X, d.U + 1, d.Row), OrderFor(d.X, d.U + 1, d.Row) + 1, HoleSide);
                }
                if (d.X + 1 < sim.Slab.Thickness && sim.Solid(d.X + 1, d.U, d.Row) && IsDrawn(d.X + 1, d.U, d.Row))
                {
                    var k = new Vector3Int(d.X + 1, d.U, d.Row + 100000);   // a separate key for the left face
                    keep.Add(k);
                    Put(_sides, k, _leftFace, Top(d.X + 1, d.U, d.Row), OrderFor(d.X + 1, d.U, d.Row) + 1, HoleSide);
                }
            }
            foreach (var k in new List<Vector3Int>(_sides.Keys))
                if (!keep.Contains(k)) Remove(_sides, k);
        }

        // ------------------------------------------------------------------ the elevator and scaffolds (drawing)

        [SerializeField] private Sprite scaffoldSprite;
        [SerializeField] private Sprite pulleySprite;
        [SerializeField] private Sprite platformSprite;
        [Tooltip("Height of the pulley's wheel above the frame's feet (units).")]
        [SerializeField] private float wheelHeight = 1.03f;

        public void EditorSetupBuild(Sprite scaffold, Sprite pulley, Sprite platform)
        {
            scaffoldSprite = scaffold;
            pulleySprite = pulley;
            platformSprite = platform;
        }

        private readonly Dictionary<DigCell, SpriteRenderer> _scaffolds = new Dictionary<DigCell, SpriteRenderer>();
        private SpriteRenderer _pulley, _platform, _ropeTop, _ropeShaft;
        private Sprite _pixel;
        private float _platformRow;
        private const float RopeInset = 0.22f;   // the rope hangs just inside the shaft's opening, where it can be seen

        public int ScaffoldsDrawn => _scaffolds.Count;
        public float PlatformRow => _platformRow;

        private void Scaffolds(SiteSimulator sim)
        {
            foreach (var kv in sim.Record.Scaffolds)
            {
                if (_scaffolds.ContainsKey(kv.Key)) continue;
                var sr = MakeRenderer("Scaffold", scaffoldSprite);
                sr.enabled = true;
                sr.transform.position = Top(kv.Key.X, kv.Key.U, kv.Key.Row);
                sr.sortingOrder = OrderFor(kv.Key.X, kv.Key.U, kv.Key.Row) + 1;
                _scaffolds[kv.Key] = sr;
            }
            foreach (var k in new List<DigCell>(_scaffolds.Keys))
            {
                if (sim.Record.Scaffolds.ContainsKey(k)) continue;
                Destroy(_scaffolds[k].gameObject);
                _scaffolds.Remove(k);
            }
        }

        /// <summary>The pulley over the elevator shaft, its rope, and the platform: under the supplier when they are in it.</summary>
        private void DrawElevator(SiteSimulator sim)
        {
            var u = sim.Slab.ElevatorU;
            var bottom = sim.ElevatorBottom;
            if (bottom < 0) return;
            if (_pulley == null)
            {
                _pixel = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 1f), 4);
                _pulley = MakeRenderer("Pulley", pulleySprite);
                _pulley.sortingLayerName = "Default";
                _pulley.spriteSortPoint = SpriteSortPoint.Pivot;
                _platform = MakeRenderer("LiftPlatform", platformSprite);
                _ropeTop = MakeRenderer("RopeTop", _pixel);
                _ropeTop.sortingLayerName = "Default";
                _ropeShaft = MakeRenderer("Rope", _pixel);
                foreach (var r in new[] { _pulley, _platform, _ropeTop, _ropeShaft }) r.enabled = true;
                _ropeTop.color = _ropeShaft.color = new Color(0.84f, 0.75f, 0.55f);
                _platformRow = Mathf.Clamp(_platformRow, 0, bottom + 1);
            }
            // Always at your floor: under you in the shaft, else gliding to the floor you are on (the top when above ground).
            var inShaft = _inside && Lane == 0 && Column == u;
            if (inShaft) _platformRow = _drawFeet;
            else _platformRow = Mathf.MoveTowards(_platformRow, _inside ? Mathf.Max(0, _feet) : 0f, RideSpeed * Time.deltaTime);
            _platformRow = Mathf.Clamp(_platformRow, 0f, bottom + 1);
            var top = Top(0, u, 0);
            var inset = new Vector3(-RopeInset, 0f, 0f);
            _pulley.transform.position = top + inset;
            var wheel = top + inset + new Vector3(0f, wheelHeight, 0f);
            var deck = Top(0, u, _platformRow) + inset;
            _platform.transform.position = Top(0, u, _platformRow);
            var row = Mathf.RoundToInt(_platformRow);
            if (row <= 0)
            {
                _platform.sortingLayerName = "Default";   // at ground level: over the ground's tiles, under the supplier
                _platform.sortingOrder = -1;
            }
            else
            {
                _platform.sortingLayerName = "Ground";
                _platform.sortingOrder = OrderFor(0, u, row - 1) + 1;
            }
            // The rope: from the wheel down to the ground above the shaft, then down the shaft to the platform.
            Rope(_ropeTop, wheel, top + inset);
            _ropeTop.sortingOrder = -2;
            Rope(_ropeShaft, top + inset, deck);
            _ropeShaft.sortingOrder = OrderFor(0, u, Mathf.Max(0, row - 1)) + 1;
            _ropeShaft.enabled = row > 0;
        }

        private static void Rope(SpriteRenderer sr, Vector3 from, Vector3 to)
        {
            sr.transform.position = from;
            sr.transform.localScale = new Vector3(0.05f, Mathf.Max(0.001f, from.y - to.y), 1f);
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
        public bool SameCell(Vector2 a, Vector2 b) => grid == null || grid.WorldToCell(a) == grid.WorldToCell(b);

        /// <summary>Whether stepping onto this point would go down into the quarry (an open hole in the ground).</summary>
        public bool CanEnterAt(Vector2 plane)
        {
            var sim = Sim;
            if (sim == null || _inside) return false;
            var s = SlabCoords(plane);
            var x = Mathf.RoundToInt(s.x);
            var u = Mathf.RoundToInt(s.y);
            return InSlabColumn(x, u) && !sim.Solid(x, u, 0);
        }

        public bool TryEnter(Vector2 plane)
        {
            var sim = Sim;
            if (sim == null || _inside) return false;
            var s = SlabCoords(plane);
            var x = Mathf.RoundToInt(s.x);
            var u = Mathf.RoundToInt(s.y);
            if (!InSlabColumn(x, u) || sim.Solid(x, u, 0)) return false;
            _inside = true;
            _x = s.x;
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
            if (!_inside) return;   // climbed out onto the ground: the town's motor has placed them
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

        /// <summary>In the way: rock, scaffolds, and the drop in front of the cliff (never walked off).</summary>
        private bool Blocks(SiteSimulator sim, int x, int u, int row) => x < 0 || sim.Standable(x, u, row);

        /// <summary>The stick's up and down (climbing a ladder, or aiming up and down) rather than walking along the cliff.</summary>
        public static bool Vertical(Vector2 m) => Mathf.Abs(m.y) > 0.3f && Mathf.Abs(m.y) > Mathf.Abs(m.x);

        /// <summary>In the elevator shaft: the platform is under the supplier wherever they are in it.</summary>
        public bool InElevator => _inside && Sim != null && Sim.Elevator(Lane, Column, Mathf.Max(0, _feet - 1));

        /// <summary>Riding: in the shaft with no floor of rock under them (the platform holds them).</summary>
        public bool Riding => InElevator && Sim != null && !Sim.Standable(Lane, Column, _feet);

        private float _climb;
        private bool _descending;

        /// <summary>Dropping through the air: the supplier's fall pose.</summary>
        public bool Falling => _inside && _drawFeet < _feet - 0.05f && !InElevator;

        /// <summary>
        /// The Mine is one slice of the cliff face: left and right on the stick walk along it (screen left goes on along
        /// the cliff), up and down ride the elevator. Single blocks (and scaffolds) are stepped up; holes are dropped into.
        /// </summary>
        private void Walk(SiteSimulator sim, Vector2 move)
        {
            if (move.sqrMagnitude < 0.04f) { _climb = 0f; return; }
            if (InElevator && Vertical(move))
            {
                // An elevator (where one exists) rides with up and down on the stick.
                _climb += RideSpeed * Mathf.Min(1f, Mathf.Abs(move.y)) * Time.deltaTime;
                if (_climb < 1f) return;
                _climb = 0f;
                if (move.y > 0f && _feet > 0) _feet -= 1;
                else if (move.y < 0f && sim.Elevator(Lane, Column, _feet)) _feet += 1;
                return;
            }
            _climb = 0f;
            // The open pit: the stick walks on the grid like in town. Screen = du·(−0.5, 0.25) + dx·(0.5, 0.25).
            var du = -move.x + 2f * move.y;
            var dx = move.x + 2f * move.y;
            var len = Mathf.Sqrt(du * du + dx * dx);
            if (len < 0.01f) return;
            if (_motor != null)
            {
                // The same ground speed as in town (the painted walk's planted feet), turned into grid steps.
                var v = _motor.WalkVelocity(move) * Time.deltaTime;
                du = -v.x + 2f * v.y;
                dx = v.x + 2f * v.y;
            }
            else
            {
                var speed = WalkSpeed * Mathf.Min(1f, move.magnitude) * Time.deltaTime / len;
                du *= speed;
                dx *= speed;
            }
            _facing = Mathf.Abs(du) >= Mathf.Abs(dx) ? new Vector2Int(0, du > 0f ? 1 : -1) : new Vector2Int(dx > 0f ? 1 : -1, 0);
            TryStep(sim, _u + du, _x);
            if (!_inside) return;
            TryStep(sim, _u, _x + dx);
        }

        /// <summary>
        /// At the top of the elevator, or in a hole one block deep, up on the stick climbs out onto the ground: the ground
        /// right behind the shaft (the town or the frontier), else the ground beside it along the cliff.
        /// </summary>
        private bool ClimbOut(SiteSimulator sim)
        {
            if (_feet > 1) return false;
            if (_feet == 1 && InElevator) return false;   // the elevator takes them up the last floor first
            var behind = (Vector2)Top(Lane + 1, Column, 0);
            Vector2? to = SurfaceWalkable(behind) ? behind : (Vector2?)null;
            for (var i = 0; !to.HasValue && i < 2; i++)
            {
                var u = Column + (i == 0 ? _facing.y : -_facing.y);
                if (u >= sim.Slab.MinU && sim.Solid(Lane, u, 0) && SurfaceWalkable(Top(Lane, u, 0))) to = Top(Lane, u, 0);
            }
            if (!to.HasValue) return false;
            _inside = false;
            _descending = false;
            _motor.Teleport(to.Value);
            return true;
        }

        /// <summary>Blocks per second up or down on the elevator.</summary>
        public const float RideSpeed = 6f;

        /// <summary>Blocks per second in the quarry when there is no supplier motor (it sets the speed: <see cref="PlayerMotor.WalkVelocity"/>).</summary>
        public const float WalkSpeed = 2.4f;

        private void TryStep(SiteSimulator sim, float u, float x)
        {
            var cu = Mathf.RoundToInt(u + Mathf.Sign(u - _u) * (Mathf.Abs(u - _u) > 1e-5f ? 0.3f : 0f));
            var cx = Mathf.RoundToInt(x + Mathf.Sign(x - _x) * (Mathf.Abs(x - _x) > 1e-5f ? 0.3f : 0f));
            if (cu == Column && cx == Lane) { _u = u; _x = x; return; }
            if (!Blocks(sim, cx, cu, _feet - 1) && !Blocks(sim, cx, cu, _feet - 2)) { _u = u; _x = x; return; }
            // A single block in the way, with headroom above it and above the supplier: step up onto it.
            if (Blocks(sim, cx, cu, _feet - 1) && !Blocks(sim, cx, cu, _feet - 2) && !Blocks(sim, cx, cu, _feet - 3) && !Blocks(sim, Lane, Column, _feet - 3))
            {
                _feet -= 1;
                _u = u;
                _x = x;
                if (_feet <= 0) ClimbOnto(sim, cx, cu);   // stepped up out of the pit onto the ground
                return;
            }
            _u = Mathf.Clamp(_u, Column - 0.3f, Column + 0.3f);
            _x = Mathf.Clamp(_x, Lane - 0.3f, Lane + 0.3f);
        }

        /// <summary>Out of the pit onto the ground at a cell: the town's motor takes over there.</summary>
        private void ClimbOnto(SiteSimulator sim, int x, int u)
        {
            var to = (Vector2)Top(x, u, 0);
            if (!SurfaceWalkable(to)) return;   // stays on the rim inside the Mine until there is ground to stand on
            _inside = false;
            _descending = false;
            _motor.Teleport(to);
        }

        private void Fall(SiteSimulator sim)
        {
            // Rock or a scaffold holds the supplier, and so does the elevator's platform anywhere in its shaft.
            if (Blocks(sim, Lane, Column, _feet)) { _fallTimer = 0f; _descending = false; return; }
            if (sim.Elevator(Lane, Column, Mathf.Max(0, _feet - 1))) { _fallTimer = 0f; _descending = false; return; }
            _fallTimer += Time.deltaTime;
            if (_fallTimer < 0.07f) return;
            _fallTimer = 0f;
            _feet += 1;
        }

        // ------------------------------------------------------------------ seeing into the pit

        private string _revealKey = "";
        private readonly List<SpriteRenderer> _faded = new List<SpriteRenderer>();
        public int FadedBlocks => _faded.Count;

        /// <summary>
        /// Down in the pit, rock between the camera and the supplier (drawn in front of them, overlapping them on screen)
        /// turns see-through; nothing is hidden.
        /// </summary>
        private void RevealInPit()
        {
            var key = $"{Lane}|{Column}|{_feet}|{_inside}|{_drawKey}";
            if (key == _revealKey) return;
            _revealKey = key;
            foreach (var sr in _faded) if (sr != null) { var c = sr.color; c.a = 1f; sr.color = c; }
            _faded.Clear();
            if (!_inside || _feet < 2) return;
            var me = (Vector2)Top(_x, _u, _feet - 1);
            var mine = OrderFor(Lane, Column, _feet - 1) + 2;
            foreach (var sr in _blocks.Values)
            {
                if (sr.sortingOrder <= mine) continue;
                if (Vector2.Distance((Vector2)sr.transform.position + new Vector2(0f, -0.3f), me) > 1.05f) continue;
                var c = sr.color;
                c.a = 0.35f;
                sr.color = c;
                _faded.Add(sr);
            }
        }

        /// <summary>Underground the supplier is sorted among the blocks (as the cube of their lower body); in town, normally.</summary>
        private void UpdatePlayerSorting()
        {
            if (_playerSorting == null) return;
            // Underground among the blocks; standing at ground level (on the elevator at the top) they are above the ground's tiles.
            if (_inside && _feet > 0)
            {
                _playerSorting.sortingLayerName = "Ground";
                _playerSorting.sortingOrder = OrderFor(Lane, Column, _feet - 1) + 2;
            }
            else if (ForestGridView.Current != null && ForestGridView.Current.PlayerSortOrder(_player.position, out var order))
            {
                // In the Forest: among its blocks and trees, on the Ground layer.
                _playerSorting.sortingLayerName = "Ground";
                _playerSorting.sortingOrder = order;
            }
            else
            {
                // In town, by height on screen; on the walk-in wagon's lower steps, in front of its door frame (the pack's
                // 80 % switch); up there and inside, height on screen puts them between its back and its counter.
                _playerSorting.sortingLayerName = "Default";
                _playerSorting.sortingOrder = WalkInCart.Current != null && WalkInCart.Current.Sample(_player.position, out _, out var lower) && lower ? 1 : 0;
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
            return x >= -1 && x <= Slab.Thickness && u >= Slab.MinU - 1 && u <= Slab.Length;
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
            if (!Standing(out var x, out var u, out var feet)) { _highlight.enabled = false; return; }
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
            _highlight.color = BlockHighlight.Tint(_selected.HasValue);
        }

        /// <summary>Within reach: up to two blocks around, from one under the feet to one above the head.</summary>
        public bool InReach(DigCell b) =>
            Standing(out var x, out var u, out var feet) && Mathf.Abs(b.X - x) <= 2 && Mathf.Abs(b.U - u) <= 2 && b.Row >= feet - 3 && b.Row <= feet + 1 &&
            (_inside || b.Row == 0);   // from the ground only the ground itself (the Mine's entry) can be broken into

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
                if (!_inside && row != 0) continue;   // from the ground only the ground itself
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
            if (_camera == null || _motor == null) return;
            var world = (Vector2)_camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            if (BuildMode) { BuildAt(world); return; }
            var near = NearestReachable(world);
            if (near.Count == 0) return;
            var i = _selected.HasValue ? near.IndexOf(_selected.Value) : -1;
            _selected = i < 0 ? near[0] : i + 1 < near.Count ? near[i + 1] : (DigCell?)null;
        }

        /// <summary>A finger held still on a reachable block selects it and digs it, without the button.</summary>
        private void PressAndHold()
        {
            _pressDigging = false;
            if (BuildMode) return;   // building is by taps
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
            var key = $"{x}|{u}|{feet}|{sim.Record.Dug.Count}|{_inside}|{BuildMode}|{sim.Record.Scaffolds.Count}";
            if (key == _outlineKey) return;
            _outlineKey = key;
            foreach (var k in new List<Vector3Int>(_outlines.Keys)) Remove(_outlines, k);
            if (BuildMode)
            {
                // Building: open spaces in reach glow green, scaffolds (which can be taken down) amber.
                foreach (var b in Buildable())
                    Put(_outlines, new Vector3Int(b.X, b.U, b.Row), highlightSprite, Top(b.X, b.U, b.Row), OrderFor(b.X, b.U, b.Row) + 3,
                        sim.IsScaffold(b.X, b.U, b.Row) ? new Color(1f, 0.75f, 0.3f, 0.35f) : new Color(0.55f, 1f, 0.55f, 0.3f));
                return;
            }
            foreach (var b in Reachable())
                Put(_outlines, new Vector3Int(b.X, b.U, b.Row), highlightSprite, Top(b.X, b.U, b.Row), OrderFor(b.X, b.U, b.Row) + 3,
                    new Color(1f, 1f, 0.85f, 0.16f));
        }

        public int OutlinedBlocks => _outlines.Count;

        // ------------------------------------------------------------------ building scaffolds

        /// <summary>Build mode (the Build button): taps place scaffolds in open space in reach, or take them down.</summary>
        public bool BuildMode
        {
            get => _buildMode && _inside;
            set { _buildMode = value; _outlineKey = ""; }
        }

        private bool _buildMode;

        /// <summary>Open spaces (and scaffolds) in reach, not where the supplier stands.</summary>
        public List<DigCell> Buildable()
        {
            var list = new List<DigCell>();
            var sim = Sim;
            if (sim == null || !_inside || !Standing(out var x, out var u, out var feet)) return list;
            for (var row = Mathf.Max(0, feet - 3); row <= feet + 1; row++)
            for (var du = -2; du <= 2; du++)
            for (var dx = -2; dx <= 2; dx++)
            {
                var c = new DigCell(x + dx, u + du, row);
                if (du == 0 && dx == 0 && (row == feet - 1 || row == feet - 2)) continue;   // the supplier's own body
                if (sim.IsScaffold(c.X, c.U, c.Row) || sim.CanPlaceScaffold(c.X, c.U, c.Row)) list.Add(c);
            }
            return list;
        }

        /// <summary>Places a scaffold in the open space nearest the tap, or takes down the scaffold there.</summary>
        public void BuildAt(Vector2 world)
        {
            DigCell? best = null;
            var bestD = TapRadius;
            foreach (var c in Buildable())
            {
                var d = Vector2.Distance(world, (Vector2)Top(c.X, c.U, c.Row) + new Vector2(0f, -0.25f));
                // Spaces that line up exactly on screen: the deeper one (a hole to bridge) wins.
                var tie = best.HasValue && Mathf.Abs(d - bestD) < 0.05f;
                if (tie ? c.Row > best.Value.Row : d < bestD) { bestD = Mathf.Min(d, bestD); best = c; }
            }
            if (!best.HasValue) return;
            var b = best.Value;
            var sites = _root.Services.Sites;
            if (Sim.IsScaffold(b.X, b.U, b.Row))
            {
                if (sites.RemoveScaffold(SiteId, b.X, b.U, b.Row)) FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            }
            else if (sites.PlaceScaffold(SiteId, b.X, b.U, b.Row, out var reason)) FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
            else _root.Hud.ShowToast(reason);
            _outlineKey = "";
        }

        private void Crack(SiteSimulator sim)
        {
            var t = sim.Record.DigTarget;
            if (!t.HasValue || t.Value.Row < 0 || crackSprites.Length == 0) { _crack.enabled = false; return; }   // surface outcrops crack themselves
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
            // Off (2026-10-15, Kevin): ore is no longer shown in the rock near the supplier; Tap still rings it out.
            if (!ShowOreInLamp)
            {
                if (_flecks.Count > 0) foreach (var k in new List<Vector3Int>(_flecks.Keys)) Remove(_flecks, k);
                StrongFlecks = 0;
                return;
            }
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
        public bool CanTap => _motor != null && (_inside || Standing(out var x, out var u, out _) && InSlabColumn(x, u));

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
