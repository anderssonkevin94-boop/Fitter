using System;
using System.Collections.Generic;

namespace Ravenholm.Domain.Sites
{
    /// <summary>
    /// The Forest as a grid of trees (2026-10-12): any tree the supplier reaches can be cut. Cutting is the progress:
    /// areas further left are the next bands (revealed the first time a tree there is cut: new species, tougher wood);
    /// trees further back from the cliff in an area cost a little more (one continuation per
    /// <see cref="ForestGrove.TreesPerContinuation"/> trees, as a clearing did). Each tree keeps the clearing economy: its
    /// durability is a clearing tree's, and it gives <see cref="SiteConfig.UnitsPerTree"/> logs of its species as it is
    /// cut, each with the area's rare roll.
    ///
    /// Species and rare rolls come from the world seed, per tree, so a tree shows what it is before it is cut and
    /// cutting by hand or on a routine gives the same. On a routine the supplier keeps cutting the standing tree nearest
    /// the last one cut, hauling to staging, and waits when none is left nearby.
    /// </summary>
    public sealed partial class SiteSimulator
    {
        public bool GroveMode => _c.Kind == SiteKind.Forest && _c.Grove != null;

        /// <summary>Both grid sites: a routine works one target at a time (face 0) and nothing waits for a choice.</summary>
        private bool BlockMode => DigMode || GroveMode;

        /// <summary>Workers work face 0 and share its yield (the Mine, or the Forest's grid).</summary>
        private bool FaceMode => _c.Kind == SiteKind.Mine || GroveMode;

        public ForestGrove Grove => _c.Grove;

        public int BandOfArea(int area) => Math.Max(1, Math.Min(area, _c.Bands.Count));

        public int GroveContinuation(int area, int slot) =>
            Math.Max(0, slot) / Math.Max(1, _c.Grove.TreesPerContinuation) + Math.Max(0, area - _c.Bands.Count);

        public long TreeDurabilityMicro(int area, int slot) => SiteMath.Durability(_c, BandOfArea(area), GroveContinuation(area, slot)) * 1000;

        public long ChopDone(int area, int slot) => _s.Chopped.TryGetValue(new GroveCell(area, slot), out var d) ? d : 0;

        public bool IsFelled(int area, int slot) => ChopDone(area, slot) >= TreeDurabilityMicro(area, slot);

        public bool CanChop(int area, int slot) => GroveMode && _c.Grove.InGrove(area, slot) && !IsRock(area, slot) && !IsFelled(area, slot) && !IsCleared(area, slot);

        /// <summary>How high the ground stands at a tree's spot (blocks above the floor), from the world seed.</summary>
        public int TerrainHeight(int area, int slot) =>
            GroveMode ? _c.Grove.TerrainHeight(_worldSeed ^ SiteMath.StableHash(_c.SiteId), _c.Grove.RowOf(slot), _c.Grove.AlongOf(area, slot)) : 0;

        /// <summary>
        /// No tree here: bare rock (a ridge's heights), or open ground between the trees. Everything that asks whether a
        /// tree can grow treats both the same.
        /// </summary>
        public bool IsRock(int area, int slot) =>
            GroveMode && (TerrainHeight(area, slot) >= _c.Grove.RockHeight ||
                          _c.Grove.OpenGround(_worldSeed ^ SiteMath.StableHash(_c.SiteId), _c.Grove.RowOf(slot), _c.Grove.AlongOf(area, slot)));

        /// <summary>A stump: felled and not yet pulled out (it grows back unless cleared).</summary>
        public bool IsStump(int area, int slot) => !IsRock(area, slot) && IsFelled(area, slot) && !IsCleared(area, slot);

        public bool IsCleared(int area, int slot) => _s.Cleared.Contains(new GroveCell(area, slot));

        /// <summary>
        /// The supplier pulls a stump out (hitting it further): one log, and the spot stays open for good. True when
        /// there was a stump.
        /// </summary>
        public bool ClearStump(int area, int slot)
        {
            if (!GroveMode || !IsStump(area, slot)) return false;
            var c = new GroveCell(area, slot);
            _s.Cleared.Add(c);
            _s.FelledAtMs.Remove(c);
            TakeIntoCarry(_s.FacePending);
            Release(SpeciesAt(area, slot), true);
            FelledVersion++;
            Resolve();
            return true;
        }

