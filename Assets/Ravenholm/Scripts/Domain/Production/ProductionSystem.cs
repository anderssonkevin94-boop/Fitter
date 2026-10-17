using System;
using System.Collections.Generic;

namespace Ravenholm.Domain.Production
{
    public sealed class ProcessRecipe
    {
        public string Id = "";
        public string Name = "";
        public string Facility = "";
        public int MinLevel = 1;
        public List<KeyValuePair<string, int>> Inputs = new List<KeyValuePair<string, int>>();
        public string Output = "";
        public int OutputQuantity = 1;
        public int DurationMs = 4000;
    }

    public sealed class CraftRecipe
    {
        public string Id = "";
        public string Station = "";
        public int MinLevel = 1;
        public List<KeyValuePair<string, int>> Inputs = new List<KeyValuePair<string, int>>();
        public string OutputItemId = "";
        public string BlueprintId = "";
        public string PadContainerId = "";
        public int PadCapacity = 6;
        /// <summary>How long one takes at the station's level (the crafting expansion: Shop Titans style, minutes to hours).</summary>
        public int DurationMs = 300000;
    }

    /// <summary>One item being made in one of a station's craft slots (materials paid at the start).</summary>
    public sealed class CraftJob
    {
        public string Station = "";
        public string RecipeId = "";
        public long StartMs;
        public long EndMs;
        /// <summary>Finished, waiting for room on the output pad.</summary>
        public bool Waiting;
    }

    /// <summary>One processing line (GDD 8: one line per facility, automatic repetition).</summary>
    public sealed class ProcessorRecord
    {
        public string Facility = "";
        public string Selected;
        /// <summary>A change requested while a batch runs; applied after that batch (GDD 8).</summary>
        public string Queued;
        public string InFlight;
        public long StartMs;
        public long EndMs;
        /// <summary>Units of each input the line must leave untouched (default 0).</summary>
        public readonly SortedDictionary<string, int> Reserves = new SortedDictionary<string, int>(StringComparer.Ordinal);
    }

    public sealed class ProductionState
    {
        public long SimMs;
        public readonly SortedDictionary<string, ProcessorRecord> Processors = new SortedDictionary<string, ProcessorRecord>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> FacilityLevels = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedSet<string> Blueprints = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>Items being made, in the order they were started.</summary>
        public readonly List<CraftJob> Crafts = new List<CraftJob>();
    }

    /// <summary>Shared-ledger and output-pad operations, implemented by the logistics system.</summary>
    public interface IProductionLedger
    {
        int SharedAvailable(string resourceId);
        /// <summary>Debits every cost or nothing.</summary>
        bool TryConsumeShared(IReadOnlyList<KeyValuePair<string, int>> costs, string reason);
        /// <summary>Approved source: processing output or starting stock.</summary>
        void ProduceShared(string resourceId, int quantity, string reason);
        int ContainerUnitCount(string containerId);
        /// <summary>Atomic craft: pad space and costs are checked, then costs are debited and one instance is created on the pad.</summary>
        bool TryCraftToPad(IReadOnlyList<KeyValuePair<string, int>> costs, string itemDefinitionId, string padId, int padCapacity, string reason,
            out string instanceId, out string failure);
    }

    /// <summary>
    /// Timed automatic processing and timed crafting (GDD 8/9; crafting timed since 2026-10-16). Processing only ever consumes
    /// delivered shared inventory, never undelivered staging. Integer milliseconds, event-driven,
    /// so it can share the offline catch-up path later.
    /// </summary>
    public sealed class ProductionSystem
    {
        private readonly ProductionState _s;
        private readonly IProductionLedger _ledger;
        private readonly Dictionary<string, ProcessRecipe> _processes = new Dictionary<string, ProcessRecipe>(StringComparer.Ordinal);
        private readonly Dictionary<string, CraftRecipe> _recipes = new Dictionary<string, CraftRecipe>(StringComparer.Ordinal);

        public ProductionSystem(ProductionState state, IProductionLedger ledger, IEnumerable<ProcessRecipe> processes, IEnumerable<CraftRecipe> recipes)
        {
            _s = state;
            _ledger = ledger;
            foreach (var p in processes) _processes[p.Id] = p;
            foreach (var r in recipes) _recipes[r.Id] = r;
        }

        public ProductionState State => _s;

        /// <summary>Batch time multiplier per level above 1, per facility (GDD 16: ×0.85 at level 2).</summary>
        public readonly Dictionary<string, double> BatchTimePerLevel = new Dictionary<string, double>(StringComparer.Ordinal);

        public int EffectiveDurationMs(ProcessRecipe r)
        {
            var level = Level(r.Facility);
            var per = BatchTimePerLevel.TryGetValue(r.Facility, out var f) ? f : 1.0;
            // GDD 16: each level above a process's own gate speeds it ("basic batch time ×0.85" at level 2).
            var above = Math.Max(0, level - Math.Max(1, r.MinLevel));
            return (int)Math.Max(1, Math.Round(r.DurationMs * Math.Pow(per, above)));
        }
        public IEnumerable<ProcessRecipe> Processes => _processes.Values;
        public IEnumerable<CraftRecipe> Recipes => _recipes.Values;

