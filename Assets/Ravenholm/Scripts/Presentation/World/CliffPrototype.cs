using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// PROTOTYPE (option A, "the cliff face", 2026-10-12): the Mine as one flat slice, on the same isometric grid.
    ///
    /// The supplier only moves along the cliff (screen left goes on along it, as the real Mine does) and up and down.
    /// Nothing is ever in front of them, so down on screen is always deeper. Blocks are half a level tall, so the
    /// supplier is two blocks tall. Behind the slice is the back wall (not dug); tunnels show as dark recesses in it.
    ///
    /// Controls: the stick left/right walks (single blocks are stepped up, holes dropped into); down aims at the block
    /// underfoot and up at the one overhead. Digging straight down two or more blocks leaves a ladder in the shaft, which
    /// the stick climbs (up/down). Dig takes the wall walked into (head height first, then legs), else, standing still,
    /// the block underfoot; a tap or a press picks a block. Ore is the real veins. Nothing here touches the company save.
    /// </summary>
    public sealed class CliffPrototype : MonoBehaviour
    {
        public const string SceneName = "MinePrototype";
        public const string TownSceneName = "DebugSandbox";
        public const int Width = 64;            // blocks along the cliff (u), going left on screen
        public const int Depth = 48;            // block rows below the surface
        public const int RowsPerLayer = 6;      // 3 levels × 2 blocks
        public const float RowHeight = 0.5f;
        public const int StartU = 3;
        private const int Margin = 8;
        private const int BackDepth = 6;        // blocks of plateau drawn behind the slice
        private const float WalkSpeed = 3.2f;   // blocks per second
        private const float ClimbSpeed = 5f;    // blocks per second
        private const float LampRadius = 2.6f;

        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform player;
        [SerializeField] private Transform playerVisual;
        [SerializeField] private Sprite[] blockSprites = new Sprite[0];
        [SerializeField] private Sprite grassSprite;
        [SerializeField] private Sprite fleckSprite;
        [SerializeField] private Sprite ladderSprite;
        [SerializeField] private Sprite[] crackSprites = new Sprite[0];
        [SerializeField] private Sprite highlightSprite;
        [SerializeField] private Material spriteMaterial;

        public void EditorSetup(Camera cam, Transform playerRoot, Transform visual, Sprite[] blocks, Sprite grass, Sprite fleck, Sprite ladder,
            Sprite[] cracks, Sprite highlight, Material mat)
        {
            worldCamera = cam;
            player = playerRoot;
            playerVisual = visual;
            blockSprites = blocks;
            grassSprite = grass;
            fleckSprite = fleck;
            ladderSprite = ladder;
            crackSprites = cracks;
            highlightSprite = highlight;
            spriteMaterial = mat;
        }

        private GameRoot _root;
        private readonly HashSet<Vector2Int> _dug = new HashSet<Vector2Int>();       // (u, row) on the slice
        private readonly HashSet<Vector2Int> _dugDown = new HashSet<Vector2Int>();   // dug from above, standing on it
        private readonly Dictionary<Vector3Int, SpriteRenderer> _blocks = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _grass = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _marks = new Dictionary<Vector3Int, SpriteRenderer>();   // ladders, flecks, outlines
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private readonly Dictionary<Vector2Int, string> _ore = new Dictionary<Vector2Int, string>();
        private readonly SortedDictionary<string, int> _tally = new SortedDictionary<string, int>();
        private Transform _blockRoot;
        private UnityEngine.Rendering.SortingGroup _playerSorting;
        private List<BandTemplate> _bands;
        private ulong _seed;
        private float _u = StartU;
        private int _feet;               // the row the supplier stands on (its top is the floor)
        private float _drawFeet;
        private int _facing = 1;         // +1 = along the cliff (screen left), −1 = back (screen right)
        private float _fallTimer, _climb;
        private Vector2Int? _target, _selected;
        private Vector2Int _aimFrom = new Vector2Int(-99, -99);
        private float _progress, _bob;
        private SpriteRenderer _highlight, _crack;
        private Sprite _sideFace;   // the right-hand (−u) face of a half block: a hole's side wall
        private HoldSensor _digButton;
        private TextMeshProUGUI _depthText, _tallyText;
        private string _hint = "";
        private float _hintUntil;

        // ------------------------------------------------------------------ tests

        public static CliffPrototype Current { get; private set; }
        public float U => _u;
        public int Feet => _feet;
        public int Column => Mathf.RoundToInt(_u);
        public Vector2Int? Target => _target;
        public Vector2Int? Selected => _selected;
        public bool ScriptedDig { get; set; }
        public IReadOnlyDictionary<string, int> Tally => _tally;
        public int DugCount => _dug.Count;
        public string DepthText => _depthText != null ? _depthText.text : "";

        // ------------------------------------------------------------------ the slice

        /// <summary>Solid at (u, row) on the slice: air above the surface; bedrock below the bottom and past the right end.</summary>
        public bool Solid(int u, int row)
        {
            if (row < 0) return false;
            if (u < 0 || u >= Width || row >= Depth) return true;
            return !_dug.Contains(new Vector2Int(u, row));
        }

        public bool Diggable(Vector2Int b) => b.x >= 0 && b.x < Width && b.y >= 0 && b.y < Depth && Solid(b.x, b.y);

        /// <summary>A ladder: a block dug straight down, in a shaft at least two blocks deep (a single step is just stepped up).</summary>
        public bool Ladder(int u, int row)
        {
            var c = new Vector2Int(u, row);
            return _dugDown.Contains(c) && (_dugDown.Contains(new Vector2Int(u, row - 1)) || _dugDown.Contains(new Vector2Int(u, row + 1)));
        }

        /// <summary>World point of the top of block (x into the cliff, u along it, row) (its sprite pivot).</summary>
        public Vector3 Top(float x, float u, float row) => new Vector3((x - u) * 0.5f, (x + u) * 0.25f + 0.25f - row * RowHeight, 0f);
        public Vector3 Top(float u, float row) => Top(0f, u, row);

        /// <summary>Behind the slice is solid rock that is never dug (it gives the tunnels their back wall).</summary>
        private bool SolidAt(int x, int u, int row) => x < 0 ? false : x == 0 ? Solid(u, row) : row >= 0;

        private int Layer(int row) => Mathf.Clamp(row / RowsPerLayer + 1, 1, _bands.Count);
        public int LayerOf(int row) => Mathf.Max(1, row / RowsPerLayer + 1);

        /// <summary>What a block holds (null = plain rock): the real veins, from the world seed and the layer's mix.</summary>
        public string OreAt(int u, int row)
        {
            var key = new Vector2Int(u, row);
            if (_ore.TryGetValue(key, out var ore)) return ore;
            var band = _bands[Layer(row) - 1];
            var comp = SiteMath.Generate(_seed, MineSlabView.SiteId, band);
            ore = MineVeins.OreAt(_seed ^ (ulong)band.Index * 0x9E3779B97F4A7C15UL, comp, band.BaselineResourceId, 0, u, row, out _);
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
            if (_root == null) { enabled = false; return; }
            var mine = _root.Services.Sites.Get(MineSlabView.SiteId);
            _bands = mine != null && mine.Config.Bands.Count > 0 ? mine.Config.Bands : new List<BandTemplate> { new BandTemplate { Index = 1 } };
            _seed = _root.Services.Scheduler.Coordinator.WorldSeed;
            _root.Hud.gameObject.SetActive(false);   // the prototype has its own controls

            _blockRoot = new GameObject("Blocks").transform;
            _blockRoot.SetParent(transform, false);
            _playerSorting = player.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (_playerSorting != null) _playerSorting.sortingLayerName = "Ground";
            _highlight = MakeRenderer("Target", highlightSprite);
            _crack = MakeRenderer("Crack", null);
            _sideFace = MakeSideFace();
            for (var row = 0; row < Depth; row++) DrawRow(row);
            BuildUi();
            _root.InputRouter.Tapped += OnTapped;
            _drawFeet = _feet;
            PlacePlayer();
            SnapCamera();
        }

        /// <summary>Front-to-back order: nearer the camera is a smaller x + u and a higher block. Four slots per block.</summary>
        public static int OrderFor(int x, int u, int row) => -4 * (x + u + row);

        private SpriteRenderer Take()
        {
            if (_pool.Count > 0)
            {
                var sr = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                sr.gameObject.SetActive(true);
                sr.transform.localScale = Vector3.one;
                return sr;
            }
            var go = new GameObject("Sprite");
            go.transform.SetParent(_blockRoot, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingLayerName = "Ground";
            if (spriteMaterial != null) r.sharedMaterial = spriteMaterial;
            return r;
        }

        private SpriteRenderer Put(Dictionary<Vector3Int, SpriteRenderer> map, Vector3Int key, Sprite sprite, Vector3 at, int order, Color colour)
        {
            if (!map.TryGetValue(key, out var sr)) map[key] = sr = Take();
            sr.sprite = sprite;
            sr.transform.position = at;
            sr.transform.localScale = Vector3.one;
            sr.sortingOrder = order;
            sr.color = colour;
            return sr;
        }

        private void Remove(Dictionary<Vector3Int, SpriteRenderer> map, Vector3Int key)
        {
            if (!map.TryGetValue(key, out var sr)) return;
            sr.gameObject.SetActive(false);
            _pool.Add(sr);
            map.Remove(key);
        }

        private void Clear(Dictionary<Vector3Int, SpriteRenderer> map)
        {
            foreach (var k in new List<Vector3Int>(map.Keys)) Remove(map, k);
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

        public int DrawnOrder(int u, int row) => _blocks.TryGetValue(new Vector3Int(0, u, row), out var sr) ? sr.sortingOrder : int.MinValue;
        public int PlayerOrder => _playerSorting != null ? _playerSorting.sortingOrder : 0;
        public bool LadderDrawn(int u, int row) => _marks.ContainsKey(new Vector3Int(-1, u, row));

        // ------------------------------------------------------------------ drawing

        private static readonly Color[] Bands =
        {
            new Color(0.86f, 0.71f, 0.50f), new Color(0.76f, 0.48f, 0.37f), new Color(0.53f, 0.56f, 0.65f),
            new Color(0.58f, 0.67f, 0.47f), new Color(0.62f, 0.49f, 0.60f), new Color(0.82f, 0.63f, 0.31f),
        };

        private static readonly Color BackWall = new Color(0.42f, 0.40f, 0.43f);   // the back of a tunnel: in shadow
        private static readonly Color Topsoil = new Color(0.80f, 0.64f, 0.46f);

        private Color RowColour(int row) => row < 2 ? Topsoil : Bands[(LayerOf(row) - 1) % Bands.Length];

        /// <summary>A block is drawn when the camera can see it: open in front, beside it, or above.</summary>
        private bool Visible(int x, int u, int row) => !SolidAt(x - 1, u, row) || !SolidAt(x, u - 1, row) || !SolidAt(x, u, row - 1);

        private void DrawRow(int row)
        {
            if (row < 0 || row >= Depth) return;
            for (var u = -Margin; u < Width + Margin; u++)
            for (var x = 0; x < (row == 0 ? BackDepth : 2); x++)
            {
                var key = new Vector3Int(x, u, row);
                if (!SolidAt(x, u, row) || !Visible(x, u, row)) { Remove(_blocks, key); Remove(_grass, key); continue; }
                var h = (u * 73856093) ^ (row * 19349663) ^ (x * 83492791);
                var sprite = blockSprites[Mathf.Abs(h) % blockSprites.Length];
                var back = x == 1 && !Solid(u, row);   // seen through a dug block: the tunnel's back wall
                var colour = RowColour(row) * (back ? BackWall : Color.white);
                colour.a = 1f;
                Put(_blocks, key, sprite, Top(x, u, row), OrderFor(x, u, row), colour);
                if (row == 0 && grassSprite != null) Put(_grass, key, grassSprite, Top(x, u, 0), OrderFor(x, u, 0) + 1, Color.white);
                else Remove(_grass, key);
            }
        }

        /// <summary>
        /// The −u face of a half block (its right-hand side on screen), as a white parallelogram whose pivot is the block's top
        /// centre, so it lines up with the block sprites. Tinted dark, it marks the side wall seen inside a hole.
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
                float lx = (x + 0.5f) / ppu, ly = (y + 0.5f) / ppu - 0.75f;   // local: top centre at (0, 0)
                var inside = ly <= -0.25f + 0.5f * lx && ly >= -0.75f + 0.5f * lx;
                px[y * w + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0f, 1f), ppu);
        }

        private static readonly Color HoleSide = new Color(0.18f, 0.13f, 0.12f, 0.62f);

        /// <summary>Shadows the side wall of every hole (a block whose right-hand neighbour along the cliff is dug). Key x = −4.</summary>
        private void DrawHoleSides()
        {
            foreach (var k in new List<Vector3Int>(_marks.Keys)) if (k.x == -4) Remove(_marks, k);
            foreach (var d in _dug)
            {
                var wall = new Vector2Int(d.x + 1, d.y);
                if (!Solid(wall.x, wall.y)) continue;
                Put(_marks, new Vector3Int(-4, wall.x, wall.y), _sideFace, Top(wall.x, wall.y), OrderFor(0, wall.x, wall.y) + 1, HoleSide);
            }
        }

        private void RedrawAround(int row)
        {
            for (var r = row - 2; r <= row + 2; r++) DrawRow(r);
            DrawHoleSides();
            DrawLadders();
        }

        /// <summary>Ladders on the back wall of every shaft cell that has one (key x = −1).</summary>
        private void DrawLadders()
        {
            foreach (var k in new List<Vector3Int>(_marks.Keys)) if (k.x == -1) Remove(_marks, k);
            if (ladderSprite == null) return;
            var scale = RowHeight / Mathf.Max(0.01f, ladderSprite.bounds.size.y) * 1.02f;
            foreach (var c in _dugDown)
            {
                if (!Ladder(c.x, c.y)) continue;
                // Seen through the shaft is the side wall of the next block along (u + 1): the ladder hangs on that face.
                var at = Top(c.x + 1, c.y) + new Vector3(0.25f, -0.125f - RowHeight, 0f);
                var sr = Put(_marks, new Vector3Int(-1, c.x, c.y), ladderSprite, at, OrderFor(0, c.x + 1, c.y) + 2, Color.white);
                sr.transform.localScale = new Vector3(0.7f, scale, 1f);
            }
        }

        // ------------------------------------------------------------------ the supplier

        private void Update()
        {
            if (_root == null) return;
            var move = _root.InputRouter.MoveVector;
            Move(move);
            Fall();
            PressAndHold();
            Aim(move);
            Dig();
            PlacePlayer();
            Outlines();
            Flecks();
            TrackCamera();
            UpdateText();
        }

        private static bool Vertical(Vector2 m) => Mathf.Abs(m.y) > 0.3f && Mathf.Abs(m.y) > Mathf.Abs(m.x);

        private bool Blocks(int u, int row) => Solid(u, row);

        /// <summary>On a ladder: standing on a rung, or with the body in the shaft beside one.</summary>
        public bool OnLadder => Ladder(Column, _feet) || Ladder(Column, _feet - 1);

        private void Move(Vector2 move)
        {
            if (move.sqrMagnitude < 0.04f) { _climb = 0f; return; }
            if (Vertical(move))
            {
                if (!OnLadder) return;   // up and down only aim, unless on a ladder
                _u = Mathf.MoveTowards(_u, Column, Time.deltaTime * 4f);   // straighten up on the ladder
                _climb += ClimbSpeed * Mathf.Min(1f, Mathf.Abs(move.y)) * Time.deltaTime;
                if (_climb < 1f) return;
                _climb = 0f;
                if (move.y > 0f && Ladder(Column, _feet - 1) && !Blocks(Column, _feet - 3)) _feet -= 1;
                else if (move.y < 0f && Ladder(Column, _feet)) _feet += 1;
                return;
            }
            _climb = 0f;
            // Along the cliff: screen left is +u. The stick's own up/down is ignored here.
            var du = -Mathf.Sign(move.x) * WalkSpeed * Mathf.Min(1f, Mathf.Abs(move.x) * 1.4f) * Time.deltaTime;
            _facing = du > 0f ? 1 : -1;
            TryStep(_u + du);
        }

        private void TryStep(float u)
        {
            u = Mathf.Clamp(u, -Margin + 1, Width + Margin - 2);
            var cu = Mathf.RoundToInt(u + Mathf.Sign(u - _u) * 0.3f);
            if (cu == Column) { _u = u; return; }
            if (!Blocks(cu, _feet - 1) && !Blocks(cu, _feet - 2)) { _u = u; return; }
            // A single block in the way, with headroom above it and above the supplier: step up onto it.
            if (Blocks(cu, _feet - 1) && !Blocks(cu, _feet - 2) && !Blocks(cu, _feet - 3) && !Blocks(Column, _feet - 3))
            {
                _feet -= 1;
                _u = u;
                return;
            }
            _u = Mathf.Clamp(_u, Column - 0.3f, Column + 0.3f);
        }

        /// <summary>No ground or rung underfoot: drop a block at a time.</summary>
        private void Fall()
        {
            if (Blocks(Column, _feet) || Ladder(Column, _feet)) { _fallTimer = 0f; return; }
            _fallTimer += Time.deltaTime;
            if (_fallTimer < 0.07f) return;
            _fallTimer = 0f;
            _feet += 1;
        }

        /// <summary>
        /// The block to dig. A tapped or pressed block wins while the supplier stays put. Otherwise the stick: down = the
        /// block underfoot, up = the block overhead, along = the wall walked into (head height, then legs). Standing still:
        /// the wall they face if they are up against it, else the block underfoot.
        /// </summary>
        private void Aim(Vector2 move)
        {
            var here = new Vector2Int(Column, _feet);
            if (here != _aimFrom) { _aimFrom = here; _selected = null; }
            if (_selected.HasValue && (!Diggable(_selected.Value) || !InReach(_selected.Value))) _selected = null;
            var t = _selected;
            if (!t.HasValue)
            {
                var head = new Vector2Int(Column + _facing, _feet - 2);
                var legs = new Vector2Int(Column + _facing, _feet - 1);
                var under = new Vector2Int(Column, _feet);
                var over = new Vector2Int(Column, _feet - 3);
                var still = move.sqrMagnitude < 0.04f;
                if (!still && Vertical(move)) t = move.y < 0f ? (Diggable(under) ? under : (Vector2Int?)null) : Diggable(over) ? over : (Vector2Int?)null;
                else if (Diggable(head)) t = head;
                else if (Diggable(legs)) t = legs;
                else if (still && Diggable(under)) t = under;
            }
            if (t != _target) _progress = 0f;
            _target = t;
            _highlight.enabled = t.HasValue;
            if (!t.HasValue) return;
            _highlight.transform.position = Top(t.Value.x, t.Value.y);
            _highlight.sortingOrder = OrderFor(0, t.Value.x, t.Value.y) + 3;
            _highlight.color = _selected.HasValue ? new Color(1f, 0.86f, 0.3f, 0.7f) : new Color(1f, 0.92f, 0.45f, 0.45f);
        }

        private float Hardness(Vector2Int b) => 0.55f + 0.02f * b.y + (OreAt(b.x, b.y) != null ? 0.25f : 0f);

        private bool Digging => (_digButton != null && _digButton.Held) || ScriptedDig || _pressDigging;

        private void Dig()
        {
            _crack.enabled = false;
            if (!Digging || !_target.HasValue) return;
            var b = _target.Value;
            _progress += Time.deltaTime / Hardness(b);
            _bob += Time.deltaTime * 14f;
            if (crackSprites.Length > 0 && _progress > 0.05f)
            {
                _crack.enabled = true;
                _crack.sprite = crackSprites[Mathf.Clamp((int)(_progress * crackSprites.Length), 0, crackSprites.Length - 1)];
                _crack.transform.position = Top(b.x, b.y);
                _crack.sortingOrder = OrderFor(0, b.x, b.y) + 3;
            }
            if (_progress < 1f) return;
            _progress = 0f;
            _dug.Add(b);
            if (b.x == Column && b.y == _feet) _dugDown.Add(b);   // straight down: a ladder once the shaft is two deep
            if (_selected == b) _selected = null;
            var ore = OreAt(b.x, b.y) ?? _bands[Layer(b.y) - 1].BaselineResourceId ?? "rock";
            _tally[ore] = _tally.TryGetValue(ore, out var n) ? n + 1 : 1;
            RedrawAround(b.y);
            _fleckKey = "";
            _outlineKey = "";
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
        }

        private void PlacePlayer()
        {
            _drawFeet = Mathf.MoveTowards(_drawFeet, _feet, Time.deltaTime * 10f);
            player.position = Top(_u, _drawFeet);
            // Sorted as the cube of the lower body: blocks in front cover them, blocks behind do not.
            if (_playerSorting != null) _playerSorting.sortingOrder = OrderFor(0, Column, _feet - 1) + 2;
            if (playerVisual == null) return;
            var sc = playerVisual.localScale;
            sc.x = Mathf.Abs(sc.x) * (_facing > 0 ? -1f : 1f);
            playerVisual.localScale = sc;
            playerVisual.localRotation = Quaternion.Euler(0f, 0f, Digging && _target.HasValue && !Feel.ReducedMotion ? Mathf.Sin(_bob) * 12f : 0f);
        }

        // ------------------------------------------------------------------ picking a block

        /// <summary>Within reach: two blocks either way, from one under the feet to one above the head.</summary>
        public bool InReach(Vector2Int b) => Mathf.Abs(b.x - Column) <= 2 && b.y >= _feet - 3 && b.y <= _feet + 1;

        public List<Vector2Int> Reachable()
        {
            var list = new List<Vector2Int>();
            for (var row = _feet - 3; row <= _feet + 1; row++)
            for (var u = Column - 2; u <= Column + 2; u++)
            {
                var b = new Vector2Int(u, row);
                if (Diggable(b) && _blocks.ContainsKey(new Vector3Int(0, u, row))) list.Add(b);
            }
            return list;
        }

        private const float TapRadius = 0.9f;

        /// <summary>Reachable blocks nearest a point (exact overlaps take turns).</summary>
        public List<Vector2Int> NearestReachable(Vector2 world)
        {
            var scored = new List<(float d, Vector2Int b)>();
            foreach (var b in Reachable())
            {
                var d = Vector2.Distance(world, (Vector2)Top(b.x, b.y) + new Vector2(0f, -0.25f));
                if (d <= TapRadius) scored.Add((d, b));
            }
            scored.Sort((a, c) => a.d.CompareTo(c.d));
            var list = new List<Vector2Int>();
            foreach (var s in scored)
                if (s.d - scored[0].d < 0.05f) list.Add(s.b);
            return list;
        }

        /// <summary>A tap selects the reachable block nearest the finger; tapping it again clears the selection.</summary>
        public void OnTapped(Vector2 screen)
        {
            var world = (Vector2)worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            var near = NearestReachable(world);
            if (near.Count == 0) return;
            var i = _selected.HasValue ? near.IndexOf(_selected.Value) : -1;
            _selected = i < 0 ? near[0] : i + 1 < near.Count ? near[i + 1] : (Vector2Int?)null;
            _progress = 0f;
        }

        private bool _pressDigging;

        /// <summary>A finger held still on a reachable block selects it and digs it, without the button.</summary>
        private void PressAndHold()
        {
            _pressDigging = false;
            if (!_root.InputRouter.StillPress(out var pixels, out var seconds) || seconds < 0.25f) return;
            var near = NearestReachable(worldCamera.ScreenToWorldPoint(new Vector3(pixels.x, pixels.y, 10f)));
            if (near.Count == 0) return;
            if (!_selected.HasValue || !near.Contains(_selected.Value)) { _selected = near[0]; _progress = 0f; }
            _pressDigging = true;
        }

        // ------------------------------------------------------------------ outlines and flecks

        private string _outlineKey = "", _fleckKey = "";

        private void Outlines()
        {
            var key = $"{Column}|{_feet}|{_dug.Count}";
            if (key == _outlineKey) return;
            _outlineKey = key;
            foreach (var k in new List<Vector3Int>(_marks.Keys)) if (k.x == -2) Remove(_marks, k);
            foreach (var b in Reachable())
                Put(_marks, new Vector3Int(-2, b.x, b.y), highlightSprite, Top(b.x, b.y), OrderFor(0, b.x, b.y) + 3, new Color(1f, 1f, 0.85f, 0.16f));
        }

        public int OutlinedBlocks
        {
            get
            {
                var n = 0;
                foreach (var k in _marks.Keys) if (k.x == -2) n++;
                return n;
            }
        }

        private void Flecks()
        {
            var key = $"{Column}|{_feet}|{_dug.Count}";
            if (key == _fleckKey) return;
            _fleckKey = key;
            foreach (var k in new List<Vector3Int>(_marks.Keys)) if (k.x == -3) Remove(_marks, k);
            var me = (Vector2)Top(_u, _feet - 1);
            for (var row = Mathf.Max(0, _feet - 7); row <= Mathf.Min(Depth - 1, _feet + 5); row++)
            for (var u = Mathf.Max(0, Column - 7); u <= Mathf.Min(Width - 1, Column + 7); u++)
            {
                if (!Solid(u, row) || !_blocks.ContainsKey(new Vector3Int(0, u, row))) continue;
                if (Vector2.Distance((Vector2)Top(u, row) + new Vector2(0f, -0.25f), me) > LampRadius) continue;
                var ore = OreAt(u, row);
                var strong = ore != null;
                if (!strong)
                    foreach (var (du, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if ((ore = OreAt(u + du, row + dr)) != null) break;
                if (ore == null) continue;
                var col = _root.Services.Catalogue.TryGet<ResourceDefinition>(ore, out var res) ? res.MapTint : Color.white;
                col.a = strong ? 1f : 0.4f;
                Put(_marks, new Vector3Int(-3, u, row), fleckSprite, Top(u, row), OrderFor(0, u, row) + 1, col);
            }
        }

        // ------------------------------------------------------------------ camera and controls

        private void TrackCamera()
        {
            worldCamera.orthographicSize = FollowCamera.OrthoSizeForWidth(9f, worldCamera.aspect);
            var target = player.position + new Vector3(0f, 0.4f, -10f);
            worldCamera.transform.position = Vector3.Lerp(worldCamera.transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        private void SnapCamera()
        {
            worldCamera.orthographicSize = FollowCamera.OrthoSizeForWidth(9f, worldCamera.aspect);
            worldCamera.transform.position = player.position + new Vector3(0f, 0.4f, -10f);
        }

        private void UpdateText()
        {
            var depth = _feet <= 0 ? "On the surface" : $"Depth {_feet} blocks · layer {LayerOf(_feet - 1)}";
            if (OnLadder) depth += " · on a ladder";
            _depthText.text = depth;
            _tallyText.text = Time.time < _hintUntil ? _hint : TallyText();
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
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1100f, 330f));
            var title = ui.Text("Title", card.transform,
                "<b>Mining prototype: the cliff face</b>\nLeft/right walks along the cliff. Down digs underfoot, up digs overhead. Dig two or more down and a ladder appears: climb it with up/down.",
                theme.smallSize - 6f, theme.ink, TextAlignmentOptions.TopLeft);
            UiFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(1040f, 180f));
            _depthText = ui.Text("Depth", card.transform, "", theme.smallSize - 2f, theme.ink, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            UiFactory.Place(_depthText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 70f), new Vector2(1040f, 60f));
            _tallyText = ui.Text("Tally", card.transform, "", theme.smallSize - 6f, theme.ink, TextAlignmentOptions.BottomLeft);
            UiFactory.Place(_tallyText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 16f), new Vector2(1040f, 56f));

            var back = ui.Button("Back", safe, "Back to town", BackToTown, out _);
            UiFactory.Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 60f), new Vector2(380f, 130f));

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
