using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// A crafting recipe for one finished item (GDD 9: no minigames or quality rolls). Timed since the crafting expansion
    /// (Kevin, 2026-10-16, Shop Titans style): it takes <see cref="CraftSeconds"/> in one of the station's craft slots.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Recipe Definition", fileName = "recipe_")]
    public sealed class RecipeDefinition : ContentDefinition
    {
        [SerializeField] private ItemDefinition outputItem;
        [Min(1)] [SerializeField] private int quantity = 1;
        [SerializeField] private List<ResourceCost> cost = new List<ResourceCost>();
        [SerializeField] private StationDefinition station;
        [Min(1)] [SerializeField] private int minFacilityLevel = 1;
        [SerializeField] private BlueprintDefinition requiredBlueprint;
        [Tooltip("How long one takes, in seconds, at the station level the recipe needs (higher levels are quicker).")]
        [Min(1f)] [SerializeField] private float craftSeconds = 300f;

        public override string IdKind => "recipe";
        public ItemDefinition OutputItem => outputItem;
        public int Quantity => quantity;
        public IReadOnlyList<ResourceCost> Cost => cost;
        public StationDefinition Station => station;
        public int MinFacilityLevel => minFacilityLevel;
        public BlueprintDefinition RequiredBlueprint => requiredBlueprint;
        public float CraftSeconds => craftSeconds;

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

        public void EditorSetCraftSeconds(float seconds) => craftSeconds = seconds;
#endif
    }
}
