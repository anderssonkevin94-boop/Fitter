using System;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Camp;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Moving on (the journey, step 4, 2026-10-15): from the next camp's clearing the company makes camp there, the
    /// stations packed (pads empty), the wagon's load within its storage; the camp's group moves; it is remembered.
    /// </summary>
    public sealed class MovingOnTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            g.Logistics.RegisterContainer("container.forge_pad", Domain.Logistics.ContainerKind.OutputPad, -4f, -4f, 0.45f, null);
            g.Stations.RegisterDefault("station.smelter", -18, 10, 2, 2);
            g.Stations.RegisterDefault("station.forge", -14, -4, 2, 2);
            return g;
        }

        /// <summary>A standing tree near a point of the level's road that the basic tool can cut.</summary>
        private static (int x, int y) TreeNear(GameServices g, double ahead)
        {
            var forest = g.Sites.Get("site.forest");
            var ridge = forest.Config.Grove.Ridge;
            for (var s = ahead; s < ridge.Length; s += 1)
            {
                var (px, py) = ridge.PointAt(ridge.CampS + s);
                for (var k = -20; k <= 20; k++)
                {
                    var (x, y) = ((int)px + k, (int)py - k);
                    if (forest.CanChop(x, y) && !g.Sites.ToolTooWeak(x, y)) return (x, y);
                }
            }
            Assert.Fail("no tree to cut");
            return default;
        }

        private static void Fell(GameServices g, (int x, int y) t)
        {
            var forest = g.Sites.Get("site.forest");
            g.Sites.SpeedMultiplier = 50f;
            for (var i = 0; i < 4000 && !forest.IsFelled(t.x, t.y); i++) g.Sites.PlayerChopCell("site.forest", t.x, t.y, 0.5f);
            g.Logistics.System.DebugUnloadCarry();
            Assert.IsTrue(forest.IsFelled(t.x, t.y));
        }

        [Test]
        public void TheCompanyGoesThroughTheGateToTheNextLevelAndItIsRemembered()
        {
            var g = Boot();
            var camp = g.Camp;
            Assert.IsTrue(camp.Levels, "gated levels (2026-10-16)");
            Assert.AreEqual(0, camp.Index);
            Assert.AreEqual("Meadow's Edge", camp.Name);
            Assert.AreEqual("River Bend", camp.NextName);
            Assert.AreEqual(Vector2.zero, camp.Offset);
            var gate = camp.ExitWorld;
            Assert.AreEqual("Go through the gate to River Bend", camp.MoveOnBlockedReason(Vector2.zero), "from the camp: no");
            Assert.IsTrue(camp.AtNext(gate), "at the road's end past the gate");
            StringAssert.Contains("summons", camp.MoveOnBlockedReason(gate), "the castle's summons first (step 5)");
            g.Jobs.Board.DebugRecordSuccess("quest.roadside_patrol");
            g.Jobs.Board.DebugRecordSuccess("quest.old_shrine");

            // A station's pad holding goods: collect them first.
            g.Logistics.System.CreateMaterialsInContainer("container.forge_pad", "resource.iron_ingot", 1);
            StringAssert.Contains("Forge", camp.MoveOnBlockedReason(gate));
            for (var i = 0; i < 40 && g.Logistics.System.State.Containers["container.forge_pad"].UnitCount > 0; i++) g.Logistics.System.Tick(0.5f, -4f, -4f);
            g.Logistics.System.DebugUnloadCarry();

            // More than the wagon carries: leave the extras behind.
            g.Logistics.System.ProduceShared("resource.cedar_log", camp.CargoCapacity + 20, "test");
            StringAssert.Contains("The wagon can carry", camp.MoveOnBlockedReason(gate));
            Assert.Greater(camp.LeaveExtrasBehind(), 0);
            Assert.LessOrEqual(camp.CargoLoad, camp.CargoCapacity);

            // A tree cut on level 1.
            var forest = g.Sites.Get("site.forest");
            var cut1 = TreeNear(g, 8);
            Fell(g, cut1);
            if (camp.CargoLoad > camp.CargoCapacity) camp.LeaveExtrasBehind();

            Assert.IsNull(camp.MoveOn(gate));
            Assert.AreEqual(1, camp.Index);
            Assert.AreEqual("River Bend", camp.Name);
            Assert.AreEqual("Hollow Wood", camp.NextName, "the levels go on");
            Assert.AreEqual(Vector2.zero, camp.Offset, "every level's camp stands in the same place");
            Assert.IsTrue(g.Stations.State.Sites.Values.All(s => s.Mode == StationSiteMode.Packed), "every station packed into its kit");
            var road = g.Catalogue.CurrentCamp;
            Assert.AreSame(road.RidgeFor(1), forest.Config.Grove.Ridge, "the Forest is level 2's");
            Assert.AreSame(road.RidgeFor(1), camp.Ridge);
            Assert.AreEqual(1, g.Sites.Level);
            Assert.IsFalse(forest.IsFelled(cut1.x, cut1.y), "level 1's cut trees stay behind (one way)");
            var cut2 = TreeNear(g, 8);
            Assert.AreEqual(2, forest.BandOf(cut2.x, cut2.y), "level 2 is the second band of wood");
            Fell(g, cut2);
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
            g.Scheduler.SaveNow("test");

            var back = Boot();
            Assert.AreEqual(1, back.Camp.Index, "the level is remembered");
            Assert.AreEqual(1, back.Sites.Level);
            Assert.AreSame(road.RidgeFor(1), back.Sites.Get("site.forest").Config.Grove.Ridge, "and its land");
            Assert.IsTrue(back.Sites.Get("site.forest").IsFelled(cut2.x, cut2.y), "level 2's cut tree is remembered");
            Assert.IsTrue(back.Stations.Site("station.forge").Mode == StationSiteMode.Packed);
        }

        [Test]
        public void EachLevelIsItsOwnLand()
        {
            var road = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath).CurrentCamp;
            Assert.IsTrue(road.Plateau);
            var a = road.RidgeFor(0);
            var b = road.RidgeFor(1);
            Assert.AreNotSame(a, b);
            Assert.AreSame(b, road.RidgeFor(1), "built once");
            var cx = (int)road.ClearingCentre.x;
            var cy = (int)road.ClearingCentre.y;
            Assert.IsTrue(a.InClearing(cx, cy) && b.InClearing(cx, cy), "the camp in the same place");
            var differ = 0;
            var e = a.Extent;
            for (var x = e.minX; x <= e.maxX; x++)
            for (var y = e.minY; y <= e.maxY; y++)
                if (a.InLand(x, y) != b.InLand(x, y)) differ++;
            Assert.Greater(differ, 60, "a different outline");
            Assert.AreEqual(2, b.BandAt(cx + 10, cy + 10), "the second band");
            Assert.Less(b.Gates[1].S, b.Length, "level 2's own gate");
            Assert.Greater(b.Gates[0].S, b.Length, "not level 1's");
        }
    }
}
