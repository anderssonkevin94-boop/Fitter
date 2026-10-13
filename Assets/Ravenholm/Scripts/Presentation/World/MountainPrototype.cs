using System.Collections.Generic;
using Ravenholm.Content;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// PROTOTYPE ("forest mountains", 2026-10-13): a small plot of forest with height, on the same isometric grid.
    ///
    /// The ground is blocks (half a level tall, like the Mine's). A ridge of rock runs across the plot, up to seven blocks
    /// above the forest floor; past it is a pocket of forest reached only over or through it. The supplier steps up one
    /// block at a time and drops down any height; two or more is a wall to walk around, climb by the gentler pass, or dig
    /// through. The button digs the wall faced (head height, then legs), or, standing still, the block underfoot; facing a
    /// tree, it chops it. Ore shows in the rock. Self-contained: nothing here touches the company save.
    /// </summary>
    public sealed class MountainPrototype : MonoBehaviour
    {
        public const string SceneName = "MountainPrototype";
        public const string TownSceneName = "DebugSandbox";
        public const int Size = 20;          // cells each way
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

        /// <summary>The ridge's centre line (x) and the pass rows (a gentler climb, one block a step).</summary>
        public const int RidgeX = 10;
        public static bool PassRow(int y) => y == 4 || y == 5;

        private static float Hash(int x, int y, int salt)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private void Generate()
        {
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                var d = Mathf.Abs(x - RidgeX);
                int top;
                if (PassRow(y)) top = 1 + Mathf.Max(0, 4 - d);   // the pass: one block per step
                else
                {
                    // The ridge: steep (two blocks a step), seven blocks above the floor at its crest, a little uneven.
                    var h = d >= 4 ? 0 : 2 * (3 - d) + 1 + (Hash(x, y, 1) < 0.3f ? 1 : 0);
                    top = 1 + Mathf.Clamp(h, 0, MaxHeight - 2);
                }
                for (var z = 0; z < top; z++)
                {
                    _solid[x, y, z] = true;
                    if (z >= 1 && z < top - 1 && Hash(x * 7 + z, y, 2) < 0.12f) _ore[new Vector3Int(x, y, z)] = (int)(Hash(x, y + z, 3) * oreBlocks.Length) % Mathf.Max(1, oreBlocks.Length);
                }
            }
            // Forest: thick on both sides of the ridge, thinner on its slopes; open ground where the supplier starts.
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                var d = Mathf.Abs(x - RidgeX);
                var start = Mathf.Abs(x - StartCell.x) <= 1 && Mathf.Abs(y - StartCell.y) <= 1;
                if (start || PassRow(y)) continue;
                var chance = d >= 4 ? 0.42f : 0.12f;
                if (Hash(x, y, 4) < chance) _trees[new Vector2Int(x, y)] = x > RidgeX && Hash(x, y, 5) < 0.6f;   // leafy trees beyond the ridge
            }
        }

        public static readonly Vector2Int StartCell = new Vector2Int(3, 9);

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
            if (!Inside(x, y)) return;
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
            if (Inside(a.x, a.y) && HasTree(a.x, a.y) && Mathf.Abs(TopOf(a.x, a.y) - _feet) <= 1) { _treeTarget = a; return; }
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
                DrawTree(c);
                Add(leafy ? "resource.maple_log" : "resource.cedar_log", 3);
                _progress = 0f;
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
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                for (var z = 0; z < MaxHeight; z++) DrawVoxel(x, y, z);
                DrawTree(new Vector2Int(x, y));
            }
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
            Reveal();
            worldCamera.orthographicSize = FollowCamera.OrthoSizeForWidth(9f, worldCamera.aspect);
            worldCamera.transform.position = Vector3.Lerp(worldCamera.transform.position, player.position + new Vector3(0f, 0.4f, -10f), 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (_buttonLabel != null) _buttonLabel.text = _treeTarget.HasValue ? "Chop" : "Dig";
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
            if (Working && _treeTarget.HasValue) name = FacingView("chop", out back, out mirrored);
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
            var where = _p.x > RidgeX + 3 ? "beyond the ridge" : _feet > 1 ? $"{_feet - 1} blocks up" : "on the forest floor";
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
                "<b>Prototype: forest mountains</b>\nStep up one block at a time, drop down any height. Two or more is a wall: find the gentler pass, or dig through. The button digs the wall you face, or chops the tree you face; stand still to dig down.",
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
