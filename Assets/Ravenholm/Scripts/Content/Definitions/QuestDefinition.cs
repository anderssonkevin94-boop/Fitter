using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    [Serializable]
    public struct QuestThreat
    {
        public ThreatDefinition threat;
        [Tooltip("Known before any visit. Unknown threats show as 'Threat not yet identified' (GDD 4).")]
        public bool knownAtStart;
    }

    /// <summary>A preparation leg's permanent effect: a threat it removes from a later quest (GDD 14 final chain).</summary>
    [Serializable]
    public struct QuestPreparation
    {
        public QuestDefinition quest;
        public ThreatDefinition threat;
        [Tooltip("Shown on the later quest once prepared, e.g. 'Route charted by the scouts'.")]
        public string note;
    }

    /// <summary>
    /// A postable job (GDD 11/17). The player posts the quest; never a reward or payment amount.
    /// Phase 4 uses difficulty, threats and duration; outcome rewards and unlocks arrive in Phase 5.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Quest Definition", fileName = "quest_")]
    public sealed class QuestDefinition : ContentDefinition
    {
        [Min(0)] [SerializeField] private int difficulty = 14;
        [Min(1)] [SerializeField] private float durationSeconds = 90f;
        [SerializeField] private List<QuestThreat> threats = new List<QuestThreat>();
        [Min(0)] [SerializeField] private int baseGold = 28;
        [SerializeField] private bool repeatable = true;
        [Tooltip("Protected recovery contract (GDD 15): sponsor pays the fee; authored safe result.")]
        [SerializeField] private bool recoveryContract;
        [Tooltip("Reputation the job is pitched at (diminishing reputation gain above it, GDD 14).")]
        [Min(0)] [SerializeField] private int prestigeTarget = 20;
        [TextArea(1, 3)] [SerializeField] private string brief = "";

        [Header("Map (GDD 14)")]
        [Tooltip("Quests that must have succeeded once before this one can be posted. Must stay acyclic.")]
        [SerializeField] private List<QuestDefinition> prerequisites = new List<QuestDefinition>();
        [Tooltip("Reputation needed the first time; once reached the quest stays unlocked.")]
        [Min(0)] [SerializeField] private int reputationGate;
        [Tooltip("Row on the quest map (0 = starting jobs).")]
        [Min(0)] [SerializeField] private int mapTier;

        [Tooltip("Map region (GDD 14). Regions show as silhouettes until one of their jobs unlocks.")]
        [SerializeField] private RegionDefinition region;

        [Header("Campaign (GDD 14)")]
        [Tooltip("Order on the campaign breadcrumb (0 = not part of the main chain).")]
        [Min(0)] [SerializeField] private int campaignStep;
        [Tooltip("Succeeding at this completes the campaign (settlement trophy). Exactly one quest.")]
        [SerializeField] private bool campaignFinale;
        [Tooltip("On first success, removes these threats from later quests (permanent expedition preparations).")]
        [SerializeField] private List<QuestPreparation> preparations = new List<QuestPreparation>();

        [Header("Dispatch requirements (checked when sending, GDD 14 prestigious exception)")]
        [Min(0)] [SerializeField] private int minContractorLevel;
        [Tooltip("Current reputation needed at dispatch (unlike the one-time reputation gate).")]
        [Min(0)] [SerializeField] private int dispatchReputation;
        [Min(0)] [SerializeField] private int minWeaponPower;
        [Min(0)] [SerializeField] private int minArmorProtection;
        [Tooltip("Requires a potion that counters one of this job's threats.")]
        [SerializeField] private bool requireCounterPotion;

        [Header("Rewards per outcome (GDD 13/17) - explicit packages, not multiplied")]
        [SerializeField] private List<ResourceCost> successMaterials = new List<ResourceCost>();
        [Tooltip("Great Success adds this small bundle on top of the success package.")]
        [SerializeField] private List<ResourceCost> greatBonusMaterials = new List<ResourceCost>();
        [SerializeField] private List<ResourceCost> partialMaterials = new List<ResourceCost>();
        [Tooltip("Milestone blueprint on the first Success or Great Success (never on partial).")]
        [SerializeField] private BlueprintDefinition firstSuccessBlueprint;
        [Tooltip("Granted instead of a blueprint the company already owns.")]
        [SerializeField] private List<ResourceCost> substituteMaterials = new List<ResourceCost>();
        [Tooltip("Rare roll (Great Success, and the Rogue's bonus roll after a success).")]
        [SerializeField] private ResourceDefinition rareResource;
        [Range(0f, 1f)] [SerializeField] private float rareChance = 0.5f;

        public override string IdKind => "quest";
        public int Difficulty => difficulty;
        public float DurationSeconds => durationSeconds;
        public IReadOnlyList<QuestThreat> Threats => threats;
        public int BaseGold => baseGold;
        public bool Repeatable => repeatable;
        public bool RecoveryContract => recoveryContract;
        public int PrestigeTarget => prestigeTarget;
        public string Brief => brief;
        public IReadOnlyList<QuestDefinition> Prerequisites => prerequisites;
        public int ReputationGate => reputationGate;
        public int MapTier => mapTier;
        public RegionDefinition Region => region;
        public int CampaignStep => campaignStep;
        public bool CampaignFinale => campaignFinale;
        public IReadOnlyList<QuestPreparation> Preparations => preparations;
        public int MinContractorLevel => minContractorLevel;
        public int DispatchReputation => dispatchReputation;
        public int MinWeaponPower => minWeaponPower;
        public int MinArmorProtection => minArmorProtection;
        public bool RequireCounterPotion => requireCounterPotion;
        public IReadOnlyList<ResourceCost> SuccessMaterials => successMaterials;
        public IReadOnlyList<ResourceCost> GreatBonusMaterials => greatBonusMaterials;
        public IReadOnlyList<ResourceCost> PartialMaterials => partialMaterials;
        public BlueprintDefinition FirstSuccessBlueprint => firstSuccessBlueprint;
        public IReadOnlyList<ResourceCost> SubstituteMaterials => substituteMaterials;
        public ResourceDefinition RareResource => rareResource;
        public float RareChance => rareChance;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            foreach (var t in threats) if (t.threat == null) errors.Add($"{Describe()}: threat entry missing");
            if (recoveryContract && threats.Count > 0) errors.Add($"{Describe()}: the recovery contract must have no threats");
            if (recoveryContract && (prerequisites.Count > 0 || reputationGate > 0)) errors.Add($"{Describe()}: the recovery contract must always be available");
            if (recoveryContract && firstSuccessBlueprint != null) errors.Add($"{Describe()}: the recovery contract awards no blueprint (GDD 15)");
            foreach (var p in prerequisites) if (p == null) errors.Add($"{Describe()}: missing prerequisite");
            if (firstSuccessBlueprint != null && substituteMaterials.Count == 0) errors.Add($"{Describe()}: blueprint reward needs substitute materials");
            if (rareChance > 0 && rareResource == null && !recoveryContract) errors.Add($"{Describe()}: rare chance without a rare resource");
            if (region == null) errors.Add($"{Describe()}: needs a map region");
            if (campaignFinale && repeatable) errors.Add($"{Describe()}: the campaign finale is a one-time expedition");
            foreach (var p in preparations)
            {
                if (p.quest == null || p.threat == null) { errors.Add($"{Describe()}: preparation entry incomplete"); continue; }
                if (p.quest == this) errors.Add($"{Describe()}: prepares itself");
                var present = false;
                foreach (var t in p.quest.Threats) if (t.threat == p.threat) present = true;
                if (!present) errors.Add($"{Describe()}: prepares {p.threat.Id}, which {p.quest.Id} does not have");
            }
        }

