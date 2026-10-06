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

        public override string IdKind => "quest";
        public int Difficulty => difficulty;
        public float DurationSeconds => durationSeconds;
        public IReadOnlyList<QuestThreat> Threats => threats;
        public int BaseGold => baseGold;
        public bool Repeatable => repeatable;
        public bool RecoveryContract => recoveryContract;
        public int PrestigeTarget => prestigeTarget;
        public string Brief => brief;

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            foreach (var t in threats) if (t.threat == null) errors.Add($"{Describe()}: threat entry missing");
            if (recoveryContract && threats.Count > 0) errors.Add($"{Describe()}: the recovery contract must have no threats");
        }

#if UNITY_EDITOR
        public void EditorSetupQuest(int d, float seconds, List<QuestThreat> t, int gold, bool repeat, bool recovery, int prestige, string text)
        {
            difficulty = d; durationSeconds = seconds; threats = t; baseGold = gold; repeatable = repeat; recoveryContract = recovery;
            prestigeTarget = prestige; brief = text;
        }
#endif
    }
}
