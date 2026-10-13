using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Sites;

namespace Ravenholm.Tests
{
    /// <summary>The Mine as a dig slab: dig any block, layers reached by digging, routines carry on the last tunnel.</summary>
    public sealed class DigTests
    {
        private static SiteSimulator Make(FakeLedger ledger, int length = 24)
        {
            var c = SiteTests.MineConfig(0);
            c.Dig = new DigSlab { Thickness = 4, Length = length, EntryThickness = 2, EntryLength = 2 };
            ledger.CarryCapacity = 100000;
            return SiteTests.Make(c, ledger, workers: 0);
        }

        private static bool DigOut(SiteSimulator sim, int x, int u, int row, DigFace from = null)
        {
            for (var i = 0; i < 1000; i++)
                if (sim.PlayerDig(x, u, row, 200, 10, from)) return true;
            return false;
        }

        [Test]
        public void TheGroundIsBrokenOnlyAtTheEntryAndBlocksGiveTheirOre()
        {
            var ledger = new FakeLedger();
            var sim = Make(ledger);
            Assert.IsTrue(sim.DigMode);
            Assert.IsFalse(sim.CanDig(3, 10, 0), "the town's and the frontier's ground cannot be dug");
            Assert.IsTrue(sim.CanDig(0, 0, 0), "but the Mine's entry can");
            Assert.IsTrue(DigOut(sim, 0, 0, 0));
            Assert.IsTrue(sim.IsDug(0, 0, 0));
            Assert.IsFalse(sim.CanDig(0, 0, 0), "a dug block is gone");
            Assert.IsTrue(sim.CanDig(3, 10, 1), "below the ground anything can be dug");
            Assert.Greater(ledger.Carry.Count, 0, "units go straight into the carry");
            var ore = sim.OreAt(0, 0, 0, out _);
            Assert.AreEqual(ore ?? "iron", ledger.Carry[0], "the block's own ore, else the layer's baseline ore");
            Assert.AreEqual(ledger.CreatedTotal, ledger.Carry.Count + sim.HeldUnits().Count());
        }

        [Test]
        public void TheElevatorShaftIsOpenFromTheStartAndDiggingItsFloorExtendsIt()
        {
            var sim = Make(new FakeLedger());
            sim.Slab.ElevatorStartRows = 8;   // the quarry has none by default; the elevator still works where one is set up
            Assert.AreEqual(-1, sim.ElevatorBottom, "a bare slab has no shaft until it is opened");
            sim.EnsureElevatorShaft();
            var start = sim.Slab.ElevatorStartRows;
            Assert.AreEqual(start - 1, sim.ElevatorBottom, "open through the first layer from the start");
            Assert.IsTrue(sim.Elevator(0, sim.Slab.ElevatorU, 0) && sim.Elevator(0, sim.Slab.ElevatorU, start - 1));
            Assert.IsFalse(sim.Elevator(0, sim.Slab.ElevatorU + 1, 0), "only its own column");
            Assert.IsTrue(DigOut(sim, 0, sim.Slab.ElevatorU, start));
            Assert.AreEqual(start, sim.ElevatorBottom, "digging the shaft's floor takes it deeper");
            sim.EnsureElevatorShaft();
            Assert.AreEqual(start, sim.ElevatorBottom, "opening it again changes nothing");
        }

        [Test]
        public void TheQuarryPlotRunsBothWaysFromTheCornerForALimitedStretch()
        {
            var sim = Make(new FakeLedger());
            var limit = sim.Slab.MinU;
            Assert.Less(limit, 0, "to the right of the corner (toward the town)");
            Assert.IsTrue(sim.CanDig(0, -1, 2) && sim.CanDig(0, limit, 2), "the plot runs to the limit");
            Assert.IsFalse(sim.CanDig(0, limit - 1, 2), "and no further");
            Assert.IsTrue(sim.CanDig(0, limit, 0), "its surface can be broken anywhere (a pit dug from above)");
            Assert.IsFalse(sim.CanDig(0, limit - 1, 0), "but not past the plot");
            Assert.IsTrue(DigOut(sim, 0, -2, 2));
            Assert.AreEqual(sim.ContinuationOf(10, 2), sim.ContinuationOf(-10, 2), "effort rises the same either way along the cliff");
        }

