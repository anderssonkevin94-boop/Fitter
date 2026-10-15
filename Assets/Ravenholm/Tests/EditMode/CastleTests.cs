using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The castle posts the jobs (Kevin, 2026-10-15): one adventurer at a time takes a castle job and comes to the shop to
    /// be kitted out; the shop lends the gear, pays no fee, and is paid a share of the castle's reward on return.
    /// </summary>
    public sealed class CastleTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");

        private GameServices Boot()
        {
            var g = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            Assert.IsTrue(g.Jobs.Board.Config.CastlePosts, "the castle posts in play");
            return g;
        }

        private static void Stock(GameServices g, params string[] items)
        {
            foreach (var i in items) g.Logistics.System.CreateStoreItem(i, "test stock");
        }

        /// <summary>Advances until the castle's adventurer has arrived at the shop (fails after an hour).</summary>
        private static PostingRecord WaitForAdventurer(GameServices g)
        {
            for (var i = 0; i < 720; i++)
            {
                var p = g.Jobs.State.Posting;
                if (p != null && p.Applicants.Count > 0 && g.Jobs.Board.HasArrived(p.Applicants[0])) return p;
                g.Simulation.Advance(5_000, 0);
            }
            Assert.Fail("no adventurer arrived within an hour");
            return null;
        }

        [Test]
        public void ANewCompanyWithNoGearIsOfferedTheSurvey()
        {
            var g = Boot();
            Assert.IsNull(g.Jobs.State.Posting, "nothing at once");
            var p = WaitForAdventurer(g);
            Assert.IsTrue(p.ByCastle);
            Assert.IsTrue(g.Jobs.Board.Quest(p.QuestId).Recovery, "nothing to lend: the castle offers the survey (it needs no gear)");
            Assert.AreEqual(1, p.Applicants.Count, "one adventurer per castle job");
            Assert.LessOrEqual(p.CreatedMs, g.Jobs.Board.Config.CastleFirstMs + 1, "the first offer comes promptly");
        }

        [Test]
        public void AnAdventurerIsKittedOutForFreeAndPaysTheShopItsShare()
        {
            var g = Boot();
            Stock(g, "item.iron_sword", "item.riveted_coat");
            var board = g.Jobs.Board;
            var p = WaitForAdventurer(g);
            Assert.IsFalse(board.Quest(p.QuestId).Recovery, "with gear in stock, a real job");
            Assert.AreEqual(board.CampaignObjective(), p.QuestId, "the campaign's next job comes first");
            var c = board.Contractor(p.Applicants[0].ContractorId);
            var l = board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner);
            var gold = g.Jobs.State.Gold;
            Assert.IsNull(board.DispatchBlockedReason(c.Id, l));
            g.Jobs.DebugSetGold(0);
            Assert.IsNull(board.DispatchBlockedReason(c.Id, l), "no fee: even a penniless shop can kit them out");
            g.Jobs.DebugSetGold(gold);
            Assert.IsTrue(g.Jobs.Dispatch(c.Id, l, out var f, out var e), f);
            Assert.AreEqual(gold, g.Jobs.State.Gold, "the castle pays them, not the shop");
            Assert.AreEqual(0, e.FeePaid);
            Assert.AreEqual(board.Config.ShopSharePercent, e.ShopSharePercent, "the share is fixed at dispatch");
            Assert.IsNull(g.Jobs.State.Posting);

            g.Simulation.Advance(e.EndMs - g.Jobs.State.SimMs + 1, 0);
            var r = g.Jobs.State.Results.Last(x => x.ExpeditionId == e.Id);
            var full = (int)Math.Floor(board.Quest(e.QuestId).BaseGold * Resolver.GoldMultiplier(r.Outcome));
            Assert.AreEqual(full * board.Config.ShopSharePercent / 100, r.GoldEarned, "the shop's share of the castle's reward");
            Assert.AreEqual(gold + r.GoldEarned, g.Jobs.State.Gold);
            if (!r.Died) CollectionAssert.IsSubsetOf(e.Items.Where(i => !r.ConsumedItems.Contains(i)).ToList(), g.Logistics.System.StoreItems.ToList(), "lent gear comes back");
            CollectionAssert.IsEmpty(g.Logistics.System.Audit());
        }

        [Test]
        public void TurningSomeoneAwayBringsSomeoneElseLater()
        {
            var g = Boot();
            Stock(g, "item.iron_sword", "item.riveted_coat");
            var first = WaitForAdventurer(g);
            var who = first.Applicants[0].ContractorId;
            g.Jobs.TurnAway();
            Assert.IsNull(g.Jobs.State.Posting);
            Assert.AreEqual(g.Jobs.Board.Config.CastleIntervalMs, g.Jobs.Board.NextCastleOfferDelay(), "the castle waits before the next offer");
            var next = WaitForAdventurer(g);
            Assert.AreNotEqual(who, next.Applicants[0].ContractorId, "someone else comes");
            Assert.AreNotEqual(first.QuestId, next.QuestId, "for a different job: turning someone away never brings the same offer straight back");
        }

        [Test]
        public void OffersSurviveARestartAndBothSlotsFullMeansNobodyComes()
        {
            var g = Boot();
            Stock(g, "item.iron_sword", "item.iron_dagger", "item.riveted_coat", "item.steel_coat");
            for (var n = 0; n < 2; n++)
            {
                var p = WaitForAdventurer(g);
                var c = g.Jobs.Board.Contractor(p.Applicants[0].ContractorId);
                Assert.IsTrue(g.Jobs.Dispatch(c.Id, g.Jobs.Board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner), out var f, out _), f);
            }
            Assert.AreEqual(long.MaxValue, g.Jobs.Board.NextCastleOfferDelay(), "both slots in use: the castle waits for a return");
            g.Simulation.Advance(g.Jobs.Board.Config.CastleIntervalMs * 3, 0);
            Assert.IsTrue(g.Jobs.State.Posting == null || g.Jobs.State.Expeditions.Count < 2, "no offer while both are out");
            g.Scheduler.SaveNow("test");
            var back = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            back.Start();
            Assert.AreEqual(g.Jobs.State.NextCastleOfferMs, back.Jobs.State.NextCastleOfferMs);
            Assert.AreEqual(g.Jobs.State.CastleOffers, back.Jobs.State.CastleOffers);
            Assert.IsTrue(back.Jobs.State.Expeditions.All(x => x.ShopSharePercent == back.Jobs.Board.Config.ShopSharePercent));
        }

        [Test]
        public void TheCastleCarriesACampaignFromAFreshSave()
        {
            var g = Boot();
            var log = g.Logistics.System;
            var board = g.Jobs.Board;
            foreach (var sim in g.Sites.Sites)
                for (var d = 1; d <= sim.Config.Bands.Count; d++) sim.Record.Revealed[d] = sim.Record.Composition;

            int expeditions = 0, turnedAway = 0, rounds = 0;
            var refusals = new Dictionary<string, int>();
            while (!board.CampaignComplete && expeditions < 900 && rounds++ < 3000)
            {
                // The journey (step 5): once the camp's summons is answered, the company moves on up the road.
                if (g.Camp.HasNext && board.SummonsAnswered(g.Camp.Index + 1)) g.Camp.DebugSetIndex(g.Camp.Index + 1);
                // A busy shop: materials flow in and the best recipes stay stocked (as the Job Board bot does).
                foreach (var r in Cat.Resources)
                {
                    var have = log.SharedAvailable(r.Id);
                    if (have < 40) log.ProduceShared(r.Id, 40 - have, "bot: production");
                }
                foreach (var u in Cat.Upgrades.Where(x => x.Kind == UpgradeKind.StationLevel).OrderBy(x => x.GoldCost))
                    if (g.Economy.UpgradeBlockedReason(u) == null) g.Economy.BuyUpgrade(u, out _);
                foreach (var rec in Cat.Recipes)
                {
                    if (!g.Production.System.State.Blueprints.Contains(rec.RequiredBlueprint.Id)) continue;
                    if (g.Production.System.Level(rec.Station.Id) < rec.MinFacilityLevel) continue;
                    var stocked = log.StoreItems.Count(i => log.DefinitionOfItem(i) == rec.OutputItem.Id);
                    for (var k = stocked; k < 2; k++)
                    {
                        var cost = rec.Cost.Select(x => new KeyValuePair<string, int>(x.resource.Id, x.quantity)).ToList();
                        if (log.TryConsumeShared(cost, "bot: crafted")) log.CreateStoreItem(rec.OutputItem.Id, "bot: crafted");
                    }
                }

                // Whoever the castle sends: kit them out with the best gear, or turn them away if they cannot go.
                var p = WaitForAdventurer(g);
                var c = board.Contractor(p.Applicants[0].ContractorId);
                var l = board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner);
                AddPotions(g, l, board.Quest(p.QuestId));
                var why = board.DispatchBlockedReason(c.Id, l);
                if (why != null)
                {
                    var k = p.QuestId + ": " + why;
                    refusals[k] = refusals.TryGetValue(k, out var n) ? n + 1 : 1;
                    g.Jobs.TurnAway();
                    turnedAway++;
                    continue;
                }
                Assert.IsTrue(g.Jobs.Dispatch(c.Id, l, out var f, out _), f);
                expeditions++;
            }
            var summary = $"{expeditions} expeditions, {turnedAway} turned away ({string.Join("; ", refusals.OrderByDescending(x => x.Value).Take(3).Select(x => $"{x.Value}× {x.Key}"))}), reputation {g.Jobs.State.Reputation}, gold {g.Jobs.State.Gold}, " +
                          $"sim {g.Jobs.State.SimMs / 3_600_000.0:0.0} h";
            UnityEngine.Debug.Log("[Castle bot] " + summary);
            Assert.IsTrue(board.CampaignComplete, "campaign not finished: " + summary + "; stuck at " + board.CampaignObjective());
            CollectionAssert.IsEmpty(log.Audit());
        }

        /// <summary>Counter potions for the job's known threats, then a tonic (as the Job Board bot packs them).</summary>
        private static void AddPotions(GameServices g, Loadout l, QuestInfo q)
        {
            var log = g.Logistics.System;
            var content = g.Jobs.Content;
            var picks = new List<string>();
            foreach (var t in q.Threats.Where(t => t.Known && !string.IsNullOrEmpty(t.CounterTag)))
            {
                var p = log.StoreItems.FirstOrDefault(i => !picks.Contains(i) && content.Item(log.DefinitionOfItem(i)) is ItemInfo ii &&
                                                          ii.Kind == ItemKind.Potion && ii.CounterTag == t.CounterTag);
                if (p != null && picks.Count < 2 && !picks.Any(x => log.DefinitionOfItem(x) == log.DefinitionOfItem(p))) picks.Add(p);
            }
            foreach (var tonic in new[] { "item.strong_tonic", "item.recovery_tonic" })
            {
                if (picks.Count >= 2) break;
                var p = log.StoreItems.FirstOrDefault(i => log.DefinitionOfItem(i) == tonic);
                if (p != null) { picks.Add(p); break; }
            }
            l.Potion1 = picks.Count > 0 ? picks[0] : null;
            l.Potion2 = picks.Count > 1 ? picks[1] : null;
        }
    }
}
