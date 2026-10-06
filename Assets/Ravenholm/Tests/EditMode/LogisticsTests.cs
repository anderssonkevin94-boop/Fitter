using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Time;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Presentation;
using Ravenholm.Services;
using UnityEngine;

namespace Ravenholm.Tests
{
    /// <summary>Simple acceptance table for domain tests (no ScriptableObjects needed).</summary>
    public sealed class FakeRules : IAcceptanceRules
    {
        public bool Accepts(ReceiverKind receiver, CarryUnit unit, string itemDefinitionId)
        {
            if (unit.Kind == UnitKind.Item) return receiver == ReceiverKind.Store && itemDefinitionId != null;
            switch (unit.Id)
            {
                case "resource.ore": case "resource.gem": return receiver == ReceiverKind.Smelter;
                case "resource.log": return receiver == ReceiverKind.Lumbermill;
                case "resource.herb": return receiver == ReceiverKind.Apothecary;
                default: return false;
            }
        }
    }

    public sealed class LogisticsTests
    {
        private LogisticsSystem _sys;
        private const float Step = 0.08f;

        [SetUp]
        public void Make()
        {
            _sys = new LogisticsSystem(new LogisticsState(), new FakeRules(), new LogisticsTuning());
            _sys.RegisterContainer("pile.a", ContainerKind.RawPile, 0f, 0f, 0.45f);
            _sys.RegisterReceiver("rx.smelter", ReceiverKind.Smelter, 10f, 0f, 0.6f);
            _sys.RegisterReceiver("rx.mill", ReceiverKind.Lumbermill, 20f, 0f, 0.6f);
            _sys.RegisterReceiver("rx.store", ReceiverKind.Store, 30f, 0f, 0.6f);
        }

        private void Hold(float x, float y, float seconds)
        {
            for (var t = 0f; t < seconds; t += 1f / 60f) _sys.Tick(1f / 60f, x, y);
        }

        [Test]
        public void PicksUpOnlyWithinReach()
        {
            _sys.CreateMaterialsInContainer("pile.a", "resource.ore", 5);
            Hold(0f, 1.3f, 1f);   // 1.3 > 0.45 + 0.8
            Assert.AreEqual(0, _sys.CarryCount);
            Hold(0f, 1.2f, 1f);
            Assert.AreEqual(5, _sys.CarryCount);
            Assert.IsEmpty(_sys.Audit());
        }

        [Test]
        public void CadenceIsOneUnitPerInterval()
        {
            _sys.CreateMaterialsInContainer("pile.a", "resource.ore", 20);
            _sys.Tick(Step * 3 + 0.001f, 0f, 0f);
            Assert.AreEqual(3, _sys.CarryCount);
            _sys.Tick(Step * 0.5f, 0f, 0f);
            Assert.AreEqual(3, _sys.CarryCount, "no transfer before the interval elapses");
            _sys.Tick(1f, 0f, 0f);   // a long frame batches transfers without skipping any
            Assert.AreEqual(12, _sys.CarryCount);
        }

        [Test]
        public void CapacityNeverExceededAndFullWarnsOncePerApproach()
        {
            _sys.CreateMaterialsInContainer("pile.a", "resource.ore", 40);
            var warnings = 0;
            _sys.CapacityBlocked += _ => warnings++;
            Hold(0f, 0f, 5f);
            Assert.AreEqual(12, _sys.CarryCount);
            Assert.AreEqual(1, warnings, "one warning, not one per frame");
            Assert.AreEqual(28, _sys.State.Containers["pile.a"].Materials["resource.ore"]);

            Hold(5f, 0f, 0.5f);   // walk away
            Hold(0f, 0f, 1f);     // come back while still full
            Assert.AreEqual(2, warnings, "a new approach may warn again");
            Assert.AreEqual(12, _sys.CarryCount);
        }

        [Test]
        public void ResumesPickupWhenSpaceOpens()
        {
            _sys.CreateMaterialsInContainer("pile.a", "resource.ore", 30);
            Hold(0f, 0f, 2f);
            Hold(10f, 0f, 0.3f);  // deliver a few at the smelter (cadence-limited)
            var afterDelivery = _sys.CarryCount;
            Assert.Less(afterDelivery, 12);
            Hold(0f, 0f, 2f);
            Assert.AreEqual(12, _sys.CarryCount);
        }

