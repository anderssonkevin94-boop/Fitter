using System;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>Mine sideways tunnels: one face per (level, side), dug in that level's layer, in parallel with the shaft.</summary>
    public sealed class TunnelTests
    {
        private static void FinishSection(SiteSimulator sim)
        {
            sim.DebugCompleteSection();
            for (var i = 0; i < 2000 && sim.Record.Status != SiteStatus.AwaitDecision; i++) sim.Advance(1000);
            Assert.AreEqual(SiteStatus.AwaitDecision, sim.Record.Status);
        }

        private static void RunTunnel(SiteSimulator sim, TunnelRecord t)
        {
            for (var i = 0; i < 20000 && t.Status != SiteStatus.AwaitDecision; i++) sim.Advance(1000);
            Assert.AreEqual(SiteStatus.AwaitDecision, t.Status, "tunnel stretch never finished");
        }

        /// <summary>Levels 1 (depth 1), 2 (depth 1), now digging level 3 at depth 2.</summary>
        private static SiteSimulator TwoLevelsDug(FakeLedger ledger, int workers = 2)
        {
            var sim = SiteTests.Make(SiteTests.MineConfig(0), ledger, pack: "p6", workers: workers);
            FinishSection(sim);
            Assert.IsTrue(sim.ChooseContinue());
            FinishSection(sim);
            Assert.IsTrue(sim.ChooseDeeper());
            return sim;
        }

        private static void AssertConserved(FakeLedger ledger, SiteSimulator sim) =>
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + sim.HeldUnits().Count(), "every created unit is staged or held");

        [Test]
        public void TunnelsLeaveFromDugLevelsBelowTheTownGroundOnly()
        {
            var sim = TwoLevelsDug(new FakeLedger());
            Assert.IsFalse(sim.CanStartTunnel(1, TunnelSide.Left), "level 1 is the town's own ground");
            Assert.IsFalse(sim.CanStartTunnel(3, TunnelSide.Left), "level 3 is still being dug");
            Assert.IsTrue(sim.CanStartTunnel(2, TunnelSide.Left));
            Assert.AreEqual(1, sim.StartTunnel(2, TunnelSide.Left));
            Assert.IsFalse(sim.CanStartTunnel(2, TunnelSide.Left), "one tunnel per level and side");
            Assert.AreEqual(2, sim.StartTunnel(2, TunnelSide.Right));
        }

        [Test]
        public void TunnelDigsItsLevelsLayerInParallelWithTheShaft()
        {
            var ledger = new FakeLedger();
            var sim = TwoLevelsDug(ledger);
            var c = sim.Config;
            var face = sim.StartTunnel(2, TunnelSide.Left);
            var t = sim.Tunnel(face);
            Assert.AreEqual(1, t.Depth, "level 2 was dug in depth 1");
            Assert.AreEqual(2, t.K, "one step above the level's own section (k = 1)");
            Assert.AreEqual(SiteMath.TunnelDurability(c, 1, 2) * 1000, t.DurabilityMicro);
            Assert.AreEqual(Math.Round(SiteMath.Durability(c, 1, 2) / 4.0), SiteMath.TunnelDurability(c, 1, 2), "same per-block effort as a section");
            Assert.AreEqual(6, sim.TunnelUnitsPerStretch, "a quarter of a section's 24 units");

            Assert.IsTrue(sim.AssignWorker("w1", face));
            var shaftBefore = sim.Record.SectionDoneMicro;
            var createdBefore = ledger.CreatedTotal;
            RunTunnel(sim, t);
            Assert.AreEqual(6, t.Emitted);
            Assert.Greater(sim.Record.SectionDoneMicro, shaftBefore, "the shaft kept going with the other miner");
            Assert.GreaterOrEqual(ledger.CreatedTotal - createdBefore, 6);
            Assert.AreEqual(4, sim.TunnelCellsDug(t));
            AssertConserved(ledger, sim);

            // Never extended automatically; the tunnel miner waits.
            sim.Advance(600000);
            Assert.AreEqual(SiteStatus.AwaitDecision, t.Status);
            Assert.AreEqual(WorkerState.AwaitDepthChoice, sim.Record.Workers[1].State);
            Assert.IsTrue(sim.ExtendTunnel(face));
            Assert.AreEqual(3, t.K);
            Assert.AreEqual(1, t.Stretches);
            Assert.Greater(t.DurabilityMicro, SiteMath.TunnelDurability(c, 1, 2) * 1000, "effort rises per stretch");
            Assert.AreEqual(WorkerState.WalkToWork, sim.Record.Workers[1].State);
            RunTunnel(sim, t);
            Assert.AreEqual(8, sim.TunnelCellsDug(t));
            AssertConserved(ledger, sim);
        }

        [Test]
        public void ReassigningAMinerCarriesThePackUpFirst()
        {
            var ledger = new FakeLedger();
            var sim = TwoLevelsDug(ledger, workers: 1);
            for (var i = 0; i < 200 && sim.Record.Workers[0].State != WorkerState.Working; i++) sim.Advance(100);
            sim.Advance(3000);
            var face = sim.StartTunnel(2, TunnelSide.Right);
            Assert.IsTrue(sim.AssignWorker("w0", face));
            Assert.AreEqual(WorkerState.ReturnToStaging, sim.Record.Workers[0].State);
            var shaft = sim.Record.SectionDoneMicro;
            RunTunnel(sim, sim.Tunnel(face));
            Assert.AreEqual(shaft, sim.Record.SectionDoneMicro, "nobody is left on the shaft");
            AssertConserved(ledger, sim);
        }

        [Test]
        public void TunnelResultsDoNotDependOnFrameSizes()
        {
            SiteRecord Run(long step)
            {
                var sim = TwoLevelsDug(new FakeLedger());
                sim.AssignWorker("w1", sim.StartTunnel(2, TunnelSide.Left));
                for (long ms = 0; ms < 240000; ms += step) sim.Advance(step);
                return sim.Record;
            }
            var a = Run(1000);
            var b = Run(37);
            Assert.AreEqual(a.Tunnels[0].DoneMicro, b.Tunnels[0].DoneMicro);
            Assert.AreEqual(a.Tunnels[0].Emitted, b.Tunnels[0].Emitted);
            Assert.AreEqual(a.SectionDoneMicro, b.SectionDoneMicro);
            Assert.AreEqual(a.Rng.State, b.Rng.State);
        }
    }

    public sealed class TunnelPersistenceTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

        private GameServices Boot()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");
            var g = new GameServices(new ManualClock(T0), cat, balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            foreach (var site in cat.Sites)
                g.Logistics.RegisterContainer(site.StagingContainerId, Domain.Logistics.ContainerKind.RawPile, 0f, 100f, 0.45f, null);
            return g;
        }

        private static void Finish(GameServices g)
        {
            g.Sites.DebugCompleteSection("site.mine");
            for (var i = 0; i < 900 && g.Sites.Get("site.mine").Record.Status != SiteStatus.AwaitDecision; i++) g.Sites.Tick(1f);
        }

        [Test]
        public void TunnelsAndMinerFacesSurviveRestart()
        {
            var a = Boot();
            Finish(a);
            Assert.IsTrue(a.Sites.ChooseContinue("site.mine"));
            Finish(a);
            Assert.IsTrue(a.Sites.ChooseContinue("site.mine"));
            var miner = a.Sites.Get("site.mine").Record.Workers[0].Id;
            var face = a.Sites.StartTunnel("site.mine", 2, TunnelSide.Left, miner);
            Assert.AreEqual(1, face);
            for (var i = 0; i < 40; i++) a.Sites.Tick(1f);
            a.Scheduler.SaveNow("test");

            var b = Boot();
            for (var i = 0; i < 40; i++) { a.Sites.Tick(1f); b.Sites.Tick(1f); }
            var ra = a.Sites.Get("site.mine").Record;
            var rb = b.Sites.Get("site.mine").Record;
            Assert.AreEqual(1, rb.Tunnels.Count);
            Assert.AreEqual(TunnelSide.Left, rb.Tunnels[0].Side);
            Assert.AreEqual(2, rb.Tunnels[0].Level);
            Assert.AreEqual(1, rb.Workers[0].Face);
            Assert.AreEqual(ra.Tunnels[0].DoneMicro, rb.Tunnels[0].DoneMicro);
            Assert.AreEqual(ra.Rng.State, rb.Rng.State);
            CollectionAssert.IsEmpty(b.Logistics.System.Audit());
        }
    }
}