        /// <summary>Stumps left standing long enough grow back into their trees (same species, uncut).</summary>
        private void RegrowDue()
        {
            if (!GroveMode || _c.Grove.RegrowMs <= 0 || _s.FelledAtMs.Count == 0) return;
            List<GroveCell> due = null;
            foreach (var kv in _s.FelledAtMs)
                if (_s.SimMs - kv.Value >= _c.Grove.RegrowMs) (due ??= new List<GroveCell>()).Add(kv.Key);
            if (due == null) return;
            foreach (var c in due)
            {
                _s.FelledAtMs.Remove(c);
                if (_s.Cleared.Contains(c)) continue;
                _s.Chopped.Remove(c);
                FelledVersion++;
            }
        }

        public float ChopProgress(int area, int slot) =>
            Math.Min(1f, (float)ChopDone(area, slot) / Math.Max(1, TreeDurabilityMicro(area, slot)));

        /// <summary>Bumped whenever a tree falls (the view redraws then).</summary>
        public int FelledVersion { get; private set; }

        /// <summary>An area's mix; the first tree cut there reveals it (as Push Deeper did).</summary>
        public Composition AreaMix(int area, bool reveal)
        {
            var band = BandOfArea(area);
            if (_s.Revealed.TryGetValue(band, out var known) && known != null) return known;
            var mix = SiteMath.Generate(_worldSeed, _c.SiteId, _c.Bands[band - 1]);
            if (!reveal) return mix;
            _s.Revealed[band] = mix;
            if (band > _s.Depth)
            {
                _s.Depth = band;
                _s.Composition = mix;
            }
            return mix;
        }

        private double CellRoll(int area, int slot, int salt)
        {
            unchecked
            {
                var h = _worldSeed ^ SiteMath.StableHash(_c.SiteId) ^ ((ulong)area * 0x9E3779B97F4A7C15UL) ^ ((ulong)(slot + 1) * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)salt * 0x165667B19E3779F9UL);
                h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
                return (h >> 11) * (1.0 / (1UL << 53));
            }
        }

        /// <summary>A tree's species: the area's mix, drawn once per tree from the world seed.</summary>
        public string SpeciesAt(int area, int slot)
        {
            var mix = AreaMix(area, false);
            if (mix.Weights.Count == 0) return "";
            var u = CellRoll(area, slot, 0);
            var acc = 0.0;
            foreach (var w in mix.Weights)
            {
                acc += w.Value;
                if (u < acc) return w.Key;
            }
            return mix.Weights[mix.Weights.Count - 1].Key;
        }

        private int LogsCut(int area, int slot, long done)
        {
            var dur = TreeDurabilityMicro(area, slot);
            var n = 0;
            while (n < _c.UnitsPerTree && done >= SiteMath.Threshold(dur, n + 1, _c.UnitsPerTree)) n++;
            return n;
        }

        /// <summary>Cuts a tree from <paramref name="before"/> to <paramref name="after"/>, releasing the logs passed on the way.</summary>
        private int Cut(GroveCell c, long before, long after, bool byHand)
        {
            var from = LogsCut(c.Area, c.Slot, before);
            var to = LogsCut(c.Area, c.Slot, after);
            _s.Chopped[c] = after;
            if (to == from) return 0;
            var mix = AreaMix(c.Area, true);
            var species = SpeciesAt(c.Area, c.Slot);
            for (var n = from; n < to; n++)
            {
                Release(species, byHand);
                if (!string.IsNullOrEmpty(mix.RareResourceId) && CellRoll(c.Area, c.Slot, n + 1) < mix.RareChance) Release(mix.RareResourceId, byHand);
            }
            if (after >= TreeDurabilityMicro(c.Area, c.Slot))
            {
                FelledVersion++;
                _s.LastChop = c;
                _s.FelledAtMs[c] = _s.SimMs;
            }
            return to - from;
        }

        // ------------------------------------------------------------------ by hand

        /// <summary>
        /// The supplier swings at a tree for <paramref name="ms"/>: its logs go straight into the carry (or wait at the
        /// site when the carry is full; they come along with the next swing). Returns the logs cut.
        /// </summary>
        public int PlayerChopCell(int area, int slot, long ms, int dps)
        {
            if (!GroveMode) return 0;
            TakeIntoCarry(_s.FacePending);
            if (!CanChop(area, slot) || ms <= 0 || dps <= 0) return 0;
            var c = new GroveCell(area, slot);
            _s.LastChop = c;
            var before = ChopDone(area, slot);
            var after = Math.Min(TreeDurabilityMicro(area, slot), before + dps * ms);
            var cut = Cut(c, before, after, byHand: true);
            Resolve();
            SaveRng();
            return cut;
        }

