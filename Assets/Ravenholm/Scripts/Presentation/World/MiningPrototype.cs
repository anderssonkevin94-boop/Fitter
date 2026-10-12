using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// PROTOTYPE (option A, 2026-10-12): Minecraft-style digging into a cliff, on the same isometric grid and camera.
    ///
    /// The ground is a slab of blocks: along the face (u), into the cliff (x, 0 = the face) and down (row). Blocks are
    /// half a level tall, so the supplier is two blocks tall and a tunnel is 2 high × 1 wide. Walk like in town (left and
    /// right along the face, up into the cliff, down back out), tap a block in reach to select it, hold Dig to mine it.
    /// Single blocks are stepped up, holes are dropped into. A see-through cutaway fades the blocks between the camera and
    /// the supplier, so tunnels dug into the cliff stay readable. Ore uses the real veins (world seed, the Mine's layer
    /// mixes) and shows as flecks within the lamp's light. Self-contained: nothing here touches the company save.
    /// </summary>
    public sealed class MiningPrototype : MonoBehaviour
    {
        public const string SceneName = "MinePrototype";
        public const string TownSceneName = "DebugSandbox";
        public const int Width = 48;            // blocks along the face
        public const int Depth = 36;            // block rows below the surface
        public const int Thickness = 8;         // blocks into the cliff that can be dug
        public const int RowsPerLayer = 6;      // 3 levels × 2 blocks
        public const float RowHeight = 0.5f;    // half a level: the supplier is two blocks tall
        private const int Margin = 8;
        private const float CutawayRadius = 1.7f;

        [SerializeField] private Grid grid;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform player;
        [SerializeField] private Transform playerVisual;
        [SerializeField] private TileBase[] blockTiles = new TileBase[0];
        [SerializeField] private TileBase surfaceTile;
        [SerializeField] private TileBase fleckTile;
        [SerializeField] private Sprite[] crackSprites = new Sprite[0];
        [SerializeField] private Sprite highlightSprite;
        [SerializeField] private Material spriteMaterial;

        private GameRoot _root;
        private readonly HashSet<Vector3Int> _dug = new HashSet<Vector3Int>();   // (x, u, row)
        // Blocks are drawn as sprites, each sorted front to back with the supplier (half-blocks are true isometric cubes).
        private readonly Dictionary<Vector3Int, SpriteRenderer> _blocks = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _grass = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _flecks = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private Transform _blockRoot;
        private UnityEngine.Rendering.SortingGroup _playerSorting;
        private readonly Dictionary<Vector3Int, string> _ore = new Dictionary<Vector3Int, string>();
        private readonly SortedDictionary<string, int> _tally = new SortedDictionary<string, int>();
        private List<BandTemplate> _bands;
        private ulong _seed;
        private float _u = Width * 0.5f;        // along the face
        private float _x;                       // into the cliff (0 = the face row)
        private int _feet;                      // the block row the supplier stands on (its top is the floor)
        private float _drawFeet;
        private Vector2Int _facing = new Vector2Int(0, 1);   // (dx, du)
        private float _fallTimer;
        private Vector3Int? _target;
        private Vector3Int? _selected;
        private float _progress;
        private SpriteRenderer _highlight, _crack;
        private HoldSensor _digButton;
        private TextMeshProUGUI _tallyText;
        private float _bob;
        private string _hint = "";
        private float _hintUntil;
        private Vector3Int _cutawayAt = new Vector3Int(-99, -99, -99);

        public void EditorSetup(Grid g, Camera cam, Transform playerRoot, Transform visual, TileBase[] blocks, TileBase surface, TileBase fleck,
            Sprite[] cracks, Sprite highlight, Material mat)
        {
            grid = g;
            worldCamera = cam;
            player = playerRoot;
            playerVisual = visual;
            blockTiles = blocks;
            surfaceTile = surface;
            fleckTile = fleck;
            crackSprites = cracks;
            highlightSprite = highlight;
            spriteMaterial = mat;
        }

        // ------------------------------------------------------------------ tests

        public static MiningPrototype Current { get; private set; }
        public float U => _u;
        public float X => _x;
        public int Feet => _feet;
        public Vector3Int? Target => _target;
        public Vector3Int? Selected => _selected;
        public int DugCount => _dug.Count;
        public bool ScriptedDig { get; set; }
        public IReadOnlyDictionary<string, int> Tally => _tally;
        public int FadedBlocks { get; private set; }

        // ------------------------------------------------------------------ the slab

        /// <summary>
        /// Solid ground at (x, u, row). Above the surface is air, and so is everything in front of the face (x &lt; 0);
        /// past the diggable slab (sides, back, bottom) is bedrock.
        /// </summary>
        public bool Solid(int x, int u, int row)
        {
            if (row < 0 || x < 0) return false;
            if (u < 0 || u >= Width || row >= Depth || x >= Thickness) return true;
            return !_dug.Contains(new Vector3Int(x, u, row));
        }

        /// <summary>The face row (prototype tests from the first version).</summary>
        public bool Solid(int u, int row) => Solid(0, u, row);

        /// <summary>Where the supplier can stand or walk: in front of the face is a drop, so it blocks like a wall.</summary>
        private bool Blocks(int x, int u, int row) => x < 0 ? row >= 0 : Solid(x, u, row);

        public bool Diggable(Vector3Int b) => b.x >= 0 && b.x < Thickness && b.y >= 0 && b.y < Width && b.z >= 0 && b.z < Depth && Solid(b.x, b.y, b.z);

        private static Vector3Int Cell(int x, int u) => new Vector3Int(x, u, 0);

        /// <summary>World point of the top of block (x, u, row) (its sprite pivot).</summary>
        public Vector3 Top(float x, float u, float row) => new Vector3((x - u) * 0.5f, (x + u) * 0.25f + 0.25f - row * RowHeight, 0f);
        public Vector3 Top(float u, float row) => Top(0f, u, row);

        private int Layer(int row) => Mathf.Clamp(row / RowsPerLayer + 1, 1, _bands.Count);

        /// <summary>What a block holds (null = plain rock): the real veins, from the world seed and the layer's mix.</summary>
        public string OreAt(int x, int u, int row)
        {
            var key = new Vector3Int(x, u, row);
            if (_ore.TryGetValue(key, out var ore)) return ore;
            var band = _bands[Layer(row) - 1];
            var comp = SiteMath.Generate(_seed, MineDigView.SiteId, band);
            ore = MineVeins.OreAt(_seed ^ (ulong)band.Index * 0x9E3779B97F4A7C15UL, comp, band.BaselineResourceId, x, u, row, out _);
            _ore[key] = ore;
            return ore;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null && _root.InputRouter != null) _root.InputRouter.Tapped -= OnTapped;
            if (_root != null && _root.Hud != null) _root.Hud.gameObject.SetActive(true);
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            var mine = _root != null ? _root.Services.Sites.Get(MineDigView.SiteId) : null;
            _bands = mine != null ? mine.Config.Bands : new List<BandTemplate> { new BandTemplate { Index = 1 } };
            _seed = _root != null ? _root.Services.Scheduler.Coordinator.WorldSeed : 1UL;
            if (_root != null) _root.Hud.gameObject.SetActive(false);   // the prototype has its own controls

            _blockRoot = new GameObject("Blocks").transform;
            _blockRoot.SetParent(transform, false);
            _playerSorting = player.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (_playerSorting != null) _playerSorting.sortingLayerName = "Ground";
            for (var row = 0; row < Depth; row++) DrawRow(row);
            DrawSurface();
            _highlight = MakeRenderer("Target", highlightSprite, new Color(1f, 0.92f, 0.45f, 0.45f));
            _crack = MakeRenderer("Crack", null, Color.white);
            BuildUi();
            _root.InputRouter.Tapped += OnTapped;
            _drawFeet = _feet;
            PlacePlayer();
            SnapCamera();
        }

        /// <summary>
        /// Front-to-back draw order of a block: nearer the camera means a smaller x + u (along and into the cliff) and a
        /// higher block (smaller row). Half-blocks are cubes on the 2:1 grid, so x + u + row orders them exactly. Each
        /// block has four slots: the block, its grass or flecks, the supplier, and the highlight.
        /// </summary>
        public static int OrderFor(int x, int u, int row) => -4 * (x + u + row);

        private static Sprite SpriteOf(TileBase tile) => tile is Tile t ? t.sprite : null;

        private SpriteRenderer Take(string name)
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
                var go = new GameObject(name);
                go.transform.SetParent(_blockRoot, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = "Ground";
                if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            }
            return sr;
        }

        private void Put(Dictionary<Vector3Int, SpriteRenderer> map, Vector3Int key, Sprite sprite, Vector3 at, int order, Color colour)
        {
            if (!map.TryGetValue(key, out var sr)) map[key] = sr = Take("Block");
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

        /// <summary>The block whose sprites are drawn at a key (tests: a block drawn in front of the supplier sorts after them).</summary>
        public int DrawnOrder(Vector3Int b) => _blocks.TryGetValue(b, out var sr) ? sr.sortingOrder : int.MinValue;
        public int PlayerOrder => _playerSorting != null ? _playerSorting.sortingOrder : 0;

        private SpriteRenderer MakeRenderer(string name, Sprite sprite, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = colour;
            sr.sortingLayerName = "Ground";
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            sr.enabled = false;
            return sr;
        }

        // ------------------------------------------------------------------ drawing

        private static readonly Color[] Bands =
        {
            new Color(0.86f, 0.71f, 0.50f), new Color(0.76f, 0.48f, 0.37f), new Color(0.53f, 0.56f, 0.65f),
            new Color(0.58f, 0.67f, 0.47f), new Color(0.62f, 0.49f, 0.60f), new Color(0.82f, 0.63f, 0.31f),
        };

        private static readonly Color InsideShade = new Color(0.5f, 0.48f, 0.5f);
        private static readonly Color CutShade = new Color(0.74f, 0.72f, 0.72f);   // rock shown by the cutaway: a cross-section
        private static readonly Color GhostShade = new Color(1f, 1f, 1f, 0.38f);    // cut away but touching the supplier

        public int GhostBlocks { get; private set; }

        /// <summary>
        /// Blocks the cutaway would hide but the supplier is up against: beside them at body or head height, the step at
        /// their feet, and the block over their head. These stay visible (ghosted) and can be tapped and dug.
        /// </summary>
        public bool Ghost(int x, int u, int row)
        {
            var px = Mathf.RoundToInt(_x);
            var pu = Mathf.RoundToInt(_u);
            var flat = Mathf.Abs(x - px) + Mathf.Abs(u - pu);
            if (flat == 0) return row == _feet - 3;
            return flat == 1 && row >= _feet - 2 && row <= _feet;
        }

        /// <summary>Open to the camera: dug, air, or cut away for the moment so the supplier can be seen.</summary>
        private bool Open(int x, int u, int row) => !Solid(x, u, row) || Hides(x, u, row);

        /// <summary>A block is drawn when the camera can see it: an open neighbour in front, or open ground above.</summary>
        private bool Visible(int x, int u, int row) => Open(x - 1, u, row) || Open(x, u - 1, row) || row > 0 && Open(x, u, row - 1);

        /// <summary>One row of the slab: every visible block, shaded inside the dig, faded where it hides the supplier.</summary>
        private void DrawRow(int row)
        {
            if (row < 0 || row >= Depth) return;
            var band = Bands[(Layer(row) - 1) % Bands.Length];
            for (var u = -Margin; u < Width + Margin; u++)
            for (var x = 0; x <= Thickness; x++)
            {
                var key = new Vector3Int(x, u, row);
                var h = (u * 73856093) ^ (row * 19349663) ^ (x * 83492791);
                var sprite = SpriteOf(blockTiles[Mathf.Abs(h) % blockTiles.Length]);
                if (!Solid(x, u, row)) { Remove(_blocks, key); continue; }
                if (Hides(x, u, row))
                {
                    FadedBlocks++;
                    if (!Ghost(x, u, row)) { Remove(_blocks, key); continue; }   // cut away: the rock behind it shows instead
                    // Touching the supplier: kept as a see-through ghost, because it is in the way and can be dug.
                    var ghost = band * GhostShade;
                    ghost.a = GhostShade.a;
                    Put(_blocks, key, sprite, Top(x, u, row), OrderFor(x, u, row), ghost);
                    GhostBlocks++;
                    continue;
                }
                if (!Visible(x, u, row)) { Remove(_blocks, key); continue; }
                // Inside the dig (a front or the ground above dug away) the rock is in shadow; the outside face stays lit.
                var inside = _dug.Contains(new Vector3Int(x - 1, u, row)) || _dug.Contains(new Vector3Int(x, u - 1, row)) ||
                             _dug.Contains(new Vector3Int(x, u, row - 1)) && x > 0;
                var cut = !inside && (Hides(x - 1, u, row) || Hides(x, u - 1, row) || Hides(x, u, row - 1));
                var colour = band * (inside ? InsideShade : cut ? CutShade : Color.white);
                colour.a = 1f;
                Put(_blocks, key, sprite, Top(x, u, row), OrderFor(x, u, row), colour);
            }
        }

        /// <summary>
        /// The see-through cutaway: underground, a block in front of the supplier (nearer the camera, or above their head)
        /// that covers the space around them on screen is faded, so tunnels along and into the cliff stay readable.
        /// </summary>
        /// <summary>The see-through cutaway (off by default: every block stays drawn; overlapping ones are tapped in turn).</summary>
        public bool CutawayOn
        {
            get => _cutawayOn;
            set
            {
                if (_cutawayOn == value) return;
                _cutawayOn = value;
                for (var r = 0; r < Depth; r++) DrawRow(r);
                DrawSurface();
                _fleckKey = "";
            }
        }

        private bool _cutawayOn;

        private bool Hides(int x, int u, int row)
        {
            if (!_cutawayOn) return false;
            if (row > _feet || _feet < 2) return false;   // the floor, and out on the surface, stay solid
            var px = Mathf.RoundToInt(_x);
            var pu = Mathf.RoundToInt(_u);
            if (x + u >= px + pu && row >= _feet - 2) return false;   // beside or behind the supplier's own cell
            if (x > px) return false;
            var body = (Vector2)Top(_x, _u, _feet - 1);
            return Vector2.Distance((Vector2)Top(x, u, row) + new Vector2(0f, -RowHeight * 0.5f), body) < CutawayRadius;
        }

        /// <summary>Grass on the top row of blocks (just above each block in the draw order).</summary>
        private void DrawSurface()
        {
            var grass = SpriteOf(surfaceTile);
            for (var u = -Margin; u < Width + Margin; u++)
            for (var x = 0; x < Thickness + 6; x++)
            {
                var key = new Vector3Int(x, u, 0);
                if (!Solid(x, u, 0) || Hides(x, u, 0)) { Remove(_grass, key); continue; }
                Put(_grass, key, grass, Top(x, u, 0), OrderFor(x, u, 0) + 1, Color.white);
            }
        }

        private void RedrawAround(int row)
        {
            FadedBlocks = 0;
            GhostBlocks = 0;
            for (var r = row - 4; r <= row + 2; r++) DrawRow(r);
            DrawSurface();
        }

        // ------------------------------------------------------------------ the supplier

        private void Update()
        {
            if (_root == null) return;
            var move = _root.InputRouter.MoveVector;
            Walk(move);
            Fall();
            PressAndHold();
            Aim();
            Dig();
            PlacePlayer();
            Cutaway();
            Outlines();
            Flecks();
            TrackCamera();
            _tallyText.text = Time.time < _hintUntil ? _hint : TallyText();
        }

        private int Column => Mathf.RoundToInt(_u);
        private int Lane => Mathf.RoundToInt(_x);

        /// <summary>
        /// The stick walks on the grid like in town: screen left/right is along the face (and in/out), up goes into the
        /// cliff, down comes back out. A single block in the way is stepped up when there is headroom.
        /// </summary>
        private void Walk(Vector2 move)
        {
            if (move.sqrMagnitude < 0.04f) return;
            // Screen = du·(−0.5, 0.25) + dx·(0.5, 0.25)  ⇒  du = −m.x + 2·m.y, dx = m.x + 2·m.y.
            var du = -move.x + 2f * move.y;
            var dx = move.x + 2f * move.y;
            var len = Mathf.Sqrt(du * du + dx * dx);
            if (len < 0.01f) return;
            var speed = 3.2f * Mathf.Min(1f, move.magnitude) * Time.deltaTime / len;
            du *= speed;
            dx *= speed;
            _facing = Mathf.Abs(du) >= Mathf.Abs(dx) ? new Vector2Int(0, du > 0 ? 1 : -1) : new Vector2Int(dx > 0 ? 1 : -1, 0);
            TryStep(_u + du, _x);
            TryStep(_u, _x + dx);
        }

        private void TryStep(float u, float x)
        {
            u = Mathf.Clamp(u, -Margin + 1, Width + Margin - 2);
            x = Mathf.Clamp(x, 0f, Thickness + 4);
            var cu = Mathf.RoundToInt(u + Mathf.Sign(u - _u) * 0.3f * (Mathf.Abs(u - _u) > 0.0001f ? 1f : 0f));
            var cx = Mathf.RoundToInt(x + Mathf.Sign(x - _x) * 0.3f * (Mathf.Abs(x - _x) > 0.0001f ? 1f : 0f));
            if (cu == Column && cx == Lane) { _u = u; _x = x; return; }
            if (!Blocks(cx, cu, _feet - 1) && !Blocks(cx, cu, _feet - 2)) { _u = u; _x = x; return; }
            // A single block in the way, with headroom above it and above the supplier: step up onto it.
            if (Blocks(cx, cu, _feet - 1) && !Blocks(cx, cu, _feet - 2) && !Blocks(cx, cu, _feet - 3) && !Blocks(Lane, Column, _feet - 3))
            {
                _feet -= 1;
                _u = u;
                _x = x;
                return;
            }
            _u = Mathf.Clamp(_u, Column - 0.3f, Column + 0.3f);
            _x = Mathf.Clamp(_x, Lane - 0.3f, Lane + 0.3f);
        }

        /// <summary>No ground underfoot: drop a block at a time.</summary>
        private void Fall()
        {
            if (Blocks(Lane, Column, _feet)) { _fallTimer = 0f; return; }
            _fallTimer += Time.deltaTime;
            if (_fallTimer < 0.07f) return;
            _fallTimer = 0f;
            _feet += 1;
        }

        /// <summary>The block to dig: the tapped one while it is in reach, else the one ahead at body height.</summary>
        private void Aim()
        {
            if (_selected.HasValue && (!Diggable(_selected.Value) || !InReach(_selected.Value))) _selected = null;
            Vector3Int? t = _selected;
            if (!t.HasValue)
            {
                var ax = Lane + _facing.x;
                var au = Column + _facing.y;
                if (Diggable(new Vector3Int(ax, au, _feet - 1))) t = new Vector3Int(ax, au, _feet - 1);
                else if (Diggable(new Vector3Int(ax, au, _feet - 2))) t = new Vector3Int(ax, au, _feet - 2);
            }
            if (t != _target) _progress = 0f;
            _target = t;
            _highlight.enabled = t.HasValue;
            if (!t.HasValue) return;
            _highlight.transform.position = Top(t.Value.x, t.Value.y, t.Value.z);
            _highlight.sortingOrder = OrderFor(t.Value.x, t.Value.y, t.Value.z) + 3;
            _highlight.color = _selected.HasValue ? new Color(1f, 0.86f, 0.3f, 0.7f) : new Color(1f, 0.92f, 0.45f, 0.4f);
        }

        private float Hardness(Vector3Int b) => 0.55f + 0.02f * b.z + (OreAt(b.x, b.y, b.z) != null ? 0.25f : 0f);

        private void Dig()
        {
            var holding = (_digButton != null && _digButton.Held) || ScriptedDig || _pressDigging;
            _crack.enabled = false;
            if (!holding || !_target.HasValue) return;
            var b = _target.Value;
            _progress += Time.deltaTime / Hardness(b);
            _bob += Time.deltaTime * 14f;
            if (crackSprites.Length > 0 && _progress > 0.05f)
            {
                _crack.enabled = true;
                _crack.sprite = crackSprites[Mathf.Clamp((int)(_progress * crackSprites.Length), 0, crackSprites.Length - 1)];
                _crack.transform.position = Top(b.x, b.y, b.z);
                _crack.sortingOrder = OrderFor(b.x, b.y, b.z) + 3;
            }
            if (_progress < 1f) return;
            _progress = 0f;
            _dug.Add(b);
            if (_selected == b) _selected = null;
            var ore = OreAt(b.x, b.y, b.z) ?? _bands[Layer(b.z) - 1].BaselineResourceId ?? "rock";
            _tally[ore] = _tally.TryGetValue(ore, out var n) ? n + 1 : 1;
            RedrawAround(Mathf.Max(b.z, _feet));
            _fleckKey = "";
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
        }

        private void PlacePlayer()
        {
            _drawFeet = Mathf.MoveTowards(_drawFeet, _feet, Time.deltaTime * 10f);
            player.position = Top(_x, _u, _drawFeet);
            // Sorted as the cube of the lower body: blocks in front of the supplier cover them, blocks behind do not.
            if (_playerSorting != null) _playerSorting.sortingOrder = OrderFor(Lane, Column, _feet - 1) + 2;
            if (playerVisual == null) return;
            var swing = (_digButton != null && _digButton.Held) || ScriptedDig || _pressDigging;
            var screenLeft = _facing.y > 0 || _facing.x < 0;
            var sc = playerVisual.localScale;
            sc.x = Mathf.Abs(sc.x) * (screenLeft ? -1f : 1f);
            playerVisual.localScale = sc;
            playerVisual.localRotation = Quaternion.Euler(0f, 0f, swing && !Feel.ReducedMotion ? Mathf.Sin(_bob) * 12f : 0f);
        }

        /// <summary>Moving to another cell refreshes the see-through cutaway around the supplier.</summary>
        private void Cutaway()
        {
            var at = new Vector3Int(Lane, Column, _feet);
            if (at == _cutawayAt) return;
            var previous = _cutawayAt;
            _cutawayAt = at;
            if (previous.z >= 0 && previous.z != at.z) RedrawAround(previous.z);   // restore what was faded around the last spot
            RedrawAround(_feet);
        }

        // ------------------------------------------------------------------ tap to select

        /// <summary>Within reach: up to two blocks around, from one under the feet to one above the head.</summary>
        public bool InReach(Vector3Int b) =>
            Mathf.Abs(b.x - Lane) <= 2 && Mathf.Abs(b.y - Column) <= 2 && b.z >= _feet - 3 && b.z <= _feet + 1;

        /// <summary>
        /// A tap selects the reachable block nearest the finger (reachable blocks wear a faint outline), so no exact aim is
        /// needed. Where two reachable blocks sit exactly on top of each other on screen, tapping again switches between
        /// them; tapping the selected block again clears the selection.
        /// </summary>
        public void OnTapped(Vector2 screen)
        {
            var world = (Vector2)worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            var near = NearestReachable(world);
            if (near.Count == 0)
            {
                if (BlocksAt(world).Count > 0)
                {
                    _hint = "Too far: walk closer to dig that block.";
                    _hintUntil = Time.time + 2f;
                }
                return;
            }
            var i = _selected.HasValue ? near.IndexOf(_selected.Value) : -1;
            _selected = i < 0 ? near[0] : i + 1 < near.Count ? near[i + 1] : (Vector3Int?)null;
            _progress = 0f;
        }

        private const float TapRadius = 0.9f;

        /// <summary>The blocks the supplier can dig now: in reach, solid and drawn (not cut away).</summary>
        public List<Vector3Int> Reachable()
        {
            var list = new List<Vector3Int>();
            for (var row = _feet - 3; row <= _feet + 1; row++)
            for (var u = Column - 2; u <= Column + 2; u++)
            for (var x = Lane - 2; x <= Lane + 2; x++)
            {
                var b = new Vector3Int(x, u, row);
                if (Diggable(b) && _blocks.ContainsKey(b) && (!Hides(x, u, row) || Ghost(x, u, row))) list.Add(b);
            }
            return list;
        }

        /// <summary>Reachable blocks whose face centre is nearest this point (ties first, nearest first), within reach of a thumb.</summary>
        public List<Vector3Int> NearestReachable(Vector2 world)
        {
            var scored = new List<(float d, int key, Vector3Int b)>();
            foreach (var b in Reachable())
            {
                var d = Vector2.Distance(world, (Vector2)Top(b.x, b.y, b.z) + new Vector2(0f, -0.25f));
                if (d <= TapRadius) scored.Add((d, b.z * 1000 + b.x + b.y, b));
            }
            scored.Sort((a, c) => Mathf.Abs(a.d - c.d) < 0.05f ? a.key.CompareTo(c.key) : a.d.CompareTo(c.d));
            var list = new List<Vector3Int>();
            if (scored.Count == 0) return list;
            foreach (var s in scored)
                if (s.d - scored[0].d < 0.05f) list.Add(s.b);   // exact overlaps take turns; others are not candidates
            return list;
        }

        // ------------------------------------------------------------------ press and hold to dig

        private bool _pressDigging;

        /// <summary>A finger held still on a reachable block selects it and digs it, without the Dig button.</summary>
        private void PressAndHold()
        {
            _pressDigging = false;
            if (!_root.InputRouter.StillPress(out var pixels, out var seconds) || seconds < 0.25f) return;
            var world = (Vector2)worldCamera.ScreenToWorldPoint(new Vector3(pixels.x, pixels.y, 10f));
            var near = NearestReachable(world);
            if (near.Count == 0) return;
            if (!_selected.HasValue || !near.Contains(_selected.Value)) { _selected = near[0]; _progress = 0f; }
            _pressDigging = true;
        }

        // ------------------------------------------------------------------ outlines on what can be dug

        private readonly Dictionary<Vector3Int, SpriteRenderer> _outlines = new Dictionary<Vector3Int, SpriteRenderer>();
        private string _outlineKey = "";

        private void Outlines()
        {
            var key = $"{Lane}|{Column}|{_feet}|{_dug.Count}|{_cutawayOn}";
            if (key == _outlineKey) return;
            _outlineKey = key;
            foreach (var k in new List<Vector3Int>(_outlines.Keys)) Remove(_outlines, k);
            foreach (var b in Reachable())
                Put(_outlines, b, highlightSprite, Top(b.x, b.y, b.z), OrderFor(b.x, b.y, b.z) + 3, new Color(1f, 1f, 0.85f, 0.16f));
        }

        public int OutlinedBlocks => _outlines.Count;

        /// <summary>The frontmost drawn block under this world point (tests and the first tap).</summary>
        public bool BlockAt(Vector2 world, out Vector3Int block)
        {
            var hits = BlocksAt(world);
            block = hits.Count > 0 ? hits[0] : default;
            return hits.Count > 0;
        }

        /// <summary>
        /// Every drawn block whose sprite covers this world point: solid rock in front-to-back draw order, then ghosted
        /// walls (they are see-through, so the rock behind them is what the eye picks first).
        /// </summary>
        public List<Vector3Int> BlocksAt(Vector2 world)
        {
            var solid = new List<(int key, Vector3Int b)>();
            var ghosts = new List<(int key, Vector3Int b)>();
            for (var row = Mathf.Max(0, _feet - 8); row <= Mathf.Min(Depth - 1, _feet + 6); row++)
            for (var u = Mathf.Max(0, Column - 6); u <= Mathf.Min(Width - 1, Column + 6); u++)
            for (var x = 0; x < Thickness; x++)
            {
                if (!Solid(x, u, row) || !InSilhouette(world, Top(x, u, row))) continue;
                // Upper rows are drawn over lower ones; within a row, the cell lower on screen (smaller x + u) is in front.
                var key = row * 1000 + (x + u);
                if (Hides(x, u, row)) { if (Ghost(x, u, row)) ghosts.Add((key, new Vector3Int(x, u, row))); }
                else if (Visible(x, u, row)) solid.Add((key, new Vector3Int(x, u, row)));
            }
            solid.Sort((a, b) => a.key.CompareTo(b.key));
            ghosts.Sort((a, b) => a.key.CompareTo(b.key));
            var list = new List<Vector3Int>();
            foreach (var h in solid) list.Add(h.b);
            foreach (var h in ghosts) list.Add(h.b);
            return list;
        }

        /// <summary>Point inside a half-block's silhouette (top diamond plus its two faces), given its top centre.</summary>
        private static bool InSilhouette(Vector2 p, Vector3 top)
        {
            var c = (Vector2)top;
            var poly = new[]
            {
                c + new Vector2(0f, 0.25f), c + new Vector2(0.5f, 0f), c + new Vector2(0.5f, -RowHeight),
                c + new Vector2(0f, -0.25f - RowHeight), c + new Vector2(-0.5f, -RowHeight), c + new Vector2(-0.5f, 0f),
            };
            for (var i = 0; i < poly.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Length];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) >= 0f) return false;   // clockwise outline; edges belong to neither
            }
            return true;
        }

        // ------------------------------------------------------------------ flecks within the lamp's light

        private string _fleckKey = "";

        private void Flecks()
        {
            var key = $"{Lane}|{Column}|{_feet}";
            if (key == _fleckKey) return;
            _fleckKey = key;
            foreach (var k in new List<Vector3Int>(_flecks.Keys)) Remove(_flecks, k);
            var fleck = SpriteOf(fleckTile);
            var me = (Vector2)Top(_x, _u, _feet - 1);
            for (var row = Mathf.Max(0, _feet - 7); row <= Mathf.Min(Depth - 1, _feet + 5); row++)
            for (var u = Column - 7; u <= Column + 7; u++)
            for (var x = 0; x < Thickness; x++)
            {
                if (u < 0 || u >= Width || !Solid(x, u, row) || !Visible(x, u, row) || Hides(x, u, row)) continue;
                if (Vector2.Distance(Top(x, u, row) + new Vector3(0f, -RowHeight * 0.5f, 0f), me) > MineProspectView.LampRadius) continue;
                var ore = OreAt(x, u, row);
                var strong = ore != null;
                if (!strong)
                    foreach (var (dx, du, dr) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                        if ((ore = OreAt(x + dx, u + du, row + dr)) != null) break;
                if (ore == null) continue;
                var col = _root.Services.Catalogue.TryGet<ResourceDefinition>(ore, out var res) ? res.MapTint : Color.white;
                col.a = strong ? 1f : 0.4f;
                Put(_flecks, new Vector3Int(x, u, row), fleck, Top(x, u, row), OrderFor(x, u, row) + 1, col);
            }
        }

        // ------------------------------------------------------------------ camera and controls

        private void TrackCamera()
        {
            worldCamera.orthographicSize = Ravenholm.Presentation.FollowCamera.OrthoSizeForWidth(8.5f, worldCamera.aspect);
            var target = player.position + new Vector3(0f, 0.6f, -10f);
            worldCamera.transform.position = Vector3.Lerp(worldCamera.transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        private void SnapCamera()
        {
            worldCamera.orthographicSize = Ravenholm.Presentation.FollowCamera.OrthoSizeForWidth(8.5f, worldCamera.aspect);
            worldCamera.transform.position = player.position + new Vector3(0f, 0.6f, -10f);
        }

        private string TallyText()
        {
            if (_tally.Count == 0) return "Dug: nothing yet";
            var parts = new List<string>();
            foreach (var kv in _tally) parts.Add($"{UnitVisuals.DisplayName(kv.Key)} {kv.Value}");
            return "Dug: " + string.Join(" · ", parts);
        }

        private void BuildUi()
        {
            var theme = _root.Config.UiTheme;
            var ui = new UiFactory(theme);
            var canvasGo = new GameObject("PrototypeUi", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = SandboxHud.ReferenceResolution;
            scaler.matchWidthOrHeight = 0f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var safe = UiFactory.Rect("SafeArea", canvasGo.transform);
            UiFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>().Apply();

            var card = ui.Panel("Card", safe, theme.panel);
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1100f, 300f));
            var title = ui.Text("Title", card.transform,
                "<b>Mining prototype</b> (option A)\nWalk like in town: up goes into the cliff. Tap an outlined block to select it and hold Dig, or press and hold on it to dig. Nothing selected: Dig takes the block ahead.",
                theme.smallSize - 6f, theme.ink, TextAlignmentOptions.TopLeft);
            UiFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(1040f, 190f));
            _tallyText = ui.Text("Tally", card.transform, "", theme.smallSize - 4f, theme.ink, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            UiFactory.Place(_tallyText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 18f), new Vector2(1040f, 70f));

            var back = ui.Button("Back", safe, "Back to town", BackToTown, out _);
            UiFactory.Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 60f), new Vector2(380f, 130f));
            TextMeshProUGUI seeLabel = null;
            var see = ui.Button("SeeThrough", safe, "See-through: off", () =>
            {
                CutawayOn = !CutawayOn;
                seeLabel.text = CutawayOn ? "See-through: on" : "See-through: off";
            }, out seeLabel);
            UiFactory.Place((RectTransform)see.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 210f), new Vector2(380f, 130f));

            var dig = ui.Panel("Dig", safe, theme.roundButton);
            dig.type = Image.Type.Simple;
            UiFactory.Place(dig.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 80f), new Vector2(260f, 260f));
            dig.gameObject.AddComponent<Button>();
            _digButton = dig.gameObject.AddComponent<HoldSensor>();
            var label = ui.Text("Label", dig.transform, "Dig", theme.bodySize, theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(label.rectTransform);
        }

        public void BackToTown()
        {
            if (_root != null) _root.Hud.gameObject.SetActive(true);
            SceneManager.LoadScene(TownSceneName);
        }
    }
}
