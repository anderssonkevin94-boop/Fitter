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
        public void SixLevelsWithABlockerAtTheEndOfEachButTheLast()
        {
            // Gated levels (Kevin, 2026-10-16): six camps, each its own level, left through its gate on the north rim.
            var road = Cat.CurrentCamp;
            Assert.IsTrue(road.Plateau);
            Assert.AreEqual(6, road.CampCount);
            CollectionAssert.AreEqual(new[] { "Meadow's Edge", "River Bend", "Hollow Wood", "Witchlight Fen", "Crown Highlands", "Castle Approach" },
                Enumerable.Range(0, 6).Select(road.CampName));
            Assert.AreEqual(2, road.Gates[0].tier, "level 1: old trees, the iron tool");
            Assert.IsTrue(road.Gates[1].rock && road.Gates[1].tier == 3, "level 2: a rockslide, the tempered tool");
            Assert.AreEqual("Bridge", road.Gates[2].project, "level 3: a ravine to bridge");
            Assert.AreEqual("Causeway", road.Gates[3].project, "level 4: a flood to cross");
            Assert.AreEqual(4, road.Gates[4].tier, "level 5: the sealed gate needs the moonsilver tool");
            Assert.IsTrue(Cat.Equipment.Any(e => e.Slot == EquipmentSlot.Tool && e.Tier == 4), "and the moonsilver tool exists");
            for (var level = 0; level < 6; level++)
            {
                var r = road.RidgeFor(level);
                var cx = (int)road.ClearingCentre.x;
                var cy = (int)road.ClearingCentre.y;
                Assert.IsTrue(r.InClearing(cx, cy), $"level {level + 1}: the camp at its front");
                Assert.AreEqual(level + 1, r.BandAt(cx + 12, cy + 12), $"level {level + 1}: its own band of wood and ore");
                var own = 0;
                for (var g = 0; g < r.Gates.Count; g++)
                    if (r.Gates[g].S <= r.Length) { own++; Assert.AreEqual(level, g, $"level {level + 1}: only its own gate"); }
                Assert.AreEqual(level < 5 ? 1 : 0, own, level < 5 ? $"level {level + 1}: a gate on its rim" : "the last level: no gate, the castle beyond");
                var steps = Enumerable.Range(0, (int)(r.Length - r.CampS)).Select(a => r.PointAt(r.CampS + a))
                    .Select(p => r.ContinuationAt((int)p.x, (int)p.y)).ToList();
                Assert.AreEqual(2, steps.Max(), $"level {level + 1}: three effort steps toward its gate");
                // The way on (the road's end past the gate) is never grown over, whatever the world seed.
                var (ex, ey) = r.PointAt(r.Length);
                for (var dx = -2; dx <= 2; dx++)
                for (var dy = -2; dy <= 2; dy++)
                {
                    int x = (int)System.Math.Round(ex) + dx, y = (int)System.Math.Round(ey) + dy;
                    if (!r.InLand(x, y) || !r.ExitClear(x, y)) continue;
                    foreach (var seed in new ulong[] { 1, 42, 9001, 123456789 })
                        Assert.IsTrue(r.FixedOpen(seed, x, y, out var open) && open, $"level {level + 1}: no tree at the gate's exit ({x}, {y})");
                }
            }
            Assert.AreEqual(Color.white, road.LevelTint(0), "Meadow's Edge is as it was");
            Assert.AreNotEqual(Color.white, road.LevelTint(2), "Hollow Wood's land has its own tint");
        }

        [Test]
        public void ABridgeIsBuiltFromStockAndRemembered()
        {
            var g = Boot();
            var camp = g.Camp;
            camp.DebugJumpTo(2);   // level 3, Hollow Wood: its gate is the ravine
            var ridge = camp.Ridge;
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
        public void TheDebugJumpPutsTheCompanyAtALaterCampAsIfItHadGotThere()
        {
            var g = Boot();
            g.Camp.DebugJumpTo(4);
            Assert.AreEqual(4, g.Camp.Index);
            Assert.AreEqual("Crown Highlands", g.Camp.Name);
            for (var c = 1; c <= 4; c++) Assert.IsTrue(g.Jobs.Board.SummonsAnswered(c), $"summons {c} answered on the way");
            Assert.IsFalse(g.Jobs.Board.SummonsAnswered(5), "this camp's own summons is still to answer");
            Assert.IsTrue(g.Camp.IsBuilt(2) && g.Camp.IsBuilt(3), "the bridge and causeway behind are built");
            g.Camp.DebugJumpTo(0);
            Assert.AreEqual(0, g.Camp.Index, "and back to camp 1");
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
