using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// Quest threat (GDD 12/17). Matching counters add; once the total reaches the required strength
    /// the threat is "countered" and its unmet penalties are removed. Below it, counters only "help".
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Threat Definition", fileName = "threat_")]
    public sealed class ThreatDefinition : ContentDefinition
    {
        [SerializeField] private CounterTag counterTag;
        [Min(0)] [SerializeField] private int requiredCounterStrength = 2;
        [Tooltip("Readiness penalty while unmet (GDD default −4).")]
        [SerializeField] private int unmetReadinessPenalty = -4;
        [Tooltip("Survival-context penalty while unmet (poison −3).")]
        [SerializeField] private int unmetSurvivalPenalty;
        [Tooltip("Heavy blows: met by armor Protection at least this (0 = not used).")]
        [Min(0)] [SerializeField] private int armorThreshold;
        [Tooltip("Bonus to readiness and survival when met by armor (heavy blows +2 / +2).")]
        [SerializeField] private int armorReadinessBonus;
        [SerializeField] private int armorSurvivalBonus;
        [TextArea(1, 3)] [SerializeField] private string revealText = "";

        public override string IdKind => "threat";
        public CounterTag CounterTag => counterTag;
        public int RequiredCounterStrength => requiredCounterStrength;
        public int UnmetReadinessPenalty => unmetReadinessPenalty;
        public int UnmetSurvivalPenalty => unmetSurvivalPenalty;
        public int ArmorThreshold => armorThreshold;
        public int ArmorReadinessBonus => armorReadinessBonus;
        public int ArmorSurvivalBonus => armorSurvivalBonus;
        public string RevealText => revealText;

#if UNITY_EDITOR
        public void EditorSetupThreat(CounterTag tag, int required, int unmet, int unmetSurvival, int armor, int armorReady, int armorSurv, string reveal)
        {
            counterTag = tag; requiredCounterStrength = required; unmetReadinessPenalty = unmet; unmetSurvivalPenalty = unmetSurvival;
            armorThreshold = armor; armorReadinessBonus = armorReady; armorSurvivalBonus = armorSurv; revealText = reveal;
        }
#endif
    }
}
