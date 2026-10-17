using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The camp's stations in build mode (the journey, step 2): tap a station to pick it, then tap cleared, level ground
    /// to set it up there (its footprint is outlined: green where it can stand, red where it can't); or pick a packed
    /// station from the kit list and tap where it should stand. Commands go to the stations service.
    /// </summary>
    public static class StationBuild
    {
        public static string Selected { get; private set; }
        public static string LastMessage { get; private set; } = "";
        public static event Action SelectionChanged;

        private static readonly List<SpriteRenderer> Marks = new List<SpriteRenderer>();
        private static Sprite _diamond;
        private static Transform _markRoot;

        public static void Select(string stationId)
        {
            // A station riding with another (the Smelter with the Forge) is moved as its whole setup.
            var root = GameRoot.Instance;
            if (stationId != null && root != null) stationId = root.Services.Stations.LeaderOf(stationId);
            Selected = stationId;
            LastMessage = "";
            if (stationId != null && ShopView.Current != null && ShopView.Current.Selected != null) ShopView.Current.Deselect();
            DrawFootprint(null, true);
            SelectionChanged?.Invoke();
        }

        public static void Deselect()
        {
            if (Selected == null && LastMessage == "") return;
            Selected = null;
            LastMessage = "";
            ClearMarks();
            SelectionChanged?.Invoke();
        }

        public static void Report(string message)
        {
            LastMessage = message ?? "";
            SelectionChanged?.Invoke();
        }

        /// <summary>A build-mode tap: picks a station, or sets the picked one up where the finger is. True when used.</summary>
        public static bool HandleTap(Vector2 world)
        {
            var root = GameRoot.Instance;
            if (root == null) return false;
            var hit = StationAt(world);
            if (hit != null && hit.StationId != Selected) { Select(hit.StationId); return true; }
            if (Selected == null) return false;
            var site = root.Services.Stations.Site(Selected);
            if (site == null) { Deselect(); return true; }
            var (x, y) = StationSiteView.CornerOf(world, new Vector2Int(site.W, site.H));   // the footprint centred under the finger
            var why = root.Services.Stations.Place(Selected, x, y);
            DrawFootprint((x, y), why == null);
            Report(why ?? "Setting up. It takes a moment to build.");
            return true;
        }

        public static StationSiteView StationAt(Vector2 world)
        {
            StationSiteView best = null;
            foreach (var v in StationSiteView.All.Values)
            {
                if (v == null || !v.isActiveAndEnabled || v.TapCollider == null) continue;
                var col = v.TapCollider;
                var was = col.enabled;
                col.enabled = true;   // a station being built can still be picked and moved
                var over = col.OverlapPoint(world);
                col.enabled = was;
                if (over && (best == null || v.transform.position.y < best.transform.position.y)) best = v;
            }
            return best;
        }

        /// <summary>The map cell under a world point (cell centres at ((x - y) / 2, (x + y) / 4 + 0.25)).</summary>
        public static Vector2Int CellAt(Vector2 world)
        {
            var c = IsoMap.Cell(world);
            return new Vector2Int(c.x, c.y);
        }

        // ------------------------------------------------------------------ the footprint outline

        private static void ClearMarks()
        {
            foreach (var m in Marks) if (m != null) m.enabled = false;
        }

        private static void DrawFootprint((int x, int y)? corner, bool ok)
        {
            ClearMarks();
            var root = GameRoot.Instance;
            if (root == null || Selected == null) return;
            var site = root.Services.Stations.Site(Selected);
            if (site == null) return;
            if (!corner.HasValue)
            {
                if (!site.Standing) return;
                corner = (site.X, site.Y);
            }
            var i = 0;
            for (var cx = corner.Value.x; cx < corner.Value.x + site.W; cx++)
            for (var cy = corner.Value.y; cy < corner.Value.y + site.H; cy++)
            {
                var m = Mark(i++);
                m.enabled = true;
                m.transform.position = IsoMap.World(cx, cy);
                var free = root.Services.Stations.GroundProblem(cx, cy) == null;
                m.color = ok || free ? new Color(0.45f, 0.9f, 0.45f, 0.55f) : new Color(0.95f, 0.35f, 0.3f, 0.6f);
            }
        }

        private static SpriteRenderer Mark(int i)
        {
            if (_markRoot == null) _markRoot = new GameObject("StationFootprint").transform;
            while (Marks.Count <= i || Marks[i] == null)
            {
                if (_diamond == null) _diamond = Diamond();
                var go = new GameObject("Cell");
                go.transform.SetParent(_markRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _diamond;
                sr.sortingLayerName = "Overlay";
                sr.sortingOrder = -50;
                if (Marks.Count <= i) Marks.Add(sr); else Marks[i] = sr;
            }
            return Marks[i];
        }

        private static Sprite Diamond()
        {
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var d = Mathf.Abs((x + 0.5f) - w * 0.5f) / (w * 0.5f) + Mathf.Abs((y + 0.5f) - h * 0.5f) / (h * 0.5f);
                var a = d <= 0.96f ? (byte)(d > 0.8f ? 255 : 110) : (byte)0;   // a soft fill with a firm rim
                px[y * w + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 128);
        }
    }
}
