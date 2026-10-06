using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Phase 9 release hardening (GDD 23/28): random play with crashes (process death = reload from disk,
    /// losing anything unsaved), offline jumps, trades and upgrades. After every step: no duplicated or lost
    /// goods, gold never negative, every result applied once, every expedition consistent, the save loads.
    /// </summary>
    public sealed class SoakTests : TempDirFixture
    {
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");

        private ManualClock _clock;

        private GameServices Boot()
        {
            var g = new GameServices(_clock, Cat, Balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            g.Logistics.RegisterContainer("test.box", ContainerKind.OutputPad, 100f, 100f, 0.45f, null);
            return g;
        }

        private static List<string> Invariants(GameServices g)
        {
            var p = new List<string>();
            p.AddRange(g.Logistics.System.Audit());
            var s = g.Jobs.State;
            if (s.Gold < 0) p.Add($"gold {s.Gold}");
            if (s.ReputationTenths < 0 || s.ReputationTenths > 1000) p.Add($"reputation {s.ReputationTenths}");
            var ids = s.Results.Select(r => r.ExpeditionId).ToList();
            if (ids.Count != ids.Distinct().Count()) p.Add("a result was recorded twice");
            foreach (var e in s.Expeditions)
            {
                if (ids.Contains(e.Id)) p.Add($"{e.Id} is both running and finished");
                var c = g.Jobs.Board.Contractor(e.ContractorId);
                if (c == null || c.Status != ContractorStatus.OnExpedition) p.Add($"{e.Id}: contractor not marked away");
                foreach (var i in e.Items) if (g.Logistics.System.StoreItems.Contains(i)) p.Add($"{i} both away and in the Store");
            }
            var away = s.Expeditions.Select(e => e.ContractorId).ToList();
            if (away.Count != away.Distinct().Count()) p.Add("a contractor is on two expeditions");
            if (s.Expeditions.Count > g.Jobs.Board.Config.ActiveSlots) p.Add("more expeditions than slots");
            foreach (var kv in g.Logistics.System.State.SharedMaterials) if (kv.Value < 0) p.Add($"{kv.Key} negative");
            return p;
        }

        [Test]
        public void RandomPlayWithCrashesNeverDuplicatesOrLosesAnything([Values(11, 22, 33)] int seed)
        {
            var rng = new Random(seed);
            _clock = new ManualClock(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
            var g = Boot();
            var log = new List<string>();
            int crashes = 0, jumps = 0, sends = 0, trades = 0;
            for (var step = 0; step < 400; step++)
            {
                var action = rng.Next(12);
                try
                {
                    switch (action)
                    {
                        case 0:   // production and stocking (stand-in for hauling and crafting)
                            foreach (var r in new[] { "resource.iron_ingot", "resource.cedar_lumber", "resource.herb", "resource.copper_ingot" })
                                g.Logistics.System.ProduceShared(r, rng.Next(0, 6), "soak: production");
                            var rec = Cat.Recipes[rng.Next(Cat.Recipes.Count)];
                            if (g.Production.System.State.Blueprints.Contains(rec.RequiredBlueprint.Id))
                            {
                                var cost = rec.Cost.Select(c => new KeyValuePair<string, int>(c.resource.Id, c.quantity)).ToList();
                                if (g.Logistics.System.TryConsumeShared(cost, "soak: craft")) g.Logistics.System.CreateStoreItem(rec.OutputItem.Id, "soak: craft");
                            }
                            g.Scheduler.MarkDirty();
                            break;
                        case 1:
                        case 2:   // post and send someone
                        {
                            var unlocked = Cat.Quests.Where(q => g.Jobs.Board.IsUnlocked(q.Id)).ToList();
                            var q = unlocked[rng.Next(unlocked.Count)];
                            if (!g.Jobs.Post(q.Id, out _)) break;
                            g.Jobs.Board.Advance(31_000);
                            var apps = g.Jobs.State.Posting.Applicants;
                            if (apps.Count == 0) { g.Jobs.CancelPosting(); break; }
                            var c = g.Jobs.Board.Contractor(apps[rng.Next(apps.Count)].ContractorId);
                            var l = g.Jobs.Board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner);
                            g.Jobs.Board.AddBestCounterPotions(l, g.Jobs.Board.Quest(q.Id), JobBoardSystem.LoadoutOwner);
                            if (rng.Next(2) == 0) g.Jobs.Board.ReserveLoadout(l);   // sometimes reserve first, like the loadout panel
                            if (g.Jobs.Dispatch(c.Id, l, out _, out _)) sends++;
                            else g.Jobs.CancelPosting();
                            break;
                        }
                        case 3:
                        case 4:   // online play for a while
                            g.Simulation.Advance(rng.Next(1, 600) * 1000L, rng.Next(1, 600) * 1000L);
                            break;
                        case 5:   // crash: process dies, anything not yet committed is lost
                            g = Boot();
                            crashes++;
                            break;
                        case 6:   // close the app properly and come back hours later
                            g.Scheduler.Flush("soak: quit");
                            _clock.Set(_clock.UtcNow.AddMinutes(rng.Next(1, 24 * 60)));
                            g = Boot();
                            jumps++;
                            break;
                        case 7:   // Trader
                        {
                            g.Economy.DebugSummonTrader();
                            var offers = g.Economy.Trader.Offers(g.Economy.Now);
                            if (offers.Count > 0 && g.Economy.Buy(offers[rng.Next(offers.Count)].Entry.Key, out _)) trades++;
                            var store = g.Logistics.System.StoreItems;
                            if (store.Count > 0 && g.Economy.Sell(store[rng.Next(store.Count)], out _)) trades++;
                            break;
                        }
                        case 8:   // upgrades and worker gear
                        {
                            var u = Cat.Upgrades[rng.Next(Cat.Upgrades.Count)];
                            g.Economy.BuyUpgrade(u, out _);
                            var e = Cat.Equipment[rng.Next(Cat.Equipment.Count)];
                            foreach (var site in g.Sites.Sites)
                                foreach (var w in site.Record.Workers) g.Economy.BuyEquipment(site.Config.SiteId, w.Id, e, out _);
                            break;
                        }
                        case 9:   // site decisions
                            foreach (var site in g.Sites.Sites)
                                if (rng.Next(2) == 0) g.Sites.ChooseContinue(site.Config.SiteId); else g.Sites.ChooseDeeper(site.Config.SiteId);
                            break;
                        case 10:  // read results, set a reserve
                            foreach (var r in g.Jobs.State.Results.Where(x => !x.Acknowledged).ToList()) g.Jobs.Acknowledge(r.ExpeditionId);
                            g.Production.SetReserve("station.smelter", "resource.iron_ore", rng.Next(0, 3) * 2);
                            break;
                        case 11:  // money troubles
                            if (rng.Next(4) == 0) g.Jobs.DebugSetGold(0);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Assert.Fail($"seed {seed} step {step} action {action} threw: {ex}");
                }
                var problems = Invariants(g);
                if (problems.Count > 0) Assert.Fail($"seed {seed} step {step} action {action}: {string.Join("; ", problems)}");
            }

            // The final save must load into an identical, valid company.
            g.Scheduler.Flush("soak: end");
            var json = File.ReadAllText(Path.Combine(Dir, SaveStore.PrimaryName));
            Assert.IsTrue(SaveFileCodec.TryDecode(json, out var body, out var err), err);
            var reloaded = Boot();
            CollectionAssert.IsEmpty(Invariants(reloaded));
            Assert.AreEqual(g.Jobs.State.Gold, reloaded.Jobs.State.Gold);
            Assert.AreEqual(g.Jobs.State.Results.Count, reloaded.Jobs.State.Results.Count);
            UnityEngine.Debug.Log($"[Soak] seed {seed}: {sends} sends, {crashes} crashes, {jumps} time jumps, {trades} trades, " +
                                  $"{g.Jobs.State.Results.Count} results, gold {g.Jobs.State.Gold}");
        }

        [Test]
        public void EveryHistoricalSaveFixtureLoadsIntoACleanCompany([Values(1, 2, 3, 4, 5, 6, 7, 8)] int version)
        {
            var json = File.ReadAllText($"Assets/Ravenholm/Tests/Fixtures/save_v{version}_fixture.json");
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out _, out var err), err);
            Assert.IsTrue(new SaveStore(Dir).Write(json, env.SaveGeneration, out err), err);
            _clock = new ManualClock(env.LastAcceptedUtc);
            var g = Boot();
            Assert.AreNotEqual(SaveLoadStatus.Unrecoverable, g.Saves.Status.LoadStatus, g.Saves.Status.LoadDetail);
            CollectionAssert.IsEmpty(Invariants(g), $"v{version}");
            g.Simulation.Advance(3_600_000, 3_600_000);   // keeps working for an hour
            CollectionAssert.IsEmpty(Invariants(g), $"v{version} after an hour");
            g.Scheduler.Flush("test");
            var again = Boot();
            Assert.AreEqual(g.Jobs.State.Gold, again.Jobs.State.Gold, $"v{version} round trip");
            CollectionAssert.IsEmpty(Invariants(again));
        }
    }
}