        [Test]
        public void WrongReceiverKeepsGoodsCarried()
        {
            _sys.CreateMaterialsInContainer("pile.a", "resource.ore", 2);
            _sys.CreateMaterialsInContainer("pile.a", "resource.herb", 2);
            Hold(0f, 0f, 1f);
            Assert.AreEqual(4, _sys.CarryCount);

            Hold(20f, 0f, 1f);    // lumbermill accepts neither
            Assert.AreEqual(4, _sys.CarryCount);
            Hold(10f, 0f, 1f);    // smelter takes only the ore
            Assert.AreEqual(2, _sys.CarryCount);
            Assert.IsTrue(_sys.State.Carry.All(u => u.Id == "resource.herb"));
            Assert.AreEqual(2, _sys.State.SharedMaterials["resource.ore"]);
            Assert.IsEmpty(_sys.Audit());
        }

        [Test]
        public void DeliveryTakesMostRecentAcceptedUnit()
        {
            _sys.State.Carry.Add(CarryUnit.Material("resource.herb"));
            _sys.State.Carry.Add(CarryUnit.Material("resource.ore"));
            _sys.State.Carry.Add(CarryUnit.Material("resource.herb"));
            _sys.State.MaterialsCreated["resource.herb"] = 2;
            _sys.State.MaterialsCreated["resource.ore"] = 1;
            Assert.AreEqual(TransferResult.Delivered, _sys.StepOnce(10f, 0f));
            CollectionAssert.AreEqual(new[] { "resource.herb", "resource.herb" }, _sys.State.Carry.Select(u => u.Id));
        }

        [Test]
        public void OutputPadWinsNearTieThenStableId()
        {
            _sys.RegisterContainer("pile.b", ContainerKind.RawPile, 1f, 0f, 0.45f);
            _sys.RegisterContainer("pad.z", ContainerKind.OutputPad, -1f, 0f, 0.45f);
            _sys.CreateMaterialsInContainer("pile.b", "resource.ore", 1);
            _sys.CreateItemInContainer("pad.z", "item.sword");
            Assert.AreEqual(TransferResult.PickedUp, _sys.StepOnce(0.01f, 0f));   // pile.b is 0.02 closer, pad still wins
            Assert.AreEqual(UnitKind.Item, _sys.State.Carry[0].Kind);

            _sys.RegisterContainer("pile.c", ContainerKind.RawPile, 0f, 1f, 0.45f);
            _sys.RegisterContainer("pile.d", ContainerKind.RawPile, 0f, -1f, 0.45f);
            _sys.CreateMaterialsInContainer("pile.c", "resource.log", 1);
            _sys.CreateMaterialsInContainer("pile.d", "resource.herb", 1);
            Assert.IsTrue(_sys.HasEligiblePickup(0f, 0f, out var best));
            Assert.AreEqual("pile.b", best.Id, "equal distances break ties by stable ID");
        }

        [Test]
        public void ItemsKeepTheirIdentityThroughTheLoop()
        {
            _sys.RegisterContainer("pad.forge", ContainerKind.OutputPad, 0f, 0f, 0.45f);
            var id = _sys.CreateItemInContainer("pad.forge", "item.sword");
            Hold(0f, 0f, 0.5f);
            Hold(10f, 0f, 0.5f);   // smelter refuses items
            Assert.AreEqual(1, _sys.CarryCount);
            Hold(30f, 0f, 0.5f);
            CollectionAssert.AreEqual(new[] { id }, _sys.State.StoreItems);
            Assert.AreEqual("item.sword", _sys.State.ItemDefinitions[id]);
            Assert.IsEmpty(_sys.Audit());
        }

