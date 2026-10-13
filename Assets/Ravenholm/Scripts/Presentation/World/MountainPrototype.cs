using System.Collections.Generic;
using Ravenholm.Content;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// PROTOTYPE ("forest mountains", 2026-10-13): a plot of forest with height, on the same isometric grid.
    ///
    /// The ground is blocks (half a level tall, like the Mine's). Mountains rise up to seven blocks above the forest
    /// floor, with valleys of forest between them; their slopes rise in steps of one (climbable) or two (walls). The
    /// supplier steps up one block at a time and drops down any height. The button chops the tree faced, clears the
    /// stump faced, digs the wall faced (head height, then legs), or, standing still, the block underfoot. A stump left
    /// standing grows back into a tree after a while; a cleared one never does. Ore shows in the rock. Only the area
    /// around the camera is drawn. Self-contained: nothing here touches the company save.
    /// </summary>
    public sealed class MountainPrototype : MonoBehaviour
    {
        public const string SceneName = "MountainPrototype";
        public const string TownSceneName = "DebugSandbox";
        public const int Size = 48;          // cells each way
        public const int MaxHeight = 9;      // blocks (z 0..8)
        public const float RowHeight = 0.5f;
        private const float WalkSpeed = 3.2f;

        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform player;
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private CharacterAnimationSet animations;
        [SerializeField] private Sprite grassBlock, soilBlock, rockBlock, deepRockBlock;
        [SerializeField] private Sprite[] oreBlocks = new Sprite[0];
        [SerializeField] private string[] oreIds = new string[0];
        [SerializeField] private Sprite pineSprite, leafySprite, stumpSprite;
        [SerializeField] private Sprite highlightSprite;
        [SerializeField] private Sprite[] crackSprites = new Sprite[0];
        [SerializeField] private Material spriteMaterial;

        public void EditorSetup(Camera cam, Transform playerRoot, SpriteRenderer bodyRenderer, CharacterAnimationSet set, Sprite grass, Sprite soil,
            Sprite rock, Sprite deepRock, Sprite[] ores, string[] ids, Sprite pine, Sprite leafy, Sprite stump, Sprite highlight, Sprite[] cracks, Material mat)
        {
            worldCamera = cam;
            player = playerRoot;
            body = bodyRenderer;
            animations = set;
            grassBlock = grass;
            soilBlock = soil;
            rockBlock = rock;
            deepRockBlock = deepRock;
            oreBlocks = ores;
            oreIds = ids;
            pineSprite = pine;
            leafySprite = leafy;
            stumpSprite = stump;
            highlightSprite = highlight;
            crackSprites = cracks;
            spriteMaterial = mat;
        }

        // ------------------------------------------------------------------ the plot

        private readonly bool[,,] _solid = new bool[Size, Size, MaxHeight];
        private readonly Dictionary<Vector2Int, bool> _trees = new Dictionary<Vector2Int, bool>();   // cell -> leafy?
        private readonly HashSet<Vector2Int> _stumps = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, float> _felledAt = new Dictionary<Vector2Int, float>();
        private readonly Dictionary<Vector2Int, bool> _wasLeafy = new Dictionary<Vector2Int, bool>();
        private readonly HashSet<Vector2Int> _cleared = new HashSet<Vector2Int>();

        /// <summary>Seconds before a stump left standing grows back into a tree (short, to see it in the prototype).</summary>
        public static float RegrowSeconds = 60f;

        public bool HasStump(int x, int y) => _stumps.Contains(new Vector2Int(x, y));
        public bool IsCleared(int x, int y) => _cleared.Contains(new Vector2Int(x, y));
        private readonly Dictionary<Vector3Int, int> _ore = new Dictionary<Vector3Int, int>();      // voxel -> ore index
        private readonly Dictionary<string, int> _tally = new Dictionary<string, int>();

        public static MountainPrototype Current { get; private set; }

        public bool Solid(int x, int y, int z) => x >= 0 && y >= 0 && x < Size && y < Size && z >= 0 && z < MaxHeight && _solid[x, y, z];
        private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;
        public int TopOf(int x, int y) { var z = MaxHeight - 1; while (z >= 0 && !Solid(x, y, z)) z--; return z + 1; }   // first free level
        public bool HasTree(int x, int y) => _trees.ContainsKey(new Vector2Int(x, y));
        public int TreesLeft => _trees.Count;
        public int DugCount { get; private set; }
        public IReadOnlyDictionary<string, int> Tally => _tally;

        private static float Hash(int x, int y, int salt)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>Smooth value noise in 0..1 on a grid of <paramref name="scale"/> cells.</summary>
        private static float Noise(float x, float y, float scale, int salt)
        {
            float fx = x / scale, fy = y / scale;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = Mathf.SmoothStep(0f, 1f, fx - x0), ty = Mathf.SmoothStep(0f, 1f, fy - y0);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, salt), Hash(x0 + 1, y0, salt), tx),
                              Mathf.Lerp(Hash(x0, y0 + 1, salt), Hash(x0 + 1, y0 + 1, salt), tx), ty);
        }

        /// <summary>The ground's top (first free level) from the mountains' shape: flat floor, hills, peaks of seven.</summary>
        private static int Height(int x, int y)
        {
            var n = 0.65f * Noise(x, y, 11f, 11) + 0.35f * Noise(x, y, 5f, 12);
            var m = Mathf.Clamp01((n - 0.42f) / 0.36f);
            var start = Vector2.Distance(new Vector2(x, y), StartCell) < 6f;   // open, flat ground where the supplier starts
            var edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(Size - 1 - x, Size - 1 - y)) < 2;
            return start || edge ? 1 : 1 + Mathf.RoundToInt(m * (MaxHeight - 2));
        }

        private void Generate()
        {
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                var top = Height(x, y);
                for (var z = 0; z < top; z++)
                {
                    _solid[x, y, z] = true;
                    if (z >= 1 && z < top - 1 && Hash(x * 7 + z, y, 2) < 0.12f) _ore[new Vector3Int(x, y, z)] = (int)(Hash(x, y + z, 3) * oreBlocks.Length) % Mathf.Max(1, oreBlocks.Length);
                }
            }
            // Forest: thick in the valleys, thinner up the mountains; open ground where the supplier starts.
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                if (Vector2.Distance(new Vector2(x, y), StartCell) < 2.5f) continue;
                var top = TopOf(x, y);
                var chance = top <= 2 ? 0.42f : top <= 4 ? 0.2f : 0.08f;
                if (Hash(x, y, 4) < chance) _trees[new Vector2Int(x, y)] = Noise(x, y, 9f, 13) > 0.55f;   // leafy groves here and there
            }
        }

        public static readonly Vector2Int StartCell = new Vector2Int(6, 20);

        // ------------------------------------------------------------------ drawing

        private readonly Dictionary<Vector3Int, SpriteRenderer> _blocks = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector2Int, SpriteRenderer> _treeSprites = new Dictionary<Vector2Int, SpriteRenderer>();
        private Transform _root3;
        private SpriteRenderer _highlight, _crack;

        public Vector3 Top(float x, float y, float z) => new Vector3((x - y) * 0.5f, (x + y) * 0.25f + 0.25f + z * RowHeight, 0f);

        /// <summary>Front to back: higher blocks and blocks nearer the camera (smaller x + y) draw later.</summary>
        public static int OrderFor(int x, int y, int z) => 4 * (z - x - y);

        private bool Visible(int x, int y, int z) => !Solid(x - 1, y, z) || !Solid(x, y - 1, z) || !Solid(x, y, z + 1);

        private SpriteRenderer NewRenderer(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root3, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = "Ground";
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }

        private Sprite SpriteFor(int x, int y, int z)
        {
            if (_ore.TryGetValue(new Vector3Int(x, y, z), out var o) && o < oreBlocks.Length) return oreBlocks[o];
            if (!Solid(x, y, z + 1)) return grassBlock;                 // open sky above: grass
            if (!Solid(x, y, z + 2)) return soilBlock;                  // the topsoil just under it
            return z >= 5 ? deepRockBlock : rockBlock;
        }

        private void Redraw(int cx, int cy)
        {
            for (var x = cx - 1; x <= cx + 1; x++)
            for (var y = cy - 1; y <= cy + 1; y++)
            for (var z = 0; z < MaxHeight; z++)
                DrawVoxel(x, y, z);
        }

        private void DrawVoxel(int x, int y, int z)
        {
            if (!Inside(x, y) || !InArea(x, y)) return;
            var key = new Vector3Int(x, y, z);
            var show = Solid(x, y, z) && Visible(x, y, z);
            if (!show)
            {
                if (_blocks.TryGetValue(key, out var old)) { Destroy(old.gameObject); _blocks.Remove(key); }
                return;
            }
            if (!_blocks.TryGetValue(key, out var sr)) _blocks[key] = sr = NewRenderer("Block", null);
            sr.sprite = SpriteFor(x, y, z);
            sr.transform.position = Top(x, y, z);
            sr.sortingOrder = OrderFor(x, y, z);
        }

        private void DrawTree(Vector2Int c)
        {
            if (_treeSprites.TryGetValue(c, out var old)) { Destroy(old.gameObject); _treeSprites.Remove(c); }
            var stump = _stumps.Contains(c);
            if (!_trees.TryGetValue(c, out var leafy) && !stump) return;
            var top = TopOf(c.x, c.y);
            var sr = NewRenderer(stump ? "Stump" : "Tree", stump ? stumpSprite : leafy ? leafySprite : pineSprite);
            sr.transform.position = Top(c.x, c.y, top - 1);
            sr.transform.localScale = Vector3.one * (stump ? 0.9f : 0.62f);
            sr.sortingOrder = OrderFor(c.x, c.y, top) + 2;
            _treeSprites[c] = sr;
        }

        public int DrawnBlocks => _blocks.Count;
        public int PlayerOrder => body != null ? body.sortingOrder : 0;
        public int DrawnOrder(int x, int y, int z) => _blocks.TryGetValue(new Vector3Int(x, y, z), out var sr) ? sr.sortingOrder : int.MinValue;

        // ------------------------------------------------------------------ the supplier

        private Vector2 _p;            // continuous cell coordinates
        private int _feet;             // the level the supplier stands at (on top of block feet − 1)
        private float _drawFeet;
        private Vector2Int _facing = new Vector2Int(1, 0);
        private float _fallTimer;
        private Vector2 _heading = new Vector2(-1f, -0.5f);

        public Vector2Int Cell => new Vector2Int(Mathf.RoundToInt(_p.x), Mathf.RoundToInt(_p.y));
        public int Feet => _feet;
        public Vector2 Position => _p;
        public Vector2Int Facing => _facing;
        public bool ScriptedWork { get; set; }

        private bool Blocks(int x, int y, int z) => !Inside(x, y) || Solid(x, y, z) || z >= 0 && HasTree(x, y) && z >= TopOf(x, y) - 1;

        private void Move(Vector2 m)
        {
            if (m.sqrMagnitude < 0.04f) return;
            // Screen = dx·(0.5, 0.25) + dy·(−0.5, 0.25)  ⇒  dx = m.x + 2·m.y, dy = −m.x + 2·m.y.
            var dx = m.x + 2f * m.y;
            var dy = -m.x + 2f * m.y;
            var len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            var step = WalkSpeed * Mathf.Min(1f, m.magnitude) * Time.deltaTime / len;
            dx *= step;
            dy *= step;
            _facing = Mathf.Abs(dx) >= Mathf.Abs(dy) ? new Vector2Int(dx > 0 ? 1 : -1, 0) : new Vector2Int(0, dy > 0 ? 1 : -1);
            _heading = m;
            TryStep(new Vector2(_p.x + dx, _p.y));
            TryStep(new Vector2(_p.x, _p.y + dy));
        }

        private void TryStep(Vector2 to)
        {
            to.x = Mathf.Clamp(to.x, 0f, Size - 1);
            to.y = Mathf.Clamp(to.y, 0f, Size - 1);
            var here = Cell;
            var delta = to - _p;
            var c = new Vector2Int(Mathf.RoundToInt(to.x + Mathf.Sign(delta.x) * (Mathf.Abs(delta.x) > 1e-5f ? 0.3f : 0f)),
                                   Mathf.RoundToInt(to.y + Mathf.Sign(delta.y) * (Mathf.Abs(delta.y) > 1e-5f ? 0.3f : 0f)));
            if (c == here) { _p = to; return; }
            if (!Blocks(c.x, c.y, _feet) && !Blocks(c.x, c.y, _feet + 1)) { _p = to; return; }
            // One block up, with headroom above it and above the supplier: step up onto it.
            if (Solid(c.x, c.y, _feet) && !HasTree(c.x, c.y) && !Blocks(c.x, c.y, _feet + 1) && !Blocks(c.x, c.y, _feet + 2) && !Solid(here.x, here.y, _feet + 2))
            {
                _feet += 1;
                _p = to;
                return;
            }
            _p = new Vector2(Mathf.Clamp(_p.x, here.x - 0.3f, here.x + 0.3f), Mathf.Clamp(_p.y, here.y - 0.3f, here.y + 0.3f));
        }

        private void Fall()
        {
            var c = Cell;
            if (Solid(c.x, c.y, _feet - 1) || _feet <= 1) { _fallTimer = 0f; return; }
            _fallTimer += Time.deltaTime;
            if (_fallTimer < 0.07f) return;
            _fallTimer = 0f;
            _feet -= 1;
        }

        // ------------------------------------------------------------------ digging and chopping

        private Vector3Int? _target;       // a block to dig
        private Vector2Int? _treeTarget;   // or a tree to chop
        private Vector2Int? _stumpTarget;  // or a stump to clear
        public Vector2Int? StumpTarget => _stumpTarget;
        private float _progress;
        private Vector3Int? _progressOn;
        private HoldSensor _button;
        private TextMeshProUGUI _buttonLabel, _status;

        public Vector3Int? Target => _target;
        public Vector2Int? TreeTarget => _treeTarget;
        private bool Working => (_button != null && _button.Held) || ScriptedWork;

        /// <summary>
        /// The tree faced, else the wall faced (head height, then legs), else, standing still, the block underfoot.
        /// The floor (level 0) is bedrock.
        /// </summary>
        private void Aim(bool still)
        {
            var c = Cell;
            var a = c + _facing;
            _target = null;
            _treeTarget = null;
            _stumpTarget = null;
            if (Inside(a.x, a.y) && HasTree(a.x, a.y) && Mathf.Abs(TopOf(a.x, a.y) - _feet) <= 1) { _treeTarget = a; return; }
            if (Inside(a.x, a.y) && HasStump(a.x, a.y) && Mathf.Abs(TopOf(a.x, a.y) - _feet) <= 1) { _stumpTarget = a; return; }
            if (HasStump(c.x, c.y) && still) { _stumpTarget = c; return; }   // the stump underfoot
            if (Solid(a.x, a.y, _feet + 1)) _target = new Vector3Int(a.x, a.y, _feet + 1);
            else if (Solid(a.x, a.y, _feet)) _target = new Vector3Int(a.x, a.y, _feet);
            else if (still && _feet - 1 >= 1 && Solid(c.x, c.y, _feet - 1)) _target = new Vector3Int(c.x, c.y, _feet - 1);
        }

        private void Work()
        {
            _crack.enabled = false;
            if (!Working) return;
            if (_treeTarget.HasValue)
            {
                var key = new Vector3Int(_treeTarget.Value.x, _treeTarget.Value.y, -1);
                if (_progressOn != key) { _progressOn = key; _progress = 0f; }
                _progress += Time.deltaTime / 1.4f;
                if (_progress < 1f) return;
                var c = _treeTarget.Value;
                var leafy = _trees[c];
                _trees.Remove(c);
                _stumps.Add(c);
                _felledAt[c] = Time.time;
                _wasLeafy[c] = leafy;
                DrawTree(c);
                Add(leafy ? "resource.maple_log" : "resource.cedar_log", 3);
                _progress = 0f;
                FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
                return;
            }
            if (_stumpTarget.HasValue)
            {
                // Hitting a stump further pulls it out: the spot stays open for good.
                var sc = _stumpTarget.Value;
                var skey = new Vector3Int(sc.x, sc.y, -2);
                if (_progressOn != skey) { _progressOn = skey; _progress = 0f; }
                _progress += Time.deltaTime / 0.8f;
                if (_progress < 1f) return;
                _progress = 0f;
                _stumps.Remove(sc);
                _felledAt.Remove(sc);
                _cleared.Add(sc);
                DrawTree(sc);
                Add(_wasLeafy.TryGetValue(sc, out var lf) && lf ? "resource.maple_log" : "resource.cedar_log", 1);
                FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
                return;
            }
            if (!_target.HasValue) return;
            var t = _target.Value;
            if (_progressOn != t) { _progressOn = t; _progress = 0f; }
            _progress += Time.deltaTime / (0.45f + 0.06f * t.z + (_ore.ContainsKey(t) ? 0.25f : 0f));
            if (crackSprites.Length > 0 && _progress > 0.05f)
            {
                _crack.enabled = true;
                _crack.sprite = crackSprites[Mathf.Clamp((int)(_progress * crackSprites.Length), 0, crackSprites.Length - 1)];
                _crack.transform.position = Top(t.x, t.y, t.z);
                _crack.sortingOrder = OrderFor(t.x, t.y, t.z) + 3;
            }
            if (_progress < 1f) return;
            _progress = 0f;
            _solid[t.x, t.y, t.z] = false;
            DugCount++;
            Add(_ore.TryGetValue(t, out var o) && o < oreIds.Length ? oreIds[o] : "resource.stone", 1);
            _ore.Remove(t);
            Redraw(t.x, t.y);
            // A tree standing on a column that was dug under drops with it.
            if (_trees.ContainsKey(new Vector2Int(t.x, t.y)) || _stumps.Contains(new Vector2Int(t.x, t.y))) DrawTree(new Vector2Int(t.x, t.y));
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
        }

        /// <summary>Stumps left standing grow back into trees after a while (never under the supplier).</summary>
        private void Regrow()
        {
            if (_felledAt.Count == 0) return;
            foreach (var kv in new List<KeyValuePair<Vector2Int, float>>(_felledAt))
            {
                if (Time.time - kv.Value < RegrowSeconds || kv.Key == Cell) continue;
                _felledAt.Remove(kv.Key);
                _stumps.Remove(kv.Key);
                _trees[kv.Key] = _wasLeafy.TryGetValue(kv.Key, out var leafy) && leafy;
                DrawTree(kv.Key);
                _revealKey = "";
            }
        }

        /// <summary>Tests: put the supplier on a cell, facing a direction.</summary>
        public void PutAt(Vector2Int cell, Vector2Int facing)
        {
            _p = cell;
            _feet = TopOf(cell.x, cell.y);
            _drawFeet = _feet;
            _facing = facing;
            Place();
        }

        private void Add(string id, int n) => _tally[id] = (_tally.TryGetValue(id, out var k) ? k : 0) + n;

        // ------------------------------------------------------------------ lifecycle

        private GameRoot _root;

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null && _root.Hud != null) _root.Hud.gameObject.SetActive(true);
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root != null) _root.Hud.gameObject.SetActive(false);   // the prototype has its own controls
            _root3 = new GameObject("Plot").transform;
            _root3.SetParent(transform, false);
            Generate();
            _highlight = NewRenderer("Target", highlightSprite);
            _highlight.color = new Color(1f, 0.92f, 0.45f, 0.45f);
            _crack = NewRenderer("Crack", null);
            _crack.enabled = false;
            _p = StartCell;
            _feet = TopOf(StartCell.x, StartCell.y);
            _drawFeet = _feet;
            if (body != null) { body.sortingLayerName = "Ground"; body.spriteSortPoint = SpriteSortPoint.Pivot; }
            if (_root != null) BuildUi();
            Place();
            worldCamera.orthographicSize = FollowCamera.OrthoSizeForWidth(9f, worldCamera.aspect);
            worldCamera.transform.position = player.position + new Vector3(0f, 0.4f, -10f);
            DrawArea(true);
        }

        // ------------------------------------------------------------------ drawing only around the camera

        private Vector2Int _areaCentre = new Vector2Int(-999, -999);
        private int _sumReach = 30, _diffReach = 20;   // the screen as a diamond of cells: tall (x + y) and narrow (x − y)

        /// <summary>The camera's cell on the floor (its centre, ignoring height).</summary>
        private Vector2Int CameraCell()
        {
            var c = (Vector2)worldCamera.transform.position;
            float sum = (c.y - 0.25f - RowHeight) * 4f, diff = c.x * 2f;
            return new Vector2Int(Mathf.RoundToInt((sum + diff) * 0.5f), Mathf.RoundToInt((sum - diff) * 0.5f));
        }

        private bool InArea(int x, int y) =>
            Mathf.Abs(x + y - _areaCentre.x - _areaCentre.y) <= _sumReach && Mathf.Abs(x - y - _areaCentre.x + _areaCentre.y) <= _diffReach;

        private void DrawArea(bool force)
        {
            var c = CameraCell();
            if (!force && Mathf.Abs(c.x - _areaCentre.x) + Mathf.Abs(c.y - _areaCentre.y) < 3) return;
            _areaCentre = c;
            // Screen height in cells of x + y (a quarter unit each), plus room for the tallest columns; width in x − y.
            var halfH = worldCamera.orthographicSize + 1f;
            _sumReach = Mathf.CeilToInt(halfH * 4f) + 2 * MaxHeight + 2;
            _diffReach = Mathf.CeilToInt((halfH * worldCamera.aspect + 1f) * 2f) + 2;
            foreach (var k in new List<Vector3Int>(_blocks.Keys))
                if (!InArea(k.x, k.y)) { Destroy(_blocks[k].gameObject); _blocks.Remove(k); }
            foreach (var k in new List<Vector2Int>(_treeSprites.Keys))
                if (!InArea(k.x, k.y)) { Destroy(_treeSprites[k].gameObject); _treeSprites.Remove(k); }
            var reach = (_sumReach + _diffReach) / 2 + 1;
            for (var x = c.x - reach; x <= c.x + reach; x++)
            for (var y = c.y - reach; y <= c.y + reach; y++)
            {
                if (!Inside(x, y) || !InArea(x, y)) continue;
                for (var z = 0; z < MaxHeight; z++)
                    if (!_blocks.ContainsKey(new Vector3Int(x, y, z))) DrawVoxel(x, y, z);
                if (!_treeSprites.ContainsKey(new Vector2Int(x, y))) DrawTree(new Vector2Int(x, y));
            }
            _revealKey = "";
        }

        private void Update()
        {
            var move = _root != null ? _root.InputRouter.MoveVector : Vector2.zero;
            var moving = move.sqrMagnitude >= 0.04f;
            Move(move);
            Fall();
            Aim(!moving);
            Work();
            Place();
            Animate(moving);
            Highlight();
            DrawArea(false);
            Regrow();
            Reveal();
            worldCamera.orthographicSize = FollowCamera.OrthoSizeForWidth(9f, worldCamera.aspect);
            worldCamera.transform.position = Vector3.Lerp(worldCamera.transform.position, player.position + new Vector3(0f, 0.4f, -10f), 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (_buttonLabel != null) _buttonLabel.text = _treeTarget.HasValue ? "Chop" : _stumpTarget.HasValue ? "Clear" : "Dig";
            if (_status != null) _status.text = StatusText();
        }

        private void Place()
        {
            _drawFeet = Mathf.MoveTowards(_drawFeet, _feet, Time.deltaTime * 10f);
            player.position = Top(_p.x, _p.y, _drawFeet - 1);
            if (body != null) body.sortingOrder = OrderFor(Cell.x, Cell.y, _feet) + 2;   // as the cube of the lower body
        }

        private void Highlight()
        {
            _highlight.enabled = _target.HasValue;
            if (!_target.HasValue) return;
            var t = _target.Value;
            _highlight.transform.position = Top(t.x, t.y, t.z);
            _highlight.sortingOrder = OrderFor(t.x, t.y, t.z) + 3;
        }

        // ------------------------------------------------------------------ seeing the supplier behind the mountain

        private string _revealKey = "";
        private readonly List<SpriteRenderer> _faded = new List<SpriteRenderer>();
        public int FadedCount => _faded.Count;

        /// <summary>
        /// Rock and trees standing between the camera and the supplier turn see-through (never hidden), so they can be
        /// seen behind a ridge. Only what is drawn in front of them and overlaps them on screen fades.
        /// </summary>
        private void Reveal()
        {
            var key = $"{Cell}|{_feet}|{DugCount}|{_trees.Count}";
            if (key == _revealKey) return;
            _revealKey = key;
            foreach (var sr in _faded) if (sr != null) sr.color = Color.white;
            _faded.Clear();
            var me = (Vector2)Top(_p.x, _p.y, _feet - 1) + new Vector2(0f, 0.5f);
            var mine = OrderFor(Cell.x, Cell.y, _feet) + 2;
            void Consider(SpriteRenderer sr, float radius)
            {
                if (sr.sortingOrder <= mine) return;
                if (Vector2.Distance((Vector2)sr.transform.position + new Vector2(0f, -0.3f), me) > radius) return;
                sr.color = new Color(1f, 1f, 1f, 0.35f);
                _faded.Add(sr);
            }
            foreach (var sr in _blocks.Values) Consider(sr, 0.95f);
            foreach (var sr in _treeSprites.Values) Consider(sr, 1.4f);
        }

        // ------------------------------------------------------------------ animation (the supplier's painted clips)

        private string _clip = "";
        private float _time;
        public string Clip => _clip;

        private void Animate(bool moving)
        {
            if (animations == null || body == null) return;
            var back = _heading.y > 0.02f;
            var mirrored = _heading.x > 0f;
            string name;
            if (Working && (_treeTarget.HasValue || _stumpTarget.HasValue)) name = FacingView("chop", out back, out mirrored);
            else if (Working && _target.HasValue) name = _target.Value.z < _feet ? "mine_down_front" : FacingView("mine", out back, out mirrored);
            else if (moving) name = back ? "walk_back" : "walk_front";
            else name = back ? "idle_back" : "idle_front";
            var clip = animations.Get(name) ?? animations.Get("idle_front");
            if (clip == null || clip.frames.Length == 0) return;
            if (clip.name != _clip) { _clip = clip.name; _time = 0f; }
            _time += Time.deltaTime;
            var total = 0f;
            foreach (var d in clip.durations) total += d;
            var t = total > 0f ? Mathf.Repeat(_time, total) : 0f;
            var frame = 0;
            while (frame < clip.frames.Length - 1 && frame < clip.durations.Length && t >= clip.durations[frame]) { t -= clip.durations[frame]; frame++; }
            body.sprite = clip.frames[frame];
            body.flipX = mirrored;
        }

        /// <summary>The view toward what is faced: +x and +y go up the screen (the back view), −x/−y down (the front).</summary>
        private string FacingView(string baseName, out bool back, out bool mirrored)
        {
            var screen = _facing.x * new Vector2(0.5f, 0.25f) + _facing.y * new Vector2(-0.5f, 0.25f);
            back = screen.y > 0f;
            mirrored = screen.x > 0f;
            return baseName + (back ? "_back" : "_front");
        }

        // ------------------------------------------------------------------ controls

        private string StatusText()
        {
            var parts = new List<string>();
            foreach (var kv in _tally) parts.Add($"{UnitVisuals.DisplayName(kv.Key)} {kv.Value}");
            var where = _feet > 1 ? $"{_feet - 1} blocks up" : "on the forest floor";
            return $"<b>{where}</b> · " + (parts.Count == 0 ? "nothing gathered yet" : string.Join(" · ", parts));
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
                "<b>Prototype: forest mountains</b>\nStep up one block at a time, drop down any height; two or more is a wall to go around or dig through. The button chops the tree you face, clears a stump (it won't grow back), or digs; stand still to dig down. Stumps left standing regrow.",
                theme.smallSize - 6f, theme.ink, TextAlignmentOptions.TopLeft);
            UiFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(1040f, 200f));
            _status = ui.Text("Status", card.transform, "", theme.smallSize - 6f, theme.ink, TextAlignmentOptions.BottomLeft);
            UiFactory.Place(_status.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 16f), new Vector2(1040f, 60f));

            var back = ui.Button("Back", safe, "Back to town", BackToTown, out _);
            UiFactory.Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 60f), new Vector2(380f, 130f));

            var work = ui.Panel("Work", safe, theme.roundButton);
            work.type = Image.Type.Simple;
            UiFactory.Place(work.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 80f), new Vector2(260f, 260f));
            work.gameObject.AddComponent<Button>();
            _button = work.gameObject.AddComponent<HoldSensor>();
            _buttonLabel = ui.Text("Label", work.transform, "Dig", theme.bodySize, theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(_buttonLabel.rectTransform);
        }

        public void BackToTown()
        {
            if (_root != null) _root.Hud.gameObject.SetActive(true);
            SceneManager.LoadScene(TownSceneName);
        }
    }
}
