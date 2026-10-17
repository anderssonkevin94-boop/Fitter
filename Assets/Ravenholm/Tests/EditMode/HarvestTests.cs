using System;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Gathering by hand (Kevin's playtest, 2026-10-17): a full carry stops the swing, so no pile of logs builds up out of
    /// sight; after unloading, the next tree gives its logs one at a time, never a full carry at the first chop.
    /// </summary>
    public sealed class HarvestTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        /// <summary>Standing trees near the road that the basic tool can cut.</summary>
        private static (int x, int y) NextTree(GameServices g, double ahead)
        {
            var forest = g.Sites.Get("site.forest");
            var ridge = forest.Config.Grove.Ridge;
            for (var s = ahead; s < ridge.Length; s += 1)
            {
                var (px, py) = ridge.PointAt(ridge.CampS + s);
                for (var k = -20; k <= 20; k++)
                {
                    var (x, y) = ((int)px + k, (int)py - k);
                    if (forest.CanChop(x, y) && !g.Sites.ToolTooWeak(x, y)) return (x, y);
                }
            }
            Assert.Fail("no tree to cut");
            return default;
        }

        [Test]
        public void AFullCarryStopsTheSwingAndTheNextTreeGivesOneLogAtATime()
        {
            var g = Boot();
            var log = g.Logistics.System;
            var forest = g.Sites.Get("site.forest");
            g.Sites.SpeedMultiplier = 20f;
            // Chop until the carry is full, then keep swinging at the tree in hand.
            var guard = 0;
            while (!log.IsFull && guard++ < 50)
            {
                var t = NextTree(g, 6);
                for (var i = 0; i < 4000 && !forest.IsFelled(t.x, t.y) && !log.IsFull; i++) g.Sites.PlayerChopCell("site.forest", t.x, t.y, 0.1f);
            }
            Assert.IsTrue(log.IsFull, "the carry filled");
            var tree = NextTree(g, 6);
            // (The tree that filled the carry may leave its last log waiting at the site; nothing more piles up.)
            var waiting = forest.Record.FacePending.Count;
            Assert.LessOrEqual(waiting, 1, "at most the filling tree's last log waits");
            for (var i = 0; i < 200; i++) Assert.AreEqual(0, g.Sites.PlayerChopCell("site.forest", tree.x, tree.y, 0.1f), "no logs while the carry is full");
            Assert.AreEqual(waiting, forest.Record.FacePending.Count, "and none piling up at the site");

            // Unloaded: the next chops bring the logs one at a time.
            log.DebugUnloadCarry();
            Assert.AreEqual(0, log.CarryCount);
            var max = 0;
            for (var i = 0; i < 4000 && !forest.IsFelled(tree.x, tree.y); i++)
            {
                var before = log.CarryCount;
                g.Sites.PlayerChopCell("site.forest", tree.x, tree.y, 0.05f);
                max = Math.Max(max, log.CarryCount - before);
            }
            Assert.LessOrEqual(max, 2, "never more than a log at a swing (and now and then the bonus herb with it)");
            Assert.Less(log.CarryCount, 12, "a tree doesn't fill the carry at once");
            CollectionAssert.IsEmpty(log.Audit());
        }

        [Test]
        public void LogsLeftWaitingFromAnOlderSaveComeBackOneAtATime()
        {
            var g = Boot();
            var log = g.Logistics.System;
            var forest = g.Sites.Get("site.forest");
            // As an older save left them: logs waiting at the site (already counted as gathered).
            for (var i = 0; i < 10; i++)
            {
                log.RecordCreated("resource.cedar_log");
                forest.Record.FacePending.Add("resource.cedar_log");
            }
            g.Sites.SpeedMultiplier = 5f;
            var tree = NextTree(g, 6);
            // Each unit gathered by hand brings at most one waiting log with it: never more waiting logs in a swing than
            // units gathered (a log, and now and then its bonus herb).
            var worst = 0;
            for (var i = 0; i < 4000 && !forest.IsFelled(tree.x, tree.y); i++)
            {
                var before = log.CarryCount;
                var waitingBefore = forest.Record.FacePending.Count;
                g.Sites.PlayerChopCell("site.forest", tree.x, tree.y, 0.05f);
                var fromWaiting = waitingBefore - forest.Record.FacePending.Count;
                var gathered = log.CarryCount - before - fromWaiting;
                Assert.LessOrEqual(fromWaiting, gathered, "no more waiting logs than units gathered at a swing");
                worst = Math.Max(worst, log.CarryCount - before);
            }
            Assert.LessOrEqual(worst, 4, "never a jump: at most a log, its herb and a waiting one each");
            Assert.Less(forest.Record.FacePending.Count, 10, "the waiting logs are coming back");
        }
    }
}
