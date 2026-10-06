using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    public enum EquipmentSlot
    {
        Tool,
        Pack,
    }

    [Serializable]
    public struct ResourceCost
    {
        public ResourceDefinition resource;
        [Min(1)] public int quantity;
    }

    /// <summary>
    /// Worker tool or pack tier (GDD 5). A facility purchase applied to a worker, not crafted goods.
    /// Phase 2 equips these through DEBUG buttons; costs and gates are recorded for Phase 6.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Worker Equipment Definition", fileName = "equipment_")]
    public sealed class WorkerEquipmentDefinition : ContentDefinition
    {
        [SerializeField] private EquipmentSlot slot;
        [Min(1)] [SerializeField] private int tier = 1;
        [Tooltip("Tool: damage per second. Pack: capacity in units.")]
        [Min(1)] [SerializeField] private int value = 10;
        [Min(0)] [SerializeField] private int goldCost;
        [SerializeField] private List<ResourceCost> materialCost = new List<ResourceCost>();
        [Tooltip("Readable unlock gate, e.g. 'Site equipment level 2'. Enforced from Phase 6.")]
        [SerializeField] private string gate = "";

        public override string IdKind => "equipment";
        public EquipmentSlot Slot => slot;
        public int Tier => tier;
        public int Value => value;
        public int GoldCost => goldCost;
        public IReadOnlyList<ResourceCost> MaterialCost => materialCost;
        public string Gate => gate;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            foreach (var c in materialCost)
                if (c.resource == null) errors.Add($"{Describe()}: material cost with missing resource");
        }

#if UNITY_EDITOR
        public void EditorSetupEquipment(EquipmentSlot newSlot, int newTier, int newValue, int gold, List<ResourceCost> cost, string newGate)
        {
            slot = newSlot;
            tier = newTier;
            value = newValue;
            goldCost = gold;
            materialCost = cost;
            gate = newGate;
        }
#endif
    }
}
