using System.Linq;
using NUnit.Framework;
using UnityEngine;
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
            var slot = 6;   // a tree two rows back from the cliff (not every spot has one: some are open ground)
            while (!sim.CanChop(1, slot)) slot++;
            var species = sim.SpeciesAt(1, slot);
            Assert.AreEqual(species, sim.SpeciesAt(1, slot), "a tree is what it is, before it is cut");
            Assert.IsTrue(Fell(sim, 1, slot), "not just the cliff edge: any tree");
            Assert.GreaterOrEqual(ledger.Carry.Count(x => x == species), sim.Config.UnitsPerTree, "its logs, straight into the carry");
            Assert.IsFalse(sim.CanChop(1, slot), "a felled tree is a stump");
            Assert.AreEqual(new GroveCell(1, slot), sim.Record.LastChop);
            var open = 0;
            for (var s = 0; s < 90; s++) if (sim.IsRock(1, s) && sim.TerrainHeight(1, s) < sim.Grove.RockHeight) open++;
            Assert.Greater(open, 10, "plenty of spots are open ground, to move around in");
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
            var first = 4;
            while (!sim.CanChop(1, first)) first++;   // the first tree past the cliff edge (some spots are open ground)
            Assert.IsTrue(Fell(sim, 1, first));
            Assert.IsTrue(sim.GroveRoutineNext(out var next));
            var g = sim.Grove;
            int Dist(int area, int slot) => System.Math.Abs(g.RowOf(slot) - g.RowOf(first)) + System.Math.Abs(g.AlongOf(area, slot) - g.AlongOf(1, first));
            var nearest = int.MaxValue;
            for (var area = 1; area <= 4; area++)
            for (var slot = 0; slot < g.Rows * g.AreaLength; slot++)
                if (sim.CanChop(area, slot)) nearest = System.Math.Min(nearest, Dist(area, slot));
            Assert.AreEqual(nearest, Dist(next.Area, next.Slot), "the next tree is the nearest to the last one");
            sim.AddWorker(new WorkerRecord { Id = SiteSimulator.PlayerId, Name = "You", ToolId = "t10", PackId = "p6" });
            for (var i = 0; i < 600; i++) sim.Advance(1000);
            Assert.Greater(sim.Record.DepositedTotal, 3 * sim.Config.UnitsPerTree, "the routine keeps cutting (more than three trees' logs)");
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
        public void StumpsGrowBackUnlessPulledOut()
        {
            var ledger = new FakeLedger();
            var sim = Make(ledger);
            sim.Grove.RegrowMs = 60000;
            Assert.IsTrue(Fell(sim, 1, 0));
            Assert.IsTrue(sim.IsStump(1, 0), "a felled tree leaves a stump");
            sim.Advance(59000);
            Assert.IsTrue(sim.IsStump(1, 0), "not yet");
            sim.Advance(2000);
            Assert.IsTrue(sim.CanChop(1, 0), "left standing, the stump grows back into its tree");
            Assert.AreEqual(sim.SpeciesAt(1, 0), sim.SpeciesAt(1, 0));
            Assert.IsTrue(Fell(sim, 1, 0));
            var logs = ledger.Carry.Count;
            Assert.IsTrue(sim.ClearStump(1, 0), "hitting it further pulls it out");
            Assert.AreEqual(logs + 1, ledger.Carry.Count, "for a log");
            sim.Advance(120000);
            Assert.IsFalse(sim.CanChop(1, 0) || sim.IsStump(1, 0), "a pulled-out stump never grows back");
            Assert.IsTrue(sim.IsCleared(1, 0));
            Assert.AreEqual(ledger.CreatedTotal, ledger.Carry.Count + sim.HeldUnits().Count());
        }

        /// <summary>The prototype's organic forest (Kevin's favourite): rolling hills to peaks of seven, thinner trees up the slopes.</summary>
        [Test]
        public void TheForestHasOrganicHillsAndTreesThinningUpTheSlopes()
        {
            var sim = Make(new FakeLedger());
            var g = sim.Grove;
            int Cell(int back, int along, out int area) { area = along / g.AreaLength + 1; return back * g.AreaLength + along % g.AreaLength; }
            int H(int back, int along) => sim.TerrainHeight(along / g.AreaLength + 1, Cell(back, along, out _));
            int max = 0, flat = 0, cells = 0, bigSteps = 0, steps = 0;
            int treesLow = 0, low = 0, treesHigh = 0, high = 0;
            for (var along = 0; along < 120; along++)
            for (var back = 0; back < g.Rows; back++)
            {
                var h = H(back, along);
                max = Mathf.Max(max, h);
                cells++;
                if (h == 0) flat++;
                if (back + 1 < g.Rows) { steps++; if (Mathf.Abs(H(back + 1, along) - h) > 2) bigSteps++; }
                var slot = Cell(back, along, out var area);
                if (back >= 1 && h <= 1) { low++; if (sim.CanChop(area, slot)) treesLow++; }
                if (h >= 4) { high++; if (sim.CanChop(area, slot)) treesHigh++; }
            }
            Assert.GreaterOrEqual(max, 5, "hills rise high somewhere");
            Assert.Greater(flat, cells / 4, "plenty of flat valley floor to walk");
            Assert.Less(bigSteps, steps / 20, "rolling hills: the ground mostly changes a block or two at a time");
            for (var along = 0; along < 120; along++) Assert.AreEqual(0, H(0, along), "the cliff edge's row stays flat");
            for (var along = 0; along < g.FlatEntrance; along++)
            for (var back = 0; back <= g.FlatEntrance; back++) Assert.AreEqual(0, H(back, along), "a flat way in from the gate");
            Assert.Greater(high, 0);
            Assert.Greater(treesLow / (float)low, 2f * treesHigh / high, "trees are thick in the valleys and thin up the hills");
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