        [Test]
        public void HundredPickupDeliveryCyclesConserveEverything()
        {
            _sys.RegisterContainer("pad.forge", ContainerKind.OutputPad, 0f, 3f, 0.45f);
            var rnd = new System.Random(7);
            long createdOre = 0, createdLog = 0;
            var createdItems = 0;
            for (var cycle = 0; cycle < 100; cycle++)
            {
                var ore = rnd.Next(0, 9); var log = rnd.Next(0, 6); var items = rnd.Next(0, 3);
                _sys.CreateMaterialsInContainer("pile.a", "resource.ore", ore); createdOre += ore;
                _sys.CreateMaterialsInContainer("pile.a", "resource.log", log); createdLog += log;
                for (var i = 0; i < items; i++) _sys.CreateItemInContainer("pad.forge", "item.sword");
                createdItems += items;

                Hold(0f, 0f, rnd.Next(1, 20) * 0.08f);             // partial or full pickups
                Hold(0f, 3f, rnd.Next(1, 10) * 0.08f);
                Assert.LessOrEqual(_sys.CarryCount, 12);
                var stop = rnd.Next(4);
                if (stop == 0) Hold(10f, 0f, 1.2f);
                else if (stop == 1) Hold(20f, 0f, 1.2f);
                else if (stop == 2) Hold(30f, 0f, 1.2f);
                else Hold(5f, 5f, 0.5f);                           // wander off still carrying
                CollectionAssert.IsEmpty(_sys.Audit(), $"cycle {cycle}");
            }

            // Drain everything and check final totals.
            for (var i = 0; i < 40; i++) { Hold(0f, 0f, 1.2f); Hold(0f, 3f, 1.2f); Hold(10f, 0f, 1.2f); Hold(20f, 0f, 1.2f); Hold(30f, 0f, 1.2f); }
            Assert.AreEqual(0, _sys.CarryCount);
            Assert.AreEqual(createdOre, _sys.State.SharedMaterials.TryGetValue("resource.ore", out var o) ? o : 0);
            Assert.AreEqual(createdLog, _sys.State.SharedMaterials.TryGetValue("resource.log", out var l) ? l : 0);
            Assert.AreEqual(createdItems, _sys.State.StoreItems.Count);
            CollectionAssert.IsEmpty(_sys.Audit());
        }

        [Test]
        public void AuditDetectsDuplicatedItemAndPhantomMaterial()
        {
            _sys.RegisterContainer("pad.forge", ContainerKind.OutputPad, 0f, 3f, 0.45f);
            var id = _sys.CreateItemInContainer("pad.forge", "item.sword");
            _sys.State.StoreItems.Add(id);                          // second owner
            _sys.State.SharedMaterials["resource.ore"] = 3;         // never created
            var problems = _sys.Audit();
            Assert.IsTrue(problems.Any(p => p.Contains("2 owners")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("resource.ore")), string.Join("\n", problems));
        }

        [Test]
        public void DebugUnloadKeepsOwnershipIntact()
        {
            _sys.CreateMaterialsInContainer("pile.a", "resource.ore", 3);
            Hold(0f, 0f, 1f);
            Assert.AreEqual(3, _sys.DebugUnloadCarry());
            Assert.AreEqual(0, _sys.CarryCount);
            Assert.IsEmpty(_sys.Audit());
        }
    }

    public sealed class LogisticsPersistenceTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

        private static (SandboxSession sandbox, LogisticsService logistics, SaveScheduler scheduler) Boot(string dir)
        {
            var c = new SaveCoordinator(new ManualClock(T0), new SaveStore(dir), "sandbox", "test", 1);
            var scheduler = new SaveScheduler(c, 1.5f);
            var sandbox = new SandboxSession(scheduler);
            var logistics = new LogisticsService(scheduler, new FakeRules(), new LogisticsTuning());
            sandbox.Start();
            logistics.RegisterReceiver("rx.smelter", ReceiverKind.Smelter, 10f, 0f, 0.6f);
            return (sandbox, logistics, scheduler);
        }

        private static ContainerFixture Fixture()
        {
            var f = new ContainerFixture();
            f.Materials.Add(new KeyValuePair<string, int>("resource.ore", 20));
            f.ItemDefinitions.Add("item.sword");
            return f;
        }

        [Test]
        public void RestartPreservesCarryAndPilesWithoutDuplicates()
        {
            var (_, logistics, scheduler) = Boot(Dir);
            logistics.RegisterContainer("pile.a", ContainerKind.RawPile, 0f, 0f, 0.45f, Fixture());
            var sys = logistics.System;
            for (var i = 0; i < 7; i++) sys.StepOnce(0f, 0f);      // sword first, then 6 ore
            sys.StepOnce(10f, 0f);                                  // deliver one ore
            scheduler.Flush("quit");

            var (_, again, _) = Boot(Dir);
            again.RegisterContainer("pile.a", ContainerKind.RawPile, 0f, 0f, 0.45f, Fixture());   // must NOT reseed
            var s2 = again.System.State;
            Assert.AreEqual(6, s2.Carry.Count);
            Assert.AreEqual(1, s2.Carry.Count(u => u.Kind == UnitKind.Item));
            Assert.AreEqual(14, s2.Containers["pile.a"].Materials["resource.ore"]);   // 20 - 6 picked
            Assert.AreEqual(0, s2.Containers["pile.a"].Items.Count);
            Assert.AreEqual(1, s2.SharedMaterials["resource.ore"]);
            Assert.AreEqual(1, s2.ItemDefinitions.Count, "no duplicate item instances");
            CollectionAssert.IsEmpty(again.System.Audit());
        }

