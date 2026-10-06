using System;
using System.Collections.Generic;
using Ravenholm.Domain.Production;
using Ravenholm.Domain.Sites;

namespace Ravenholm.Domain.Logistics
{
    public enum TransferResult
    {
        Nothing,
        PickedUp,
        Delivered,
        CarryFull,
    }

    /// <summary>
    /// Owns physical transfers: automatic pickup from world containers, automatic delivery
    /// into compatible receivers, and debug fixtures. Each transfer moves exactly one unit and
    /// either fully commits or changes nothing. Events fire only after the state change.
    /// Presentation feeds the player position; it never edits containers directly.
    /// </summary>
    public sealed class LogisticsSystem : ISiteLedger, IProductionLedger
    {
        private readonly LogisticsState _s;
        private readonly IAcceptanceRules _rules;
        private readonly LogisticsTuning _tuning;
        private readonly List<Func<IEnumerable<string>>> _externalHolders = new List<Func<IEnumerable<string>>>();
        private readonly LinkedList<TransactionRecord> _history = new LinkedList<TransactionRecord>();
        public const int HistoryLimit = 40;
        private float _accumulator;
        private bool _fullNotified;

        public LogisticsSystem(LogisticsState state, IAcceptanceRules rules, LogisticsTuning tuning)
        {
            _s = state ?? throw new ArgumentNullException(nameof(state));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _tuning = tuning ?? new LogisticsTuning();
        }

        public LogisticsState State => _s;
        public LogisticsTuning Tuning => _tuning;
        public int CarryCount => _s.Carry.Count;
        public int Capacity => _tuning.Capacity;
        public bool IsFull => _s.Carry.Count >= _tuning.Capacity;

        /// <summary>(containerId, unit) after the unit left the container and joined the carry.</summary>
        public event Action<string, CarryUnit> PickedUp;
        /// <summary>(receiverId, unit) after the unit left the carry and joined the receiver's ledger.</summary>
        public event Action<string, CarryUnit> Delivered;
        /// <summary>(containerId) once per approach when an eligible pickup is blocked by a full carry.</summary>
        public event Action<string> CapacityBlocked;
        /// <summary>Any ownership change (including debug fixtures).</summary>
        public event Action Changed;

        // ------------------------------------------------------------------ registration

        /// <summary>Registers (or moves) a container. Returns true when it is new to this save.</summary>
        public bool RegisterContainer(string id, ContainerKind kind, float x, float y, float radius)
        {
            if (_s.Containers.TryGetValue(id, out var c))
            {
                c.X = x; c.Y = y; c.Radius = radius;
                return false;
            }
            _s.Containers.Add(id, new WorldContainer(id, kind, x, y, radius));
            return true;
        }

