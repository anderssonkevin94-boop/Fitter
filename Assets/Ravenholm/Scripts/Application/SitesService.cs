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
        public float SpeedMultiplier
        {
            get => _speed;
            set
            {
                _speed = value;
                if (value > 1f) _scheduler.NoteAssisted("accelerated time");
            }
        }

        private float _speed = 1f;

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

        // ------------------------------------------------------------------ the supplier's own work (solo play)

        /// <summary>The supplier's tool and pack (equipment ids). The tool sets how fast they mine and chop.</summary>
        public string PlayerToolId { get; private set; }
        public string PlayerPackId { get; private set; }
        public int PlayerDamagePerSecond => _equipment.ToolDamagePerSecond(PlayerToolId);

        public void SetPlayerGear(string toolId, string packId)
        {
            if (toolId != null) PlayerToolId = toolId;
            if (packId != null) PlayerPackId = packId;
            foreach (var sim in _sims) sim.Equip(SiteSimulator.PlayerId, toolId, packId);   // a routine in progress gets it too
            _scheduler.SaveNow("player gear");
        }

        /// <summary>The site the supplier is working on a routine, or null.</summary>
        public string RoutineSiteId => _sims.Find(s => s.Record.Workers.Exists(w => w.Id == SiteSimulator.PlayerId))?.Config.SiteId;

        public WorkerRecord RoutineWorker
        {
            get
            {
                foreach (var s in _sims)
                {
                    var w = s.Record.Workers.Find(x => x.Id == SiteSimulator.PlayerId);
                    if (w != null) return w;
                }
                return null;
            }
        }

        /// <summary>
        /// Puts the supplier on a routine while away (one job: mine a face, or cut the forest). The simulation does that
        /// work at the ordinary rate, online and offline, until the supplier takes control again.
        /// </summary>
        public bool StartRoutine(string siteId, int face = 0)
        {
            var sim = Get(siteId);
            if (sim == null) return false;
            EndRoutine();
            sim.AddWorker(new WorkerRecord { Id = SiteSimulator.PlayerId, Name = "You", ToolId = PlayerToolId, PackId = PlayerPackId, Face = face });
            _scheduler.SaveNow("routine started");
            RoutineChanged?.Invoke();
            return true;
        }

        /// <summary>Ends the routine: whatever the supplier carries for it comes back to the carry (else staging).</summary>
        public bool EndRoutine()
        {
            var ended = false;
            foreach (var sim in _sims) ended |= sim.RemoveWorker(SiteSimulator.PlayerId);
            if (!ended) return false;
            _scheduler.SaveNow("routine ended");
            RoutineChanged?.Invoke();
            return true;
        }

        public event Action RoutineChanged;

        private double _mineMs, _chopMs;

        /// <summary>The supplier swings at a Mine face for <paramref name="seconds"/>. Returns units carried away.</summary>
        public int PlayerMine(string siteId, int face, float seconds)
        {
            var sim = Get(siteId);
            if (sim == null) return 0;
            _mineMs += seconds * 1000.0 * SpeedMultiplier;
            var ms = (long)Math.Floor(_mineMs);
            _mineMs -= ms;
            var n = sim.PlayerMine(face, ms, PlayerDamagePerSecond);
            _scheduler.MarkDirty();
            return n;
        }

        /// <summary>The supplier chops a tree of the current clearing. Returns units carried away.</summary>
        public int PlayerChop(string siteId, int tree, float seconds)
        {
            var sim = Get(siteId);
            if (sim == null) return 0;
            _chopMs += seconds * 1000.0 * SpeedMultiplier;
            var ms = (long)Math.Floor(_chopMs);
            _chopMs -= ms;
            var n = sim.PlayerChop(tree, ms, PlayerDamagePerSecond);
            _scheduler.MarkDirty();
            return n;
        }

        // ------------------------------------------------------------------ Mine tunnels

        /// <summary>Starts a sideways tunnel at a dug level; sends <paramref name="workerId"/> there when given. Returns the face (0 = failed).</summary>
        public int StartTunnel(string siteId, int level, string workerId = null)
        {
            var sim = Get(siteId);
            var face = sim?.StartTunnel(level) ?? 0;
            if (face == 0) return 0;
            if (workerId != null) sim.AssignWorker(workerId, face);
            _scheduler.SaveNow("site: tunnel");
            return face;
        }

        public bool ExtendTunnel(string siteId, int face)
        {
            var ok = Get(siteId)?.ExtendTunnel(face) ?? false;
            if (ok) _scheduler.SaveNow("site: extend tunnel");
            return ok;
        }

        public bool AssignWorker(string siteId, string workerId, int face)
        {
            var ok = Get(siteId)?.AssignWorker(workerId, face) ?? false;
            if (ok) _scheduler.SaveNow("site: assign miner");
            return ok;
        }

        // ------------------------------------------------------------------ debug (Phase 2: no economy yet)

        /// <summary>DEBUG purchase: cycles a worker's tool or pack to the next tier (wrapping to tier 1).</summary>
        public WorkerEquipmentDefinition DebugCycleEquipment(string siteId, string workerId, EquipmentSlot slot)
        {
            _scheduler.NoteAssisted("free worker gear");
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

        /// <summary>DEBUG: cycles the supplier's own tool or pack to the next tier (wrapping to tier 1).</summary>
        public void DebugCyclePlayerGear(EquipmentSlot slot)
        {
            _scheduler.NoteAssisted("free player gear");
            var tiers = new List<WorkerEquipmentDefinition>();
            foreach (var e in _catalogue.Equipment) if (e != null && e.Slot == slot) tiers.Add(e);
            if (tiers.Count == 0) return;
            tiers.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            var current = slot == EquipmentSlot.Tool ? PlayerToolId : PlayerPackId;
            var next = tiers[(tiers.FindIndex(e => e.Id == current) + 1) % tiers.Count];
            if (slot == EquipmentSlot.Tool) SetPlayerGear(next.Id, null);
            else SetPlayerGear(null, next.Id);
        }

        public void DebugCompleteSection(string siteId)
        {
            _scheduler.NoteAssisted("instant site completion");
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
                LevelsPerBand = Math.Max(1, def.LevelsPerBand),
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

        private string StartingGear(EquipmentSlot slot)
        {
            WorkerEquipmentDefinition best = null;
            foreach (var e in _catalogue.Equipment)
                if (e != null && e.Slot == slot && (best == null || e.Tier < best.Tier)) best = e;
            return best?.Id;
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
            PlayerToolId = env?.PlayerGear?.ToolId ?? StartingGear(EquipmentSlot.Tool);
            PlayerPackId = env?.PlayerGear?.PackId ?? StartingGear(EquipmentSlot.Pack);
            foreach (var def in _catalogue.Sites)
            {
                if (def == null) continue;
                var saved = env?.Sites?.Find(x => x.SiteId == def.Id);
                if (saved != null)
                {
                    var record = FromSave(saved);
                    if (def.SiteType == SiteType.Mine && saved.Layout < MineLayout) ConvertOldMine(record);
                    var loaded = Create(def, record);
                    // Solo play: hired crews from older saves go home; what they carried stays on the site to collect.
                    foreach (var w in record.Workers.ToArray())
                        if (w.Id != SiteSimulator.PlayerId) loaded.RemoveWorker(w.Id, keepOnSite: true);
                    _sims.Add(loaded);
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
            env.PlayerGear = new PlayerGearData { ToolId = PlayerToolId, PackId = PlayerPackId };
            env.Sites = new List<SiteSaveData>();
            foreach (var sim in _sims)
            {
                var d = ToSave(sim.Record);
                if (sim.Config.Kind == SiteKind.Mine) d.Layout = MineLayout;
                env.Sites.Add(d);
            }
        }

        public const int MineLayout = 2;

        /// <summary>
        /// Saves from before the Mine had directions (every section dug a level down; tunnels ran both ways): keep the
        /// layer, its revealed mixes and everything earned. The section in progress becomes the shaft sinking through the
        /// current layer (layers above count as dug), old tunnels close, and their waiting units go to the shaft face so
        /// nothing owned is lost. Every miner returns to the shaft.
        /// </summary>
        public static void ConvertOldMine(SiteRecord s)
        {
            s.SectionsCompleted = s.Status == SiteStatus.AwaitDecision ? s.Depth : s.Depth - 1;
            s.K = 0;
            s.SectionDepths.Clear();
            foreach (var t in s.Tunnels) s.FacePending.AddRange(t.Pending);
            s.Tunnels.Clear();
            foreach (var w in s.Workers) w.Face = 0;
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
            d.SectionDepths = new List<int>(s.SectionDepths);
            d.FacePending.AddRange(s.FacePending);
            if (s.Tunnels.Count > 0)
            {
                d.Tunnels = new List<TunnelSaveData>();
                foreach (var t in s.Tunnels)
                {
                    var td = new TunnelSaveData
                    {
                        Level = t.Level, Side = t.Side.ToString(), Depth = t.Depth, K = t.K, Stretches = t.Stretches, Status = t.Status.ToString(),
                        DurabilityMicro = t.DurabilityMicro, DoneMicro = t.DoneMicro, Emitted = t.Emitted, NextRecipient = t.NextRecipient,
                    };
                    td.Pending.AddRange(t.Pending);
                    d.Tunnels.Add(td);
                }
            }
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
                    PhaseStartMs = w.PhaseStartMs, PhaseEndMs = w.PhaseEndMs, TargetTree = w.TargetTree, Face = w.Face,
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
            if (d.SectionDepths != null) s.SectionDepths.AddRange(d.SectionDepths);
            else
            {
                // Older saves: one section per earlier depth, then the current depth's continuations.
                for (var depth = 1; depth < s.Depth; depth++) s.SectionDepths.Add(depth);
                for (var k = 0; k < s.K; k++) s.SectionDepths.Add(s.Depth);
            }
            while (s.SectionDepths.Count < s.SectionsCompleted) s.SectionDepths.Insert(0, 1);
            while (s.SectionDepths.Count > s.SectionsCompleted) s.SectionDepths.RemoveAt(0);
            s.FacePending.AddRange(d.FacePending);
            if (d.Tunnels != null)
                foreach (var td in d.Tunnels)
                {
                    var t = new TunnelRecord
                    {
                        Level = td.Level, Side = Enum.TryParse(td.Side, out TunnelSide side) ? side : TunnelSide.Left, Depth = Math.Max(1, td.Depth),
                        K = td.K, Stretches = td.Stretches, Status = Enum.TryParse(td.Status, out SiteStatus ts) ? ts : SiteStatus.Working,
                        DurabilityMicro = td.DurabilityMicro, DoneMicro = td.DoneMicro, Emitted = td.Emitted, NextRecipient = td.NextRecipient,
                    };
                    if (td.Pending != null) t.Pending.AddRange(td.Pending);
                    s.Tunnels.Add(t);
                }
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
                    Face = wd.Face >= 0 && wd.Face <= s.Tunnels.Count ? wd.Face : 0,
                };
                w.Pack.AddRange(wd.Carried);
                s.Workers.Add(w);
            }
            return s;
        }
    }
}