        public event Action<string, ProcessRecipe> BatchStarted;
        public event Action<string, ProcessRecipe> BatchCompleted;
        /// <summary>(recipe, instanceId) after a successful craft.</summary>
        public event Action<CraftRecipe, string> ItemCrafted;
        /// <summary>A craft was started in one of a station's slots.</summary>
        public event Action<CraftRecipe> CraftStarted;

        /// <summary>Craft slots at level 1 (each station level above 1 adds one).</summary>
        public int CraftSlotsBase = 2;

        public int CraftSlots(string station) => CraftSlotsBase + Math.Max(0, Level(station) - 1);

        public IEnumerable<CraftJob> CraftsAt(string station)
        {
            foreach (var j in _s.Crafts) if (j.Station == station) yield return j;
        }

        public int BusySlots(string station)
        {
            var n = 0;
            foreach (var j in _s.Crafts) if (j.Station == station) n++;
            return n;
        }

        /// <summary>A recipe's time at its station's level (each level above the recipe's own gate is quicker, as for batches).</summary>
        public int EffectiveCraftMs(CraftRecipe r)
        {
            if (InstantCrafts) return 1;   // playtesting: every craft done at once
            var per = BatchTimePerLevel.TryGetValue(r.Station, out var f) ? f : 1.0;
            var above = Math.Max(0, Level(r.Station) - Math.Max(1, r.MinLevel));
            return (int)Math.Max(1, Math.Round(r.DurationMs * Math.Pow(per, above)));
        }

        public int Level(string facility) => _s.FacilityLevels.TryGetValue(facility, out var l) ? l : 1;

        /// <summary>Playtesting (Kevin, 2026-10-17, temporary): crafts take no time.</summary>
        public bool InstantCrafts;

        public ProcessRecipe Process(string id) => id != null && _processes.TryGetValue(id, out var p) ? p : null;
        public CraftRecipe Recipe(string id) => id != null && _recipes.TryGetValue(id, out var r) ? r : null;

        // ------------------------------------------------------------------ setup / debug

        public ProcessorRecord EnsureProcessor(string facility)
        {
            if (_s.Processors.TryGetValue(facility, out var p)) return p;
            p = new ProcessorRecord { Facility = facility };
            foreach (var r in _processes.Values)
                if (r.Facility == facility && r.MinLevel <= Level(facility)) { p.Selected = r.Id; break; }
            _s.Processors.Add(facility, p);
            return p;
        }

        public void GrantBlueprint(string blueprintId) => _s.Blueprints.Add(blueprintId);

        /// <summary>DEBUG / Phase 6 upgrades: sets a facility level. Running batches keep their saved timing.</summary>
        public void SetLevel(string facility, int level) => _s.FacilityLevels[facility] = Math.Max(1, level);

        // ------------------------------------------------------------------ processing commands

        /// <summary>Selects a line's recipe. Idle lines switch now; running lines switch after the current batch.</summary>
        public bool SelectProcess(string facility, string processId)
        {
            var r = Process(processId);
            if (r == null || r.Facility != facility) return false;
            var p = EnsureProcessor(facility);
            if (p.InFlight == null)
            {
                p.Selected = processId;
                p.Queued = null;
                TryStart(p);
            }
            else p.Queued = processId == p.Selected ? null : processId;
            return true;
        }

        public void SetReserve(string facility, string resourceId, int quantity)
        {
            var p = EnsureProcessor(facility);
            if (quantity <= 0) p.Reserves.Remove(resourceId);
            else p.Reserves[resourceId] = quantity;
            TryStart(p);
        }

        /// <summary>Why an idle line is not running (null when it is running or could start).</summary>
        public string BlockedReason(string facility)
        {
            if (!_s.Processors.TryGetValue(facility, out var p) || p.InFlight != null) return null;
            var r = Process(p.Selected);
            if (r == null) return "No process selected";
            if (Level(facility) < r.MinLevel) return $"Requires facility level {r.MinLevel}";
            foreach (var input in r.Inputs)
            {
                p.Reserves.TryGetValue(input.Key, out var reserve);
                var usable = _ledger.SharedAvailable(input.Key) - reserve;
                if (usable < input.Value) return $"Missing {input.Value - Math.Max(0, usable)}× {input.Key}" + (reserve > 0 ? $" (reserving {reserve})" : "");
            }
            return null;
        }

        // ------------------------------------------------------------------ simulation

