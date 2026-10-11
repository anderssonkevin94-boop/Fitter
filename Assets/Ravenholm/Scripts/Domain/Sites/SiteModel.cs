using System;
using System.Collections.Generic;
using Ravenholm.Domain.Rng;

namespace Ravenholm.Domain.Sites
{
    public enum SiteKind
    {
        Mine,
        Forest,
    }

    /// <summary>Authored depth band (Mine layer) or Forest area, converted from content definitions.</summary>
    public sealed class BandTemplate
    {
        public int Index;                       // 1-based depth or area
        public string Name = "";
        public string Discovery = "";           // shown before reveal as the expected tier
        public List<KeyValuePair<string, double>> Weights = new List<KeyValuePair<string, double>>();
        public string RareResourceId;           // gem (Mine) or herb (Forest): independent bonus roll per primary unit
        public double RareChance;
        public double DurabilityFactor = 1.0;
        public int LegMs = 2500;                // one-way travel staging <-> work
        public double Variation = 0.08;         // relative weight jitter applied once at generation
        public string BaselineResourceId;       // guaranteed share so recovery recipes never strand (GDD 6/7)
        public double BaselineMinShare;
    }

    public sealed class SiteConfig
    {
        public string SiteId = "";
        public SiteKind Kind;
        public string StagingContainerId = "";
        public int StagingCapacity = 96;
        public List<BandTemplate> Bands = new List<BandTemplate>();

        public int UnitsPerSection = 24;        // Mine primary units per section
        public int TreesPerClearing = 6;        // Forest
        public int UnitsPerTree = 4;            // Forest

        public double BaseDurability = 240;     // Mine section or Forest tree at band 1
        public double Growth = 1.22;
        public double RoundTo = 10;
        public double KLinear = 0.18;
        public double KQuad = 0.04;
        public int TerminalKCap = 6;

        public int LevelsPerBand = 3;           // Mine: shaft levels per layer (one tunnel per level)
        public int TunnelStretchCells = 4;      // Mine: blocks dug left per tunnel stretch (presentation and reach)

        public int HopMs = 800;                 // Forest: walk between trees
        public int DepositMs = 600;
    }

    public interface IEquipmentTable
    {
        int ToolDamagePerSecond(string equipmentId);
        int PackCapacity(string equipmentId);
    }

    /// <summary>Where extracted units become owned goods. Implemented by the logistics system.</summary>
    public interface ISiteLedger
    {
        /// <summary>Approved source: one unit extracted from a section or tree.</summary>
        void RecordCreated(string resourceId);
        int StagingFree(string containerId, int capacity);
        /// <summary>Moves one unit from a worker pack into a staging container (no creation).</summary>
        void Deposit(string containerId, string resourceId);
    }

    public sealed class Composition
    {
        public List<KeyValuePair<string, double>> Weights = new List<KeyValuePair<string, double>>();
        public string RareResourceId;
        public double RareChance;

        public string Draw(IRandomStream rng)
        {
            var u = rng.NextDouble();
            var acc = 0.0;
            for (var i = 0; i < Weights.Count; i++)
            {
                acc += Weights[i].Value;
                if (u < acc) return Weights[i].Key;
            }
            return Weights[Weights.Count - 1].Key;
        }
    }

    public enum SiteStatus
    {
        Working,
        /// <summary>Section or clearing exhausted and every earned unit collected; waits for the player.</summary>
        AwaitDecision,
    }

    public enum WorkerState
    {
        WalkToWork,
        Working,
        Hop,
        ReturnToStaging,
        Depositing,
        StagingFull,
        AwaitDepthChoice,
    }

    public sealed class WorkerRecord
    {
        public string Id = "";
        public string Name = "";
        public string ToolId = "";
        public string PackId = "";
        public WorkerState State;
        public long PhaseStartMs;
        public long PhaseEndMs;
        public int TargetTree = -1;
        /// <summary>Mine: which face the worker digs. 0 = the shaft; n = <see cref="SiteRecord.Tunnels"/>[n − 1].</summary>
        public int Face;
        public readonly List<string> Pack = new List<string>();
    }

    public enum TunnelSide
    {
        Left,
        Right,
    }

    /// <summary>
    /// Mine: a sideways tunnel dug from the shaft at one level, in that level's layer. It advances in stretches of a
    /// few blocks; each stretch is one decision (like "Continue Here"), with effort rising per stretch.
    /// </summary>
    public sealed class TunnelRecord
    {
        public int Level;                       // shaft level it leaves from (2 = first level below the town's own ground)
        // Side: tunnels run left (kept for saves made while tunnels could also run right).
        public TunnelSide Side;
        public int Depth;                       // the layer (depth band) of that level
        public int K;                           // continuation index driving effort
        public int Stretches;                   // completed stretches
        public SiteStatus Status;
        public long DurabilityMicro;
        public long DoneMicro;
        public int Emitted;
        public int NextRecipient;
        public readonly List<string> Pending = new List<string>();
    }

    public sealed class TreeRecord
    {
        public int Index;
        public string Species = "";
        public readonly List<string> Primary = new List<string>();
        /// <summary>Bonus unit per primary unit (null = none), precomputed so visible and offline play match.</summary>
        public readonly List<string> Bonus = new List<string>();
        public long DurabilityMicro;
        public long DoneMicro;
        public int Emitted;
        public string ClaimedBy;
        public readonly List<string> Pending = new List<string>();
        public bool Felled => Emitted >= Primary.Count;
    }

