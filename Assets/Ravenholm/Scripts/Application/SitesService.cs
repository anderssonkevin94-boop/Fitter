using System;
using System.Collections.Generic;
using System.Globalization;
using Ravenholm.Content;
using Ravenholm.Domain.Rng;
using Ravenholm.Domain.Sites;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>Equipment stats resolved from authored definitions.</summary>
    public sealed class CatalogueEquipmentTable : IEquipmentTable
    {
        private readonly ContentCatalogue _catalogue;

        public CatalogueEquipmentTable(ContentCatalogue catalogue) => _catalogue = catalogue;

        public int ToolDamagePerSecond(string id) =>
            _catalogue != null && _catalogue.TryGet<WorkerEquipmentDefinition>(id, out var e) && e.Slot == EquipmentSlot.Tool ? e.Value : 0;

        public int PackCapacity(string id) =>
            _catalogue != null && _catalogue.TryGet<WorkerEquipmentDefinition>(id, out var e) && e.Slot == EquipmentSlot.Pack ? e.Value : 1;
    }

    /// <summary>
    /// Application wrapper for the Mine and Forest simulations: builds them from content, advances
    /// them with the (debug-adjustable) clock, persists the "sites" save section, and exposes the
    /// player's depth decisions and the Phase 2 debug equipment purchases.
    /// </summary>
    public sealed class SitesService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly LogisticsService _logistics;
        private readonly ContentCatalogue _catalogue;
        private readonly IEquipmentTable _equipment;
        private readonly List<SiteSimulator> _sims = new List<SiteSimulator>();
        private double _msRemainder;

        public SitesService(SaveScheduler scheduler, LogisticsService logistics, ContentCatalogue catalogue)
        {
            _scheduler = scheduler;
            _logistics = logistics;
            _catalogue = catalogue;
            _equipment = new CatalogueEquipmentTable(catalogue);
            logistics.System.RegisterExternalHolder(HeldUnits);
            scheduler.Register(this);
        }

        public IReadOnlyList<SiteSimulator> Sites => _sims;

        /// <summary>DEBUG accelerated time (GDD 24 "time advance"). 1 = real time.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>Simulators were rebuilt (load or reset); views must rebind.</summary>
        public event Action Reloaded;
        /// <summary>(siteId) a section or clearing is exhausted and waits for the player.</summary>
        public event Action<string> DecisionRequired;
        /// <summary>(siteId, workerId or null, treeIndex, resourceId) for presentation feedback.</summary>
        public event Action<string, string, int, string> UnitEarned;

        public SiteSimulator Get(string siteId) => _sims.Find(s => s.Config.SiteId == siteId);

        public void Tick(float deltaSeconds)
        {
            if (_sims.Count == 0 || deltaSeconds <= 0) return;
            var exact = deltaSeconds * 1000.0 * SpeedMultiplier + _msRemainder;
            var whole = (long)Math.Floor(exact);
            _msRemainder = exact - whole;
            if (whole <= 0) return;
            foreach (var sim in _sims) sim.Advance(whole);
            _scheduler.MarkDirty();
        }

        /// <summary>Advances every site by exact simulated milliseconds (used by the shared simulation clock).</summary>
        public void AdvanceMs(long ms)
        {
            if (ms <= 0) return;
            foreach (var sim in _sims) sim.Advance(ms);
            _scheduler.MarkDirty();
        }

        /// <summary>Crew expansion: adds a named recruit to a site (no-op if already present).</summary>
        public bool AddWorker(string siteId, WorkerDefinition w)
        {
            var sim = Get(siteId);
            if (sim == null || w == null || sim.Record.Workers.Exists(x => x.Id == w.Id)) return false;
            sim.AddWorker(new WorkerRecord { Id = w.Id, Name = w.DisplayName, ToolId = w.StartingTool?.Id, PackId = w.StartingPack?.Id });
            return true;
        }

        // ------------------------------------------------------------------ player decisions

        public bool ChooseContinue(string siteId)
        {
            var ok = Get(siteId)?.ChooseContinue() ?? false;
            if (ok) _scheduler.SaveNow("site: continue");
            return ok;
        }

        public bool ChooseDeeper(string siteId)
        {
            var ok = Get(siteId)?.ChooseDeeper() ?? false;
            if (ok) _scheduler.SaveNow("site: deeper");
            return ok;
        }

        // ------------------------------------------------------------------ debug (Phase 2: no economy yet)

        /// <summary>DEBUG purchase: cycles a worker's tool or pack to the next tier (wrapping to tier 1).</summary>
        public WorkerEquipmentDefinition DebugCycleEquipment(string siteId, string workerId, EquipmentSlot slot)
        {
            var sim = Get(siteId);
            var w = sim?.Record.Workers.Find(x => x.Id == workerId);
            if (w == null || _catalogue == null) return null;
            var tiers = new List<WorkerEquipmentDefinition>();
            foreach (var e in _catalogue.Equipment) if (e != null && e.Slot == slot) tiers.Add(e);
            if (tiers.Count == 0) return null;
            tiers.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            var current = slot == EquipmentSlot.Tool ? w.ToolId : w.PackId;
            var i = tiers.FindIndex(e => e.Id == current);
            var next = tiers[(i + 1) % tiers.Count];
            if (slot == EquipmentSlot.Tool) sim.Equip(workerId, next.Id, null);
            else sim.Equip(workerId, null, next.Id);
            _scheduler.SaveNow("debug: equipment");
            return next;
        }

        public void DebugCompleteSection(string siteId)
        {
            Get(siteId)?.DebugCompleteSection();
            _scheduler.SaveNow("debug: complete section");
        }

        public void ResetMeasurements()
        {
            foreach (var s in _sims) s.ResetMeasurement();
        }

        // ------------------------------------------------------------------ build

        public static SiteConfig BuildConfig(SiteDefinition def)
        {
            var c = new SiteConfig
            {
                SiteId = def.Id,
                Kind = def.SiteType == SiteType.Mine ? SiteKind.Mine : SiteKind.Forest,
                StagingContainerId = def.StagingContainerId,
                StagingCapacity = def.StagingCapacity,
                UnitsPerSection = def.UnitsPerSectionOrTree,
                UnitsPerTree = def.UnitsPerSectionOrTree,
                TreesPerClearing = def.TreesPerClearing,
                BaseDurability = def.BaseDurability,
                Growth = def.GrowthPerBand,
                RoundTo = def.RoundTo,
                KLinear = def.ContinuationLinear,
                KQuad = def.ContinuationQuadratic,
                TerminalKCap = def.TerminalContinuationCap,
                HopMs = (int)Math.Round(def.HopSeconds * 1000),
                DepositMs = (int)Math.Round(def.DepositSeconds * 1000),
            };
            foreach (var b in def.Bands)
            {
                var t = new BandTemplate
                {
                    Index = b.Index,
                    Name = b.DisplayName,
                    Discovery = b.Discovery,
                    RareResourceId = b.RareResource != null ? b.RareResource.Id : null,
                    RareChance = b.RareChance,
                    DurabilityFactor = b.DurabilityFactor,
                    LegMs = (int)Math.Round(b.TravelSecondsPerLeg * 1000),
                    Variation = b.CompositionVariation,
                    BaselineResourceId = b.BaselineResource != null ? b.BaselineResource.Id : null,
                    BaselineMinShare = b.BaselineMinShare,
                };
                foreach (var w in b.PrimaryWeights)
                    if (w.resource != null) t.Weights.Add(new KeyValuePair<string, double>(w.resource.Id, w.weight));
                c.Bands.Add(t);
            }
            return c;
        }

        private IEnumerable<string> HeldUnits()
        {
            foreach (var s in _sims)
                foreach (var u in s.HeldUnits()) yield return u;
        }

        private SiteSimulator Create(SiteDefinition def, SiteRecord record)
        {
            var sim = new SiteSimulator(BuildConfig(def), record, _logistics.System, _equipment, _scheduler.Coordinator.WorldSeed);
            var id = def.Id;
            sim.DecisionRequired += () => DecisionRequired?.Invoke(id);
            sim.UnitEarned += (worker, tree, res) => UnitEarned?.Invoke(id, worker, tree, res);
            return sim;
        }

        // ------------------------------------------------------------------ persistence

        void ISaveSection.Read(SaveEnvelope env)
        {
            _sims.Clear();
            _msRemainder = 0;
            if (_catalogue == null) return;
            foreach (var def in _catalogue.Sites)
            {
                if (def == null) continue;
                var saved = env?.Sites?.Find(x => x.SiteId == def.Id);
                if (saved != null)
                {
                    _sims.Add(Create(def, FromSave(saved)));
                    continue;
                }
                var sim = Create(def, new SiteRecord());
                var workers = new List<WorkerRecord>();
                foreach (var w in def.Workers)
                    if (w != null) workers.Add(new WorkerRecord { Id = w.Id, Name = w.DisplayName, ToolId = w.StartingTool?.Id, PackId = w.StartingPack?.Id });
                sim.InitializeFresh(workers);
                _sims.Add(sim);
            }
            Reloaded?.Invoke();
        }

        void ISaveSection.Write(SaveEnvelope env)
        {
            env.Sites = new List<SiteSaveData>();
            foreach (var sim in _sims) env.Sites.Add(ToSave(sim.Record));
        }

        private static SiteSaveData ToSave(SiteRecord s)
        {
            var d = new SiteSaveData
            {
                SiteId = s.SiteId, Depth = s.Depth, K = s.K, SectionsCompleted = s.SectionsCompleted, Status = s.Status.ToString(),
                SimMs = s.SimMs,
                RngState = s.Rng.State.ToString(CultureInfo.InvariantCulture),
                RngIncrement = s.Rng.Increment.ToString(CultureInfo.InvariantCulture),
                Composition = ToSave(s.Composition),
                SectionDurabilityMicro = s.SectionDurabilityMicro, SectionDoneMicro = s.SectionDoneMicro, SectionEmitted = s.SectionEmitted,
                NextRecipient = s.NextRecipient,
                DepositedTotal = s.DepositedTotal, MeasureStartMs = s.MeasureStartMs, DepositedSinceMeasure = s.DepositedSinceMeasure,
            };
            foreach (var kv in s.Revealed) d.Revealed[kv.Key] = ToSave(kv.Value);
            d.FacePending.AddRange(s.FacePending);
            foreach (var t in s.Trees)
            {
                var td = new TreeSaveData
                {
                    Index = t.Index, Species = t.Species, DurabilityMicro = t.DurabilityMicro, DoneMicro = t.DoneMicro,
                    Emitted = t.Emitted, ClaimedBy = t.ClaimedBy,
                };
                td.Primary.AddRange(t.Primary);
                td.Bonus.AddRange(t.Bonus);
                td.Pending.AddRange(t.Pending);
                d.Trees.Add(td);
            }
            foreach (var w in s.Workers)
            {
                var wd = new WorkerSaveData
                {
                    Id = w.Id, Name = w.Name, ToolId = w.ToolId, PackId = w.PackId, State = w.State.ToString(),
                    PhaseStartMs = w.PhaseStartMs, PhaseEndMs = w.PhaseEndMs, TargetTree = w.TargetTree,
                };
                wd.Carried.AddRange(w.Pack);
                d.Workers.Add(wd);
            }
            return d;
        }

        private static CompositionData ToSave(Composition c)
        {
            var d = new CompositionData { RareResourceId = c.RareResourceId, RareChance = c.RareChance };
            foreach (var kv in c.Weights) d.Weights.Add(new WeightData { Id = kv.Key, Weight = kv.Value });
            return d;
        }

        private static Composition FromSave(CompositionData d)
        {
            var c = new Composition { RareResourceId = d?.RareResourceId, RareChance = d?.RareChance ?? 0 };
            if (d != null) foreach (var w in d.Weights) c.Weights.Add(new KeyValuePair<string, double>(w.Id, w.Weight));
            return c;
        }

        private static SiteRecord FromSave(SiteSaveData d)
        {
            var s = new SiteRecord
            {
                SiteId = d.SiteId, Depth = Math.Max(1, d.Depth), K = d.K, SectionsCompleted = d.SectionsCompleted,
                Status = Enum.TryParse(d.Status, out SiteStatus st) ? st : SiteStatus.Working,
                SimMs = d.SimMs,
                Rng = new RandomState
                {
                    State = ulong.Parse(d.RngState ?? "0", CultureInfo.InvariantCulture),
                    Increment = ulong.Parse(d.RngIncrement ?? "0", CultureInfo.InvariantCulture),
                },
                Composition = FromSave(d.Composition),
                SectionDurabilityMicro = d.SectionDurabilityMicro, SectionDoneMicro = d.SectionDoneMicro, SectionEmitted = d.SectionEmitted,
                NextRecipient = d.NextRecipient,
                DepositedTotal = d.DepositedTotal, MeasureStartMs = d.MeasureStartMs, DepositedSinceMeasure = d.DepositedSinceMeasure,
            };
            foreach (var kv in d.Revealed) s.Revealed[kv.Key] = FromSave(kv.Value);
            s.FacePending.AddRange(d.FacePending);
            foreach (var td in d.Trees)
            {
                var t = new TreeRecord
                {
                    Index = td.Index, Species = td.Species, DurabilityMicro = td.DurabilityMicro, DoneMicro = td.DoneMicro,
                    Emitted = td.Emitted, ClaimedBy = td.ClaimedBy,
                };
                t.Primary.AddRange(td.Primary);
                t.Bonus.AddRange(td.Bonus);
                t.Pending.AddRange(td.Pending);
                s.Trees.Add(t);
            }
            foreach (var wd in d.Workers)
            {
                var w = new WorkerRecord
                {
                    Id = wd.Id, Name = wd.Name, ToolId = wd.ToolId, PackId = wd.PackId,
                    State = Enum.TryParse(wd.State, out WorkerState ws) ? ws : WorkerState.ReturnToStaging,
                    PhaseStartMs = wd.PhaseStartMs, PhaseEndMs = wd.PhaseEndMs, TargetTree = wd.TargetTree,
                };
                w.Pack.AddRange(wd.Carried);
                s.Workers.Add(w);
            }
            return s;
        }
    }
}
