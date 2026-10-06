using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Sites;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>Trader exchanges routed to the systems that own gold, materials, items and blueprints.</summary>
    public sealed class TradeLedger : ITradeLedger
    {
        private readonly JobsService _jobs;
        private readonly LogisticsService _logistics;
        private readonly ProductionService _production;
        private readonly ContentCatalogue _catalogue;

        public TradeLedger(JobsService jobs, LogisticsService logistics, ProductionService production, ContentCatalogue catalogue)
        {
            _jobs = jobs;
            _logistics = logistics;
            _production = production;
            _catalogue = catalogue;
        }

        public int Gold => _jobs.State.Gold;
        public void AddGold(int delta, string reason) => _jobs.AddGold(delta);
        public void ProduceShared(string id, int q, string reason) => _logistics.System.ProduceShared(id, q, reason);
        public string CreateStoreItem(string def, string reason) => _logistics.System.CreateStoreItem(def, reason);
        public bool OwnsBlueprint(string id) => _production.System.State.Blueprints.Contains(id);
        public void GrantBlueprint(string id) => _production.System.GrantBlueprint(id);
        public bool QuestSucceeded(string id) => _jobs.State.Progress.TryGetValue(id, out var p) && p.Successes > 0;

        public bool TakeUnreservedStoreItem(string instanceId, string reason)
        {
            var log = _logistics.System;
            if (log.IsReserved(instanceId)) return false;
            if (!log.TakeFromStore(new[] { instanceId }, "trader", reason)) return false;
            log.DestroyItems(new[] { instanceId }, reason);
            return true;
        }

        public int SellValue(string instanceId)
        {
            var def = _logistics.System.DefinitionOfItem(instanceId);
            return def != null && _catalogue.TryGet<ItemDefinition>(def, out var item) ? item.TraderBuyValue : 0;
        }
    }

    /// <summary>
    /// Gold sinks and the periodic Trader (GDD 15/16): Trader visits and stock, building and site
    /// upgrades, worker tool and pack purchases, crew expansion. Persists the "economy" section.
    /// Every purchase validates everything first, then debits and applies together.
    /// </summary>
    public sealed class EconomyService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly ContentCatalogue _catalogue;
        private readonly JobsService _jobs;
        private readonly LogisticsService _logistics;
        private readonly ProductionService _production;
        private readonly SitesService _sites;
        private readonly TradeLedger _ledger;
        private readonly TraderState _traderState = new TraderState();
        private readonly TraderConfig _traderConfig = new TraderConfig();

        public EconomyService(SaveScheduler scheduler, ContentCatalogue catalogue, JobsService jobs, LogisticsService logistics,
            ProductionService production, SitesService sites)
        {
            _scheduler = scheduler;
            _catalogue = catalogue;
            _jobs = jobs;
            _logistics = logistics;
            _production = production;
            _sites = sites;
            _ledger = new TradeLedger(jobs, logistics, production, catalogue);
            BuildTraderConfig();
            // The Trader's first visit follows the first ordinary quest return (GDD 16).
            jobs.Completed += r => { if (!r.Sponsored) Trader?.ScheduleFirstVisit(r.CompletedMs); };
            scheduler.Register(this);
        }

        public TraderSystem Trader { get; private set; }
        public readonly SortedSet<string> Purchased = new SortedSet<string>(StringComparer.Ordinal);
        public long Now => _jobs.State.SimMs;
        public string LastCatchUpId { get; set; } = "";

        public string DescribeTrader()
        {
            if (Trader == null || !Trader.Scheduled) return "The Trader has not found Ravenholm yet";
            if (Trader.IsPresent(Now)) return $"The Trader is in town for {Minutes(Trader.UntilDeparture(Now))} more";
            return $"The Trader returns in {Minutes(Trader.UntilNextArrival(Now))}";
        }

        public static string Minutes(long ms)
        {
            var s = (long)Math.Ceiling(ms / 1000.0);
            return s >= 60 ? $"{s / 60}m {s % 60:D2}s" : $"{s}s";
        }

        // ------------------------------------------------------------------ Trader

        private void BuildTraderConfig()
        {
            var t = _catalogue?.TraderStock;
            if (t == null) return;
            _traderConfig.GapMs = (long)(t.MinutesBetweenVisits * 60000);
            _traderConfig.StayMs = (long)(t.MinutesPerVisit * 60000);
            _traderConfig.FirstDelayMs = (long)(t.FirstArrivalDelaySeconds * 1000);
            foreach (var e in t.Entries)
            {
                _traderConfig.Entries.Add(new TraderEntryInfo
                {
                    Key = e.key,
                    Goods = e.sideBlueprint ? TraderGoods.Blueprint : e.item != null ? TraderGoods.Item : TraderGoods.Resource,
                    GoodsId = e.resource != null ? e.resource.Id : e.item != null ? e.item.Id : "",
                    Price = e.price,
                    Quantity = e.quantity,
                    Guaranteed = e.availability == StockAvailability.Guaranteed || e.availability == StockAvailability.AfterQuest,
                    Chance = e.availability == StockAvailability.Chance ? e.chance : 1,
                    AfterQuest = e.availability == StockAvailability.AfterQuest && e.afterQuest != null ? e.afterQuest.Id : null,
                });
            }
            foreach (var b in t.SideBlueprints) if (b != null) _traderConfig.SideBlueprints.Add(b.Id);
        }

        public event Action TraderUsed;

        public bool Buy(string entryKey, out string failure)
        {
            var ok = Trader.Buy(entryKey, Now, out failure);
            if (ok) TraderUsed?.Invoke();
            if (ok) _scheduler.SaveNow("bought from the Trader");
            return ok;
        }

        public bool Sell(string instanceId, out string failure)
        {
            var ok = Trader.Sell(instanceId, Now, out failure);
            if (ok) TraderUsed?.Invoke();
            if (ok) _scheduler.SaveNow("sold to the Trader");
            return ok;
        }

        /// <summary>DEBUG: brings the Trader to town now (anchors the schedule so a visit starts this moment).</summary>
        public void DebugSummonTrader()
        {
            var gap = _traderConfig.GapMs;
            var k = Now / gap;
            _traderState.AnchorMs = Now - k * gap;
            _scheduler.SaveNow("debug: trader");
        }

        // ------------------------------------------------------------------ upgrades

        public int SiteEquipmentLevel(string siteId)
        {
            var level = 1;
            foreach (var id in Purchased)
                if (_catalogue.TryGet<UpgradeDefinition>(id, out var u) && u.Kind == UpgradeKind.SiteEquipment && u.Site != null && u.Site.Id == siteId)
                    level = Math.Max(level, u.Level);
            return level;
        }

        /// <summary>Why an upgrade cannot be bought now (null = it can).</summary>
        public string UpgradeBlockedReason(UpgradeDefinition u) => UpgradePrerequisiteReason(u) ?? CostBlockedReason(u.GoldCost, u.MaterialCost);

        /// <summary>Unmet prerequisite (not cost), or null.</summary>
        public string UpgradePrerequisiteReason(UpgradeDefinition u)
        {
            if (Purchased.Contains(u.Id)) return "Already built";
            if (u.Kind == UpgradeKind.StationLevel && _production.System.Level(u.Station.Id) >= u.Level) return "Already at this level";
            if (u.RequiresUpgrade != null && !Purchased.Contains(u.RequiresUpgrade.Id)) return $"Needs {u.RequiresUpgrade.DisplayName} first";
            if (u.RequiresSiteBand != null)
            {
                var sim = _sites.Get(u.RequiresSiteBand.Id);
                var deepest = 0;
                if (sim != null) foreach (var k in sim.Record.Revealed.Keys) deepest = Math.Max(deepest, k);
                if (deepest < u.RequiresBand)
                    return u.RequiresSiteBand.SiteType == SiteType.Mine ? $"Reveal Mine depth {u.RequiresBand} first" : $"Reach Forest area {u.RequiresBand} first";
            }
            if (u.RequiresQuestSuccess != null && !_ledger.QuestSucceeded(u.RequiresQuestSuccess.Id)) return $"Succeed at {u.RequiresQuestSuccess.DisplayName} first";
            if (u.RequiresOrdinarySuccesses > 0 && _jobs.OrdinarySuccesses() < u.RequiresOrdinarySuccesses)
                return $"Needs {u.RequiresOrdinarySuccesses} quest successes ({_jobs.OrdinarySuccesses()} so far)";
            if (u.Kind == UpgradeKind.CrewExpansion && NextRecruit(u.Site) == null) return "No more recruits for this site";
            return null;
        }

        private string CostBlockedReason(int gold, IReadOnlyList<ResourceCost> materials)
        {
            if (_jobs.State.Gold < gold) return $"Needs {gold} gold (you have {_jobs.State.Gold})";
            foreach (var c in materials)
            {
                var have = _logistics.System.SharedAvailable(c.resource.Id);
                if (have < c.quantity) return $"Needs {c.quantity} {c.resource.DisplayName} (you have {have})";
            }
            return null;
        }

        private static List<KeyValuePair<string, int>> Pairs(IReadOnlyList<ResourceCost> list)
        {
            var r = new List<KeyValuePair<string, int>>();
            foreach (var c in list) r.Add(new KeyValuePair<string, int>(c.resource.Id, c.quantity));
            return r;
        }

        private WorkerDefinition NextRecruit(SiteDefinition site)
        {
            var sim = _sites.Get(site.Id);
            foreach (var w in site.Recruits)
                if (w != null && sim != null && !sim.Record.Workers.Exists(x => x.Id == w.Id)) return w;
            return null;
        }

        /// <summary>Buys an upgrade: validate everything, then debit gold and materials and apply (GDD 16).</summary>
        public bool BuyUpgrade(UpgradeDefinition u, out string failure)
        {
            failure = UpgradeBlockedReason(u);
            if (failure != null) return false;
            if (!_logistics.System.TryConsumeShared(Pairs(u.MaterialCost), "upgrade")) { failure = "Materials changed; try again"; return false; }
            _jobs.AddGold(-u.GoldCost);
            switch (u.Kind)
            {
                case UpgradeKind.StationLevel: _production.System.SetLevel(u.Station.Id, u.Level); break;
                case UpgradeKind.CrewExpansion: _sites.AddWorker(u.Site.Id, NextRecruit(u.Site)); break;
            }
            Purchased.Add(u.Id);
            _scheduler.SaveNow("upgrade bought");
            return true;
        }

        /// <summary>Why a worker cannot be given this tool or pack (null = can).</summary>
        public string EquipmentBlockedReason(string siteId, string workerId, WorkerEquipmentDefinition e)
        {
            var w = _sites.Get(siteId)?.Record.Workers.Find(x => x.Id == workerId);
            if (w == null) return "No such worker";
            var current = e.Slot == EquipmentSlot.Tool ? w.ToolId : w.PackId;
            var currentTier = _catalogue.TryGet<WorkerEquipmentDefinition>(current, out var cur) ? cur.Tier : 1;
            if (e.Tier <= currentTier) return "Already equipped or better";
            if (SiteEquipmentLevel(siteId) < e.Tier) return $"Needs site equipment level {e.Tier}";
            return CostBlockedReason(e.GoldCost, e.MaterialCost);
        }

        /// <summary>Worker equipment is a facility purchase applied straight to the worker (GDD 5), not crafted goods.</summary>
        public bool BuyEquipment(string siteId, string workerId, WorkerEquipmentDefinition e, out string failure)
        {
            failure = EquipmentBlockedReason(siteId, workerId, e);
            if (failure != null) return false;
            if (!_logistics.System.TryConsumeShared(Pairs(e.MaterialCost), "worker equipment")) { failure = "Materials changed; try again"; return false; }
            _jobs.AddGold(-e.GoldCost);
            var sim = _sites.Get(siteId);
            if (e.Slot == EquipmentSlot.Tool) sim.Equip(workerId, e.Id, null);
            else sim.Equip(workerId, null, e.Id);
            _scheduler.SaveNow("worker equipment bought");
            return true;
        }

        // ------------------------------------------------------------------ persistence

        void ISaveSection.Read(SaveEnvelope env)
        {
            _traderState.AnchorMs = -1;
            _traderState.VisitIndex = -1;
            _traderState.Remaining.Clear();
            Purchased.Clear();
            LastCatchUpId = "";
            var d = env?.Economy;
            if (d != null)
            {
                _traderState.AnchorMs = d.TraderAnchorMs;
                _traderState.VisitIndex = d.TraderVisitIndex;
                foreach (var kv in d.TraderRemaining) _traderState.Remaining[kv.Key] = kv.Value;
                foreach (var u in d.Upgrades) Purchased.Add(u);
                LastCatchUpId = d.LastCatchUpId ?? "";
            }
            else
            {
                // Saves from before Phase 6: if an ordinary quest has already returned, the Trader is due.
                ResultRecord first = null;
                foreach (var r in _jobs.State.Results) if (!r.Sponsored && (first == null || r.CompletedMs < first.CompletedMs)) first = r;
                if (first != null) _traderState.AnchorMs = first.CompletedMs + _traderConfig.FirstDelayMs;
            }
            Trader = new TraderSystem(_traderState, _traderConfig, _ledger, _scheduler.Coordinator.WorldSeed);
        }

        void ISaveSection.Write(SaveEnvelope env)
        {
            var d = new EconomySaveData { TraderAnchorMs = _traderState.AnchorMs, TraderVisitIndex = _traderState.VisitIndex };
            foreach (var kv in _traderState.Remaining) d.TraderRemaining[kv.Key] = kv.Value;
            d.Upgrades.AddRange(Purchased);
            d.LastCatchUpId = LastCatchUpId;
            env.Economy = d;
        }
    }
}
