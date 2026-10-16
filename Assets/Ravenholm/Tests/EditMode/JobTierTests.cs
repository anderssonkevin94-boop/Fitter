using System;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The journey, step 5 (2026-10-15): each camp's castle posts its own tier of jobs (and earlier camps'), its summons
    /// first once it can be taken; answering the summons is needed to move on.
    /// </summary>
    public sealed class JobTierTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            g.Logistics.System.CreateStoreItem("item.iron_sword", "test stock");   // gear to lend: the survey is only the fallback
            return g;
        }

        [Test]
        public void EachCampsCastlePostsItsOwnJobsAndEarlierOnes()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            Assert.AreEqual(1, board.Position.Camp);
            Assert.AreEqual(g.Catalogue.CurrentCamp.CampCount, board.Position.CampCount);
            // Open everything up to River Bend's jobs.
            foreach (var id in new[] { "roadside_patrol", "briar_track", "old_shrine" }) board.DebugRecordSuccess("quest." + id);
            Assert.IsTrue(board.IsUnlocked("quest.quarry_road"), "unlocked by the Briar Track");
            CollectionAssert.DoesNotContain(board.CastleCandidates(), "quest.quarry_road", "but River Bend's castle posts it, not Meadow's Edge's");
            Assert.IsTrue(board.CastleCandidates().All(id => board.Quest(id).Camp == 1), "at camp 1: camp 1's jobs only");

            g.Camp.DebugSetIndex(1);
            Assert.AreEqual(2, board.Position.Camp);
            CollectionAssert.Contains(board.CastleCandidates(), "quest.quarry_road", "at River Bend it is posted");
            CollectionAssert.Contains(board.CastleCandidates(), "quest.roadside_patrol", "and earlier camps' jobs too");
        }

        [Test]
        public void TheSummonsComesFirstAndIsNeededToMoveOn()
        {
            var g = Boot();
            var board = g.Jobs.Board;
            var summons = board.SummonsFor(1);
            Assert.AreEqual("quest.old_shrine", summons.Id, "Meadow's Edge's summons");
            Assert.AreEqual("quest.shrine_chamber", board.SummonsFor(2).Id, "River Bend's");
            Assert.IsFalse(board.SummonsAnswered(1));
            board.DebugRecordSuccess("quest.roadside_patrol");   // opens the Old Shrine
            Assert.AreEqual("quest.old_shrine", board.CastleCandidates()[0], "the castle sends for the summons first");

            var there = g.Camp.Levels ? g.Camp.ExitWorld : g.Camp.CentreOf(1);   // gated levels: at the gate
            StringAssert.Contains("summons", g.Camp.MoveOnBlockedReason(there));
            board.DebugRecordSuccess("quest.old_shrine");
            Assert.IsTrue(board.SummonsAnswered(1));
            Assert.IsNull(g.Camp.MoveOnBlockedReason(there), "answered and through: the company can move on");
        }
    }
}
