using System;

namespace Ravenholm.Domain.Rng
{
    /// <summary>Deterministic random source with saveable state. Never use UnityEngine.Random for game state.</summary>
    public interface IRandomStream
    {
        uint NextUInt();

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>Uniform double in [0, 1).</summary>
        double NextDouble();

        RandomState Snapshot();
    }

    [Serializable]
    public struct RandomState
    {
        public ulong State;
        public ulong Increment;
    }

    /// <summary>
    /// PCG32 (XSH-RR) by M. O'Neill, a small well-tested generator. Same seed and same call order
    /// produce the same sequence on every platform, which save determinism depends on.
    /// </summary>
    public sealed class Pcg32 : IRandomStream
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private ulong _state;
        private ulong _inc;

        public Pcg32(ulong seed, ulong sequence)
        {
            _state = 0;
            _inc = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public Pcg32(RandomState restored)
        {
            if ((restored.Increment & 1UL) == 0) throw new ArgumentException("Invalid PCG32 state: increment must be odd.");
            _state = restored.State;
            _inc = restored.Increment;
        }

        public uint NextUInt()
        {
            var old = _state;
            _state = unchecked(old * Multiplier + _inc);
            var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            var rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            var range = (uint)(maxExclusive - minInclusive);
            // Lemire-style rejection to avoid modulo bias.
            var threshold = (uint)(-range) % range;
            while (true)
            {
                var r = NextUInt();
                if (r >= threshold) return minInclusive + (int)(r % range);
            }
        }

        public double NextDouble()
        {
            // 53 random bits -> [0,1)
            ulong hi = NextUInt() >> 5, lo = NextUInt() >> 6;
            return (hi * 67108864.0 + lo) / 9007199254740992.0;
        }

        public RandomState Snapshot() => new RandomState { State = _state, Increment = _inc };
    }

    /// <summary>
    /// Independent named streams derived from the world seed, so that (for example) a Trader
    /// visit or cosmetic roll can never shift a quest outcome (GDD 22).
    /// </summary>
    public enum RandomStreamId
    {
        WorldGeneration = 1,
        SectionYield = 2,
        Applications = 3,
        Expeditions = 4,
        Trader = 5,
        Cosmetic = 6,
    }

    public static class RandomStreams
    {
        public static IRandomStream Create(ulong worldSeed, RandomStreamId stream, ulong salt = 0)
        {
            var seed = SplitMix64(worldSeed ^ SplitMix64((ulong)stream * 0x9E3779B97F4A7C15UL + salt));
            var sequence = SplitMix64(seed ^ (ulong)stream);
            return new Pcg32(seed, sequence);
        }

        public static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }
    }
}
