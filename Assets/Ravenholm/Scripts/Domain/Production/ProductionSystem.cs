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
    /// Timed automatic processing and instant crafting (GDD 8/9). Processing only ever consumes
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
        public IEnumerable<ProcessRecipe> Processes => _processes.Values;
        public IEnumerable<CraftRecipe> Recipes => _recipes.Values;

        public event Action<string, ProcessRecipe> BatchStarted;
        public event Action<string, ProcessRecipe> BatchCompleted;
        /// <summary>(recipe, instanceId) after a successful craft.</summary>
        public event Action<CraftRecipe, string> ItemCrafted;

        public int Level(string facility) => _s.FacilityLevels.TryGetValue(facility, out var l) ? l : 1;

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
            var remaining = ms;
            for (var guard = 0; remaining > 0 && guard < 100000; guard++)
            {
                var next = long.MaxValue;
                foreach (var p in _s.Processors.Values)
                    if (p.InFlight != null) next = Math.Min(next, Math.Max(0, p.EndMs - _s.SimMs));
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
            }
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
            p.EndMs = at + r.DurationMs;
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
            if (_ledger.ContainerUnitCount(r.PadContainerId) >= r.PadCapacity) return "Output pad full: carry finished goods away first";
            foreach (var c in r.Inputs)
            {
                var have = _ledger.SharedAvailable(c.Key);
                if (have < c.Value) return $"Missing {c.Value - have}× {c.Key}";
            }
            return null;
        }

        /// <summary>Instant craft (GDD 9). All checks pass and everything commits, or nothing changes.</summary>
        public bool Craft(string recipeId, out string failure)
        {
            failure = CraftBlockedReason(recipeId);
            if (failure != null) return false;
            var r = Recipe(recipeId);
            if (!_ledger.TryCraftToPad(r.Inputs, r.OutputItemId, r.PadContainerId, r.PadCapacity, "crafted", out var instance, out failure)) return false;
            ItemCrafted?.Invoke(r, instance);
            return true;
        }
    }
}
