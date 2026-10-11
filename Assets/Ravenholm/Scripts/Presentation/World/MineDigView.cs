using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The Mine dug into the town's own ground (GDD 6: "angled cutaway, visibly stacked geological layers").
    ///
    /// It is built from the town's isometric grid: a 4×4 pit at the exposed front edge, made of ground blocks
    /// (the ground tile with its earthen skirt one level deep, one miner tall). Each dug level is one Tilemap on
    /// the same grid, offset one level down:
    ///   - the working level keeps its pit blocks until the miners clear them, front first, one by one
    ///     (one section = one level, so tile progress follows the section's work);
    ///   - the ring of ground behind the pit continues down as the walls, tinted by the layer each level was dug in;
    ///   - the next levels below show as darker previews; the open front exposes them to the camera.
    /// Only levels near the camera exist at any time, so depth is unlimited. Presentation only.
    /// </summary>
    public sealed class MineDigView : MonoBehaviour
    {
        public const string SiteId = "site.mine";
        public const int Size = 4;              // pit cells per side
        public const float LevelHeight = 1f;    // one level = one miner tall (world units)
        public const int Preview = 3;           // darker levels shown below the working one
        public const int Blocks = Size * Size;

        [SerializeField] private Grid grid;
        [SerializeField] private Vector3Int pitOrigin;   // front cell of the pit (lowest x and y)
        [SerializeField] private TileBase[] blockTiles = new TileBase[0];
        [SerializeField] private Sprite ladderSprite;
        [SerializeField] private Material spriteMaterial;

        public static MineDigView Current { get; private set; }

        private GameRoot _root;
        private Camera _camera;
        private readonly Dictionary<int, Tilemap> _levels = new Dictionary<int, Tilemap>();
        private readonly Dictionary<int, string> _levelKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, SpriteRenderer> _ladders = new Dictionary<int, SpriteRenderer>();
        private readonly List<Vector3Int> _order = new List<Vector3Int>();
        private TextMeshPro _workingLabel, _nextLabel;
        private string _labelKey = "";

        public void EditorSetup(Grid g, Vector3Int origin, TileBase[] tiles, Sprite ladder, Material mat)
        {
            grid = g;
            pitOrigin = origin;
            blockTiles = tiles;
            ladderSprite = ladder;
            spriteMaterial = mat;
        }

        // ------------------------------------------------------------------ what the view shows (tests, workers)

        public int WorkingLevel { get; private set; } = 1;
        public int BlocksRemoved { get; private set; }
        public int LevelsBuilt => _levels.Count;
        /// <summary>When true the camera looks at the working level until the player moves (Mine panel "Look down").</summary>
        public bool ViewingDig { get; set; }

        /// <summary>Pit cells in dig order: front first, so the cut is visible from the camera.</summary>
        public IReadOnlyList<Vector3Int> DigOrder => _order;

        public bool IsPitCell(Vector3Int c) => c.x >= pitOrigin.x && c.x < pitOrigin.x + Size && c.y >= pitOrigin.y && c.y < pitOrigin.y + Size;

        /// <summary>The ring of ground behind the pit that forms its walls.</summary>
        public bool IsWallCell(Vector3Int c) =>
            (c.x == pitOrigin.x + Size && c.y >= pitOrigin.y && c.y <= pitOrigin.y + Size) ||
            (c.y == pitOrigin.y + Size && c.x >= pitOrigin.x && c.x <= pitOrigin.x + Size);

        private Vector3 CellTop(Vector3Int c, int level) => grid.GetCellCenterWorld(c) + new Vector3(0f, -(level - 1) * LevelHeight, 0f);

        /// <summary>Top surface of the pit floor where the miners stand now.</summary>
        public float FloorLevel => BlocksRemoved >= Blocks ? WorkingLevel + 1 : WorkingLevel;

        /// <summary>Where a miner stands: on the next block to clear (or on the cleared floor while a choice waits).</summary>
        public Vector3 StandSpot(int index)
        {
            if (_order.Count == 0) BuildOrder();
            if (BlocksRemoved >= Blocks)
                return CellTop(new Vector3Int(pitOrigin.x + 1 + (index % 2), pitOrigin.y + 2, 0), WorkingLevel + 1);
            var i = Mathf.Min(BlocksRemoved + index, Blocks - 1);
            return CellTop(_order[i], WorkingLevel);
        }

        private Vector3 LadderAt(float level)
        {
            var wall = new Vector3Int(pitOrigin.x + 1, pitOrigin.y + Size, 0);
            return grid.GetCellCenterWorld(wall) + new Vector3(0.25f, -0.125f, 0f) + new Vector3(0f, -(level - 1) * LevelHeight, 0f) + new Vector3(0.08f, -0.06f, 0f);
        }

        public Vector3 LadderTop => LadderAt(1);
        public Vector3 LadderBottom => LadderAt(FloorLevel);

        /// <summary>Middle of the pit at the working floor (camera focus).</summary>
        public Vector3 DigFocus => grid.GetCellCenterWorld(new Vector3Int(pitOrigin.x + 2, pitOrigin.y + 2, 0)) + new Vector3(0f, -(FloorLevel - 1) * LevelHeight - 0.5f, 0f);

        public Vector3 PitTopCentre => grid.GetCellCenterWorld(new Vector3Int(pitOrigin.x + 2, pitOrigin.y + 2, 0));

        // ------------------------------------------------------------------ lifecycle

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null || grid == null || blockTiles.Length == 0) { enabled = false; return; }
            BuildOrder();
            _workingLabel = Label("WorkingLabel");
            _nextLabel = Label("NextLabel");
        }

        private void BuildOrder()
        {
            _order.Clear();
            for (var x = 0; x < Size; x++)
                for (var y = 0; y < Size; y++)
                    _order.Add(new Vector3Int(pitOrigin.x + x, pitOrigin.y + y, 0));
            _order.Sort((a, b) =>
            {
                var sa = a.x + a.y;
                var sb = b.x + b.y;
                return sa != sb ? sa.CompareTo(sb) : a.x.CompareTo(b.x);
            });
        }

        private TextMeshPro Label(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = 2.2f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Right;
            t.color = new Color32(250, 242, 226, 255);
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(59, 42, 32, 255);
            t.rectTransform.sizeDelta = new Vector2(4.5f, 0.5f);
            t.rectTransform.pivot = new Vector2(1f, 0.5f);   // right-aligned against the pit's left side
            t.sortingLayerID = SortingLayer.NameToID("Overlay");
            return t;
        }

        private void Update()
        {
            var sim = _root.Services.Sites.Get(SiteId);
            if (sim == null) return;
            if (_camera == null) _camera = Camera.main;
            var s = sim.Record;
            WorkingLevel = s.SectionsCompleted + 1;
            var awaiting = s.Status == SiteStatus.AwaitDecision;
            var progress = s.SectionDurabilityMicro > 0 ? (double)s.SectionDoneMicro / s.SectionDurabilityMicro : 0;
            BlocksRemoved = awaiting ? Blocks : Mathf.Clamp((int)(progress * Blocks), 0, Blocks);
            if (ViewingDig && _root.InputRouter != null && _root.InputRouter.MoveVector.sqrMagnitude > 0.01f) ViewingDig = false;

            // Levels from the town surface (1) to the previews, limited to what the camera can see.
            int first = 1, last = WorkingLevel + Preview;
            if (_camera != null)
            {
                var top = _camera.transform.position.y + _camera.orthographicSize + 2f;
                var bottom = _camera.transform.position.y - _camera.orthographicSize - 2f;
                var surface = grid.GetCellCenterWorld(pitOrigin).y;
                first = Mathf.Max(1, Mathf.FloorToInt((surface - top) / LevelHeight));
                last = Mathf.Min(last, Mathf.CeilToInt((surface - bottom) / LevelHeight) + 2);
            }
            foreach (var level in new List<int>(_levels.Keys))
                if (level < first || level > last) Release(level);
            for (var level = first; level <= last; level++) EnsureLevel(level, sim);
            UpdateLabels(sim);
            KeepOnScreen(_workingLabel);
            KeepOnScreen(_nextLabel);
        }

        /// <summary>Right-aligned labels slide right just enough to stay fully on screen on narrow views.</summary>
        private void KeepOnScreen(TextMeshPro label)
        {
            if (_camera == null || label == null) return;
            var leftEdge = _camera.transform.position.x - _camera.orthographicSize * _camera.aspect + 0.15f;
            var p = label.transform.position;
            var wanted = _labelAnchorX.TryGetValue(label, out var x) ? x : p.x;
            p.x = Mathf.Max(wanted, leftEdge + label.preferredWidth);
            label.transform.position = p;
        }

        private readonly Dictionary<TextMeshPro, float> _labelAnchorX = new Dictionary<TextMeshPro, float>();

        // ------------------------------------------------------------------ levels

        private void Release(int level)
        {
            Destroy(_levels[level].gameObject);
            _levels.Remove(level);
            _levelKeys.Remove(level);
            if (_ladders.TryGetValue(level, out var l)) { Destroy(l.gameObject); _ladders.Remove(level); }
        }

        private void EnsureLevel(int level, SiteSimulator sim)
        {
            var s = sim.Record;
            var removed = level < WorkingLevel ? Blocks : level == WorkingLevel ? BlocksRemoved : 0;
            var depth = DepthOfLevel(s, level);
            var key = $"{removed}|{depth}|{level - WorkingLevel}";
            if (_levelKeys.TryGetValue(level, out var k) && k == key) return;
            _levelKeys[level] = key;

            if (!_levels.TryGetValue(level, out var tm))
            {
                var go = new GameObject($"MineLevel{level}");
                go.transform.SetParent(grid.transform, false);
                go.transform.localPosition = new Vector3(0f, -(level - 1) * LevelHeight, 0f);
                tm = go.AddComponent<Tilemap>();
                var tr = go.AddComponent<TilemapRenderer>();
                tr.mode = TilemapRenderer.Mode.Individual;
                tr.sortingLayerName = "Ground";
                tr.sortingOrder = -2 * level;   // deeper levels behind; the town surface (order 0) above all
                if (spriteMaterial != null) tr.sharedMaterial = spriteMaterial;
                _levels[level] = tm;
            }
            tm.ClearAllTiles();
            // Walls: the ground behind the pit continues down. Level 1's tops are the town tiles themselves.
            for (var i = 0; i <= Size; i++)
            {
                SetBlock(tm, new Vector3Int(pitOrigin.x + Size, pitOrigin.y + i, 0));
                if (i < Size) SetBlock(tm, new Vector3Int(pitOrigin.x + i, pitOrigin.y + Size, 0));
            }
            // The pit itself: blocks not yet dug at this level.
            for (var i = removed; i < Blocks; i++) SetBlock(tm, _order[i]);
            tm.color = Tint(depth, level - WorkingLevel);
            UpdateLadder(level);
        }

        private void SetBlock(Tilemap tm, Vector3Int cell)
        {
            var h = (cell.x * 73856093) ^ (cell.y * 19349663);
            tm.SetTile(cell, blockTiles[Mathf.Abs(h) % blockTiles.Length]);
        }

        private void UpdateLadder(int level)
        {
            var needed = ladderSprite != null && level <= FloorLevel - 1;
            _ladders.TryGetValue(level, out var sr);
            if (!needed)
            {
                if (sr != null) { Destroy(sr.gameObject); _ladders.Remove(level); }
                return;
            }
            if (sr == null)
            {
                var go = new GameObject($"Ladder{level}");
                go.transform.SetParent(transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ladderSprite;
                sr.sortingLayerName = "Ground";
                sr.sortingOrder = -2 * level + 1;
                if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
                _ladders[level] = sr;
            }
            sr.transform.position = LadderAt(level + 1) + new Vector3(0f, 0.02f, 0f);   // pivot at the segment's foot
        }

        /// <summary>Which depth band a level was (or will be) dug in.</summary>
        public static int DepthOfLevel(SiteRecord s, int level)
        {
            if (level <= s.SectionDepths.Count) return s.SectionDepths[level - 1];
            return s.Depth;   // the working level, and the preview below (the layer continues unless you dig deeper)
        }

        private static readonly Color[] Bands =
        {
            Color.white, new Color(0.96f, 0.86f, 0.78f), new Color(0.86f, 0.84f, 0.88f),
            new Color(0.98f, 0.92f, 0.80f), new Color(0.84f, 0.80f, 0.78f), new Color(0.92f, 0.86f, 0.90f),
        };

        /// <summary>The layer's earth tint (its ore mix over a base that alternates by depth), darker for previews.</summary>
        public Color Tint(int depth, int belowWorking)
        {
            var sim = _root != null ? _root.Services.Sites.Get(SiteId) : null;
            var col = Bands[(Mathf.Max(1, depth) - 1) % Bands.Length];
            if (sim != null && sim.Record.Revealed.TryGetValue(depth, out var comp) && comp != null)
            {
                var ore = Color.black;
                foreach (var w in comp.Weights)
                    ore += (_root.Services.Catalogue.TryGet<ResourceDefinition>(w.Key, out var r) ? r.MapTint : Color.gray) * (float)w.Value;
                col = Color.Lerp(col, Color.Lerp(Color.white, ore, 0.6f), 0.5f);
            }
            if (belowWorking > 0) col = Color.Lerp(col, new Color(0.35f, 0.3f, 0.32f), Mathf.Clamp01(0.35f + 0.18f * belowWorking));
            col.a = 1f;
            return col;
        }

        private void UpdateLabels(SiteSimulator sim)
        {
            var s = sim.Record;
            var key = $"{WorkingLevel}|{s.Depth}|{s.K}|{sim.CanGoDeeper}|{s.Status}";
            if (key == _labelKey) return;
            _labelKey = key;
            // Labels sit in the open background left of the pit (the screen's free side; the right side holds the buttons).
            var leftCorner = grid.GetCellCenterWorld(new Vector3Int(pitOrigin.x, pitOrigin.y + Size - 1, 0)) + new Vector3(-0.5f, 0f, 0f);
            Vector3 FaceMid(int level) => leftCorner + new Vector3(-0.15f, -(level - 1) * LevelHeight - LevelHeight * 0.5f, 0f);
            _workingLabel.transform.position = FaceMid(WorkingLevel);
            _labelAnchorX[_workingLabel] = FaceMid(WorkingLevel).x;
            _labelAnchorX[_nextLabel] = FaceMid(WorkingLevel + 1).x;
            _workingLabel.text = $"Depth {s.Depth} · level {WorkingLevel}" + (s.Status == SiteStatus.AwaitDecision ? " · cleared" : "");
            _nextLabel.transform.position = FaceMid(WorkingLevel + 1);
            _nextLabel.text = sim.CanGoDeeper ? $"below: depth {s.Depth} or {s.Depth + 1}" : "below: this layer continues";
            _nextLabel.fontSize = 1.6f;
        }
    }
}