        public void RegisterReceiver(string id, ReceiverKind kind, float x, float y, float radius)
        {
            if (_s.Receivers.TryGetValue(id, out var r))
            {
                r.X = x; r.Y = y; r.Radius = radius;
                return;
            }
            _s.Receivers.Add(id, new ReceiverZone(id, kind, x, y, radius));
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>
        /// Advances transfer cadence. At most one unit moves per <see cref="LogisticsTuning.TransferInterval"/>.
        /// Delivery (when standing in a receiver) is tried before pickup.
        /// </summary>
        public void Tick(float dt, float px, float py)
        {
            if (!HasEligiblePickup(px, py, out _)) _fullNotified = false;   // left range: allow one new warning later

            _accumulator += dt;
            while (_accumulator >= _tuning.TransferInterval)
            {
                var r = StepOnce(px, py);
                if (r == TransferResult.Nothing || r == TransferResult.CarryFull)
                {
                    // Nothing to do: keep at most one interval banked so the next transfer is prompt, not a burst.
                    _accumulator = Math.Min(_accumulator, _tuning.TransferInterval);
                    break;
                }
                _accumulator -= _tuning.TransferInterval;
            }
        }

        /// <summary>One transfer attempt at a position (no cadence). Exposed for tests and tools.</summary>
        public TransferResult StepOnce(float px, float py)
        {
            var zone = ReceiverAt(px, py);
            if (zone != null && TryDeliver(zone)) return TransferResult.Delivered;
            return TryPickup(px, py);
        }

        public ReceiverZone ReceiverAt(float px, float py)
        {
            ReceiverZone best = null;
            var bestD = float.MaxValue;
            foreach (var r in _s.Receivers.Values)
            {
                if (!r.Contains(px, py)) continue;
                var d = Dist2(px, py, r.X, r.Y);
                if (d < bestD || d == bestD && string.CompareOrdinal(r.Id, best.Id) < 0) { best = r; bestD = d; }
            }
            return best;
        }

        public bool Accepts(ReceiverKind receiver, CarryUnit unit) => _rules.Accepts(receiver, unit, DefinitionOf(unit));

        public string DefinitionOf(CarryUnit unit) =>
            unit.Kind == UnitKind.Item && _s.ItemDefinitions.TryGetValue(unit.Id, out var def) ? def : null;

        private bool TryDeliver(ReceiverZone zone)
        {
            // Take the most recently stacked accepted unit, leaving everything else carried.
            for (var i = _s.Carry.Count - 1; i >= 0; i--)
            {
                var unit = _s.Carry[i];
                if (!Accepts(zone.Kind, unit)) continue;

                _s.Carry.RemoveAt(i);
                if (unit.Kind == UnitKind.Material) Add(_s.SharedMaterials, unit.Id, 1);
                else InsertSorted(_s.StoreItems, unit.Id);
                Log("delivered", $"{unit.Id} → {zone.Id}");

                _fullNotified = false;
                Delivered?.Invoke(zone.Id, unit);
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        private TransferResult TryPickup(float px, float py)
        {
            if (!HasEligiblePickup(px, py, out var container)) return TransferResult.Nothing;

            if (IsFull)
            {
                if (!_fullNotified)
                {
                    _fullNotified = true;
                    CapacityBlocked?.Invoke(container.Id);
                }
                return TransferResult.CarryFull;
            }

            CarryUnit unit;
            if (container.Items.Count > 0)
            {
                unit = CarryUnit.Item(container.Items[0]);
                container.Items.RemoveAt(0);
            }
            else
            {
                string resource = null;
                foreach (var kv in container.Materials) { if (kv.Value > 0) { resource = kv.Key; break; } }
                if (resource == null) return TransferResult.Nothing;   // defensive: HasEligiblePickup checked counts
                Add(container.Materials, resource, -1);
                unit = CarryUnit.Material(resource);
            }

            _s.Carry.Add(unit);
            PickedUp?.Invoke(container.Id, unit);
            Changed?.Invoke();
            return TransferResult.PickedUp;
        }

        /// <summary>Nearest non-empty container in reach; output pads win near-ties; then stable ID order.</summary>
        public bool HasEligiblePickup(float px, float py, out WorldContainer best)
        {
            best = null;
            var bestScore = float.MaxValue;
            foreach (var c in _s.Containers.Values)
            {
                if (c.UnitCount == 0) continue;
                var d = (float)Math.Sqrt(Dist2(px, py, c.X, c.Y));
                if (d > c.Radius + _tuning.PickupRadius) continue;
                var score = d - (c.Kind == ContainerKind.OutputPad ? _tuning.OutputPadPriority : 0f);
                if (score < bestScore || Math.Abs(score - bestScore) < 1e-6f && string.CompareOrdinal(c.Id, best.Id) < 0)
                {
                    best = c;
                    bestScore = score;
                }
            }
            return best != null;
        }

        // ------------------------------------------------------------------ fixtures and debug

        /// <summary>Creates a new item instance owned by a container. Approved source for test fixtures.</summary>
        public string CreateItemInContainer(string containerId, string itemDefinitionId)
        {
            var c = _s.Containers[containerId];
            var id = $"inst.{_s.NextInstanceNumber++:D6}";
            _s.ItemDefinitions.Add(id, itemDefinitionId);
            InsertSorted(c.Items, id);
            Changed?.Invoke();
            return id;
        }

        /// <summary>Adds newly created material units to a container. Approved source for test fixtures.</summary>
        public void CreateMaterialsInContainer(string containerId, string resourceId, int quantity)
        {
            if (quantity <= 0) return;
            var c = _s.Containers[containerId];
            Add(c.Materials, resourceId, quantity);
            AddLong(_s.MaterialsCreated, resourceId, quantity);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ gathering sites (ISiteLedger)

        /// <summary>Material owners outside this state (worker packs, earned yield on a face), included in the audit.</summary>
        public void RegisterExternalHolder(Func<IEnumerable<string>> holder) => _externalHolders.Add(holder);

        /// <summary>Approved source: a unit extracted by a worker. Ownership starts in the site's pack or pending yield.</summary>
        public void RecordCreated(string resourceId) => AddLong(_s.MaterialsCreated, resourceId, 1);

        public int StagingFree(string containerId, int capacity) =>
            _s.Containers.TryGetValue(containerId, out var c) ? Math.Max(0, capacity - c.UnitCount) : 0;

        public void Deposit(string containerId, string resourceId)
        {
            Add(_s.Containers[containerId].Materials, resourceId, 1);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ shared ledger (IProductionLedger)

        /// <summary>Most recent first; bounded and not saved (diagnosis only).</summary>
        public IEnumerable<TransactionRecord> History => _history;

        private void Log(string reason, string detail)
        {
            _history.AddFirst(new TransactionRecord(reason, detail));
            while (_history.Count > HistoryLimit) _history.RemoveLast();
        }

        public int SharedAvailable(string resourceId) => _s.SharedMaterials.TryGetValue(resourceId, out var q) ? q : 0;

        public bool TryConsumeShared(IReadOnlyList<KeyValuePair<string, int>> costs, string reason)
        {
            foreach (var c in costs) if (SharedAvailable(c.Key) < c.Value) return false;
            foreach (var c in costs)
            {
                Add(_s.SharedMaterials, c.Key, -c.Value);
                AddLong(_s.MaterialsConsumed, c.Key, c.Value);
            }
            Log(reason, Describe(costs, "-"));
            Changed?.Invoke();
            return true;
        }

        public void ProduceShared(string resourceId, int quantity, string reason)
        {
            if (quantity <= 0) return;
            Add(_s.SharedMaterials, resourceId, quantity);
            AddLong(_s.MaterialsCreated, resourceId, quantity);
            Log(reason, $"+{quantity} {resourceId}");
            Changed?.Invoke();
        }

        public int ContainerUnitCount(string containerId) => _s.Containers.TryGetValue(containerId, out var c) ? c.UnitCount : 0;

        public bool TryCraftToPad(IReadOnlyList<KeyValuePair<string, int>> costs, string itemDefinitionId, string padId, int padCapacity, string reason,
            out string instanceId, out string failure)
        {
            instanceId = null;
            if (!_s.Containers.TryGetValue(padId, out var pad)) { failure = "Output pad is not in this scene"; return false; }
            if (pad.UnitCount >= padCapacity) { failure = "Output pad full"; return false; }
            foreach (var c in costs)
                if (SharedAvailable(c.Key) < c.Value) { failure = $"Missing {c.Value - SharedAvailable(c.Key)}× {c.Key}"; return false; }

            foreach (var c in costs)
            {
                Add(_s.SharedMaterials, c.Key, -c.Value);
                AddLong(_s.MaterialsConsumed, c.Key, c.Value);
            }
            instanceId = $"inst.{_s.NextInstanceNumber++:D6}";
            _s.ItemDefinitions.Add(instanceId, itemDefinitionId);
            InsertSorted(pad.Items, instanceId);
            Log(reason, $"{itemDefinitionId} ({instanceId}) {Describe(costs, "-")}");
            failure = null;
            Changed?.Invoke();
            return true;
        }

        private static string Describe(IReadOnlyList<KeyValuePair<string, int>> costs, string sign)
        {
            var parts = new List<string>();
            foreach (var c in costs) parts.Add($"{sign}{c.Value} {c.Key}");
            return string.Join(", ", parts);
        }

        /// <summary>DEBUG: empties the carry into Store/shared inventory regardless of receivers ("Store return").</summary>
        public int DebugUnloadCarry()
        {
            var n = _s.Carry.Count;
            foreach (var unit in _s.Carry)
            {
                if (unit.Kind == UnitKind.Material) Add(_s.SharedMaterials, unit.Id, 1);
                else InsertSorted(_s.StoreItems, unit.Id);
            }
            _s.Carry.Clear();
            _fullNotified = false;
            if (n > 0) Changed?.Invoke();
            return n;
        }

        /// <summary>Checks the ownership invariant. Returns human-readable violations (empty = OK).</summary>
        public List<string> Audit()
        {
            var problems = new List<string>();
            if (_s.Carry.Count > _tuning.Capacity) problems.Add($"carry {_s.Carry.Count} exceeds capacity {_tuning.Capacity}");

            // Materials: created == containers + carry + shared ledger, per resource.
            var held = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var c in _s.Containers.Values)
                foreach (var kv in c.Materials)
                {
                    if (kv.Value < 0) problems.Add($"negative {kv.Key} in {c.Id}");
                    AddLong(held, kv.Key, kv.Value);
                }
            foreach (var u in _s.Carry) if (u.Kind == UnitKind.Material) AddLong(held, u.Id, 1);
            foreach (var kv in _s.SharedMaterials)
            {
                if (kv.Value < 0) problems.Add($"negative shared {kv.Key}");
                AddLong(held, kv.Key, kv.Value);
            }
            foreach (var holder in _externalHolders)
                foreach (var unit in holder()) AddLong(held, unit, 1);
            foreach (var kv in _s.MaterialsConsumed) AddLong(held, kv.Key, kv.Value);   // consumed units are accounted for, not missing
            var keys = new SortedSet<string>(held.Keys, StringComparer.Ordinal);
            keys.UnionWith(_s.MaterialsCreated.Keys);
            foreach (var k in keys)
            {
                held.TryGetValue(k, out var h);
                _s.MaterialsCreated.TryGetValue(k, out var made);
                if (h != made) problems.Add($"{k}: {h} held or consumed but {made} created");
            }

            // Items: every instance has exactly one owner.
            var owners = new Dictionary<string, int>(StringComparer.Ordinal);
            void Own(string id) => owners[id] = owners.TryGetValue(id, out var n) ? n + 1 : 1;
            foreach (var c in _s.Containers.Values) foreach (var i in c.Items) Own(i);
            foreach (var u in _s.Carry) if (u.Kind == UnitKind.Item) Own(u.Id);
            foreach (var i in _s.StoreItems) Own(i);
            foreach (var id in _s.ItemDefinitions.Keys)
            {
                owners.TryGetValue(id, out var n);
                if (n != 1) problems.Add($"item {id} has {n} owners");
            }
            foreach (var id in owners.Keys)
                if (!_s.ItemDefinitions.ContainsKey(id)) problems.Add($"unknown item instance {id}");
            return problems;
        }

        // ------------------------------------------------------------------ helpers

        private static float Dist2(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy;
        }

        private static void Add(SortedDictionary<string, int> d, string key, int delta)
        {
            d.TryGetValue(key, out var v);
            v += delta;
            if (v == 0) d.Remove(key); else d[key] = v;
        }

        private static void AddLong(SortedDictionary<string, long> d, string key, long delta)
        {
            d.TryGetValue(key, out var v);
            d[key] = v + delta;
        }

        private static void InsertSorted(List<string> list, string id)
        {
            var i = list.BinarySearch(id, StringComparer.Ordinal);
            list.Insert(i < 0 ? ~i : i, id);
        }
    }
}
