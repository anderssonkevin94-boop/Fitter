using System;

namespace Ravenholm.Domain.Sites
{
    /// <summary>
    /// The Mine as a dig slab (dig any block, 2026-10-12). Digging is the progress: going down reaches the next layer
    /// (revealed the first time it is dug into); staying in a layer costs a little more effort the further along the
    /// cliff. Every block counts as one unit of a section's work at that layer and continuation, so the economy keeps the
    /// section curve (GDD 6/7): a block's durability is the section's durability spread over its units. A block gives its
    /// own ore (veins) or the layer's baseline ore, with a bonus roll for ore blocks and the layer's rare roll.
    ///
    /// The supplier digs by hand (straight into the carry). On a routine they carry on the tunnel they last dug, two
    /// blocks high, straight ahead, hauling to staging, and wait once it is blocked (bedrock, the edge, a drop).
    /// </summary>
    public sealed partial class SiteSimulator
    {
        public bool DigMode => _c.Kind == SiteKind.Mine && _c.Dig != null;
        public DigSlab Slab => _c.Dig;

        public bool IsDug(int x, int u, int row) => _s.Dug.Contains(new DigCell(x, u, row));

        // ------------------------------------------------------------------ the elevator and scaffolds

        /// <summary>
        /// The elevator: a shaft at the cliff corner, open from the start through the first layer, with a pulley on top.
        /// Its platform is always at the supplier's floor. Digging its floor deeper extends it. True for every open block
        /// of the shaft (rows 0 down to the first rock).
        /// </summary>
        public bool Elevator(int x, int u, int row) => DigMode && x == 0 && u == _c.Dig.ElevatorU && row >= 0 && row <= ElevatorBottom;

        /// <summary>The deepest open row of the elevator shaft (−1 before it is opened).</summary>
        public int ElevatorBottom
        {
            get
            {
                var r = -1;
                while (r + 1 < _c.Dig.Depth && IsDug(0, _c.Dig.ElevatorU, r + 1)) r++;
                return r;
            }
        }

        /// <summary>Opens the elevator shaft (new games, and older saves). Opening it gives no ore.</summary>
        public void EnsureElevatorShaft()
        {
            if (!DigMode) return;
            for (var r = 0; r < _c.Dig.ElevatorStartRows; r++) _s.Dug.Add(new DigCell(0, _c.Dig.ElevatorU, r));
            LayerMix(BandOfRow(_c.Dig.ElevatorStartRows - 1), true);
        }

        public bool IsScaffold(int x, int u, int row) => _s.Scaffolds.ContainsKey(new DigCell(x, u, row));

        /// <summary>Something to stand on or bump into: rock, or a scaffold.</summary>
        public bool Standable(int x, int u, int row) => Solid(x, u, row) || IsScaffold(x, u, row);

        /// <summary>Open space in the Mine where a scaffold can go (not the elevator shaft).</summary>
        public bool CanPlaceScaffold(int x, int u, int row) =>
            DigMode && _c.Dig.InSlab(x, u, row) && IsDug(x, u, row) && !IsScaffold(x, u, row) && !(x == 0 && u == _c.Dig.ElevatorU);

        public bool PlaceScaffold(int x, int u, int row, string material)
        {
            if (!CanPlaceScaffold(x, u, row)) return false;
            _s.Scaffolds[new DigCell(x, u, row)] = material ?? "";
            return true;
        }

        /// <summary>Takes a scaffold down; returns the material it used (null if there was none).</summary>
        public string RemoveScaffold(int x, int u, int row)
        {
            var c = new DigCell(x, u, row);
            if (!_s.Scaffolds.TryGetValue(c, out var m)) return null;
            _s.Scaffolds.Remove(c);
            return m;
        }

        /// <summary>Solid rock at a block: air above the surface and in front of the cliff; bedrock past the slab.</summary>
        public bool Solid(int x, int u, int row)
        {
            if (row < 0 || x < 0) return false;
            if (!_c.Dig.InSlab(x, u, row)) return true;
            return !IsDug(x, u, row);
        }

