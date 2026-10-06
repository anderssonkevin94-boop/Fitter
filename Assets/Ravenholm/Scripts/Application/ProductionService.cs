using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Production;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>
    /// Application wrapper for processing and crafting: builds the production system from content,
    /// grants the starting state to a new company, persists the "production" section and exposes
    /// player commands plus Phase 3 debug unlocks.
    /// </summary>
    public sealed class ProductionService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly LogisticsService _logistics;
        private readonly SitesService _sites;
        private readonly ContentCatalogue _catalogue;
        private readonly ProductionState _state = new ProductionState();
        private double _msRemainder;

        public ProductionService(SaveScheduler scheduler, LogisticsService logistics, SitesService sites, ContentCatalogue catalogue)
        {
            _scheduler = scheduler;
            _logistics = logistics;
            _sites = sites;
            _catalogue = catalogue;
            var processes = new List<ProcessRecipe>();
            var recipes = new List<CraftRecipe>();
            if (catalogue != null)
            {
                foreach (var p in catalogue.Processes) if (p != null) processes.Add(Build(p));
                foreach (var r in catalogue.Recipes) if (r != null) recipes.Add(Build(r));
            }
            System = new ProductionSystem(_state, logistics.System, processes, recipes);
            if (catalogue != null)
                foreach (var st in catalogue.Stations)
                    if (st != null) System.BatchTimePerLevel[st.Id] = st.BatchTimePerLevel;
            scheduler.Register(this);
        }

        public ProductionSystem System { get; }

        /// <summary>Raised after a load or reset replaced the whole state.</summary>
        public event Action Reloaded;

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0) return;
            // Shares the debug time multiplier with the gathering sites.
            var exact = deltaSeconds * 1000.0 * _sites.SpeedMultiplier + _msRemainder;
            var whole = (long)Math.Floor(exact);
            _msRemainder = exact - whole;
            if (whole <= 0) return;
            System.Advance(whole);
            _scheduler.MarkDirty();
        }

        /// <summary>Advances processing by exact simulated milliseconds (shared simulation clock).</summary>
        public void AdvanceMs(long ms)
        {
            if (ms <= 0) return;
            System.Advance(ms);
            _scheduler.MarkDirty();
        }

        // ------------------------------------------------------------------ player commands (critical: save at once)

        public bool Craft(string recipeId, out string failure)
        {
            var ok = System.Craft(recipeId, out failure);
            if (ok) _scheduler.SaveNow("crafted");
            return ok;
        }

        public bool SelectProcess(string facility, string processId)
        {
            var ok = System.SelectProcess(facility, processId);
            if (ok) _scheduler.SaveNow("process selected");
            return ok;
        }

        public void SetReserve(string facility, string resourceId, int quantity)
        {
            System.SetReserve(facility, resourceId, quantity);
            _scheduler.SaveNow("reserve set");
        }

        // ------------------------------------------------------------------ debug (Phase 3 test unlocks)

        public void DebugGrantBlueprint(string blueprintId)
        {
            _scheduler.NoteAssisted("grant blueprint");
            System.GrantBlueprint(blueprintId);
            _scheduler.SaveNow("debug: blueprint");
        }

        public int DebugCycleLevel(StationDefinition station)
        {
            _scheduler.NoteAssisted("free station level");
            var next = System.Level(station.Id) + 1;
            if (next > station.MaxLevel) next = station.StartingLevel;
            System.SetLevel(station.Id, next);
            _scheduler.SaveNow("debug: facility level");
            return next;
        }

        /// <summary>DEBUG: adds processed materials to shared inventory (approved test source).</summary>
        public void DebugGrantMaterials(IReadOnlyList<ResourceCost> materials)
        {
            _scheduler.NoteAssisted("grant materials");
            foreach (var m in materials) _logistics.System.ProduceShared(m.resource.Id, m.quantity, "debug grant");
            _scheduler.SaveNow("debug: materials");
        }

        // ------------------------------------------------------------------ build

        private static ProcessRecipe Build(ProcessDefinition p)
        {
            var r = new ProcessRecipe
            {
                Id = p.Id, Name = p.DisplayName, Facility = p.Facility != null ? p.Facility.Id : "", MinLevel = p.MinFacilityLevel,
                Output = p.Output != null ? p.Output.Id : "", OutputQuantity = p.OutputQuantity,
                DurationMs = (int)Math.Round(p.BatchSeconds * 1000),
            };
            foreach (var c in p.Inputs) if (c.resource != null) r.Inputs.Add(new KeyValuePair<string, int>(c.resource.Id, c.quantity));
            return r;
        }

        private static CraftRecipe Build(RecipeDefinition d)
        {
            var r = new CraftRecipe
            {
                Id = d.Id, Station = d.Station != null ? d.Station.Id : "", MinLevel = d.MinFacilityLevel,
                OutputItemId = d.OutputItem != null ? d.OutputItem.Id : "", BlueprintId = d.RequiredBlueprint != null ? d.RequiredBlueprint.Id : "",
                PadContainerId = d.Station != null ? d.Station.OutputPadContainerId : "", PadCapacity = d.Station != null ? d.Station.OutputPadCapacity : 6,
            };
            foreach (var c in d.Cost) if (c.resource != null) r.Inputs.Add(new KeyValuePair<string, int>(c.resource.Id, c.quantity));
            return r;
        }

        // ------------------------------------------------------------------ persistence

        void ISaveSection.Read(SaveEnvelope env)
        {
            _state.Processors.Clear();
            _state.FacilityLevels.Clear();
            _state.Blueprints.Clear();
            _state.SimMs = 0;
            _msRemainder = 0;
            var d = env?.Production;

            if (d == null)
            {
                // New company (or a pre-Phase 3 save): GDD 15 starting state.
                if (_catalogue != null)
                {
                    foreach (var st in _catalogue.Stations) if (st != null) _state.FacilityLevels[st.Id] = st.StartingLevel;
                    foreach (var bp in _catalogue.Blueprints) if (bp != null && bp.OwnedAtStart) _state.Blueprints.Add(bp.Id);
                    if (_catalogue.StartingState != null)
                        foreach (var m in _catalogue.StartingState.SharedStock)
                            if (m.resource != null) _logistics.System.ProduceShared(m.resource.Id, m.quantity, "starting stock");
                }
            }
            else
            {
                _state.SimMs = d.SimMs;
                foreach (var kv in d.FacilityLevels) _state.FacilityLevels[kv.Key] = kv.Value;
                foreach (var b in d.Blueprints) _state.Blueprints.Add(b);
                foreach (var p in d.Processors)
                {
                    var rec = new ProcessorRecord
                    {
                        Facility = p.Facility, Selected = p.Selected, Queued = p.Queued, InFlight = p.InFlight, StartMs = p.StartMs, EndMs = p.EndMs,
                    };
                    foreach (var kv in p.Reserves) rec.Reserves[kv.Key] = kv.Value;
                    _state.Processors[p.Facility] = rec;
                }
            }

            foreach (var p in System.Processes) System.EnsureProcessor(p.Facility);
            Reloaded?.Invoke();
        }

        void ISaveSection.Write(SaveEnvelope env)
        {
            var d = new ProductionSaveData { SimMs = _state.SimMs };
            foreach (var kv in _state.FacilityLevels) d.FacilityLevels[kv.Key] = kv.Value;
            d.Blueprints.AddRange(_state.Blueprints);
            foreach (var p in _state.Processors.Values)
            {
                var pd = new ProcessorSaveData
                {
                    Facility = p.Facility, Selected = p.Selected, Queued = p.Queued, InFlight = p.InFlight, StartMs = p.StartMs, EndMs = p.EndMs,
                };
                foreach (var kv in p.Reserves) pd.Reserves[kv.Key] = kv.Value;
                d.Processors.Add(pd);
            }
            env.Production = d;
        }
    }
}
