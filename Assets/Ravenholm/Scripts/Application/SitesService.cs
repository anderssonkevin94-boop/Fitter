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
            if (sim.DigMode && !sim.AimRoutineAtLastDig()) return false;   // nothing dug yet: no tunnel to carry on
            if (sim.GroveMode && !sim.AimRoutineAtLastChop()) return false;   // nothing cut yet
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

        /// <summary>
        /// The supplier swings at one known block of a Mine face (prospecting): its vein decides what comes out. Returns
        /// units carried away.
        /// </summary>
        public int PlayerMineBlock(string siteId, int face, float seconds, int x, int y, int level)
        {
            var sim = Get(siteId);
            if (sim == null) return 0;
            var ore = OreAt(siteId, x, y, level, out _);
            _mineMs += seconds * 1000.0 * SpeedMultiplier;
            var ms = (long)Math.Floor(_mineMs);
            _mineMs -= ms;
            var n = sim.PlayerMine(face, ms, PlayerDamagePerSecond, ore, plainRock: ore == null);
            _scheduler.MarkDirty();
            return n;
        }

        private double _digMs;

        /// <summary>
        /// The supplier digs a block of the Mine's dig slab for <paramref name="seconds"/>, standing at
        /// <paramref name="from"/> (remembered for routines). Returns true when the block breaks.
        /// </summary>
        public bool PlayerDig(int x, int u, int row, float seconds, DigFace from)
        {
            var sim = Get("site.mine");
            if (sim == null || !sim.DigMode) return false;
            _digMs += seconds * 1000.0 * SpeedMultiplier;
            var ms = (long)Math.Floor(_digMs);
            _digMs -= ms;
            var broke = sim.PlayerDig(x, u, row, ms, PlayerDamagePerSecond, from);
            if (broke) _scheduler.SaveNow("dug a block");
            else _scheduler.MarkDirty();
            return broke;
        }

        /// <summary>The ore in a Mine block (null = plain rock), from the world seed and that level's layer.</summary>
        public string OreAt(string siteId, int x, int y, int level, out double richness)
        {
            richness = 0;
            var sim = Get(siteId);
            if (sim == null || sim.Config.Kind != SiteKind.Mine || sim.Config.Bands.Count == 0) return null;
            if (sim.DigMode) return sim.OreAt(x, y, level, out richness);   // the dig slab: (x, u, row)
            var band = Math.Min(sim.BandOfLevel(level), sim.Config.Bands.Count);
            var template = sim.Config.Bands[band - 1];
            var comp = sim.Record.Revealed.TryGetValue(band, out var known) && known != null
                ? known : SiteMath.Generate(_scheduler.Coordinator.WorldSeed, siteId, template);
            return MineVeins.OreAt(_scheduler.Coordinator.WorldSeed ^ (ulong)band * 0x9E3779B97F4A7C15UL, comp, template.BaselineResourceId, x, y, level, out richness);
        }

        /// <summary>The supplier chops a tree of the current clearing. Returns units carried away.</summary>
        // ------------------------------------------------------------------ surface outcrops

        /// <summary>The supplier breaks a surface outcrop block for a moment (the Mine's rock, above ground). True when it breaks.</summary>
        public bool PlayerBreakSurface(int x, int y, int level, float seconds)
        {
            var sim = Get("site.mine");
            if (sim == null || !sim.DigMode) return false;
            _digMs += seconds * 1000.0 * SpeedMultiplier;
            var ms = (long)Math.Floor(_digMs);
            _digMs -= ms;
            var broke = sim.PlayerBreakSurface(x, y, level, ms, PlayerDamagePerSecond);
            if (broke) _scheduler.SaveNow("broke an outcrop block");
            else _scheduler.MarkDirty();
            return broke;
        }

        // ------------------------------------------------------------------ Mine scaffolds

        /// <summary>The Mine's scaffold materials in the town's stock (what building one could use), and the cost of one.</summary>
        public int ScaffoldMaterialInStock(string siteId)
        {
            var def = Definition(siteId);
            if (def == null) return 0;
            var n = 0;
            foreach (var m in def.ScaffoldMaterials) if (m != null) n += _logistics.System.SharedAvailable(m.Id);
            return n;
        }

        public int ScaffoldCost(string siteId) => Definition(siteId)?.ScaffoldCost ?? 0;

        /// <summary>Builds a scaffold in open space, paying from the town's stock. False (with a reason) when it can't.</summary>
        public bool PlaceScaffold(string siteId, int x, int u, int row, out string reason)
        {
            reason = null;
            var sim = Get(siteId);
            var def = Definition(siteId);
            if (sim == null || def == null || !sim.CanPlaceScaffold(x, u, row)) { reason = "No room for a scaffold there."; return false; }
            string used = null;
            var cost = def.ScaffoldCost;
            if (cost > 0)
            {
                foreach (var m in def.ScaffoldMaterials)
                {
                    if (m == null || _logistics.System.SharedAvailable(m.Id) < cost) continue;
                    if (!_logistics.System.TryConsumeShared(new[] { new KeyValuePair<string, int>(m.Id, cost) }, "built a scaffold")) continue;
                    used = m.Id;
                    break;
                }
                if (used == null) { reason = "You need lumber in town to build a scaffold."; return false; }
            }
            sim.PlaceScaffold(x, u, row, used);
            _scheduler.SaveNow("scaffold built");
            return true;
        }

        /// <summary>Takes a scaffold down, giving its material back to the town's stock.</summary>
        public bool RemoveScaffold(string siteId, int x, int u, int row)
        {
            var sim = Get(siteId);
            var def = Definition(siteId);
            var material = sim?.RemoveScaffold(x, u, row);
            if (material == null) return false;
            if (!string.IsNullOrEmpty(material) && def != null && def.ScaffoldCost > 0)
                _logistics.System.ProduceShared(material, def.ScaffoldCost, "took down a scaffold");
            _scheduler.SaveNow("scaffold removed");
            return true;
        }

        private SiteDefinition Definition(string siteId)
        {
            if (_catalogue == null) return null;
            foreach (var d in _catalogue.Sites) if (d != null && d.Id == siteId) return d;
            return null;
        }

        /// <summary>The supplier pulls out a stump of the Forest's grid (a log; nothing grows back there).</summary>
        public bool ClearStump(string siteId, int area, int slot)
        {
            var sim = Get(siteId);
            if (sim == null || !sim.ClearStump(area, slot)) return false;
            _scheduler.SaveNow("cleared a stump");
            return true;
        }

        /// <summary>The supplier cuts a tree of the Forest's grid for a moment. Returns the logs cut.</summary>
        public int PlayerChopCell(string siteId, int area, int slot, float seconds)
        {
            var sim = Get(siteId);
            if (sim == null || !sim.GroveMode) return 0;
            _chopMs += seconds * 1000.0 * SpeedMultiplier;
            var ms = (long)Math.Floor(_chopMs);
            _chopMs -= ms;
            var felledBefore = sim.FelledVersion;
            var n = sim.PlayerChopCell(area, slot, ms, PlayerDamagePerSecond);
            if (sim.FelledVersion != felledBefore) _scheduler.SaveNow("felled a tree");
            else _scheduler.MarkDirty();
            return n;
        }

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
                // Solo play: the Mine is dug block by block (the dig slab); three levels of two blocks per layer.
                Dig = def.SiteType == SiteType.Mine ? new DigSlab { RowsPerLayer = 2 * Math.Max(1, def.LevelsPerBand) } : null,
                // Solo play: the Forest is a grid of trees, any of them cut where the supplier reaches (2026-10-12).
                Grove = def.SiteType == SiteType.Forest
                    ? new ForestGrove { TreesPerContinuation = Math.Max(1, def.TreesPerClearing), RegrowMs = (long)Math.Round(def.TreeRegrowSeconds * 1000) }
                    : null,
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
                    if (def.SiteType == SiteType.Mine && saved.Layout < 2) ConvertOldMine(record);
                    if (def.SiteType == SiteType.Mine && saved.Layout < 4) ConvertToDigSlab(record);
                    if (def.SiteType == SiteType.Forest && saved.Layout < ForestLayout) SiteSimulator.ConvertToGrove(record, def.TreesPerClearing);
                    var loaded = Create(def, record);
                    loaded.EnsureElevatorShaft();   // older saves get the elevator shaft too
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
                sim.EnsureElevatorShaft();
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
                if (sim.Config.Kind == SiteKind.Forest) d.Layout = ForestLayout;
                env.Sites.Add(d);
            }
        }

        public const int MineLayout = 4;

        /// <summary>Forest layout: 2 = the grid of trees (absent = clearings).</summary>
        public const int ForestLayout = 2;

        /// <summary>
        /// Saves from before the Mine had directions (every section dug a level down; tunnels ran both ways): keep the
        /// layer, its revealed mixes and everything earned. The section in progress becomes the shaft sinking through the
        /// current layer (layers above count as dug), old tunnels close, and their waiting units go to the shaft face so
        /// nothing owned is lost. Every miner returns to the shaft.
        /// </summary>
        /// <summary>
        /// Saves from before the dig slab: the shaft-and-tunnel Mine gives way to untouched ground. Everything earned
        /// (packs, units waiting at faces) is kept, and the layers already revealed stay revealed.
        /// </summary>
        public static void ConvertToDigSlab(SiteRecord s)
        {
            // From the 8-deep slab (layout 3) to the cliff-face slice: the ground is untouched again.
            s.Dug.Clear();
            s.DugDown.Clear();
            s.DigTarget = null;
            s.DigDoneMicro = 0;
            s.LastDig = null;
            s.RoutineFace = null;
            foreach (var t in s.Tunnels) s.FacePending.AddRange(t.Pending);
            s.Tunnels.Clear();
            foreach (var w in s.Workers) w.Face = 0;
            s.Status = SiteStatus.Working;
            s.K = 0;
        }

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
            if (s.Dug.Count > 0)
            {
                d.Dug = new List<int>(s.Dug.Count * 3);
                foreach (var c in s.Dug) { d.Dug.Add(c.X); d.Dug.Add(c.U); d.Dug.Add(c.Row); }
            }
            if (s.DugDown.Count > 0)
            {
                d.DugDown = new List<int>(s.DugDown.Count * 3);
                foreach (var c in s.DugDown) { d.DugDown.Add(c.X); d.DugDown.Add(c.U); d.DugDown.Add(c.Row); }
            }
            if (s.DigTarget.HasValue) d.DigTarget = new List<int> { s.DigTarget.Value.X, s.DigTarget.Value.U, s.DigTarget.Value.Row };
            d.DigDoneMicro = s.DigDoneMicro;
            d.LastDig = Face(s.LastDig);
            d.RoutineFace = Face(s.RoutineFace);
            if (s.Chopped.Count > 0)
            {
                d.Chopped = new List<long>(s.Chopped.Count * 3);
                foreach (var kv in s.Chopped) { d.Chopped.Add(kv.Key.Area); d.Chopped.Add(kv.Key.Slot); d.Chopped.Add(kv.Value); }
            }
            if (s.LastChop.HasValue) d.LastChop = new List<int> { s.LastChop.Value.Area, s.LastChop.Value.Slot };
            if (s.FelledAtMs.Count > 0)
            {
                d.Stumps = new List<long>(s.FelledAtMs.Count * 3);
                foreach (var kv in s.FelledAtMs) { d.Stumps.Add(kv.Key.Area); d.Stumps.Add(kv.Key.Slot); d.Stumps.Add(kv.Value); }
            }
            if (s.Cleared.Count > 0)
            {
                d.Cleared = new List<int>(s.Cleared.Count * 2);
                foreach (var c in s.Cleared) { d.Cleared.Add(c.Area); d.Cleared.Add(c.Slot); }
            }
            if (s.SurfaceBroken.Count > 0)
            {
                d.SurfaceBroken = new List<int>(s.SurfaceBroken.Count * 3);
                foreach (var c in s.SurfaceBroken) { d.SurfaceBroken.Add(c.X); d.SurfaceBroken.Add(c.U); d.SurfaceBroken.Add(c.Row); }
            }
            if (s.Scaffolds.Count > 0)
            {
                d.Scaffolds = new List<int>(s.Scaffolds.Count * 3);
                d.ScaffoldMaterials = new List<string>(s.Scaffolds.Count);
                foreach (var kv in s.Scaffolds) { d.Scaffolds.Add(kv.Key.X); d.Scaffolds.Add(kv.Key.U); d.Scaffolds.Add(kv.Key.Row); d.ScaffoldMaterials.Add(kv.Value); }
            }
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

        private static List<long> Face(DigFace f) => f == null ? null : new List<long> { f.X, f.U, f.Feet, f.DX, f.DU, f.DoneMicro };

        private static DigFace Face(List<long> d) => d == null || d.Count < 6 ? null
            : new DigFace { X = (int)d[0], U = (int)d[1], Feet = (int)d[2], DX = (int)d[3], DU = (int)d[4], DoneMicro = d[5] };

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
            if (d.Dug != null)
                for (var i = 0; i + 2 < d.Dug.Count; i += 3) s.Dug.Add(new DigCell(d.Dug[i], d.Dug[i + 1], d.Dug[i + 2]));
            if (d.DugDown != null)
                for (var i = 0; i + 2 < d.DugDown.Count; i += 3) s.DugDown.Add(new DigCell(d.DugDown[i], d.DugDown[i + 1], d.DugDown[i + 2]));
            if (d.DigTarget != null && d.DigTarget.Count == 3) s.DigTarget = new DigCell(d.DigTarget[0], d.DigTarget[1], d.DigTarget[2]);
            s.DigDoneMicro = d.DigDoneMicro;
            s.LastDig = Face(d.LastDig);
            s.RoutineFace = Face(d.RoutineFace);
            if (d.Chopped != null)
                for (var i = 0; i + 2 < d.Chopped.Count; i += 3) s.Chopped[new GroveCell((int)d.Chopped[i], (int)d.Chopped[i + 1])] = d.Chopped[i + 2];
            if (d.LastChop != null && d.LastChop.Count == 2) s.LastChop = new GroveCell(d.LastChop[0], d.LastChop[1]);
            if (d.Stumps != null)
                for (var i = 0; i + 2 < d.Stumps.Count; i += 3) s.FelledAtMs[new GroveCell((int)d.Stumps[i], (int)d.Stumps[i + 1])] = d.Stumps[i + 2];
            if (d.Cleared != null)
                for (var i = 0; i + 1 < d.Cleared.Count; i += 2) s.Cleared.Add(new GroveCell(d.Cleared[i], d.Cleared[i + 1]));
            if (d.SurfaceBroken != null)
                for (var i = 0; i + 2 < d.SurfaceBroken.Count; i += 3) s.SurfaceBroken.Add(new DigCell(d.SurfaceBroken[i], d.SurfaceBroken[i + 1], d.SurfaceBroken[i + 2]));
            if (d.Scaffolds != null)
                for (var i = 0; i + 2 < d.Scaffolds.Count; i += 3)
                    s.Scaffolds[new DigCell(d.Scaffolds[i], d.Scaffolds[i + 1], d.Scaffolds[i + 2])] =
                        d.ScaffoldMaterials != null && i / 3 < d.ScaffoldMaterials.Count ? d.ScaffoldMaterials[i / 3] : "";
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