        /// <summary>A block that can be broken: inside the slab, not yet dug, and below the ground (or at the Mine's entry).</summary>
        public bool CanDig(int x, int u, int row) =>
            DigMode && _c.Dig.InSlab(x, u, row) && !IsDug(x, u, row) &&
            (row >= 1 || x < _c.Dig.EntryThickness && u < _c.Dig.EntryLength);

        /// <summary>The charted band used for a row's mix and effort (deeper than charted keeps the last band).</summary>
        public int BandOfRow(int row) => Math.Min(_c.Dig.LayerOfRow(row), _c.Bands.Count);

        /// <summary>Continuation index of a block: further along the cliff, and layers past the charted ones, cost more.</summary>
        public int ContinuationOf(int u, int row) =>
            Math.Max(0, u) / Math.Max(1, _c.Dig.BlocksPerContinuation) + Math.Max(0, _c.Dig.LayerOfRow(row) - _c.Bands.Count);

        /// <summary>A layer's mix; the first time a layer is reached it is revealed (as Dig Deeper did).</summary>
        public Composition LayerMix(int band, bool reveal)
        {
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

        /// <summary>What a block holds (null = plain rock, the layer's baseline ore).</summary>
        public string OreAt(int x, int u, int row, out double richness)
        {
            var band = BandOfRow(row);
            var template = _c.Bands[band - 1];
            return MineVeins.OreAt(_worldSeed ^ (ulong)band * 0x9E3779B97F4A7C15UL, LayerMix(band, false), template.BaselineResourceId, x, u, row, out richness);
        }

        /// <summary>One block's share of a section: the section's durability over its units (ore is a little harder).</summary>
        public long BlockDurabilityMicro(int x, int u, int row)
        {
            var band = BandOfRow(row);
            var section = SiteMath.Durability(_c, band, ContinuationOf(u, row));
            var micro = Math.Max(1L, section * 1000 / Math.Max(1, _c.UnitsPerSection));
            return OreAt(x, u, row, out _) != null ? micro * 5 / 4 : micro;
        }

        // ------------------------------------------------------------------ by hand

        /// <summary>
        /// The supplier swings at a block for <paramref name="ms"/>. Units go straight into the carry (or wait at the face).
        /// <paramref name="from"/> is where they stand and which way they face, remembered for routines. Returns true when
        /// the block breaks.
        /// </summary>
        public bool PlayerDig(int x, int u, int row, long ms, int dps, DigFace from)
        {
            if (!CanDig(x, u, row)) return false;
            TakeIntoCarry(_s.FacePending);
            var cell = new DigCell(x, u, row);
            if (!_s.DigTarget.HasValue || !_s.DigTarget.Value.Equals(cell))
            {
                _s.DigTarget = cell;
                _s.DigDoneMicro = 0;
            }
            if (from != null) _s.LastDig = from.Copy();
            var straightDown = from != null && from.X == x && from.U == u && from.Feet == row;
            _s.DigDoneMicro += Math.Max(0, dps) * Math.Max(0, ms);
            var broke = _s.DigDoneMicro >= BlockDurabilityMicro(x, u, row);
            if (broke)
            {
                _s.DigTarget = null;
                _s.DigDoneMicro = 0;
                Break(cell, byHand: true);
                if (straightDown) _s.DugDown.Add(cell);
            }
            Resolve();
            SaveRng();
            return broke;
        }

        /// <summary>How far the block being dug by hand is broken (0..1), for its cracks.</summary>
        public float DigProgress(int x, int u, int row)
        {
            if (!_s.DigTarget.HasValue || !_s.DigTarget.Value.Equals(new DigCell(x, u, row))) return 0f;
            return Math.Min(1f, (float)_s.DigDoneMicro / Math.Max(1, BlockDurabilityMicro(x, u, row)));
        }

        /// <summary>Breaks a block: the layer is reached, and its units released (to the carry, or to a routine's pack).</summary>
        private void Break(DigCell c, bool byHand)
        {
            _s.Dug.Add(c);
            var band = BandOfRow(c.Row);
            var mix = LayerMix(band, true);
            var ore = OreAt(c.X, c.U, c.Row, out _);
            var primary = ore ?? LayerBaseline(band);
            Release(primary, byHand);
            if (ore != null && _rng.NextDouble() < MineVeins.HandBonusChance) Release(ore, byHand);
            if (!string.IsNullOrEmpty(mix.RareResourceId) && _rng.NextDouble() < mix.RareChance) Release(mix.RareResourceId, byHand);
        }

        private string LayerBaseline(int band)
        {
            var template = _c.Bands[band - 1];
            if (!string.IsNullOrEmpty(template.BaselineResourceId)) return template.BaselineResourceId;
            var mix = LayerMix(band, false);
            var best = mix.Weights[0];
            foreach (var w in mix.Weights) if (w.Value > best.Value) best = w;
            return best.Key;
        }

        private void Release(string resource, bool byHand)
        {
            if (!byHand)
            {
                GiveMine(0, resource);   // a routine: into the pack of whoever works the face
                return;
            }
            _ledger.RecordCreated(resource);
            if (_ledger.TryCarry(resource)) UnitEarned?.Invoke(PlayerId, -1, resource);
            else
            {
                _s.FacePending.Add(resource);
                UnitEarned?.Invoke(null, -1, resource);
            }
        }

        // ------------------------------------------------------------------ routines

        /// <summary>Points a routine at the tunnel the supplier last dug. False when they have not dug anywhere yet.</summary>
        public bool AimRoutineAtLastDig()
        {
            if (!DigMode || _s.LastDig == null) return false;
            _s.RoutineFace = _s.LastDig.Copy();
            return true;
        }

        /// <summary>
        /// The next block of the routine's tunnel: the block ahead at head height, then at leg height; when both are open
        /// the miner steps forward. Stops at bedrock, the edge, ground that cannot be dug, or a drop ahead.
        /// </summary>
        public bool RoutineNext(out DigCell cell)
        {
            cell = default;
            var f = _s.RoutineFace;
            if (!DigMode || f == null || f.DX == 0 && f.DU == 0) return false;
            for (var step = 0; step < 2; step++)
            {
                int ax = f.X + f.DX, au = f.U + f.DU;
                if (!_c.Dig.InSlab(Math.Max(0, ax), au, Math.Max(0, f.Feet))) return false;
                if (CanDig(ax, au, f.Feet - 2)) { cell = new DigCell(ax, au, f.Feet - 2); return true; }
                if (CanDig(ax, au, f.Feet - 1)) { cell = new DigCell(ax, au, f.Feet - 1); return true; }
                if (Standable(ax, au, f.Feet - 2) || Standable(ax, au, f.Feet - 1)) return false;   // rock that cannot be dug, or a scaffold
                if (!Standable(ax, au, f.Feet)) return false;                               // a drop ahead: stop
                if (step == 1) return false;
                f.X = ax;   // the way ahead is open: step forward
                f.U = au;
            }
            return false;
        }

        private long DigNextEventDelay()
        {
            var dps = MineCombinedDps(0);
            if (dps <= 0 || !RoutineNext(out var c)) return long.MaxValue;
            return CeilDiv(BlockDurabilityMicro(c.X, c.U, c.Row) - _s.RoutineFace.DoneMicro, dps);
        }

        private void DigApplyWork(long ms)
        {
            var dps = MineCombinedDps(0);
            if (dps <= 0 || !RoutineNext(out var c)) return;
            _s.RoutineFace.DoneMicro = Math.Min(BlockDurabilityMicro(c.X, c.U, c.Row), _s.RoutineFace.DoneMicro + dps * ms);
        }

        private bool DigEmitDue()
        {
            var changed = false;
            for (var i = 0; i < 64 && RoutineNext(out var c) && _s.RoutineFace.DoneMicro >= BlockDurabilityMicro(c.X, c.U, c.Row); i++)
            {
                _s.RoutineFace.DoneMicro = 0;
                Break(c, byHand: false);
                changed = true;
            }
            return changed;
        }
    }
}
