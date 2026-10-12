using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Production;
using Ravenholm.Domain.Sites;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>Phase 6: offline catch-up, the Trader, upgrades and worker equipment, recovery.</summary>
    public sealed class EconomyTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private const long Minute = 60_000;
        private const long Hour = 60 * Minute;

        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");

        private GameServices Boot(DateTime now, string dir = null)
        {
            var g = new GameServices(new ManualClock(now), Cat, Balance, dir ?? Dir, "sandbox", "test", 1.5f);
            g.Start();
            g.Logistics.RegisterContainer("test.box", ContainerKind.OutputPad, 100f, 100f, 0.45f, null);
            return g;
        }

        private static List<string> Stock(GameServices g, params string[] itemIds)
        {
            var log = g.Logistics.System;
            var ids = itemIds.Select(i => log.CreateItemInContainer("test.box", i)).ToList();
            for (var i = 0; i < 20; i++) log.StepOnce(100f, 100f);
            log.DebugUnloadCarry();
            return ids;
        }

        /// <summary>Stocks a starter kit (applicants refuse ordinary jobs with an empty Store) and sends the first applicant with their best gear.</summary>
        private static ExpeditionRecord SendPatrol(GameServices g)
        {
            Stock(g, "item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff");
            Assert.IsTrue(g.Jobs.Post("quest.roadside_patrol", out var f), f);
            g.Jobs.Board.Advance(31_000);
            var a = g.Jobs.State.Posting.Applicants.FirstOrDefault();
            Assert.NotNull(a, g.Jobs.State.Posting.NoApplicantReason);
            var c = g.Jobs.Board.Contractor(a.ContractorId);
            Assert.IsTrue(g.Jobs.Dispatch(c.Id, g.Jobs.Board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner), out f, out var e), f);
            return e;
        }

        private static JObject Saved(GameServices g)
        {
            Assert.IsTrue(g.Scheduler.SaveNow("test snapshot"));
            Assert.IsTrue(SaveFileCodec.TryDecode(File.ReadAllText(g.Saves.Store.PrimaryPath), out var json, out var err), err);
            return JObject.Parse(json);
        }

        private static void GrantMaterials(GameServices g, params (string id, int q)[] m)
        {
            foreach (var (id, q) in m) g.Logistics.System.ProduceShared("resource." + id, q, "test grant");
        }

        // ------------------------------------------------------------------ offline catch-up

        [Test]
        public void OfflineCatchUpMatchesOnlinePlayForTheSameInterval()
        {
            // One starting point, saved, copied to two profile folders.
            var setup = Boot(T0);
            GrantMaterials(setup, ("iron_ore", 12), ("cedar_log", 8));
            SendPatrol(setup);
            setup.Scheduler.SaveNow("fork");
            var onlineDir = Path.Combine(Dir, "online");
            var offlineDir = Path.Combine(Dir, "offline");
            foreach (var d in new[] { onlineDir, offlineDir })
            {
                Directory.CreateDirectory(d);
                File.Copy(setup.Saves.Store.PrimaryPath, Path.Combine(d, SaveStore.PrimaryName));
            }

            const long span = 12 * Minute;
            var online = Boot(T0, onlineDir);
            for (long t = 0; t < span; t += 250) online.Simulation.Advance(250, 250);   // frame-sized steps
            var offline = Boot(T0.AddMilliseconds(span), offlineDir);                      // one absence

            var a = Saved(online);
            var b = Saved(offline);
            foreach (var section in new[] { "logistics", "sites", "production", "jobs" })
                Assert.IsTrue(JToken.DeepEquals(a[section], b[section]), $"section '{section}' differs between online and offline");
            Assert.AreEqual(1, offline.Jobs.State.Results.Count, "the patrol returned during the absence");
            Assert.IsNotNull(offline.Simulation.PendingReport);
            Assert.AreEqual(1, offline.Simulation.PendingReport.Results.Count);
            CollectionAssert.IsEmpty(offline.Logistics.System.Audit());
        }

        [Test]
        public void CatchUpCapsResourcesAtEightHoursAndMakesNoDecisions()
        {
            var g = Boot(T0);
            DigTestKit.StartMiningRoutine(g);   // solo play: the supplier leaves themself mining
            var oreBefore = g.Logistics.System.SharedAvailable("resource.iron_ore");
            var sitesMs = g.Sites.Get("site.mine").Record.SimMs;
            var jobsMs = g.Jobs.State.SimMs;
            g.Scheduler.SaveNow("before");

            var back = Boot(T0.AddHours(24));
            var r = back.Simulation.PendingReport;
            Assert.IsNotNull(r);
            Assert.AreEqual(24 * Hour, r.ElapsedMs);
            Assert.AreEqual(8 * Hour, r.ResourceMs);
            Assert.IsTrue(r.Capped);
            Assert.AreEqual(sitesMs + 8 * Hour, back.Sites.Get("site.mine").Record.SimMs, "gathering stops at the cap");
            Assert.AreEqual(jobsMs + 24 * Hour, back.Jobs.State.SimMs, "expeditions and the Trader see the whole absence");

            foreach (var sim in back.Sites.Sites)
            {
                Assert.AreEqual(1, sim.Record.Depth, "catch-up never digs deeper or pushes on");
                var staging = back.Logistics.System.ContainerUnitCount(sim.Config.StagingContainerId);
                Assert.LessOrEqual(staging, sim.Config.StagingCapacity);
            }
            Assert.AreEqual(oreBefore, back.Logistics.System.SharedAvailable("resource.iron_ore"), "nothing is hauled to the facilities for you");
            Assert.AreEqual(0, back.Logistics.System.CarryCount);
            Assert.IsTrue(r.SiteBlockers.Count > 0, "after 8 hours the sites are waiting on the player");
        }

        [Test]
        public void LongAbsenceAppliesQuestResultsExactlyOnce()
        {
            var g = Boot(T0);
            var e = SendPatrol(g);
            var gold = g.Jobs.State.Gold;
            g.Scheduler.SaveNow("away");

            var back = Boot(T0.AddHours(24));
            Assert.AreEqual(1, back.Jobs.State.Results.Count(x => x.ExpeditionId == e.Id));
            var afterFirst = back.Jobs.State.Gold;
            Assert.GreaterOrEqual(afterFirst, gold);

            var again = Boot(T0.AddHours(24));          // restart with no further time
            Assert.AreEqual(1, again.Jobs.State.Results.Count(x => x.ExpeditionId == e.Id));
            Assert.AreEqual(afterFirst, again.Jobs.State.Gold);
            var later = Boot(T0.AddHours(48));
            Assert.AreEqual(1, later.Jobs.State.Results.Count(x => x.ExpeditionId == e.Id));
            Assert.AreEqual(afterFirst, later.Jobs.State.Gold, "a second absence never re-applies the reward");
            CollectionAssert.IsEmpty(later.Logistics.System.Audit());
        }

        [Test]
        public void ClockGoingBackwardsAddsNoTime()
        {
            var g = Boot(T0);
            var ms = g.Sites.Get("site.mine").Record.SimMs;
            g.Scheduler.SaveNow("before");
            var back = Boot(T0.AddHours(-3));
            Assert.AreEqual(ms, back.Sites.Get("site.mine").Record.SimMs);
            Assert.IsNull(back.Simulation.PendingReport);
        }

        // ------------------------------------------------------------------ Trader

        private static void FirstReturn(GameServices g)
        {
            var e = SendPatrol(g);
            g.Simulation.Advance(e.EndMs - g.Jobs.State.SimMs + 1, 0);
            Assert.IsTrue(g.Economy.Trader.Scheduled, "the first ordinary return schedules the Trader");
        }

        [Test]
        public void TraderArrivesAfterTheFirstOrdinaryReturnAndMissedVisitsDoNotAccumulate()
        {
            var g = Boot(T0);
            Assert.IsFalse(g.Economy.Trader.Scheduled);
            FirstReturn(g);
            var t = g.Economy.Trader;
            Assert.IsFalse(t.IsPresent(g.Economy.Now));
            g.Simulation.Advance(t.UntilNextArrival(g.Economy.Now), 0);
            Assert.IsTrue(t.IsPresent(g.Economy.Now));

            // Skip many visits: the next visit has exactly one visit's stock.
            g.Simulation.Advance(5 * Hour, 0);
            g.Simulation.Advance(t.UntilNextArrival(g.Economy.Now), 0);
            var iron = t.Offers(g.Economy.Now).Single(o => o.Entry.Key == "iron_bundle");
            Assert.AreEqual(6, iron.Remaining);
        }

        [Test]
        public void EveryVisitHasTheEmergencyBundleAndStockIsStableAcrossReload()
        {
            var g = Boot(T0);
            FirstReturn(g);
            var t = g.Economy.Trader;
            for (var v = 0; v < 40; v++)
            {
                g.Simulation.Advance(t.UntilNextArrival(g.Economy.Now), 0);
                var keys = t.Offers(g.Economy.Now).Select(o => o.Entry.Key).ToList();
                CollectionAssert.Contains(keys, "iron_bundle");
                CollectionAssert.Contains(keys, "cedar_bundle");
                CollectionAssert.DoesNotContain(keys, "arcane_dust", "dust only after the Old Shrine");
                g.Simulation.Advance(t.UntilDeparture(g.Economy.Now) + 1, 0);
            }

            g.Simulation.Advance(t.UntilNextArrival(g.Economy.Now), 0);
            g.Jobs.DebugSetGold(500);
            Assert.IsTrue(g.Economy.Buy("iron_bundle", out var f), f);
            var before = t.Offers(g.Economy.Now).Select(o => $"{o.Entry.Key}:{o.GoodsId}:{o.Remaining}").ToList();
            var reloaded = Boot(g.Clock.UtcNow);
            var after = reloaded.Economy.Trader.Offers(reloaded.Economy.Now).Select(o => $"{o.Entry.Key}:{o.GoodsId}:{o.Remaining}").ToList();
            CollectionAssert.AreEqual(before, after, "reopening or reloading never rerolls the visit");
            Assert.AreEqual(5, reloaded.Economy.Trader.Offers(reloaded.Economy.Now).Single(o => o.Entry.Key == "iron_bundle").Remaining);
        }

        [Test]
        public void BuyingIsAtomicAndSellingNeverTouchesReservedOrAwayGear()
        {
            var g = Boot(T0);
            FirstReturn(g);
            g.Economy.DebugSummonTrader();
            var log = g.Logistics.System;

            g.Jobs.DebugSetGold(2);
            var ingots = log.SharedAvailable("resource.iron_ingot");
            Assert.IsFalse(g.Economy.Buy("iron_bundle", out _));
            Assert.AreEqual(2, g.Jobs.State.Gold);
            Assert.AreEqual(ingots, log.SharedAvailable("resource.iron_ingot"));

            g.Jobs.DebugSetGold(3);
            Assert.IsTrue(g.Economy.Buy("iron_bundle", out var f), f);
            Assert.AreEqual(0, g.Jobs.State.Gold);
            Assert.AreEqual(ingots + 1, log.SharedAvailable("resource.iron_ingot"));

            var ids = Stock(g, "item.iron_sword", "item.riveted_coat", "item.iron_dagger");
            Assert.IsTrue(log.Reserve("loadout.test", new[] { ids[0] }));
            Assert.IsFalse(g.Economy.Sell(ids[0], out _), "reserved for a loadout");
            Assert.IsTrue(log.StoreItems.Contains(ids[0]));

            var value = Cat.Get<ItemDefinition>("item.riveted_coat").TraderBuyValue;
            Assert.IsTrue(g.Economy.Sell(ids[1], out f), f);
            Assert.AreEqual(value, g.Jobs.State.Gold);
            Assert.IsFalse(log.State.ItemDefinitions.ContainsKey(ids[1]), "sold goods leave the company");
            Assert.IsFalse(g.Economy.Sell(ids[1], out _), "cannot sell twice");

            // Gear on an expedition is in expedition custody, not the Store.
            log.ReleaseReservations("loadout.test");
            Assert.IsTrue(g.Jobs.Post("quest.roadside_patrol", out f), f);
            g.Jobs.Board.Advance(31_000);
            var a = g.Jobs.State.Posting.Applicants.First();
            var c = g.Jobs.Board.Contractor(a.ContractorId);
            g.Jobs.DebugSetGold(200);
            var loadout = g.Jobs.Board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner);
            Assert.IsTrue(g.Jobs.Dispatch(c.Id, loadout, out f, out var e), f);
            foreach (var item in e.Items) Assert.IsFalse(g.Economy.Sell(item, out _), "away gear cannot be sold");
            CollectionAssert.IsEmpty(log.Audit());
        }

        [Test]
        public void TraderIsAwayMeansNoTrade()
        {
            var g = Boot(T0);
            g.Jobs.DebugSetGold(100);
            Assert.IsFalse(g.Economy.Buy("iron_bundle", out var f));
            StringAssert.Contains("away", f);
        }

        // ------------------------------------------------------------------ upgrades and equipment

        private static UpgradeDefinition Up(string key) => Cat.Get<UpgradeDefinition>("upgrade." + key);

        [Test]
        public void StationUpgradeChecksPrerequisitesThenDebitsEverythingTogether()
        {
            var g = Boot(T0);
            var smelter2 = Up("smelter_2");
            g.Jobs.DebugSetGold(500);
            GrantMaterials(g, ("cedar_lumber", 20), ("iron_ingot", 20));
            StringAssert.Contains("depth 3", g.Economy.UpgradeBlockedReason(smelter2));
            Assert.IsFalse(g.Economy.BuyUpgrade(smelter2, out _));
            Assert.AreEqual(500, g.Jobs.State.Gold);

            // Test fixture: depth 3 revealed.
            var mine = g.Sites.Get("site.mine").Record;
            mine.Revealed[3] = mine.Composition;

            g.Jobs.DebugSetGold(10);
            var lumber = g.Logistics.System.SharedAvailable("resource.cedar_lumber");
            Assert.IsFalse(g.Economy.BuyUpgrade(smelter2, out var f));
            StringAssert.Contains("gold", f);
            Assert.AreEqual(lumber, g.Logistics.System.SharedAvailable("resource.cedar_lumber"), "nothing debited on failure");

            g.Jobs.DebugSetGold(60);
            var iron = g.Logistics.System.SharedAvailable("resource.iron_ingot");
            Assert.IsTrue(g.Economy.BuyUpgrade(smelter2, out f), f);
            Assert.AreEqual(10, g.Jobs.State.Gold);
            Assert.AreEqual(lumber - 8, g.Logistics.System.SharedAvailable("resource.cedar_lumber"));
            Assert.AreEqual(iron - 6, g.Logistics.System.SharedAvailable("resource.iron_ingot"));
            Assert.AreEqual(2, g.Production.System.Level("station.smelter"));
            Assert.IsFalse(g.Economy.BuyUpgrade(smelter2, out _), "built once");

            var reloaded = Boot(T0);
            Assert.AreEqual(2, reloaded.Production.System.Level("station.smelter"));
            Assert.IsTrue(reloaded.Economy.Purchased.Contains(smelter2.Id));
            Assert.IsNull(reloaded.Economy.UpgradePrerequisiteReason(Up("forge_2")), "Forge 2 now only needs its cost");
            CollectionAssert.IsEmpty(reloaded.Logistics.System.Audit());
        }

        [Test]
        public void LevelTwoSpeedsBasicBatchesButNotTheNewRecipe()
        {
            var g = Boot(T0);
            var p = g.Production.System;
            g.Production.System.SetLevel("station.smelter", 2);
            Assert.AreEqual(3400, p.EffectiveDurationMs(p.Process("process.smelt_iron")));
            Assert.AreEqual(6000, p.EffectiveDurationMs(p.Process("process.make_steel")));
        }

        [Test]
        public void SiteEquipmentUnlocksPurchasesItDoesNotGrantThem()
        {
            var g = Boot(T0);
            var eq2 = Up("mine_equipment_2");
            var iron = Cat.Get<WorkerEquipmentDefinition>("equipment.tool_iron");
            g.Jobs.DebugSetGold(500);
            GrantMaterials(g, ("cedar_lumber", 30), ("iron_ingot", 30));
            StringAssert.Contains("quest successes", g.Economy.UpgradeBlockedReason(eq2));
            StringAssert.Contains("level 2", g.Economy.PlayerEquipmentBlockedReason(iron));

            g.Jobs.State.Progress["quest.roadside_patrol"] = new QuestProgress { Attempts = 2, Successes = 2 };   // test fixture
            Assert.IsTrue(g.Economy.BuyUpgrade(eq2, out var f), f);
            Assert.AreEqual(2, g.Economy.SiteEquipmentLevel("site.mine"));
            Assert.AreEqual("equipment.tool_basic", g.Sites.PlayerToolId, "unlocking does not equip");
            Assert.AreEqual(1, g.Economy.SiteEquipmentLevel("site.forest"), "per site");

            var gold = g.Jobs.State.Gold;
            Assert.IsTrue(g.Economy.BuyPlayerEquipment(iron, out f), f);
            Assert.AreEqual(gold - iron.GoldCost, g.Jobs.State.Gold);
            Assert.AreEqual(iron.Id, g.Sites.PlayerToolId, "the supplier's own pick");
            Assert.IsFalse(g.Economy.BuyPlayerEquipment(iron, out _), "already equipped");
            var tempered = Cat.Get<WorkerEquipmentDefinition>("equipment.tool_tempered");
            StringAssert.Contains("level 3", g.Economy.PlayerEquipmentBlockedReason(tempered));

            var reloaded = Boot(T0);
            Assert.AreEqual(iron.Id, reloaded.Sites.PlayerToolId);
            Assert.AreEqual(2, reloaded.Economy.SiteEquipmentLevel("site.mine"));
        }

        [Test]
        public void TheSupplierMinesAndChopsIntoTheCarryAndRoutinesRunWhileAway()
        {
            var g = Boot(T0);
            Assert.AreEqual(0, g.Sites.Get("site.mine").Record.Workers.Count, "solo play: no hired crew");
            Assert.AreEqual(0, g.Sites.Get("site.forest").Record.Workers.Count);

            // Dig by hand: each block's units go straight into the carry.
            var u = 2;
            for (; u < 40 && g.Logistics.System.CarryCount < 4; u++) Assert.IsTrue(DigTestKit.Dig(g, 0, u, 2));
            Assert.GreaterOrEqual(g.Logistics.System.CarryCount, 4);
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());

            // A full carry leaves the rest waiting at the face; nothing is lost.
            for (; u < 80 && g.Sites.Get("site.mine").Record.FacePending.Count == 0; u++) Assert.IsTrue(DigTestKit.Dig(g, 0, u, 2));
            Assert.IsTrue(g.Logistics.System.IsFull);
            Assert.Greater(g.Sites.Get("site.mine").Record.FacePending.Count, 0);
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
            g.Logistics.System.DebugUnloadCarry();

            // Chop any tree in reach (the Forest's grid): its logs go straight into the carry.
            var forest = g.Sites.Get("site.forest");
            Assert.IsTrue(forest.GroveMode);
            for (var i = 0; i < 2000 && !forest.IsFelled(1, 0); i++) g.Sites.PlayerChopCell("site.forest", 1, 0, 0.1f);
            Assert.IsTrue(forest.IsFelled(1, 0));
            Assert.GreaterOrEqual(g.Logistics.System.CarryCount, 4, "a tree's four logs (herbs are a bonus roll)");
            Assert.AreEqual(new GroveCell(1, 0), forest.Record.LastChop);
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
            g.Logistics.System.DebugUnloadCarry();

            // A routine: the supplier keeps cutting the trees nearest the last one while away; taking over again brings back
            // what they carried.
            Assert.IsTrue(g.Sites.StartRoutine("site.forest"));
            Assert.AreEqual("site.forest", g.Sites.RoutineSiteId);
            g.Simulation.Advance(10 * Minute, 10 * Minute);
            var routine = g.Sites.RoutineWorker;
            Assert.IsNotNull(routine);
            var felled = forest.Record.Chopped.Keys.Count(c => forest.IsFelled(c.Area, c.Slot));
            Assert.Greater(felled, 1, "the routine keeps cutting while away");
            Assert.Greater(forest.Record.DepositedTotal + routine.Pack.Count, 0, "and hauls to staging");
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
            g.Scheduler.SaveNow("test");
            var reloaded = Boot(T0);
            Assert.AreEqual("site.forest", reloaded.Sites.RoutineSiteId, "a routine survives a restart");
            Assert.IsTrue(reloaded.Sites.EndRoutine());
            Assert.IsNull(reloaded.Sites.RoutineSiteId);
            CollectionAssert.IsEmpty(reloaded.Logistics.System.Audit());
        }

        // ------------------------------------------------------------------ recovery and balance guards

        [Test]
        public void CompanyRecoversFromZeroGoldGearAndReputation()
        {
            var g = Boot(T0);
            g.Jobs.DebugSetGold(0);
            g.Jobs.DebugSetReputation(0);
            Assert.AreEqual(0, g.Logistics.System.StoreItems.Count);
            Assert.IsTrue(g.Jobs.Post("quest.local_supply_survey", out var f), f);
            g.Jobs.Board.Advance(31_000);
            var a = g.Jobs.State.Posting.Applicants.Single();
            Assert.IsTrue(g.Jobs.Dispatch(a.ContractorId, new Loadout(), out f, out var e), f);
            g.Simulation.Advance(e.EndMs - g.Jobs.State.SimMs + 1, 0);
            Assert.Greater(g.Jobs.State.Gold, 0, "the safe contract pays");
            Assert.IsFalse(g.Economy.Trader.Scheduled, "a sponsored recovery run alone does not bring the Trader");

            // The Trader's guaranteed bundle is always affordable from a recovery payout.
            var stock = Cat.TraderStock;
            Assert.IsTrue(stock.Entries.Any(x => x.availability == StockAvailability.Guaranteed && x.price <= g.Jobs.State.Gold));
        }

        [Test]
        public void SellingStarterGearNeverOutEarnsOneOrdinaryQuest()
        {
            var cat = Cat;
            var kit = new[] { "item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff", "item.recovery_tonic" };
            var liquidation = kit.Sum(i => cat.Get<ItemDefinition>(i).TraderBuyValue);
            var patrol = cat.Get<QuestDefinition>("quest.roadside_patrol").BaseGold;
            Assert.Less(liquidation, patrol, "selling a full starter kit pays less than one Roadside Patrol");

            // No craft-and-sell loop from Trader-bought materials.
            var prices = new Dictionary<string, int>();
            foreach (var e in cat.TraderStock.Entries) if (e.resource != null) prices[e.resource.Id] = e.price;
            foreach (var r in cat.Recipes)
            {
                if (r == null || r.Cost.Any(c => !prices.ContainsKey(c.resource.Id))) continue;
                var buy = r.Cost.Sum(c => prices[c.resource.Id] * c.quantity);
                Assert.Less(r.OutputItem.TraderBuyValue, buy, $"{r.Id} could be crafted from Trader goods and sold at a profit");
            }
        }

        // ------------------------------------------------------------------ migration

        [Test]
        public void V7FixtureMigratesAndSchedulesTheTraderFromPastResults()
        {
            var json = File.ReadAllText("Assets/Ravenholm/Tests/Fixtures/save_v7_fixture.json");
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(7, from);
            Assert.AreEqual(SaveEnvelope.CurrentSchemaVersion, env.SchemaVersion);
            Assert.IsNull(env.Economy);

            var store = new SaveStore(Dir);
            Assert.IsTrue(store.Write(json, env.SaveGeneration, out err), err);
            var g = Boot(env.LastAcceptedUtc);
            Assert.AreEqual(120_000 + 60_000, g.Economy.Trader.State.AnchorMs, "first visit follows the earliest ordinary return");
            Assert.AreEqual(0, g.Economy.Purchased.Count);
        }
    }
}
