using System;

namespace Ravenholm.Domain.Sites
{
    /// <summary>
    /// Where the ore is (prospecting, GDD 6 extension): every block of the Mine's grid has fixed contents, generated
    /// from the world seed. A layer's mix still sets the odds, but ore sits in veins: smooth noise fields (one per ore),
    /// stretched along the tunnels' direction so they read as streaks and pockets that run between levels. A block holds
    /// the ore whose field rises highest above that ore's threshold, or else plain rock (the layer's baseline ore).
    /// Pure and deterministic: the same seed always gives the same Mine.
    /// </summary>
    public static class MineVeins
    {
        /// <summary>Share of a layer's blocks that are vein rather than plain rock, relative to the ore's weight.</summary>
        public const double Density = 0.85;

        /// <summary>Chance of a bonus unit for each unit released while digging an ore block by hand.</summary>
        public const double HandBonusChance = 0.5;

        /// <summary>
        /// The ore in block (x, y, level), or null for plain rock. <paramref name="richness"/> (0..1) says how deep into
        /// the vein the block is (its core is richest).
        /// </summary>
        public static string OreAt(ulong worldSeed, Composition comp, string baselineId, int x, int y, int level, out double richness)
        {
            richness = 0;
            if (comp == null) return null;
            string best = null;
            var bestMargin = 0.0;
            for (var i = 0; i < comp.Weights.Count; i++)
            {
                var id = comp.Weights[i].Key;
                if (id == baselineId) continue;
                var share = comp.Weights[i].Value;
                var threshold = 1.0 - Math.Min(0.95, share * Density);
                var v = Field(worldSeed ^ SiteMath.StableHash(id), x / 2.0, y / 3.5, level / 1.6);
                var margin = v - threshold;
                if (margin > bestMargin)
                {
                    bestMargin = margin;
                    best = id;
                    richness = Math.Min(1.0, margin / Math.Max(1e-6, 1.0 - threshold));
                }
            }
            return best;
        }

        /// <summary>
        /// Smooth noise in [0, 1): value noise on a lattice, smoothstep-interpolated, then pushed through a sigmoid so
        /// values spread evenly enough that a threshold picks roughly the intended share of blocks.
        /// </summary>
        public static double Field(ulong seed, double x, double y, double z)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y), z0 = (int)Math.Floor(z);
            double fx = Smooth(x - x0), fy = Smooth(y - y0), fz = Smooth(z - z0);
            double Lerp(double a, double b, double t) => a + (b - a) * t;
            double C(int dx, int dy, int dz) => Hash01(seed, x0 + dx, y0 + dy, z0 + dz);
            var x00 = Lerp(C(0, 0, 0), C(1, 0, 0), fx);
            var x10 = Lerp(C(0, 1, 0), C(1, 1, 0), fx);
            var x01 = Lerp(C(0, 0, 1), C(1, 0, 1), fx);
            var x11 = Lerp(C(0, 1, 1), C(1, 1, 1), fx);
            var v = Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
            // Interpolated noise bunches around 0.5; spread it back toward uniform.
            var spread = 1.0 / (1.0 + Math.Exp(-(v - 0.5) * 9.0));
            return Math.Min(0.999999, Math.Max(0.0, spread));
        }

        private static double Smooth(double t) => t * t * (3 - 2 * t);

        private static double Hash01(ulong seed, int x, int y, int z)
        {
            unchecked
            {
                var h = seed ^ 0x9E3779B97F4A7C15UL;
                h ^= (ulong)x * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 31)) * 0x94D049BB133111EBUL;
                h ^= (ulong)y * 0xD6E8FEB86659FD93UL;
                h = (h ^ (h >> 29)) * 0xBF58476D1CE4E5B9UL;
                h ^= (ulong)z * 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 32)) * 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return (h >> 11) * (1.0 / (1UL << 53));
            }
        }
    }
}