        public void Advance(long ms)
        {
            if (ms < 0) ms = 0;
            foreach (var p in _s.Processors.Values) TryStart(p);
            DeliverFinishedCrafts();
            var remaining = ms;
            for (var guard = 0; remaining > 0 && guard < 100000; guard++)
            {
                var next = long.MaxValue;
                foreach (var p in _s.Processors.Values)
                    if (p.InFlight != null) next = Math.Min(next, Math.Max(0, p.EndMs - _s.SimMs));
                foreach (var j in _s.Crafts)
                    if (!j.Waiting) next = Math.Min(next, Math.Max(0, j.EndMs - _s.SimMs));
                if (next == long.MaxValue)
                {
                    _s.SimMs += remaining;
                    break;
                }
                var step = Math.Min(remaining, next);
                _s.SimMs += step;
                remaining -= step;
                foreach (var p in _s.Processors.Values)
                {
                    if (p.InFlight == null || _s.SimMs < p.EndMs) continue;
                    var r = Process(p.InFlight);
                    var completedAt = p.EndMs;
                    p.InFlight = null;
                    if (r != null)
                    {
                        _ledger.ProduceShared(r.Output, r.OutputQuantity, "processing output");
                        BatchCompleted?.Invoke(p.Facility, r);
                    }
                    if (p.Queued != null)
                    {
                        p.Selected = p.Queued;
                        p.Queued = null;
                    }
                    TryStart(p, completedAt);
                }
                foreach (var j in _s.Crafts) if (!j.Waiting && _s.SimMs >= j.EndMs) j.Waiting = true;
                DeliverFinishedCrafts();
            }
        }

        /// <summary>Finished crafts move onto their station's output pad, in order, as long as there is room.</summary>
        private void DeliverFinishedCrafts()
        {
            for (var i = 0; i < _s.Crafts.Count; i++)
            {
                var j = _s.Crafts[i];
                if (!j.Waiting) continue;
                var r = Recipe(j.RecipeId);
                if (r == null) { _s.Crafts.RemoveAt(i--); continue; }
                if (!_ledger.TryCraftToPad(Array.Empty<KeyValuePair<string, int>>(), r.OutputItemId, r.PadContainerId, r.PadCapacity, "crafted", out var instance, out _))
                    continue;   // the pad is full: it waits in its slot until there is room
                _s.Crafts.RemoveAt(i--);
                ItemCrafted?.Invoke(r, instance);
            }
        }

        /// <summary>DEBUG: every craft in progress finishes now (and goes to its pad if there is room).</summary>
        public void DebugFinishCrafts()
        {
            foreach (var j in _s.Crafts) { j.Waiting = true; j.EndMs = Math.Min(j.EndMs, _s.SimMs); }
            DeliverFinishedCrafts();
        }

        private void TryStart(ProcessorRecord p) => TryStart(p, _s.SimMs);

        /// <summary>Starts a batch if inputs (above reserves) exist. Inputs are debited atomically at the start (GDD 8).</summary>
        private void TryStart(ProcessorRecord p, long at)
        {
            if (p.InFlight != null) return;
            var r = Process(p.Selected);
            if (r == null || Level(p.Facility) < r.MinLevel) return;
            foreach (var input in r.Inputs)
            {
                p.Reserves.TryGetValue(input.Key, out var reserve);
                if (_ledger.SharedAvailable(input.Key) - reserve < input.Value) return;
            }
            if (!_ledger.TryConsumeShared(r.Inputs, "processing input")) return;
            p.InFlight = r.Id;
            p.StartMs = at;
            p.EndMs = at + EffectiveDurationMs(r);   // level speed applies to new batches only
            BatchStarted?.Invoke(p.Facility, r);
        }

        // ------------------------------------------------------------------ crafting

        /// <summary>Why a recipe cannot be crafted right now (null = it can). Checked in GDD 9 order.</summary>
        public string CraftBlockedReason(string recipeId)
        {
            var r = Recipe(recipeId);
            if (r == null) return "Unknown recipe";
            if (!_s.Blueprints.Contains(r.BlueprintId)) return "Blueprint not owned";
            if (Level(r.Station) < r.MinLevel) return $"Requires station level {r.MinLevel}";
            if (BusySlots(r.Station) >= CraftSlots(r.Station)) return $"Every craft slot is busy ({CraftSlots(r.Station)})";
            foreach (var c in r.Inputs)
            {
                var have = _ledger.SharedAvailable(c.Key);
                if (have < c.Value) return $"Missing {c.Value - have}× {c.Key}";
            }
            return null;
        }

        /// <summary>
        /// Starts a craft in a free slot (the crafting expansion, Kevin 2026-10-16: timed, Shop Titans style). The materials
        /// are paid now; the item lands on the output pad when it is done (or waits in its slot until the pad has room).
        /// </summary>
        public bool Craft(string recipeId, out string failure)
        {
            failure = CraftBlockedReason(recipeId);
            if (failure != null) return false;
            var r = Recipe(recipeId);
            if (!_ledger.TryConsumeShared(r.Inputs, "crafting input")) { failure = "Materials changed; try again"; return false; }
            _s.Crafts.Add(new CraftJob { Station = r.Station, RecipeId = r.Id, StartMs = _s.SimMs, EndMs = _s.SimMs + EffectiveCraftMs(r) });
            CraftStarted?.Invoke(r);
            return true;
        }
    }
}
