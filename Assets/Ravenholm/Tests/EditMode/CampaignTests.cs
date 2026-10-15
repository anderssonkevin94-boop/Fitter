using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>Phase 7: world content and the campaign (GDD 14, 27).</summary>
    public sealed class CampaignTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");

        private GameServices Boot(string dir = null)
        {
            var g = new GameServices(new ManualClock(T0), Cat, Balance, dir ?? Dir, "sandbox", "test", 1.5f);
            g.Start();
            g.Jobs.Board.Config.CastlePosts = false;   // drives the original Job Board directly (in play, the castle posts)
            return g;
        }

        // ------------------------------------------------------------------ content shape

        [Test]
        public void ContentStaysWithinTheV1CeilingsAndValidates()
        {
            var c = Cat;
            Assert.IsTrue(c.Validate().IsValid, c.Validate().ToString());
            Assert.AreEqual(3, c.Regions.Count);
            Assert.That(c.Quests.Count, Is.InRange(18, 24));
            Assert.That(c.Recipes.Count, Is.LessThanOrEqualTo(30));
            foreach (var site in c.Sites) Assert.AreEqual(6, site.Bands.Count, site.Id);
            Assert.AreEqual(1, c.Quests.Count(q => q.CampaignFinale));
        }

        [Test]
        public void EveryRecipeQuestAndUpgradeIsReachableWithoutRareDrops()
        {
            CollectionAssert.IsEmpty(ProgressionGraph.Problems(Cat));
            var g = ProgressionGraph.Build(Cat);
            // Quest rare rolls never count as a source: the gem is reachable only because the Mine yields it.
            Assert.IsTrue(g.Materials.Contains(Cat.Get<ResourceDefinition>("resource.gem")));
        }

        [Test]
        public void EveryClassHasANiche()
        {
            // A class's niche: some unlockable job where a level-6 member with the best gear has the best forecast.
            var g = Boot();
            var log = g.Logistics.System;
            foreach (var item in Cat.Items) log.CreateStoreItem(item.Id, "test fixture");
            var board = g.Jobs.Board;
            var best = new Dictionary<string, List<string>>();
            foreach (var q in Cat.Quests.Where(x => !x.RecoveryContract))
            {
                var view = board.Quest(q.Id);
                string winner = null;
                var top = double.MinValue;
                foreach (var cls in Cat.Classes)
                {
                    var c = new ContractorRecord { Id = "test." + cls.Id, Name = cls.DisplayName, ClassId = cls.Id, Level = 6 };
                    var l = board.BestAvailableLoadout(c, null);
                    var margin = board.Forecast(c, l, view).MeanMargin;
                    if (margin > top) { top = margin; winner = cls.Id; }
                }
                if (!best.TryGetValue(winner, out var list)) best[winner] = list = new List<string>();
                list.Add(q.Id);
            }
            foreach (var cls in Cat.Classes)
                Assert.IsTrue(best.ContainsKey(cls.Id), $"{cls.Id} is never the best choice for any job: " +
                    string.Join("; ", best.Select(kv => kv.Key + ": " + string.Join(",", kv.Value))));
        }

        // ------------------------------------------------------------------ the Beacon chain

        private static void Succeed(GameServices g, string questId) =>
            g.Jobs.State.Progress[questId] = new QuestProgress { Attempts = 1, Successes = 1, Unlocked = true };

        [Test]
        public void PreparationLegsPermanentlyRemoveTheirThreatsFromTheFinale()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            var before = board.Quest("quest.beacon_expedition").Threats.Select(t => t.Id).ToList();
            CollectionAssert.Contains(before, "threat.beacon_approach");
            CollectionAssert.Contains(before, "threat.pass_wardens");

            Succeed(g, "quest.beacon_road");
            var mid = board.Quest("quest.beacon_expedition");
            CollectionAssert.DoesNotContain(mid.Threats.Select(t => t.Id).ToList(), "threat.beacon_approach");
            CollectionAssert.Contains(mid.PreparedThreats, "threat.beacon_approach");
            Succeed(g, "quest.beacon_pass");
            CollectionAssert.AreEqual(new[] { "threat.pale_flame" }, board.Quest("quest.beacon_expedition").Threats.Select(t => t.Id).ToList());

            g.Scheduler.SaveNow("test");
            var reloaded = Boot();
            CollectionAssert.AreEqual(new[] { "threat.pale_flame" }, reloaded.Jobs.Board.Quest("quest.beacon_expedition").Threats.Select(t => t.Id).ToList(),
                "preparations survive a restart (derived from saved progress)");
        }

        [Test]
        public void FinaleChecksLevelCurrentReputationGearAndCounterPotionAtDispatch()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            var log = g.Logistics.System;
            var q = board.Quest("quest.beacon_expedition");
            var c = new ContractorRecord { Id = "test.mage", Name = "Test Mage", ClassId = "class.mage", Level = 5 };
            var focus = log.CreateStoreItem("item.moonsilver_focus", "fixture");
            var coat = log.CreateStoreItem("item.riveted_coat", "fixture");
            var mail = log.CreateStoreItem("item.moonsilver_mail", "fixture");
            var draught = log.CreateStoreItem("item.warding_draught", "fixture");
            var antidote = log.CreateStoreItem("item.antidote", "fixture");

            StringAssert.Contains("level 6", board.RequirementProblem(c, new Loadout { Weapon = focus, Armor = mail, Potion1 = draught }, q));
            c.Level = 6;
            g.Jobs.DebugSetReputation(69);
            StringAssert.Contains("reputation 70", board.RequirementProblem(c, new Loadout { Weapon = focus, Armor = mail, Potion1 = draught }, q));
            g.Jobs.DebugSetReputation(70);
            StringAssert.Contains("Protection 10", board.RequirementProblem(c, new Loadout { Weapon = focus, Armor = coat, Potion1 = draught }, q));
            StringAssert.Contains("potion", board.RequirementProblem(c, new Loadout { Weapon = focus, Armor = mail, Potion1 = antidote }, q));
            Assert.IsNull(board.RequirementProblem(c, new Loadout { Weapon = focus, Armor = mail, Potion1 = draught }, q));
        }

        [Test]
        public void TheFinaleIsOneTimeAndThePostgameContinues()
        {
            var g = Boot();
            foreach (var q in Cat.Quests) Succeed(g, q.Id);
            g.Jobs.Board.RefreshUnlocks();
            Assert.IsTrue(g.Jobs.Board.CampaignComplete);
            Assert.IsNull(g.Jobs.Board.CampaignObjective());
            StringAssert.Contains("Completed", g.Jobs.Board.PostBlockedReason("quest.beacon_expedition"));
            Assert.IsNull(g.Jobs.Board.PostBlockedReason("quest.ashen_barracks"), "repeatable contracts stay available afterwards");
            g.Scheduler.SaveNow("test");
            Assert.IsTrue(Boot().Jobs.Board.CampaignComplete, "completion survives a restart");
        }

        [Test]
        public void BreadcrumbStartsAtTheFirstPatrolAndAdvances()
        {
            var g = Boot();
            Assert.AreEqual("quest.roadside_patrol", g.Jobs.Board.CampaignObjective());
            Succeed(g, "quest.roadside_patrol");
            Assert.AreEqual("quest.briar_track", g.Jobs.Board.CampaignObjective());
        }

        // ------------------------------------------------------------------ full playthrough

        /// <summary>
        /// A simple bot plays the campaign from a fresh company with the real resolver, fees, gold,
        /// reputation, applicants, injuries and deaths. Production is approximated by topping up shared
        /// materials (the Mine, Forest, facilities and Trader supply these in play), and every band counts as
        /// revealed. Crafting still needs owned blueprints and facility levels; upgrades still cost gold.
        /// </summary>
        [Test]
        public void CampaignCanBeCompletedFromAFreshSaveWithOrdinaryPlay([Values(1, 2, 3)] int run)
        {
            var g = Boot();
            var log = g.Logistics.System;
            var board = g.Jobs.Board;
            var content = g.Jobs.Content;
            foreach (var sim in g.Sites.Sites)
                for (var d = 1; d <= sim.Config.Bands.Count; d++) sim.Record.Revealed[d] = sim.Record.Composition;

            int expeditions = 0, deaths = 0, recoveries = 0;
            const int limit = 700;
            while (!board.CampaignComplete && expeditions < limit)
            {
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

                var sent = TrySend(g, board.CampaignObjective(), RiskBand.Risky);
                if (sent == null)
                    foreach (var q in Cat.Quests.Where(x => x.Repeatable && !x.RecoveryContract && board.IsUnlocked(x.Id)).OrderByDescending(x => x.Difficulty))
                        if ((sent = TrySend(g, q.Id, RiskBand.Good)) != null) break;
                if (sent == null) { sent = TrySend(g, Cat.Quests.First(x => x.RecoveryContract).Id, RiskBand.Suicidal); recoveries++; }
                if (sent == null) { g.Simulation.Advance(60_000, 0); continue; }   // everyone injured or away: wait

                expeditions++;
                g.Simulation.Advance(sent.EndMs - g.Jobs.State.SimMs + 1, 0);
                var res = g.Jobs.State.Results.Last(x => x.ExpeditionId == sent.Id);
                if (res.Died) deaths++;
            }

            var summary = $"run {run}: {expeditions} expeditions, {deaths} deaths, {recoveries} recovery surveys, " +
                          $"reputation {g.Jobs.State.Reputation}, gold {g.Jobs.State.Gold}, sim {g.Jobs.State.SimMs / 3_600_000.0:0.0} h";
            UnityEngine.Debug.Log("[Campaign bot] " + summary);
            Assert.IsTrue(board.CampaignComplete, "campaign not finished: " + summary + "; stuck at " + board.CampaignObjective());
            CollectionAssert.IsEmpty(log.Audit());
        }

        /// <summary>Posts a job and sends the applicant with the best forecast if it reaches <paramref name="minimum"/>.</summary>
        private static ExpeditionRecord TrySend(GameServices g, string questId, RiskBand minimum)
        {
            var board = g.Jobs.Board;
            if (questId == null || !board.IsUnlocked(questId) || board.PostBlockedReason(questId) != null) return null;
            if (!g.Jobs.Post(questId, out _)) return null;
            board.Advance(31_000);
            var q = board.Quest(questId);
            ExpeditionRecord best = null;
            string bestId = null;
            Loadout bestLoadout = null;
            var bestMargin = double.MinValue;
            foreach (var a in g.Jobs.State.Posting.Applicants)
            {
                var c = board.Contractor(a.ContractorId);
                var l = board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner);
                AddPotions(g, l, q);
                var f = board.Forecast(c, l, q);
                if (board.DispatchBlockedReason(c.Id, l) != null || f.Band < minimum) continue;
                if (f.MeanMargin > bestMargin) { bestMargin = f.MeanMargin; bestId = c.Id; bestLoadout = l; }
            }
            if (bestId != null && g.Jobs.Dispatch(bestId, bestLoadout, out _, out best)) return best;
            g.Jobs.CancelPosting();
            return null;
        }

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
