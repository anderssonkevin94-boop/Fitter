using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Production;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    public sealed class ProductionTests
    {
        private LogisticsSystem _log;
        private ProductionSystem _prod;
        private const string Pad = "pad.forge";

        private static List<KeyValuePair<string, int>> Costs(params (string id, int q)[] c) =>
            c.Select(x => new KeyValuePair<string, int>(x.id, x.q)).ToList();

        [SetUp]
        public void Make()
        {
            _log = new LogisticsSystem(new LogisticsState(), new FakeRules(), new LogisticsTuning());
            _log.RegisterContainer(Pad, ContainerKind.OutputPad, 0f, 0f, 0.45f);
            _log.RegisterContainer("staging", ContainerKind.RawPile, 5f, 5f, 0.45f);
            var processes = new[]
            {
                new ProcessRecipe { Id = "process.smelt_iron", Name = "Smelt iron", Facility = "station.smelter", Inputs = Costs(("ore", 2)), Output = "ingot", DurationMs = 4000 },
                new ProcessRecipe { Id = "process.smelt_copper", Name = "Smelt copper", Facility = "station.smelter", Inputs = Costs(("copper", 2)), Output = "copper_ingot", DurationMs = 4000 },
                new ProcessRecipe { Id = "process.steel", Name = "Make steel", Facility = "station.smelter", MinLevel = 2, Inputs = Costs(("ore", 2), ("coal", 1)), Output = "steel", DurationMs = 6000 },
            };
            var recipes = new[]
            {
                new CraftRecipe { Id = "recipe.sword", Station = "station.forge", Inputs = Costs(("ingot", 3), ("lumber", 1)), OutputItemId = "item.sword", BlueprintId = "bp.sword", PadContainerId = Pad, PadCapacity = 6 },
                new CraftRecipe { Id = "recipe.steel_sword", Station = "station.forge", MinLevel = 2, Inputs = Costs(("steel", 4)), OutputItemId = "item.steel_sword", BlueprintId = "bp.steel", PadContainerId = Pad, PadCapacity = 6 },
            };
            _prod = new ProductionSystem(new ProductionState(), _log, processes, recipes);
            _prod.GrantBlueprint("bp.sword");
            _prod.EnsureProcessor("station.smelter");
        }

        private int Shared(string id) => _log.SharedAvailable(id);

        [Test]
        public void CraftPaysAtTheStartAndFinishesOnTime()
        {
            _log.ProduceShared("ingot", 6, "test");
            _log.ProduceShared("lumber", 6, "test");
            Assert.IsTrue(_prod.Craft("recipe.sword", out var f), f);
            Assert.AreEqual(3, Shared("ingot"), "paid at the start");
            Assert.AreEqual(5, Shared("lumber"));
            Assert.AreEqual(0, _log.State.Containers[Pad].Items.Count, "being made (the crafting expansion: timed, Shop Titans style)");
            Assert.AreEqual(1, _prod.BusySlots("station.forge"));
            _prod.Advance(_prod.EffectiveCraftMs(_prod.Recipe("recipe.sword")) - 1);
            Assert.AreEqual(0, _log.State.Containers[Pad].Items.Count, "not quite yet");
            _prod.Advance(1);
            Assert.AreEqual(1, _log.State.Containers[Pad].Items.Count, "done: on the pad");
            Assert.AreEqual("item.sword", _log.State.ItemDefinitions[_log.State.Containers[Pad].Items[0]]);
            Assert.AreEqual(0, _prod.BusySlots("station.forge"), "the slot is free again");
            CollectionAssert.IsEmpty(_log.Audit());
        }

        [Test]
        public void AStationHasTwoCraftSlotsAndOneMorePerLevel()
        {
            _log.ProduceShared("ingot", 30, "test");
            _log.ProduceShared("lumber", 30, "test");
            Assert.AreEqual(2, _prod.CraftSlots("station.forge"));
            Assert.IsTrue(_prod.Craft("recipe.sword", out _));
            Assert.IsTrue(_prod.Craft("recipe.sword", out _), "two at once");
            Assert.IsFalse(_prod.Craft("recipe.sword", out var busy));
            StringAssert.Contains("busy", busy);
            _prod.SetLevel("station.forge", 2);
            Assert.AreEqual(3, _prod.CraftSlots("station.forge"));
            Assert.IsTrue(_prod.Craft("recipe.sword", out _), "a third slot at level 2");
        }

        [Test]
        public void RepeatedTapsNeverOverspend()
        {
            _log.ProduceShared("ingot", 7, "test");
            _log.ProduceShared("lumber", 7, "test");
            var ok = 0;
            for (var i = 0; i < 10; i++) if (_prod.Craft("recipe.sword", out _)) ok++;
            Assert.AreEqual(2, ok);
            Assert.AreEqual(1, Shared("ingot"));
            Assert.AreEqual(5, Shared("lumber"));
            StringAssert.Contains("busy", _prod.CraftBlockedReason("recipe.sword"), "both slots in use");
            _prod.DebugFinishCrafts();
            StringAssert.Contains("Missing 2", _prod.CraftBlockedReason("recipe.sword"));
            CollectionAssert.IsEmpty(_log.Audit());
        }

        [Test]
        public void AFinishedCraftWaitsInItsSlotWhileThePadIsFull()
        {
            _log.ProduceShared("ingot", 30, "test");
            _log.ProduceShared("lumber", 30, "test");
            for (var round = 0; round < 3; round++)
            {
                Assert.IsTrue(_prod.Craft("recipe.sword", out _));
                Assert.IsTrue(_prod.Craft("recipe.sword", out _));
                _prod.Advance(_prod.EffectiveCraftMs(_prod.Recipe("recipe.sword")));
            }
            Assert.AreEqual(6, _log.State.Containers[Pad].Items.Count, "the pad is full");
            Assert.IsTrue(_prod.Craft("recipe.sword", out _), "a full pad does not stop a start");
            _prod.Advance(_prod.EffectiveCraftMs(_prod.Recipe("recipe.sword")));
            Assert.AreEqual(6, _log.State.Containers[Pad].Items.Count);
            Assert.IsTrue(_prod.CraftsAt("station.forge").Single().Waiting, "done, waiting for room");
            // Carry the goods away: the waiting sword lands at once.
            for (var i = 0; i < 20; i++) _log.StepOnce(0f, 0f);
            _prod.Advance(0);
            Assert.AreEqual(0, _prod.BusySlots("station.forge"));
            Assert.Greater(_log.State.Containers[Pad].Items.Count, 0);
            CollectionAssert.IsEmpty(_log.Audit());
        }

        [Test]
        public void BlueprintAndLevelGatesExplainThemselves()
        {
            _log.ProduceShared("steel", 8, "test");
            StringAssert.Contains("Blueprint not owned", _prod.CraftBlockedReason("recipe.steel_sword"));
            _prod.GrantBlueprint("bp.steel");
            StringAssert.Contains("level 2", _prod.CraftBlockedReason("recipe.steel_sword"));
            _prod.SetLevel("station.forge", 2);
            Assert.IsNull(_prod.CraftBlockedReason("recipe.steel_sword"));
            Assert.IsTrue(_prod.Craft("recipe.steel_sword", out _));
            _prod.GrantBlueprint("bp.steel");   // blueprints are permanent and never consumed
            Assert.IsTrue(_prod.State.Blueprints.Contains("bp.steel"));
        }

        [Test]
        public void ProcessingDebitsAtStartAndCreditsAtCompletion()
        {
            _log.ProduceShared("ore", 5, "test");
            _prod.Advance(0);
            Assert.AreEqual(3, Shared("ore"), "inputs are debited when the batch starts");
            _prod.Advance(3999);
            Assert.AreEqual(0, Shared("ingot"));
            _prod.Advance(1);
            Assert.AreEqual(1, Shared("ingot"));
            Assert.AreEqual(1, Shared("ore"), "the next batch started at once");
            _prod.Advance(4000);
            Assert.AreEqual(2, Shared("ingot"));
            StringAssert.Contains("Missing 1× ore", _prod.BlockedReason("station.smelter"));
            CollectionAssert.IsEmpty(_log.Audit());
        }

        [Test]
        public void ProcessingIsIndependentOfFrameSizes()
        {
            _log.ProduceShared("ore", 41, "test");
            var other = new LogisticsSystem(new LogisticsState(), new FakeRules(), new LogisticsTuning());
            other.ProduceShared("ore", 41, "test");
            var p2 = new ProductionSystem(new ProductionState(), other, _prod.Processes.ToList(), _prod.Recipes.ToList());
            p2.EnsureProcessor("station.smelter");
            _prod.Advance(61_000);
            var rnd = new Random(5);
            long t = 0;
            while (t < 61_000) { var step = Math.Min(61_000 - t, rnd.Next(1, 90)); p2.Advance(step); t += step; }
            Assert.AreEqual(Shared("ingot"), other.SharedAvailable("ingot"));
            Assert.AreEqual(Shared("ore"), other.SharedAvailable("ore"));
            Assert.AreEqual(_prod.State.Processors["station.smelter"].EndMs, p2.State.Processors["station.smelter"].EndMs);
        }

        [Test]
        public void SelectionChangeAppliesAfterTheCurrentBatch()
        {
            _log.ProduceShared("ore", 4, "test");
            _log.ProduceShared("copper", 4, "test");
            _prod.Advance(0);
            Assert.IsTrue(_prod.SelectProcess("station.smelter", "process.smelt_copper"));
            var p = _prod.State.Processors["station.smelter"];
            Assert.AreEqual("process.smelt_iron", p.InFlight, "no silent switch mid-batch");
            Assert.AreEqual("process.smelt_copper", p.Queued);
            _prod.Advance(4000);
            Assert.AreEqual(1, Shared("ingot"));
            Assert.AreEqual("process.smelt_copper", p.InFlight);
            Assert.AreEqual(2, Shared("ore"), "iron ore no longer consumed after the switch");
        }

        [Test]
        public void ReserveIsLeftUntouched()
        {
            _log.ProduceShared("ore", 5, "test");
            _prod.SetReserve("station.smelter", "ore", 4);
            _prod.Advance(20_000);
            Assert.AreEqual(5, Shared("ore"));
            StringAssert.Contains("reserving 4", _prod.BlockedReason("station.smelter"));
            _prod.SetReserve("station.smelter", "ore", 1);
            _prod.Advance(20_000);
            Assert.AreEqual(1, Shared("ore"));
            Assert.AreEqual(2, Shared("ingot"));
        }

        [Test]
        public void ProcessingNeverTouchesUndeliveredStaging()
        {
            _log.CreateMaterialsInContainer("staging", "ore", 20);
            _prod.Advance(60_000);
            Assert.AreEqual(0, Shared("ingot"));
            Assert.AreEqual(20, _log.State.Containers["staging"].UnitCount);
        }

        [Test]
        public void LockedProcessDoesNotRun()
        {
            _log.ProduceShared("ore", 4, "test");
            _log.ProduceShared("coal", 2, "test");
            Assert.IsTrue(_prod.SelectProcess("station.smelter", "process.steel"));
            _prod.Advance(10_000);
            Assert.AreEqual(0, Shared("steel"));
            StringAssert.Contains("level 2", _prod.BlockedReason("station.smelter"));
        }
    }

    public sealed class ProductionPersistenceTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            // Staging piles are not registered here, so workers pause on "staging full" and never touch shared stock.
            foreach (var st in cat.Stations)
                if (!string.IsNullOrEmpty(st.OutputPadContainerId))
                    g.Logistics.RegisterContainer(st.OutputPadContainerId, ContainerKind.OutputPad, 0f, 50f, 0.45f, null);
            return g;
        }

        [Test]
        public void NewCompanyGetsTheGddStartingState()
        {
            var g = Boot();
            var log = g.Logistics.System;
            Assert.AreEqual(6, log.SharedAvailable("resource.iron_ingot"));
            Assert.AreEqual(6, log.SharedAvailable("resource.cedar_lumber"));
            Assert.AreEqual(1, log.SharedAvailable("resource.copper_ingot"));
            Assert.AreEqual(2, log.SharedAvailable("resource.herb"));
            var bps = g.Production.System.State.Blueprints;
            Assert.AreEqual(18, bps.Count, "the six GDD starter blueprints, the helmets and shields, the light armour and the food (the crafting expansion)");
            Assert.IsFalse(bps.Contains("blueprint.copper_ward_staff"));
            // The starting stock is enough for an early loadout: a sword and a coat... not both plus a bow.
            Assert.IsTrue(g.Production.Craft("recipe.iron_sword", out var f), f);
            Assert.IsTrue(g.Production.Craft("recipe.riveted_coat", out f), f);
            CollectionAssert.IsEmpty(log.Audit());
        }

        [Test]
        public void PartialBatchResumesAfterReloadAndCompletesOnce()
        {
            var a = Boot();
            a.Logistics.System.ProduceShared("resource.iron_ore", 2, "test delivery");
            a.Production.Tick(2.0f);
            var p = a.Production.System.State.Processors["station.smelter"];
            Assert.AreEqual("process.smelt_iron", p.InFlight);
            a.Scheduler.SaveNow("test");

            var b = Boot();
            var pb = b.Production.System.State.Processors["station.smelter"];
            Assert.AreEqual("process.smelt_iron", pb.InFlight, "the in-flight batch was saved");
            Assert.AreEqual(0, b.Logistics.System.SharedAvailable("resource.iron_ore"), "inputs are not refunded or re-debited");
            b.Production.Tick(1.9f);
            Assert.AreEqual(6, b.Logistics.System.SharedAvailable("resource.iron_ingot"));
            b.Production.Tick(0.2f);
            Assert.AreEqual(7, b.Logistics.System.SharedAvailable("resource.iron_ingot"));
            b.Production.Tick(30f);
            Assert.AreEqual(7, b.Logistics.System.SharedAvailable("resource.iron_ingot"), "completed exactly once");
            CollectionAssert.IsEmpty(b.Logistics.System.Audit());
        }

        [Test]
        public void CraftedItemsAndBlueprintsSurviveReload()
        {
            var a = Boot();
            Assert.IsTrue(a.Production.Craft("recipe.iron_dagger", out _));
            a.Production.DebugGrantBlueprint("blueprint.copper_ward_staff");
            var b = Boot();
            Assert.AreEqual(1, b.Production.System.BusySlots("station.forge"), "the craft in progress was saved");
            Assert.AreEqual(0, b.Logistics.System.ContainerUnitCount("container.forge_pad"));
            b.Production.AdvanceMs(b.Production.System.EffectiveCraftMs(b.Production.System.Recipe("recipe.iron_dagger")));
            Assert.AreEqual(1, b.Logistics.System.ContainerUnitCount("container.forge_pad"), "and finishes after the restart");
            Assert.IsTrue(b.Production.System.State.Blueprints.Contains("blueprint.copper_ward_staff"));
            Assert.AreEqual(4, b.Logistics.System.SharedAvailable("resource.iron_ingot"), "starting stock is not granted twice");
            CollectionAssert.IsEmpty(b.Logistics.System.Audit());
        }

        [Test]
        public void V4FixtureMigratesToV5()
        {
            var json = File.ReadAllText("Assets/Ravenholm/Tests/Fixtures/save_v4_fixture.json");
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(4, from);
            Assert.AreEqual(SaveEnvelope.CurrentSchemaVersion, env.SchemaVersion);
            Assert.IsNull(env.Production);
            Assert.IsNotNull(env.Logistics.MaterialsConsumed);
            Assert.AreEqual(1, env.Sites.Count);
        }

        [Test]
        public void ProjectRecipesMatchTheGddStarterCatalogue()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            string Cost(string recipe) => string.Join(",", cat.Get<RecipeDefinition>(recipe).Cost.Select(c => $"{c.resource.Id.Substring(9)}:{c.quantity}"));
            Assert.AreEqual("iron_ingot:3,cedar_lumber:1", Cost("recipe.iron_sword"));
            Assert.AreEqual("cedar_lumber:3,iron_ingot:1", Cost("recipe.cedar_bow"));
            Assert.AreEqual("iron_ingot:2,cedar_lumber:1", Cost("recipe.iron_dagger"));
            Assert.AreEqual("cedar_lumber:3,copper_ingot:1", Cost("recipe.cedar_staff"));
            Assert.AreEqual("iron_ingot:3,cedar_lumber:2", Cost("recipe.riveted_coat"));
            Assert.AreEqual("herb:2", Cost("recipe.recovery_tonic"));
            Assert.AreEqual(4f, cat.Get<ProcessDefinition>("process.smelt_iron").BatchSeconds);
            Assert.AreEqual(6f, cat.Get<ProcessDefinition>("process.make_steel").BatchSeconds);
            Assert.AreEqual(2, cat.Get<ProcessDefinition>("process.make_steel").MinFacilityLevel);
        }
    }
}
