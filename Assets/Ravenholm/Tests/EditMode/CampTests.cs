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
                // The horseshoe (Kevin's drawing, 2026-10-19): wide land open to the north in the middle (the bay), the camp
                // at the bottom of the U looking over it, arms either side, the gate in a pass to the north-west; nothing
                // past the level's edges.
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
                Assert.Greater(land, 4000, $"a big level ({land} cells)");
                Assert.Less(land, 8000, $"but not endless ({land} cells)");
                // Straight up the screen from the camp the land ends at the bay within about ten world units (the castle
                // view), clear of the camp's buildings; and the bay stays open on north (no land further up that line).
                var up = 0;
                while (up < 120 && r.InLand((int)cx + up, (int)cy + up)) up++;
                Assert.Less(up, 20, $"the bay close above the camp ({up} cells up the screen)");
                Assert.Greater(up, 8, "but clear of the camp's buildings");
                Assert.IsTrue(r.InBay((int)cx + up + 2, (int)cy + up + 2), "the bay");
                for (var k = up + 2; k < up + 80; k++) Assert.IsFalse(r.InLand((int)cx + k, (int)cy + k), "open to the north: the view to the castle");
                // Arms either side of the bay reach north past it.
                var wy0 = (cx + cy) * 0.25 + 0.25;
                var (bwy, bwx) = (r.BayCY, r.BayCX);
                int Arm(double side)
                {
                    var n = 0;
                    for (var dx = 2.0; dx < 30; dx += 0.5)
                    {
                        var wx = bwx + side * (r.BayRX + dx);
                        var (x, y) = ((int)Math.Round(wx + 2 * (bwy - 0.25)), (int)Math.Round(2 * (bwy - 0.25) - wx));
                        if (r.InLand(x, y)) n++;
                    }
                    return n;
                }
                Assert.Greater(Arm(-1), 6, "a west arm beside the bay");
                Assert.Greater(Arm(1), 6, "an east arm beside the bay");
                Assert.Greater(r.Length - r.CampS, 30, "the road climbs to the pass");
                // The gate: to the north-west, in a narrow pass between ridges no tool breaks.
                var (gx, gy) = r.PointAt(r.Gates[0].S);
                var (gwx, gwy) = ((gx - gy) * 0.5, (gx + gy) * 0.25 + 0.25);
                var cwx = (cx - cy) * 0.5;
                Assert.Less(gwx, cwx - 8, "the gate is west of the camp");
                Assert.Greater(gwy, wy0 + 8, "and north of it");
                Assert.IsTrue(r.GateAt((int)gx, (int)gy) != null, "the gate on the road");
                Assert.IsTrue(r.Gates[0].Project && r.Gates[0].Barrier, "a gate to repair, not a gap");
                var (bx, by) = r.PointBeside(r.Gates[0].S, 5);
                Assert.IsTrue(r.InPassRidge((int)Math.Round(bx), (int)Math.Round(by)), "a ridge beside the pass");
                Assert.AreEqual(RidgeLayout.UnbreakableTier, r.GateTierAt((int)Math.Round(bx), (int)Math.Round(by)), "that no tool breaks");
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
        public void TheRoadLeadsForwardAndEachGateIsSomethingToFix()
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
            // The horseshoe levels (2026-10-19): every gate is a project to fix; the pass's ridges are what no tool breaks.
            foreach (var g in camp.Gates) Assert.IsFalse(string.IsNullOrEmpty(g.project), "each level's gate is something to fix");
            var (gx, gy) = r.PointAt(r.Gates[0].S);
            Assert.AreEqual(0, r.GateTierAt((int)gx, (int)gy), "the gate itself takes no tool: it is repaired");
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
