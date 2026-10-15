using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The journey's land (Kevin, 2026-10-15): a long, wide, winding mountain top running forward from the camp toward the
    /// castle; the old road along it; a gate across it that needs a better tool; tougher further forward.
    /// </summary>
    public sealed class CampTests
    {
        private static CampDefinition Camp() => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath).CurrentCamp;

        [Test]
        public void TheLandRunsForwardFromTheCampAndIsWideButNotEndless()
        {
            var camp = Camp();
            Assert.NotNull(camp, "camp 1 is in the catalogue");
            var r = camp.Ridge;
            var cx = (int)camp.ClearingCentre.x;
            var cy = (int)camp.ClearingCentre.y;
            Assert.IsTrue(r.InClearing(cx, cy), "the camp's clearing");
            Assert.Greater(r.Length - r.CampS, 120, "the land runs well forward of the camp");
            Assert.Less(r.CampS, 40, "and only a little way behind it");
            // Across the ridge at points ahead: wide (2-4 screens), never a corridor, never a plain.
            for (var ahead = 30; ahead <= 120; ahead += 15)
            {
                var (px, py) = r.PointAt(r.CampS + ahead);
                var across = 0;
                for (var k = -60; k <= 60; k++)
                    if (r.InLand((int)System.Math.Round(px + k * 0.7071), (int)System.Math.Round(py - k * 0.7071))) across++;
                Assert.Greater(across, 30, $"wide {ahead} ahead ({across} cells across)");
                Assert.Less(across, 85, $"but not endless {ahead} ahead ({across} cells across)");
            }
        }

        [Test]
        public void TheEdgesAreRaggedNotStraight()
        {
            var r = Camp().Ridge;
            // Walk along the ridge, measuring the left edge's offset: it wanders by more than a few cells.
            double min = double.MaxValue, max = double.MinValue;
            for (var ahead = 20; ahead <= 120; ahead += 4)
            {
                var (px, py) = r.PointAt(r.CampS + ahead);
                var k = 0;
                while (k < 80 && r.InLand((int)System.Math.Round(px - k * 0.7071), (int)System.Math.Round(py + k * 0.7071))) k++;
                min = System.Math.Min(min, k);
                max = System.Math.Max(max, k);
            }
            Assert.Greater(max - min, 6, "the edge wanders");
        }

        [Test]
        public void TheRoadLeadsForwardAndTheGateNeedsTheIronTool()
        {
            var camp = Camp();
            var r = camp.Ridge;
            var road = 0;
            for (var ahead = 25; ahead <= 90; ahead++)
            {
                var (px, py) = r.PointAt(r.CampS + ahead);
                var found = false;
                for (var dx = -8; dx <= 8 && !found; dx++)
                for (var dy = -8; dy <= 8 && !found; dy++)
                    found = r.OnRoad((int)px + dx, (int)py + dy);
                if (found) road++;
            }
            Assert.Greater(road, 60, "the old road runs on ahead");
            Assert.AreEqual(2, camp.Gates.Count, "the old trees before camp 2, a rock wall after it");
            var (gx, gy) = r.PointAt(r.CampS + camp.Gates[0].ahead);
            Assert.AreEqual(2, r.GateTierAt((int)gx, (int)gy), "the gate across the road needs the iron tool");
            Assert.IsTrue(r.FixedOpen(42, (int)gx, (int)gy, out var open) && !open, "solid: no gaps in the old trees");
            var (fx, fy) = r.PointAt(r.CampS + 20);
            Assert.AreEqual(0, r.GateTierAt((int)fx, (int)fy), "the forest before it takes any tool");
        }

        [Test]
        public void FurtherForwardIsTougherAndDeeper()
        {
            var r = Camp().Ridge;
            var (ax, ay) = r.PointAt(r.CampS + 20);
            var (bx, by) = r.PointAt(r.CampS + 100);
            Assert.AreEqual(1, r.BandAt((int)ax, (int)ay));
            Assert.Greater(r.BandAt((int)bx, (int)by), 1, "the next band forward");
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var mine = SitesService.BuildConfig(cat.Get<SiteDefinition>("site.mine"), cat.CurrentCamp);
            Assert.AreEqual(0, mine.Dig.Length, "no quarry plot: the hills are the Mine");
            var forest = SitesService.BuildConfig(cat.Get<SiteDefinition>("site.forest"), cat.CurrentCamp);
            Assert.AreSame(r, forest.Grove.Ridge);
            Assert.IsFalse(forest.Grove.InGrove((int)Camp().ClearingCentre.x, (int)Camp().ClearingCentre.y), "no trees in the clearing");
            var hills = 0;
            var trees = 0;
            var cells = 0;
            for (var x = (int)ax - 25; x <= (int)ax + 25; x++)
            for (var y = (int)ay - 25; y <= (int)ay + 25; y++)
            {
                if (!forest.Grove.InGrove(x, y)) continue;
                var h = forest.Grove.TerrainHeightAt(42, x, y);
                if (r.OnRoad(x, y)) Assert.AreEqual(0, h, "the road stays flat");
                cells++;
                if (h >= 3) hills++;
                if (!forest.Grove.OpenGroundAt(42, x, y)) trees++;
            }
            Assert.Greater(hills, cells / 20, $"the forest-mountains hills are back ({hills} of {cells} cells 3+ high)");
            Assert.Less(trees, cells * 6 / 10, $"trees not packed tight ({trees} of {cells})");
        }
    }
}
