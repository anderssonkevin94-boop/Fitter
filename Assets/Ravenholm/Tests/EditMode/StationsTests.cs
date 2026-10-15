using System;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Camp;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The journey, step 2 (2026-10-15): stations are set up on cleared, level ground at the camp (a short build), packed
    /// into kits and set up again, and remembered.
    /// </summary>
    public sealed class StationsTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
        private ManualClock _clock;

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            _clock ??= new ManualClock(T0);
            var g = new GameServices(_clock, cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            foreach (var site in cat.Sites)
                g.Logistics.RegisterContainer(site.StagingContainerId, Domain.Logistics.ContainerKind.RawPile, 0f, 100f, 0.45f, null);
            g.Logistics.RegisterContainer("container.forge_pad", Domain.Logistics.ContainerKind.OutputPad, -4f, -4f, 0.45f, null);
            // Where the scene lays the stations out (corner cells).
            g.Stations.RegisterDefault("station.smelter", -18, 10, 2, 2);
            g.Stations.RegisterDefault("station.forge", -14, -4, 2, 2);
            return g;
        }

        /// <summary>A forest cell ahead of the camp with a tree on flat ground.</summary>
        private static (int x, int y) FlatTree(GameServices g)
        {
            var forest = g.Sites.Get("site.forest");
            var r = forest.Config.Grove.Ridge;
            for (var s = 20; s < 50; s++)
            {
                var (px, py) = r.PointAt(r.CampS + s);
                for (var k = -10; k <= 10; k++)
                {
                    var (x, y) = ((int)px + k, (int)py - k);
                    if (forest.CanChop(x, y) && forest.TerrainHeight(x, y) == 0 && g.Stations.GroundProblem(x, y) != null) return (x, y);
                }
            }
            Assert.Fail("no flat tree");
            return default;
        }

        [Test]
        public void AStationIsSetUpInTheClearingAfterAShortBuild()
        {
            var g = Boot();
            var camp = g.Catalogue.CurrentCamp;
            var (cx, cy) = ((int)camp.ClearingCentre.x + 6, (int)camp.ClearingCentre.y + 6);
            Assert.IsNull(g.Stations.CanPlace("station.smelter", cx, cy), "the clearing is cleared, level ground");
            Assert.IsNull(g.Stations.Place("station.smelter", cx, cy));
            var site = g.Stations.Site("station.smelter");
            Assert.AreEqual(StationSiteMode.Placed, site.Mode);
            Assert.AreEqual((cx, cy), (site.X, site.Y));
            Assert.IsTrue(g.Stations.Building("station.smelter"), "it takes a moment to build");
            _clock.Advance(TimeSpan.FromSeconds(g.Balance.stationBuildSeconds + 0.5));
            Assert.IsFalse(g.Stations.Building("station.smelter"));
            Assert.AreEqual(1f, g.Stations.BuildProgress("station.smelter"));
            Assert.IsNotNull(g.Stations.CanPlace("station.forge", cx + 1, cy + 1), "never on another station");
        }

        [Test]
        public void ForestGroundMustBeClearedOfTreesAndStumpsAndHillsDugFlat()
        {
            var g = Boot();
            var forest = g.Sites.Get("site.forest");
            var (x, y) = FlatTree(g);
            Assert.AreEqual("Clear the trees first", g.Stations.GroundProblem(x, y));
            g.Sites.SpeedMultiplier = 50f;
            for (var i = 0; i < 4000 && !forest.IsFelled(x, y); i++) g.Sites.PlayerChopCell("site.forest", x, y, 0.5f);
            g.Logistics.System.DebugUnloadCarry();
            Assert.AreEqual("Pull the stumps out first", g.Stations.GroundProblem(x, y));
            Assert.IsTrue(g.Sites.ClearStump("site.forest", x, y));
            g.Logistics.System.DebugUnloadCarry();
            Assert.IsNull(g.Stations.GroundProblem(x, y), "cleared, level forest ground can be built on");

            // A hill: dig it down to the floor first.
            var r = forest.Config.Grove.Ridge;
            (int x, int y) hill = default;
            for (var s = 20; s < 60 && hill == default; s++)
            {
                var (px, py) = r.PointAt(r.CampS + s);
                for (var k = -15; k <= 15 && hill == default; k++)
                {
                    var (hx, hy) = ((int)px + k, (int)py - k);
                    if (forest.Config.Grove.InGrove(hx, hy) && forest.TerrainHeight(hx, hy) > 0 && forest.IsRock(hx, hy) && !g.Sites.ToolTooWeak(hx, hy)) hill = (hx, hy);
                }
            }
            Assert.AreNotEqual(default((int, int)), hill, "a bare hill somewhere ahead");
            Assert.AreEqual("Dig the ground level first", g.Stations.GroundProblem(hill.x, hill.y));
            for (var level = forest.TerrainHeight(hill.x, hill.y); level >= 1; level--)
            {
                for (var i = 0; i < 4000 && !g.Sites.PlayerBreakSurface(hill.x, hill.y, level, 0.5f); i++) { }
                g.Logistics.System.DebugUnloadCarry();
            }
            Assert.IsNull(g.Stations.GroundProblem(hill.x, hill.y), "dug flat");
            Assert.AreEqual("Not on the land", g.Stations.GroundProblem(hill.x + 200, hill.y - 200));
        }

        [Test]
        public void AStationPacksIntoItsKitOnlyWithAnEmptyPadAndIsRemembered()
        {
            var g = Boot();
            g.Logistics.System.CreateMaterialsInContainer("container.forge_pad", "resource.iron_ingot", 1);
            Assert.AreEqual("Collect the goods from its pad first", g.Stations.Pack("station.forge"));
            for (var i = 0; i < 40 && g.Logistics.System.State.Containers["container.forge_pad"].UnitCount > 0; i++)
                g.Logistics.System.Tick(0.5f, -4f, -4f);   // the supplier picks the goods up
            Assert.AreEqual(0, g.Logistics.System.State.Containers["container.forge_pad"].UnitCount);
            Assert.IsNull(g.Stations.Pack("station.forge"));
            CollectionAssert.Contains(new System.Collections.Generic.List<StationSite>(g.Stations.Kits()), g.Stations.Site("station.forge"));
            var camp = g.Catalogue.CurrentCamp;
            var (cx, cy) = ((int)camp.ClearingCentre.x - 4, (int)camp.ClearingCentre.y + 8);
            Assert.IsNull(g.Stations.Place("station.smelter", cx, cy));
            g.Scheduler.SaveNow("test");

            var back = Boot();
            var forge = back.Stations.Site("station.forge");
            Assert.AreEqual(StationSiteMode.Packed, forge.Mode, "packed stays packed");
            var smelter = back.Stations.Site("station.smelter");
            Assert.AreEqual(StationSiteMode.Placed, smelter.Mode);
            Assert.AreEqual((cx, cy), (smelter.X, smelter.Y), "set up where it was put");
            Assert.IsNull(back.Stations.Place("station.forge", cx + 4, cy), "a kit is set up again");
            Assert.AreEqual(StationSiteMode.Placed, back.Stations.Site("station.forge").Mode);
        }
    }
}
