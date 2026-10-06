using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Sites;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Presentation;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    public sealed class FakeLedger : ISiteLedger
    {
        public readonly Dictionary<string, int> Created = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Staging = new Dictionary<string, int>();
        public int? CapacityOverride;

        public void RecordCreated(string r) => Created[r] = Created.TryGetValue(r, out var n) ? n + 1 : 1;
        public int StagingCount => Staging.Values.Sum();
        public int StagingFree(string id, int cap) => Math.Max(0, (CapacityOverride ?? cap) - StagingCount);
        public void Deposit(string id, string r) => Staging[r] = Staging.TryGetValue(r, out var n) ? n + 1 : 1;
        public int CreatedTotal => Created.Values.Sum();
    }

    public sealed class FakeEquipment : IEquipmentTable
    {
        public int ToolDamagePerSecond(string id) => id != null && id.StartsWith("t") ? int.Parse(id.Substring(1)) : 0;
        public int PackCapacity(string id) => id != null && id.StartsWith("p") ? int.Parse(id.Substring(1)) : 1;
    }

    public sealed class SiteTests
    {
        private const ulong Seed = 424242UL;

        public static SiteConfig MineConfig(double rare = 0.01) => new SiteConfig
        {
            SiteId = "site.mine", Kind = SiteKind.Mine, StagingContainerId = "staging", StagingCapacity = 96,
            BaseDurability = 240, Growth = 1.22, RoundTo = 10, KLinear = 0.18, KQuad = 0.04, TerminalKCap = 6, UnitsPerSection = 24,
            Bands = new List<BandTemplate>
            {
                Band(1, 2500, rare, ("iron", 80), ("copper", 20)),
                Band(2, 3000, rare, ("iron", 65), ("copper", 35)),
                Band(3, 3500, rare, ("iron", 55), ("copper", 20), ("coal", 25)),
            },
        };

        public static SiteConfig ForestConfig() => new SiteConfig
        {
            SiteId = "site.forest", Kind = SiteKind.Forest, StagingContainerId = "staging", StagingCapacity = 96,
            BaseDurability = 40, Growth = 1.18, RoundTo = 5, KLinear = 0.15, KQuad = 0.03, TerminalKCap = 6,
            TreesPerClearing = 6, UnitsPerTree = 4, HopMs = 800,
            Bands = new List<BandTemplate>
            {
                ForestBand(1, 2000, 0.04, 90, 10), ForestBand(2, 3000, 0.06, 70, 30), ForestBand(3, 4000, 0.08, 55, 45),
            },
        };

        private static BandTemplate Band(int index, int leg, double rare, params (string id, double w)[] weights) => new BandTemplate
        {
            Index = index, LegMs = leg, RareResourceId = "gem", RareChance = rare, BaselineResourceId = "iron", BaselineMinShare = 0.35,
            Weights = weights.Select(x => new KeyValuePair<string, double>(x.id, x.w)).ToList(),
        };

        private static BandTemplate ForestBand(int index, int leg, double herb, double cedar, double maple) => new BandTemplate
        {
            Index = index, LegMs = leg, RareResourceId = "herb", RareChance = herb, BaselineResourceId = "cedar", BaselineMinShare = 0.35,
            Weights = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("cedar", cedar), new KeyValuePair<string, double>("maple", maple) },
        };

        public static SiteSimulator Make(SiteConfig c, FakeLedger ledger, string tool = "t10", string pack = "p6", int workers = 1, ulong seed = Seed)
        {
            var sim = new SiteSimulator(c, new SiteRecord(), ledger, new FakeEquipment(), seed);
            var list = new List<WorkerRecord>();
            for (var i = 0; i < workers; i++) list.Add(new WorkerRecord { Id = $"w{i}", Name = $"Worker {i}", ToolId = tool, PackId = pack });
            sim.InitializeFresh(list);
            return sim;
        }

        /// <summary>Advances until the site waits for a decision (with a generous bound).</summary>
        private static long RunToDecision(SiteSimulator sim, long stepMs = 1000)
        {
            var start = sim.Record.SimMs;
            for (var i = 0; i < 100000 && sim.Record.Status != SiteStatus.AwaitDecision; i++) sim.Advance(stepMs);
            Assert.AreEqual(SiteStatus.AwaitDecision, sim.Record.Status, "site never finished its section");
            return sim.Record.SimMs - start;
        }

        private static int Held(SiteSimulator sim) => sim.HeldUnits().Count();

        // ------------------------------------------------------------------ generation and curves

        [Test]
        public void SameSeedSameCompositionDifferentSeedDiffers()
        {
            var c = MineConfig();
            var a = SiteMath.Generate(Seed, c.SiteId, c.Bands[1]);
            var b = SiteMath.Generate(Seed, c.SiteId, c.Bands[1]);
            var d = SiteMath.Generate(Seed + 1, c.SiteId, c.Bands[1]);
            CollectionAssert.AreEqual(a.Weights, b.Weights);
            CollectionAssert.AreNotEqual(a.Weights, d.Weights);
            Assert.AreEqual(1.0, a.Weights.Sum(w => w.Value), 1e-9);
            foreach (var w in a.Weights)   // ±8 % jitter around the template
                Assert.That(w.Value, Is.InRange(w.Key == "iron" ? 0.55 : 0.28, w.Key == "iron" ? 0.72 : 0.42));
        }

        [Test]
        public void BaselineShareIsEnforced()
        {
            var band = Band(1, 1000, 0, ("iron", 10), ("copper", 90));
            var comp = SiteMath.Generate(Seed, "site.x", band);
            Assert.AreEqual(0.35, comp.Weights.First(w => w.Key == "iron").Value, 1e-9);
            Assert.AreEqual(1.0, comp.Weights.Sum(w => w.Value), 1e-9);
        }

        [Test]
        public void DurabilityCurvesMatchTheGdd()
        {
            var m = MineConfig();
            CollectionAssert.AreEqual(new long[] { 240, 290, 360 }, new[] { 1, 2, 3 }.Select(d => SiteMath.Durability(m, d, 0)));
            // Continuation multipliers 1.00, 1.22, 1.52, 1.90 at depth 1.
            CollectionAssert.AreEqual(new long[] { 240, 293, 365, 456 }, new[] { 0, 1, 2, 3 }.Select(k => SiteMath.Durability(m, 1, k)));
            var f = ForestConfig();
            CollectionAssert.AreEqual(new long[] { 40, 45, 55 }, new[] { 1, 2, 3 }.Select(a => SiteMath.Durability(f, a, 0)));
        }

        [Test]
        public void ContinuationCapAppliesOnlyAtTheTerminalBand()
        {
            var m = MineConfig();
            Assert.AreEqual(SiteMath.Durability(m, 3, 6), SiteMath.Durability(m, 3, 12), "frontier stays renewable");
            Assert.Greater(SiteMath.Durability(m, 1, 12), SiteMath.Durability(m, 1, 6), "earlier layers keep diminishing efficiency");
        }

        // ------------------------------------------------------------------ yield rules

        [Test]
        public void SectionReleasesExactlyTwentyFourPrimaryUnitsAndRareIsSeparate()
        {
            foreach (var rare in new[] { 0.0, 1.0 })
            {
                var ledger = new FakeLedger();
                var sim = Make(MineConfig(rare), ledger);
                RunToDecision(sim);
                var primary = ledger.Created.Where(kv => kv.Key != "gem").Sum(kv => kv.Value);
                Assert.AreEqual(24, primary, $"rare={rare}");
                Assert.AreEqual(rare > 0 ? 24 : 0, ledger.Created.TryGetValue("gem", out var g) ? g : 0, "rare roll per primary unit, not a primary share");
                Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + Held(sim), "nothing lost");
            }
        }

        [Test]
        public void ContinuingRaisesEffortButNotYield()
        {
            var ledger = new FakeLedger();
            var sim = Make(MineConfig(0), ledger);
            var t0 = RunToDecision(sim);
            var after0 = ledger.CreatedTotal;
            Assert.IsTrue(sim.ChooseContinue());
            var t1 = RunToDecision(sim);
            Assert.AreEqual(24, ledger.CreatedTotal - after0, "same yield");
            Assert.Greater(t1, t0, "more effort");
            Assert.AreEqual(1, sim.Record.K);
            Assert.AreEqual(0.0, 1.0 - SiteMath.Generate(Seed, "site.mine", sim.Config.Bands[0]).Weights.Sum(w => w.Value), 1e-9);
        }

        [Test]
        public void DecisionIsNeverMadeAutomatically()
        {
            var ledger = new FakeLedger();
            var sim = Make(MineConfig(), ledger);
            RunToDecision(sim);
            var created = ledger.CreatedTotal;
            sim.Advance(3_600_000);   // an hour later
            Assert.AreEqual(SiteStatus.AwaitDecision, sim.Record.Status);
            Assert.AreEqual(1, sim.Record.Depth);
            Assert.AreEqual(created, ledger.CreatedTotal);
            Assert.IsTrue(sim.Record.Workers.All(w => w.State == WorkerState.AwaitDepthChoice));
        }

        [Test]
        public void DeeperRevealsTheGeneratedBandAndFrontierStops()
        {
            var ledger = new FakeLedger();
            var sim = Make(MineConfig(), ledger);
            Assert.IsFalse(sim.ChooseDeeper(), "not before the section is worked out");
            RunToDecision(sim);
            Assert.IsTrue(sim.ChooseDeeper());
            Assert.AreEqual(2, sim.Record.Depth);
            CollectionAssert.AreEqual(SiteMath.Generate(Seed, "site.mine", sim.Config.Bands[1]).Weights, sim.Record.Composition.Weights);
            Assert.AreEqual(290 * 1000, sim.Record.SectionDurabilityMicro);
            RunToDecision(sim);
            Assert.IsTrue(sim.ChooseDeeper());
            RunToDecision(sim);
            Assert.IsFalse(sim.CanGoDeeper);
            Assert.IsFalse(sim.ChooseDeeper(), "More depths not yet charted");
            Assert.IsTrue(sim.ChooseContinue());
        }

        // ------------------------------------------------------------------ pauses and safety

        [Test]
        public void FullStagingPausesWithoutLosingAnything()
        {
            var ledger = new FakeLedger { CapacityOverride = 10 };
            var sim = Make(MineConfig(0.5), ledger);
            for (var i = 0; i < 300; i++) sim.Advance(1000);
            Assert.AreEqual(10, ledger.StagingCount);
            Assert.IsTrue(sim.Record.Workers.Any(w => w.State == WorkerState.StagingFull));
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + Held(sim));

            ledger.CapacityOverride = 96;   // player hauls some away / space opens
            RunToDecision(sim);
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + Held(sim));
        }

        [Test]
        public void RareDropAtAFullPackWaitsOnTheFace()
        {
            var ledger = new FakeLedger();
            var sim = Make(MineConfig(1.0), ledger, pack: "p1");
            sim.Advance(2500 + 1000 + 1);   // walk to the face, then release the first unit
            Assert.AreEqual(1, sim.Record.Workers[0].Pack.Count);
            Assert.AreEqual(1, sim.Record.FacePending.Count, "the gem is held on the face, not suppressed");
            RunToDecision(sim);
            Assert.AreEqual(48, ledger.CreatedTotal);
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + Held(sim));
        }

        [Test]
        public void BiggerPacksImproveThroughputWhenTravelDominates()
        {
            // One section with a strong tool: travel, not mining, is the bottleneck.
            var small = Make(MineConfig(0), new FakeLedger(), tool: "t24", pack: "p6");
            var large = Make(MineConfig(0), new FakeLedger(), tool: "t24", pack: "p18");
            var tSmall = RunToDecision(small, 100);
            var tLarge = RunToDecision(large, 100);
            Assert.Less(tLarge, tSmall * 0.8, $"pack 18 finishes the section meaningfully sooner ({tLarge} vs {tSmall} ms)");
            Assert.Greater(large.MeasuredUnitsPerMinute, small.MeasuredUnitsPerMinute);
        }

        [Test]
        public void StrongerToolsWorkFaster()
        {
            var a = Make(MineConfig(0), new FakeLedger(), tool: "t10", pack: "p18");
            var b = Make(MineConfig(0), new FakeLedger(), tool: "t24", pack: "p18");
            Assert.Greater(RunToDecision(a), RunToDecision(b));
        }

        [Test]
        public void ResultsDoNotDependOnFrameSizes()
        {
            string Snapshot(SiteSimulator s, FakeLedger l) =>
                $"{s.Record.SimMs}|{s.Record.SectionEmitted}|{s.Record.SectionDoneMicro}|{s.Record.Rng.State}|{l.StagingCount}|" +
                string.Join(",", s.Record.Workers.Select(w => $"{w.State}:{w.PhaseEndMs}:{string.Join("+", w.Pack)}")) + "|" +
                string.Join(",", l.Staging.OrderBy(k => k.Key).Select(k => k.Key + k.Value));

            foreach (var cfg in new[] { MineConfig(0.2), ForestConfig() })
            {
                var la = new FakeLedger();
                var a = Make(cfg, la);
                a.Advance(600_000);

                var lb = new FakeLedger();
                var b = Make(cfg, lb);
                var rnd = new Random(3);
                long total = 0;
                while (total < 600_000)
                {
                    var step = Math.Min(600_000 - total, rnd.Next(1, 120));
                    b.Advance(step);
                    total += step;
                }
                Assert.AreEqual(Snapshot(a, la), Snapshot(b, lb), cfg.SiteId);
            }
        }

        // ------------------------------------------------------------------ forest and multiple workers

        [Test]
        public void ForestClearingYieldsTwentyFourWoodWithExclusiveTrees()
        {
            var ledger = new FakeLedger();
            var sim = Make(ForestConfig(), ledger, workers: 2);
            for (var i = 0; i < 400; i++)
            {
                sim.Advance(250);
                var claims = sim.Record.Trees.Where(t => t.ClaimedBy != null).GroupBy(t => t.ClaimedBy);
                Assert.IsTrue(claims.All(g => g.Count() == 1), "a worker claims one tree at a time");
            }
            RunToDecision(sim);
            var wood = ledger.Created.Where(kv => kv.Key != "herb").Sum(kv => kv.Value);
            Assert.AreEqual(24, wood);
            Assert.IsTrue(sim.Record.Trees.All(t => t.Felled));
            Assert.AreEqual(ledger.CreatedTotal, ledger.StagingCount + Held(sim));
            Assert.IsTrue(sim.ChooseDeeper());
            Assert.AreEqual(45 * 1000, sim.Record.Trees[0].DurabilityMicro);
        }

        [Test]
        public void TwoMinersShareTheFaceInRotation()
        {
            var ledger = new FakeLedger();
            var sim = Make(MineConfig(0), ledger, pack: "p18", workers: 2);
            sim.Advance(2500 + 2000);
            var a = sim.Record.Workers[0].Pack.Count;
            var b = sim.Record.Workers[1].Pack.Count;
            Assert.LessOrEqual(Math.Abs(a - b), 1, $"rotating split ({a} vs {b})");
            Assert.Greater(a + b, 0);
        }
    }

    /// <summary>Phase 2 persistence through the real content catalogue and save files.</summary>
    public sealed class SitePersistenceTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

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
        public void SitesSurviveRestartExactlyAndContinueIdentically()
        {
            var a = Boot();
            for (var i = 0; i < 90; i++) a.Sites.Tick(1f);
            a.Scheduler.SaveNow("test");

            var b = Boot();
            for (var i = 0; i < 90; i++) { a.Sites.Tick(1f); b.Sites.Tick(1f); }
            foreach (var id in new[] { "site.mine", "site.forest" })
            {
                var ra = a.Sites.Get(id).Record;
                var rb = b.Sites.Get(id).Record;
                Assert.AreEqual(ra.SimMs, rb.SimMs, id);
                Assert.AreEqual(ra.DepositedTotal, rb.DepositedTotal, id);
                Assert.AreEqual(ra.Rng.State, rb.Rng.State, id);
                Assert.AreEqual(ra.SectionEmitted, rb.SectionEmitted, id);
                CollectionAssert.AreEqual(ra.Workers.SelectMany(w => w.Pack), rb.Workers.SelectMany(w => w.Pack), id);
            }
            CollectionAssert.IsEmpty(b.Logistics.System.Audit());
        }

        [Test]
        public void DeeperRevealPersistsThroughReload()
        {
            var a = Boot();
            a.Sites.DebugCompleteSection("site.mine");
            for (var i = 0; i < 600 && a.Sites.Get("site.mine").Record.Status != SiteStatus.AwaitDecision; i++) a.Sites.Tick(1f);
            Assert.IsTrue(a.Sites.ChooseDeeper("site.mine"));
            var comp = a.Sites.Get("site.mine").Record.Composition.Weights.ToList();

            var b = Boot();
            var mine = b.Sites.Get("site.mine").Record;
            Assert.AreEqual(2, mine.Depth);
            CollectionAssert.AreEqual(comp, mine.Composition.Weights);
            Assert.IsTrue(mine.Revealed.ContainsKey(1) && mine.Revealed.ContainsKey(2));
            CollectionAssert.IsEmpty(b.Logistics.System.Audit());
        }

        [Test]
        public void V3FixtureMigratesToV4()
        {
            var json = File.ReadAllText("Assets/Ravenholm/Tests/Fixtures/save_v3_fixture.json");
            Assert.IsTrue(Infrastructure.Save.SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(3, from);
            Assert.AreEqual(Infrastructure.Save.SaveEnvelope.CurrentSchemaVersion, env.SchemaVersion);
            Assert.IsNull(env.Sites);
            Assert.AreEqual(2, env.Logistics.Carry.Count);
        }

        [Test]
        public void ProjectContentHasSixChartedBandsPerSite()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
            Assert.AreEqual(2, cat.Sites.Count);
            foreach (var s in cat.Sites)
            {
                Assert.AreEqual(6, s.Bands.Count, s.Id);   // v1: six depth bands (GDD 1, Phase 7)
                Assert.AreEqual(1, s.Workers.Count, s.Id);
                var cfg = SitesService.BuildConfig(s);
                Assert.AreEqual(s.SiteType == SiteType.Mine ? 240 : 40, SiteMath.Durability(cfg, 1, 0));
            }
        }
    }
}
