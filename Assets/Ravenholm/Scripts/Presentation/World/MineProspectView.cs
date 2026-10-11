using System.Collections.Generic;
using Ravenholm.Content;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Reading the rock (prospecting): the Mine's veins are hidden, but the supplier can read signs.
    ///   - Flecks: within the lamp's light, exposed faces of ore blocks show specks in the ore's colour; faces next to a
    ///     vein show faint specks. Walking the tunnels shows more of what lies around them.
    ///   - The prospector's tap: a short-cooldown tap on the rock; nearby blocks that hold ore ring (coloured echo
    ///     rings), even behind the face, and a short note says what answered.
    ///   - Cracks: the block being dug cracks in three stages.
    /// Presentation only: what each block holds comes from the sites service (world seed and layer mix).
    /// </summary>
    public sealed class MineProspectView : MonoBehaviour
    {
        public const float LampRadius = 2.6f;
        public const int TapReach = 4;
        public const float TapCooldown = 5f;

        [SerializeField] private TileBase fleckTile;
        [SerializeField] private Sprite[] crackSprites = new Sprite[0];
        [SerializeField] private Sprite ringSprite;
        [SerializeField] private Material spriteMaterial;

        public static MineProspectView Current { get; private set; }

        private GameRoot _root;
        private readonly Dictionary<int, Tilemap> _flecks = new Dictionary<int, Tilemap>();
        private readonly Dictionary<Vector3Int, string> _oreCache = new Dictionary<Vector3Int, string>();
        private readonly List<(SpriteRenderer sr, float born)> _rings = new List<(SpriteRenderer, float)>();
        private readonly List<SpriteRenderer> _ringPool = new List<SpriteRenderer>();
        private SpriteRenderer _crack;
        private float _crackSeen = -10f;
        private float _lastTap = -100f;
        private float _nextScan;
        private string _scanKey = "";

        public void EditorSetup(TileBase fleck, Sprite[] cracks, Sprite ring, Material mat)
        {
            fleckTile = fleck;
            crackSprites = cracks;
            ringSprite = ring;
            spriteMaterial = mat;
        }

        /// <summary>Blocks showing ore flecks now (tests): strong for ore blocks, faint beside a vein.</summary>
        public int StrongFlecks { get; private set; }
        public int FaintFlecks { get; private set; }
        public int LastTapRings { get; private set; }
        public string LastTapNote { get; private set; } = "";

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _root.Services.Sites.Reloaded += () => _oreCache.Clear();
        }

        private static PlayerMotor Player =>
            SandboxSceneController.Current != null && SandboxSceneController.Current.Player != null
                ? SandboxSceneController.Current.Player.GetComponent<PlayerMotor>() : null;

        /// <summary>The ore in a block, cached (contents never change; what is dug is shown by the block grid).</summary>
        public string OreAt(Vector3Int c, int level)
        {
            var key = new Vector3Int(c.x, c.y, level);
            if (_oreCache.TryGetValue(key, out var ore)) return ore;
            ore = _root.Services.Sites.OreAt(MineDigView.SiteId, c.x, c.y, level, out _);
            _oreCache[key] = ore;
            return ore;
        }

        private Color OreColour(string ore) =>
            _root.Services.Catalogue.TryGet<ResourceDefinition>(ore, out var r) ? r.MapTint : Color.white;

        private void Update()
        {
            var mine = MineDigView.Current;
            var player = Player;
            if (mine == null || player == null) return;
            UpdateRings();
            if (_crack != null && _crack.enabled && Time.time - _crackSeen > 0.3f) _crack.enabled = false;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 0.2f;
            ScanFlecks(mine, player);
        }

        // ------------------------------------------------------------------ flecks

        private void ScanFlecks(MineDigView mine, PlayerMotor player)
        {
            var p = (Vector2)player.transform.position;
            var level = player.HiddenForRoutine ? -1 : Mathf.Max(0, player.MineLevel);
            var key = $"{Mathf.Round(p.x * 4f)}|{Mathf.Round(p.y * 4f)}|{level}|{mine.WorkingLevel}|{mine.BlocksRemoved}|{mine.DeepestFloor}|{TunnelKey()}";
            if (key == _scanKey) return;
            _scanKey = key;
            foreach (var tm in _flecks.Values) tm.ClearAllTiles();
            StrongFlecks = FaintFlecks = 0;
            if (level < 0) return;
            var grid = mine.GridRef;
            for (var l = Mathf.Max(1, level - 1); l <= level + 2; l++)
            {
                // Blocks of level l whose faces lie near the supplier: their cells sit about (l − 0.5) levels above on the plane.
                var centre = grid.WorldToCell(p + new Vector2(0f, l - 0.5f));
                for (var dx = -5; dx <= 5; dx++)
                for (var dy = -5; dy <= 5; dy++)
                {
                    var c = new Vector3Int(centre.x + dx, centre.y + dy, 0);
                    if (!mine.BlockVisible(c, l)) continue;
                    var face = (Vector2)mine.BlockTop(c, l) + new Vector2(0f, -0.5f);
                    if (Vector2.Distance(face, p) > LampRadius) continue;
                    var ore = OreAt(c, l);
                    var strong = ore != null;
                    if (!strong) ore = NeighbourOre(c, l);
                    if (ore == null) continue;
                    var tm = Flecks(l, mine);
                    tm.SetTile(c, fleckTile);
                    tm.SetTileFlags(c, TileFlags.None);
                    var col = OreColour(ore);
                    col.a = strong ? 1f : 0.4f;
                    tm.SetColor(c, col);
                    if (strong) StrongFlecks++;
                    else FaintFlecks++;
                }
            }
        }

        private string TunnelKey()
        {
            var sim = _root.Services.Sites.Get(MineDigView.SiteId);
            if (sim == null) return "";
            var k = 0L;
            foreach (var t in sim.Record.Tunnels) k = k * 31 + t.Stretches * 7 + (t.DoneMicro > 0 ? 1 : 0);
            return k.ToString();
        }

        private string NeighbourOre(Vector3Int c, int level)
        {
            foreach (var (dx, dy, dl) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                if (level + dl < 1) continue;
                var ore = OreAt(new Vector3Int(c.x + dx, c.y + dy, 0), level + dl);
                if (ore != null) return ore;
            }
            return null;
        }

        private Tilemap Flecks(int level, MineDigView mine)
        {
            if (_flecks.TryGetValue(level, out var tm) && tm != null) return tm;
            var go = new GameObject($"MineFlecks{level}");
            go.transform.SetParent(mine.GridRef.transform, false);
            go.transform.localPosition = new Vector3(0f, -(level - 1) * MineDigView.LevelHeight, 0f);
            tm = go.AddComponent<Tilemap>();
            var tr = go.AddComponent<TilemapRenderer>();
            tr.mode = TilemapRenderer.Mode.Individual;
            tr.sortingLayerName = "Ground";
            tr.sortingOrder = MineDigView.SortingOrderFor(level) + 1;
            if (spriteMaterial != null) tr.sharedMaterial = spriteMaterial;
            _flecks[level] = tm;
            return tm;
        }

        // ------------------------------------------------------------------ cracks

        /// <summary>Shows the block being dug cracking (stage by progress); hides itself once digging stops.</summary>
        public void ShowCrack(Vector3Int cell, int level, float progress)
        {
            var mine = MineDigView.Current;
            if (mine == null || crackSprites.Length == 0) return;
            if (_crack == null)
            {
                var go = new GameObject("BlockCrack");
                go.transform.SetParent(transform, false);
                _crack = go.AddComponent<SpriteRenderer>();
                _crack.sortingLayerName = "Ground";
                if (spriteMaterial != null) _crack.sharedMaterial = spriteMaterial;
            }
            _crackSeen = Time.time;
            _crack.enabled = progress > 0.05f;
            _crack.sprite = crackSprites[Mathf.Clamp((int)(progress * crackSprites.Length), 0, crackSprites.Length - 1)];
            _crack.sortingOrder = MineDigView.SortingOrderFor(level) + 3;
            _crack.transform.position = mine.BlockTop(cell, level);
        }

        public bool CrackShowing => _crack != null && _crack.enabled;

        // ------------------------------------------------------------------ the prospector's tap

        /// <summary>Near the Mine (underground, or at the shaft's rim) and not on cooldown.</summary>
        public bool CanTap
        {
            get
            {
                var mine = MineDigView.Current;
                var player = Player;
                if (mine == null || player == null || player.HiddenForRoutine || player.OnLadder) return false;
                var nearShaft = Vector2.Distance(player.transform.position, mine.PitTopCentre) < 3.5f;
                return player.Underground || nearShaft;
            }
        }

        public float TapCooldownLeft => Mathf.Max(0f, _lastTap + TapCooldown - Time.time);

        /// <summary>
        /// Taps the rock: every block within reach (this level and the ones above and below) that holds ore rings, even
        /// behind the face. Returns how many answered.
        /// </summary>
        public int Tap()
        {
            var mine = MineDigView.Current;
            var player = Player;
            if (!CanTap || TapCooldownLeft > 0f) return 0;
            _lastTap = Time.time;
            var p = (Vector2)player.transform.position;
            var level = Mathf.Max(1, player.MineLevel);
            var counts = new SortedDictionary<string, int>();
            var rings = 0;
            for (var l = Mathf.Max(1, level - 1); l <= level + 1; l++)
            {
                var centre = mine.GridRef.WorldToCell(p + new Vector2(0f, l - 0.5f));
                for (var dx = -TapReach; dx <= TapReach; dx++)
                for (var dy = -TapReach; dy <= TapReach; dy++)
                {
                    var c = new Vector3Int(centre.x + dx, centre.y + dy, 0);
                    if (!mine.InPlot(c) || mine.IsPitCell(c) && l <= mine.DeepestFloor) continue;
                    var ore = OreAt(c, l);
                    if (ore == null) continue;
                    counts[ore] = counts.TryGetValue(ore, out var n) ? n + 1 : 1;
                    Ring(mine.BlockTop(c, l) + new Vector3(0f, -0.5f, 0f), OreColour(ore), rings * 0.015f);
                    rings++;
                }
            }
            LastTapRings = rings;
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{UnitVisuals.DisplayName(kv.Key).ToLowerInvariant()} ×{kv.Value}");
            LastTapNote = rings == 0 ? "Tap… dull rock all round." : "Tap… it rings: " + string.Join(", ", parts) + " nearby.";
            _root.Hud.ShowToast(LastTapNote);
            FeedbackAudio.Play(FeedbackAudio.Cue.Discovery);
            return rings;
        }

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
                var t = Mathf.Clamp01(age / 2.5f);
                var c = sr.color;
                c.a = age < 0f ? 0f : 1f - t;
                sr.color = c;
                var scale = Feel.ReducedMotion ? 1f : 0.6f + 0.8f * Mathf.Sin(Mathf.Clamp01(age * 2f) * Mathf.PI * 0.5f);
                sr.transform.localScale = Vector3.one * scale;
            }
        }
    }
}
