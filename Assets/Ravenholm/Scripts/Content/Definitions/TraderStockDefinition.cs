using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    public enum StockAvailability
    {
        /// <summary>Every visit (GDD 16: emergency bundle).</summary>
        Guaranteed,
        /// <summary>Appears on a visit with the given chance.</summary>
        Chance,
        /// <summary>Every visit once the given quest has succeeded (e.g. arcane dust after the Shrine).</summary>
        AfterQuest,
    }

    [Serializable]
    public struct TraderStockEntry
    {
        [Tooltip("Stable key for saved remaining quantities.")]
        public string key;
        public ResourceDefinition resource;
        public ItemDefinition item;
        [Tooltip("Sells an unowned blueprint from the side pool instead of goods.")]
        public bool sideBlueprint;
        [Min(1)] public int price;
        [Min(1)] public int quantity;
        public StockAvailability availability;
        [Range(0f, 1f)] public float chance;
        public QuestDefinition afterQuest;
    }

    /// <summary>
    /// The periodic Trader's stock table (GDD 16). Buy prices are set independently from crafting
    /// costs so buy-and-resell can never profit; the Trader pays each item's TraderBuyValue.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Trader Stock Definition", fileName = "trader_stock")]
    public sealed class TraderStockDefinition : ContentDefinition
    {
        [SerializeField] private List<TraderStockEntry> entries = new List<TraderStockEntry>();
        [Tooltip("Blueprints the Trader may offer (only ones the company does not own).")]
        [SerializeField] private List<BlueprintDefinition> sideBlueprints = new List<BlueprintDefinition>();
        [Min(1)] [SerializeField] private float minutesBetweenVisits = 20f;
        [Min(1)] [SerializeField] private float minutesPerVisit = 5f;
        [Tooltip("Delay after the first ordinary quest return before the first visit.")]
        [Min(0)] [SerializeField] private float firstArrivalDelaySeconds = 60f;

        public override string IdKind => "trader";
        public IReadOnlyList<TraderStockEntry> Entries => entries;
        public IReadOnlyList<BlueprintDefinition> SideBlueprints => sideBlueprints;
        public float MinutesBetweenVisits => minutesBetweenVisits;
        public float MinutesPerVisit => minutesPerVisit;
        public float FirstArrivalDelaySeconds => firstArrivalDelaySeconds;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            var keys = new HashSet<string>();
            var guaranteed = false;
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.key) || !keys.Add(e.key)) errors.Add($"{Describe()}: entry key '{e.key}' empty or duplicate");
                var kinds = (e.resource != null ? 1 : 0) + (e.item != null ? 1 : 0) + (e.sideBlueprint ? 1 : 0);
                if (kinds != 1) errors.Add($"{Describe()}: entry '{e.key}' must sell exactly one thing");
                if (e.availability == StockAvailability.AfterQuest && e.afterQuest == null) errors.Add($"{Describe()}: entry '{e.key}' needs a quest");
                if (e.availability == StockAvailability.Guaranteed) guaranteed = true;
                // Never allow buy-and-resell profit on identical goods (GDD 15).
                if (e.item != null && e.item.TraderBuyValue >= e.price) errors.Add($"{Describe()}: '{e.key}' could be resold at a profit");
            }
            if (!guaranteed) errors.Add($"{Describe()}: needs a guaranteed emergency entry (recovery must not wait on a rare roll)");
            if (minutesPerVisit >= minutesBetweenVisits) errors.Add($"{Describe()}: visit must be shorter than the gap");
        }

#if UNITY_EDITOR
        public void EditorSetupTrader(List<TraderStockEntry> e, List<BlueprintDefinition> side, float gap, float stay, float delay)
        {
            entries = e; sideBlueprints = side; minutesBetweenVisits = gap; minutesPerVisit = stay; firstArrivalDelaySeconds = delay;
        }
#endif
    }
}