    /// <summary>Authoritative runtime state of one gathering site. Damage is in micro-units (damage × 1000).</summary>
    public sealed class SiteRecord
    {
        public string SiteId = "";
        public int Depth = 1;
        public int K;                                   // continuation (exploit) index within the band
        public int SectionsCompleted;
        public SiteStatus Status;
        public long SimMs;
        public RandomState Rng;
        public Composition Composition = new Composition();
        public readonly SortedDictionary<int, Composition> Revealed = new SortedDictionary<int, Composition>();
        /// <summary>Depth band of each completed section, in order (one dug level each; drives the Mine's layered view).</summary>
        public readonly List<int> SectionDepths = new List<int>();
        /// <summary>Mine: sideways tunnels, at most one per (level, side), each its own face.</summary>
        public readonly List<TunnelRecord> Tunnels = new List<TunnelRecord>();

        // Mine
        public long SectionDurabilityMicro;
        public long SectionDoneMicro;
        public int SectionEmitted;
        public readonly List<string> FacePending = new List<string>();
        public int NextRecipient;

        // Forest
        public readonly List<TreeRecord> Trees = new List<TreeRecord>();

        public readonly List<WorkerRecord> Workers = new List<WorkerRecord>();

        // Measurement (debug readout)
        public long DepositedTotal;
        public long MeasureStartMs;
        public long DepositedSinceMeasure;
    }

    public static class SiteMath
    {
        public static double RoundToStep(double v, double step) => step <= 0 ? v : Math.Round(v / step, MidpointRounding.AwayFromZero) * step;

        /// <summary>GDD 6/7: base × growth^(band−1), rounded to the step, then × template factor.</summary>
        public static double BandBase(SiteConfig c, BandTemplate band) =>
            Math.Round(RoundToStep(c.BaseDurability * Math.Pow(c.Growth, band.Index - 1), c.RoundTo) * band.DurabilityFactor);

        /// <summary>Continuation multiplier 1 + a·k + b·k².</summary>
        public static double ContinuationMultiplier(SiteConfig c, int k) => 1.0 + c.KLinear * k + c.KQuad * k * k;

        /// <summary>The continuation index used for durability; capped only at the terminal (deepest authored) band.</summary>
        public static int EffectiveK(SiteConfig c, int depth, int k) => depth >= c.Bands.Count ? Math.Min(k, c.TerminalKCap) : k;

        /// <summary>A tunnel stretch is one Mine section ("Continue Here" in that layer): same durability curve and yield.</summary>
        public static long TunnelDurability(SiteConfig c, int depth, int k) => Durability(c, depth, k);

        public static int TunnelUnits(SiteConfig c) => c.UnitsPerSection;

        /// <summary>Durability (whole damage) of one Mine section or one Forest tree.</summary>
        public static long Durability(SiteConfig c, int depth, int k)
        {
            var band = c.Bands[depth - 1];
            return (long)Math.Round(BandBase(c, band) * ContinuationMultiplier(c, EffectiveK(c, depth, k)), MidpointRounding.AwayFromZero);
        }

        /// <summary>Micro-damage at which unit <paramref name="n"/> (1-based) of <paramref name="total"/> is released.</summary>
        public static long Threshold(long durabilityMicro, int n, int total) => (durabilityMicro * n + total - 1) / total;

        /// <summary>Stable FNV-1a hash (string.GetHashCode is not stable across runtimes).</summary>
        public static ulong StableHash(string s)
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                foreach (var ch in s)
                {
                    h ^= ch;
                    h *= 1099511628211UL;
                }
                return h;
            }
        }

        /// <summary>Generated once per (world seed, site, band); never rerolled.</summary>
        public static Composition Generate(ulong worldSeed, string siteId, BandTemplate band)
        {
            var rng = RandomStreams.Create(worldSeed, RandomStreamId.WorldGeneration, StableHash(siteId) ^ ((ulong)band.Index * 0x9E3779B97F4A7C15UL));
            var w = new List<KeyValuePair<string, double>>();
            foreach (var kv in band.Weights)
            {
                var jitter = 1.0 + band.Variation * (2.0 * rng.NextDouble() - 1.0);
                w.Add(new KeyValuePair<string, double>(kv.Key, Math.Max(0.0001, kv.Value * jitter)));
            }
            Normalize(w);

            if (!string.IsNullOrEmpty(band.BaselineResourceId))
            {
                var i = w.FindIndex(p => p.Key == band.BaselineResourceId);
                if (i >= 0 && w[i].Value < band.BaselineMinShare)
                {
                    var others = 1.0 - w[i].Value;
                    var scale = others > 0 ? (1.0 - band.BaselineMinShare) / others : 0;
                    for (var j = 0; j < w.Count; j++)
                        w[j] = new KeyValuePair<string, double>(w[j].Key, j == i ? band.BaselineMinShare : w[j].Value * scale);
                }
            }
            return new Composition { Weights = w, RareResourceId = band.RareResourceId, RareChance = band.RareChance };
        }

        private static void Normalize(List<KeyValuePair<string, double>> w)
        {
            var total = 0.0;
            foreach (var kv in w) total += kv.Value;
            for (var i = 0; i < w.Count; i++) w[i] = new KeyValuePair<string, double>(w[i].Key, w[i].Value / total);
        }
    }
}
