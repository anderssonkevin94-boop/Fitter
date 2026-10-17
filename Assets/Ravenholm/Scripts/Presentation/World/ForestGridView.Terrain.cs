using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The Forest's ground has height (forest mountains, 2026-10-13): small hills, and ridges of rock running back from
    /// the cliff (see <see cref="ForestGrove.TerrainHeight"/>). Blocks stand on the frontier's tiles, half a level each,
    /// like the Mine's; the supplier steps up one at a time, drops down any height, and digs through rock (the Mine's
    /// surface rock: <see cref="SiteSimulator.PlayerBreakSurface"/>). Trees stand on top.
    ///
    /// Everything in the Forest sorts on the Ground layer in front of the town's tiles, by cell and level, so blocks,
    /// trees and the supplier overlap correctly. Shadows mark where blocks are: ground just past a taller neighbour (the
    /// light comes from the top-left), and, darker, ground under an overhang.
    /// </summary>
    public sealed partial class ForestGridView
    {
        public const int TerrainBase = 20000;

        [SerializeField] private Sprite grassBlock, soilBlock, rockBlock, deepRockBlock;

        public void EditorSetupTerrain(Sprite grass, Sprite soil, Sprite rock, Sprite deepRock)
        {
            grassBlock = grass;
            soilBlock = soil;
            rockBlock = rock;
            deepRockBlock = deepRock;
        }

        /// <summary>Front to back: higher levels and cells nearer the camera (smaller x + y) draw later.</summary>
        public static int TerrainOrder(Vector3Int c, int level) => TerrainBase + 4 * (level - c.x - c.y);

        private SiteSimulator Mine => _root != null ? _root.Services.Sites.Get("site.mine") : null;
        private readonly Dictionary<Vector3Int, int> _heights = new Dictionary<Vector3Int, int>();

        /// <summary>How high the ground's blocks stand on a tile (0 = flat; tiles outside the Forest are flat).</summary>
        public int HeightAt(Vector3Int c)
        {
            Build();
            if (_heights.TryGetValue(c, out var h)) return h;
            var sim = Sim;
            h = sim != null && sim.GroveMode && _slotOf.TryGetValue(c, out var t) ? sim.TerrainHeight(t.area, t.slot) : 0;
            _heights[c] = h;
            return h;
        }

        /// <summary>A block of the ground at a level (1 = on the floor), unless it has been dug away.</summary>
        public bool SolidAt(Vector3Int c, int level)
        {
            if (level < 1 || level > HeightAt(c)) return false;
            var mine = Mine;
            return mine == null || !mine.IsSurfaceBroken(c.x, c.y, level);
        }

        /// <summary>The highest standing block on a tile (0 = the floor).</summary>
        public int SurfaceTop(Vector3Int c)
        {
            for (var l = HeightAt(c); l >= 1; l--) if (SolidAt(c, l)) return l;
            return 0;
        }

        private Vector3 SurfacePoint(Vector3Int c, int level) => Centre(c) + new Vector3(0f, level * MineSlabView.RowHeight, 0f);

        // ------------------------------------------------------------------ drawing the ground's blocks and shadows

        private readonly Dictionary<Vector3Int, SpriteRenderer> _terrain = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly Dictionary<Vector3Int, SpriteRenderer> _shadows = new Dictionary<Vector3Int, SpriteRenderer>();
        private readonly List<SpriteRenderer> _terrainPool = new List<SpriteRenderer>();
        private Sprite _shadowSprite;

        public int TerrainBlocksDrawn => _terrain.Count;
        public int ShadowsDrawn => _shadows.Count;

        private SpriteRenderer TakeTerrain()
        {
            if (_terrainPool.Count > 0)
            {
                var r = _terrainPool[_terrainPool.Count - 1];
                _terrainPool.RemoveAt(_terrainPool.Count - 1);
                r.gameObject.SetActive(true);
                return r;
            }
            var go = new GameObject("Ground");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Ground";
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }

        private void GiveTerrain(Dictionary<Vector3Int, SpriteRenderer> map, Vector3Int k)
        {
            if (!map.TryGetValue(k, out var sr)) return;
            sr.gameObject.SetActive(false);
            _terrainPool.Add(sr);
            map.Remove(k);
        }

        /// <summary>A flat diamond the size of a tile's top (white; tinted dark it is a shadow on a surface).</summary>
        private static Sprite MakeDiamond()
        {
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var d = Mathf.Abs((x + 0.5f) - w * 0.5f) / (w * 0.5f) + Mathf.Abs((y + 0.5f) - h * 0.5f) / (h * 0.5f);
                var a = d <= 1f ? (byte)Mathf.RoundToInt(255f * Mathf.Clamp01((1f - d) * 6f)) : (byte)0;   // soft edge
                px[y * w + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 128);
        }

        private Sprite BlockSprite(Vector3Int c, int level, int top)
        {
            if (level == top) return grassBlock;                       // open sky above: grass
            if (level == top - 1) return soilBlock;
            // Ore stays hidden in the rock until it is broken, as in the Mine (2026-10-15, Kevin).
            return level >= 5 ? deepRockBlock : rockBlock;
        }

        /// <summary>The ground's blocks and shadows on the tiles in view (called with the trees' redraw).</summary>
        private void RedrawTerrain(HashSet<Vector3Int> cells)
        {
            if (_shadowSprite == null) _shadowSprite = MakeDiamond();
            var keepBlocks = new HashSet<Vector3Int>();
            var keepShadows = new HashSet<Vector3Int>();
            foreach (var c in cells)
            {
                var h = HeightAt(c);
                var top = SurfaceTop(c);
                for (var l = 1; l <= h; l++)
                {
                    if (!SolidAt(c, l)) continue;
                    var visible = !SolidAt(c + Vector3Int.left, l) || !SolidAt(c + Vector3Int.down, l) || !SolidAt(c, l + 1);
                    if (!visible) continue;
                    var k = new Vector3Int(c.x, c.y, l);
                    keepBlocks.Add(k);
                    if (!_terrain.TryGetValue(k, out var sr)) _terrain[k] = sr = TakeTerrain();
                    sr.sprite = BlockSprite(c, l, top);
                    sr.transform.position = SurfacePoint(c, l);
                    sr.sortingOrder = TerrainOrder(c, l);
                    sr.color = Color.white;
                }
                // Shadows on every open surface of this tile: under an overhang (dark), else cast by a taller neighbour.
                for (var s = 0; s <= h; s++)
                {
                    var surface = s == 0 ? !SolidAt(c, 1) : SolidAt(c, s) && !SolidAt(c, s + 1);
                    if (!surface) continue;
                    var alpha = 0f;
                    for (var above = s + 2; above <= h; above++) if (SolidAt(c, above)) { alpha = 0.42f; break; }   // an overhang
                    if (alpha == 0f)
                    {
                        var rise = SurfaceTop(c + Vector3Int.up) - s;               // the neighbour toward the light (up-left)
                        var rise2 = SurfaceTop(c + new Vector3Int(1, 1, 0)) - s;   // and straight up the screen
                        var r = Mathf.Max(rise, rise2 - 1);
                        if (r > 0) alpha = Mathf.Min(0.34f, 0.12f + 0.07f * r);
                    }
                    if (alpha <= 0f) continue;
                    var k = new Vector3Int(c.x, c.y, s);
                    keepShadows.Add(k);
                    if (!_shadows.TryGetValue(k, out var sh)) _shadows[k] = sh = TakeTerrain();
                    sh.sprite = _shadowSprite;
                    sh.transform.position = SurfacePoint(c, s);
                    sh.sortingOrder = TerrainOrder(c, s) + 1;
                    sh.color = new Color(0.08f, 0.06f, 0.1f, alpha);
                }
            }
            foreach (var k in new List<Vector3Int>(_terrain.Keys)) if (!keepBlocks.Contains(k)) GiveTerrain(_terrain, k);
            foreach (var k in new List<Vector3Int>(_shadows.Keys)) if (!keepShadows.Contains(k)) GiveTerrain(_shadows, k);
        }

        // ------------------------------------------------------------------ the supplier on the ground's blocks

        private int _feet;
        private float _drawFeet;
        private float _fallTimer;
        private bool _feetKnown;

        /// <summary>The level the supplier stands at (0 = the floor) and where they are drawn (it glides).</summary>
        public int PlayerFeet => _feet;
        public float PlayerDrawFeet => _drawFeet;

        private Vector3Int CellAt(Vector2 plane) => IsoMap.Cell(plane);

        /// <summary>
        /// Whether the supplier can go from one point to another on the ground's blocks: the body (two blocks) must be
        /// free there, or the step is a single block up with headroom. Drops are always allowed.
        /// </summary>
        public bool CanEnter(Vector2 from, Vector2 to)
        {
            var oc = CellAt(from);
            var nc = CellAt(to);
            if (oc == nc) return true;
            var f = _feet;
            if (!SolidAt(nc, f + 1) && !SolidAt(nc, f + 2)) return true;
            return SolidAt(nc, f + 1) && !SolidAt(nc, f + 2) && !SolidAt(nc, f + 3) && !SolidAt(oc, f + 3);
        }

        /// <summary>Moves onto a point already allowed by <see cref="CanEnter"/>: a step up raises the supplier a block.</summary>
        public void Commit(Vector2 from, Vector2 to)
        {
            var oc = CellAt(from);
            var nc = CellAt(to);
            if (oc != nc && SolidAt(nc, _feet + 1)) _feet += 1;
        }

        /// <summary>Each frame: drops a block at a time where there is nothing underfoot; stands on top after a teleport.</summary>
        private void UpdateSupplier()
        {
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            if (player == null) return;
            var c = CellAt(player.position);
            if (!_feetKnown || SolidAt(c, _feet + 1)) { _feet = SurfaceTop(c); _drawFeet = _feet; _feetKnown = true; }   // inside rock: onto it
            if (_feet > 0 && !SolidAt(c, _feet))
            {
                _fallTimer += Time.deltaTime;
                if (_fallTimer >= 0.07f) { _fallTimer = 0f; _feet -= 1; }
            }
            else _fallTimer = 0f;
            _drawFeet = Mathf.MoveTowards(_drawFeet, _feet, Time.deltaTime * 10f);
        }

        /// <summary>The supplier's draw order while in the Forest (on the Ground layer, among its blocks and trees).</summary>
        public bool PlayerSortOrder(Vector2 plane, out int order)
        {
            var c = CellAt(plane);
            Build();
            order = TerrainOrder(c, _feet + 1) + 2;
            return _slotOf.ContainsKey(c) || _feet > 0;
        }

        /// <summary>The cell the supplier faces from the way they last moved (along the grid's axes).</summary>
        /// <summary>The neighbouring cell faced, then the other one beside that way (the second-nearest grid axis).</summary>
        public static IEnumerable<Vector3Int> FacingCells(Vector2 move)
        {
            var dx = move.x + 2f * move.y;
            var dy = -move.x + 2f * move.y;
            var first = FacingFrom(move);
            yield return first;
            // Only when facing between the axes (more than about 20 degrees off the one faced): facing straight along a
            // grid line, a side cell is not in front (a tree there took the button from the rock faced, 2026-10-17).
            var major = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            var minor = Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy));
            if (minor < major * 0.36f) yield break;
            var second = first.x != 0 ? new Vector3Int(0, dy >= 0 ? 1 : -1, 0) : new Vector3Int(dx >= 0 ? 1 : -1, 0, 0);
            yield return second;
        }

        public static Vector3Int FacingFrom(Vector2 move)
        {
            var dx = move.x + 2f * move.y;
            var dy = -move.x + 2f * move.y;
            return Mathf.Abs(dx) >= Mathf.Abs(dy) ? new Vector3Int(dx > 0 ? 1 : -1, 0, 0) : new Vector3Int(0, dy > 0 ? 1 : -1, 0);
        }

        /// <summary>
        /// The rock the supplier can dig: the wall faced (head height, then legs), or, standing still on a block, the
        /// block underfoot. Never under a tree or a stump.
        /// </summary>
        public bool RockTarget(Vector2 plane, Vector2 facingMove, bool still, out Vector3Int block)
        {
            block = default;
            var c = CellAt(plane);
            // The cell faced, then (facing between the grid's axes, Kevin's playtest 2026-10-17) the other cell beside
            // that way, so the rock right in front is never skipped.
            foreach (var step in FacingCells(facingMove))
            {
                var a = c + step;
                // A tree within reach wins (it is chopped instead); one standing on top of a wall doesn't hide the wall.
                if (HasTreeOrStump(a) && SurfaceTop(a) <= _feet + 1) continue;
                if (SolidAt(a, _feet + 2)) { block = new Vector3Int(a.x, a.y, _feet + 2); return true; }
                if (SolidAt(a, _feet + 1)) { block = new Vector3Int(a.x, a.y, _feet + 1); return true; }
            }
            if (still && _feet >= 1 && SolidAt(c, _feet) && !HasTreeOrStump(c)) { block = new Vector3Int(c.x, c.y, _feet); return true; }
            return false;
        }

        private bool HasTreeOrStump(Vector3Int c)
        {
            if (!_slotOf.TryGetValue(c, out var t)) return false;
            return State(t.area, t.slot, out _) != TreeState.None;
        }

        public Vector3 BlockTop(Vector3Int block) => SurfacePoint(new Vector3Int(block.x, block.y, 0), block.z);

        [SerializeField] private Sprite highlightSprite;
        private SpriteRenderer _highlight;

        public void EditorSetupHighlight(Sprite highlight) => highlightSprite = highlight;

        /// <summary>Whether the rock block about to be dug is highlighted now (for tests).</summary>
        public bool HighlightShown => _highlight != null && _highlight.enabled;

        private SpriteRenderer _treeHighlight;

        /// <summary>Whether a tree (or stump) about to be chopped is highlighted now (for tests).</summary>
        public bool TreeHighlightShown => _treeHighlight != null && _treeHighlight.enabled;

        /// <summary>
        /// The tree (or stump) the main button would chop is highlighted as a block is (2026-10-16, Kevin): a bright line
        /// round its silhouette, outside its outline, pulsing like the block highlight.
        /// </summary>
        private void UpdateTreeHighlight()
        {
            var c = SandboxSceneController.Current;
            var spot = c != null ? c.ContextTarget as ForestChopSpot : null;
            SpriteRenderer tree = null;
            if (spot != null && spot.Available) _shown.TryGetValue(spot.Cell, out tree);
            if (tree == null || !tree.enabled || tree.sprite == null)
            {
                if (_treeHighlight != null) _treeHighlight.enabled = false;
                return;
            }
            if (_treeHighlight == null)
            {
                var outlines = GameRoot.Instance != null ? GameRoot.Instance.Config.SandboxSettings.outlines : null;
                if (outlines == null || outlines.material == null) return;
                var mat = new Material(outlines.material) { name = "TreeHighlight" };
                mat.SetFloat("_HighlightPx", 3f);
                mat.SetColor("_HighlightColor", Color.white);
                var go = new GameObject("TreeHighlight");
                go.transform.SetParent(transform, false);
                _treeHighlight = go.AddComponent<SpriteRenderer>();
                _treeHighlight.sharedMaterial = mat;
            }
            _treeHighlight.enabled = true;
            _treeHighlight.sprite = tree.sprite;
            _treeHighlight.flipX = tree.flipX;
            _treeHighlight.transform.position = tree.transform.position;
            _treeHighlight.transform.rotation = tree.transform.rotation;
            _treeHighlight.transform.localScale = tree.transform.lossyScale;
            _treeHighlight.sortingLayerID = tree.sortingLayerID;
            _treeHighlight.sortingOrder = tree.sortingOrder - 2;   // behind the tree and its own outline
            _treeHighlight.spriteSortPoint = tree.spriteSortPoint;
            _treeHighlight.color = BlockHighlight.Tint(false) * new Color(1f, 1f, 1f, tree.color.a);
        }

        /// <summary>The rock block the main button would dig is highlighted while the rock spot is the button's target.</summary>
        private void UpdateHighlight()
        {
            UpdateTreeHighlight();
            if (highlightSprite == null) return;
            if (_highlight == null)
            {
                _highlight = BlockHighlight.Make(transform, highlightSprite, spriteMaterial);
                _highlight.sortingLayerName = "Ground";
            }
            var c = SandboxSceneController.Current;
            var spot = c != null ? c.ContextTarget as ForestRockSpot : null;
            _highlight.enabled = spot != null && spot.Available;
            if (!_highlight.enabled) return;
            var b = spot.Block;
            _highlight.transform.position = BlockTop(b);
            _highlight.sortingOrder = TerrainOrder(new Vector3Int(b.x, b.y, 0), b.z) + 1;   // with its block: the supplier in front covers it
            _highlight.color = BlockHighlight.Tint(false);
        }
    }
}
