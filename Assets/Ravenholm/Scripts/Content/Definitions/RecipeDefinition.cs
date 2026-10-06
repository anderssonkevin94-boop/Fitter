using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>Instant crafting recipe for one finished item (GDD 9: no timers, minigames or quality rolls).</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Recipe Definition", fileName = "recipe_")]
    public sealed class RecipeDefinition : ContentDefinition
    {
        [SerializeField] private ItemDefinition outputItem;
        [Min(1)] [SerializeField] private int quantity = 1;
        [SerializeField] private List<ResourceCost> cost = new List<ResourceCost>();
        [SerializeField] private StationDefinition station;
        [Min(1)] [SerializeField] private int minFacilityLevel = 1;
        [SerializeField] private BlueprintDefinition requiredBlueprint;

        public override string IdKind => "recipe";
        public ItemDefinition OutputItem => outputItem;
        public int Quantity => quantity;
        public IReadOnlyList<ResourceCost> Cost => cost;
        public StationDefinition Station => station;
        public int MinFacilityLevel => minFacilityLevel;
        public BlueprintDefinition RequiredBlueprint => requiredBlueprint;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (outputItem == null) errors.Add($"{Describe()}: output item missing");
            if (station == null) errors.Add($"{Describe()}: station missing");
            else if (string.IsNullOrEmpty(station.OutputPadContainerId)) errors.Add($"{Describe()}: station '{station.Id}' has no output pad");
            if (requiredBlueprint == null) errors.Add($"{Describe()}: blueprint missing");
            if (cost.Count == 0) errors.Add($"{Describe()}: needs a cost");
            foreach (var c in cost) if (c.resource == null || c.quantity <= 0) errors.Add($"{Describe()}: invalid cost entry");
        }

#if UNITY_EDITOR
        public void EditorSetupRecipe(ItemDefinition item, List<ResourceCost> costs, StationDefinition s, int level, BlueprintDefinition bp)
        {
            outputItem = item;
            cost = costs;
            station = s;
            minFacilityLevel = level;
            requiredBlueprint = bp;
        }
#endif
    }
}
