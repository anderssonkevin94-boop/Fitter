using System;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The road to the castle (the journey, step 7, 2026-10-16): six camps, a blocker before each, two of them build
    /// projects (a bridge over a ravine, a causeway over a flood), the sealed gate needing the moonsilver tool; each camp's
    /// land tinted; one band per camp; the finale at the last camp.
    /// </summary>
    public sealed class RoadTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);

        private GameServices Boot()
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), Cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        [Test]
        public void SixCampsUpTheRoadWithABlockerBeforeEach()
        {
            var road = Cat.CurrentCamp;
            Assert.AreEqual(6, road.CampCount);
            CollectionAssert.AreEqual(new[] { "Meadow's Edge", "River Bend", "Hollow Wood", "Witchlight Fen", "Crown Highlands", "Castle Approach" },
                Enumerable.Range(0, 6).Select(road.CampName));
            for (var i = 0; i < 5; i++)
                Assert.That(road.Gates[i].ahead, Is.InRange(road.CampAhead(i) + 20, road.CampAhead(i + 1) - 20), $"blocker {i + 1} lies between camps {i + 1} and {i + 2}");
            Assert.AreEqual("Bridge", road.Gates[2].project);
            Assert.AreEqual("Causeway", road.Gates[3].project);
            Assert.AreEqual(4, road.Gates[4].tier, "the sealed gate needs the moonsilver tool");
            Assert.IsTrue(Cat.Equipment.Any(e => e.Slot == EquipmentSlot.Tool && e.Tier == 4), "and the moonsilver tool exists");
            var ridge = road.Ridge;
            Assert.Greater(ridge.Length - ridge.CampS, road.CampAhead(5) + 20, "the land runs past the last camp");
            Assert.AreEqual(6, ridge.BandAt((int)ridge.PointAt(ridge.CampS + road.CampAhead(5)).x, (int)ridge.PointAt(ridge.CampS + road.CampAhead(5)).y),
                "one band per camp: the last camp is band 6");
            var steps = Enumerable.Range(0, (int)(ridge.Length - ridge.CampS)).Select(a => ridge.PointAt(ridge.CampS + a))
                .Select(p => ridge.ContinuationAt((int)p.x, (int)p.y)).ToList();
            Assert.AreEqual(2, steps.Max(), "three effort steps per band, so a band's last trees stay easier than the next band's first");
            Assert.AreNotEqual(Color.white, road.GroundTintAt(road.CampAhead(2)), "Hollow Wood's land has its own tint");
            Assert.AreEqual(Color.white, road.GroundTintAt(10), "Meadow's Edge is as it was");
        }

        [Test]
        public void ABridgeIsBuiltFromStockAndRemembered()
        {
            var g = Boot();
            var camp = g.Camp;
            var ridge = g.Catalogue.CurrentCamp.Ridge;
            var (gx, gy) = ridge.PointAt(ridge.Gates[2].S);
            Assert.IsTrue(ridge.InProjectGap((int)gx, (int)gy), "a ravine across the road");
            Assert.IsFalse(ridge.InGrove((int)gx, (int)gy), "no trees in it");
            Assert.AreEqual(0, ridge.GateTierAt((int)gx, (int)gy), "no tool gets you across: build the bridge");
            StringAssert.Contains("Needs", camp.BuildBlockedReason(2));
            g.Logistics.System.ProduceShared("resource.ironwood_lumber", 12, "test");
            g.Logistics.System.ProduceShared("resource.rope", 8, "test");
            Assert.IsNull(camp.Build(2));
            Assert.IsTrue(camp.IsBuilt(2));
            Assert.AreEqual(0, g.Logistics.System.SharedAvailable("resource.ironwood_lumber"), "paid from stock");
            Assert.AreEqual("Already built", camp.BuildBlockedReason(2));
            g.Scheduler.SaveNow("test");
            Assert.IsTrue(Boot().Camp.IsBuilt(2), "the bridge stays built");
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
        }

        [Test]
        public void TheFinaleIsTheCastlesLastGreatJobAtTheLastCamp()
        {
            var finale = Cat.Quests.Single(q => q.CampaignFinale);
            Assert.AreEqual("The Castle's Last Great Job", finale.DisplayName);
            Assert.AreEqual(6, finale.Camp, "posted at Castle Approach");
            Assert.IsFalse(Cat.Quests.Any(q => q.DisplayName.Contains("Beacon")), "the Beacon wording is retired");
            var g = Boot();
            for (var camp = 1; camp <= 5; camp++) Assert.NotNull(g.Jobs.Board.SummonsFor(camp), $"camp {camp} has a summons");
        }
    }
}
