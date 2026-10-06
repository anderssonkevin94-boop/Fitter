using System;
using System.Collections.Generic;
using Ravenholm.Domain.Rng;

namespace Ravenholm.Domain.Sites
{
    /// <summary>
    /// Event-driven simulation of one gathering site (GDD 5–7). Time and damage are integers
    /// (milliseconds, micro-damage) and the loop steps from event to event, so splitting the same
    /// duration into different frame sizes gives identical results, and the same code can later
    /// run offline catch-up. It never makes depth decisions and never hauls for the player.
    /// </summary>
    public sealed class SiteSimulator
    {
        private const int MaxEventsPerAdvance = 200000;

        private readonly SiteConfig _c;
        private readonly SiteRecord _s;
        private readonly ISiteLedger _ledger;
        private readonly IEquipmentTable _eq;
        private readonly ulong _worldSeed;
        private IRandomStream _rng;
        private bool _decisionAnnounced;

        public SiteSimulator(SiteConfig config, SiteRecord record, ISiteLedger ledger, IEquipmentTable equipment, ulong worldSeed)
        {
            _c = config;
            _s = record;
            _ledger = ledger;
            _eq = equipment;
            _worldSeed = worldSeed;
            _rng = new Pcg32(record.Rng.Increment != 0 ? record.Rng : RandomStreams.Create(worldSeed, RandomStreamId.SectionYield, SiteMath.StableHash(config.SiteId)).Snapshot());
            _decisionAnnounced = record.Status == SiteStatus.AwaitDecision;
        }

        public SiteConfig Config => _c;
        public SiteRecord Record => _s;
        public int MaxDepth => _c.Bands.Count;
        public bool CanGoDeeper => _s.Depth < _c.Bands.Count;
        public string LastError { get; private set; }

        /// <summary>(workerId or null, treeIndex or -1, resourceId) after a unit is extracted.</summary>
        public event Action<string, int, string> UnitEarned;
        /// <summary>(workerId, units) after a deposit into staging.</summary>
        public event Action<string, int> Deposited;
        public event Action DecisionRequired;

        // ------------------------------------------------------------------ setup

        /// <summary>Starts a brand-new site at band 1 with the given workers walking out.</summary>
        public void InitializeFresh(IEnumerable<WorkerRecord> workers)
        {
            _s.SiteId = _c.SiteId;
            _s.Depth = 1;
            _s.K = 0;
            _s.Composition = SiteMath.Generate(_worldSeed, _c.SiteId, _c.Bands[0]);
            _s.Revealed[1] = _s.Composition;
            _s.Workers.AddRange(workers);
            StartSection();
            foreach (var w in _s.Workers) StartWalkToWork(w);
            SaveRng();
        }

        private void StartSection()
        {
            _s.Status = SiteStatus.Working;
            _decisionAnnounced = false;
            var durability = SiteMath.Durability(_c, _s.Depth, _s.K);
            if (_c.Kind == SiteKind.Mine)
            {
                _s.SectionDurabilityMicro = durability * 1000;
                _s.SectionDoneMicro = 0;
                _s.SectionEmitted = 0;
                return;
            }

            // Forest: preassign each tree's species and its yields from the yield stream (GDD 7).
            _s.Trees.Clear();
            for (var i = 0; i < _c.TreesPerClearing; i++)
            {
                var t = new TreeRecord { Index = i, DurabilityMicro = durability * 1000 };
                t.Species = _s.Composition.Draw(_rng);
                for (var u = 0; u < _c.UnitsPerTree; u++)
                {
                    t.Primary.Add(t.Species);
                    var bonus = !string.IsNullOrEmpty(_s.Composition.RareResourceId) && _rng.NextDouble() < _s.Composition.RareChance;
                    t.Bonus.Add(bonus ? _s.Composition.RareResourceId : null);
                }
                _s.Trees.Add(t);
            }
        }

        // ------------------------------------------------------------------ decisions (player only)

