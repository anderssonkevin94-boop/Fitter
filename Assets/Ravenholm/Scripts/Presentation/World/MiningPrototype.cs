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
    /// PROTOTYPE (option A, 2026-10-12): Minecraft-style digging in the cliff face, on the same isometric grid and camera.
    /// Blocks are half a level tall, so the supplier is two blocks tall and a tunnel is 2 high × 1 deep: the face row is
    /// dug, the row behind is its back wall. Dig any block next to you (left, right, up, down, diagonally down), walk the
    /// tunnels you make, step up single blocks and drop into holes. Ore uses the real veins (world seed, the Mine's layer
    /// mixes) and shows as flecks within the lamp's light. Self-contained: nothing here touches the company save.
    /// </summary>
    public sealed class MiningPrototype : MonoBehaviour
    {
        public const string SceneName = "MinePrototype";
        public const string TownSceneName = "DebugSandbox";
        public const int Width = 48;            // blocks along the face
        public const int Depth = 36;            // block rows below the surface
        public const int RowsPerLayer = 6;      // 3 levels × 2 blocks
        public const float RowHeight = 0.5f;    // half a level: the supplier is two blocks tall
        private const int Margin = 8;

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
        private readonly HashSet<Vector2Int> _dug = new HashSet<Vector2Int>();
        private readonly Dictionary<int, Tilemap> _rows = new Dictionary<int, Tilemap>();
        private readonly Dictionary<int, Tilemap> _fleckRows = new Dictionary<int, Tilemap>();
        private readonly Dictionary<Vector2Int, string> _ore = new Dictionary<Vector2Int, string>();
        private readonly SortedDictionary<string, int> _tally = new SortedDictionary<string, int>();
        private Tilemap _surface;
        private List<BandTemplate> _bands;
        private ulong _seed;
        private float _u = Width * 0.5f;        // position along the face (cells)
        private int _feet;                      // the block row the supplier stands on (its top is the floor)
        private float _drawFeet;
        private int _facing = 1;
        private float _fallTimer;
        private Vector2Int? _target;
        private float _progress;
        private SpriteRenderer _highlight, _crack;
        private HoldSensor _digButton;
        private TextMeshProUGUI _tallyText;
        private float _bob;

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
        public int Feet => _feet;
        public Vector2Int? Target => _target;
        public int DugCount => _dug.Count;
        public bool ScriptedDig { get; set; }
        public IReadOnlyDictionary<string, int> Tally => _tally;

        // ------------------------------------------------------------------ the grid

        /// <summary>Solid ground at (u, row). Above the surface is air; the sides and the bottom are bedrock.</summary>
        public bool Solid(int u, int row)
        {
            if (row < 0) return false;
            if (u < 0 || u >= Width || row >= Depth) return true;
            return !_dug.Contains(new Vector2Int(u, row));
        }

        private Vector3Int Cell(int x, int u) => new Vector3Int(x, u, 0);

        /// <summary>World point on the face row at (u, row): the top of that block.</summary>
        public Vector3 Top(float u, float row) => new Vector3(-u * 0.5f, u * 0.25f + 0.25f - row * RowHeight, 0f);

        private int Layer(int row) => Mathf.Clamp(row / RowsPerLayer + 1, 1, _bands.Count);

        /// <summary>What a block holds (null = plain rock): the real veins, from the world seed and the layer's mix.</summary>
        public string OreAt(int u, int row)
        {
            var key = new Vector2Int(u, row);
            if (_ore.TryGetValue(key, out var ore)) return ore;
            var band = _bands[Layer(row) - 1];
            var comp = SiteMath.Generate(_seed, MineDigView.SiteId, band);
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
            var mine = _root != null ? _root.Services.Sites.Get(MineDigView.SiteId) : null;
            _bands = mine != null ? mine.Config.Bands : new List<BandTemplate> { new BandTemplate { Index = 1 } };
            _seed = _root != null ? _root.Services.Scheduler.Coordinator.WorldSeed : 1UL;
            if (_root != null) _root.Hud.gameObject.SetActive(false);   // the prototype has its own controls

            _surface = Map("Surface", 0f, 1);
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

        private Tilemap Map(string name, float y, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(grid.transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var tm = go.AddComponent<Tilemap>();
            var tr = go.AddComponent<TilemapRenderer>();
            tr.mode = TilemapRenderer.Mode.Individual;
            tr.sortingLayerName = "Ground";
            tr.sortingOrder = order;
            if (spriteMaterial != null) tr.sharedMaterial = spriteMaterial;
            return tm;
        }

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

        private static readonly Color[] Bands =
        {
            new Color(0.86f, 0.71f, 0.50f), new Color(0.76f, 0.48f, 0.37f), new Color(0.53f, 0.56f, 0.65f),
            new Color(0.58f, 0.67f, 0.47f), new Color(0.62f, 0.49f, 0.60f), new Color(0.82f, 0.63f, 0.31f),
        };

        private static readonly Color Inside = new Color(0.5f, 0.48f, 0.5f);

        /// <summary>One row of the face: solid face blocks, and the back wall wherever the face is dug.</summary>
        private void DrawRow(int row)
        {
            if (row < 0 || row >= Depth) return;
            if (!_rows.TryGetValue(row, out var tm)) _rows[row] = tm = Map($"Row{row}", -row * RowHeight, -2 * row);
            tm.ClearAllTiles();
            tm.color = Bands[(Layer(row) - 1) % Bands.Length];
            for (var u = -Margin; u < Width + Margin; u++)
            {
                var front = Cell(0, u);
                if (Solid(u, row)) { Set(tm, front, u, row, Color.white); continue; }
                // Dug: the back wall shows, in shadow, so the tunnel reads at a glance.
                Set(tm, Cell(1, u), u, row, Inside);
            }
        }

        private void Set(Tilemap tm, Vector3Int cell, int u, int row, Color shade)
        {
            var h = (u * 73856093) ^ (row * 19349663) ^ (cell.x * 83492791);
            tm.SetTile(cell, blockTiles[Mathf.Abs(h) % blockTiles.Length]);
            tm.SetTileFlags(cell, TileFlags.None);
            tm.SetColor(cell, shade);
        }

        private void DrawSurface()
        {
            _surface.ClearAllTiles();
            for (var u = -Margin; u < Width + Margin; u++)
            for (var x = 0; x < 7; x++)
            {
                if (x == 0 && !Solid(u, 0)) continue;
                _surface.SetTile(Cell(x, u), surfaceTile);
            }
        }

        // ------------------------------------------------------------------ the supplier

        private void Update()
        {
            if (_root == null) return;
            var move = _root.InputRouter.MoveVector;
            Walk(move);
            Fall();
            Aim(move);
            Dig();
            PlacePlayer();
            Flecks();
            TrackCamera();
            _tallyText.text = Time.time < _hintUntil ? _hint : TallyText();
        }

        private int Column => Mathf.RoundToInt(_u);

        /// <summary>Left/right walks along the face (left on screen = further along); single blocks are stepped up.</summary>
        private void Walk(Vector2 move)
        {
            if (Mathf.Abs(move.x) < 0.2f) return;
            var dir = move.x < 0 ? 1 : -1;   // the face runs up-left: screen-left is +u
            _facing = dir;
            var next = Mathf.Clamp(_u + dir * 3.2f * Mathf.Abs(move.x) * Time.deltaTime, -Margin + 1, Width + Margin - 2);
            var col = Mathf.RoundToInt(next + dir * 0.3f);
            if (col == Column) { _u = next; return; }
            if (!Solid(col, _feet - 1) && !Solid(col, _feet - 2)) { _u = next; return; }
            // A single block in the way, with headroom above it and above the supplier: step up onto it.
            if (Solid(col, _feet - 1) && !Solid(col, _feet - 2) && !Solid(col, _feet - 3) && !Solid(Column, _feet - 3))
            {
                _feet -= 1;
                _u = next;
                return;
            }
            _u = Mathf.Clamp(_u, Column - 0.3f, Column + 0.3f);
        }

        /// <summary>No ground underfoot: drop a block at a time.</summary>
        private void Fall()
        {
            if (Solid(Column, _feet)) { _fallTimer = 0f; return; }
            _fallTimer += Time.deltaTime;
            if (_fallTimer < 0.07f) return;
            _fallTimer = 0f;
            _feet += 1;
        }

        /// <summary>
        /// The block to dig, from the stick: sideways at body height (lower block first), diagonally down when leaning
        /// down, above the head when pushing up, under the feet when pushing down.
        /// </summary>
        private void Aim(Vector2 move)
        {
            // A tapped block stays selected while it is in reach and still there.
            if (_selected.HasValue && (!Solid(_selected.Value.x, _selected.Value.y) || !InReach(_selected.Value))) _selected = null;
            if (_selected.HasValue)
            {
                SetTarget(_selected);
                return;
            }
            var col = Column;
            Vector2Int? t = null;
            if (move.y < -0.5f && Mathf.Abs(move.x) < 0.5f) t = new Vector2Int(col, _feet);
            else if (move.y > 0.5f && Mathf.Abs(move.x) < 0.5f) t = new Vector2Int(col, _feet - 3);
            else
            {
                var side = col + _facing;
                if (move.y < -0.3f) t = new Vector2Int(side, _feet);
                else if (move.y > 0.3f) t = new Vector2Int(side, _feet - 2);
                else if (Solid(side, _feet - 1)) t = new Vector2Int(side, _feet - 1);
                else if (Solid(side, _feet - 2)) t = new Vector2Int(side, _feet - 2);
                else t = new Vector2Int(side, _feet);
            }
            if (t.HasValue && !Diggable(t.Value)) t = null;
            SetTarget(t);
        }

        private bool Diggable(Vector2Int b) => b.y >= 0 && b.x >= 0 && b.x < Width && b.y < Depth && Solid(b.x, b.y);

        private void SetTarget(Vector2Int? t)
        {
            if (t != _target) _progress = 0f;
            _target = t;
            _highlight.enabled = t.HasValue;
            if (!t.HasValue) return;
            _highlight.transform.position = Top(t.Value.x, t.Value.y);
            _highlight.sortingOrder = -2 * t.Value.y + 1;
            _highlight.color = _selected.HasValue ? new Color(1f, 0.86f, 0.3f, 0.7f) : new Color(1f, 0.92f, 0.45f, 0.4f);
        }

        // ------------------------------------------------------------------ tap to select

        private Vector2Int? _selected;
        private string _hint = "";
        private float _hintUntil;

        public Vector2Int? Selected => _selected;

        /// <summary>Within reach: up to two blocks to either side, from one under the feet to one above the head.</summary>
        public bool InReach(Vector2Int b) => Mathf.Abs(b.x - Column) <= 2 && b.y >= _feet - 3 && b.y <= _feet + 1;

        /// <summary>A tap on the screen selects the block drawn there (the frontmost solid one), if it is in reach.</summary>
        public void OnTapped(Vector2 screen)
        {
            var world = (Vector2)worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            if (!BlockAt(world, out var b)) return;
            if (!InReach(b))
            {
                _hint = "Too far: walk closer to dig that block.";
                _hintUntil = Time.time + 2f;
                return;
            }
            _selected = _selected == b ? (Vector2Int?)null : b;   // tapping it again deselects
            _progress = 0f;
        }

        /// <summary>The frontmost solid face block whose sprite covers this world point.</summary>
        public bool BlockAt(Vector2 world, out Vector2Int block)
        {
            block = default;
            var found = false;
            for (var row = Mathf.Max(0, _feet - 8); row <= Mathf.Min(Depth - 1, _feet + 6); row++)
            for (var u = Mathf.Max(0, Column - 6); u <= Mathf.Min(Width - 1, Column + 6); u++)
            {
                if (!Solid(u, row) || !InSilhouette(world, Top(u, row))) continue;
                // Upper rows and nearer columns are drawn over the others.
                if (!found || row < block.y || row == block.y && u < block.x) block = new Vector2Int(u, row);
                found = true;
            }
            return found;
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

        private float Hardness(Vector2Int b) => 0.55f + 0.02f * b.y + (OreAt(b.x, b.y) != null ? 0.25f : 0f);

        private void Dig()
        {
            var holding = (_digButton != null && _digButton.Held) || ScriptedDig;
            _crack.enabled = false;
            if (!holding || !_target.HasValue) return;
            var b = _target.Value;
            _progress += Time.deltaTime / Hardness(b);
            _bob += Time.deltaTime * 14f;
            if (crackSprites.Length > 0 && _progress > 0.05f)
            {
                _crack.enabled = true;
                _crack.sprite = crackSprites[Mathf.Clamp((int)(_progress * crackSprites.Length), 0, crackSprites.Length - 1)];
                _crack.transform.position = Top(b.x, b.y);
                _crack.sortingOrder = -2 * b.y + 1;
            }
            if (_progress < 1f) return;
            _progress = 0f;
            _dug.Add(b);
            if (_selected == b) _selected = null;
            var ore = OreAt(b.x, b.y) ?? _bands[Layer(b.y) - 1].BaselineResourceId ?? "rock";
            _tally[ore] = _tally.TryGetValue(ore, out var n) ? n + 1 : 1;
            for (var r = b.y - 1; r <= b.y + 1; r++) DrawRow(r);
            if (b.y == 0) DrawSurface();
            _fleckKey = "";
            FeedbackAudio.Play(FeedbackAudio.Cue.Craft);
        }

        private void PlacePlayer()
        {
            _drawFeet = Mathf.MoveTowards(_drawFeet, _feet, Time.deltaTime * 10f);
            player.position = Top(_u, _drawFeet);
            if (playerVisual == null) return;
            var swing = (_digButton != null && _digButton.Held) || ScriptedDig;
            var sc = playerVisual.localScale;
            sc.x = Mathf.Abs(sc.x) * (_facing > 0 ? -1f : 1f);
            playerVisual.localScale = sc;
            playerVisual.localRotation = Quaternion.Euler(0f, 0f, swing && !Feel.ReducedMotion ? Mathf.Sin(_bob) * 12f : 0f);
        }

        // ------------------------------------------------------------------ flecks within the lamp's light

        private string _fleckKey = "";

        private void Flecks()
        {
            var key = $"{Column}|{_feet}";
            if (key == _fleckKey) return;
            _fleckKey = key;
            foreach (var tm in _fleckRows.Values) tm.ClearAllTiles();
            var me = (Vector2)Top(_u, _feet - 1);
            for (var row = Mathf.Max(0, _feet - 7); row <= Mathf.Min(Depth - 1, _feet + 5); row++)
            for (var u = Column - 7; u <= Column + 7; u++)
            {
                if (u < 0 || u >= Width || !Solid(u, row)) continue;
                if (Vector2.Distance(Top(u, row) + new Vector3(0f, -RowHeight * 0.5f, 0f), me) > MineProspectView.LampRadius) continue;
                var ore = OreAt(u, row);
                var strong = ore != null;
                if (!strong)
                    foreach (var (du, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if ((ore = OreAt(u + du, row + dr)) != null) break;
                if (ore == null) continue;
                if (!_fleckRows.TryGetValue(row, out var tm)) _fleckRows[row] = tm = Map($"Flecks{row}", -row * RowHeight, -2 * row + 1);
                var c = Cell(0, u);
                tm.SetTile(c, fleckTile);
                tm.SetTileFlags(c, TileFlags.None);
                var col = _root.Services.Catalogue.TryGet<ResourceDefinition>(ore, out var res) ? res.MapTint : Color.white;
                col.a = strong ? 1f : 0.4f;
                tm.SetColor(c, col);
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
                "<b>Mining prototype</b> (option A)\nDrag left/right to walk. Tap a block near you to select it (or lean the stick to aim), then hold Dig. Dig a 2-high tunnel, step up, drop down, chase the flecks.",
                theme.smallSize - 6f, theme.ink, TextAlignmentOptions.TopLeft);
            UiFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(1040f, 190f));
            _tallyText = ui.Text("Tally", card.transform, "", theme.smallSize - 4f, theme.ink, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            UiFactory.Place(_tallyText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 18f), new Vector2(1040f, 70f));

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
