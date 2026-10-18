using System;
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
            if (camp.Plateau)
            {
                // Gated levels (Kevin, 2026-10-16): a square plateau of 60 cells, the camp at its front, the road on to the
                // gate across the far corner; nothing past its edges.
                Assert.AreEqual(60, camp.LevelSize);
                var e = r.Extent;
                int land = 0;
                for (var x = e.minX - 4; x <= e.maxX + 4; x++)
                for (var y = e.minY - 4; y <= e.maxY + 4; y++)
                {
                    var outside = x < e.minX || x > e.maxX || y < e.minY || y > e.maxY;
                    if (outside) Assert.IsFalse(r.InLand(x, y), $"nothing past the level at ({x}, {y})");
                    else if (r.InLand(x, y)) land++;
                }
                Assert.Greater(land, 3200, "about 60 x 60");
                Assert.Less(land, 4800, "about 60 x 60");
                // The camp stands just below the north rim (Kevin, 2026-10-18: the castle view from camp): straight up the
                // screen from it the land ends within about ten world units, and that rim is broad, not a corner.
                var up = 0;
                while (up < 80 && r.InLand((int)cx + up, (int)cy + up)) up++;
                Assert.Less(up, 20, $"the north rim close above the camp ({up} cells up the screen)");
                Assert.Greater(up, 8, "but clear of the camp's buildings");
                var (rx, ry) = ((int)cx + up - 3, (int)cy + up - 3);
                var across = 0;
                for (var k = -30; k <= 30; k++) if (r.InLand(rx + k, ry - k)) across++;
                Assert.Greater(across, 30, $"a broad north rim ({across} cells across near the top)");
                Assert.Greater(r.Length - r.CampS, 20, "the road crosses the level to the gate");
                var (gx, gy) = r.PointAt(r.Gates[0].S);
                Assert.Greater(r.PlateauSide * ((gx - gy) - (cx - cy)) * 0.5, 12, "the gate is on a side rim, across the screen from the camp");
                Assert.IsTrue(r.GateAt((int)gx, (int)gy) != null, "the gate on the road");
                var (bx, by) = r.PointBeside(r.Gates[0].S, 12);
                Assert.IsNull(r.GateAt((int)Math.Round(bx), (int)Math.Round(by)), "only by the road");
                for (var g = 1; g < r.Gates.Count; g++) Assert.Greater(r.Gates[g].S, r.Length, "only this level's gate");
                return;
            }
            Assert.Greater(r.Length - r.CampS, 120, "the land runs well forward of the camp");
            Assert.Less(r.CampS, 40, "and only a little way behind it");
            // Across the ridge at points ahead: wide (2-4 screens), never a corridor, never a plain.
            for (var ahead = 30; ahead <= 120; ahead += 15)
            {
                var (px, py) = r.PointAt(r.CampS + ahead);
                var across = 0;
                for (var k = -60; k <= 60; k++)
                    if (r.InLand((int)System.Math.Round(px + k * 0.7071), (int)System.Math.Round(py - k * 0.7071))) across++;
                // Beside a camp's bay (the drop it looks over, 2026-10-16) the land is narrower, still wider than the view.
                var byBay = false;
                for (var k = -60; k <= 60 && !byBay; k++)
                    byBay = r.InCove((int)System.Math.Round(px + k * 0.7071), (int)System.Math.Round(py - k * 0.7071));
                // The switchback pass (2026-10-16): legs 16 tiles wide (Astra's paintings), wider than the 7-unit view.
                Assert.Greater(across, byBay || camp.Switchback ? 14 : 30, $"wide {ahead} ahead ({across} cells across)");
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
                for (var dx = -14; dx <= 14 && !found; dx++)   // it swings out to the edge near a camp
                for (var dy = -14; dy <= 14 && !found; dy++)
                    found = r.OnRoad((int)px + dx, (int)py + dy);
                if (found) road++;
            }
            Assert.Greater(road, 60, "the old road runs on ahead");
            Assert.AreEqual(5, camp.Gates.Count, "one blocker between each pair of the six camps");
            var (gx, gy) = r.PointAt(r.CampS + camp.GateAhead(0));
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
            Assert.AreEqual(1, r.BandAt((int)ax, (int)ay));
            if (Camp().Plateau)
            {
                // A level is one band; within it, tougher toward the gate.
                var (gx, gy) = r.PointAt(r.Length - 6);
                Assert.AreEqual(1, r.BandAt((int)gx, (int)gy), "one band across the level");
                Assert.Less(r.ContinuationAt((int)ax, (int)ay), r.ContinuationAt((int)gx, (int)gy), "tougher toward the gate");
            }
            else
            {
                var (bx, by) = r.PointAt(r.CampS + 100);
                Assert.Greater(r.BandAt((int)bx, (int)by), 1, "the next band forward");
            }
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