        /// <summary>"Continue Here": same composition, higher effort, same yield.</summary>
        public bool ChooseContinue()
        {
            if (_s.Status != SiteStatus.AwaitDecision) return false;
            _s.K++;
            _s.SectionsCompleted++;
            Resume();
            return true;
        }

        /// <summary>"Dig Deeper" / "Push Deeper": closes this band and reveals the next one.</summary>
        public bool ChooseDeeper()
        {
            if (_s.Status != SiteStatus.AwaitDecision || !CanGoDeeper) return false;
            _s.Depth++;
            _s.K = 0;
            _s.SectionsCompleted++;
            _s.Composition = SiteMath.Generate(_worldSeed, _c.SiteId, _c.Bands[_s.Depth - 1]);
            _s.Revealed[_s.Depth] = _s.Composition;
            Resume();
            return true;
        }

        private void Resume()
        {
            StartSection();
            foreach (var w in _s.Workers)
                if (w.State == WorkerState.AwaitDepthChoice) StartWalkToWork(w);
            SaveRng();
        }

        public long CurrentDurability => SiteMath.Durability(_c, _s.Depth, _s.K);
        public long ContinueDurability => SiteMath.Durability(_c, _s.Depth, _s.K + 1);
        public long DeeperDurability => CanGoDeeper ? SiteMath.Durability(_c, _s.Depth + 1, 0) : 0;

        // ------------------------------------------------------------------ equipment (debug purchases in Phase 2)

        /// <summary>Applies new equipment. Earned material and progress are never reset.</summary>
        public void Equip(string workerId, string toolId, string packId)
        {
            var w = _s.Workers.Find(x => x.Id == workerId);
            if (w == null) return;
            if (toolId != null) w.ToolId = toolId;
            if (packId != null) w.PackId = packId;
            Resolve();
        }

        /// <summary>Adds a recruited worker (crew expansion) who walks straight out to work.</summary>
        public void AddWorker(WorkerRecord w)
        {
            if (_s.Workers.Exists(x => x.Id == w.Id)) return;
            _s.Workers.Add(w);
            StartWalkToWork(w);
            Resolve();
        }

        public void ResetMeasurement()
        {
            _s.MeasureStartMs = _s.SimMs;
            _s.DepositedSinceMeasure = 0;
        }

        /// <summary>Units deposited into staging per simulated minute since the last reset.</summary>
        public double MeasuredUnitsPerMinute =>
            _s.SimMs > _s.MeasureStartMs ? _s.DepositedSinceMeasure * 60000.0 / (_s.SimMs - _s.MeasureStartMs) : 0;

        /// <summary>DEBUG: finish the current section or clearing instantly. Units are still extracted normally.</summary>
        public void DebugCompleteSection()
        {
            if (_c.Kind == SiteKind.Mine) _s.SectionDoneMicro = _s.SectionDurabilityMicro;
            else foreach (var t in _s.Trees) t.DoneMicro = t.DurabilityMicro;
            Resolve();
        }

        // ------------------------------------------------------------------ simulation

        public void Advance(long ms)
        {
            if (ms < 0) ms = 0;
            var remaining = ms;
            var guard = 0;
            Resolve();
            while (remaining > 0)
            {
                if (++guard > MaxEventsPerAdvance)
                {
                    LastError = $"{_c.SiteId}: event bound exceeded; simulation paused for this step";
                    break;
                }
                var next = NextEventDelay();
                if (next == long.MaxValue)
                {
                    _s.SimMs += remaining;
                    break;
                }
                var step = Math.Min(remaining, Math.Max(1, next));
                ApplyWork(step);
                _s.SimMs += step;
                remaining -= step;
                Resolve();
            }
            SaveRng();
        }