        [Test]
        public void ScaffoldsGoInOpenSpaceAndCanBeStoodOn()
        {
            var sim = Make(new FakeLedger());
            sim.EnsureElevatorShaft();
            Assert.IsFalse(sim.CanPlaceScaffold(0, 3, 2), "not in rock");
            Assert.IsTrue(DigOut(sim, 0, 3, 2));
            Assert.IsTrue(sim.CanPlaceScaffold(0, 3, 2), "in open space");
            Assert.IsFalse(sim.CanPlaceScaffold(0, sim.Slab.ElevatorU, 2), "never in the elevator shaft");
            Assert.IsTrue(sim.PlaceScaffold(0, 3, 2, "lumber"));
            Assert.IsTrue(sim.Standable(0, 3, 2) && !sim.Solid(0, 3, 2), "something to stand on, not rock");
            Assert.IsFalse(sim.CanDig(0, 3, 2), "and nothing to dig");
            Assert.AreEqual("lumber", sim.RemoveScaffold(0, 3, 2), "taking it down gives its material back");
            Assert.IsFalse(sim.Standable(0, 3, 2));
        }

        [Test]
        public void EffortFollowsTheSectionCurveBlockByBlock()
        {
            var sim = Make(new FakeLedger());
            var c = sim.Config;
            long Plain(int x, int u, int row) => sim.OreAt(x, u, row, out _) == null ? sim.BlockDurabilityMicro(x, u, row) : sim.BlockDurabilityMicro(x, u, row) * 4 / 5;
            Assert.AreEqual(SiteMath.Durability(c, 1, 0) * 1000 / c.UnitsPerSection, Plain(1, 1, 2), 2, "one block = one unit of a section's work");
            Assert.Greater(Plain(1, 20, 2), Plain(1, 1, 2), "staying in a layer costs more the further along the cliff");
            var deep = sim.Slab.FirstRowOf(2);
            Assert.Greater(Plain(1, 1, deep), Plain(1, 1, deep - 1), "the next layer is harder rock");
        }

        [Test]
        public void DiggingIntoTheNextLayerRevealsIt()
        {
            var sim = Make(new FakeLedger());
            Assert.IsFalse(sim.Record.Revealed.ContainsKey(2));
            var deep = sim.Slab.FirstRowOf(2);
            Assert.IsTrue(DigOut(sim, 1, 1, deep));
            Assert.IsTrue(sim.Record.Revealed.ContainsKey(2), "reaching a layer reveals it");
            Assert.AreEqual(2, sim.Record.Depth);
            Assert.IsFalse(sim.ChooseDeeper(), "no choice buttons: digging is the choice");
            Assert.IsFalse(sim.ChooseContinue());
        }

        [Test]
        public void ARoutineCarriesOnTheLastTunnelUntilItIsBlocked()
        {
            var ledger = new FakeLedger();
            var sim = Make(ledger, length: 12);
            // The supplier dug in at row 3 (feet), facing along the cliff (+u).
            var from = new DigFace { X = 1, U = 2, Feet = 4, DX = 0, DU = 1 };
            Assert.IsTrue(DigOut(sim, 1, 3, 2, from));
            Assert.IsTrue(sim.AimRoutineAtLastDig());
            sim.AddWorker(new WorkerRecord { Id = SiteSimulator.PlayerId, Name = "You", ToolId = "t10", PackId = "p6" });
            var dug = sim.Record.Dug.Count;
            for (var i = 0; i < 4000 && sim.Record.Status != SiteStatus.AwaitDecision; i++) sim.Advance(1000);
            Assert.Greater(sim.Record.Dug.Count, dug + 6, "the routine digs on, two blocks high");
            Assert.IsTrue(sim.IsDug(1, 6, 2) && sim.IsDug(1, 6, 3), "straight ahead along the tunnel");
            Assert.IsFalse(sim.IsDug(1, 6, 4), "never the floor");
            Assert.AreEqual(SiteStatus.AwaitDecision, sim.Record.Status, "it waits at the slab's edge");
            Assert.Greater(ledger.StagingCount, 0, "and hauled to staging");
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + ledger.Carry.Count + sim.HeldUnits().Count());
        }

        [Test]
        public void RoutineDiggingDoesNotDependOnFrameSizes()
        {
            SiteRecord Run(long step)
            {
                var sim = Make(new FakeLedger());
                DigOut(sim, 1, 3, 2, new DigFace { X = 1, U = 2, Feet = 4, DX = 0, DU = 1 });
                sim.AimRoutineAtLastDig();
                sim.AddWorker(new WorkerRecord { Id = SiteSimulator.PlayerId, Name = "You", ToolId = "t10", PackId = "p6" });
                for (long ms = 0; ms < 300000; ms += step) sim.Advance(step);
                return sim.Record;
            }
            var a = Run(1000);
            var b = Run(37);
            Assert.AreEqual(a.Dug.Count, b.Dug.Count);
            Assert.AreEqual(a.RoutineFace.U, b.RoutineFace.U);
            Assert.AreEqual(a.Rng.State, b.Rng.State);
        }
    }
}
