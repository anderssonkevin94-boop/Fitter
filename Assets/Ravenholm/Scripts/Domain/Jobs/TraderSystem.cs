using System;
using System.Collections.Generic;
using Ravenholm.Domain.Rng;
using Ravenholm.Domain.Sites;

namespace Ravenholm.Domain.Jobs
{
    public enum TraderGoods { Resource, Item, Blueprint }

    public sealed class TraderEntryInfo
    {
        public string Key = "";
        public TraderGoods Goods;
        public string GoodsId = "";       // resource or item definition ID (blueprint chosen per visit)
        public int Price;
        public int Quantity;
        public bool Guaranteed;
        public double Chance = 1;
        public string AfterQuest;
    }

    public sealed class TraderConfig
    {
        public readonly List<TraderEntryInfo> Entries = new List<TraderEntryInfo>();
        public readonly List<string> SideBlueprints = new List<string>();
        public long GapMs = 20 * 60 * 1000;
        public long StayMs = 5 * 60 * 1000;
        public long FirstDelayMs = 60 * 1000;
    }

    /// <summary>Saved Trader state. Everything else (visit window, stock) is derived deterministically.</summary>
    public sealed class TraderState
    {
        /// <summary>Clock time of the first arrival; −1 until the first ordinary quest return.</summary>
        public long AnchorMs = -1;
        /// <summary>The visit whose remaining quantities are stored below.</summary>
        public long VisitIndex = -1;
        public readonly SortedDictionary<string, int> Remaining = new SortedDictionary<string, int>(StringComparer.Ordinal);
    }

    public sealed class TraderOffer
    {
        public TraderEntryInfo Entry;
        public string GoodsId = "";
        public int Remaining;
    }

    /// <summary>Goods and gold the Trader exchanges, implemented across logistics, production and the company.</summary>
    public interface ITradeLedger
    {
        int Gold { get; }
        void AddGold(int delta, string reason);
        void ProduceShared(string resourceId, int quantity, string reason);
        string CreateStoreItem(string itemDefinitionId, string reason);
        bool OwnsBlueprint(string blueprintId);
        void GrantBlueprint(string blueprintId);
        bool QuestSucceeded(string questId);
        /// <summary>Removes a Store item that is not reserved or away; false changes nothing.</summary>
        bool TakeUnreservedStoreItem(string instanceId, string reason);
        int SellValue(string instanceId);
    }

    /// <summary>
    /// The periodic Trader (GDD 16). Visits follow a fixed schedule from an anchor time, so the
    /// current window is derived from the clock; missed visits never accumulate. Each visit's stock
    /// comes from a seed of (world, visit index) and cannot be rerolled by reopening or reloading.
    /// </summary>
    public sealed class TraderSystem
    {
        private readonly TraderState _s;
        private readonly TraderConfig _cfg;
        private readonly ITradeLedger _ledger;
        private readonly ulong _worldSeed;

        public TraderSystem(TraderState state, TraderConfig config, ITradeLedger ledger, ulong worldSeed)
        {
            _s = state;
            _cfg = config;
            _ledger = ledger;
            _worldSeed = worldSeed;
        }

        public TraderState State => _s;
        public TraderConfig Config => _cfg;
        public bool Scheduled => _s.AnchorMs >= 0;

        /// <summary>Starts the schedule after the first ordinary quest return (idempotent).</summary>
        public void ScheduleFirstVisit(long completedAtMs)
        {
            if (_s.AnchorMs < 0) _s.AnchorMs = completedAtMs + _cfg.FirstDelayMs;
        }

        public long VisitIndexAt(long nowMs) => _s.AnchorMs < 0 || nowMs < _s.AnchorMs ? -1 : (nowMs - _s.AnchorMs) / _cfg.GapMs;

        public bool IsPresent(long nowMs)
        {
            var k = VisitIndexAt(nowMs);
            return k >= 0 && nowMs - (_s.AnchorMs + k * _cfg.GapMs) < _cfg.StayMs;
        }

