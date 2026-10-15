using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>Authored starting contents for a world container (Phase 1 test fixture).</summary>
    public sealed class ContainerFixture
    {
        public readonly List<KeyValuePair<string, int>> Materials = new List<KeyValuePair<string, int>>();
        public readonly List<string> ItemDefinitions = new List<string>();
    }

    /// <summary>Maps authored content to receiver acceptance. No name-based branches.</summary>
    public sealed class CatalogueAcceptanceRules : IAcceptanceRules
    {
        private readonly ContentCatalogue _catalogue;

        public CatalogueAcceptanceRules(ContentCatalogue catalogue) => _catalogue = catalogue;

        public bool Accepts(Domain.Logistics.ReceiverKind receiver, CarryUnit unit, string itemDefinitionId)
        {
            if (_catalogue == null) return false;
            if (unit.Kind == UnitKind.Item)
                return receiver == Domain.Logistics.ReceiverKind.Store && itemDefinitionId != null && _catalogue.TryGet<ItemDefinition>(itemDefinitionId, out _);
            return _catalogue.TryGet<ResourceDefinition>(unit.Id, out var res) && Map(res.AcceptedReceiver) == receiver;
        }

        public static Domain.Logistics.ReceiverKind? Map(ReceiverType t)
        {
            switch (t)
            {
                case ReceiverType.Smelter: return Domain.Logistics.ReceiverKind.Smelter;
                case ReceiverType.Lumbermill: return Domain.Logistics.ReceiverKind.Lumbermill;
                case ReceiverType.Apothecary: return Domain.Logistics.ReceiverKind.Apothecary;
                case ReceiverType.Store: return Domain.Logistics.ReceiverKind.Store;
                case ReceiverType.Kitchen: return Domain.Logistics.ReceiverKind.Kitchen;
                default: return null;
            }
        }
    }

    /// <summary>
    /// Application wrapper around the logistics domain: scene registration, fixture seeding,
    /// persistence of the "logistics" save section and debug commands.
    /// </summary>
    public sealed class LogisticsService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly LogisticsState _state = new LogisticsState();
        private readonly Dictionary<string, (ContainerKind kind, float x, float y, float r, ContainerFixture fixture)> _containerViews =
            new Dictionary<string, (ContainerKind, float, float, float, ContainerFixture)>(StringComparer.Ordinal);

        public LogisticsService(SaveScheduler scheduler, IAcceptanceRules rules, LogisticsTuning tuning)
        {
            _scheduler = scheduler;
            System = new LogisticsSystem(_state, rules, tuning);
            System.Changed += scheduler.MarkDirty;
            scheduler.Register(this);
        }

        public LogisticsSystem System { get; }

        /// <summary>Raised after a load or reset replaced the whole state (views should rebuild).</summary>
        public event Action Reloaded;

        /// <summary>
        /// Scene views call this on load. A container missing from the save is created and seeded
        /// with its fixture; an existing one keeps its saved contents (no duplication on restart).
        /// </summary>
        public void RegisterContainer(string id, ContainerKind kind, float x, float y, float radius, ContainerFixture fixture)
        {
            _containerViews[id] = (kind, x, y, radius, fixture);
            if (System.RegisterContainer(id, kind, x, y, radius)) Seed(id, fixture);
        }

        /// <summary>A registered container's scene position changed (its station was moved); its contents stay.</summary>
        public void MoveContainer(string id, float x, float y)
        {
            if (!_containerViews.TryGetValue(id, out var v)) return;
            _containerViews[id] = (v.kind, x, y, v.r, v.fixture);
            System.RegisterContainer(id, v.kind, x, y, v.r);
        }

        public void RegisterReceiver(string id, Domain.Logistics.ReceiverKind kind, float x, float y, float radius) =>
            System.RegisterReceiver(id, kind, x, y, radius);

        /// <summary>DEBUG: top every registered container back up to its fixture contents.</summary>
        public void RefillFixtures()
        {
            _scheduler.NoteAssisted("refill fixtures");
            foreach (var kv in _containerViews)
            {
                var fixture = kv.Value.fixture;
                if (fixture == null) continue;
                var c = _state.Containers[kv.Key];
                foreach (var m in fixture.Materials)
                {
                    c.Materials.TryGetValue(m.Key, out var have);
                    if (have < m.Value) System.CreateMaterialsInContainer(kv.Key, m.Key, m.Value - have);
                }
                var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var def in fixture.ItemDefinitions) wanted[def] = wanted.TryGetValue(def, out var n) ? n + 1 : 1;
                foreach (var item in c.Items)
                    if (_state.ItemDefinitions.TryGetValue(item, out var def) && wanted.ContainsKey(def)) wanted[def]--;
                foreach (var w in wanted)
                    for (var i = 0; i < w.Value; i++) System.CreateItemInContainer(kv.Key, w.Key);
            }
            _scheduler.SaveNow("debug: refill fixtures");
        }

        public int DebugUnloadCarry()
        {
            _scheduler.NoteAssisted("unload carry");
            var n = System.DebugUnloadCarry();
            _scheduler.SaveNow("debug: unload carry");
            return n;
        }

        private void Seed(string id, ContainerFixture fixture)
        {
            if (fixture == null) return;
            foreach (var m in fixture.Materials) System.CreateMaterialsInContainer(id, m.Key, m.Value);
            foreach (var def in fixture.ItemDefinitions) System.CreateItemInContainer(id, def);
        }

        /// <summary>Pins or unpins an item type on the Store racks (GDD 4: newest, strongest and player-pinned items show).</summary>
        public bool TogglePin(string itemDefinitionId)
        {
            var pinned = _state.PinnedDefinitions.Add(itemDefinitionId);
            if (!pinned) _state.PinnedDefinitions.Remove(itemDefinitionId);
            _scheduler.MarkDirty();
            return pinned;
        }

        // ------------------------------------------------------------------ persistence

        void ISaveSection.Write(SaveEnvelope env)
        {
            var d = new LogisticsSaveData { NextInstanceNumber = _state.NextInstanceNumber };
            foreach (var u in _state.Carry) d.Carry.Add(new CarryUnitData { Kind = u.Kind == UnitKind.Item ? "i" : "m", Id = u.Id });
            foreach (var c in _state.Containers.Values)
            {
                var cd = new ContainerData { Id = c.Id, Kind = c.Kind.ToString() };
                foreach (var kv in c.Materials) cd.Materials[kv.Key] = kv.Value;
                cd.Items.AddRange(c.Items);
                d.Containers.Add(cd);
            }
            foreach (var kv in _state.SharedMaterials) d.SharedMaterials[kv.Key] = kv.Value;
            d.StoreItems.AddRange(_state.StoreItems);
            foreach (var kv in _state.ItemDefinitions) d.ItemDefinitions[kv.Key] = kv.Value;
            foreach (var kv in _state.MaterialsCreated) d.MaterialsCreated[kv.Key] = kv.Value;
            foreach (var kv in _state.MaterialsConsumed) d.MaterialsConsumed[kv.Key] = kv.Value;
            d.Pinned.AddRange(_state.PinnedDefinitions);
            env.Logistics = d;
        }

        void ISaveSection.Read(SaveEnvelope env)
        {
            System.ClearAllReservations();
            _state.Carry.Clear();
            _state.Containers.Clear();
            _state.SharedMaterials.Clear();
            _state.StoreItems.Clear();
            _state.ItemDefinitions.Clear();
            _state.MaterialsCreated.Clear();
            _state.MaterialsConsumed.Clear();
            _state.NextInstanceNumber = 1;
            _state.PinnedDefinitions.Clear();
            // Receivers are scene geometry, not saved state; they stay registered.

            var d = env?.Logistics;
            if (d != null)
            {
                _state.NextInstanceNumber = Math.Max(1, d.NextInstanceNumber);
                if (d.Pinned != null) foreach (var p in d.Pinned) _state.PinnedDefinitions.Add(p);
                foreach (var kv in d.ItemDefinitions) _state.ItemDefinitions[kv.Key] = kv.Value;
                foreach (var kv in d.MaterialsCreated) _state.MaterialsCreated[kv.Key] = kv.Value;
                if (d.MaterialsConsumed != null) foreach (var kv in d.MaterialsConsumed) _state.MaterialsConsumed[kv.Key] = kv.Value;
                foreach (var kv in d.SharedMaterials) _state.SharedMaterials[kv.Key] = kv.Value;
                _state.StoreItems.AddRange(d.StoreItems);
                _state.StoreItems.Sort(StringComparer.Ordinal);
                foreach (var u in d.Carry)
                    _state.Carry.Add(u.Kind == "i" ? CarryUnit.Item(u.Id) : CarryUnit.Material(u.Id));
                foreach (var cd in d.Containers)
                {
                    Enum.TryParse(cd.Kind, out ContainerKind kind);
                    // A saved container the current scene does not place keeps its contents but is unreachable (negative reach).
                    var view = _containerViews.TryGetValue(cd.Id, out var v) ? v : (kind, 0f, 0f, -1000f, null);
                    System.RegisterContainer(cd.Id, kind, view.x, view.y, view.r);
                    var c = _state.Containers[cd.Id];
                    foreach (var kv in cd.Materials) c.Materials[kv.Key] = kv.Value;
                    c.Items.AddRange(cd.Items);
                    c.Items.Sort(StringComparer.Ordinal);
                }
            }

            // Containers registered by the current scene but absent from the save start from their fixtures.
            foreach (var kv in _containerViews)
                if (System.RegisterContainer(kv.Key, kv.Value.kind, kv.Value.x, kv.Value.y, kv.Value.r)) Seed(kv.Key, kv.Value.fixture);

            Reloaded?.Invoke();
        }
    }
}