        [Test]
        public void FreshProfileSeedsFixturesOnceAndRefillTopsUp()
        {
            var (_, logistics, _) = Boot(Dir);
            logistics.RegisterContainer("pile.a", ContainerKind.RawPile, 0f, 0f, 0.45f, Fixture());
            var sys = logistics.System;
            Assert.AreEqual(21, sys.State.Containers["pile.a"].UnitCount);
            for (var i = 0; i < 5; i++) sys.StepOnce(0f, 0f);
            logistics.RefillFixtures();
            Assert.AreEqual(21, sys.State.Containers["pile.a"].UnitCount, "refill tops up to the fixture, not beyond");
            // Picked: the sword plus 4 ore. Refill created exactly those again.
            Assert.AreEqual(24, sys.State.MaterialsCreated["resource.ore"]);
            Assert.AreEqual(2, sys.State.ItemDefinitions.Count);
            Assert.AreEqual(5, sys.CarryCount, "refill never touches the carry");
            CollectionAssert.IsEmpty(sys.Audit());
        }

        [Test]
        public void ProfileResetClearsGoodsAndReseeds()
        {
            var (sandbox, logistics, _) = Boot(Dir);
            logistics.RegisterContainer("pile.a", ContainerKind.RawPile, 0f, 0f, 0.45f, Fixture());
            logistics.System.StepOnce(0f, 0f);
            sandbox.ResetProfile();
            Assert.AreEqual(0, logistics.System.CarryCount);
            Assert.AreEqual(21, logistics.System.State.Containers["pile.a"].UnitCount);
            CollectionAssert.IsEmpty(logistics.System.Audit());
        }

        [Test]
        public void V2FixtureMigratesToV3()
        {
            var json = File.ReadAllText("Assets/Ravenholm/Tests/Fixtures/save_v2_fixture.json");
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(2, from);
            Assert.AreEqual(3, env.SchemaVersion);
            Assert.IsNull(env.Logistics, "Phase 0 saves have no goods; fixtures seed on first load");
            Assert.AreEqual(4, env.Sandbox.BellRingCount);
            Assert.AreEqual(12, env.SaveGeneration);
        }
    }

    public sealed class FloatingJoystickTests
    {
        private static FloatingJoystick Stick() => new FloatingJoystick { DeadZone = 8f, Radius = 60f };

        [Test]
        public void DeadZoneGivesZero()
        {
            var j = Stick();
            j.Begin(new Vector2(100, 100));
            Assert.AreEqual(Vector2.zero, j.Move(new Vector2(105, 103)));
        }

        [Test]
        public void FullRadiusGivesUnitMagnitudeInAnyDirection()
        {
            foreach (var dir in new[] { Vector2.up, Vector2.right, new Vector2(1, 1).normalized, new Vector2(-1, -1).normalized })
            {
                var j = Stick();
                j.Begin(Vector2.zero);
                var v = j.Move(dir * 200f);
                Assert.AreEqual(1f, v.magnitude, 1e-4f, $"diagonals are normalized ({dir})");
                Assert.Greater(Vector2.Dot(v, dir), 0.999f);
            }
        }

        [Test]
        public void ScreenUpMeansWorldUp()
        {
            var j = Stick();
            j.Begin(new Vector2(200, 200));
            Assert.Greater(j.Move(new Vector2(200, 300)).y, 0.9f);
        }

        [Test]
        public void AnchorTrailsSoReversingIsImmediate()
        {
            var j = Stick();
            j.Begin(Vector2.zero);
            j.Move(new Vector2(300, 0));
            Assert.AreEqual(240f, j.Anchor.x, 1e-3f);
            Assert.Less(j.Move(new Vector2(230, 0)).x, 0f, "a short move back already points left");
        }

        [Test]
        public void ResetAlwaysReturnsZero()
        {
            var j = Stick();
            j.Begin(Vector2.zero);
            j.Move(new Vector2(0, 80));
            j.Reset();
            Assert.IsFalse(j.Active);
            Assert.AreEqual(Vector2.zero, j.Output);
            Assert.AreEqual(Vector2.zero, j.Move(new Vector2(0, 200)), "moves after reset do nothing until a new Begin");
        }
    }
}
