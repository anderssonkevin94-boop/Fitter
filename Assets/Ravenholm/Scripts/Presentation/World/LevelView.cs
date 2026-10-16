using System.Collections.Generic;
using Ravenholm.Domain.Sites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Gated levels (Kevin, 2026-10-16): the scene is built with level 1's ground; arriving at another level (or loading a
    /// save there) lays that level's ground fresh, then the Forest and the hills are rebuilt on it. The camp's town stands in
    /// the same place on every level, so its ground is kept as built. Presentation only: the land comes from the level's
    /// <see cref="RidgeLayout"/>.
    /// </summary>
    public sealed class LevelView : MonoBehaviour
    {
        [SerializeField] private Tilemap ground;
        [SerializeField] private List<string> keys = new List<string>();
        [SerializeField] private List<TileBase> tiles = new List<TileBase>();
        [Tooltip("The camp's town (cells): its ground is built once and kept on every level.")]
        [SerializeField] private RectInt town;

        public static LevelView Current { get; private set; }

        /// <summary>The level whose ground is laid now (the scene is built with level 1's, index 0).</summary>
        public int Painted { get; private set; }

        private GameRoot _root;
        private readonly Dictionary<string, TileBase> _tiles = new Dictionary<string, TileBase>();

        public void EditorSetup(Tilemap groundMap, Dictionary<string, TileBase> byKey, RectInt townCells)
        {
            ground = groundMap;
            keys = new List<string>(byKey.Keys);
            tiles = new List<TileBase>(byKey.Values);
            town = townCells;
        }

        private void Awake()
        {
            Current = this;
            for (var i = 0; i < keys.Count && i < tiles.Count; i++) _tiles[keys[i]] = tiles[i];
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _root.Services.Camp.Moved += (_, __) => Show();
            _root.Services.Camp.Reloaded += Show;
            Show();
        }

        /// <summary>Lays the company's level if it isn't laid already.</summary>
        private void Show()
        {
            if (this == null || ground == null) return;
            var camp = _root.Services.Camp;
            if (!camp.Levels || camp.Index == Painted) return;
            Lay(camp.Ridge);
            Painted = camp.Index;
            if (ForestGridView.Current != null) ForestGridView.Current.Rebuild();
            if (MineSlabView.Current != null) MineSlabView.Current.RefreshGround();
        }

        /// <summary>The level's ground: its land (no tile past its edges or in a project's gap), the town kept as built.</summary>
        private void Lay(RidgeLayout ridge)
        {
            var old = ground.cellBounds;
            var e = ridge.Extent;
            int x0 = Mathf.Min(old.xMin, e.minX), y0 = Mathf.Min(old.yMin, e.minY);
            int x1 = Mathf.Max(old.xMax - 1, e.maxX), y1 = Mathf.Max(old.yMax - 1, e.maxY);
            var cells = new List<Vector3Int>();
            var laid = new List<TileBase>();
            for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
            {
                var c = new Vector3Int(x, y, 0);
                var land = ridge.InLand(x, y) && !ridge.InProjectGap(x, y);
                TileBase tile = null;
                if (land)
                {
                    var inTown = ridge.InClearing(x, y) && x >= town.xMin && x < town.xMax && y >= town.yMin && y < town.yMax;
                    tile = inTown && ground.HasTile(c) ? ground.GetTile(c) : Tile(GroundKeys.Outside(ridge, x, y));
                }
                cells.Add(c);
                laid.Add(tile);
            }
            ground.SetTiles(cells.ToArray(), laid.ToArray());
            foreach (var c in cells)
            {
                if (!ground.HasTile(c)) continue;
                ground.SetTileFlags(c, TileFlags.None);
                ground.SetColor(c, Color.white);
            }
        }

        private TileBase Tile(string key) => _tiles.TryGetValue(key, out var t) ? t : _tiles.TryGetValue("forest_floor", out var f) ? f : null;
    }

    /// <summary>Which ground tile a cell of the land outside the town gets (shared by the scene builder and <see cref="LevelView"/>).</summary>
    public static class GroundKeys
    {
        /// <summary>A clearing's grass, the old road (cobble, then worn earth, then forest floor), else forest floor.</summary>
        public static string Outside(RidgeLayout ridge, int x, int y)
        {
            if (ridge.InClearing(x, y)) return Grass(x, y);
            if (ridge.OnRoad(x, y))
            {
                var r = Hash01(x + 313, y + 17) * 0.5f + SmoothNoise(x + 40, y, 4f) * 0.5f;
                return r < 0.38f ? "plaza" : r < 0.6f ? Path(x, y) : "forest_floor_b";
            }
            return Hash01(x + 911, y) < 0.12f ? "forest_floor_b" : "forest_floor";   // trees and hills: ForestGridView
        }

        /// <summary>Grass, mostly plain, with tufts and the odd flower patch.</summary>
        public static string Grass(int x, int y)
        {
            var patch = SmoothNoise(x, y, 5f);
            var r = Hash01(x, y);
            if (patch > 0.74f && r < 0.4f) return "grass_b";    // a small flower patch
            if (r < 0.14f) return "grass_c";                   // tufts
            if (r > 0.99f) return "grass_b";                   // a stray flower
            return "grass_a";
        }

        /// <summary>Paths mostly plain, with pebbles and the odd set of footprints.</summary>
        public static string Path(int x, int y)
        {
            var r = Hash01(x + 4057, y + 77);
            return r < 0.18f ? "path_b" : r < 0.26f ? "path_c" : "path";
        }

        /// <summary>Value noise in 0..1 on a grid of <paramref name="scale"/> cells, smoothly interpolated.</summary>
        public static float SmoothNoise(int x, int y, float scale)
        {
            float fx = x / scale, fy = y / scale;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = Mathf.SmoothStep(0f, 1f, fx - x0), ty = Mathf.SmoothStep(0f, 1f, fy - y0);
            float a = Hash01(x0 * 31 + 7, y0 * 17 + 3), b = Hash01((x0 + 1) * 31 + 7, y0 * 17 + 3);
            float c = Hash01(x0 * 31 + 7, (y0 + 1) * 17 + 3), d = Hash01((x0 + 1) * 31 + 7, (y0 + 1) * 17 + 3);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        public static float Hash01(int x, int y)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
