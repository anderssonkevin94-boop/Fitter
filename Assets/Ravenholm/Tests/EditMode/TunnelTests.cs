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
    /// <summary>
    /// The Mine's directions (GDD 6, as directed): Dig Deeper sinks the shaft through a layer of three levels;
    /// Continue Here digs left at a level of the layer, each level its own tunnel face, worked in parallel.
    /// </summary>
    public sealed class TunnelTests
    {
        private static void FinishShaft(SiteSimulator sim)
        {
            sim.DebugCompleteSection();
            for (var i = 0; i < 2000 && !sim.ShaftDone; i++) sim.Advance(1000);
            for (var i = 0; i < 2000 && sim.Record.Workers.Exists(w => w.Face == 0 && w.State != WorkerState.AwaitDepthChoice); i++) sim.Advance(1000);
            Assert.IsTrue(sim.ShaftDone);
        }

        private static void RunTunnel(SiteSimulator sim, TunnelRecord t)
        {
            for (var i = 0; i < 20000 && t.Status != SiteStatus.AwaitDecision; i++) sim.Advance(1000);
            Assert.AreEqual(SiteStatus.AwaitDecision, t.Status, "tunnel stretch never finished");
        }

        private static SiteSimulator Fresh(FakeLedger ledger, int workers = 2) =>
            SiteTests.Make(SiteTests.MineConfig(0), ledger, pack: "p6", workers: workers);

        private static void AssertConserved(FakeLedger ledger, SiteSimulator sim) =>
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + sim.HeldUnits().Count(), "every created unit is staged or held");

        [Test]
        public void TheShaftSinksThroughALayerOfThreeLevels()
        {
            var sim = Fresh(new FakeLedger());
            Assert.IsFalse(sim.ShaftDone, "a new Mine starts by sinking its shaft through depth 1");
            Assert.AreEqual(1, sim.OpenLevels, "only through the town's own ground");
            Assert.IsFalse(sim.CanStartTunnel(2));
            FinishShaft(sim);
            Assert.AreEqual(1, sim.Record.SectionsCompleted);
            Assert.AreEqual(4, sim.OpenLevels, "depth 1 is levels 2-4");
            Assert.AreEqual(SiteStatus.AwaitDecision, sim.Record.Status, "the shaft's miners wait for you");
            for (var level = 2; level <= 4; level++)
            {
                Assert.IsTrue(sim.CanStartTunnel(level), $"level {level}");
                Assert.AreEqual(1, sim.BandOfLevel(level));
            }
            Assert.IsFalse(sim.CanStartTunnel(1), "level 1 is the town's ground");
            Assert.IsFalse(sim.CanStartTunnel(5), "depth 2 is not dug yet");
            Assert.AreEqual(5, sim.FirstLevelOf(2));
            sim.Advance(600000);
            Assert.AreEqual(1, sim.Record.Depth, "nothing is decided for you");
            Assert.AreEqual(0, sim.Record.Tunnels.Count);
        }

        [Test]
        public void ContinueDigsLeftAndEachContinueIsOneSection()
        {
            var ledger = new FakeLedger();
            var sim = Fresh(ledger, workers: 1);
            var c = sim.Config;
            FinishShaft(sim);
            Assert.IsTrue(sim.ChooseContinue(), "Continue Here: dig left in this layer");
            Assert.AreEqual(1, sim.Record.Tunnels.Count);
            var t = sim.Record.Tunnels[0];
            Assert.AreEqual(2, t.Level, "the layer's first level");
            Assert.AreEqual(1, t.Depth);
            Assert.AreEqual(1, t.K, "one step above the shaft's section");
            Assert.AreEqual(1, sim.Record.Workers[0].Face, "the shaft's miner goes there");
            Assert.AreEqual(SiteStatus.Working, sim.Record.Status);
            Assert.AreEqual(SiteMath.Durability(c, 1, 1) * 1000, t.DurabilityMicro, "a stretch is one section");

            RunTunnel(sim, t);
            Assert.AreEqual(c.UnitsPerSection, t.Emitted, "same yield as a section");
            Assert.AreEqual(c.TunnelStretchCells, sim.TunnelCellsDug(t));
            sim.Advance(600000);
            Assert.AreEqual(SiteStatus.AwaitDecision, sim.Record.Status, "never extended automatically");
            Assert.AreEqual(1, sim.WaitingFace());
            Assert.IsTrue(sim.ChooseContinue(), "Continue Here again: one stretch further left");
            Assert.AreEqual(2, t.K);
            Assert.AreEqual(1, t.Stretches);
            Assert.AreEqual(SiteMath.Durability(c, 1, 2) * 1000, t.DurabilityMicro, "effort rises like any continuation");
            RunTunnel(sim, t);
            Assert.AreEqual(2 * c.TunnelStretchCells, sim.TunnelCellsDug(t));
            AssertConserved(ledger, sim);
        }

        [Test]
        public void ALayersLevelsAreWorkedInParallel()
        {
            var ledger = new FakeLedger();
            var sim = Fresh(ledger, workers: 2);
            FinishShaft(sim);
            Assert.IsTrue(sim.ChooseContinue());
            var second = sim.StartTunnel(3);
            Assert.AreEqual(2, second);
            Assert.IsTrue(sim.AssignWorker("w1", second));
            for (var i = 0; i < 120; i++) sim.Advance(1000);
            Assert.Greater(sim.Record.Tunnels[0].DoneMicro + sim.Record.Tunnels[0].Stretches, 0, "level 2 is being dug");
            Assert.Greater(sim.Record.Tunnels[1].DoneMicro + sim.Record.Tunnels[1].Stretches, 0, "and level 3, side by side");
            Assert.AreEqual(1, sim.Record.Tunnels[1].Depth, "both in the depth 1 layer");
            Assert.AreEqual(1, sim.Record.Tunnels[1].K, "each level has its own continuation");
            AssertConserved(ledger, sim);
        }

        [Test]
        public void DeeperSinksTheShaftWhileOldTunnelsStayOpen()
        {
            var ledger = new FakeLedger();
            var sim = Fresh(ledger, workers: 2);
            FinishShaft(sim);
            Assert.IsTrue(sim.ChooseContinue());   // both shaft miners go to level 2
            Assert.IsTrue(sim.AssignWorker("w1", 0));
            for (var i = 0; i < 50 && sim.Record.Workers[1].State != WorkerState.AwaitDepthChoice; i++) sim.Advance(1000);
            Assert.IsTrue(sim.ChooseDeeper(), "Dig Deeper once the shaft is through a layer");
            Assert.AreEqual(2, sim.Record.Depth);
            Assert.IsTrue(sim.Record.Revealed.ContainsKey(2));
            Assert.IsFalse(sim.ShaftDone, "the shaft sinks through depth 2");
            Assert.AreEqual(0, sim.Record.Workers[1].Face, "the waiting miner digs the shaft");
            Assert.AreEqual(1, sim.Record.Workers[0].Face, "the tunnel miner keeps going");
            Assert.IsFalse(sim.ChooseDeeper(), "not again until the shaft is through");
            FinishShaft(sim);
            Assert.AreEqual(7, sim.OpenLevels, "depth 2 is levels 5-7");
            var deep = sim.StartTunnel(5);
            Assert.AreEqual(2, sim.Tunnel(deep).Depth);
            Assert.IsTrue(sim.CanStartTunnel(3), "depth 1's levels stay open");
            AssertConserved(ledger, sim);
        }

        [Test]
        public void ReassigningAMinerCarriesThePackUpFirst()
        {
            var ledger = new FakeLedger();
            var sim = Fresh(ledger, workers: 1);
            for (var i = 0; i < 200 && sim.Record.Workers[0].State != WorkerState.Working; i++) sim.Advance(100);
            sim.Advance(2000);
            Assert.AreEqual(WorkerState.Working, sim.Record.Workers[0].State);
            FinishShaft(sim);
            var face = sim.StartTunnel(4);
            Assert.IsTrue(sim.AssignWorker("w0", face));
            RunTunnel(sim, sim.Tunnel(face));
            AssertConserved(ledger, sim);
        }

        [Test]
        public void TunnelResultsDoNotDependOnFrameSizes()
        {
            SiteRecord Run(long step)
            {
                var sim = Fresh(new FakeLedger());
                FinishShaft(sim);
                sim.ChooseContinue();
                sim.AssignWorker("w1", sim.StartTunnel(3));
                for (long ms = 0; ms < 240000; ms += step) sim.Advance(step);
                return sim.Record;
            }
            var a = Run(1000);
            var b = Run(37);
            Assert.AreEqual(a.Tunnels[0].DoneMicro, b.Tunnels[0].DoneMicro);
            Assert.AreEqual(a.Tunnels[1].Emitted, b.Tunnels[1].Emitted);
            Assert.AreEqual(a.Rng.State, b.Rng.State);
        }

        [Test]
        public void OlderMineSavesConvertWithoutLosingAnything()
        {
            var s = new SiteRecord { Depth = 2, K = 1, SectionsCompleted = 3, Status = SiteStatus.Working };
            s.SectionDepths.AddRange(new[] { 1, 1, 2 });
            var old = new TunnelRecord { Level = 2, Side = TunnelSide.Right, Depth = 1 };
            old.Pending.Add("iron");
            s.Tunnels.Add(old);
            s.Workers.Add(new WorkerRecord { Id = "w0", Face = 1 });
            SitesService.ConvertOldMine(s);
            Assert.AreEqual(1, s.SectionsCompleted, "the section in progress sinks the shaft through depth 2");
            Assert.AreEqual(0, s.Tunnels.Count);
            CollectionAssert.Contains(s.FacePending, "iron", "units waiting in old tunnels are kept");
            Assert.AreEqual(0, s.Workers[0].Face);
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

        [Test]
        public void TheDigSlabSurvivesRestart()
        {
            var a = Boot();
            DigTestKit.DigShaft(a, 3, 3);
            a.Logistics.System.ProduceShared("resource.cedar_lumber", 2, "test");
            var lumber = a.Sites.ScaffoldMaterialInStock("site.mine");
            Assert.IsTrue(a.Sites.PlaceScaffold("site.mine", 0, 3, 1, out var why), why);
            Assert.AreEqual(lumber - 1, a.Sites.ScaffoldMaterialInStock("site.mine"), "a scaffold costs one lumber");
            Assert.IsTrue(DigTestKit.Dig(a, 0, 2, 2, DigTestKit.Along), "dig the first block of a tunnel");
            a.Scheduler.SaveNow("test");

            var b = Boot();
            var ra = a.Sites.Get("site.mine").Record;
            var rb = b.Sites.Get("site.mine").Record;
            Assert.Greater(rb.Dug.Count, 1);
            Assert.AreEqual(ra.Dug.Count, rb.Dug.Count, "every dug block is kept");
            Assert.IsTrue(rb.Dug.SetEquals(ra.Dug));
            Assert.IsTrue(b.Sites.Get("site.mine").IsScaffold(0, 3, 1), "and so are the scaffolds");

            Assert.AreEqual(ra.Rng.State, rb.Rng.State);
            CollectionAssert.IsEmpty(b.Logistics.System.Audit());
        }
    }
}
