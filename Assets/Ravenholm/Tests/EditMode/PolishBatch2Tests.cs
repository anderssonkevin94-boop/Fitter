using System;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Presentation;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>Polish batch 2: Store pinning, transaction IDs, developer tools, worker bottlenecks, colour-free markers.</summary>
    public sealed class PolishBatch2Tests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");

        private GameServices Boot()
        {
            var g = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        [Test]
        public void PinsAreSavedAndDoNotTouchOwnership()
        {
            var g = Boot();
            g.Logistics.System.CreateStoreItem("item.cedar_bow", "fixture");
            Assert.IsTrue(g.Logistics.TogglePin("item.cedar_bow"));
            g.Scheduler.SaveNow("test");
            var back = Boot();
            CollectionAssert.Contains(back.Logistics.System.State.PinnedDefinitions, "item.cedar_bow");
            Assert.IsFalse(back.Logistics.TogglePin("item.cedar_bow"), "second tap unpins");
            CollectionAssert.IsEmpty(back.Logistics.System.Audit());
        }

        [Test]
        public void TransactionsCarryIncreasingIds()
        {
            var g = Boot();
            g.Logistics.System.ProduceShared("resource.iron_ingot", 1, "a");
            g.Logistics.System.ProduceShared("resource.iron_ingot", 1, "b");
            var h = g.Logistics.System.History.ToList();
            Assert.Greater(h[0].Id, h[1].Id, "newest first, unique increasing IDs");
            StringAssert.StartsWith("#", h[0].ToString());
        }

        [Test]
        public void RevealKnowledgeRevealsEveryThreatAndIsRecorded()
        {
            var g = Boot();
            Assert.Greater(g.Jobs.DebugRevealKnowledge(), 0);
            foreach (var id in g.Jobs.Content.QuestIds) Assert.IsTrue(g.Jobs.Board.Quest(id).Threats.All(t => t.Known), id);
            CollectionAssert.Contains(g.Scheduler.Assisted.ToList(), "reveal knowledge");
        }

        [Test]
        public void BottleneckAccountingSeesStalledWorkers()
        {
            var g = Boot();
            var mine = g.Sites.Get("site.mine");
            g.Sites.StartRoutine("site.mine");   // solo play: the supplier on a mining routine
            mine.ResetMeasurement();
            g.Simulation.Advance(8 * 3_600_000L, 8 * 3_600_000L);   // nobody hauls: staging fills or the section waits for a choice
            Assert.Greater(mine.ObservedWorkerMs, 0);
            Assert.Greater(mine.StagingFullWorkerMs + mine.WaitingForChoiceWorkerMs, mine.ObservedWorkerMs / 2, mine.BottleneckText);
            StringAssert.StartsWith("blocked", mine.BottleneckText);
        }

        [Test]
        public void EveryRiskWordHasItsOwnMarker()
        {
            var markers = Enum.GetValues(typeof(RiskBand)).Cast<RiskBand>().Select(LoadoutPanel.BandMarker).ToList();
            Assert.AreEqual(markers.Count, markers.Distinct().Count(), "never colour alone (GDD 19)");
        }
    }
}
