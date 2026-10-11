using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Sites;

namespace Ravenholm.Tests
{
    /// <summary>Prospecting: ore sits in seeded veins in the block grid, and digging by hand takes what the block holds.</summary>
    public sealed class ProspectingTests
    {
        private static Composition Mix(params (string id, double w)[] weights) => new Composition
        {
            Weights = weights.Select(x => new KeyValuePair<string, double>(x.id, x.w)).ToList(),
        };

        private static readonly Composition Layer = Mix(("iron", 0.55), ("copper", 0.25), ("coal", 0.20));

        [Test]
        public void VeinsAreFixedByTheWorldSeed()
        {
            var a = MineVeins.OreAt(42UL, Layer, "iron", 5, 9, 3, out _);
            Assert.AreEqual(a, MineVeins.OreAt(42UL, Layer, "iron", 5, 9, 3, out _), "same seed, same block, same ore");
            var differs = 0;
            for (var x = 0; x < 30; x++)
                if (MineVeins.OreAt(42UL, Layer, "iron", x, 4, 2, out _) != MineVeins.OreAt(43UL, Layer, "iron", x, 4, 2, out _)) differs++;
            Assert.Greater(differs, 0, "another world has other veins");
            Assert.IsNull(MineVeins.OreAt(42UL, Mix(("iron", 1.0)), "iron", 3, 3, 3, out _), "the baseline ore is plain rock, never a vein");
        }

        [Test]
        public void VeinsFollowTheLayersMixAndHangTogether()
        {
            var counts = new Dictionary<string, int> { { "copper", 0 }, { "coal", 0 } };
            int total = 0, ore = 0, sameNeighbour = 0;
            for (var level = 2; level < 8; level++)
            for (var x = 0; x < 40; x++)
            for (var y = 0; y < 40; y++)
            {
                total++;
                var o = MineVeins.OreAt(7UL, Layer, "iron", x, y, level, out _);
                if (o == null) continue;
                ore++;
                counts[o]++;
                if (MineVeins.OreAt(7UL, Layer, "iron", x, y + 1, level, out _) == o) sameNeighbour++;
            }
            foreach (var (id, share) in new[] { ("copper", 0.25), ("coal", 0.20) })
            {
                var frac = counts[id] / (double)total;
                Assert.That(frac, Is.InRange(share * MineVeins.Density * 0.4, share * MineVeins.Density * 1.8), $"{id}: {frac:0.000} of blocks");
            }
            Assert.Greater(counts["copper"], counts["coal"], "the commoner ore has more vein");
            Assert.Greater(sameNeighbour / (double)ore, 0.6, "veins are streaks, not specks: the next block along is usually the same ore");
        }

        [Test]
        public void DiggingByHandTakesWhatTheBlockHolds()
        {
            var ledger = new FakeLedger { CarryCapacity = 10000 };
            var sim = SiteTests.Make(SiteTests.MineConfig(0), ledger, workers: 0);
            // An ore block: every unit is that ore, often with a bonus unit.
            for (var i = 0; i < 400 && sim.Record.SectionEmitted < 12; i++) sim.PlayerMine(0, 500, 10, "copper");
            Assert.IsTrue(ledger.Carry.All(r => r == "copper"), string.Join(",", ledger.Carry));
            Assert.Greater(ledger.Carry.Count, sim.Record.SectionEmitted, "ore blocks give bonus units");
            Assert.AreEqual(ledger.CreatedTotal, ledger.Carry.Count + sim.HeldUnits().Count(), "every created unit is carried or waiting");
            // Plain rock: the layer's baseline ore, no bonus.
            ledger.Carry.Clear();
            var before = sim.Record.SectionEmitted;
            for (var i = 0; i < 400 && sim.Record.SectionEmitted < 24; i++) sim.PlayerMine(0, 500, 10, null, plainRock: true);
            Assert.IsTrue(ledger.Carry.All(r => r == "iron"));
            Assert.AreEqual(sim.Record.SectionEmitted - before, ledger.Carry.Count);
            Assert.AreEqual(ledger.Created["iron"], ledger.Carry.Count + sim.HeldUnits().Count(r => r == "iron"), "every iron unit created is carried or waiting");
        }
    }
}
