using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    public enum UpgradeKind
    {
        /// <summary>Raises a station to <see cref="UpgradeDefinition.Level"/> (recipes, processing speed).</summary>
        StationLevel,
        /// <summary>Raises a site's equipment level, which gates tool and pack purchases.</summary>
        SiteEquipment,
        /// <summary>Adds the site's next named worker (GDD 5: deterministic recruits, no hiring economy).</summary>
        CrewExpansion,
    }

    /// <summary>
    /// A building or site upgrade bought at the physical building (GDD 16): exact effect and cost,
    /// applied instantly; gold and materials are debited atomically. Never improves existing gear.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Upgrade Definition", fileName = "upgrade_")]
    public sealed class UpgradeDefinition : ContentDefinition
    {
        [SerializeField] private UpgradeKind kind;
        [SerializeField] private StationDefinition station;
        [SerializeField] private SiteDefinition site;
        [Min(1)] [SerializeField] private int level = 2;
        [Min(0)] [SerializeField] private int goldCost;
        [SerializeField] private List<ResourceCost> materialCost = new List<ResourceCost>();
        [TextArea(1, 2)] [SerializeField] private string effect = "";

        [Header("Prerequisites")]
        [SerializeField] private UpgradeDefinition requiresUpgrade;
        [Tooltip("Requires this site to have revealed at least band N.")]
        [SerializeField] private SiteDefinition requiresSiteBand;
        [Min(0)] [SerializeField] private int requiresBand;
        [SerializeField] private QuestDefinition requiresQuestSuccess;
        [Tooltip("Ordinary (non-recovery) quest successes needed.")]
        [Min(0)] [SerializeField] private int requiresOrdinarySuccesses;

        public override string IdKind => "upgrade";
        public UpgradeKind Kind => kind;
        public StationDefinition Station => station;
        public SiteDefinition Site => site;
        public int Level => level;
        public int GoldCost => goldCost;
        public IReadOnlyList<ResourceCost> MaterialCost => materialCost;
        public string Effect => effect;
        public UpgradeDefinition RequiresUpgrade => requiresUpgrade;
        public SiteDefinition RequiresSiteBand => requiresSiteBand;
        public int RequiresBand => requiresBand;
        public QuestDefinition RequiresQuestSuccess => requiresQuestSuccess;
        public int RequiresOrdinarySuccesses => requiresOrdinarySuccesses;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (kind == UpgradeKind.StationLevel && station == null) errors.Add($"{Describe()}: station missing");
            if (kind != UpgradeKind.StationLevel && site == null) errors.Add($"{Describe()}: site missing");
            foreach (var c in materialCost) if (c.resource == null || c.quantity <= 0) errors.Add($"{Describe()}: invalid material cost");
            if (requiresUpgrade == this) errors.Add($"{Describe()}: requires itself");
        }

#if UNITY_EDITOR
        public void EditorSetupUpgrade(UpgradeKind k, StationDefinition st, SiteDefinition si, int lvl, int gold, List<ResourceCost> mats, string fx,
            UpgradeDefinition reqUp, SiteDefinition reqSite, int reqBand, QuestDefinition reqQuest, int reqSuccesses)
        {
            kind = k; station = st; site = si; level = lvl; goldCost = gold; materialCost = mats; effect = fx;
            requiresUpgrade = reqUp; requiresSiteBand = reqSite; requiresBand = reqBand; requiresQuestSuccess = reqQuest; requiresOrdinarySuccesses = reqSuccesses;
        }
#endif
    }
}
