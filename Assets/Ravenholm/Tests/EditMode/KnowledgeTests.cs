using System;
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
    /// <summary>GDD 13 knowledge stages: clue on the second success, route bonus on the third, one failure observation.</summary>
    public sealed class KnowledgeTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
        private GameServices _g;
        private int _n;

        [SetUp]
        public void BootServices()
        {
            _g = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            _g.Start();
            _g.Jobs.State.Progress["quest.briar_track"] = new QuestProgress { Unlocked = true };
        }

        /// <summary>A minimal expedition through the real completion path (hiring and dispatch are tested elsewhere).</summary>
        private ResultRecord Run(string quest, Outcome o)
        {
            var board = _g.Jobs.Board;
            var c = _g.Jobs.State.Contractors.Values.First(x => !x.Sponsored && x.Status == ContractorStatus.Available);
            var q = board.Quest(quest);
            var e = new ExpeditionRecord
            {
                Id = $"expedition.k{++_n}", QuestId = quest, ContractorId = c.Id, StartMs = _g.Jobs.State.SimMs, EndMs = _g.Jobs.State.SimMs + 1000,
                Seed = 1, Snapshot = board.StatsWith(c, new Loadout()), Difficulty = q.Difficulty, RouteBonus = q.RouteBonus,
            };
            e.Threats.AddRange(q.Threats);
            c.Status = ContractorStatus.OnExpedition;
            _g.Jobs.State.Expeditions.Add(e);
            _g.Jobs.DebugForceNext(o);
            _g.Simulation.Advance(2000, 0);
            return _g.Jobs.State.Results.Last();
        }

        [Test]
        public void SecondSuccessGivesAClueAndTheThirdARouteBonus()
        {
            var board = _g.Jobs.Board;
            var first = Run("quest.briar_track", Outcome.Success);
            CollectionAssert.IsEmpty(first.Facts, "the first success reveals the primary threat only");
            var second = Run("quest.briar_track", Outcome.Success);
            StringAssert.Contains("gem", second.Facts.Single());
            Assert.AreEqual(0, board.Quest("quest.briar_track").RouteBonus);

            var c = _g.Jobs.State.Contractors.Values.First(x => !x.Sponsored);
            var third = Run("quest.briar_track", Outcome.Success);
            StringAssert.Contains("+2 readiness", third.Facts.Single());
            var view = board.Quest("quest.briar_track");
            Assert.AreEqual(2, view.RouteBonus);
            Assert.AreEqual(2, view.KnownFacts.Count);
            var without = ForecastCalculator.Evaluate(board.Config.Resolver, board.StatsWith(c, new Loadout()), view.Difficulty, view.Threats).Readiness;
            Assert.AreEqual(without + 2, board.Forecast(c, new Loadout(), view).Readiness, 1e-9, "the route is part of the forecast");

            var fourth = Run("quest.briar_track", Outcome.Success);
            CollectionAssert.IsEmpty(fourth.Facts, "each fact is learned once");

            _g.Scheduler.SaveNow("test");
            var back = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            back.Start();
            Assert.AreEqual(2, back.Jobs.Board.Quest("quest.briar_track").RouteBonus, "derived from saved progress");
        }

        [Test]
        public void TheRouteBonusIsFrozenIntoTheExpeditionAndSaved()
        {
            _g.Jobs.State.Progress["quest.briar_track"] = new QuestProgress { Unlocked = true, Successes = 3 };
            var board = _g.Jobs.Board;
            var c = _g.Jobs.State.Contractors.Values.First(x => !x.Sponsored);
            var q = board.Quest("quest.briar_track");
            var e = new ExpeditionRecord { Id = "expedition.frozen", QuestId = q.Id, ContractorId = c.Id, EndMs = long.MaxValue / 4, RouteBonus = q.RouteBonus, Snapshot = board.StatsWith(c, new Loadout()) };
            c.Status = ContractorStatus.OnExpedition;
            _g.Jobs.State.Expeditions.Add(e);
            _g.Scheduler.SaveNow("test");
            var back = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            back.Start();
            Assert.AreEqual(2, back.Jobs.State.Expeditions.Single().RouteBonus);
        }

        [Test]
        public void AFailureTeachesOneBasicObservationOnce()
        {
            var r1 = Run("quest.briar_track", Outcome.Failure);
            StringAssert.Contains("someone who knows the woods", r1.Facts.Single());
            Assert.AreEqual(1, _g.Jobs.Board.Quest("quest.briar_track").KnownFacts.Count);
            var r2 = Run("quest.briar_track", Outcome.Failure);
            CollectionAssert.IsEmpty(r2.Facts);
            var partial = Run("quest.briar_track", Outcome.PartialInjury);
            CollectionAssert.IsEmpty(partial.Facts, "partials reveal a threat fact, not the observation");
        }

        [Test]
        public void KnowledgeIsAuthoredAsDataAndStaysSmall()
        {
            foreach (var q in Cat.Quests)
                foreach (var k in q.Knowledge) Assert.That(k.readinessBonus, Is.InRange(0, 3), q.Id);
            Assert.IsEmpty(Cat.Quests.First(q => q.RecoveryContract).Knowledge, "the recovery contract teaches nothing");
            Assert.GreaterOrEqual(Cat.Quests.Count(q => q.Knowledge.Count > 0), 12);
        }
    }
}
