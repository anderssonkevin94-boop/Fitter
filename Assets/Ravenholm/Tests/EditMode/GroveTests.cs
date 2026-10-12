using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Sites;

namespace Ravenholm.Tests
{
    /// <summary>The Forest as a grid of trees: cut any tree in reach; routines carry on nearest the last tree cut.</summary>
    public sealed class GroveTests
    {
        private static SiteSimulator Make(FakeLedger ledger)
        {
            var c = SiteTests.ForestConfig();
            c.Grove = new ForestGrove { TreesPerContinuation = c.TreesPerClearing };
            ledger.CarryCapacity = 100000;
            return SiteTests.Make(c, ledger, workers: 0);
        }

        private static bool Fell(SiteSimulator sim, int area, int slot)
        {
            for (var i = 0; i < 4000 && !sim.IsFelled(area, slot); i++) sim.PlayerChopCell(area, slot, 200, 10);
            return sim.IsFelled(area, slot);
        }

        [Test]
        public void AnyTreeCanBeCutAndGivesItsSpeciesLogs()
        {
            var ledger = new FakeLedger();
            var sim = Make(ledger);
            Assert.IsTrue(sim.GroveMode);
            Assert.IsFalse(sim.ChooseContinue() || sim.ChooseDeeper(), "cutting is the choice");
            var species = sim.SpeciesAt(1, 7);
            Assert.AreEqual(species, sim.SpeciesAt(1, 7), "a tree is what it is, before it is cut");
            Assert.IsTrue(Fell(sim, 1, 7), "not just the cliff edge: any tree");
            Assert.GreaterOrEqual(ledger.Carry.Count(x => x == species), sim.Config.UnitsPerTree, "its logs, straight into the carry");
            Assert.IsFalse(sim.CanChop(1, 7), "a felled tree is a stump");
            Assert.AreEqual(new GroveCell(1, 7), sim.Record.LastChop);
            Assert.AreEqual(ledger.CreatedTotal, ledger.Carry.Count + sim.HeldUnits().Count());
        }

        [Test]
        public void FurtherLeftIsTheNextAreaAndFurtherBackCostsMore()
        {
            var sim = Make(new FakeLedger());
            Assert.Greater(sim.TreeDurabilityMicro(1, 6 * 3), sim.TreeDurabilityMicro(1, 0), "further back from the cliff is harder");
            Assert.Greater(sim.TreeDurabilityMicro(2, 0), sim.TreeDurabilityMicro(1, 0), "the next area is tougher wood");
            Assert.IsFalse(sim.Record.Revealed.ContainsKey(2));
            Assert.IsTrue(Fell(sim, 2, 0));
            Assert.IsTrue(sim.Record.Revealed.ContainsKey(2), "cutting into an area reveals it");
            Assert.AreEqual(2, sim.Record.Depth);
        }

        [Test]
        public void ARoutineCutsTheTreesNearestTheLastOne()
        {
            var ledger = new FakeLedger();
            var sim = Make(ledger);
            Assert.IsFalse(sim.AimRoutineAtLastChop(), "nothing cut yet");
            Assert.IsTrue(Fell(sim, 1, 4));
            Assert.IsTrue(sim.GroveRoutineNext(out var next));
            var g = sim.Grove;
            var d = System.Math.Abs(g.RowOf(next.Slot) - g.RowOf(4)) + System.Math.Abs(g.AlongOf(next.Area, next.Slot) - g.AlongOf(1, 4));
            Assert.AreEqual(1, d, "the next tree is right beside the last one");
            sim.AddWorker(new WorkerRecord { Id = SiteSimulator.PlayerId, Name = "You", ToolId = "t10", PackId = "p6" });
            for (var i = 0; i < 600; i++) sim.Advance(1000);
            var felled = sim.Record.Chopped.Keys.Count(c => sim.IsFelled(c.Area, c.Slot));
            Assert.Greater(felled, 3, "the routine keeps cutting");
            Assert.Greater(ledger.StagingCount, 0, "and hauls to staging");
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + ledger.Carry.Count + sim.HeldUnits().Count());
        }

        [Test]
        public void RoutineCuttingDoesNotDependOnFrameSizes()
        {
            SiteRecord Run(long step)
            {
                var sim = Make(new FakeLedger());
                Fell(sim, 1, 0);
                sim.AddWorker(new WorkerRecord { Id = SiteSimulator.PlayerId, Name = "You", ToolId = "t10", PackId = "p6" });
                for (long ms = 0; ms < 300000; ms += step) sim.Advance(step);
                return sim.Record;
            }
            var a = Run(1000);
            var b = Run(37);
            Assert.AreEqual(a.Chopped.Count, b.Chopped.Count);
            foreach (var kv in a.Chopped) Assert.AreEqual(kv.Value, b.Chopped[kv.Key], $"tree {kv.Key}");
            Assert.AreEqual(a.LastChop, b.LastChop);
        }

        [Test]
        public void OldClearingsBecomeStumpsOnTheGrid()
        {
            var s = new SiteRecord { Depth = 2, K = 1 };
            s.SectionDepths.Add(1);
            s.SectionDepths.Add(1);
            s.SectionDepths.Add(2);
            var t = new TreeRecord { Index = 0, DurabilityMicro = 10, DoneMicro = 10 };
            t.Primary.Add("log");
            t.Emitted = 1;
            t.Pending.Add("log");
            s.Trees.Add(t);
            SiteSimulator.ConvertToGrove(s, 6);
            Assert.IsTrue(s.Chopped.ContainsKey(new GroveCell(1, 11)), "two clearings in area 1: twelve trees");
            Assert.IsFalse(s.Chopped.ContainsKey(new GroveCell(1, 12)));
            Assert.IsTrue(s.Chopped.ContainsKey(new GroveCell(2, 5)), "one clearing in area 2");
            Assert.IsTrue(s.Chopped.ContainsKey(new GroveCell(2, 6)), "and the current clearing's felled tree");
            Assert.AreEqual(1, s.FacePending.Count, "its waiting logs are kept");
            Assert.AreEqual(0, s.Trees.Count);
        }
    }
}
