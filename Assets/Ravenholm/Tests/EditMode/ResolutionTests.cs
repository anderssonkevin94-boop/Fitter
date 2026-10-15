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
    public sealed class ResolverTests
    {
        private static readonly ResolverCoefficients K = new ResolverCoefficients();
        private static readonly List<CarriedPotion> NoPotions = new List<CarriedPotion>();

        private static LoadoutStats Warrior(int power = 8, int protection = 6, int tonic = 0) =>
            new LoadoutStats { Might = 8, Survival = 8, Insight = 3, WeaponPower = power, ArmorProtection = protection, SurvivalPotionBonus = tonic };

        [Test]
        public void SameSeedSameOutcomeAndTheTableHolds()
        {
            for (ulong seed = 1; seed < 3000; seed++)
            {
                var a = Resolver.Resolve(K, Warrior(), 20, new List<ThreatSpec>(), NoPotions, seed);
                var b = Resolver.Resolve(K, Warrior(), 20, new List<ThreatSpec>(), NoPotions, seed);
                Assert.AreEqual(a.Outcome, b.Outcome);
                Assert.AreEqual(a.J, b.J);
                Assert.That(a.J, Is.InRange(-10.0, 10.0));
                Assert.That(a.K, Is.InRange(-6.0, 6.0));
                var expected = a.E >= 10 ? Outcome.GreatSuccess : a.E >= 0 ? Outcome.Success : a.E >= -6 ? Outcome.PartialInjury
                             : a.V >= 0 ? Outcome.Failure : Outcome.Death;
                Assert.AreEqual(expected, a.Outcome, $"seed {seed}");
            }
        }

        [Test]
        public void EveryOutcomeIsReproducibleBySeed()
        {
            // A rogue-like profile at difficulty 11 can reach every outcome (Great Success needs E ≥ 10).
            var rogue = new LoadoutStats { Might = 7, Survival = 4, Insight = 8, WeaponPower = 7 };
            var found = new Dictionary<Outcome, ulong>();
            for (ulong seed = 1; seed < 40000 && found.Count < 5; seed++)
            {
                var r = Resolver.Resolve(K, rogue, 11, new List<ThreatSpec>(), NoPotions, seed);
                if (!found.ContainsKey(r.Outcome)) found[r.Outcome] = seed;
            }
            Assert.AreEqual(5, found.Count, "all five outcomes occur");
            foreach (var kv in found)
                Assert.AreEqual(kv.Key, Resolver.Resolve(K, rogue, 11, new List<ThreatSpec>(), NoPotions, kv.Value).Outcome);
        }

        [Test]
        public void StrongerRelevantGearNeverWorsensAnOutcome()
        {
            var threats = new List<ThreatSpec> { new ThreatSpec { Id = "h", ArmorThreshold = 8, ArmorReadiness = 2, ArmorSurvival = 2 } };
            for (ulong seed = 1; seed < 1500; seed++)
            {
                var prev = Outcome.Death;
                for (var gear = 0; gear <= 14; gear += 2)
                {
                    var o = Resolver.Resolve(K, Warrior(gear, gear), 22, threats, NoPotions, seed).Outcome;
                    Assert.GreaterOrEqual(o, prev, $"seed {seed}, gear {gear}");
                    prev = o;
                }
            }
        }

        private static double SuccessRate(LoadoutStats s, IReadOnlyList<ThreatSpec> threats, int d)
        {
            var ok = 0;
            for (ulong seed = 1; seed <= 4000; seed++)
                if (Resolver.Resolve(K, s, d, threats, NoPotions, seed).Outcome >= Outcome.Success) ok++;
            return ok / 4000.0;
        }

        [Test]
        public void ThreatCountersMateriallyImproveTheDistribution()
        {
            var arcane = new List<ThreatSpec> { new ThreatSpec { Id = "arcane", CounterTag = "Arcane", Required = 4 } };
            var plain = Warrior();
            var warded = Warrior();
            warded.AddCounter("Arcane", 4);
            var without = SuccessRate(plain, arcane, 20);
            var with = SuccessRate(warded, arcane, 20);
            Assert.Greater(with - without, 0.15, $"countering the threat should matter a lot ({without:P0} → {with:P0})");
        }

        [Test]
        public void PotionTriggers()
        {
            var tonic = new CarriedPotion { InstanceId = "t", DefinitionId = "item.recovery_tonic", SurvivalBonus = 4 };
            var antidote = new CarriedPotion { InstanceId = "a", DefinitionId = "item.antidote", CounterTag = "Poison" };
            var poison = new List<ThreatSpec> { new ThreatSpec { Id = "poison", CounterTag = "Poison", Required = 4, UnmetSurvival = -3 } };
            for (ulong seed = 1; seed < 2000; seed++)
            {
                var r = Resolver.Resolve(K, Warrior(tonic: 4), 22, poison, new List<CarriedPotion> { tonic, antidote }, seed);
                Assert.AreEqual(r.E < -6, r.ConsumedPotions.Contains("t"), $"tonic only on a retreat (seed {seed})");
                Assert.IsTrue(r.ConsumedPotions.Contains("a"), "a counter potion fires whenever its threat is present");
            }
            var noThreat = Resolver.Resolve(K, Warrior(), 10, new List<ThreatSpec>(), new List<CarriedPotion> { antidote }, 5);
            Assert.IsFalse(noThreat.ConsumedPotions.Contains("a"), "no poison, no antidote use");
        }

        [Test]
        public void ReputationGainsDiminishAndLossesDoNot()
        {
            Assert.AreEqual(230, Resolver.ApplyReputation(200, 30, 20), "full gain up to the prestige target");
            Assert.AreEqual(358, Resolver.ApplyReputation(350, 30, 20), "×0.25 at reputation 35 (rounded)");
            Assert.AreEqual(805, Resolver.ApplyReputation(800, 30, 20), "never below ×0.15, never zero");
            Assert.AreEqual(730, Resolver.ApplyReputation(800, -70, 20), "losses are not reduced");
            Assert.AreEqual(0, Resolver.ApplyReputation(30, -70, 20), "clamped at 0");
            Assert.AreEqual(1000, Resolver.ApplyReputation(999, 40, 100), "clamped at 100");
        }
    }

    public sealed class CompletionTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private GameServices _g;
        private JobBoardSystem Board => _g.Jobs.Board;
        private LogisticsSystem Log => _g.Logistics.System;

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            g.Jobs.Board.Config.CastlePosts = false;   // drives the original Job Board directly (in play, the castle posts)
            g.Logistics.RegisterContainer("test.box", ContainerKind.OutputPad, 100f, 100f, 0.45f, null);
            return g;
        }

        [SetUp]
        public void BootServices() => _g = Boot();

        private void StockStarterKit()
        {
            foreach (var i in new[] { "item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff", "item.recovery_tonic" })
                Log.CreateItemInContainer("test.box", i);
            for (var i = 0; i < 20; i++) Log.StepOnce(100f, 100f);
            Log.DebugUnloadCarry();
        }

        /// <summary>Posts, waits for applicants, dispatches the first with its best gear plus any potion, and returns the expedition.</summary>
        private ExpeditionRecord Send(string quest, Outcome? forced)
        {
            Assert.IsTrue(_g.Jobs.Post(quest, out var f), f);
            Board.Advance(31_000);
            var a = _g.Jobs.State.Posting.Applicants.FirstOrDefault();
            Assert.NotNull(a, _g.Jobs.State.Posting.NoApplicantReason);
            var c = Board.Contractor(a.ContractorId);
            var l = Board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner);
            l.Potion1 = Log.StoreItems.FirstOrDefault(i => Log.DefinitionOfItem(i) == "item.recovery_tonic");
            Assert.IsTrue(_g.Jobs.Dispatch(c.Id, l, out f, out var e), f);
            _g.Jobs.DebugForceNext(forced);
            return e;
        }

        private ResultRecord Finish(ExpeditionRecord e)
        {
            _g.Jobs.AdvanceMs(e.EndMs - Board.State.SimMs + 1);
            return _g.Jobs.State.Results.Last(r => r.ExpeditionId == e.Id);
        }

        [Test]
        public void DeathRemovesTheRightPersonAndAllCarriedGear()
        {
            StockStarterKit();
            var e = Send("quest.roadside_patrol", Outcome.Death);
            var items = e.Items.ToList();
            var gold = _g.Jobs.State.Gold;
            var rep = _g.Jobs.State.ReputationTenths;
            var r = Finish(e);
            Assert.IsTrue(r.Died);
            Assert.AreEqual(ContractorStatus.Dead, Board.Contractor(e.ContractorId).Status);
            Assert.AreEqual(1, _g.Jobs.State.Contractors.Values.Count(c => c.Status == ContractorStatus.Dead), "only that person");
            foreach (var i in items)
            {
                CollectionAssert.DoesNotContain(Log.StoreItems, i);
                Assert.IsFalse(Log.State.ItemDefinitions.ContainsKey(i), "carried gear is destroyed, including unused potions");
            }
            Assert.AreEqual(gold, _g.Jobs.State.Gold);
            Assert.AreEqual(rep - 70, _g.Jobs.State.ReputationTenths);
            CollectionAssert.IsEmpty(Log.Audit());
            _g.Jobs.Post("quest.roadside_patrol", out _);
            Assert.IsFalse(_g.Jobs.State.Posting.Applicants.Any(a => a.ContractorId == e.ContractorId), "the dead never apply");
        }

        [Test]
        public void SuccessReturnsGearRewardsUnlocksAndSurvivesRestart()
        {
            StockStarterKit();
            var e = Send("quest.roadside_patrol", Outcome.Success);
            var gold = _g.Jobs.State.Gold;
            var ingots = Log.SharedAvailable("resource.iron_ingot");
            Assert.IsFalse(Board.IsUnlocked("quest.briar_track"));
            var r = Finish(e);
            Assert.AreEqual(Outcome.Success, r.Outcome);
            Assert.AreEqual(gold + 28, _g.Jobs.State.Gold, "base gold × 1.0");
            Assert.AreEqual(ingots + 2, Log.SharedAvailable("resource.iron_ingot"));
            foreach (var i in e.Items) CollectionAssert.Contains(Log.StoreItems, i);
            Assert.IsTrue(r.ConsumedItems.Count == 0, "an unused tonic comes back");
            Assert.IsTrue(Board.IsUnlocked("quest.briar_track") && Board.IsUnlocked("quest.old_shrine"));
            CollectionAssert.AreEquivalent(new[] { "quest.briar_track", "quest.old_shrine" }, r.QuestsUnlocked);
            Assert.AreEqual(1, Board.Contractor(e.ContractorId).Xp);
            Assert.AreEqual(ContractorStatus.Available, Board.Contractor(e.ContractorId).Status);

            var again = Boot();
            Assert.IsTrue(again.Jobs.Board.IsUnlocked("quest.briar_track"), "unlocks survive restart");
            Assert.AreEqual(_g.Jobs.State.Gold, again.Jobs.State.Gold);
            Assert.AreEqual(1, again.Jobs.State.Results.Count);
            CollectionAssert.IsEmpty(again.Logistics.System.Audit());
        }

        [Test]
        public void FirstSuccessBlueprintIsGrantedOnceAndDuplicatesBecomeMaterials()
        {
            StockStarterKit();
            Finish(Send("quest.roadside_patrol", Outcome.Success));
            var e1 = Send("quest.briar_track", Outcome.Success);
            var r1 = Finish(e1);
            Assert.AreEqual("blueprint.maple_longbow", r1.BlueprintGranted);
            Assert.IsTrue(_g.Production.System.State.Blueprints.Contains("blueprint.maple_longbow"));

            // Old Shrine's milestone is the ward staff; if the company already owns it (another source got there first) → substitute.
            _g.Production.System.GrantBlueprint("blueprint.copper_ward_staff");
            var count = _g.Production.System.State.Blueprints.Count;
            var copper = Log.SharedAvailable("resource.copper_ingot");
            // Test fixture: the applicant pool is random per world seed; seasoned contractors make sure someone takes Old Shrine.
            foreach (var c0 in _g.Jobs.State.Contractors.Values) c0.Level = 6;
            var r2 = Finish(Send("quest.old_shrine", Outcome.Success));
            Assert.IsNull(r2.BlueprintGranted);
            Assert.IsTrue(r2.SubstituteGranted);
            Assert.AreEqual(count, _g.Production.System.State.Blueprints.Count, "no duplicate blueprint");
            Assert.AreEqual(copper + 2, Log.SharedAvailable("resource.copper_ingot"));
        }

        [Test]
        public void PartialSuccessInjuresAndRecoversRevealingOneFact()
        {
            StockStarterKit();
            Finish(Send("quest.roadside_patrol", Outcome.Success));
            Finish(Send("quest.briar_track", Outcome.Success));
            // Test fixture: seasoned contractors, so someone is willing to take the harder Quarry Road.
            foreach (var c0 in _g.Jobs.State.Contractors.Values) c0.Level = 6;
            var e = Send("quest.quarry_road", Outcome.PartialInjury);   // heavy blows is not yet identified
            var gold = _g.Jobs.State.Gold;
            var r = Finish(e);
            Assert.IsTrue(r.Injured);
            Assert.AreEqual(gold + (int)Math.Floor(48 * 0.4), _g.Jobs.State.Gold);
            CollectionAssert.AreEqual(new[] { "threat.heavy_blows" }, r.RevealedThreats);
            Assert.IsTrue(Board.Quest("quest.quarry_road").Threats[0].Known);
            Assert.IsNull(r.BlueprintGranted, "no success-only unlock on a partial");
            var c = Board.Contractor(e.ContractorId);
            Assert.AreEqual(ContractorStatus.Injured, c.Status);
            Board.Advance(10 * 60 * 1000);
            Assert.AreEqual(ContractorStatus.Available, c.Status, "back after about 10 minutes");
        }

        [Test]
        public void FailureSpendsTheTonicAndReturnsTheRest()
        {
            StockStarterKit();
            var e = Send("quest.roadside_patrol", Outcome.Failure);
            var tonic = e.Items.First(i => Log.DefinitionOfItem(i) == "item.recovery_tonic");
            var r = Finish(e);
            CollectionAssert.Contains(r.ConsumedItems, "item.recovery_tonic");
            Assert.IsFalse(Log.State.ItemDefinitions.ContainsKey(tonic));
            foreach (var i in e.Items.Where(i => i != tonic)) CollectionAssert.Contains(Log.StoreItems, i);
            CollectionAssert.IsEmpty(Log.Audit());
        }

        [Test]
        public void CompletionAppliesExactlyOnce()
        {
            StockStarterKit();
            var e = Send("quest.roadside_patrol", Outcome.Success);
            _g.Jobs.AdvanceMs(e.EndMs - Board.State.SimMs + 1);
            var gold = _g.Jobs.State.Gold;
            Assert.IsNull(Board.Complete(e), "a second completion of the same expedition does nothing");
            _g.Jobs.AdvanceMs(100_000);
            Assert.AreEqual(gold, _g.Jobs.State.Gold);
            var again = Boot();
            again.Jobs.AdvanceMs(100_000);
            Assert.AreEqual(gold, again.Jobs.State.Gold, "a restart does not replay the reward");
            Assert.AreEqual(1, again.Jobs.State.Results.Count);
        }

        [Test]
        public void SimultaneousCompletionsResolveInStableOrder()
        {
            StockStarterKit();
            var a = Send("quest.local_supply_survey", null);
            var b = Send("quest.roadside_patrol", Outcome.Success);
            b.EndMs = a.EndMs;   // same completion time
            _g.Jobs.AdvanceMs(Math.Max(a.EndMs, b.EndMs) - Board.State.SimMs + 1);
            var ids = _g.Jobs.State.Results.Select(r => r.ExpeditionId).ToList();
            CollectionAssert.AreEqual(new[] { a.Id, b.Id }.OrderBy(x => x, StringComparer.Ordinal), ids);
        }

        [Test]
        public void SafeSurveyPaysEightAndRaisesReputation()
        {
            _g.Jobs.DebugSetGold(0);
            var e = Send("quest.local_supply_survey", Outcome.Death);   // forcing never applies to the authored safe result
            var r = Finish(e);
            Assert.AreEqual(Outcome.Success, r.Outcome);
            Assert.IsFalse(r.Forced);
            Assert.AreEqual(8, _g.Jobs.State.Gold);
            Assert.AreEqual(210, _g.Jobs.State.ReputationTenths, "+1 base at reputation 20");
            Assert.AreEqual(0, Board.Contractor(e.ContractorId).Xp, "no XP from the survey");
            Assert.IsNull(r.BlueprintGranted);
        }

        [Test]
        public void V6FixtureMigratesToV7()
        {
            var json = File.ReadAllText("Assets/Ravenholm/Tests/Fixtures/save_v6_fixture.json");
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(6, from);
            Assert.AreEqual(SaveEnvelope.CurrentSchemaVersion, env.SchemaVersion);
            Assert.IsNotNull(env.Jobs.Progress);
            Assert.AreEqual(0, env.Jobs.Expeditions[0].ResolverVersion, "old expeditions are marked for input reconstruction");
        }
    }
}
