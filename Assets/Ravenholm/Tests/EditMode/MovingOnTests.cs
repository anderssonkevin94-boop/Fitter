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

        [Test]
        [Ignore("Gated levels, step 2 (2026-10-16): moving on goes through the level's gate; rewritten then")]
        public void TheCompanyMakesCampAtTheNextClearingAndItIsRemembered()
        {
            var g = Boot();
            var camp = g.Camp;
            Assert.AreEqual(0, camp.Index);
            Assert.AreEqual("Meadow's Edge", camp.Name);
            Assert.AreEqual("River Bend", camp.NextName);
            Assert.AreEqual(Vector2.zero, camp.Offset);
            var there = camp.CentreOf(1);
            Assert.AreEqual("Make camp from River Bend's clearing", camp.MoveOnBlockedReason(Vector2.zero), "from camp 1 itself: no");
            Assert.IsTrue(camp.AtNext(there));
            StringAssert.Contains("summons", camp.MoveOnBlockedReason(there), "the castle's summons first (step 5)");
            g.Jobs.Board.DebugRecordSuccess("quest.roadside_patrol");
            g.Jobs.Board.DebugRecordSuccess("quest.old_shrine");

            // A station's pad holding goods: collect them first.
            g.Logistics.System.CreateMaterialsInContainer("container.forge_pad", "resource.iron_ingot", 1);
            StringAssert.Contains("Forge", camp.MoveOnBlockedReason(there));
            for (var i = 0; i < 40 && g.Logistics.System.State.Containers["container.forge_pad"].UnitCount > 0; i++) g.Logistics.System.Tick(0.5f, -4f, -4f);
            g.Logistics.System.DebugUnloadCarry();

            // More than the wagon carries: leave the extras behind.
            g.Logistics.System.ProduceShared("resource.cedar_log", camp.CargoCapacity + 20, "test");
            StringAssert.Contains("The wagon can carry", camp.MoveOnBlockedReason(there));
            Assert.Greater(camp.LeaveExtrasBehind(), 0);
            Assert.LessOrEqual(camp.CargoLoad, camp.CargoCapacity);

            Assert.IsNull(camp.MoveOn(there));
            Assert.AreEqual(1, camp.Index);
            Assert.AreEqual("River Bend", camp.Name);
            Assert.AreEqual("Hollow Wood", camp.NextName, "the road goes on (step 7)");
            Assert.Greater(camp.Offset.magnitude, 10f, "the camp's group moved up the road");
            Assert.IsTrue(g.Stations.State.Sites.Values.All(s => s.Mode == StationSiteMode.Packed), "every station packed into its kit");
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
            g.Scheduler.SaveNow("test");

            var back = Boot();
            Assert.AreEqual(1, back.Camp.Index, "the camp is remembered");
            Assert.IsTrue(back.Stations.Site("station.forge").Mode == StationSiteMode.Packed);
        }
    }
}