        // ------------------------------------------------------------------ routines

        /// <summary>Points a routine at the last tree cut. False when nothing has been cut yet.</summary>
        public bool AimRoutineAtLastChop() => GroveMode && _s.LastChop.HasValue;

        /// <summary>The tree a routine cuts next: the last one if it still stands, else the standing tree nearest it.</summary>
        public bool GroveRoutineNext(out GroveCell cell)
        {
            cell = default;
            if (!GroveMode || !_s.LastChop.HasValue) return false;
            var g = _c.Grove;
            var from = _s.LastChop.Value;
            if (CanChop(from.Area, from.Slot)) { cell = from; return true; }
            int fx = g.RowOf(from.Slot), fy = g.AlongOf(from.Area, from.Slot);
            var best = int.MaxValue;
            for (var dy = -g.RoutineReach; dy <= g.RoutineReach; dy++)
            for (var dx = -g.RoutineReach; dx <= g.RoutineReach; dx++)
            {
                var d = Math.Abs(dx) + Math.Abs(dy);
                if (d == 0 || d > g.RoutineReach || d > best) continue;
                int row = fx + dx, along = fy + dy;
                if (row < 0 || along < 0) continue;
                var area = along / g.AreaLength + 1;
                var slot = row * g.AreaLength + along % g.AreaLength;
                if (!CanChop(area, slot)) continue;
                var c = new GroveCell(area, slot);
                // Nearest first; ties stay in the same area (a routine never wanders into a new one on a tie), then go to the
                // cliff edge, then toward the town.
                var sameArea = area == from.Area;
                var bestSame = cell.Area == from.Area;
                var better = d < best ||
                             sameArea && !bestSame ||
                             sameArea == bestSame && (row < g.RowOf(cell.Slot) || row == g.RowOf(cell.Slot) && along < g.AlongOf(cell.Area, cell.Slot));
                if (better)
                {
                    best = d;
                    cell = c;
                }
            }
            return best != int.MaxValue;
        }

        private long GroveNextEventDelay()
        {
            var dps = MineCombinedDps(0);
            if (dps <= 0 || !GroveRoutineNext(out var c)) return long.MaxValue;
            var done = ChopDone(c.Area, c.Slot);
            var next = SiteMath.Threshold(TreeDurabilityMicro(c.Area, c.Slot), LogsCut(c.Area, c.Slot, done) + 1, _c.UnitsPerTree);
            return CeilDiv(next - done, dps);
        }

        private void GroveApplyWork(long ms)
        {
            var dps = MineCombinedDps(0);
            if (dps <= 0 || !GroveRoutineNext(out var c)) return;
            var before = ChopDone(c.Area, c.Slot);
            var after = Math.Min(TreeDurabilityMicro(c.Area, c.Slot), before + dps * ms);
            _s.LastChop = c;
            Cut(c, before, after, byHand: false);
        }

        /// <summary>Old saves: the clearings already cut become felled trees on the grid; their logs wait at the site.</summary>
        public static void ConvertToGrove(SiteRecord s, int treesPerClearing)
        {
            var per = Math.Max(1, treesPerClearing);
            var done = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var d in s.SectionDepths) done[d] = (done.TryGetValue(d, out var n) ? n : 0) + 1;
            foreach (var kv in done)
                for (var slot = 0; slot < kv.Value * per; slot++)
                    s.Chopped[new GroveCell(kv.Key, slot)] = long.MaxValue / 4;   // felled (beyond any durability)
            foreach (var t in s.Trees)
            {
                var c = new GroveCell(s.Depth, s.K * per + t.Index);
                s.Chopped[c] = t.Felled ? long.MaxValue / 4 : t.DoneMicro;
                if (t.DoneMicro > 0) s.LastChop = c;
                s.FacePending.AddRange(t.Pending);
            }
            s.Trees.Clear();
            foreach (var w in s.Workers) w.TargetTree = -1;
            s.Status = SiteStatus.Working;
        }
    }
}