        private long NextEventDelay()
        {
            var best = long.MaxValue;
            foreach (var w in _s.Workers)
            {
                if (IsTimed(w.State)) best = Math.Min(best, Math.Max(0, w.PhaseEndMs - _s.SimMs));
                else if (w.State == WorkerState.Working && _c.Kind == SiteKind.Forest)
                {
                    var t = _s.Trees[w.TargetTree];
                    var dps = Dps(w);
                    if (dps > 0 && !t.Felled)
                        best = Math.Min(best, CeilDiv(SiteMath.Threshold(t.DurabilityMicro, t.Emitted + 1, t.Primary.Count) - t.DoneMicro, dps));
                }
            }
            if (_c.Kind == SiteKind.Mine && _s.SectionEmitted < _c.UnitsPerSection)
            {
                var dps = MineCombinedDps();
                if (dps > 0)
                    best = Math.Min(best, CeilDiv(SiteMath.Threshold(_s.SectionDurabilityMicro, _s.SectionEmitted + 1, _c.UnitsPerSection) - _s.SectionDoneMicro, dps));
            }
            return best;
        }

        private void ApplyWork(long ms)
        {
            if (_c.Kind == SiteKind.Mine)
            {
                var dps = MineCombinedDps();
                // Damage beyond the section's durability is clamped; it never spills into the next section.
                _s.SectionDoneMicro = Math.Min(_s.SectionDurabilityMicro, _s.SectionDoneMicro + dps * ms);
                return;
            }
            foreach (var w in _s.Workers)
            {
                if (w.State != WorkerState.Working) continue;
                var t = _s.Trees[w.TargetTree];
                t.DoneMicro = Math.Min(t.DurabilityMicro, t.DoneMicro + Dps(w) * ms);
            }
        }

        /// <summary>Applies everything due at the current instant until nothing changes.</summary>
        private void Resolve()
        {
            for (var pass = 0; pass < 1000; pass++)
            {
                var changed = EmitDueUnits();
                foreach (var w in _s.Workers) changed |= Step(w);
                if (!changed) break;
            }
            UpdateStatus();
        }