        /// <summary>Milliseconds until the next arrival (0 while present, −1 if not scheduled).</summary>
        public long UntilNextArrival(long nowMs)
        {
            if (_s.AnchorMs < 0) return -1;
            if (nowMs < _s.AnchorMs) return _s.AnchorMs - nowMs;
            if (IsPresent(nowMs)) return 0;
            var k = VisitIndexAt(nowMs) + 1;
            return _s.AnchorMs + k * _cfg.GapMs - nowMs;
        }

        public long UntilDeparture(long nowMs)
        {
            if (!IsPresent(nowMs)) return 0;
            var k = VisitIndexAt(nowMs);
            return _s.AnchorMs + k * _cfg.GapMs + _cfg.StayMs - nowMs;
        }

        /// <summary>The current visit's offers (empty when away). Remaining quantities reset per visit only.</summary>
        public List<TraderOffer> Offers(long nowMs)
        {
            var list = new List<TraderOffer>();
            if (!IsPresent(nowMs)) return list;
            var k = VisitIndexAt(nowMs);
            if (_s.VisitIndex != k)
            {
                _s.VisitIndex = k;
                _s.Remaining.Clear();
            }
            var rng = new Pcg32(RandomStreams.SplitMix64(_worldSeed ^ SiteMath.StableHash("trader") ^ (ulong)(k + 1)), 31);
            foreach (var e in _cfg.Entries)
            {
                // Draw every entry's roll in a fixed order, so availability never depends on what else was sold.
                var roll = rng.NextDouble();
                var pickRoll = rng.NextDouble();
                if (!string.IsNullOrEmpty(e.AfterQuest) && !_ledger.QuestSucceeded(e.AfterQuest)) continue;
                if (!e.Guaranteed && roll >= e.Chance) continue;
                var goods = e.GoodsId;
                if (e.Goods == TraderGoods.Blueprint)
                {
                    var unowned = _cfg.SideBlueprints.FindAll(b => !_ledger.OwnsBlueprint(b));
                    if (unowned.Count == 0) continue;
                    goods = unowned[(int)(pickRoll * unowned.Count) % unowned.Count];
                }
                if (!_s.Remaining.TryGetValue(e.Key, out var left)) left = e.Quantity;
                list.Add(new TraderOffer { Entry = e, GoodsId = goods, Remaining = left });
            }
            return list;
        }

        /// <summary>Atomic purchase: visit, stock and gold are validated; gold and goods change together.</summary>
        public bool Buy(string entryKey, long nowMs, out string failure)
        {
            var offer = Offers(nowMs).Find(o => o.Entry.Key == entryKey);
            if (offer == null) { failure = IsPresent(nowMs) ? "Not on offer this visit" : "The Trader is away"; return false; }
            if (offer.Remaining <= 0) { failure = "Sold out this visit"; return false; }
            if (_ledger.Gold < offer.Entry.Price) { failure = $"Not enough gold ({offer.Entry.Price} needed)"; return false; }
            if (offer.Entry.Goods == TraderGoods.Blueprint && _ledger.OwnsBlueprint(offer.GoodsId)) { failure = "Already known"; return false; }

            _ledger.AddGold(-offer.Entry.Price, "bought from the Trader");
            switch (offer.Entry.Goods)
            {
                case TraderGoods.Resource: _ledger.ProduceShared(offer.GoodsId, 1, "bought"); break;
                case TraderGoods.Item: _ledger.CreateStoreItem(offer.GoodsId, "bought"); break;
                default: _ledger.GrantBlueprint(offer.GoodsId); break;
            }
            _s.Remaining[entryKey] = offer.Remaining - 1;
            failure = null;
            return true;
        }

        /// <summary>Atomic sale of one unreserved Store item at the Trader's poor price (GDD 15/16).</summary>
        public bool Sell(string instanceId, long nowMs, out string failure)
        {
            if (!IsPresent(nowMs)) { failure = "The Trader is away"; return false; }
            var value = _ledger.SellValue(instanceId);
            if (!_ledger.TakeUnreservedStoreItem(instanceId, "sold to the Trader")) { failure = "That item is reserved, away or gone"; return false; }
            _ledger.AddGold(value, "sold to the Trader");
            failure = null;
            return true;
        }
    }
}
