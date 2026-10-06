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
        }

#if UNITY_EDITOR
        public void EditorSetupProgression(List<QuestDefinition> prereqs, int gate, int tier, List<ResourceCost> success, List<ResourceCost> great,
            List<ResourceCost> partial, BlueprintDefinition blueprint, List<ResourceCost> substitute, ResourceDefinition rare, float chance)
        {
            prerequisites = prereqs; reputationGate = gate; mapTier = tier; successMaterials = success; greatBonusMaterials = great;
            partialMaterials = partial; firstSuccessBlueprint = blueprint; substituteMaterials = substitute; rareResource = rare; rareChance = chance;
        }

        public void EditorSetupQuest(int d, float seconds, List<QuestThreat> t, int gold, bool repeat, bool recovery, int prestige, string text)
        {
            difficulty = d; durationSeconds = seconds; threats = t; baseGold = gold; repeatable = repeat; recoveryContract = recovery;
            prestigeTarget = prestige; brief = text;
        }
#endif
    }
}