#if UNITY_EDITOR
        public void EditorSetupProgression(List<QuestDefinition> prereqs, int gate, int tier, List<ResourceCost> success, List<ResourceCost> great,
            List<ResourceCost> partial, BlueprintDefinition blueprint, List<ResourceCost> substitute, ResourceDefinition rare, float chance)
        {
            prerequisites = prereqs; reputationGate = gate; mapTier = tier; successMaterials = success; greatBonusMaterials = great;
            partialMaterials = partial; firstSuccessBlueprint = blueprint; substituteMaterials = substitute; rareResource = rare; rareChance = chance;
        }

        public void EditorSetRegion(RegionDefinition r) => region = r;
        public void EditorSetDifficulty(int d) => difficulty = d;

        public void EditorSetupCampaign(int step, bool finale, List<QuestPreparation> preps, int minLevel, int dispatchRep, int minPower, int minProtection,
            bool counterPotion, bool repeat)
        {
            campaignStep = step; campaignFinale = finale; preparations = preps; minContractorLevel = minLevel; dispatchReputation = dispatchRep;
            minWeaponPower = minPower; minArmorProtection = minProtection; requireCounterPotion = counterPotion; repeatable = repeat;
        }

        public void EditorSetupQuest(int d, float seconds, List<QuestThreat> t, int gold, bool repeat, bool recovery, int prestige, string text)
        {
            difficulty = d; durationSeconds = seconds; threats = t; baseGold = gold; repeatable = repeat; recoveryContract = recovery;
            prestigeTarget = prestige; brief = text;
        }
#endif
    }
}
