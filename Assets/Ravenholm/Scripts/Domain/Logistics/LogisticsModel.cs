using System;
using System.Collections.Generic;

namespace Ravenholm.Domain.Logistics
{
    public enum UnitKind
    {
        /// <summary>One unit of a fungible resource; <see cref="CarryUnit.Id"/> is the resource ID.</summary>
        Material,
        /// <summary>One unique item instance; <see cref="CarryUnit.Id"/> is the instance ID.</summary>
        Item,
    }

    /// <summary>One carry slot. Raw materials and finished items each occupy exactly one slot (GDD 3).</summary>
    public readonly struct CarryUnit : IEquatable<CarryUnit>
    {
        public readonly UnitKind Kind;
        public readonly string Id;

        public CarryUnit(UnitKind kind, string id)
        {
            Kind = kind;
            Id = id;
        }

        public static CarryUnit Material(string resourceId) => new CarryUnit(UnitKind.Material, resourceId);
        public static CarryUnit Item(string instanceId) => new CarryUnit(UnitKind.Item, instanceId);

        public bool Equals(CarryUnit other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object obj) => obj is CarryUnit u && Equals(u);
        public override int GetHashCode() => ((int)Kind * 397) ^ (Id?.GetHashCode() ?? 0);
        public override string ToString() => $"{Kind}:{Id}";
    }

    public enum ContainerKind
    {
        /// <summary>Incidental raw pile, e.g. site staging.</summary>
        RawPile,
        /// <summary>Station output pad holding finished goods; wins pickup ties (GDD 3).</summary>
        OutputPad,
    }

    public enum ReceiverKind
    {
        Smelter,
        Lumbermill,
        Apothecary,
        Store,
    }

    /// <summary>World container that owns units until the player picks them up. Geometry is on the 2D simulation plane.</summary>
    public sealed class WorldContainer
    {
        public WorldContainer(string id, ContainerKind kind, float x, float y, float radius)
        {
            Id = id;
            Kind = kind;
            X = x;
            Y = y;
            Radius = radius;
        }

        public string Id { get; }
        public ContainerKind Kind { get; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
        public float Radius { get; internal set; }

        /// <summary>Resource ID → quantity. Sorted for deterministic pickup order.</summary>
        public SortedDictionary<string, int> Materials { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Item instance IDs, kept sorted (stable ID tie-break).</summary>
        public List<string> Items { get; } = new List<string>();

        public int UnitCount
        {
            get
            {
                var n = Items.Count;
                foreach (var q in Materials.Values) n += q;
                return n;
            }
        }
    }

    public sealed class ReceiverZone
    {
        public ReceiverZone(string id, ReceiverKind kind, float x, float y, float radius)
        {
            Id = id;
            Kind = kind;
            X = x;
            Y = y;
            Radius = radius;
        }

        public string Id { get; }
        public ReceiverKind Kind { get; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
        public float Radius { get; internal set; }

        public bool Contains(float px, float py)
        {
            float dx = px - X, dy = py - Y;
            return dx * dx + dy * dy <= Radius * Radius;
        }
    }

    /// <summary>Which receiver accepts which unit. Implemented from authored content outside the domain.</summary>
    public interface IAcceptanceRules
    {
        bool Accepts(ReceiverKind receiver, CarryUnit unit, string itemDefinitionId);
    }

    public sealed class LogisticsTuning
    {
        /// <summary>Carry capacity in units (GDD default 12).</summary>
        public int Capacity = 12;
        /// <summary>Extra reach beyond a container's own radius (GDD default 0.8 units).</summary>
        public float PickupRadius = 0.8f;
        /// <summary>Seconds per transferred unit (GDD default 0.08).</summary>
        public float TransferInterval = 0.08f;
        /// <summary>Distance bonus that makes output pads win near-ties against raw piles.</summary>
        public float OutputPadPriority = 0.05f;
    }

    /// <summary>
    /// Authoritative ownership state for physical goods. Every material unit and item instance
    /// has exactly one owner: a world container, the carry, the shared ledger, or the Store.
    /// </summary>
    public sealed class LogisticsState
    {
        public readonly List<CarryUnit> Carry = new List<CarryUnit>();
        public readonly Dictionary<string, WorldContainer> Containers = new Dictionary<string, WorldContainer>(StringComparer.Ordinal);
        public readonly Dictionary<string, ReceiverZone> Receivers = new Dictionary<string, ReceiverZone>(StringComparer.Ordinal);

        /// <summary>Shared usable inventory reached through receivers (resource ID → quantity).</summary>
        public readonly SortedDictionary<string, int> SharedMaterials = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Item instances owned by the Store.</summary>
        public readonly List<string> StoreItems = new List<string>();

        /// <summary>Instance ID → item definition ID for every instance that exists.</summary>
        public readonly SortedDictionary<string, string> ItemDefinitions = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Total material units ever created per resource (approved sources), used by the conservation audit.</summary>
        public readonly SortedDictionary<string, long> MaterialsCreated = new SortedDictionary<string, long>(StringComparer.Ordinal);

        public long NextInstanceNumber = 1;
    }
}
