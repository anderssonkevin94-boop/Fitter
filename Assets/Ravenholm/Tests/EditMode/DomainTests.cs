using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ravenholm.Domain.Ids;
using Ravenholm.Domain.Rng;
using Ravenholm.Domain.Time;

namespace Ravenholm.Tests
{
    public sealed class ContentIdTests
    {
        [TestCase("resource.iron_ore")]
        [TestCase("item.iron_sword")]
        [TestCase("quest.roadside_patrol.v2")]
        public void ValidIds(string id) => Assert.IsTrue(ContentIds.IsValid(id, out var r), r);

        [TestCase(null)]
        [TestCase("")]
        [TestCase("iron_ore")]            // no kind prefix
        [TestCase("Resource.iron_ore")]   // uppercase
        [TestCase("resource.iron ore")]   // space
        [TestCase("resource..iron")]      // empty segment
        [TestCase("resource.")]           // trailing dot
        [TestCase("1resource.iron")]      // leading digit
        public void InvalidIds(string id) => Assert.IsFalse(ContentIds.IsValid(id, out _));

        [Test]
        public void KindPrefix()
        {
            Assert.IsTrue(ContentIds.HasKind("resource.iron_ore", "resource"));
            Assert.IsFalse(ContentIds.HasKind("resources.iron_ore", "resource"));
            Assert.IsFalse(ContentIds.HasKind("item.iron_sword", "resource"));
        }
    }

    public sealed class RandomTests
    {
        [Test]
        public void Pcg32_MatchesReferenceVectors()
        {
            // pcg32-demo reference output for pcg32_srandom_r(42, 54).
            var rng = new Pcg32(42UL, 54UL);
            var expected = new uint[] { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
            foreach (var e in expected) Assert.AreEqual(e, rng.NextUInt());
        }

        [Test]
        public void SnapshotRestoreContinuesIdentically()
        {
            var a = new Pcg32(7, 11);
            for (var i = 0; i < 100; i++) a.NextUInt();
            var b = new Pcg32(a.Snapshot());
            for (var i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void StreamsAreDeterministicAndIndependent()
        {
            const ulong seed = 123456789UL;
            var q1 = RandomStreams.Create(seed, RandomStreamId.Expeditions);
            var q2 = RandomStreams.Create(seed, RandomStreamId.Expeditions);
            var trader = RandomStreams.Create(seed, RandomStreamId.Trader);
            var same = 0;
            for (var i = 0; i < 64; i++)
            {
                var v = q1.NextUInt();
                Assert.AreEqual(v, q2.NextUInt());
                if (v == trader.NextUInt()) same++;
            }
            Assert.Less(same, 2, "different named streams must not mirror each other");
        }

        [Test]
        public void NextIntStaysInRangeAndCoversIt()
        {
            var rng = new Pcg32(1, 2);
            var seen = new HashSet<int>();
            for (var i = 0; i < 5000; i++)
            {
                var v = rng.NextInt(-3, 4);
                Assert.That(v, Is.InRange(-3, 3));
                seen.Add(v);
            }
            Assert.AreEqual(7, seen.Count);
        }

        [Test]
        public void NextDoubleInUnitInterval()
        {
            var rng = new Pcg32(3, 4);
            for (var i = 0; i < 5000; i++) Assert.That(rng.NextDouble(), Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
        }
    }

    public sealed class ClockTests
    {
        [Test]
        public void BackwardsClockNeverRewindsAcceptedTime()
        {
            var t0 = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
            var accepted = ClockGuard.Accept(t0, t0.AddHours(-3), out var back);
            Assert.IsTrue(back);
            Assert.AreEqual(t0, accepted);
            accepted = ClockGuard.Accept(t0, t0.AddMinutes(5), out back);
            Assert.IsFalse(back);
            Assert.AreEqual(t0.AddMinutes(5), accepted);
        }
    }
}