        private bool EmitDueUnits()
        {
            var changed = false;
            if (_c.Kind == SiteKind.Mine)
            {
                while (_s.SectionEmitted < _c.UnitsPerSection &&
                       _s.SectionDoneMicro >= SiteMath.Threshold(_s.SectionDurabilityMicro, _s.SectionEmitted + 1, _c.UnitsPerSection))
                {
                    _s.SectionEmitted++;
                    // Primary and rare come from distinct draws: the rare roll is a bonus, never a primary share (GDD 6).
                    var primary = _s.Composition.Draw(_rng);
                    GiveMine(primary);
                    if (!string.IsNullOrEmpty(_s.Composition.RareResourceId) && _rng.NextDouble() < _s.Composition.RareChance)
                        GiveMine(_s.Composition.RareResourceId);
                    changed = true;
                }
                return changed;
            }

            foreach (var t in _s.Trees)
            {
                while (!t.Felled && t.DoneMicro >= SiteMath.Threshold(t.DurabilityMicro, t.Emitted + 1, t.Primary.Count))
                {
                    var i = t.Emitted++;
                    GiveTree(t, t.Primary[i]);
                    if (t.Bonus[i] != null) GiveTree(t, t.Bonus[i]);
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Earned units go to working miners in stable rotating order; overflow waits on the face.</summary>
        private void GiveMine(string resource)
        {
            _ledger.RecordCreated(resource);
            var n = _s.Workers.Count;
            for (var i = 0; i < n; i++)
            {
                var w = _s.Workers[(_s.NextRecipient + i) % n];
                if (w.State != WorkerState.Working || w.Pack.Count >= PackCap(w)) continue;
                w.Pack.Add(resource);
                _s.NextRecipient = (_s.NextRecipient + i + 1) % n;
                UnitEarned?.Invoke(w.Id, -1, resource);
                return;
            }
            _s.FacePending.Add(resource);
            UnitEarned?.Invoke(null, -1, resource);
        }

        private void GiveTree(TreeRecord t, string resource)
        {
            _ledger.RecordCreated(resource);
            var w = t.ClaimedBy != null ? _s.Workers.Find(x => x.Id == t.ClaimedBy) : null;
            if (w != null && w.State == WorkerState.Working && w.TargetTree == t.Index && w.Pack.Count < PackCap(w))
            {
                w.Pack.Add(resource);
                UnitEarned?.Invoke(w.Id, t.Index, resource);
                return;
            }
            t.Pending.Add(resource);
            UnitEarned?.Invoke(null, t.Index, resource);
        }

        /// <summary>One worker's state machine. Returns true when something changed.</summary>
        private bool Step(WorkerRecord w)
        {
            switch (w.State)
            {
                case WorkerState.WalkToWork:
                case WorkerState.Hop:
                    if (_s.SimMs < w.PhaseEndMs) return false;
                    ArriveAtWork(w);
                    return true;

                case WorkerState.Working:
                    if (w.Pack.Count >= PackCap(w)) { StartReturn(w); return true; }   // full pack returns early
                    if (_c.Kind == SiteKind.Mine)
                    {
                        if (_s.SectionEmitted < _c.UnitsPerSection) return false;
                        StartReturn(w);
                        return true;
                    }
                    var tree = _s.Trees[w.TargetTree];
                    if (!tree.Felled) return false;
                    tree.ClaimedBy = null;
                    w.TargetTree = -1;
                    if (ClaimTree(w)) StartTimed(w, WorkerState.Hop, _c.HopMs);
                    else StartReturn(w);
                    return true;

                case WorkerState.ReturnToStaging:
                    if (_s.SimMs < w.PhaseEndMs) return false;
                    if (w.Pack.Count > 0) StartTimed(w, WorkerState.Depositing, _c.DepositMs);
                    else AfterDeposit(w);
                    return true;

                case WorkerState.Depositing:
                    if (_s.SimMs < w.PhaseEndMs) return false;
                    TryDeposit(w);
                    return true;

                case WorkerState.StagingFull:
                    if (_ledger.StagingFree(_c.StagingContainerId, _c.StagingCapacity) <= 0) return false;
                    TryDeposit(w);
                    return true;

                case WorkerState.AwaitDepthChoice:
                    if (!HasWork(w)) return false;
                    StartWalkToWork(w);
                    return true;
            }
            return false;
        }

        private void ArriveAtWork(WorkerRecord w)
        {
            if (_c.Kind == SiteKind.Mine)
            {
                TakePending(w, _s.FacePending);
                if (w.Pack.Count >= PackCap(w) || _s.SectionEmitted >= _c.UnitsPerSection) StartReturn(w);
                else SetState(w, WorkerState.Working);
                return;
            }

            var t = _s.Trees[w.TargetTree];
            TakePending(w, t.Pending);
            if (w.Pack.Count >= PackCap(w)) { StartReturn(w); return; }
            if (!t.Felled) { SetState(w, WorkerState.Working); return; }
            t.ClaimedBy = null;
            w.TargetTree = -1;
            if (ClaimTree(w)) StartTimed(w, WorkerState.Hop, _c.HopMs);
            else StartReturn(w);
        }

        private void TakePending(WorkerRecord w, List<string> pending)
        {
            while (pending.Count > 0 && w.Pack.Count < PackCap(w))
            {
                w.Pack.Add(pending[0]);
                pending.RemoveAt(0);
            }
        }

        private void TryDeposit(WorkerRecord w)
        {
            var free = _ledger.StagingFree(_c.StagingContainerId, _c.StagingCapacity);
            var n = Math.Min(free, w.Pack.Count);
            for (var i = 0; i < n; i++)
            {
                _ledger.Deposit(_c.StagingContainerId, w.Pack[0]);
                w.Pack.RemoveAt(0);
            }
            if (n > 0)
            {
                _s.DepositedTotal += n;
                _s.DepositedSinceMeasure += n;
                Deposited?.Invoke(w.Id, n);
            }
            // Staging full: wait with the remainder still owned by the worker. Nothing is deleted.
            if (w.Pack.Count > 0) SetState(w, WorkerState.StagingFull);
            else AfterDeposit(w);
        }

        private void AfterDeposit(WorkerRecord w)
        {
            if (HasWork(w)) StartWalkToWork(w);
            else SetState(w, WorkerState.AwaitDepthChoice);
        }

        private bool HasWork(WorkerRecord w)
        {
            if (_c.Kind == SiteKind.Mine) return _s.SectionEmitted < _c.UnitsPerSection || _s.FacePending.Count > 0;
            foreach (var t in _s.Trees)
            {
                if (t.ClaimedBy == w.Id) return true;
                if (t.ClaimedBy == null && (!t.Felled || t.Pending.Count > 0)) return true;
            }
            return false;
        }

        /// <summary>Claims the worker's own tree, else the first free standing tree, else a felled tree with pending yield.</summary>
        private bool ClaimTree(WorkerRecord w)
        {
            TreeRecord pick = null;
            foreach (var t in _s.Trees) if (t.ClaimedBy == w.Id) { pick = t; break; }
            if (pick == null) foreach (var t in _s.Trees) if (t.ClaimedBy == null && !t.Felled) { pick = t; break; }
            if (pick == null) foreach (var t in _s.Trees) if (t.ClaimedBy == null && t.Pending.Count > 0) { pick = t; break; }
            if (pick == null) return false;
            pick.ClaimedBy = w.Id;
            w.TargetTree = pick.Index;
            return true;
        }

        private void StartWalkToWork(WorkerRecord w)
        {
            if (_c.Kind == SiteKind.Forest && !ClaimTree(w))
            {
                SetState(w, WorkerState.AwaitDepthChoice);
                return;
            }
            StartTimed(w, WorkerState.WalkToWork, Leg());
        }

        private void StartReturn(WorkerRecord w) => StartTimed(w, WorkerState.ReturnToStaging, Leg());

        private void StartTimed(WorkerRecord w, WorkerState state, int durationMs)
        {
            w.State = state;
            w.PhaseStartMs = _s.SimMs;
            w.PhaseEndMs = _s.SimMs + durationMs;
        }

        private void SetState(WorkerRecord w, WorkerState state)
        {
            w.State = state;
            w.PhaseStartMs = _s.SimMs;
            w.PhaseEndMs = _s.SimMs;
        }

        private void UpdateStatus()
        {
            var exhausted = _c.Kind == SiteKind.Mine
                ? _s.SectionEmitted >= _c.UnitsPerSection && _s.FacePending.Count == 0
                : _s.Trees.TrueForAll(t => t.Felled && t.Pending.Count == 0);
            if (!exhausted) return;
            _s.Status = SiteStatus.AwaitDecision;
            if (_decisionAnnounced) return;
            _decisionAnnounced = true;
            DecisionRequired?.Invoke();
        }

        // ------------------------------------------------------------------ helpers

        private int Leg() => _c.Bands[_s.Depth - 1].LegMs;
        private int Dps(WorkerRecord w) => _eq.ToolDamagePerSecond(w.ToolId);
        private int PackCap(WorkerRecord w) => Math.Max(1, _eq.PackCapacity(w.PackId));

        private long MineCombinedDps()
        {
            long sum = 0;
            foreach (var w in _s.Workers) if (w.State == WorkerState.Working) sum += Dps(w);
            return sum;
        }

        private static bool IsTimed(WorkerState s) =>
            s == WorkerState.WalkToWork || s == WorkerState.Hop || s == WorkerState.ReturnToStaging || s == WorkerState.Depositing;

        private static long CeilDiv(long a, long b) => a <= 0 ? 0 : (a + b - 1) / b;

        private void SaveRng() => _s.Rng = _rng.Snapshot();

        /// <summary>Units currently owned by this site (packs and pending yield), for the ownership audit.</summary>
        public IEnumerable<string> HeldUnits()
        {
            foreach (var w in _s.Workers) foreach (var u in w.Pack) yield return u;
            foreach (var u in _s.FacePending) yield return u;
            foreach (var t in _s.Trees) foreach (var u in t.Pending) yield return u;
        }
    }
}
