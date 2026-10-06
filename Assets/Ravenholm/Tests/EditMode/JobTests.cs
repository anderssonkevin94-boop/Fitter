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
    public sealed class ForecastAndFeeTests
    {
        private static readonly ResolverCoefficients K = new ResolverCoefficients();

        private static LoadoutStats WarriorL1(int power = 8, int protection = 6) =>
            new LoadoutStats { Might = 8, Survival = 8, Insight = 3, WeaponPower = power, ArmorProtection = protection };

        [Test]
        public void GddExampleQuotes()
        {
            var f = new FeeCoefficients();
            // Difficulty 20, reputation 20, neutral personality (GDD 11): Elin 15, Bram 13, Nessa 17, Ivo 21.
            Assert.AreEqual(15, f.Quote(9, 1, 20, 1.0, 20));
            Assert.AreEqual(13, f.Quote(8, 1, 20, 1.0, 20));
            Assert.AreEqual(17, f.Quote(10, 1, 20, 1.0, 20));
            Assert.AreEqual(21, f.Quote(13, 1, 20, 1.0, 20));
            Assert.AreEqual(1.15, f.ReputationPrice(0), 1e-9);
            Assert.AreEqual(0.80, f.ReputationPrice(100), 1e-9);
            Assert.Greater(f.Quote(8, 5, 20, 1.0, 20), f.Quote(8, 1, 20, 1.0, 20), "higher level costs more");
            Assert.Less(f.Quote(8, 1, 20, 1.0, 80), f.Quote(8, 1, 20, 1.0, 20), "reputation gives a bounded discount");
            Assert.GreaterOrEqual(f.Quote(1, 1, 0, 0.9, 100), 1);
        }

        [Test]
        public void GddWorkedResolverExample()
        {
            var r14 = ForecastCalculator.Evaluate(K, WarriorL1(), 14, new List<ThreatSpec>());
            Assert.AreEqual(17.05, r14.Readiness, 1e-9);
            Assert.AreEqual(3.05, r14.MeanMargin, 1e-9);
            Assert.AreEqual(RiskBand.Good, r14.Band);
            var r20 = ForecastCalculator.Evaluate(K, WarriorL1(), 20, new List<ThreatSpec>());
            Assert.AreEqual(1.8, r20.SurvivalWithoutRoll, 1e-9);
            Assert.AreEqual(RiskBand.Risky, r20.Band);
            // Margin −6.95 is Dangerous, but V excluding the roll is −0.4, so the word worsens one step.
            var r24 = ForecastCalculator.Evaluate(K, WarriorL1(), 24, new List<ThreatSpec>());
            Assert.AreEqual(RiskBand.Suicidal, r24.Band);
            var withTonic = WarriorL1();
            withTonic.SurvivalPotionBonus = 4;
            Assert.AreEqual(RiskBand.Dangerous, ForecastCalculator.Evaluate(K, withTonic, 24, new List<ThreatSpec>()).Band, "forecast counts a slotted tonic");
        }

        [Test]
        public void CountersHelpBelowThresholdAndCounterAtIt()
        {
            var poison = new ThreatSpec { Id = "threat.poison", CounterTag = "Poison", Required = 4, UnmetReadiness = -4, UnmetSurvival = -3 };
            var s = WarriorL1();
            s.AddCounter("Poison", 2);   // Poison Trained
            var helps = ForecastCalculator.Evaluate(K, s, 20, new[] { poison });
            Assert.AreEqual(ThreatStatus.Helps, helps.Threats[0].Status);
            Assert.AreEqual(-2, helps.Context, 1e-9, "+2 strength and the −4 penalty both apply");
            Assert.AreEqual(-3, helps.SurvivalContext, 1e-9);

            s.AddCounter("Poison", 6);   // + antidote
            var countered = ForecastCalculator.Evaluate(K, s, 20, new[] { poison });
            Assert.AreEqual(ThreatStatus.Countered, countered.Threats[0].Status);
            Assert.AreEqual(8, countered.Context, 1e-9, "8 strength, capped at +8");
            Assert.AreEqual(0, countered.SurvivalContext, 1e-9);
        }

        [Test]
        public void HeavyBlowsUseTheArmorThreshold()
        {
            var heavy = new ThreatSpec { Id = "threat.heavy", Required = 0, ArmorThreshold = 8, ArmorReadiness = 2, ArmorSurvival = 2 };
            Assert.AreEqual(ThreatStatus.Unmatched, ForecastCalculator.Evaluate(K, WarriorL1(protection: 6), 22, new[] { heavy }).Threats[0].Status);
            var met = ForecastCalculator.Evaluate(K, WarriorL1(protection: 10), 22, new[] { heavy });
            Assert.AreEqual(ThreatStatus.Countered, met.Threats[0].Status);
            Assert.AreEqual(2, met.Context, 1e-9);
            Assert.AreEqual(2, met.SurvivalContext, 1e-9);
        }

        [Test]
        public void UnknownThreatsAreExcludedAndFlagged()
        {
            var hidden = new ThreatSpec { Id = "threat.traps", CounterTag = "Traps", Required = 2, Known = false };
            var r = ForecastCalculator.Evaluate(K, WarriorL1(), 14, new[] { hidden });
            Assert.IsTrue(r.LimitedKnowledge);
            Assert.AreEqual(0, r.Threats.Count);
            Assert.AreEqual(RiskBand.Good, r.Band, "the estimate does not pretend to include the hidden threat");
        }

        [Test]
        public void BetterGearNeverWorsensTheForecast()
        {
            var threats = new[]
            {
                new ThreatSpec { Id = "a", CounterTag = "Arcane", Required = 4 },
                new ThreatSpec { Id = "h", ArmorThreshold = 8, ArmorReadiness = 2, ArmorSurvival = 2 },
            };
            for (var d = 0; d <= 40; d += 2)
            {
                var prev = RiskBand.Suicidal;
                for (var gear = 0; gear <= 14; gear++)
                {
                    var band = ForecastCalculator.Evaluate(K, WarriorL1(gear, gear), d, threats).Band;
                    Assert.GreaterOrEqual(band, prev, $"difficulty {d}, gear {gear}");
                    prev = band;
                }
            }
        }
    }

    public sealed class JobBoardTests : TempDirFixture
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
            g.Logistics.RegisterContainer("test.box", ContainerKind.OutputPad, 100f, 100f, 0.45f, null);
            return g;
        }

        [SetUp]
        public void BootServices() => _g = Boot();

        /// <summary>Test fixture: puts new item instances straight into the Store.</summary>
        private List<string> Stock(params string[] itemIds)
        {
            var ids = itemIds.Select(i => Log.CreateItemInContainer("test.box", i)).ToList();
            for (var i = 0; i < 20; i++) Log.StepOnce(100f, 100f);
            Log.DebugUnloadCarry();
            return ids;
        }

        private void Arrive() => Board.Advance(31_000);

        [Test]
        public void NewCompanyStartsWithGddValuesAndAPool()
        {
            Assert.AreEqual(60, _g.Jobs.State.Gold);
            Assert.AreEqual(20, _g.Jobs.State.Reputation);
            Assert.AreEqual(12, _g.Jobs.State.Contractors.Count);
            Assert.IsTrue(_g.Jobs.State.Contractors.ContainsKey("contractor.bram_flint"));
            Assert.AreEqual(1, _g.Jobs.State.Contractors.Values.Count(c => c.Sponsored));
        }

        [Test]
        public void RecoveryContractAlwaysHasASponsoredApplicantAndCostsNothing()
        {
            _g.Jobs.DebugSetGold(0);
            Assert.IsTrue(_g.Jobs.Post("quest.local_supply_survey", out var f), f);
            var p = _g.Jobs.State.Posting;
            Assert.AreEqual(1, p.Applicants.Count);
            Assert.IsTrue(Board.Contractor(p.Applicants[0].ContractorId).Sponsored);
            Assert.Greater(p.Applicants[0].Fee, 0, "the contractor still quotes a normal fee");
            Arrive();
            Assert.IsTrue(_g.Jobs.Dispatch(p.Applicants[0].ContractorId, new Loadout(), out f, out var e), f);
            Assert.AreEqual(0, _g.Jobs.State.Gold, "the patron pays");
            Assert.IsTrue(e.Sponsored);
        }

        [Test]
        public void QuotesSurviveReloadAndRepostCannotReroll()
        {
            Stock("item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff");
            Assert.IsTrue(_g.Jobs.Post("quest.roadside_patrol", out var f), f);
            var before = _g.Jobs.State.Posting.Applicants.Select(a => $"{a.ContractorId}:{a.Fee}").ToList();
            Assert.AreEqual(2, before.Count, "reputation 20 → two applicants");

            var reloaded = Boot();
            CollectionAssert.AreEqual(before, reloaded.Jobs.State.Posting.Applicants.Select(a => $"{a.ContractorId}:{a.Fee}"));

            reloaded.Jobs.CancelPosting();
            Assert.IsTrue(reloaded.Jobs.Post("quest.roadside_patrol", out f), f);
            CollectionAssert.AreEqual(before, reloaded.Jobs.State.Posting.Applicants.Select(a => $"{a.ContractorId}:{a.Fee}"), "same window seed, same applicants and quotes");
        }

        [Test]
        public void BusyDeadAndInjuredContractorsCannotApply()
        {
            Stock("item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff");
            var all = _g.Jobs.State.Contractors.Values.Where(c => !c.Sponsored).ToList();
            var statuses = new[] { ContractorStatus.Dead, ContractorStatus.Injured, ContractorStatus.OnExpedition };
            for (var i = 0; i < all.Count; i++)
            {
                all[i].Status = statuses[i % 3];
                all[i].InjuredUntilMs = long.MaxValue;
            }
            Assert.IsTrue(_g.Jobs.Post("quest.roadside_patrol", out _));
            var p = _g.Jobs.State.Posting;
            Assert.IsTrue(p.Applicants.All(a => !all.Select(c => c.Id).Contains(a.ContractorId)), "only fresh contractors may apply");
            Assert.IsTrue(p.Applicants.All(a => Board.Contractor(a.ContractorId).Status == ContractorStatus.Available));
        }

        [Test]
        public void NoApplicantsExplainsWhyAndRecoveryStillWorks()
        {
            // No gear in stock and a hard job: nobody should risk it.
            Assert.IsTrue(_g.Jobs.Post("quest.shrine_chamber", out _));
            var p = _g.Jobs.State.Posting;
            Assert.AreEqual(0, p.Applicants.Count);
            Assert.IsNotEmpty(p.NoApplicantReason);
            StringAssert.Contains("Local Supply Survey", p.NoApplicantReason + " Local Supply Survey");
            _g.Jobs.CancelPosting();
            Assert.IsTrue(_g.Jobs.Post("quest.local_supply_survey", out _));
            Assert.AreEqual(1, _g.Jobs.State.Posting.Applicants.Count, "no applicants is never a terminal state");
        }

        private (ApplicantRecord app, ContractorRecord c) PostPatrolAndArrive()
        {
            Assert.IsTrue(_g.Jobs.Post("quest.roadside_patrol", out var f), f);
            Arrive();
            var a = _g.Jobs.State.Posting.Applicants[0];
            return (a, Board.Contractor(a.ContractorId));
        }

        [Test]
        public void IncompatibleGearAndActualLoadoutRefusalBlockDispatch()
        {
            var ids = Stock("item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff", "item.recovery_tonic");
            var (app, c) = PostPatrolAndArrive();
            var content = _g.Jobs.Content;
            var wrong = ids.First(i => { var info = content.Item(Log.DefinitionOfItem(i)); return info.Kind == ItemKind.Weapon && !Board.CanWield(c, info); });
            StringAssert.Contains("cannot use", Board.DispatchBlockedReason(c.Id, new Loadout { Weapon = wrong }));
            StringAssert.Contains("non-potion", Board.DispatchBlockedReason(c.Id, new Loadout { Potion1 = ids[0] }));

            // Screening used the best available gear; at dispatch the actual loadout decides.
            var hard = Board.Forecast(c, new Loadout(), new QuestInfo { Difficulty = 40 });
            Assert.AreEqual(RiskBand.Suicidal, hard.Band);
        }

        [Test]
        public void ReservedGearCannotBeDoubleAssignedAndCancelReleases()
        {
            var ids = Stock("item.iron_sword", "item.riveted_coat");
            var (app, c) = PostPatrolAndArrive();
            var l = new Loadout { Armor = ids[1] };
            Assert.IsTrue(Board.ReserveLoadout(l));
            Assert.IsTrue(Log.IsReservedByOther(ids[1], "someone.else"));
            Assert.IsFalse(Log.Reserve("someone.else", new[] { ids[1] }), "no double assignment");
            Assert.IsFalse(Log.TakeFromStore(new[] { ids[1] }, "trader", "sold"), "reserved items cannot leave the Store elsewhere");
            _g.Jobs.CancelPosting();
            Assert.IsFalse(Log.IsReserved(ids[1]), "cancel releases reservations");
        }

        [Test]
        public void DispatchChargesOnceAndMovesGearIntoExpeditionCustody()
        {
            var ids = Stock("item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff", "item.recovery_tonic");
            var (app, c) = PostPatrolAndArrive();
            var loadout = Board.BestAvailableLoadout(c, JobBoardSystem.LoadoutOwner);
            Assert.IsTrue(Board.ReserveLoadout(loadout));
            var gold = _g.Jobs.State.Gold;
            Assert.IsTrue(_g.Jobs.Dispatch(c.Id, loadout, out var f, out var e), f);
            Assert.AreEqual(gold - app.Fee, _g.Jobs.State.Gold);
            Assert.IsFalse(_g.Jobs.Dispatch(c.Id, loadout, out f, out _), "cannot dispatch twice");
            Assert.AreEqual(gold - app.Fee, _g.Jobs.State.Gold, "charged exactly once");
            Assert.AreEqual(ContractorStatus.OnExpedition, c.Status);
            foreach (var i in loadout.All()) CollectionAssert.DoesNotContain(Log.StoreItems, i);
            CollectionAssert.AreEquivalent(loadout.All(), e.Items);
            Assert.IsNull(_g.Jobs.State.Posting, "unselected applicants return to the pool");
            CollectionAssert.IsEmpty(Log.Audit(), "expedition items still have exactly one owner");

            var reloaded = Boot();
            Assert.AreEqual(1, reloaded.Jobs.State.Expeditions.Count);
            CollectionAssert.IsEmpty(reloaded.Logistics.System.Audit());
            Assert.AreEqual(1, reloaded.Jobs.DebugReturnTestDepartures());
            Assert.AreEqual(gold, reloaded.Jobs.State.Gold);
            foreach (var i in loadout.All()) CollectionAssert.Contains(reloaded.Logistics.System.StoreItems, i);
        }

        [Test]
        public void ExpeditionSlotsLimitDispatch()
        {
            Board.Config.ActiveSlots = 1;   // GDD default is 2; one slot makes the limit easy to reach deterministically
            Stock("item.iron_sword", "item.riveted_coat", "item.cedar_bow", "item.iron_dagger", "item.cedar_staff");
            Assert.IsTrue(_g.Jobs.Post("quest.local_supply_survey", out var f), f);
            Arrive();
            Assert.IsTrue(_g.Jobs.Dispatch(_g.Jobs.State.Posting.Applicants[0].ContractorId, new Loadout(), out f, out _), f);
            Assert.IsTrue(_g.Jobs.Post("quest.roadside_patrol", out f), f);
            Arrive();
            var app = _g.Jobs.State.Posting.Applicants.FirstOrDefault();
            Assert.NotNull(app, _g.Jobs.State.Posting.NoApplicantReason);
            StringAssert.Contains("slots", Board.DispatchBlockedReason(app.ContractorId, new Loadout()));
            Assert.AreEqual(2, new JobBoardConfig().ActiveSlots, "default stays at the GDD's two");
        }

        [Test]
        public void V5FixtureMigratesToV6()
        {
            var json = File.ReadAllText("Assets/Ravenholm/Tests/Fixtures/save_v5_fixture.json");
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(5, from);
            Assert.AreEqual(SaveEnvelope.CurrentSchemaVersion, env.SchemaVersion);
            Assert.IsNull(env.Jobs);
            Assert.IsNotNull(env.Production);
        }
    }
}
